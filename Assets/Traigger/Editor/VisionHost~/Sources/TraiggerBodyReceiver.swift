import Foundation
import Network

// Recibe el cuerpo y las manos que envía un iPhone o iPad con Traigger en modo sensor (misma Wi-Fi, Bonjour).
// Así Traigger se mueve con el cuerpo real sin abrir el espacio inmersivo: FaceTime y otras apps siguen visibles.
@MainActor final class TraiggerBodyReceiver {
    static let shared = TraiggerBodyReceiver()
    private var listener: NWListener?
    func start() {
        guard listener == nil, let listener = try? NWListener(using: .udp) else { return }
        listener.service = NWListener.Service(name: "Traigger Vision Pro", type: "_traigger-sensor._udp")
        listener.newConnectionHandler = { connection in
            connection.start(queue: .main)
            Self.receive(on: connection)
        }
        listener.stateUpdateHandler = { state in
            let text: String?
            switch state {
            case .failed(let error): text = "Red: \(error.localizedDescription)"
            case .ready: text = "Esperando un iPhone o iPad en modo sensor"
            default: text = nil
            }
            if let text { Task { @MainActor in TraiggerUnity.shared.send("{\"bodyStatus\":\"\(text)\"}") } }
        }
        listener.start(queue: .main)
        self.listener = listener
    }
    nonisolated private static func receive(on connection: NWConnection) {
        connection.receiveMessage { data, _, _, error in
            if let data, let json = String(data: data, encoding: .utf8) {
                Task { @MainActor in TraiggerUnity.shared.send(json) }
            }
            if error == nil { receive(on: connection) }
        }
    }
}
