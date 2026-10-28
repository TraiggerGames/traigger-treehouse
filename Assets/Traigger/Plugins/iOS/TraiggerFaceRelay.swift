import Foundation
import Network
import UIKit

// Cara por Wi-Fi entre aparatos con Traigger (p. ej. el iPhone como Face ID del iPad mientras el iPad usa la
// capturadora). Todos escuchan en Bonjour «_traigger-face._udp» y pasan lo que llega al receptor de Unity
// (FaceReceiver.cs, UDP 49983 local). Con «Enviar mi cara» activado, este aparato manda cada fotograma de ARKit
// (FaceFrame en JSON, desde StudioController.cs) al primer otro Traigger que encuentre.
final class TraiggerFaceRelay: @unchecked Sendable {
    static let shared = TraiggerFaceRelay()
    private let queue = DispatchQueue(label: "traigger.face.relay", qos: .userInteractive)
    // Nombre estable (el destino elegido se recuerda por nombre).
    private let me: String = {
        let key = "traigger.face.id"
        var id = UserDefaults.standard.integer(forKey: key)
        if id == 0 { id = Int.random(in: 1000...9999); UserDefaults.standard.set(id, forKey: key) }
        return "Traigger \(UIDevice.current.name) \(id)"
    }()
    private var results: [NWBrowser.Result] = []
    /// Otros Traigger que escuchan la cara (para elegir a cuál se envía).
    private(set) var peers: [String] = []
    /// Destino elegido («» = el primero que aparezca).
    var chosen: String { UserDefaults.standard.string(forKey: "traigger.face.target") ?? "" }
    func choose(_ name: String) { UserDefaults.standard.set(name, forKey: "traigger.face.target"); queue.async { [self] in pick() } }
    private var listener: NWListener?, browser: NWBrowser?, target: NWConnection?, local: NWConnection?
    private var sent = 0, received = 0, statusAt = Date(), peer = ""
    private(set) var status = "Esperando"
    var enabled: Bool { UserDefaults.standard.bool(forKey: "traigger.face.send") }

    func start() {
        queue.async { [self] in
            guard listener == nil else { return }
            let parameters = NWParameters.udp; parameters.includePeerToPeer = true; parameters.serviceClass = .interactiveVoice
            guard let listener = try? NWListener(using: parameters) else { return }
            listener.service = NWListener.Service(name: me, type: "_traigger-face._udp")
            listener.newConnectionHandler = { [weak self] connection in
                guard let self else { return }
                connection.start(queue: self.queue); self.receive(connection)
            }
            listener.stateUpdateHandler = { [weak self] state in
                if case .failed = state { listener.cancel(); self?.queue.asyncAfter(deadline: .now() + 1) { self?.listener = nil; self?.start() } }
            }
            listener.start(queue: queue); self.listener = listener
            let local = NWConnection(host: "127.0.0.1", port: 49983, using: .udp); local.start(queue: queue); self.local = local
        }
    }
    private func receive(_ connection: NWConnection) {
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self else { return }
            if let data { self.local?.send(content: data, completion: .idempotent); self.received += 1; self.report() }
            if error == nil { self.receive(connection) }
        }
    }

    func send(_ json: Data) {
        queue.async { [self] in
            guard enabled else { if browser != nil { browser?.cancel(); browser = nil; target?.cancel(); target = nil; peer = ""; results = []; peers = [] }; return }
            if browser == nil { browse() }
            guard let target, target.state == .ready else { return }
            target.send(content: json, completion: .idempotent); sent += 1; report()
        }
    }
    private func browse() {
        let parameters = NWParameters.udp; parameters.includePeerToPeer = true
        let browser = NWBrowser(for: .bonjour(type: "_traigger-face._udp", domain: nil), using: parameters)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self else { return }
            self.results = results.filter { if case .service(let name, _, _, _) = $0.endpoint { return name != self.me }; return false }
            self.peers = self.results.compactMap { if case .service(let name, _, _, _) = $0.endpoint { return name }; return nil }.sorted()
            self.pick()
        }
        browser.start(queue: queue); self.browser = browser
    }
    private func pick() {
        let wanted = chosen
        let match = results.first { if case .service(let name, _, _, _) = $0.endpoint { return wanted.isEmpty || name == wanted }; return false }
        guard let match, case .service(let name, _, _, _) = match.endpoint else { target?.cancel(); target = nil; peer = ""; return }
        if name == peer, target != nil { return }
        target?.cancel()
        let parameters = NWParameters.udp; parameters.includePeerToPeer = true; parameters.serviceClass = .interactiveVoice
        let connection = NWConnection(to: match.endpoint, using: parameters)
        connection.start(queue: queue); target = connection; peer = name
        NSLog("Traigger: enviando la cara a %@", name)
    }
    private func report() {
        guard Date().timeIntervalSince(statusAt) > 1 else { return }
        let seconds = Date().timeIntervalSince(statusAt)
        var parts: [String] = []
        if enabled { parts.append(peer.isEmpty ? (chosen.isEmpty ? "Buscando otro Traigger…" : "Esperando a \(chosen.replacingOccurrences(of: "Traigger ", with: ""))…") : String(format: "Enviando a %@ (%.0f fps)", peer.replacingOccurrences(of: "Traigger ", with: ""), Double(sent) / seconds)) }
        if received > 0 { parts.append(String(format: "Recibiendo cara de otro aparato (%.0f fps)", Double(received) / seconds)) }
        status = parts.isEmpty ? "Esperando" : parts.joined(separator: " · ")
        sent = 0; received = 0; statusAt = Date()
    }
}

@_cdecl("TraiggerFaceRelayStart") public func TraiggerFaceRelayStart() { TraiggerFaceRelay.shared.start() }
@_cdecl("TraiggerFaceRelayEnabled") public func TraiggerFaceRelayEnabled() -> Int32 { TraiggerFaceRelay.shared.enabled ? 1 : 0 }
@_cdecl("TraiggerFaceRelaySend") public func TraiggerFaceRelaySend(_ bytes: UnsafePointer<UInt8>, _ count: Int32) {
    TraiggerFaceRelay.shared.send(Data(bytes: bytes, count: Int(count)))
}
