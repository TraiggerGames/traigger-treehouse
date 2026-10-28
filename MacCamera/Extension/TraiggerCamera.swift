import CoreMediaIO
import CoreVideo
import QuartzCore
import Foundation
import IOKit.audio
import os.log

// Extensión de cámara de Traigger (Core Media I/O). Tiene dos flujos:
//  · «salida» (source): lo que ven FaceTime y las demás apps.
//  · «entrada» (sink): por donde Traigger para Mac le manda cada imagen del personaje (VirtualCameraOutput.cs).
// Si Traigger no está enviando, la salida sigue mostrando la última imagen del personaje (guardada en disco, así que
// sobrevive a reinicios); sin texto, porque FaceTime enseña tu propia vista en espejo.
// 1080p; las apps eligen 60 o 30 fps (FaceTime y Zoom suelen pedir 30; Discord, Meet u OBS, 60).
let width = Int32(TraiggerVideo.width), height = Int32(TraiggerVideo.height)
let frameDurations = [CMTime(value: 1, timescale: 60), CMTime(value: 1, timescale: 30)]
let log = Logger(subsystem: "com.traigger.camera", category: "extension")

final class TraiggerDeviceSource: NSObject, CMIOExtensionDeviceSource {
    private(set) var device: CMIOExtensionDevice!
    private var source: TraiggerStreamSource!
    private var sink: TraiggerSinkSource!
    private var format: CMFormatDescription!
    private var pool: CVPixelBufferPool!
    private var timer: DispatchSourceTimer?
    private let queue = DispatchQueue(label: "traigger.camera", qos: .userInteractive)
    private var streamingClients = 0
    // Ritmo de salida: el que pida la app (60 por defecto).
    var frameDuration = frameDurations[0] { didSet { queue.async { if self.timer != nil { self.timer?.cancel(); self.timer = nil; self.startTimer() } } } }
    private var latest: CVPixelBuffer?
    private var latestTime = 0.0
    private lazy var waiting: CVPixelBuffer? = loadSavedFrame() ?? makePlainFrame()
    private var phone: PhoneReceiver?
    private var lastSave = 0.0

    init(localizedName: String) {
        super.init()
        device = CMIOExtensionDevice(localizedName: localizedName, deviceID: UUID(uuidString: "5B6F3A2E-7C1D-4E8F-9A0B-1C2D3E4F5A6B")!, legacyDeviceID: nil, source: self)
        CMVideoFormatDescriptionCreate(allocator: kCFAllocatorDefault, codecType: kCVPixelFormatType_32BGRA, width: width, height: height, extensions: nil, formatDescriptionOut: &format)
        let attributes: [String: Any] = [kCVPixelBufferWidthKey as String: width, kCVPixelBufferHeightKey as String: height,
                                         kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                                         kCVPixelBufferIOSurfacePropertiesKey as String: [:]]
        CVPixelBufferPoolCreate(kCFAllocatorDefault, nil, attributes as CFDictionary, &pool)
        let streamFormat = CMIOExtensionStreamFormat(formatDescription: format, maxFrameDuration: frameDurations[1],
                                                     minFrameDuration: frameDurations[0], validFrameDurations: frameDurations)
        source = TraiggerStreamSource(localizedName: "Traigger", streamID: UUID(uuidString: "6C7A4B3F-8D2E-4F90-AB1C-2D3E4F5A6B7C")!, format: streamFormat, device: self)
        sink = TraiggerSinkSource(localizedName: "Traigger · entrada", streamID: UUID(uuidString: "7D8B5C40-9E3F-4A01-BC2D-3E4F5A6B7C8D")!, format: streamFormat, device: self)
        // Vídeo desde el iPhone/iPad por la Wi-Fi (Traigger abierto allí, con «Enviar al Mac»).
        phone = PhoneReceiver(pool: pool, width: Int(width), height: Int(height)) { [weak self] pixels in self?.receivedPixels(pixels) }
        phone?.start()
        do {
            try device.addStream(source.stream)
            try device.addStream(sink.stream)
        } catch { log.error("No se pudieron añadir los flujos: \(error.localizedDescription)") }
    }

    var availableProperties: Set<CMIOExtensionProperty> { [.deviceTransportType, .deviceModel] }
    func deviceProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionDeviceProperties {
        let result = CMIOExtensionDeviceProperties(dictionary: [:])
        if properties.contains(.deviceTransportType) { result.transportType = kIOAudioDeviceTransportTypeVirtual }
        if properties.contains(.deviceModel) { result.model = "Traigger" }
        return result
    }
    func setDeviceProperties(_ deviceProperties: CMIOExtensionDeviceProperties) throws {}

    // Imagen recibida por la entrada (la llama TraiggerSinkSource).
    func received(_ buffer: CMSampleBuffer) {
        guard let pixels = CMSampleBufferGetImageBuffer(buffer) else { return }
        receivedPixels(pixels)
    }
    func receivedPixels(_ pixels: CVPixelBuffer) {
        queue.async {
            self.latest = pixels; self.latestTime = CACurrentMediaTime()
            if self.latestTime - self.lastSave > 5 { self.lastSave = self.latestTime; self.save(pixels) }
        }
    }

    func startStreaming() {
        queue.async {
            self.streamingClients += 1
            if self.timer == nil { self.startTimer() }
        }
    }
    private func startTimer() {
        let timer = DispatchSource.makeTimerSource(flags: .strict, queue: queue)
        timer.schedule(deadline: .now(), repeating: frameDuration.seconds, leeway: .milliseconds(1))
        timer.setEventHandler { [weak self] in self?.emit() }
        timer.resume()
        self.timer = timer
    }
    func stopStreaming() {
        queue.async {
            self.streamingClients = max(0, self.streamingClients - 1)
            if self.streamingClients == 0 { self.timer?.cancel(); self.timer = nil }
        }
    }

    private func emit() {
        // Si Traigger dejó de enviar, se queda la última imagen recibida (o la guardada de otra vez).
        guard let pixels = latest ?? waiting else { return }
        var timing = CMSampleTimingInfo(duration: frameDuration,
                                        presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()), decodeTimeStamp: .invalid)
        var description: CMFormatDescription?
        CMVideoFormatDescriptionCreateForImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: pixels, formatDescriptionOut: &description)
        var sample: CMSampleBuffer?
        guard let description, CMSampleBufferCreateReadyWithImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: pixels, formatDescription: description,
                                                                         sampleTiming: &timing, sampleBufferOut: &sample) == noErr, let sample else { return }
        source.stream.send(sample, discontinuity: [], hostTimeInNanoseconds: UInt64(timing.presentationTimeStamp.seconds * Double(NSEC_PER_SEC)))
    }

    // Última imagen guardada en el contenedor de la extensión (BGRA en bruto, 1280×720).
    private var savedURL: URL? {
        guard let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return nil }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder.appendingPathComponent("ultima-imagen-1080p.bgra")
    }
    private func save(_ pixels: CVPixelBuffer) {
        guard let url = savedURL, CVPixelBufferGetWidth(pixels) == Int(width), CVPixelBufferGetHeight(pixels) == Int(height) else { return }
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(pixels) else { return }
        let row = Int(width) * 4, stride = CVPixelBufferGetBytesPerRow(pixels)
        var data = Data(count: row * Int(height))
        data.withUnsafeMutableBytes { out in for y in 0..<Int(height) { memcpy(out.baseAddress! + y * row, base + y * stride, row) } }
        try? data.write(to: url, options: .atomic)
        waiting = nil // la próxima espera usará esta
    }
    private func loadSavedFrame() -> CVPixelBuffer? {
        guard let url = savedURL, let data = try? Data(contentsOf: url), data.count == Int(width) * Int(height) * 4 else { return nil }
        var buffer: CVPixelBuffer?
        CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &buffer)
        guard let buffer else { return nil }
        CVPixelBufferLockBaseAddress(buffer, [])
        let row = Int(width) * 4, stride = CVPixelBufferGetBytesPerRow(buffer), base = CVPixelBufferGetBaseAddress(buffer)!
        data.withUnsafeBytes { input in for y in 0..<Int(height) { memcpy(base + y * stride, input.baseAddress! + y * row, row) } }
        CVPixelBufferUnlockBaseAddress(buffer, [])
        return buffer
    }
    // Sin imagen guardada todavía: el fondo oscuro de Traigger.
    private func makePlainFrame() -> CVPixelBuffer? {
        var buffer: CVPixelBuffer?
        CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &buffer)
        guard let buffer else { return nil }
        CVPixelBufferLockBaseAddress(buffer, [])
        defer { CVPixelBufferUnlockBaseAddress(buffer, []) }
        guard let context = CGContext(data: CVPixelBufferGetBaseAddress(buffer), width: Int(width), height: Int(height), bitsPerComponent: 8,
                                      bytesPerRow: CVPixelBufferGetBytesPerRow(buffer), space: CGColorSpaceCreateDeviceRGB(),
                                      bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue) else { return buffer }
        context.setFillColor(CGColor(red: 0.07, green: 0.08, blue: 0.12, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: Int(width), height: Int(height)))
        return buffer
    }
}

// Salida: lo que ven las apps de videollamada.
final class TraiggerStreamSource: NSObject, CMIOExtensionStreamSource {
    private(set) var stream: CMIOExtensionStream!
    private let format: CMIOExtensionStreamFormat
    private unowned let device: TraiggerDeviceSource
    init(localizedName: String, streamID: UUID, format: CMIOExtensionStreamFormat, device: TraiggerDeviceSource) {
        self.format = format; self.device = device
        super.init()
        stream = CMIOExtensionStream(localizedName: localizedName, streamID: streamID, direction: .source, clockType: .hostTime, source: self)
    }
    var formats: [CMIOExtensionStreamFormat] { [format] }
    var availableProperties: Set<CMIOExtensionProperty> { [.streamActiveFormatIndex, .streamFrameDuration] }
    func streamProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionStreamProperties {
        let result = CMIOExtensionStreamProperties(dictionary: [:])
        if properties.contains(.streamActiveFormatIndex) { result.activeFormatIndex = 0 }
        if properties.contains(.streamFrameDuration) { result.frameDuration = device.frameDuration }
        return result
    }
    func setStreamProperties(_ streamProperties: CMIOExtensionStreamProperties) throws {
        if let duration = streamProperties.frameDuration, frameDurations.contains(duration) { device.frameDuration = duration }
    }
    func authorizedToStartStream(for client: CMIOExtensionClient) -> Bool { true }
    func startStream() throws { device.startStreaming() }
    func stopStream() throws { device.stopStreaming() }
}

// Entrada: Traigger para Mac encola aquí sus imágenes.
final class TraiggerSinkSource: NSObject, CMIOExtensionStreamSource {
    private(set) var stream: CMIOExtensionStream!
    private let format: CMIOExtensionStreamFormat
    private unowned let device: TraiggerDeviceSource
    private var client: CMIOExtensionClient?
    private var running = false
    init(localizedName: String, streamID: UUID, format: CMIOExtensionStreamFormat, device: TraiggerDeviceSource) {
        self.format = format; self.device = device
        super.init()
        stream = CMIOExtensionStream(localizedName: localizedName, streamID: streamID, direction: .sink, clockType: .hostTime, source: self)
    }
    var formats: [CMIOExtensionStreamFormat] { [format] }
    var availableProperties: Set<CMIOExtensionProperty> {
        [.streamActiveFormatIndex, .streamFrameDuration, .streamSinkBufferQueueSize, .streamSinkBuffersRequiredForStartup, .streamSinkBufferUnderrunCount, .streamSinkEndOfData]
    }
    func streamProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionStreamProperties {
        let result = CMIOExtensionStreamProperties(dictionary: [:])
        if properties.contains(.streamActiveFormatIndex) { result.activeFormatIndex = 0 }
        if properties.contains(.streamFrameDuration) { result.frameDuration = frameDurations[0] }
        if properties.contains(.streamSinkBufferQueueSize) { result.sinkBufferQueueSize = 2 }
        if properties.contains(.streamSinkBuffersRequiredForStartup) { result.sinkBuffersRequiredForStartup = 1 }
        return result
    }
    func setStreamProperties(_ streamProperties: CMIOExtensionStreamProperties) throws {}
    func authorizedToStartStream(for client: CMIOExtensionClient) -> Bool { self.client = client; return true }
    func startStream() throws { running = true; consume() }
    func stopStream() throws { running = false }

    private func consume() {
        guard running, let client else { return }
        stream.consumeSampleBuffer(from: client) { [weak self] buffer, sequence, _, _, _ in
            guard let self else { return }
            if let buffer {
                self.device.received(buffer)
                let output = CMIOExtensionScheduledOutput(sequenceNumber: sequence, hostTimeInNanoseconds: UInt64(CMClockGetTime(CMClockGetHostTimeClock()).seconds * Double(NSEC_PER_SEC)))
                self.stream.notifyScheduledOutputChanged(output)
            }
            // Siguiente imagen; si no hay ninguna, se reintenta en breve.
            if buffer == nil { DispatchQueue.global().asyncAfter(deadline: .now() + 0.01) { self.consume() } } else { self.consume() }
        }
    }
}

final class TraiggerProviderSource: NSObject, CMIOExtensionProviderSource {
    private(set) var provider: CMIOExtensionProvider!
    private var deviceSource: TraiggerDeviceSource!
    init(clientQueue: DispatchQueue?) {
        super.init()
        provider = CMIOExtensionProvider(source: self, clientQueue: clientQueue)
        deviceSource = TraiggerDeviceSource(localizedName: "Traigger")
        do { try provider.addDevice(deviceSource.device) } catch { log.error("No se pudo añadir la cámara: \(error.localizedDescription)") }
    }
    func connect(to client: CMIOExtensionClient) throws {}
    func disconnect(from client: CMIOExtensionClient) {}
    var availableProperties: Set<CMIOExtensionProperty> { [.providerManufacturer] }
    func providerProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionProviderProperties {
        let result = CMIOExtensionProviderProperties(dictionary: [:])
        if properties.contains(.providerManufacturer) { result.manufacturer = "Traigger" }
        return result
    }
    func setProviderProperties(_ providerProperties: CMIOExtensionProviderProperties) throws {}
}
