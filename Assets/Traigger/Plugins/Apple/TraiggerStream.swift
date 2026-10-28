import AVFoundation
import CoreMedia
import Foundation
import Network
import VideoToolbox

// Emisión en directo a Twitch, YouTube o cualquier servidor RTMP, sin OBS ni librerías externas (iPhone, iPad, Mac y
// Vision Pro; en las gafas el micrófono y la capturadora llegan desde el host nativo con TraiggerStreamPushPCM).
// Vídeo: la imagen compuesta de Unity (StreamOutput.cs: Traigger, película, capturadora, carteles…) en H.264 por
// hardware, con un fotograma clave cada 2 s. Sonido: AAC con la mezcla del micrófono, la película y la capturadora.
// RTMP: saludo simple, connect → createStream → publish, mensajes en trozos de 4096 bytes (rtmp:// o rtmps://).
final class TraiggerStream: @unchecked Sendable {
    static let shared = TraiggerStream()
    private let queue = DispatchQueue(label: "traigger.stream", qos: .userInteractive)
    private let lock = NSLock()
    private var rtmp: TraiggerRTMP?
    private var statusText = "Sin emitir"
    private var running = false
    private var startTime = DispatchTime.now()
    private var settings = Settings()
    struct Settings: Codable { var url = ""; var key = ""; var width = 1280; var height = 720; var fps = 30; var mbps = 4.5; var audioKbps = 160 }

    var status: String { lock.withLock { statusText } }
    var isLive: Bool { lock.withLock { running } && rtmp?.publishing == true }
    private func setStatus(_ text: String) { lock.withLock { statusText = text } }
    private func millis() -> UInt32 { UInt32((DispatchTime.now().uptimeNanoseconds - startTime.uptimeNanoseconds) / 1_000_000) }

    func start(_ json: String) {
        guard let s = try? JSONDecoder().decode(Settings.self, from: Data(json.utf8)), !s.url.isEmpty, !s.key.isEmpty else { setStatus("Falta la dirección o la clave de emisión"); return }
        queue.async {
            self.stopLocked()
            self.settings = s; self.startTime = DispatchTime.now()
            self.lock.withLock { self.running = true }
            self.setStatus("Conectando con el servidor…")
            let client = TraiggerRTMP(url: s.url, key: s.key, queue: self.queue)
            client.onStatus = { [weak self] text in self?.setStatus(text) }
            client.onPublishing = { [weak self] in self?.queue.async { self?.sendMetadata(); self?.needsKey = true; self?.audioConfigSent = false } }
            client.connect()
            self.rtmp = client
            self.mixer.start()
            TraiggerMovieSetStreamSink { samples, frames in TraiggerStream.shared.mixer.push(int16: samples, frames: Int(frames), source: .movie) }
        }
    }
    func stop() { queue.async { self.stopLocked(); self.setStatus("Sin emitir") } }
    private func stopLocked() {
        lock.withLock { running = false }
        rtmp?.close(); rtmp = nil
        if let encoder { VTCompressionSessionInvalidate(encoder) }; encoder = nil
        mixer.stop(); aac = nil; audioConfigSent = false; videoConfigSent = false
    }

    // MARK: Vídeo
    private var encoder: VTCompressionSession?
    private var pool: CVPixelBufferPool?
    private var poolSize = (0, 0)
    private var needsKey = true, videoConfigSent = false, busy = false
    private var frames = 0, sentSince = DispatchTime.now(), sentFrames = 0

    // Píxeles BGRA de Unity (fila 0 = abajo).
    func sendVideo(_ pixels: UnsafeRawPointer, _ width: Int, _ height: Int) {
        guard isLive else { return }
        let accepted: Bool = queue.sync { if busy { return false }; busy = true; return true }
        guard accepted else { return }
        guard let buffer = makeBuffer(pixels, width, height) else { queue.async { self.busy = false }; return }
        let pts = CMTime(value: CMTimeValue(millis()), timescale: 1000)
        queue.async { self.encode(buffer, width, height, pts) }
    }
    private func makeBuffer(_ pixels: UnsafeRawPointer, _ width: Int, _ height: Int) -> CVPixelBuffer? {
        if pool == nil || poolSize != (width, height) {
            let attributes: [String: Any] = [kCVPixelBufferWidthKey as String: width, kCVPixelBufferHeightKey as String: height,
                                             kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA, kCVPixelBufferIOSurfacePropertiesKey as String: [:]]
            pool = nil; CVPixelBufferPoolCreate(nil, nil, attributes as CFDictionary, &pool); poolSize = (width, height)
        }
        var buffer: CVPixelBuffer?
        guard let pool, CVPixelBufferPoolCreatePixelBuffer(nil, pool, &buffer) == kCVReturnSuccess, let buffer else { return nil }
        CVPixelBufferLockBaseAddress(buffer, [])
        let destination = CVPixelBufferGetBaseAddress(buffer)!, stride = CVPixelBufferGetBytesPerRow(buffer), row = width * 4
        for y in 0..<height { memcpy(destination + y * stride, pixels + (height - 1 - y) * row, row) }
        CVPixelBufferUnlockBaseAddress(buffer, [])
        return buffer
    }
    private func encode(_ buffer: CVPixelBuffer, _ width: Int, _ height: Int, _ pts: CMTime) {
        let s = settings
        if encoder == nil {
            // Tamaño de emisión (el que pide la plataforma), escalado por el propio codificador.
            VTCompressionSessionCreate(allocator: nil, width: Int32(s.width), height: Int32(s.height), codecType: kCMVideoCodecType_H264,
                                       encoderSpecification: nil, imageBufferAttributes: nil, compressedDataAllocator: nil,
                                       outputCallback: nil, refcon: nil, compressionSessionOut: &encoder)
            guard let encoder else { busy = false; return }
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_RealTime, value: kCFBooleanTrue)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_ProfileLevel, value: kVTProfileLevel_H264_Main_AutoLevel)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_AllowFrameReordering, value: kCFBooleanFalse)  // sin B-frames
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_ExpectedFrameRate, value: s.fps as CFNumber)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_AverageBitRate, value: Int(s.mbps * 1_000_000) as CFNumber)
            // Tope de tasa (1,5× la media en cada segundo): las plataformas cortan si se pasa mucho.
            let limit = [Int(s.mbps * 1_500_000 / 8), 1] as CFArray
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_DataRateLimits, value: limit)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration, value: 2.0 as CFNumber)
            needsKey = true
        }
        guard let encoder else { busy = false; return }
        let options = needsKey ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary : nil
        needsKey = false
        let status = VTCompressionSessionEncodeFrame(encoder, imageBuffer: buffer, presentationTimeStamp: pts, duration: .invalid,
                                                     frameProperties: options, infoFlagsOut: nil) { [weak self] status, _, sample in
            guard let self else { return }
            self.queue.async {
                self.busy = false
                guard status == noErr, let sample else { return }
                self.sendSample(sample, pts)
            }
        }
        if status != noErr { busy = false; VTCompressionSessionInvalidate(encoder); self.encoder = nil }
    }
    private func sendSample(_ sample: CMSampleBuffer, _ pts: CMTime) {
        guard let rtmp, rtmp.publishing, let data = CMSampleBufferGetDataBuffer(sample) else { return }
        let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false) as? [[CFString: Any]]
        let key = !(attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool ?? false)
        let time = UInt32(pts.value)
        // Con la red atascada se saltan fotogramas (y se pide uno clave para reengancharse) en vez de acumular retraso.
        if rtmp.backlog > 3_000_000 && !key { needsKey = true; return }
        if key, !videoConfigSent, let format = CMSampleBufferGetFormatDescription(sample), let config = Self.avcConfig(format) {
            rtmp.send(type: 9, time: time, body: Data([0x17, 0, 0, 0, 0]) + config); videoConfigSent = true
        }
        var length = 0, pointer: UnsafeMutablePointer<CChar>?
        guard CMBlockBufferGetDataPointer(data, atOffset: 0, lengthAtOffsetOut: nil, totalLengthOut: &length, dataPointerOut: &pointer) == noErr, let pointer else { return }
        var body = Data([key ? 0x17 : 0x27, 1, 0, 0, 0])
        body.append(UnsafeRawPointer(pointer).assumingMemoryBound(to: UInt8.self), count: length)
        rtmp.send(type: 9, time: time, body: body)
        sentFrames += 1
        let elapsed = Double(DispatchTime.now().uptimeNanoseconds - sentSince.uptimeNanoseconds) / 1e9
        if elapsed > 2 {
            setStatus(String(format: "En directo · %.0f fps · %.1f Mbps", Double(sentFrames) / elapsed, Double(rtmp.sentBytesAndReset()) * 8 / elapsed / 1_000_000))
            sentFrames = 0; sentSince = DispatchTime.now()
        }
    }
    // AVCDecoderConfigurationRecord a partir del SPS y el PPS del codificador.
    private static func avcConfig(_ format: CMFormatDescription) -> Data? {
        var sets: [Data] = []
        for index in 0..<2 {
            var pointer: UnsafePointer<UInt8>?, size = 0
            guard CMVideoFormatDescriptionGetH264ParameterSetAtIndex(format, parameterSetIndex: index, parameterSetPointerOut: &pointer,
                                                                     parameterSetSizeOut: &size, parameterSetCountOut: nil, nalUnitHeaderLengthOut: nil) == noErr, let pointer else { return nil }
            sets.append(Data(bytes: pointer, count: size))
        }
        let sps = sets[0], pps = sets[1]
        guard sps.count > 3 else { return nil }
        var record = Data([1, sps[1], sps[2], sps[3], 0xFF, 0xE1, UInt8(sps.count >> 8), UInt8(sps.count & 0xFF)])
        record.append(sps); record.append(contentsOf: [1, UInt8(pps.count >> 8), UInt8(pps.count & 0xFF)]); record.append(pps)
        return record
    }
    private func sendMetadata() {
        let s = settings
        var body = AMF.string("@setDataFrame") + AMF.string("onMetaData")
        body += AMF.ecmaArray([("width", .number(Double(s.width))), ("height", .number(Double(s.height))), ("framerate", .number(Double(s.fps))),
                               ("videocodecid", .number(7)), ("videodatarate", .number(s.mbps * 1000)), ("audiocodecid", .number(10)),
                               ("audiodatarate", .number(Double(s.audioKbps))), ("audiosamplerate", .number(48000)), ("audiosamplesize", .number(16)),
                               ("stereo", .bool(true)), ("encoder", .string("Traigger"))])
        rtmp?.send(type: 18, time: 0, body: body)
    }

    // MARK: Sonido
    let mixer = TraiggerStreamMixer()
    private var aac: AVAudioConverter?
    private var audioConfigSent = false
    private let pcmFormat = AVAudioFormat(standardFormatWithSampleRate: 48000, channels: 2)!
    // El mezclador entrega bloques de 1024 muestras (lo que ocupa una trama AAC).
    func encodeAudio(_ pcm: AVAudioPCMBuffer, time: UInt32) {
        queue.async {
            guard let rtmp = self.rtmp, rtmp.publishing else { return }
            if self.aac == nil {
                var description = AudioStreamBasicDescription(mSampleRate: 48000, mFormatID: kAudioFormatMPEG4AAC, mFormatFlags: 0, mBytesPerPacket: 0,
                                                              mFramesPerPacket: 1024, mBytesPerFrame: 0, mChannelsPerFrame: 2, mBitsPerChannel: 0, mReserved: 0)
                guard let format = AVAudioFormat(streamDescription: &description), let converter = AVAudioConverter(from: self.pcmFormat, to: format) else { return }
                converter.bitRate = self.settings.audioKbps * 1000
                self.aac = converter
            }
            if !self.audioConfigSent {
                // AudioSpecificConfig: AAC-LC, 48 kHz, estéreo.
                rtmp.send(type: 8, time: time, body: Data([0xAF, 0, 0x11, 0x90])); self.audioConfigSent = true
            }
            guard let converter = self.aac else { return }
            let output = AVAudioCompressedBuffer(format: converter.outputFormat, packetCapacity: 1, maximumPacketSize: converter.maximumOutputPacketSize)
            var given = false, error: NSError?
            let status = converter.convert(to: output, error: &error) { _, state in
                if given { state.pointee = .noDataNow; return nil }
                given = true; state.pointee = .haveData; return pcm
            }
            guard status != .error, output.byteLength > 0 else { return }
            var body = Data([0xAF, 1]); body.append(output.data.assumingMemoryBound(to: UInt8.self), count: Int(output.byteLength))
            rtmp.send(type: 8, time: time, body: body)
        }
    }
    func audioTime() -> UInt32 { millis() }
}

// Mezcla en tiempo real del micrófono, la película y la capturadora (48 kHz estéreo), en bloques de 1024 muestras.
final class TraiggerStreamMixer: @unchecked Sendable {
    enum Source: Int, CaseIterable { case mic, movie, capture }
    private let lock = NSLock()
    private var rings = [[Float]](repeating: [], count: 3)
    private var converters: [Int: AVAudioConverter] = [:]
    private let format = AVAudioFormat(standardFormatWithSampleRate: 48000, channels: 2)!
    var gains: [Float] = [1, 1, 1]
    private var timer: DispatchSourceTimer?
    private var produced: Int64 = 0, startedAt = DispatchTime.now()
    #if os(macOS)
    private var micEngine: AVAudioEngine?
    #endif

    func start() {
        lock.withLock { rings = [[Float]](repeating: [], count: 3) }
        produced = 0; startedAt = DispatchTime.now()
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue(label: "traigger.stream.mix", qos: .userInteractive))
        timer.schedule(deadline: .now() + 0.05, repeating: .milliseconds(10))
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume(); self.timer = timer
        #if os(macOS)
        // En el Mac el micrófono se escucha aquí (en iPhone/iPad lo pasa la voz de Traigger, TraiggerVoiceIOS.swift).
        let engine = AVAudioEngine(), input = engine.inputNode
        input.installTap(onBus: 0, bufferSize: 1024, format: input.outputFormat(forBus: 0)) { [weak self] buffer, _ in self?.push(.mic, buffer) }
        try? engine.start(); micEngine = engine
        #endif
    }
    func stop() {
        timer?.cancel(); timer = nil
        #if os(macOS)
        micEngine?.inputNode.removeTap(onBus: 0); micEngine?.stop(); micEngine = nil
        #endif
    }
    var active: Bool { timer != nil }

    // Cualquier formato: se convierte a 48 kHz estéreo y se añade a su cola (con un máximo de 0,5 s).
    func push(_ source: Source, _ buffer: AVAudioPCMBuffer) {
        guard active else { return }
        var converted = buffer
        if buffer.format != format {
            let converter: AVAudioConverter? = lock.withLock {
                if let c = converters[source.rawValue], c.inputFormat == buffer.format { return c }
                let c = AVAudioConverter(from: buffer.format, to: format); converters[source.rawValue] = c; return c
            }
            guard let converter, let output = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(Double(buffer.frameLength) * 48000 / buffer.format.sampleRate) + 64) else { return }
            var given = false, error: NSError?
            converter.convert(to: output, error: &error) { _, state in if given { state.pointee = .noDataNow; return nil }; given = true; state.pointee = .haveData; return buffer }
            guard error == nil else { return }
            converted = output
        }
        guard let channels = converted.floatChannelData else { return }
        let frames = Int(converted.frameLength)
        lock.withLock {
            var ring = rings[source.rawValue]
            ring.reserveCapacity(ring.count + frames * 2)
            for i in 0..<frames { ring.append(channels[0][i]); ring.append(channels[min(1, Int(converted.format.channelCount) - 1)][i]) }
            if ring.count > 48000 { ring.removeFirst(ring.count - 48000) }
            rings[source.rawValue] = ring
        }
    }
    func push(int16 samples: UnsafePointer<Int16>, frames: Int, source: Source) {
        guard active, frames > 0, let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(frames)), let channels = buffer.floatChannelData else { return }
        buffer.frameLength = AVAudioFrameCount(frames)
        for i in 0..<frames { channels[0][i] = Float(samples[i * 2]) / 32768; channels[1][i] = Float(samples[i * 2 + 1]) / 32768 }
        push(source, buffer)
    }

    // Al ritmo del reloj (no de las fuentes): cada 1024 muestras se mezcla lo que haya; lo que falte, silencio.
    private func tick() {
        let elapsed = Double(DispatchTime.now().uptimeNanoseconds - startedAt.uptimeNanoseconds) / 1e9
        while Double(produced + 1024) / 48000 <= elapsed {
            guard let block = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 1024), let out = block.floatChannelData else { return }
            block.frameLength = 1024
            for i in 0..<1024 { out[0][i] = 0; out[1][i] = 0 }
            lock.withLock {
                for source in Source.allCases {
                    let gain = gains[source.rawValue]
                    var ring = rings[source.rawValue]
                    let available = min(1024, ring.count / 2)
                    for i in 0..<available { out[0][i] += ring[i * 2] * gain; out[1][i] += ring[i * 2 + 1] * gain }
                    ring.removeFirst(available * 2)
                    rings[source.rawValue] = ring
                }
            }
            for i in 0..<1024 { out[0][i] = max(-1, min(1, out[0][i])); out[1][i] = max(-1, min(1, out[1][i])) }
            let time = UInt32(Double(produced) / 48)
            produced += 1024
            TraiggerStream.shared.encodeAudio(block, time: time)
        }
    }
}

// MARK: - RTMP
final class TraiggerRTMP: @unchecked Sendable {
    private let url: String, key: String, queue: DispatchQueue
    private var connection: NWConnection?
    private var outChunk = 128, inChunk = 128
    private var streamId: Double = 0
    private(set) var publishing = false
    private var inFlight = 0, sentBytes = 0
    var backlog: Int { inFlight }
    var onStatus: ((String) -> Void)?
    var onPublishing: (() -> Void)?
    private var app = "", tcUrl = ""

    init(url: String, key: String, queue: DispatchQueue) { self.url = url; self.key = key; self.queue = queue }
    func sentBytesAndReset() -> Int { defer { sentBytes = 0 }; return sentBytes }

    func connect() {
        // rtmp(s)://servidor[:puerto]/app[/instancia]
        guard let parsed = URL(string: url), let host = parsed.host else { onStatus?("Dirección de emisión no válida"); return }
        let secure = parsed.scheme == "rtmps"
        let port = parsed.port ?? (secure ? 443 : 1935)
        app = parsed.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        tcUrl = url.hasSuffix("/") ? String(url.dropLast()) : url
        let tcp = NWProtocolTCP.Options(); tcp.noDelay = true
        let parameters = secure ? NWParameters(tls: NWProtocolTLS.Options(), tcp: tcp) : NWParameters(tls: nil, tcp: tcp)
        let connection = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: UInt16(port))!, using: parameters)
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready: self.handshake()
            case .failed(let error): self.publishing = false; self.onStatus?("Error de conexión: \(error.localizedDescription)")
            case .waiting(let error): self.onStatus?("Esperando a la red: \(error.localizedDescription)")
            default: break
            }
        }
        connection.start(queue: queue)
        self.connection = connection
    }
    func close() { publishing = false; connection?.cancel(); connection = nil }

    private func raw(_ data: Data) {
        inFlight += data.count; sentBytes += data.count
        connection?.send(content: data, completion: .contentProcessed { [weak self] _ in self?.queue.async { self?.inFlight -= data.count } })
    }

    // Saludo simple: C0+C1, se espera S0+S1+S2, se devuelve C2 (= S1).
    private func handshake() {
        var c1 = Data(count: 1536)
        c1.withUnsafeMutableBytes { for i in 8..<1536 { $0[i] = UInt8.random(in: 0...255) } }
        raw(Data([3]) + c1)
        receive(exactly: 1 + 1536 + 1536) { [weak self] data in
            guard let self else { return }
            self.raw(data.subdata(in: 1..<1537))
            self.onStatus?("Conectado; iniciando la emisión…")
            self.setChunkSize(4096)
            self.command("connect", 1, [AMF.object([("app", .string(self.app)), ("type", .string("nonprivate")), ("flashVer", .string("FMLE/3.0 (compatible; Traigger)")),
                                                    ("tcUrl", .string(self.tcUrl))])])
            self.readMessages()
        }
    }
    private func receive(exactly count: Int, _ done: @escaping (Data) -> Void) {
        connection?.receive(minimumIncompleteLength: count, maximumLength: count) { data, _, _, error in
            if let data, data.count == count, error == nil { done(data) }
        }
    }

    // MARK: Envío de mensajes (cabecera completa en cada mensaje; los trozos siguientes con cabecera de tipo 3).
    private func setChunkSize(_ size: Int) {
        var value = UInt32(size).bigEndian
        sendMessage(csid: 2, type: 1, stream: 0, time: 0, body: Data(bytes: &value, count: 4))
        outChunk = size
    }
    private func command(_ name: String, _ transaction: Double, _ args: [Data], stream: UInt32 = 0, csid: UInt8 = 3) {
        // connect lleva su objeto justo detrás; el resto de comandos, un null y luego sus argumentos.
        var body = AMF.string(name) + AMF.number(transaction)
        if name != "connect" { body += AMF.null() }
        for arg in args { body += arg }
        sendMessage(csid: csid, type: 20, stream: stream, time: 0, body: body)
    }
    func send(type: UInt8, time: UInt32, body: Data) {
        guard publishing else { return }
        sendMessage(csid: type == 8 ? 4 : type == 9 ? 6 : 5, type: type, stream: UInt32(streamId), time: time, body: body)
    }
    private func sendMessage(csid: UInt8, type: UInt8, stream: UInt32, time: UInt32, body: Data) {
        var out = Data(capacity: body.count + 32)
        let extended = time >= 0xFFFFFF
        let stamp = extended ? 0xFFFFFF : time
        out.append(csid & 0x3F)
        out.append(contentsOf: [UInt8(stamp >> 16 & 0xFF), UInt8(stamp >> 8 & 0xFF), UInt8(stamp & 0xFF)])
        out.append(contentsOf: [UInt8(body.count >> 16 & 0xFF), UInt8(body.count >> 8 & 0xFF), UInt8(body.count & 0xFF), type])
        var little = stream.littleEndian; out.append(Data(bytes: &little, count: 4))
        var big = time.bigEndian
        if extended { out.append(Data(bytes: &big, count: 4)) }
        var offset = 0
        while offset < body.count {
            if offset > 0 { out.append(0xC0 | (csid & 0x3F)); if extended { out.append(Data(bytes: &big, count: 4)) } }
            let end = min(body.count, offset + outChunk)
            out.append(body.subdata(in: offset..<end)); offset = end
        }
        raw(out)
    }

    // MARK: Recepción (solo lo necesario: tamaño de trozo y comandos _result / onStatus).
    private struct ChunkState { var length = 0, type: UInt8 = 0, time: UInt32 = 0, stream: UInt32 = 0, data = Data() }
    private var chunks: [UInt32: ChunkState] = [:]
    private var buffer = Data()
    private func readMessages() {
        connection?.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, done, error in
            guard let self else { return }
            if let data { self.buffer.append(data); self.parse() }
            if done || error != nil { self.publishing = false; self.onStatus?("El servidor cerró la conexión"); return }
            self.readMessages()
        }
    }
    private func parse() {
        while true {
            var index = buffer.startIndex
            guard index < buffer.endIndex else { return }
            let first = buffer[index]; index += 1
            let fmt = first >> 6
            var csid = UInt32(first & 0x3F)
            if csid == 0 { guard index < buffer.endIndex else { return }; csid = UInt32(buffer[index]) + 64; index += 1 }
            else if csid == 1 { guard index + 1 < buffer.endIndex else { return }; csid = UInt32(buffer[index]) + UInt32(buffer[index + 1]) * 256 + 64; index += 2 }
            let headerSize = [11, 7, 3, 0][Int(fmt)]
            guard buffer.endIndex - index >= headerSize else { return }
            var state = chunks[csid] ?? ChunkState()
            func u24(_ i: Int) -> UInt32 { UInt32(buffer[i]) << 16 | UInt32(buffer[i + 1]) << 8 | UInt32(buffer[i + 2]) }
            var stamp: UInt32 = 0
            if fmt <= 2 { stamp = u24(index) }
            if fmt <= 1 { state.length = Int(u24(index + 3)); state.type = buffer[index + 6] }
            if fmt == 0 { state.stream = UInt32(buffer[index + 7]) | UInt32(buffer[index + 8]) << 8 | UInt32(buffer[index + 9]) << 16 | UInt32(buffer[index + 10]) << 24 }
            index += headerSize
            if fmt <= 2 && stamp == 0xFFFFFF { guard buffer.endIndex - index >= 4 else { return }; index += 4 }
            let remaining = state.length - state.data.count
            let take = min(inChunk, remaining)
            guard buffer.endIndex - index >= take else { return }
            state.data.append(buffer.subdata(in: index..<index + take)); index += take
            buffer = buffer.subdata(in: index..<buffer.endIndex)
            if state.data.count >= state.length {
                handle(type: state.type, body: state.data)
                state.data = Data()
            }
            chunks[csid] = state
        }
    }
    private func handle(type: UInt8, body: Data) {
        switch type {
        case 1 where body.count >= 4:
            inChunk = Int(UInt32(body[body.startIndex]) << 24 | UInt32(body[body.startIndex + 1]) << 16 | UInt32(body[body.startIndex + 2]) << 8 | UInt32(body[body.startIndex + 3]))
        case 20:
            let values = AMF.decode(body)
            guard let name = values.first?.string else { return }
            let transaction = values.count > 1 ? values[1].number ?? 0 : 0
            if name == "_result" && transaction == 1 {
                command("releaseStream", 2, [AMF.string(key)])
                command("FCPublish", 3, [AMF.string(key)])
                command("createStream", 4, [])
            } else if name == "_result" && transaction == 4 {
                streamId = values.count > 3 ? values[3].number ?? 1 : 1
                command("publish", 5, [AMF.string(key), AMF.string("live")], stream: UInt32(streamId), csid: 8)
            } else if name == "onStatus" {
                let code = values.last?.object?["code"]?.string ?? ""
                if code == "NetStream.Publish.Start" { publishing = true; onStatus?("En directo"); onPublishing?() }
                else if code.contains("Fail") || code.contains("BadName") || code.contains("Rejected") { onStatus?("El servidor rechazó la emisión (\(code)): revisa la clave") }
            } else if name == "_error" {
                onStatus?("El servidor rechazó la conexión: revisa la dirección y la clave")
            }
        default: break
        }
    }
}

// AMF0, lo justo para RTMP.
enum AMF {
    indirect enum Value {
        case number(Double), bool(Bool), string(String), null, object([String: Value])
        var string: String? { if case .string(let s) = self { return s }; return nil }
        var number: Double? { if case .number(let n) = self { return n }; return nil }
        var object: [String: Value]? { if case .object(let o) = self { return o }; return nil }
    }
    static func number(_ n: Double) -> Data { var bits = n.bitPattern.bigEndian; return Data([0]) + Data(bytes: &bits, count: 8) }
    static func string(_ s: String) -> Data { let utf = Data(s.utf8); return Data([2, UInt8(utf.count >> 8), UInt8(utf.count & 0xFF)]) + utf }
    static func null() -> Data { Data([5]) }
    private static func key(_ k: String) -> Data { let utf = Data(k.utf8); return Data([UInt8(utf.count >> 8), UInt8(utf.count & 0xFF)]) + utf }
    private static func encode(_ v: Value) -> Data {
        switch v {
        case .number(let n): return number(n)
        case .bool(let b): return Data([1, b ? 1 : 0])
        case .string(let s): return string(s)
        case .null: return null()
        case .object(let o): return object(o.map { ($0.key, $0.value) })
        }
    }
    static func object(_ pairs: [(String, Value)]) -> Data {
        var data = Data([3]); for (k, v) in pairs { data += key(k) + encode(v) }; return data + Data([0, 0, 9])
    }
    static func ecmaArray(_ pairs: [(String, Value)]) -> Data {
        var count = UInt32(pairs.count).bigEndian
        var data = Data([8]) + Data(bytes: &count, count: 4); for (k, v) in pairs { data += key(k) + encode(v) }; return data + Data([0, 0, 9])
    }
    static func decode(_ data: Data) -> [Value] {
        var values: [Value] = [], index = data.startIndex
        func readString() -> String? {
            guard index + 2 <= data.endIndex else { return nil }
            let length = Int(data[index]) << 8 | Int(data[index + 1]); index += 2
            guard index + length <= data.endIndex else { return nil }
            defer { index += length }
            return String(data: data.subdata(in: index..<index + length), encoding: .utf8)
        }
        func readValue() -> Value? {
            guard index < data.endIndex else { return nil }
            let marker = data[index]; index += 1
            switch marker {
            case 0:
                guard index + 8 <= data.endIndex else { return nil }
                let bits = data.subdata(in: index..<index + 8).withUnsafeBytes { $0.loadUnaligned(as: UInt64.self).bigEndian }; index += 8
                return .number(Double(bitPattern: bits))
            case 1: guard index < data.endIndex else { return nil }; defer { index += 1 }; return .bool(data[index] != 0)
            case 2: return readString().map { .string($0) }
            case 3, 8:
                if marker == 8 { index += 4 }
                var object: [String: Value] = [:]
                while index + 3 <= data.endIndex {
                    if data[index] == 0 && data[index + 1] == 0 && data[index + 2] == 9 { index += 3; break }
                    guard let k = readString(), let v = readValue() else { break }
                    object[k] = v
                }
                return .object(object)
            case 5, 6: return .null
            default: return nil
            }
        }
        while let value = readValue() { values.append(value) }
        return values
    }
}

@_silgen_name("TraiggerMovieSetStreamSink") func TraiggerMovieSetStreamSink(_ sink: @convention(c) (UnsafePointer<Int16>, Int32) -> Void)

// Funciones para Unity (StreamOutput.cs) y para las fuentes de sonido.
@_cdecl("TraiggerStreamStart") public func TraiggerStreamStart(_ json: UnsafePointer<CChar>) { TraiggerStream.shared.start(String(cString: json)) }
@_cdecl("TraiggerStreamStop") public func TraiggerStreamStop() { TraiggerStream.shared.stop() }
@_cdecl("TraiggerStreamIsLive") public func TraiggerStreamIsLive() -> Int32 { TraiggerStream.shared.isLive ? 1 : 0 }
@_cdecl("TraiggerStreamSendVideo") public func TraiggerStreamSendVideo(_ pixels: UnsafeRawPointer, _ width: Int32, _ height: Int32) {
    TraiggerStream.shared.sendVideo(pixels, Int(width), Int(height))
}
@_cdecl("TraiggerStreamStatus") public func TraiggerStreamStatus() -> UnsafeMutablePointer<CChar>? { strdup(TraiggerStream.shared.status) }
@_cdecl("TraiggerStreamSetGains") public func TraiggerStreamSetGains(_ mic: Float, _ movie: Float, _ capture: Float) {
    TraiggerStream.shared.mixer.gains = [mic, movie, capture]
}
@_cdecl("TraiggerStreamFree") public func TraiggerStreamFree(_ pointer: UnsafeMutableRawPointer?) { free(pointer) }
// Sonido de otras partes de la app (host de Vision Pro): float intercalado; fuente 0 = micrófono, 2 = capturadora.
@_cdecl("TraiggerStreamPushPCM") public func TraiggerStreamPushPCM(_ samples: UnsafePointer<Float>, _ frames: Int32, _ channels: Int32, _ rate: Double, _ source: Int32) {
    guard frames > 0, channels > 0, let kind = TraiggerStreamMixer.Source(rawValue: Int(source)),
          let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: AVAudioChannelCount(channels), interleaved: false),
          let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(frames)), let out = buffer.floatChannelData else { return }
    buffer.frameLength = AVAudioFrameCount(frames)
    for i in 0..<Int(frames) { for c in 0..<Int(channels) { out[c][i] = samples[i * Int(channels) + c] } }
    TraiggerStream.shared.mixer.push(kind, buffer)
}
