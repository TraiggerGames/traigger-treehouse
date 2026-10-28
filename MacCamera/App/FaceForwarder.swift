import Foundation
import Network

// El iPhone o el iPad como cámara de cara para Traigger en el Mac: con «Enviar mi cara» activado en el iPhone/iPad
// (TraiggerFaceRelay.swift), su Face ID llega aquí por Wi-Fi y se pasa a la app de Traigger del Mac
// (FaceReceiver.cs, UDP 49983 local). Se anuncia en Bonjour «_traigger-face._udp» como «Traigger Mac».
@MainActor final class FaceForwarder: ObservableObject {
    static let shared = FaceForwarder()
    @Published var status = "Cara del iPhone/iPad: esperando"
    private var listener: NWListener?
    private let local = NWConnection(host: "127.0.0.1", port: 49983, using: .udp)
    private nonisolated(unsafe) var received = 0
    private var timer: Timer?

    func start() {
        guard listener == nil else { return }
        local.start(queue: .global(qos: .userInteractive))
        let parameters = NWParameters.udp; parameters.includePeerToPeer = true; parameters.serviceClass = .interactiveVoice
        guard let listener = try? NWListener(using: parameters) else { status = "Cara del iPhone/iPad: no se pudo abrir"; return }
        listener.service = NWListener.Service(name: "Traigger Mac \(Host.current().localizedName ?? "")", type: "_traigger-face._udp")
        listener.newConnectionHandler = { [weak self] connection in
            connection.start(queue: .global(qos: .userInteractive)); self?.receive(connection)
        }
        listener.stateUpdateHandler = { state in
            if case .failed = state { Task { @MainActor in FaceForwarder.shared.listener = nil; FaceForwarder.shared.start() } }
        }
        listener.start(queue: .global(qos: .userInteractive)); self.listener = listener
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in
            MainActor.assumeIsolated {
                let me = FaceForwarder.shared, fps = me.received; me.received = 0
                me.status = fps > 0 ? "Cara del iPhone/iPad: recibiendo (\(fps) fps)" : "Cara del iPhone/iPad: esperando (actívalo en el iPhone → Seguimiento)"
            }
        }
    }
    private nonisolated func receive(_ connection: NWConnection) {
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self else { return }
            if let data { self.local.send(content: data, completion: .idempotent); self.received += 1 }
            if error == nil { self.receive(connection) }
        }
    }
}
