import ARKit
import Foundation
import QuartzCore
import simd

// Pose de la cabeza (WorldTrackingProvider) y de las manos (HandTrackingProvider), 30 veces por segundo.
// Se envía a Unity como JSON pequeño: {"head":{...},"left":{...},"right":{...}} en metros, espacio del mundo.
@MainActor final class TraiggerSensors {
    static let shared = TraiggerSensors()
    private var session = ARKitSession()
    private var world = WorldTrackingProvider()
    private var hands = HandTrackingProvider()
    private var headSamples = 0
    private var status = "Sensores apagados (modo compartido)"
    func reportMode(_ on: Bool) { if !on { status = "Sensores apagados (modo compartido)" }; sendStatus() }
    private func sendStatus() { TraiggerUnity.shared.send("{\"sensors\":\"\(status)\",\"immersive\":\(running)}") }
    func stop() {
        running = false; session.stop(); session = ARKitSession(); world = WorldTrackingProvider(); hands = HandTrackingProvider()
        left = ["tracked": false]; right = ["tracked": false]; reportMode(false)
    }
    private var left: [String: Any] = ["tracked": false], right: [String: Any] = ["tracked": false]
    private var running = false

    func start() async {
        guard !running else { return }
        running = true
        var providers: [any DataProvider] = []
        if WorldTrackingProvider.isSupported { providers.append(world) }
        if HandTrackingProvider.isSupported {
            let auth = await session.requestAuthorization(for: [.handTracking])
            if auth[.handTracking] == .allowed { providers.append(hands) }
        }
        guard !providers.isEmpty else { status = "ARKit no disponible"; running = false; sendStatus(); return }
        do { try await session.run(providers) } catch { status = "ARKit: \(error.localizedDescription)"; running = false; sendStatus(); return }
        status = "Sensores activos"; sendStatus()
        if providers.contains(where: { $0 is HandTrackingProvider }) { Task { await watchHands() } }
        Task { await loop() }
    }

    private func watchHands() async {
        for await update in hands.anchorUpdates {
            let anchor = update.anchor
            var sample: [String: Any] = ["tracked": anchor.isTracked]
            if anchor.isTracked, let skeleton = anchor.handSkeleton {
                let wrist = anchor.originFromAnchorTransform * skeleton.joint(.wrist).anchorFromJointTransform
                let index = anchor.originFromAnchorTransform * skeleton.joint(.indexFingerTip).anchorFromJointTransform
                let thumb = anchor.originFromAnchorTransform * skeleton.joint(.thumbTip).anchorFromJointTransform
                sample["wrist"] = point(wrist.columns.3)
                // Todos los puntos conservan su nombre ARKit, estado y posición en metros.
                sample["joints"] = HandSkeleton.JointName.allCases.map { name -> [String: Any] in
                    let joint = skeleton.joint(name)
                    let transform = anchor.originFromAnchorTransform * joint.anchorFromJointTransform
                    return ["name": String(describing: name), "tracked": joint.isTracked, "position": point(transform.columns.3)]
                }
                let chains: [[HandSkeleton.JointName]] = [
                    [.thumbKnuckle, .thumbIntermediateBase, .thumbIntermediateTip, .thumbTip],
                    [.indexFingerKnuckle, .indexFingerIntermediateBase, .indexFingerIntermediateTip, .indexFingerTip],
                    [.middleFingerKnuckle, .middleFingerIntermediateBase, .middleFingerIntermediateTip, .middleFingerTip],
                    [.ringFingerKnuckle, .ringFingerIntermediateBase, .ringFingerIntermediateTip, .ringFingerTip],
                    [.littleFingerKnuckle, .littleFingerIntermediateBase, .littleFingerIntermediateTip, .littleFingerTip]
                ]
                sample["curls"] = chains.map { chain -> Float in
                    let joints = chain.map { skeleton.joint($0) }
                    guard joints.allSatisfy({ $0.isTracked }) else { return -1 }
                    let p = joints.map { j -> SIMD3<Float> in
                        let v = j.anchorFromJointTransform.columns.3
                        return SIMD3(v.x, v.y, v.z)
                    }
                    let segments = [p[1]-p[0], p[2]-p[1], p[3]-p[2]]
                    guard segments.allSatisfy({ simd_length($0) > 0.0001 }) else { return -1 }
                    let a = acos(max(-1, min(1, simd_dot(simd_normalize(segments[0]), simd_normalize(segments[1])))))
                    let b = acos(max(-1, min(1, simd_dot(simd_normalize(segments[1]), simd_normalize(segments[2])))))
                    return min(1, max(0, (a+b) / 2.8))
                }
                sample["sampleTime"] = CACurrentMediaTime()

                sample["pinch"] = simd_distance(SIMD3(index.columns.3.x, index.columns.3.y, index.columns.3.z),
                                                SIMD3(thumb.columns.3.x, thumb.columns.3.y, thumb.columns.3.z))
            }
            if anchor.chirality == .left { left = sample } else { right = sample }
        }
    }

    private func loop() async {
        while running {
            var l = left, r = right
            let now = CACurrentMediaTime()
            l["sampleAge"] = now - (l["sampleTime"] as? Double ?? 0)
            r["sampleAge"] = now - (r["sampleTime"] as? Double ?? 0)
            var message: [String: Any] = ["left": l, "right": r]
            // queryDeviceAnchor con la hora actual a veces no tiene muestra; se pide una pose ligeramente futura.
            if let device = world.queryDeviceAnchor(atTimestamp: CACurrentMediaTime() + 0.02) {
                headSamples += 1
                let m = device.originFromAnchorTransform
                let q = simd_quatf(m)
                message["headSamples"] = headSamples
                message["head"] = ["position": point(m.columns.3), "rotation": ["x": q.imag.x, "y": q.imag.y, "z": q.imag.z, "w": q.real]]
            }
            if let data = try? JSONSerialization.data(withJSONObject: message), let json = String(data: data, encoding: .utf8) {
                TraiggerUnity.shared.send(json)
            }
            try? await Task.sleep(nanoseconds: 33_000_000)
        }
    }

    private func point(_ v: SIMD4<Float>) -> [String: Float] { ["x": v.x, "y": v.y, "z": v.z] }
}
