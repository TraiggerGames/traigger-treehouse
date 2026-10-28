import Foundation
import Network
// Opcional: únicamente se envían datos a la dirección que el usuario escribe en Ajustes.
@MainActor final class TraiggerMotionRelay {
    static let shared = TraiggerMotionRelay()
    private var connection: NWConnection?
    private var destination = ""
    func send(_ json: String) {
        let host = UserDefaults.standard.string(forKey: "TraiggerRelayHost")?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if host != destination {
            connection?.cancel(); connection = nil; destination = host
            if !host.isEmpty {
                let c = NWConnection(host: NWEndpoint.Host(host), port: 49984, using: .udp)
                c.start(queue: .main); connection = c
            }
        }
        guard let data = json.data(using: .utf8), data.count < 32768 else { return }
        connection?.send(content: data, completion: .contentProcessed { _ in })
    }
}
