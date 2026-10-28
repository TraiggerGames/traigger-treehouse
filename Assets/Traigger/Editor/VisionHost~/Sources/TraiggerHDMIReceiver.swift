import AVFoundation
import Network
import SwiftUI

// Entrada HDMI en Vision Pro: las Vision Pro no tienen USB, así que reciben por Wi-Fi lo que capta el iPad o el Mac
// (TraiggerCapture.swift, Bonjour «_traigger-hdmi._tcp»). La imagen va en la ventana «Entrada HDMI», que se abre sola
// al llegar el primer fotograma y se redimensiona como cualquier ventana; el sonido suena aquí.
// Todo lo de red, vídeo y sonido va en su propia cola de alta prioridad (antes iba en el hilo principal, compartido con
// Unity, y cada fotograma esperaba a que Unity terminase el suyo: decenas de milisegundos de más). Solo los cambios que
// ve la interfaz pasan por el hilo principal.
final class TraiggerHDMIReceiver: ObservableObject, @unchecked Sendable {
    static let shared = TraiggerHDMIReceiver()
    static let firstFrame = Notification.Name("Traigger.HDMI.First")
    @Published var aspect: Double = 16.0 / 9.0
    @Published var connected = false
    let layer = AVSampleBufferDisplayLayer()
    private var listener: NWListener?
    private var format: CMVideoFormatDescription?
    private var engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private var queued = 0
    private let queue = DispatchQueue(label: "traigger.hdmi.receive", qos: .userInteractive)
    private var frames = 0, logAt = Date(), firstSent = false
    // UDP (TraiggerLink.swift): mensajes troceados en datagramas que aquí se recomponen.
    private var udpListener: NWListener?
    private struct Partial { var type: UInt8; var parts: [Data?]; var received = 0 }
    private var partials: [UInt32: Partial] = [:]
    private var lastVideoId: UInt32 = 0, lastKeyRequest = Date.distantPast, dropped = 0
    private let audioFormat = AVAudioFormat(standardFormatWithSampleRate: 48000, channels: 2)!

    func start() {
        // Sin Nagle y aceptando también el enlace directo (AWDL) que usa el iPad con «Conexión directa».
        let tcp = NWProtocolTCP.Options(); tcp.noDelay = true
        let parameters = NWParameters(tls: nil, tcp: tcp)
        parameters.includePeerToPeer = true
        parameters.serviceClass = .interactiveVideo
        guard listener == nil else { return }
        let listener: NWListener
        do { listener = try NWListener(using: parameters) } catch { NSLog("Traigger: no se puede abrir el receptor HDMI: %@", error.localizedDescription); return }
        // Si el receptor falla (cambio de red, suspensión), se vuelve a abrir solo para seguir anunciándose.
        listener.stateUpdateHandler = { [weak self] state in
            NSLog("Traigger: receptor HDMI → %@", "\(state)")
            guard let self else { return }
            if case .failed = state { listener.cancel(); self.queue.asyncAfter(deadline: .now() + 1) { self.listener = nil; DispatchQueue.main.async { self.start() } } }
        }
        listener.serviceRegistrationUpdateHandler = { change in NSLog("Traigger: anuncio HDMI en la red → %@", "\(change)") }
        listener.service = NWListener.Service(name: "Traigger Vision Pro", type: "_traigger-hdmi._tcp")
        listener.newConnectionHandler = { [weak self] connection in
            guard let self else { return }
            NSLog("Traigger: conexión HDMI entrante desde %@", "\(connection.endpoint)")
            connection.start(queue: self.queue)
            DispatchQueue.main.async { self.connected = true }
            self.receive(connection, buffer: Data())
        }
        listener.start(queue: queue)
        self.listener = listener
        startUDP()
        layer.videoGravity = .resizeAspect
        engine.attach(player)
        engine.connect(player, to: engine.mainMixerNode, format: audioFormat)
    }

    private func startUDP() {
        guard udpListener == nil else { return }
        let parameters = NWParameters(dtls: nil, udp: NWProtocolUDP.Options())
        parameters.includePeerToPeer = true
        parameters.serviceClass = .interactiveVideo
        guard let listener = try? NWListener(using: parameters) else { NSLog("Traigger: no se puede abrir el receptor UDP"); return }
        listener.service = NWListener.Service(name: "Traigger Vision Pro", type: "_traigger-hdmiu._udp")
        listener.stateUpdateHandler = { [weak self] state in
            NSLog("Traigger: receptor UDP → %@", "\(state)")
            if case .failed = state { listener.cancel(); self?.queue.asyncAfter(deadline: .now() + 1) { self?.udpListener = nil; self?.startUDP() } }
        }
        listener.newConnectionHandler = { [weak self] connection in
            guard let self else { return }
            NSLog("Traigger: datos UDP desde %@", "\(connection.endpoint)")
            connection.start(queue: self.queue)
            DispatchQueue.main.async { self.connected = true }
            self.receiveDatagram(connection)
        }
        listener.start(queue: queue)
        udpListener = listener
    }
    // Datagrama: [«T»][tipo][trozo UInt16][total UInt16][id UInt32][datos].
    private func receiveDatagram(_ connection: NWConnection) {
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self else { return }
            if let data, data.count >= 10, data[data.startIndex] == 0x54 {
                let b = data.startIndex
                let type = data[b + 1]
                if type == 4 { connection.send(content: data, completion: .idempotent) }   // eco del ping
                else {
                    let index = Int(data[b + 2]) << 8 | Int(data[b + 3]), count = Int(data[b + 4]) << 8 | Int(data[b + 5])
                    let id = UInt32(data[b + 6]) << 24 | UInt32(data[b + 7]) << 16 | UInt32(data[b + 8]) << 8 | UInt32(data[b + 9])
                    self.assemble(id: id, type: type, index: index, count: count, payload: data.subdata(in: b + 10..<data.endIndex), connection: connection)
                }
            }
            if error == nil { self.receiveDatagram(connection) }
        }
    }
    private func assemble(id: UInt32, type: UInt8, index: Int, count: Int, payload: Data, connection: NWConnection) {
        guard count > 0, index < count, count < 4096 else { return }
        var partial = partials[id] ?? Partial(type: type, parts: Array(repeating: nil, count: count))
        guard partial.parts.count == count, partial.parts[index] == nil else { return }
        partial.parts[index] = payload; partial.received += 1
        guard partial.received == count else { partials[id] = partial; return }
        partials[id] = nil
        let message = partial.parts.reduce(into: Data()) { $0.append($1!) }
        let video = type == 2 || type == 5
        if video {
            // Un fotograma más nuevo completo: los anteriores que no llegaron enteros se descartan y se pide uno clave
            // (el descodificador no puede seguir sin las referencias que faltan).
            let stale = partials.filter { ($0.value.type == 2 || $0.value.type == 5) && $0.key < id }
            if !stale.isEmpty {
                stale.keys.forEach { partials[$0] = nil }; dropped += stale.count
                if Date().timeIntervalSince(lastKeyRequest) > 0.2 {
                    lastKeyRequest = Date()
                    var request = Data([0x54, 0x4B, 0, 0, 0, 1]); var zero = UInt32(0); request.append(Data(bytes: &zero, count: 4))
                    connection.send(content: request, completion: .idempotent)
                }
            }
            if id < lastVideoId && message.count > 1 && message[message.startIndex + 1] != 1 { return }   // llega tarde y no es clave
            lastVideoId = max(lastVideoId, id)
        }
        // Limpieza de restos (sonido perdido, etc.).
        if partials.count > 64 { partials.keys.filter { $0 < id &- 64 }.forEach { partials[$0] = nil } }
        handle(message)
    }

    private func receive(_ connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 1 << 20) { [weak self] data, _, done, error in
            guard let self else { return }
            do {
                var buffer = buffer
                if let data { buffer.append(data) }
                while buffer.count >= 4 {
                    let size = Int(buffer[buffer.startIndex]) << 24 | Int(buffer[buffer.startIndex + 1]) << 16 | Int(buffer[buffer.startIndex + 2]) << 8 | Int(buffer[buffer.startIndex + 3])
                    guard buffer.count >= 4 + size else { break }
                    let packet = buffer.subdata(in: buffer.startIndex + 4..<buffer.startIndex + 4 + size)
                    buffer = buffer.subdata(in: buffer.startIndex + 4 + size..<buffer.endIndex)
                    if packet.first == 4 { self.pong(connection, packet) } else { self.handle(packet) }
                }
                if done || error != nil { DispatchQueue.main.async { self.connected = false }; connection.cancel(); return }
                self.receive(connection, buffer: buffer)
            }
        }
    }

    // Ping del iPad: se devuelve tal cual para que mida la latencia de red.
    private func pong(_ connection: NWConnection, _ packet: Data) {
        var size = UInt32(packet.count).bigEndian
        var reply = Data(bytes: &size, count: 4); reply.append(packet)
        connection.send(content: reply, completion: .idempotent)
    }

    private func handle(_ packet: Data) {
        guard let type = packet.first else { return }
        if type == 3 { play(packet.dropFirst()); return }
        // 2 = H.264 (SPS, PPS), 5 = HEVC (VPS, SPS, PPS).
        guard type == 2 || type == 5, packet.count > 2 else { return }
        let hevc = type == 5
        var index = packet.startIndex + 1
        let key = packet[index] == 1; index += 1
        if key {
            func block() -> Data? {
                guard index + 2 <= packet.endIndex else { return nil }
                let size = Int(packet[index]) << 8 | Int(packet[index + 1]); index += 2
                guard index + size <= packet.endIndex else { return nil }
                defer { index += size }
                return packet.subdata(in: index..<index + size)
            }
            var sets: [Data] = []
            for _ in 0..<(hevc ? 3 : 2) { guard let set = block() else { return }; sets.append(set) }
            var newFormat: CMFormatDescription?
            let copies = sets.map { set -> UnsafeMutablePointer<UInt8> in
                let pointer = UnsafeMutablePointer<UInt8>.allocate(capacity: set.count); set.copyBytes(to: pointer, count: set.count); return pointer
            }
            defer { copies.forEach { $0.deallocate() } }
            let pointers = copies.map { UnsafePointer($0) }, sizes = sets.map(\.count)
            if hevc {
                _ = CMVideoFormatDescriptionCreateFromHEVCParameterSets(allocator: kCFAllocatorDefault, parameterSetCount: sets.count, parameterSetPointers: pointers,
                                                                        parameterSetSizes: sizes, nalUnitHeaderLength: 4, extensions: nil, formatDescriptionOut: &newFormat)
            } else {
                _ = CMVideoFormatDescriptionCreateFromH264ParameterSets(allocator: kCFAllocatorDefault, parameterSetCount: sets.count, parameterSetPointers: pointers,
                                                                        parameterSetSizes: sizes, nalUnitHeaderLength: 4, formatDescriptionOut: &newFormat)
            }
            if let newFormat {
                let first = format == nil
                format = newFormat
                let size = CMVideoFormatDescriptionGetDimensions(newFormat)
                if size.height > 0 { let value = Double(size.width) / Double(size.height); DispatchQueue.main.async { self.aspect = value } }
                if first { DispatchQueue.main.async { NotificationCenter.default.post(name: Self.firstFrame, object: nil) } }
            }
        }
        guard let format, index < packet.endIndex else { return }
        let frame = packet.subdata(in: index..<packet.endIndex)
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(allocator: kCFAllocatorDefault, memoryBlock: nil, blockLength: frame.count, blockAllocator: kCFAllocatorDefault,
                                                 customBlockSource: nil, offsetToData: 0, dataLength: frame.count, flags: 0, blockBufferOut: &block) == noErr, let block else { return }
        frame.withUnsafeBytes { _ = CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block, offsetIntoDestination: 0, dataLength: frame.count) }
        var sample: CMSampleBuffer?, size = frame.count
        guard CMSampleBufferCreateReady(allocator: kCFAllocatorDefault, dataBuffer: block, formatDescription: format, sampleCount: 1, sampleTimingEntryCount: 0,
                                        sampleTimingArray: nil, sampleSizeEntryCount: 1, sampleSizeArray: &size, sampleBufferOut: &sample) == noErr, let sample else { return }
        if let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true), CFArrayGetCount(attachments) > 0 {
            let dictionary = unsafeBitCast(CFArrayGetValueAtIndex(attachments, 0), to: CFMutableDictionary.self)
            CFDictionarySetValue(dictionary, Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(), Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
        }
        if layer.status == .failed { layer.flush() }
        layer.enqueue(sample)
        frames += 1
        if Date().timeIntervalSince(logAt) > 3 {
            NSLog("Traigger: HDMI recibido %.0f fps (estado de la capa: %d, fotogramas perdidos: %d)", Double(frames) / Date().timeIntervalSince(logAt), layer.status.rawValue, dropped)
            frames = 0; logAt = Date(); dropped = 0
        }
    }

    // PCM de 16 bits estéreo intercalado a 48 kHz.
    private func play(_ bytes: Data) {
        let frames = bytes.count / 4
        guard frames > 0, let buffer = AVAudioPCMBuffer(pcmFormat: audioFormat, frameCapacity: AVAudioFrameCount(frames)), let channels = buffer.floatChannelData else { return }
        buffer.frameLength = AVAudioFrameCount(frames)
        bytes.withUnsafeBytes { raw in
            let samples = raw.bindMemory(to: Int16.self)
            for i in 0..<frames { for c in 0..<2 { channels[c][i] = Float(Int16(littleEndian: samples[i * 2 + c])) / 32768 } }
        }
        player.volume = Float(UserDefaults.standard.object(forKey: "traigger.mix.vision.volume") as? Double ?? 1)   // mezcladora
        if !engine.isRunning {
            do { try engine.start(); player.play(); NSLog("Traigger: sonido HDMI sonando por %@", AVAudioSession.sharedInstance().currentRoute.outputs.map(\.portName).joined(separator: ", ")) }
            catch { NSLog("Traigger: no arranca el sonido HDMI: %@", error.localizedDescription) }
        }
        // Para jugar, el sonido no puede ir por detrás de la imagen: con más de ~80 ms en cola se vacía y se sigue.
        if queued > 4 { player.stop(); queued = 0; player.play() }
        queued += 1
        TraiggerStreamAudio.push(buffer, source: 2)
        player.scheduleBuffer(buffer) { [weak self] in self?.queue.async { self?.queued = max(0, (self?.queued ?? 1) - 1) } }
    }
}

struct TraiggerHDMIScreen: UIViewRepresentable {
    final class Host: UIView {
        let display: AVSampleBufferDisplayLayer
        init(_ display: AVSampleBufferDisplayLayer) { self.display = display; super.init(frame: .zero); layer.addSublayer(display); backgroundColor = .black }
        required init?(coder: NSCoder) { nil }
        override func layoutSubviews() { super.layoutSubviews(); display.frame = bounds }
    }
    func makeUIView(context: Context) -> Host { Host(TraiggerHDMIReceiver.shared.layer) }
    func updateUIView(_ view: Host, context: Context) {}
}

struct TraiggerHDMIWindow: View {
    @ObservedObject var receiver = TraiggerHDMIReceiver.shared
    var body: some View {
        TraiggerHDMIScreen()
            .aspectRatio(receiver.aspect, contentMode: .fit)
            .frame(minWidth: 480, minHeight: 480 / receiver.aspect)
            .overlay(alignment: .bottom) {
                if !receiver.connected { Text("Esperando la entrada HDMI del iPad o del Mac…").padding().glassBackgroundEffect() }
            }
    }
}
