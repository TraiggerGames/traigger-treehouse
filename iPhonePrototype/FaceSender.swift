// PROTOTIPO: añadir a una app iOS de Xcode con ARKit y un iPhone compatible.
// Info.plist: NSCameraUsageDescription y NSLocalNetworkUsageDescription.
// La app debe retener FaceSender y llamar start() / stop() desde su ciclo de vida.
// No incluye interfaz, proyecto Xcode, firma, reconexión ni calibración.
import ARKit
import Network
import simd

final class FaceSender: NSObject, ARSessionDelegate {
    private let session = ARSession()
    private let connection: NWConnection
    private var sequence = 0
    private var lastSent: TimeInterval = 0
    private var started = false
    init(macIP: String, port: UInt16 = 49983) {
        connection = NWConnection(host: NWEndpoint.Host(macIP), port: NWEndpoint.Port(rawValue: port)!, using: .udp)
        super.init()
        session.delegate = self
        session.delegateQueue = DispatchQueue.main
    }
    @discardableResult func start() -> Bool {
        guard ARFaceTrackingConfiguration.isSupported, !started else { return false }
        started = true
        connection.start(queue: DispatchQueue(label: "Traigger.UDP"))
        session.run(ARFaceTrackingConfiguration(), options: [.resetTracking, .removeExistingAnchors])
        return true
    }
    func stop() { session.pause(); connection.cancel() }
    func session(_ session: ARSession, didUpdate anchors: [ARAnchor]) {
        guard let face = anchors.compactMap({ $0 as? ARFaceAnchor }).first,
              face.isTracked,
              let frame = session.currentFrame,
              frame.timestamp - lastSent >= 1.0 / 30 else { return }
        lastSent = frame.timestamp
        // Cara relativa a cámara; los signos/ejes requieren calibración visual.
        let relative = simd_inverse(frame.camera.transform) * face.transform
        let roll = atan2(relative.columns.0.y, relative.columns.0.x) * 180 / Float.pi
        let yaw = atan2(relative.columns.2.x, relative.columns.2.z) * 180 / Float.pi
        let pitch = asin(max(-1, min(1, -relative.columns.2.y))) * 180 / Float.pi
        func value(_ key: ARFaceAnchor.BlendShapeLocation) -> Float { face.blendShapes[key]?.floatValue ?? 0 }
        sequence += 1
        let payload: [String: Any] = [
            "version": 1, "sequence": sequence,
            "headRoll": roll, "headYaw": yaw, "headPitch": pitch,
            "independentEyes": true,
            "eyeLookLeftX": value(.eyeLookInLeft) - value(.eyeLookOutLeft),
            "eyeLookLeftY": value(.eyeLookUpLeft) - value(.eyeLookDownLeft),
            "eyeLookRightX": value(.eyeLookOutRight) - value(.eyeLookInRight),
            "eyeLookRightY": value(.eyeLookUpRight) - value(.eyeLookDownRight),
            "jawOpen": value(.jawOpen), "eyeBlinkLeft": value(.eyeBlinkLeft), "eyeBlinkRight": value(.eyeBlinkRight),
            "mouthSmileLeft": value(.mouthSmileLeft), "mouthSmileRight": value(.mouthSmileRight),
            "eyeLookX": (value(.eyeLookOutLeft) - value(.eyeLookInLeft) + value(.eyeLookInRight) - value(.eyeLookOutRight)) / 2,
            "tongueOut": value(.tongueOut),
            "mouthFunnel": value(.mouthFunnel),
"mouthPucker": value(.mouthPucker),
"browInnerUp": value(.browInnerUp),
"browDownLeft": value(.browDownLeft),
"browDownRight": value(.browDownRight),
"browOuterUpLeft": value(.browOuterUpLeft),
"browOuterUpRight": value(.browOuterUpRight),
"eyeWideLeft": value(.eyeWideLeft),
"eyeWideRight": value(.eyeWideRight),
"eyeSquintLeft": value(.eyeSquintLeft),
"eyeSquintRight": value(.eyeSquintRight),
"cheekPuff": value(.cheekPuff),
"cheekSquintLeft": value(.cheekSquintLeft),
"cheekSquintRight": value(.cheekSquintRight),
"mouthFrownLeft": value(.mouthFrownLeft),
"mouthFrownRight": value(.mouthFrownRight),
"mouthLeft": value(.mouthLeft),
"mouthRight": value(.mouthRight),
"mouthPressLeft": value(.mouthPressLeft),
"mouthPressRight": value(.mouthPressRight),
"noseSneerLeft": value(.noseSneerLeft),
"noseSneerRight": value(.noseSneerRight),
            "eyeLookY": (value(.eyeLookUpLeft) + value(.eyeLookUpRight) - value(.eyeLookDownLeft) - value(.eyeLookDownRight)) / 2
        ]
        if let bytes = try? JSONSerialization.data(withJSONObject: payload) {
            connection.send(content: bytes, completion: .contentProcessed({ _ in }))
        }
    }
}
