#if !os(visionOS)
import Foundation
import Network

// TraiggerLink (versión abierta): transporte del emisor (iPhone/iPad/Mac) hacia un receptor en la red local
// (iPad o Vision Pro) para el vídeo de la capturadora HDMI.
//
// Esta es la versión básica del repositorio público: un único camino por la red de casa (Wi-Fi o cable), con la
// misma interfaz y el mismo formato de paquetes que la versión completa, así que los receptores son los mismos.
// No incluye el doble camino con conexión directa (AWDL), el relevo automático ni el diagnóstico con consejos:
// esa parte es de una app comercial aparte (ver docs/TERCEROS.md → «Módulos no incluidos»). Puedes sustituir este
// archivo por tu propia implementación siempre que mantengas la interfaz pública.
//
// Formato de los paquetes (lo decide quien lo usa): [UInt32 big endian: tamaño][datos]. Los pings son
// [tamaño][4][UInt64 big endian: hora en ns]; el receptor los devuelve tal cual por la misma conexión.
public final class TraiggerLink: @unchecked Sendable {
    public struct PathReport { public var name: String; public var connected: Bool; public var rttMs: Double; public var jitterMs: Double; public var lossPercent: Double; public var active: Bool }
    public struct Diagnosis { public var paths: [PathReport]; public var verdict: String; public var advice: String; public var latencyMs: Double }

    public var onSwitch: (() -> Void)?
    public var onStatus: ((String) -> Void)?
    public var onKeyRequest: (() -> Void)?
    // Receptor concreto por su nombre anunciado («iPad», «Vision Pro»…); nil = el primero que aparezca.
    public var targetName: String?
    // Se conserva por compatibilidad con la interfaz: esta versión siempre usa la red de casa.
    public enum Mode: String { case router, direct, auto }
    public var mode: Mode = .router
    public var useDirect: Bool { false }
    // UDP: un paquete perdido no frena a los demás. TCP: todo llega, pero un reenvío retrasa lo que viene detrás.
    public enum Transport { case udp, tcp }
    public let transport: Transport
    public private(set) var running = false

    private let service: String, queue: DispatchQueue
    private var browser: NWBrowser?
    private var connection: NWConnection?
    private var connected = false
    private var inFlight = 0
    private var rtt = 0.0, jitter = 0.0, lastPong = Date.distantPast
    private var sent: [UInt64] = [], window: [(Date, Bool)] = []
    private var timer: DispatchSourceTimer?
    private var messageId: UInt32 = 0
    static let maxPayload = 1200

    public init(service: String, queue: DispatchQueue, transport: Transport = .tcp) {
        self.service = service; self.queue = queue; self.transport = transport
        queue.setSpecific(key: Self.key, value: ObjectIdentifier(self))
    }
    private static let key = DispatchSpecificKey<ObjectIdentifier>()
    private func onQueue<T>(_ body: () -> T) -> T {
        DispatchQueue.getSpecific(key: Self.key) == ObjectIdentifier(self) ? body() : queue.sync(execute: body)
    }

    public func start() {
        queue.async {
            guard !self.running else { return }
            self.running = true
            self.browse()
            let timer = DispatchSource.makeTimerSource(queue: self.queue)
            timer.schedule(deadline: .now() + 0.25, repeating: 0.25)
            timer.setEventHandler { [weak self] in self?.ping() }
            timer.resume(); self.timer = timer
        }
    }
    public func stop() {
        queue.async {
            self.running = false
            self.timer?.cancel(); self.timer = nil
            self.browser?.cancel(); self.browser = nil
            self.connection?.cancel(); self.connection = nil; self.connected = false
        }
    }

    private func browse() {
        let browser = NWBrowser(for: .bonjour(type: service, domain: nil), using: transport == .udp ? .udp : .tcp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self, self.connection == nil else { return }
            let wanted = results.first { result in
                guard let target = self.targetName else { return true }
                if case .service(let name, _, _, _) = result.endpoint { return name.localizedCaseInsensitiveContains(target) }
                return false
            }
            if let wanted { self.connect(wanted.endpoint) }
        }
        browser.stateUpdateHandler = { [weak self] state in
            if case .failed(let error) = state { self?.onStatus?("No se puede buscar en la red local: \(error.localizedDescription)") }
        }
        browser.start(queue: queue)
        self.browser = browser
        onStatus?("Buscando el receptor en la red…")
    }
    private func connect(_ endpoint: NWEndpoint) {
        let parameters: NWParameters
        if transport == .udp { parameters = NWParameters(dtls: nil, udp: NWProtocolUDP.Options()) }
        else { let tcp = NWProtocolTCP.Options(); tcp.noDelay = true; parameters = NWParameters(tls: nil, tcp: tcp) }
        parameters.serviceClass = .interactiveVideo
        let connection = NWConnection(to: endpoint, using: parameters)
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                self.connected = true; self.onStatus?("Conectado por la red de casa"); self.onSwitch?(); self.read()
            case .failed, .cancelled:
                self.connected = false
                // Reintento a los 2 s mientras siga en marcha.
                if case .failed = state, self.running {
                    self.connection = nil
                    self.queue.asyncAfter(deadline: .now() + 2) { if self.running && self.connection == nil { self.connect(endpoint) } }
                }
            default: break
            }
        }
        connection.start(queue: queue)
        self.connection = connection
    }

    // MARK: Medida (ping cada 0,25 s para mostrar la latencia)
    private func ping() {
        guard connected, let connection else { return }
        let now = DispatchTime.now().uptimeNanoseconds
        var stamp = now.bigEndian
        var body = Data([4]); body.append(Data(bytes: &stamp, count: 8))
        if transport == .udp { sendDatagrams(body, on: connection) } else {
            var size = UInt32(body.count).bigEndian
            var packet = Data(bytes: &size, count: 4); packet.append(body)
            connection.send(content: packet, completion: .idempotent)
        }
        sent.append(now); if sent.count > 40 { sent.removeFirst() }
        let cutoff = now - 1_000_000_000
        while let first = sent.first, first < cutoff { sent.removeFirst(); window.append((Date(), false)) }
        window.removeAll { Date().timeIntervalSince($0.0) > 4 }
    }
    private func pong(_ sentAt: UInt64) {
        if let index = sent.firstIndex(of: sentAt) { sent.remove(at: index) }
        let measured = Double(DispatchTime.now().uptimeNanoseconds - sentAt) / 1_000_000
        jitter = rtt == 0 ? 0 : jitter * 0.8 + abs(measured - rtt) * 0.2
        rtt = rtt == 0 ? measured : rtt * 0.8 + measured * 0.2
        lastPong = Date(); window.append((Date(), true))
    }
    private func read() {
        guard let connection else { return }
        if transport == .tcp {
            connection.receive(minimumIncompleteLength: 13, maximumLength: 13) { [weak self] data, _, done, error in
                guard let self, let data, data.count == 13, error == nil, !done else { return }
                self.pong(data.subdata(in: 5..<13).withUnsafeBytes { $0.loadUnaligned(as: UInt64.self).bigEndian })
                self.read()
            }
            return
        }
        // UDP: ecos de ping (tipo 4) y peticiones de fotograma clave («K»).
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self, error == nil else { return }
            if let data, data.count >= 10, data[data.startIndex] == 0x54 {
                let payload = data.subdata(in: data.startIndex + 10..<data.endIndex)
                if data[data.startIndex + 1] == 4, payload.count == 9 {
                    self.pong(payload.subdata(in: payload.startIndex + 1..<payload.endIndex).withUnsafeBytes { $0.loadUnaligned(as: UInt64.self).bigEndian })
                } else if data[data.startIndex + 1] == 0x4B { self.onKeyRequest?() }
            }
            self.read()
        }
    }

    // MARK: UDP
    // Datagrama: [«T»][tipo del mensaje][trozo UInt16][total UInt16][id del mensaje UInt32][datos ≤ 1200].
    private func sendDatagrams(_ message: Data, on connection: NWConnection) {
        messageId &+= 1
        let count = max(1, (message.count + Self.maxPayload - 1) / Self.maxPayload)
        let type = message.first ?? 0
        for index in 0..<count {
            let start = message.startIndex + index * Self.maxPayload, end = min(message.endIndex, start + Self.maxPayload)
            var header = Data([0x54, type, UInt8(index >> 8), UInt8(index & 0xff), UInt8(count >> 8), UInt8(count & 0xff)])
            var id = messageId.bigEndian; header.append(Data(bytes: &id, count: 4))
            inFlight += end - start
            connection.send(content: header + message.subdata(in: start..<end), completion: .contentProcessed { [weak self] _ in
                self?.queue.async { self?.inFlight -= end - start }
            })
        }
    }

    // MARK: Envío (solo desde la cola del enlace)
    public var ready: Bool { connected && Date().timeIntervalSince(lastPong) < 0.75 }
    // Los paquetes «descartables» (fotogramas no clave) no se mandan con más de 1,5 MB pendientes. Devuelve si se envió.
    @discardableResult
    public func send(_ packet: Data, droppable: Bool, done: (() -> Void)? = nil) -> Bool {
        dispatchPrecondition(condition: .onQueue(queue))
        guard connected, let connection else { done?(); return false }
        if droppable && inFlight > 1_500_000 { done?(); return false }
        if transport == .udp {
            sendDatagrams(packet.count > 4 ? packet.subdata(in: packet.startIndex + 4..<packet.endIndex) : packet, on: connection)
            done?(); return true
        }
        inFlight += packet.count
        connection.send(content: packet, completion: .contentProcessed { [weak self] _ in
            self?.queue.async { self?.inFlight -= packet.count; done?() }
        })
        return true
    }

    // MARK: Diagnóstico (básico: latencia, variación y pérdidas del único camino)
    public func diagnose() -> Diagnosis {
        onQueue {
            let total = window.count, lost = window.filter { !$0.1 }.count
            let loss = total == 0 ? 0 : Double(lost) / Double(total) * 100
            let usable = ready && rtt > 0
            let report = PathReport(name: "Router", connected: usable, rttMs: rtt, jitterMs: jitter, lossPercent: loss, active: usable)
            guard usable else {
                return Diagnosis(paths: [report], verdict: "Sin conexión", advice: "Abre Traigger en el receptor, en la misma Wi-Fi.", latencyMs: 0)
            }
            let oneWay = rtt / 2
            let verdict = oneWay < 6 && jitter < 3 && loss < 1 ? "Óptimo" : oneWay < 15 && jitter < 8 && loss < 3 ? "Bueno" : "Mejorable"
            return Diagnosis(paths: [report], verdict: verdict, advice: verdict == "Óptimo" ? "Condiciones ideales." : "Para menos retraso, acerca los aparatos al router o usa 5 GHz.", latencyMs: oneWay)
        }
    }
}
#endif
