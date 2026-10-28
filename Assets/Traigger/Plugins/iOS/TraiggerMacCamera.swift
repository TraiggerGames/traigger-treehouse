import CoreMedia
import CoreVideo
import Foundation
import Network
import VideoToolbox
#if canImport(UIKit)
import UIKit
#endif

// Envía el vídeo del personaje a la cámara «Traigger» del Mac en H.264 por hardware, 1080p a 60 fps (formato en
// MacCamera/Shared/TraiggerVideo.swift). Dos caminos:
//  · Cable: se abre el puerto 47815 y «Traigger Camera» del Mac entra por el USB (usbmuxd). Si hay cable, manda.
//  · Wi-Fi: el Mac se encuentra solo por Bonjour («_traigger-cam._tcp», MacCamera/Extension/PhoneReceiver.swift). Mismas funciones que el plugin del Mac (TraiggerVCam…), así VirtualCameraOutput.cs sirve para los dos.
final class TraiggerMacCamera: @unchecked Sendable {
    static let shared = TraiggerMacCamera()
    private let queue = DispatchQueue(label: "traigger.maccamera")
    private let lock = NSLock()
    static let cablePort: UInt16 = 47815
    private var browser: NWBrowser?
    private var link: NWConnection?
    private var listener: NWListener?
    private var cable: NWConnection?
    private var cableReady = false
    // Sonido de la película: por el cable (mismo túnel) o por su propio canal de Wi-Fi hacia Traigger Camera.
    private var audioBrowser: NWBrowser?
    private var audioLink: NWConnection?
    private var audioReady = false
    // Codificador H.264: más calidad por cable (12 Mbps) que por Wi-Fi (6 Mbps), donde importa no perder fotogramas.
    private var encoder: VTCompressionSession?
    private var encoderSize = (0, 0)
    private var pool: CVPixelBufferPool?
    private var needsKeyFrame = true
    private var frameIndex: Int64 = 0
    private var bitrate = 0
    private var ready = false, busy = false
    private var macName = ""
    private var statusText = "Apagada"
    // Recuperación tras segundo plano o reposo: iOS cierra el puerto del cable, los sockets y el codificador, y el
    // buscador de Bonjour no vuelve a avisar si el Mac «sigue ahí». Un vigilante cada 2 s lo rehace todo solo.
    private var running = false
    private var watchdog: DispatchSourceTimer?
    private var busySince = Date.distantFuture
    private var linkWaitingSince: Date?
    private var observers: [NSObjectProtocol] = []
    var status: String { lock.withLock { statusText } }
    private func setStatus(_ value: String) { lock.withLock { statusText = value } }

    func start() -> Int32 {
        queue.async {
            guard !self.running else { return }
            self.running = true
            self.setStatus("Buscando tu Mac… (por cable o en la misma Wi-Fi)")
            TraiggerMovieSetAudioSink { samples, frames in TraiggerMacCamera.shared.sendAudio(samples, Int(frames)) }
            self.openNetwork()
            self.startWatchdog()
            self.observeApp()
        }
        return 1
    }

    private func openNetwork() {
        listenForCable()
        browseAudio()
        browseMac()
    }

    private func browseMac() {
        guard browser == nil else { return }
        let parameters = NWParameters.tcp
        parameters.includePeerToPeer = true
        let browser = NWBrowser(for: .bonjour(type: "_traigger-cam._tcp", domain: nil), using: parameters)
        browser.browseResultsChangedHandler = { [weak self] results, _ in self?.connectIfNeeded(results) }
        browser.stateUpdateHandler = { [weak self, weak browser] state in
            guard let self, let browser else { return }
            if case .failed = state { browser.cancel(); if self.browser === browser { self.browser = nil } }
        }
        browser.start(queue: queue)
        self.browser = browser
    }
    private func connectIfNeeded(_ results: Set<NWBrowser.Result>) {
        guard running, link == nil, let result = results.first else { return }
        if case .service(let name, _, _, _) = result.endpoint { macName = name }
        connect(result.endpoint)
    }

    // Vigilante: rehace lo que el sistema haya cerrado y suelta lo que se haya quedado atascado.
    private func startWatchdog() {
        guard watchdog == nil else { return }
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 2, repeating: 2)
        timer.setEventHandler { [weak self] in self?.check() }
        timer.resume()
        watchdog = timer
    }
    private func check() {
        guard running else { return }
        if let listener, listener.state == .cancelled || isFailed(listener.state) { listener.cancel(); self.listener = nil }
        if let audioBrowser, audioBrowser.state == .cancelled || isFailed(audioBrowser.state) { audioBrowser.cancel(); self.audioBrowser = nil }
        if let browser, browser.state == .cancelled || isFailed(browser.state) { browser.cancel(); self.browser = nil }
        // Una conexión «en espera» más de 4 s no va a volver: se cierra y se busca de nuevo.
        if let link, !ready {
            if linkWaitingSince == nil { linkWaitingSince = Date() }
            if Date().timeIntervalSince(linkWaitingSince!) > 4 { link.cancel(); self.link = nil; linkWaitingSince = nil }
        } else { linkWaitingSince = nil }
        // Un envío que no termina en 2 s (socket muerto en reposo) no debe bloquear los siguientes fotogramas.
        if busy, Date().timeIntervalSince(busySince) > 2 { busy = false; needsKeyFrame = true }
        openNetwork()
        if link == nil, let results = browser?.browseResults, !results.isEmpty { connectIfNeeded(results) }
        if !cableReady && !ready { setStatus(link == nil ? "Buscando tu Mac… (por cable o en la misma Wi-Fi)" : "Conectando con tu Mac…") }
    }
    private func isFailed(_ state: NWListener.State) -> Bool { if case .failed = state { return true }; return false }
    private func isFailed(_ state: NWBrowser.State) -> Bool { if case .failed = state { return true }; return false }

    // Al volver a primer plano se rehace todo de cero: es más rápido que esperar a que cada pieza note el corte.
    private func observeApp() {
        #if canImport(UIKit)
        guard observers.isEmpty else { return }
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: UIApplication.willEnterForegroundNotification, object: nil, queue: nil) { [weak self] _ in
            self?.queue.async { self?.reset("Volviendo a conectar con tu Mac…") }
        })
        observers.append(center.addObserver(forName: UIApplication.didEnterBackgroundNotification, object: nil, queue: nil) { [weak self] _ in
            self?.queue.async { self?.setStatus("En pausa (la app está en segundo plano)") }
        })
        #endif
    }
    private func reset(_ message: String) {
        guard running else { return }
        closeNetwork()
        if let encoder { VTCompressionSessionInvalidate(encoder) }
        encoder = nil; needsKeyFrame = true
        setStatus(message)
        openNetwork()
    }
    private func closeNetwork() {
        audioBrowser?.cancel(); audioBrowser = nil; audioLink?.cancel(); audioLink = nil; audioReady = false
        listener?.cancel(); listener = nil
        cable?.cancel(); cable = nil; cableReady = false
        browser?.cancel(); browser = nil
        link?.cancel(); link = nil; ready = false; busy = false; linkWaitingSince = nil
    }

    private func connect(_ endpoint: NWEndpoint) {
        let parameters = NWParameters.tcp
        parameters.includePeerToPeer = true
        let link = NWConnection(to: endpoint, using: parameters)
        link.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready: self.ready = true; self.needsKeyFrame = true; if !self.cableReady { self.setStatus("Enviando a «Traigger» en \(self.macName) (Wi-Fi)") }
            case .failed, .waiting:
                if case .waiting = state { return }  // lo resuelve el vigilante si dura
                link.cancel()
            case .cancelled:
                guard self.link === link else { return }
                self.ready = false; self.busy = false; self.link = nil
                if self.running { self.setStatus("Conexión perdida; buscando tu Mac…") }
            default: break
            }
        }
        link.start(queue: queue)
        self.link = link
    }

    // El Mac se conecta a este puerto a través del cable USB; solo se acepta una conexión a la vez.
    private func listenForCable() {
        guard listener == nil, let port = NWEndpoint.Port(rawValue: Self.cablePort), let listener = try? NWListener(using: .tcp, on: port) else { return }
        listener.newConnectionHandler = { [weak self] connection in
            guard let self else { return }
            self.cable?.cancel()
            self.cable = connection
            connection.stateUpdateHandler = { [weak self] state in
                guard let self else { return }
                switch state {
                case .ready: self.cableReady = true; self.busy = false; self.needsKeyFrame = true; self.setStatus("Enviando por cable a «Traigger» en el Mac")
                case .failed, .cancelled:
                    if self.cable === connection { self.cable = nil; self.cableReady = false; self.busy = false }
                    self.setStatus(self.ready ? "Enviando a «Traigger» en \(self.macName) (Wi-Fi)" : "Buscando tu Mac… (por cable o en la misma Wi-Fi)")
                default: break
                }
            }
            connection.start(queue: self.queue)
        }
        listener.stateUpdateHandler = { [weak self, weak listener] state in
            guard let self, let listener else { return }
            if case .failed = state { listener.cancel(); if self.listener === listener { self.listener = nil } }
        }
        listener.start(queue: queue)
        self.listener = listener
    }

    private func browseAudio() {
        guard audioBrowser == nil else {
            if audioLink == nil, let result = audioBrowser?.browseResults.first { connectAudio(result.endpoint) }
            return
        }
        let browser = NWBrowser(for: .bonjour(type: "_traigger-audio._tcp", domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self, self.audioLink == nil, let result = results.first else { return }
            self.connectAudio(result.endpoint)
        }
        browser.start(queue: queue)
        audioBrowser = browser
    }
    private func connectAudio(_ endpoint: NWEndpoint) {
        let link = NWConnection(to: endpoint, using: .tcp)
        link.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            if case .ready = state { self.audioReady = true }
            if case .failed = state { link.cancel() }
            if case .cancelled = state, self.audioLink === link { self.audioReady = false; self.audioLink = nil }
        }
        link.start(queue: self.queue)
        self.audioLink = link
    }

    // Hilo de audio de VLC: estéreo 16 bits a 48 kHz → [tamaño][3][muestras].
    func sendAudio(_ samples: UnsafePointer<Int16>, _ frames: Int) {
        var body = Data([3]); body.append(UnsafeBufferPointer(start: samples, count: frames * 2))
        var size = UInt32(body.count).bigEndian
        var packet = Data(bytes: &size, count: 4); packet.append(body)
        queue.async {
            let target = self.cableReady ? self.cable : (self.audioReady ? self.audioLink : nil)
            target?.send(content: packet, completion: .idempotent)
        }
    }

    func stop() {
        queue.async {
            self.running = false
            self.watchdog?.cancel(); self.watchdog = nil
            self.observers.forEach(NotificationCenter.default.removeObserver); self.observers = []
            self.closeNetwork()
            self.setStatus("Apagada")
        }
    }

    // Píxeles BGRA de Unity (fila 0 = abajo). Se copian al derecho en un CVPixelBuffer y se codifican en H.264.
    // Si la imagen anterior aún se está enviando, esta se descarta (mejor saltar un fotograma que acumular retraso).
    func send(_ pixels: UnsafeRawPointer, _ width: Int, _ height: Int) -> Int32 {
        let accepted: Bool = queue.sync {
            guard cableReady || (ready && link != nil), !busy else { return false }
            busy = true; busySince = Date(); return true
        }
        guard accepted else { return 0 }
        guard let buffer = makeBuffer(pixels, width, height) else { queue.async { self.busy = false }; return 0 }
        queue.async { self.encode(buffer, width, height) }
        return 1
    }

    private func makeBuffer(_ pixels: UnsafeRawPointer, _ width: Int, _ height: Int) -> CVPixelBuffer? {
        if pool == nil || encoderSize != (width, height) {
            let attributes: [String: Any] = [kCVPixelBufferWidthKey as String: width, kCVPixelBufferHeightKey as String: height,
                                             kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                                             kCVPixelBufferIOSurfacePropertiesKey as String: [:]]
            pool = nil; CVPixelBufferPoolCreate(kCFAllocatorDefault, nil, attributes as CFDictionary, &pool)
        }
        var buffer: CVPixelBuffer?
        guard let pool, CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &buffer) == kCVReturnSuccess, let buffer else { return nil }
        CVPixelBufferLockBaseAddress(buffer, [])
        let destination = CVPixelBufferGetBaseAddress(buffer)!, stride = CVPixelBufferGetBytesPerRow(buffer), row = width * 4
        for y in 0..<height { memcpy(destination + y * stride, pixels + (height - 1 - y) * row, row) }
        CVPixelBufferUnlockBaseAddress(buffer, [])
        return buffer
    }

    private func encode(_ buffer: CVPixelBuffer, _ width: Int, _ height: Int) {
        if encoder == nil || encoderSize != (width, height) {
            if let encoder { VTCompressionSessionInvalidate(encoder) }
            encoder = nil; encoderSize = (width, height); bitrate = 0
            let specification = [kVTVideoEncoderSpecification_EnableLowLatencyRateControl: true] as CFDictionary
            VTCompressionSessionCreate(allocator: nil, width: Int32(width), height: Int32(height), codecType: kCMVideoCodecType_H264,
                                       encoderSpecification: specification, imageBufferAttributes: nil, compressedDataAllocator: nil,
                                       outputCallback: nil, refcon: nil, compressionSessionOut: &encoder)
            guard let encoder else { busy = false; return }
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_RealTime, value: kCFBooleanTrue)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_ProfileLevel, value: kVTProfileLevel_H264_High_AutoLevel)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_AllowFrameReordering, value: kCFBooleanFalse)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_ExpectedFrameRate, value: 60 as CFNumber)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_MaxKeyFrameInterval, value: 120 as CFNumber)
            needsKeyFrame = true
        }
        guard let encoder else { busy = false; return }
        let wanted = cableReady ? 12_000_000 : 6_000_000
        if wanted != bitrate { bitrate = wanted; VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_AverageBitRate, value: wanted as CFNumber) }
        let properties = needsKeyFrame ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary : nil
        needsKeyFrame = false
        frameIndex += 1
        let status = VTCompressionSessionEncodeFrame(encoder, imageBuffer: buffer, presentationTimeStamp: CMTime(value: frameIndex, timescale: 60),
                                                     duration: CMTime(value: 1, timescale: 60), frameProperties: properties, infoFlagsOut: nil) { [weak self] status, _, sample in
            guard let self else { return }
            self.queue.async {
                guard status == noErr, let sample, let packet = Self.packet(sample) else { self.busy = false; return }
                let target = self.cableReady ? self.cable : self.link
                guard let target else { self.busy = false; return }
                target.send(content: packet, completion: .contentProcessed { [weak self] _ in self?.queue.async { self?.busy = false } })
            }
        }
        // Tras el reposo el codificador queda inválido (kVTInvalidSessionErr): se rehace en el siguiente fotograma.
        if status != noErr {
            busy = false
            VTCompressionSessionInvalidate(encoder); self.encoder = nil
        }
    }

    // [tamaño][tipo 2][clave?][SPS/PPS si es clave][fotograma AVCC]
    private static func packet(_ sample: CMSampleBuffer) -> Data? {
        guard let data = CMSampleBufferGetDataBuffer(sample) else { return nil }
        let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false) as? [[CFString: Any]]
        let key = !(attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool ?? false)
        var body = Data([2, key ? 1 : 0])
        if key, let format = CMSampleBufferGetFormatDescription(sample) {
            for index in 0..<2 {
                var pointer: UnsafePointer<UInt8>?, size = 0
                guard CMVideoFormatDescriptionGetH264ParameterSetAtIndex(format, parameterSetIndex: index, parameterSetPointerOut: &pointer,
                                                                         parameterSetSizeOut: &size, parameterSetCountOut: nil, nalUnitHeaderLengthOut: nil) == noErr,
                      let pointer else { return nil }
                body.append(contentsOf: [UInt8(size >> 8), UInt8(size & 0xff)]); body.append(pointer, count: size)
            }
        }
        var length = 0, pointer: UnsafeMutablePointer<CChar>?
        guard CMBlockBufferGetDataPointer(data, atOffset: 0, lengthAtOffsetOut: nil, totalLengthOut: &length, dataPointerOut: &pointer) == noErr, let pointer else { return nil }
        body.append(UnsafeRawPointer(pointer).assumingMemoryBound(to: UInt8.self), count: length)
        var size = UInt32(body.count).bigEndian
        var packet = Data(bytes: &size, count: 4); packet.append(body)
        return packet
    }
}

@_cdecl("TraiggerVCamStart") public func TraiggerVCamStart() -> Int32 { TraiggerMacCamera.shared.start() }
@_cdecl("TraiggerVCamStop") public func TraiggerVCamStop() { TraiggerMacCamera.shared.stop() }
@_cdecl("TraiggerVCamSend") public func TraiggerVCamSend(_ pixels: UnsafeRawPointer, _ width: Int32, _ height: Int32) -> Int32 {
    TraiggerMacCamera.shared.send(pixels, Int(width), Int(height))
}
@_cdecl("TraiggerVCamStatus") public func TraiggerVCamStatus() -> UnsafeMutablePointer<CChar>? { strdup(TraiggerMacCamera.shared.status) }

// Del reproductor de películas (Plugins/Apple/TraiggerMoviePlayer.m).
@_silgen_name("TraiggerMovieSetAudioSink") func TraiggerMovieSetAudioSink(_ sink: @convention(c) (UnsafePointer<Int16>, Int32) -> Void)
