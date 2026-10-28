import ARKit
import AVFoundation
import Foundation
import Network
import UIKit
import Vision

// Cuerpo con la cámara del iPhone o iPad, en cuatro modos:
//  1 frontal · 2 trasera gran angular · 3 trasera ultra gran angular — Vision de Apple sobre la imagen (2D).
//  4 cuerpo 3D — ARKit con la cámara trasera (y LiDAR si lo hay): esqueleto en 3D con profundidad real.
// Los datos mueven al personaje en el propio dispositivo (Unity los recoge con TraiggerBodySensorPoll) y, si hay
// unas Vision Pro en la misma Wi-Fi (Bonjour «_traigger-sensor._udp»), también se les envían.
// Izquierda y derecha son las del usuario en todos los modos.
final class TraiggerBodySensor: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate, ARSessionDelegate, @unchecked Sendable {
    static let shared = TraiggerBodySensor()
    private let queue = DispatchQueue(label: "traigger.body")
    private let lock = NSLock()
    private var session: AVCaptureSession?
    private var arSession: ARSession?
    private let bodyRequest = VNDetectHumanBodyPoseRequest()
    private let handRequest: VNDetectHumanHandPoseRequest = { let r = VNDetectHumanHandPoseRequest(); r.maximumHandCount = 2; return r }()
    private var browser: NWBrowser?
    private var link: NWConnection?
    private var linked = false
    private var lastSend = 0.0
    private var mode = 0
    private var latest: String?
    private var statusText = "Sensor apagado"
    var status: String { lock.withLock { statusText } }
    private func setStatus(_ value: String) { lock.withLock { statusText = value } }

    // Modos disponibles en este aparato (bit n = modo n).
    static func available() -> Int32 {
        var mask: Int32 = 0
        if AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .front) != nil { mask |= 1 << 1 }
        if AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) != nil { mask |= 1 << 2 }
        if AVCaptureDevice.default(.builtInUltraWideCamera, for: .video, position: .back) != nil { mask |= 1 << 3 }
        if ARBodyTrackingConfiguration.isSupported { mask |= 1 << 4 }
        return mask
    }
    static var hasLiDAR: Bool { ARWorldTrackingConfiguration.supportsSceneReconstruction(.mesh) }

    func start(_ newMode: Int) {
        queue.async {
            self.stopCapture()
            self.mode = newMode
            self.startBrowsing()
            if newMode == 4 { self.startAR() } else { self.startCamera(newMode) }
        }
    }
    func stop() {
        queue.async {
            self.stopCapture(); self.mode = 0
            self.browser?.cancel(); self.browser = nil
            self.link?.cancel(); self.link = nil; self.linked = false
            self.setStatus("Sensor apagado")
        }
    }
    func poll() -> String? { lock.withLock { defer { latest = nil }; return latest } }

    private func stopCapture() {
        session?.stopRunning(); session = nil
        if let arSession { DispatchQueue.main.async { arSession.pause() } }
        arSession = nil
    }

    // MARK: Cámara + Vision (2D)

    private func startCamera(_ mode: Int) {
        let device: AVCaptureDevice?
        switch mode {
        case 2: device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back)
        case 3: device = AVCaptureDevice.default(.builtInUltraWideCamera, for: .video, position: .back)
        default: device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .front)
        }
        let session = AVCaptureSession()
        session.beginConfiguration()
        session.sessionPreset = .vga640x480
        guard let device, let input = try? AVCaptureDeviceInput(device: device), session.canAddInput(input) else {
            setStatus("No se puede abrir esa cámara"); session.commitConfiguration(); return
        }
        session.addInput(input)
        let output = AVCaptureVideoDataOutput()
        output.alwaysDiscardsLateVideoFrames = true
        output.setSampleBufferDelegate(self, queue: queue)
        if session.canAddOutput(output) { session.addOutput(output) }
        session.commitConfiguration()
        session.startRunning()
        self.session = session
        setStatus(linked ? "Conectado a Vision Pro" : "Siguiendo el cuerpo")
    }

    // La imagen llega girada; la frontal además en espejo.
    private func orientation() -> CGImagePropertyOrientation {
        let front = mode == 1
        switch UIDevice.current.orientation {
        case .landscapeLeft: return front ? .downMirrored : .up
        case .landscapeRight: return front ? .upMirrored : .down
        case .portraitUpsideDown: return front ? .rightMirrored : .left
        default: return front ? .leftMirrored : .right
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        let now = CACurrentMediaTime()
        guard now - lastSend > 1.0 / 30.0, let pixels = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        lastSend = now
        let handler = VNImageRequestHandler(cvPixelBuffer: pixels, orientation: orientation())
        try? handler.perform([bodyRequest, handRequest])
        var message: [String: Any] = ["body": true]
        let found = bodyRequest.results?.first != nil
        setStatus(linked ? "Conectado a Vision Pro" : found ? "Siguiendo el cuerpo" : "Buscando tu cuerpo… (que se vea de cintura para arriba)")
        if let body = bodyRequest.results?.first, let points = try? body.recognizedPoints(.all) {
            func p(_ name: VNHumanBodyPoseObservation.JointName) -> CGPoint? {
                guard let point = points[name], point.confidence > 0.3 else { return nil }
                return point.location
            }
            if let leftEye = p(.leftEye), let rightEye = p(.rightEye) {
                // Inclinación: ángulo de la línea entre los ojos (grados, positivo hacia el hombro derecho del usuario).
                message["roll"] = atan2(leftEye.y - rightEye.y, leftEye.x - rightEye.x) * 180 / .pi
                if let nose = p(.nose) {
                    let mid = CGPoint(x: (leftEye.x + rightEye.x) / 2, y: (leftEye.y + rightEye.y) / 2)
                    let width = max(hypot(leftEye.x - rightEye.x, leftEye.y - rightEye.y), 0.001)
                    message["yaw"] = (nose.x - mid.x) / width       // ~-1..1 (giro)
                    message["pitch"] = (mid.y - nose.y) / width     // asentir
                }
            }
            for (side, shoulder, elbow, wrist) in [("left", VNHumanBodyPoseObservation.JointName.leftShoulder, VNHumanBodyPoseObservation.JointName.leftElbow, VNHumanBodyPoseObservation.JointName.leftWrist),
                                                   ("right", .rightShoulder, .rightElbow, .rightWrist)] {
                guard let s = p(shoulder), let w = p(wrist) else { continue }
                // Elevación del brazo: ángulo del hombro a la muñeca desde la vertical hacia abajo (0 = colgando).
                let raise = atan2(abs(w.x - s.x), s.y - w.y) * 180 / .pi
                var arm: [String: Any] = ["raise": raise, "up": w.y > s.y]
                if let e = p(elbow) {
                    let a = atan2(e.y - s.y, e.x - s.x), b = atan2(w.y - e.y, w.x - e.x)
                    arm["bend"] = abs(remainder(Double(b - a), 2 * .pi)) * 180 / .pi
                }
                message[side == "left" ? "armLeft" : "armRight"] = arm
            }
        }
        if let hands = handRequest.results {
            for hand in hands {
                guard let points = try? hand.recognizedPoints(.all), let wrist = points[.wrist], let middle = points[.middleTip],
                      wrist.confidence > 0.3, middle.confidence > 0.3 else { continue }
                let side = hand.chirality == .left ? "handLeft" : "handRight"
                message[side] = ["open": hypot(middle.location.x - wrist.location.x, middle.location.y - wrist.location.y)]
            }
        }
        deliver(message)
    }

    // MARK: ARKit cuerpo 3D (cámara trasera, LiDAR si lo hay)

    private func startAR() {
        guard ARBodyTrackingConfiguration.isSupported else { setStatus("Este aparato no admite cuerpo 3D (requiere chip A12 o posterior)"); return }
        let configuration = ARBodyTrackingConfiguration()
        // Con LiDAR, ARKit mide tu altura real y el esqueleto encaja mejor.
        configuration.automaticSkeletonScaleEstimationEnabled = true
        let session = ARSession()
        session.delegate = self
        session.delegateQueue = queue
        arSession = session
        DispatchQueue.main.async { session.run(configuration, options: [.resetTracking, .removeExistingAnchors]) }
        setStatus("Buscando tu cuerpo (cámara trasera" + (Self.hasLiDAR ? " + LiDAR" : "") + ")…")
    }

    func session(_ session: ARSession, didUpdate anchors: [ARAnchor]) {
        guard let body = anchors.compactMap({ $0 as? ARBodyAnchor }).first else { return }
        let now = CACurrentMediaTime()
        guard now - lastSend > 1.0 / 30.0 else { return }
        lastSend = now
        let skeleton = body.skeleton
        func joint(_ name: String) -> simd_float4x4? { skeleton.modelTransform(for: ARSkeleton.JointName(rawValue: name)) }
        func position(_ name: String) -> SIMD3<Float>? { joint(name).map { SIMD3($0.columns.3.x, $0.columns.3.y, $0.columns.3.z) } }
        var message: [String: Any] = ["body": true, "body3D": true]
        // Cabeza respecto al pecho, en grados. Se envía con la misma escala que el modo 2D (giro ÷35, asentir ÷-40).
        if let head = joint("head_joint"), let chest = joint("spine_7_joint") {
            let r = simd_mul(simd_inverse(chest), head)
            let yaw = atan2(r.columns.2.x, r.columns.2.z) * 180 / .pi
            let pitch = asin(max(-1, min(1, -r.columns.2.y))) * 180 / .pi
            let roll = atan2(r.columns.0.y, r.columns.1.y) * 180 / .pi
            message["yaw"] = Double(yaw) / 35
            message["pitch"] = Double(pitch) / -40
            message["roll"] = Double(roll)
        }
        for side in ["left", "right"] {
            guard let s = position("\(side)_arm_joint"), let e = position("\(side)_forearm_joint"), let w = position("\(side)_hand_joint") else { continue }
            let upper = e - s, lower = w - e, whole = w - s
            // Elevación: ángulo entre el brazo (hombro → muñeca) y la vertical hacia abajo; flexión: codo.
            let raise = acos(max(-1, min(1, simd_dot(simd_normalize(whole), SIMD3<Float>(0, -1, 0))))) * 180 / .pi
            let bend = acos(max(-1, min(1, simd_dot(simd_normalize(upper), simd_normalize(lower))))) * 180 / .pi
            message["\(side == "left" ? "armLeft" : "armRight")"] = ["raise": Double(raise), "up": w.y > s.y, "bend": Double(bend), "forward": Double(whole.z)]
        }
        // Piernas: apertura (muslo hacia fuera, en grados, vista de frente) y flexión de la rodilla. En el espacio del
        // esqueleto de ARKit la persona mira hacia +z y su izquierda es +x, así que «hacia fuera» es +x en la izquierda.
        for side in ["left", "right"] {
            guard let h = position("\(side)_upLeg_joint"), let k = position("\(side)_leg_joint"), let f = position("\(side)_foot_joint") else { continue }
            let thigh = k - h, shin = f - k
            let outward: Float = side == "left" ? 1 : -1
            let spread = atan2(thigh.x * outward, -thigh.y) * 180 / .pi
            let bend = acos(max(-1, min(1, simd_dot(simd_normalize(thigh), simd_normalize(shin))))) * 180 / .pi
            message[side == "left" ? "legLeft" : "legRight"] = ["spread": Double(spread), "bend": Double(bend)]
        }
        // Inclinación del torso respecto a la cadera (grados, positivo hacia la derecha del usuario).
        if let chest = joint("spine_7_joint") { message["lean"] = Double(atan2(chest.columns.1.x, chest.columns.1.y) * 180 / .pi) }
        setStatus(linked ? "Cuerpo 3D · conectado a Vision Pro" : "Siguiendo tu cuerpo en 3D")
        deliver(message)
    }
    func session(_ session: ARSession, didFailWithError error: Error) { setStatus("ARKit: \(error.localizedDescription)") }

    // MARK: Entrega (Unity local + Vision Pro)

    private func deliver(_ message: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: message) else { return }
        if let json = String(data: data, encoding: .utf8) { lock.withLock { latest = json } }
        link?.send(content: data, completion: .idempotent)
    }

    private func startBrowsing() {
        guard browser == nil else { return }
        let browser = NWBrowser(for: .bonjour(type: "_traigger-sensor._udp", domain: nil), using: .udp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self, self.link == nil, let result = results.first else { return }
            let link = NWConnection(to: result.endpoint, using: .udp)
            link.stateUpdateHandler = { [weak self] state in
                if case .ready = state { self?.linked = true }
                if case .failed = state { self?.link = nil; self?.linked = false }
            }
            link.start(queue: self.queue)
            self.link = link
        }
        browser.start(queue: queue)
        self.browser = browser
    }
}

@_cdecl("TraiggerBodySensorStart") public func TraiggerBodySensorStart() { TraiggerBodySensor.shared.start(1) }
@_cdecl("TraiggerBodySensorStartMode") public func TraiggerBodySensorStartMode(_ mode: Int32) { TraiggerBodySensor.shared.start(Int(mode)) }
@_cdecl("TraiggerBodySensorStop") public func TraiggerBodySensorStop() { TraiggerBodySensor.shared.stop() }
@_cdecl("TraiggerBodySensorAvailable") public func TraiggerBodySensorAvailable() -> Int32 { TraiggerBodySensor.available() | (TraiggerBodySensor.hasLiDAR ? 1 << 8 : 0) }
@_cdecl("TraiggerBodySensorStatus") public func TraiggerBodySensorStatus() -> UnsafeMutablePointer<CChar>? {
    strdup(TraiggerBodySensor.shared.status)
}
@_cdecl("TraiggerBodySensorPoll") public func TraiggerBodySensorPoll() -> UnsafeMutablePointer<CChar>? {
    guard let message = TraiggerBodySensor.shared.poll() else { return nil }
    return strdup(message)
}
