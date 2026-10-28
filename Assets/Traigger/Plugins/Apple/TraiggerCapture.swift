#if !os(visionOS)
import AVFoundation
import CoreMedia
import Foundation
import Network
import VideoToolbox
#if os(iOS)
import UIKit
#if canImport(MediaPipeTasksVision)
import MediaPipeTasksVision
#endif
#endif

// Entrada HDMI de Traigger: capturadoras USB (UVC) como cámaras externas, igual que hace Orion en el iPad
// (iPadOS 17+ y macOS). El vídeo queda en memoria para Unity (fondo detrás de Traigger, ver CaptureBackground.cs),
// el sonido suena aquí y, si se pide, ambos se envían a las Vision Pro por Wi-Fi (H.264 + PCM, mismo formato que la
// cámara del Mac: [tamaño][tipo][datos], tipo 2 vídeo y 3 sonido; Bonjour «_traigger-hdmi._tcp»).
// Decisiones de la sesión local, comprobables sin hardware ni permisos de cámara.
enum TraiggerCapturePolicy {
    enum Action: Equatable { case wait, keepRunning, stopUSB, retryUSB }
    static func supportsPair(sets: [[String]], usb: String, front: String) -> Bool { sets.contains { $0.contains(usb) && $0.contains(front) } }
    static func probeStalled(age: Double, usb: Int, front: Int, usbAge: Double, frontAge: Double) -> Bool { age > 8 && (usb == 0 || front == 0 || usbAge > 3 || frontAge > 3) }
    static func fullFace(sending: Bool, explicitPriority: Bool) -> Bool { explicitPriority }
    static func action(fullFace: Bool, age: Double, silent: Double, conflict: Bool, retries: Int, backgroundPaused: Bool = false) -> Action {
        if backgroundPaused { return .wait }
        if fullFace { if age <= 3 { return .wait }; return conflict || silent > 2.5 ? .stopUSB : .keepRunning }
        return silent > 2.5 + Double(min(retries, 5)) * 2 ? .retryUSB : .keepRunning
    }
}

final class TraiggerCapture: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate, AVCaptureAudioDataOutputSampleBufferDelegate, @unchecked Sendable {
    static let shared = TraiggerCapture()
    private let queue = DispatchQueue(label: "traigger.capture", qos: .userInteractive)
    private let lock = NSLock()
    private var session: AVCaptureSession?
    private var videoOutput: AVCaptureVideoDataOutput?
    private var latest: CVPixelBuffer?
    private var serial: Int64 = -1
    private var statusText = "Sin capturadora"
    private var audioEngine: AVAudioEngine?
    private var player: AVAudioPlayerNode?
    private let playFormat = AVAudioFormat(standardFormatWithSampleRate: 48000, channels: 2)!
    private var converter: AVAudioConverter?
    var status: String { lock.withLock { statusText } }
    private func setStatus(_ value: String) { lock.withLock { statusText = value } }

    // Protección contra bloqueos: si deja de llegar imagen, si iOS da un error o interrumpe la sesión, o si se
    // desconecta el cable, la captura se recupera sola (y vuelve en cuanto se enchufa otra vez).
    private var wanted: (id: String, audio: Bool)?
    // Modo local: no sustituir ARKit por Vision. Si hay conflicto, se detiene USB y se conserva la cara.
    private var hybridActive = false
    private var probeActive = false
    private var probeStarted = Date.distantPast
    private var probeUSB = 0, probeFront = 0
    private var probeUSBAge = Date.distantPast, probeFrontAge = Date.distantPast
    private var probePreviousUSB = 0, probePreviousFront = 0
    private var probeReportAt = Date.distantPast
    private var probeFormats = ""
    private var localFullFace = false
    private var backgroundPaused = false
    private var appBackground = false
    private var blockedForFace = false
    private var startGeneration = 0
    private var captureStarted = Date.distantPast
    private var lastFrame = Date.distantPast
    private var frameLogAt = Date.distantPast
    private var loggedFrames = 0
    private var restarts = 0
    private var watchdog: DispatchSourceTimer?
    private var observers: [NSObjectProtocol] = []
    private func protect() {
        guard watchdog == nil else { return }
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 2, repeating: 1)
        timer.setEventHandler { [weak self] in self?.check() }
        timer.resume(); watchdog = timer
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: AVCaptureSession.runtimeErrorNotification, object: nil, queue: nil) { [weak self] note in
            let error = note.userInfo?[AVCaptureSessionErrorKey] as? NSError
            self?.queue.async {
                guard let self, let current = self.session, note.object as AnyObject? === current else { return }
                #if os(iOS)
                if self.probeActive { self.failProbe("Error de sesión: \(error?.domain ?? "?") / \(error?.code ?? 0): \(error?.localizedDescription ?? "?")"); return }
                #endif
                if self.localFullFace { self.suspendForFace("Error USB: \(error?.localizedDescription ?? "desconocido")") }
                else { self.recover("Error de la capturadora; recuperando…") }
            }
        })
        #if os(iOS)
        observers.append(center.addObserver(forName: AVCaptureSession.wasInterruptedNotification, object: nil, queue: nil) { [weak self] note in
            let reason = (note.userInfo?[AVCaptureSessionInterruptionReasonKey] as? NSNumber).flatMap { AVCaptureSession.InterruptionReason(rawValue: $0.intValue) }
            self?.queue.async {
                guard let self, let current = self.session, note.object as AnyObject? === current else { return }
                NSLog("Traigger: interrupción de nuestra captura USB, motivo %d", reason?.rawValue ?? -1)
                if reason == .videoDeviceNotAvailableInBackground {
                    self.backgroundPaused = true
                    self.setStatus("Captura en pausa temporal de segundo plano; se reanuda al volver. No es un conflicto de ARKit.")
                    return // Conservar la sesión y startRunning: AVFoundation la reanuda al terminar la interrupción.
                }
                if self.probeActive { self.failProbe("Interrupción de sesión, motivo \(reason?.rawValue ?? -1)"); return }
                if self.localFullFace { self.suspendForFace("iPadOS interrumpió USB (motivo \(reason?.rawValue ?? -1))") }
                else { self.setStatus("Captura en pausa (motivo \(reason?.rawValue ?? -1)); se reanuda sola") }
            }
        })
        observers.append(center.addObserver(forName: AVCaptureSession.interruptionEndedNotification, object: nil, queue: nil) { [weak self] note in
            self?.queue.async {
                guard let self, let current = self.session, note.object as AnyObject? === current, self.backgroundPaused else { return }
                self.backgroundPaused = false
                self.resetForegroundGrace()
                self.setStatus("Interrupción finalizada; esperando nuevos fotogramas · " + self.priorityLabel)
            }
        })
        observers.append(center.addObserver(forName: UIApplication.didEnterBackgroundNotification, object: nil, queue: .main) { [weak self] _ in
            self?.queue.async { self?.appBackground = true }
        })
        observers.append(center.addObserver(forName: UIApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            let scenes = UIApplication.shared.connectedScenes.map { "\($0.session.persistentIdentifier):\($0.activationState.rawValue)" }.joined(separator: ", ")
            NSLog("Traigger: aplicación activa; escenas %@", scenes)
            self?.queue.async {
                guard let self else { return }; self.appBackground = false
                // La notificación de fin de interrupción, o un fotograma real, confirma la reanudación.
                if self.session != nil { self.resetForegroundGrace() }
            }
        })
        #endif
        observers.append(center.addObserver(forName: AVCaptureDevice.wasDisconnectedNotification, object: nil, queue: nil) { [weak self] note in
            guard let device = note.object as? AVCaptureDevice, device.hasMediaType(.video) else { return }
            self?.queue.async { guard let self, self.wanted != nil, self.session != nil else { return }
                if Self.devices().isEmpty { self.stopSession(); self.setStatus("Capturadora desconectada: vuelve sola al enchufarla") } }
        })
        observers.append(center.addObserver(forName: AVCaptureDevice.wasConnectedNotification, object: nil, queue: nil) { [weak self] _ in
            self?.queue.async { guard let self, !self.blockedForFace, self.wanted != nil, self.session == nil else { return }; self.restarts = 0; self.recover("Capturadora conectada; arrancando…") }
        })
    }
    private func check() {
        guard let wanted, let session else { return }
        #if os(iOS)
        if probeActive {
            if !backgroundPaused && !appBackground { reportProbe() }
            return
        }
        #endif
        var conflict = false
        #if os(iOS)
        conflict = localFullFace && TraiggerFaceCaptureConflict() != 0
        #endif
        switch TraiggerCapturePolicy.action(fullFace: localFullFace, age: Date().timeIntervalSince(captureStarted), silent: Date().timeIntervalSince(lastFrame), conflict: conflict, retries: restarts, backgroundPaused: backgroundPaused || appBackground) {
        case .wait: return
        case .stopUSB:
            suspendForFace(conflict ? "ARKit se interrumpió o dejó de recibir cámara al activar USB" : "La capturadora dejó de entregar vídeo"); return
        case .retryUSB:
            if !Self.devices().isEmpty { NSLog("Traigger: USB sin imagen; reinicio %d", restarts + 1); _ = wanted; recover("La capturadora no daba imagen; reiniciando…") }
        case .keepRunning:
            if localFullFace { setStatus("Capturadora activa · prioridad de expresiones completas · sin conflicto detectado") }
        }
    }
    private var priorityLabel: String { localFullFace ? "prioridad expresiones completas" : "prioridad vídeo USB; ARKit local en pausa" }
    private func resetForegroundGrace() {
        captureStarted = Date(); lastFrame = Date()
        if probeActive { probeStarted = Date(); probeUSBAge = Date(); probeFrontAge = Date(); probeReportAt = Date(); probePreviousUSB = probeUSB; probePreviousFront = probeFront }
        #if os(iOS)
        if localFullFace { TraiggerFaceForegroundGrace() }
        #endif
    }
    private func recover(_ message: String) {
        guard let wanted else { return }
        restarts += 1
        setStatus(message)
        startLocked(wanted.id, audio: wanted.audio)
    }

    // Capturadoras conectadas (cámaras externas).
    static func devices() -> [AVCaptureDevice] {
        #if os(iOS)
        guard #available(iOS 17.0, *) else { return [] }
        return AVCaptureDevice.DiscoverySession(deviceTypes: [.external], mediaType: .video, position: .unspecified).devices
        #else
        if #available(macOS 14.0, *) { return AVCaptureDevice.DiscoverySession(deviceTypes: [.external], mediaType: .video, position: .unspecified).devices }
        return AVCaptureDevice.DiscoverySession(deviceTypes: [.externalUnknown], mediaType: .video, position: .unspecified).devices
        #endif
    }

    func start(_ identifier: String, audio: Bool) {
        queue.async {
            self.wanted = (identifier, audio); self.restarts = 0
            self.protect()
            self.startLocked(identifier, audio: audio)
        }
    }
    private func startLocked(_ identifier: String, audio: Bool) {
        startGeneration += 1; let generation = startGeneration
        blockedForFace = false; stopSession()
        #if os(iOS)
        let sender = TraiggerHDMISender.shared
        hybridActive = UserDefaults.standard.bool(forKey: "traigger.hdmi.hybridFace")
        probeActive = hybridActive || UserDefaults.standard.bool(forKey: "traigger.hdmi.multicamProbe")
        localFullFace = !probeActive && TraiggerCapturePolicy.fullFace(sending: sender.sending, explicitPriority: UserDefaults.standard.bool(forKey: "traigger.hdmi.forceFaceID"))
        // Pausar/reanudar la frontal en el hilo principal ANTES de abrir USB, sin bloquear la interfaz.
        setStatus("Aplicando " + priorityLabel + "…")
        let fullFace = localFullFace
        DispatchQueue.main.async {
            TraiggerFaceCapturePriority(fullFace ? 0 : 1)
            self.queue.async {
                guard generation == self.startGeneration, self.wanted != nil else { return }
                self.openSession(identifier, audio: audio)
            }
        }
        #else
        openSession(identifier, audio: audio)
        #endif
    }
    private func openSession(_ identifier: String, audio: Bool) {
        do {
            guard let device = Self.devices().first(where: { $0.uniqueID == identifier }) ?? Self.devices().first else { self.setStatus("No hay ninguna capturadora conectada"); return }
            #if os(iOS)
            let sender = TraiggerHDMISender.shared
            if probeActive {
                guard !sender.sending else { failProbe("Desactiva Enviar a Vision Pro antes de probar"); return }
                if #available(iOS 17.0, *) { startProbe(device, audio: audio) }
                else { failProbe("Requiere iPadOS 17 o posterior") }
                return
            }
            NSLog("Traigger: captura · modo %@ · dispositivo %@ · sistema %@", localFullFace ? "iPad local / ARKit completo" : "envío", device.localizedName, ProcessInfo.processInfo.operatingSystemVersionString)
            if !localFullFace, sender.quality.faceWhileSending == true, let multi = self.makeMultiCam(device, audio: audio) {
                self.session = multi; self.lastFrame = Date()
                self.setStatus("Capturando «\(device.localizedName)» · seguimiento aproximado con Vision")
                return
            }
            #endif
            let session = AVCaptureSession()
            session.beginConfiguration()
            #if os(iOS)
            session.sessionPreset = .inputPriority // respeta el modo elegido en chooseFormat
            #endif
            guard let input = try? AVCaptureDeviceInput(device: device), session.canAddInput(input) else {
                self.setStatus("No se puede abrir «\(device.localizedName)»"); session.commitConfiguration(); return
            }
            session.addInput(input)
            self.chooseFormat(device, localFace: self.localFullFace)
            let video = AVCaptureVideoDataOutput()
            video.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: self.pixelFormat]
            video.alwaysDiscardsLateVideoFrames = true
            video.setSampleBufferDelegate(self, queue: self.queue)
            guard session.canAddOutput(video) else { session.commitConfiguration(); self.setStatus("No se puede abrir la salida de vídeo USB"); return }
            session.addOutput(video); self.videoOutput = video
            if audio { self.addAudio(to: session, near: device) }
            #if os(iOS)
            // Multitarea no garantiza convivencia entre sesiones; se consulta sin usarla como prueba de compatibilidad.
            if localFullFace, #available(iOS 16.0, *) {
                NSLog("Traigger: experimento Face ID: acceso multitarea %@", session.isMultitaskingCameraAccessSupported ? "admitido" : "NO admitido")
                if session.isMultitaskingCameraAccessSupported { session.isMultitaskingCameraAccessEnabled = true }
            }
            #endif
            session.commitConfiguration()
            #if os(iOS)
            if localFullFace { TraiggerFaceLocalProbe(1) }
            #endif
            session.startRunning()
            self.session = session
            // No reiniciar ARKit después de USB: evita que ambas sesiones compitan por turnos.
            self.lastFrame = Date(); self.captureStarted = Date()
            self.setStatus("Capturando «\(device.localizedName)»" + (audio ? " con sonido" : "") + (self.localFullFace ? " · ARKit completo prioritario · comprobando coexistencia" : " · prioridad vídeo USB; ARKit local en pausa"))
        }
    }

    private func suspendForFace(_ reason: String) {
        blockedForFace = true // Conservar dispositivo para reconfigurar al cambiar prioridad; sin reintento automático.
        stopSession()
        setStatus("Capturadora detenida para conservar las expresiones completas. \(reason). Puedes reintentar o detener USB y continuar con ARKit.")
        NSLog("Traigger: prioridad ARKit · %@", reason)
        #if os(iOS)
        DispatchQueue.main.async { TraiggerFaceReleaseCapture() }
        #endif
    }

    func stop() { queue.async { self.startGeneration += 1; self.wanted = nil; self.blockedForFace = false; self.stopSession(); self.setStatus("Sin capturadora")
        #if os(iOS)
        DispatchQueue.main.async { TraiggerFaceReleaseCapture() }
        #endif
    } }
    // Al empezar o dejar de enviar a Vision Pro, la captura se rehace con el modo adecuado (latencia o cara).
    func reconfigure() { queue.async { guard let wanted = self.wanted else { return }; self.startLocked(wanted.id, audio: wanted.audio) } }

    func reconfigurePriority() {
        queue.async {
            guard let wanted = self.wanted else { return }
            let selected = UserDefaults.standard.bool(forKey: "traigger.hdmi.forceFaceID")
            let hybrid = UserDefaults.standard.bool(forKey: "traigger.hdmi.hybridFace")
            let probe = hybrid || UserDefaults.standard.bool(forKey: "traigger.hdmi.multicamProbe")
            guard (selected && !probe) != self.localFullFace || probe != self.probeActive || hybrid != self.hybridActive || self.blockedForFace else { return }
            self.startLocked(wanted.id, audio: wanted.audio)
        }
    }
    // Modo de la capturadora: el de más fps (hasta los pedidos) con el tamaño más cercano al pedido. El modo genérico
    // («high») suele quedarse en 30 fps, que para jugar se nota mucho.
    private func chooseFormat(_ device: AVCaptureDevice, multiCam: Bool = false, localFace: Bool = false) {
        let quality = TraiggerHDMISender.shared.quality
        let wantedHeight = localFace ? 720 : (quality.height > 0 ? quality.height : 2160)
        let wantedFPS = localFace ? 30 : max(1, quality.fps)
        // Orden: más fps (hasta los pedidos), luego sin compresión (MJPEG hay que descomprimirlo: más retraso) y luego el
        // tamaño más cercano al pedido. Para jugar, 720p a 60 fps es mejor que 1080p a 30.
        func fourcc(_ format: AVCaptureDevice.Format) -> String {
            let code = CMFormatDescriptionGetMediaSubType(format.formatDescription)
            return String(bytes: [24, 16, 8, 0].map { UInt8((code >> $0) & 0xff) }, encoding: .ascii) ?? "?"
        }
        func score(_ format: AVCaptureDevice.Format) -> (Double, Int, Int) {
            let fps = format.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0
            let height = Int(CMVideoFormatDescriptionGetDimensions(format.formatDescription).height)
            let compressed = ["dmb1", "jpeg", "mjpg", "avc1", "hvc1"].contains(fourcc(format).lowercased()) ? 0 : 1
            if localFace { return (-Double(abs(height - wantedHeight)), compressed, Int(min(fps, Double(wantedFPS)))) }
            return (min(fps, Double(wantedFPS)), compressed, -abs(height - wantedHeight))
        }
        NSLog("Traigger: modos de la capturadora: %@", device.formats.map { f -> String in
            let d = CMVideoFormatDescriptionGetDimensions(f.formatDescription)
            return "\(d.width)x\(d.height) \(fourcc(f)) \(Int(f.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0))fps"
        }.joined(separator: ", "))
        #if os(iOS)
        let formats = multiCam ? device.formats.filter(\.isMultiCamSupported) : device.formats
        #else
        let formats = device.formats
        #endif
        guard let best = formats.max(by: { score($0) < score($1) }), (try? device.lockForConfiguration()) != nil else { return }
        device.activeFormat = best
        let ranges = best.videoSupportedFrameRateRanges
        let range = ranges.min { abs($0.maxFrameRate - Double(wantedFPS)) < abs($1.maxFrameRate - Double(wantedFPS)) }
        let fps = max(range?.minFrameRate ?? 1, min(Double(wantedFPS), range?.maxFrameRate ?? 30))
        let duration = CMTime(value: 1, timescale: CMTimeScale(fps.rounded()))
        device.activeVideoMinFrameDuration = duration
        device.activeVideoMaxFrameDuration = duration
        device.unlockForConfiguration()
        let size = CMVideoFormatDescriptionGetDimensions(best.formatDescription)
        lock.withLock { mode = "\(size.width)×\(size.height) a \(Int(fps.rounded())) fps" }
        NSLog("Traigger: modo elegido %dx%d %@ a %.0f fps", size.width, size.height, fourcc(best), fps)
    }
    private var mode = ""
    var modeText: String { lock.withLock { mode } }
    // Enviando a Vision Pro, la imagen llega en YUV (el formato nativo del codificador): sin convertir a RGB, menos
    // retraso. Entonces no se dibuja de fondo en Unity (que necesita RGB); la ventana de vista previa sí funciona.
    private var pixelFormat: OSType { TraiggerHDMISender.shared.sending ? kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange : kCVPixelFormatType_32BGRA }
    // Sesión activa, para ver la capturadora en su propia ventana (AVCaptureVideoPreviewLayer, sin pasar por Unity).
    var previewSession: AVCaptureSession? { queue.sync { session } }
    #if os(iOS)
    private var probeFrontOutput: AVCaptureVideoDataOutput?
    private var faceOutput: AVCaptureVideoDataOutput?
    private let faceQueue = DispatchQueue(label: "traigger.capture.face", qos: .userInitiated)
    // Experimento aislado: una sola sesión, sin análisis facial, sonido ni codificación/envío.
    @available(iOS 17.0, *)
    private func startProbe(_ usb: AVCaptureDevice, audio: Bool) {
        lock.withLock { mode = "" }
        if hybridActive && !TraiggerHybridFace.available { failProbe("Falta MediaPipe en esta compilación; vuelve a exportar desde Unity"); return }
        guard AVCaptureMultiCamSession.isMultiCamSupported else { failProbe("Este aparato no admite MultiCam"); return }
        let discovery = AVCaptureDevice.DiscoverySession(deviceTypes: [.external, .builtInTrueDepthCamera, .builtInWideAngleCamera, .builtInUltraWideCamera], mediaType: .video, position: .unspecified)
        let fronts = discovery.devices.filter { $0.position == .front }
        let allowed = discovery.supportedMultiCamDeviceSets
        NSLog("Traigger MultiCam: dispositivos %@; conjuntos %@", discovery.devices.map { "\($0.localizedName) [\($0.uniqueID)]" }.joined(separator: "; "), allowed.map { $0.map(\.uniqueID).sorted().joined(separator: "+") }.joined(separator: "; "))
        guard let front = fronts.first(where: { candidate in TraiggerCapturePolicy.supportsPair(sets: allowed.map { $0.map(\.uniqueID) }, usb: usb.uniqueID, front: candidate.uniqueID) }) else {
            failProbe("iPadOS no anuncia una pareja compatible USB + frontal (\(fronts.count) frontales, \(allowed.count) conjuntos). No se ha intentado forzarla."); return
        }
        let multi = AVCaptureMultiCamSession()
        multi.beginConfiguration()
        var committed = false
        defer { if !committed { multi.commitConfiguration() } }
        do {
            let usbInput = try AVCaptureDeviceInput(device: usb)
            guard multi.canAddInput(usbInput) else { failProbe("No admite entrada USB"); return }; multi.addInputWithNoConnections(usbInput)
            let frontInput = try AVCaptureDeviceInput(device: front)
            guard multi.canAddInput(frontInput) else { failProbe("No admite frontal después de añadir USB"); return }; multi.addInputWithNoConnections(frontInput)
            guard let usbMode = configureProbeFormat(usb, targetHeight: 720), let frontMode = configureProbeFormat(front, targetHeight: 480) else { failProbe("Faltan formatos MultiCam o no se pudo configurar la cámara"); return }
            let video = AVCaptureVideoDataOutput(), frontal = AVCaptureVideoDataOutput()
            video.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA]
            frontal.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: hybridActive ? kCVPixelFormatType_32BGRA : kCVPixelFormatType_420YpCbCr8BiPlanarFullRange]
            for output in [video, frontal] {
                output.alwaysDiscardsLateVideoFrames = true
                guard multi.canAddOutput(output) else { failProbe("No admite ambas salidas de vídeo"); return }
                multi.addOutputWithNoConnections(output); output.setSampleBufferDelegate(self, queue: queue)
            }
            guard let up = usbInput.ports.first(where: { $0.mediaType == .video }), let fp = frontInput.ports.first(where: { $0.mediaType == .video }) else { failProbe("Faltan puertos de vídeo"); return }
            let frontConnection = AVCaptureConnection(inputPorts: [fp], output: frontal)
            for connection in [AVCaptureConnection(inputPorts: [up], output: video), frontConnection] {
                guard multi.canAddConnection(connection) else { failProbe("No admite las conexiones de vídeo"); return }; multi.addConnection(connection)
            }
            // Imagen derecha y sin espejo: los coeficientes Left/Right corresponden al usuario.
            let angle = AVCaptureDevice.RotationCoordinator(device: front, previewLayer: nil).videoRotationAngleForHorizonLevelCapture
            if frontConnection.isVideoRotationAngleSupported(angle) { frontConnection.videoRotationAngle = angle }
            if frontConnection.isVideoMirroringSupported { frontConnection.automaticallyAdjustsVideoMirroring = false; frontConnection.isVideoMirrored = false }
            multi.automaticallyConfiguresApplicationAudioSession = false
            if hybridActive && audio { addAudio(to: multi, near: usb) }
            if multi.isMultitaskingCameraAccessSupported { multi.isMultitaskingCameraAccessEnabled = true }
            multi.commitConfiguration(); committed = true
            if hybridActive && multi.hardwareCost > 1 {
                for limit in [20.0, 15.0] where multi.hardwareCost > 1 {
                    multi.beginConfiguration()
                    for device in [usb, front] {
                        if let range = device.activeFormat.videoSupportedFrameRateRanges.first(where: { $0.minFrameRate <= limit && $0.maxFrameRate >= limit }), (try? device.lockForConfiguration()) != nil {
                            let duration = CMTime(seconds: 1 / max(range.minFrameRate, limit), preferredTimescale: 60000)
                            device.activeVideoMinFrameDuration = duration; device.activeVideoMaxFrameDuration = duration
                            device.unlockForConfiguration()
                        }
                    }
                    multi.commitConfiguration()
                }
            }
            guard multi.hardwareCost <= 1 else { failProbe(String(format: "Presupuesto de hardware excedido: %.2f", multi.hardwareCost)); return }
            probeUSB = 0; probeFront = 0; probePreviousUSB = 0; probePreviousFront = 0
            probeStarted = Date(); probeReportAt = Date(); probeUSBAge = Date(); probeFrontAge = Date()
            probeFormats = "USB \(usbMode) [activo \(String(format: "%.1f", 1 / usb.activeVideoMinFrameDuration.seconds)) fps]; frontal \(frontMode) [activo \(String(format: "%.1f", 1 / front.activeVideoMinFrameDuration.seconds)) fps]; coste \(String(format: "%.2f", multi.hardwareCost))"
            session = multi; videoOutput = video; probeFrontOutput = frontal
            if hybridActive { TraiggerFaceHybridPriority(); TraiggerHybridFace.shared.start() }
            multi.startRunning()
            setStatus((hybridActive ? "Híbrido · esperando vídeo y cara · " : "EXPERIMENTAL · esperando ambos vídeos · ") + probeFormats)
        } catch { failProbe("No se pudieron abrir las entradas: \(error.localizedDescription)") }
    }
    private func configureProbeFormat(_ device: AVCaptureDevice, targetHeight: Int) -> String? {
        let formats = device.formats.filter { $0.isMultiCamSupported }
        // Primero frecuencia útil, después resolución: el formato más cercano podía limitar USB a 10 fps.
        let ranked = formats.sorted { a, b in
            let af = min(30, a.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0)
            let bf = min(30, b.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0)
            if af != bf { return af > bf }
            return abs(Int(CMVideoFormatDescriptionGetDimensions(a.formatDescription).height) - targetHeight) < abs(Int(CMVideoFormatDescriptionGetDimensions(b.formatDescription).height) - targetHeight)
        }
        guard let format = ranked.first,
              let range = format.videoSupportedFrameRateRanges.max(by: { min(30, $0.maxFrameRate) < min(30, $1.maxFrameRate) }) else { return nil }
        do {
            try device.lockForConfiguration(); defer { device.unlockForConfiguration() }
            device.activeFormat = format
            let fps = max(range.minFrameRate, min(30, range.maxFrameRate))
            let duration = CMTime(seconds: 1 / fps, preferredTimescale: 60000)
            device.activeVideoMinFrameDuration = duration; device.activeVideoMaxFrameDuration = duration
            let dimensions = CMVideoFormatDescriptionGetDimensions(format.formatDescription)
            return "\(dimensions.width)×\(dimensions.height) @\(String(format: "%.1f", fps))"
        } catch { return nil }
    }
    private func reportProbe() {
        let now = Date(), elapsed = max(0.001, now.timeIntervalSince(probeReportAt)), age = now.timeIntervalSince(probeStarted)
        let usbFPS = Double(probeUSB - probePreviousUSB) / elapsed, frontFPS = Double(probeFront - probePreviousFront) / elapsed
        let usbAge = now.timeIntervalSince(probeUSBAge), frontAge = now.timeIntervalSince(probeFrontAge)
        // Prioridad HDMI: si sólo se detiene la frontal, mantener el vídeo y retirar la cara antigua.
        // Si vuelven los fotogramas frontales, el analizador continúa sin reiniciar USB.
        if hybridActive && age > 8 && probeUSB > 0 && usbAge <= 3 && frontAge > 3 {
            TraiggerHybridFace.shared.invalidateFace()
            setStatus(String(format: "Híbrido · HDMI %.1f fps activo; frontal sin imagen hace %.1f s. Detén y vuelve a capturar si no se recupera.", usbFPS, frontAge))
            probePreviousUSB = probeUSB; probePreviousFront = probeFront; probeReportAt = now
            return
        }
        if TraiggerCapturePolicy.probeStalled(age: age, usb: probeUSB, front: probeFront, usbAge: usbAge, frontAge: frontAge) {
            failProbe("Flujo detenido: USB \(probeUSB) fotogramas (hace \(String(format: "%.1f", usbAge)) s); frontal \(probeFront) (hace \(String(format: "%.1f", frontAge)) s)"); return
        }
        let message = String(format: "EXPERIMENTAL · USB %.1f fps (%d); frontal %.1f fps (%d) · %.0f s · %@ · sin ARKit/análisis/audio", usbFPS, probeUSB, frontFPS, probeFront, age, probeFormats)
        let report = hybridActive ? message.replacingOccurrences(of: "EXPERIMENTAL", with: "Híbrido").replacingOccurrences(of: "sin ARKit/análisis/audio", with: TraiggerHybridFace.shared.status) : message
        setStatus(report); NSLog("Traigger MultiCam: %@", report)
        probePreviousUSB = probeUSB; probePreviousFront = probeFront; probeReportAt = now
    }
    private func failProbe(_ reason: String) {
        blockedForFace = true; stopSession()
        setStatus("EXPERIMENTAL · prueba detenida: " + reason)
        NSLog("Traigger MultiCam: prueba detenida: %@", reason)
        DispatchQueue.main.async { TraiggerFaceReleaseCapture() }
    }
    // Sesión multicámara: capturadora (todo su vídeo) + cámara frontal a baja resolución para Vision. Si el iPad no
    // admite la combinación o falta algo, devuelve nil y se usa la sesión normal.
    private func makeMultiCam(_ device: AVCaptureDevice, audio: Bool) -> AVCaptureSession? {
        // Face ID se pausa antes de pedir las cámaras: si sigue activo, iOS reparte las cámaras a tirones entre los dos.
        TraiggerFaceVisionPriority()
        guard AVCaptureMultiCamSession.isMultiCamSupported,
              let front = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .front),
              let externalInput = try? AVCaptureDeviceInput(device: device), let frontInput = try? AVCaptureDeviceInput(device: front) else { return nil }
        let session = AVCaptureMultiCamSession()
        session.beginConfiguration()
        var ok = false
        defer { if !ok { session.commitConfiguration() } }
        guard session.canAddInput(externalInput), session.canAddInput(frontInput) else { NSLog("Traigger: multicámara sin capturadora + frontal"); return nil }
        session.addInputWithNoConnections(externalInput); session.addInputWithNoConnections(frontInput)
        chooseFormat(device, multiCam: true)
        // Frontal: el formato multicámara más pequeño (para Vision basta) a 30 fps.
        if let small = front.formats.filter({ $0.isMultiCamSupported }).min(by: { CMVideoFormatDescriptionGetDimensions($0.formatDescription).width < CMVideoFormatDescriptionGetDimensions($1.formatDescription).width }),
           (try? front.lockForConfiguration()) != nil {
            // La frontal a la máxima frecuencia que permita ese formato (60 fps si se puede): menos tirones.
            let maxFps = min(60, small.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 30)
            front.activeFormat = small; front.activeVideoMinFrameDuration = CMTime(value: 1, timescale: CMTimeScale(maxFps)); front.unlockForConfiguration()
            NSLog("Traigger: cámara frontal para la cara a %.0f fps", maxFps)
        }
        let video = AVCaptureVideoDataOutput(), faces = AVCaptureVideoDataOutput()
        video.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: pixelFormat]
        faces.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarFullRange]
        video.alwaysDiscardsLateVideoFrames = true; faces.alwaysDiscardsLateVideoFrames = true
        video.setSampleBufferDelegate(self, queue: queue); faces.setSampleBufferDelegate(self, queue: faceQueue)
        guard session.canAddOutput(video), session.canAddOutput(faces) else { return nil }
        session.addOutputWithNoConnections(video); session.addOutputWithNoConnections(faces)
        guard let externalPort = externalInput.ports(for: .video, sourceDeviceType: device.deviceType, sourceDevicePosition: device.position).first,
              let frontPort = frontInput.ports(for: .video, sourceDeviceType: front.deviceType, sourceDevicePosition: .front).first else { return nil }
        let externalConnection = AVCaptureConnection(inputPorts: [externalPort], output: video)
        let frontConnection = AVCaptureConnection(inputPorts: [frontPort], output: faces)
        guard session.canAddConnection(externalConnection), session.canAddConnection(frontConnection) else { return nil }
        session.addConnection(externalConnection); session.addConnection(frontConnection)
        // La frontal se gira para que llegue derecha (Vision recibe la orientación «up»).
        if #available(iOS 17.0, *) {
            let angle = AVCaptureDevice.RotationCoordinator(device: front, previewLayer: nil).videoRotationAngleForHorizonLevelCapture
            if frontConnection.isVideoRotationAngleSupported(angle) { frontConnection.videoRotationAngle = angle }
        }
        if audio { addAudio(to: session, near: device) }
        guard session.hardwareCost <= 1 else { NSLog("Traigger: multicámara demasiado costosa (%.2f)", session.hardwareCost); return nil }
        session.commitConfiguration(); ok = true
        faceOutput = faces; videoOutput = video
        session.startRunning()
        TraiggerVisionFace.shared.status = "Siguiendo la cara con la cámara frontal (mientras se captura)"
        return session
    }
    #endif
    private func stopSession() {
        #if os(iOS)
        TraiggerHybridFace.shared.stop()
        TraiggerFaceLocalProbe(0)
        #endif
        // Desconectar delegados antes de liberar la sesión; ignorar también callbacks ya encolados.
        for output in session?.outputs ?? [] {
            if let video = output as? AVCaptureVideoDataOutput { video.setSampleBufferDelegate(nil, queue: nil) }
            if let audio = output as? AVCaptureAudioDataOutput { audio.setSampleBufferDelegate(nil, queue: nil) }
        }
        backgroundPaused = false
        videoOutput = nil; loggedFrames = 0; frameLogAt = Date.distantPast
        session?.stopRunning(); session = nil
        #if os(iOS)
        if faceOutput != nil { TraiggerFaceSetExternal(0) }
        faceOutput = nil; probeFrontOutput = nil
        #endif
        audioEngine?.stop(); audioEngine = nil; player = nil; converter = nil
        lock.withLock { latest = nil; serial = -1 }
    }

    // Sonido de la capturadora. En iPad es una entrada USB del sistema (solo cabe una a la vez: la voz de Traigger se
    // detiene mientras tanto); en el Mac, un dispositivo de audio con un nombre parecido al de la capturadora.
    private func addAudio(to session: AVCaptureSession, near device: AVCaptureDevice, routeReady: Bool = false) {
        #if os(iOS)
        if !routeReady {
        // La sesión de captura no debe tocar el audio del sistema: si lo hace, vuelve al micrófono del iPad y se pierde
        // la entrada USB de la capturadora.
        session.automaticallyConfiguresApplicationAudioSession = false
        let audioSession = AVAudioSession.sharedInstance()
        try? audioSession.setCategory(.playAndRecord, mode: .default, options: [.mixWithOthers, .defaultToSpeaker, .allowBluetoothA2DP])
        try? audioSession.setActive(true)
        let usb = audioSession.availableInputs?.first(where: { $0.portType == .usbAudio })
        if let usb { try? audioSession.setPreferredInput(usb) }
        // Elegir la entrada USB puede llevarse también la salida a la capturadora: el sonido vuelve al altavoz (o a los
        // auriculares si hay).
        let headphones = audioSession.currentRoute.outputs.contains { [.headphones, .bluetoothA2DP, .bluetoothLE, .airPlay].contains($0.portType) }
        if !headphones { try? audioSession.overrideOutputAudioPort(.speaker) }
        NSLog("Traigger: salida de sonido: %@", audioSession.currentRoute.outputs.map { "\($0.portName) (\($0.portType.rawValue))" }.joined(separator: ", "))
        NSLog("Traigger: entradas de sonido: %@ · elegida: %@", (audioSession.availableInputs ?? []).map { "\($0.portName) (\($0.portType.rawValue))" }.joined(separator: ", "), usb?.portName ?? "ninguna USB")
        if usb == nil { setStatus("La capturadora no ofrece sonido por USB: revisa que su audio HDMI esté activado") }
        // iOS tarda un momento en cambiar la ruta a la entrada USB: se espera (hasta 1,5 s). Nunca se usa el micrófono
        // del iPad como sonido de la capturadora: se oiría la habitación y se acoplaría con el altavoz.
        guard usb != nil else { NSLog("Traigger: la capturadora no tiene entrada de sonido USB"); return }
        // La ruta tarda en pasar a la USB (a veces varios segundos): el sonido se engancha en cuanto iOS la confirma.
        guard audioSession.currentRoute.inputs.contains(where: { $0.portType == .usbAudio }) else {
            NSLog("Traigger: esperando a que iOS pase el sonido a la entrada USB…")
            waitForUSB(session, device: device, attempts: 20)
            return
        }
        }
        let microphone = AVCaptureDevice.default(for: .audio)
        #else
        let types: [AVCaptureDevice.DeviceType]
        if #available(macOS 14.0, *) { types = [.microphone, .external] } else { types = [.builtInMicrophone, .externalUnknown] }
        let microphone = AVCaptureDevice.DiscoverySession(deviceTypes: types, mediaType: .audio, position: .unspecified).devices
            .first { $0.localizedName.localizedCaseInsensitiveContains(device.localizedName.components(separatedBy: " ").first ?? device.localizedName) }
        #endif
        guard let microphone, let input = try? AVCaptureDeviceInput(device: microphone), session.canAddInput(input) else { NSLog("Traigger: no se puede añadir la entrada de sonido"); return }
        NSLog("Traigger: sonido de la capturadora por «%@»", microphone.localizedName)
        let output = AVCaptureAudioDataOutput()
        #if os(iOS)
        // En la sesión multicámara las uniones son manuales también para el sonido.
        if let multi = session as? AVCaptureMultiCamSession {
            multi.addInputWithNoConnections(input)
            output.setSampleBufferDelegate(self, queue: queue)
            guard multi.canAddOutput(output) else { NSLog("Traigger: la multicámara no admite la salida de sonido"); return }
            multi.addOutputWithNoConnections(output)
            if let port = input.ports(for: .audio, sourceDeviceType: microphone.deviceType, sourceDevicePosition: microphone.position).first {
                let connection = AVCaptureConnection(inputPorts: [port], output: output)
                if multi.canAddConnection(connection) { multi.addConnection(connection) } else { NSLog("Traigger: la multicámara no une el sonido") }
            }
            startPlayer(); return
        }
        #endif
        session.addInput(input)
        #if os(macOS)
        output.audioSettings = [AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: 48000, AVNumberOfChannelsKey: 2,
                                AVLinearPCMBitDepthKey: 32, AVLinearPCMIsFloatKey: true, AVLinearPCMIsNonInterleaved: true]
        #endif
        output.setSampleBufferDelegate(self, queue: queue)
        if session.canAddOutput(output) { session.addOutput(output) } else { NSLog("Traigger: no se puede añadir la salida de sonido") }
        startPlayer()
    }
    private var audioBlocks = 0, audioLogAt = Date()
    private func waitForUSB(_ session: AVCaptureSession, device: AVCaptureDevice, attempts: Int) {
        queue.asyncAfter(deadline: .now() + 0.5) { [weak self, weak session] in
            guard let self, let session, session === self.session else { return }
            #if os(iOS)
            let route = AVAudioSession.sharedInstance().currentRoute.inputs
            if route.contains(where: { $0.portType == .usbAudio }) {
                NSLog("Traigger: entrada USB lista; añadiendo el sonido de la capturadora")
                // Ya con la ruta lista: se añade la entrada sin volver a tocar el audio del sistema (eso hacía saltar la
                // ruta y repetir el proceso en bucle).
                session.beginConfiguration(); self.addAudio(to: session, near: device, routeReady: true); session.commitConfiguration()
            } else if attempts > 0 {
                if attempts % 4 == 0, let usb = AVAudioSession.sharedInstance().availableInputs?.first(where: { $0.portType == .usbAudio }) { try? AVAudioSession.sharedInstance().setPreferredInput(usb) }
                self.waitForUSB(session, device: device, attempts: attempts - 1)
            } else {
                NSLog("Traigger: iOS no pasó el sonido a la entrada USB (sigue: %@)", route.map(\.portName).joined(separator: ", "))
                self.setStatus("Capturando sin sonido: iOS no deja usar la entrada de sonido de la capturadora")
            }
            #endif
        }
    }
    private var engineObserver: NSObjectProtocol?
    private func startPlayer() {
        let engine = AVAudioEngine(), node = AVAudioPlayerNode()
        engine.attach(node); engine.connect(node, to: engine.mainMixerNode, format: playFormat)
        try? engine.start(); node.play()
        audioEngine = engine; player = node
        // Al cambiar la ruta (p. ej. cuando iOS pasa la entrada a la USB) el motor se para solo: se vuelve a arrancar.
        if let engineObserver { NotificationCenter.default.removeObserver(engineObserver) }
        engineObserver = NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: nil) { [weak self] _ in
            self?.queue.async { self?.restartPlayer("cambio de ruta") }
        }
    }
    private func restartPlayer(_ reason: String) {
        guard let engine = audioEngine, let player else { return }
        if !engine.isRunning { do { try engine.start() } catch { NSLog("Traigger: no arranca el sonido: %@", error.localizedDescription); return } }
        if !player.isPlaying { player.play() }
        NSLog("Traigger: sonido de la capturadora rearrancado (%@)", reason)
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sample: CMSampleBuffer, from connection: AVCaptureConnection) {
        #if os(iOS)
        if output === probeFrontOutput {
            if CMSampleBufferGetImageBuffer(sample) != nil {
                probeFront += 1; probeFrontAge = Date()
                if hybridActive { TraiggerHybridFace.shared.process(sample) }
            }
            return
        }
        if output === faceOutput { TraiggerVisionFace.shared.process(sample); return }
        #endif
        if output is AVCaptureVideoDataOutput {
            guard output === videoOutput else { return }
            guard let pixels = CMSampleBufferGetImageBuffer(sample) else { return }
            lock.withLock { latest = pixels; serial += 1 }
            if probeActive { probeUSB += 1; probeUSBAge = Date() }
            if backgroundPaused { backgroundPaused = false; resetForegroundGrace(); setStatus("Captura reanudada · " + priorityLabel) }
            lastFrame = Date(); if restarts > 0 { restarts = 0 }
            loggedFrames += 1
            if Date().timeIntervalSince(frameLogAt) > 3 {
                NSLog("Traigger: entrada USB · %d fotogramas nuevos · prioridad %@", loggedFrames, localFullFace ? "ARKit" : "vídeo")
                frameLogAt = Date(); loggedFrames = 0
            }
            // Antigüedad del fotograma al salir de la capturadora (reloj del sistema): retraso de captura y descodificación.
            if !probeActive { TraiggerHDMISender.shared.send(pixels, age: CACurrentMediaTime() - CMSampleBufferGetPresentationTimeStamp(sample).seconds) }
        } else {
            audioBlocks += 1
            if Date().timeIntervalSince(audioLogAt) > 3 { NSLog("Traigger: sonido de la capturadora: %d bloques en 3 s", audioBlocks); audioBlocks = 0; audioLogAt = Date() }
            playAudio(sample)
        }
    }

    // Convierte el sonido a 48 kHz estéreo, lo reproduce y lo pasa a quien lo envía (Vision Pro y el Mac).
    private func playAudio(_ sample: CMSampleBuffer) {
        guard let player, let description = CMSampleBufferGetFormatDescription(sample),
              let streamDescription = CMAudioFormatDescriptionGetStreamBasicDescription(description) else { return }
        var asbd = streamDescription.pointee
        guard let source = AVAudioFormat(streamDescription: &asbd) else { return }
        let frames = AVAudioFrameCount(CMSampleBufferGetNumSamples(sample))
        guard let input = AVAudioPCMBuffer(pcmFormat: source, frameCapacity: frames) else { return }
        input.frameLength = frames
        guard CMSampleBufferCopyPCMDataIntoAudioBufferList(sample, at: 0, frameCount: Int32(frames), into: input.mutableAudioBufferList) == noErr else { return }
        if converter?.inputFormat != source { converter = AVAudioConverter(from: source, to: playFormat) }
        guard let converter, let output = AVAudioPCMBuffer(pcmFormat: playFormat, frameCapacity: AVAudioFrameCount(Double(frames) * 48000 / source.sampleRate) + 32) else { return }
        var given = false, error: NSError?
        converter.convert(to: output, error: &error) { _, state in if given { state.pointee = .noDataNow; return nil }; given = true; state.pointee = .haveData; return input }
        guard error == nil, output.frameLength > 0 else { return }
        // Mezcladora (ajustes en la pantalla «Mezcladora»): volumen en el altavoz y a qué destinos va.
        let defaults = UserDefaults.standard
        let local = defaults.object(forKey: "traigger.mix.capture.local") as? Bool ?? true
        player.volume = local ? Float(defaults.object(forKey: "traigger.mix.capture.volume") as? Double ?? 1) : 0
        if audioEngine?.isRunning != true || !player.isPlaying { restartPlayer("parado") }
        player.scheduleBuffer(output)
        if defaults.object(forKey: "traigger.mix.capture.stream") as? Bool ?? true { TraiggerStream.shared.mixer.push(.capture, output) }
        if defaults.object(forKey: "traigger.mix.capture.vision") as? Bool ?? true { TraiggerHDMISender.shared.send(audio: output) }
    }

    // Último fotograma para Unity (BGRA, filas de arriba abajo).
    func frameInfo(_ width: UnsafeMutablePointer<Int32>, _ height: UnsafeMutablePointer<Int32>) -> Int64 {
        lock.withLock {
            guard let latest else { width.pointee = 0; height.pointee = 0; return -1 }
            width.pointee = Int32(CVPixelBufferGetWidth(latest)); height.pointee = Int32(CVPixelBufferGetHeight(latest))
            return serial
        }
    }
    func copyFrame(_ destination: UnsafeMutablePointer<UInt8>, _ capacity: Int32) -> Int32 {
        guard let pixels = lock.withLock({ latest }), CVPixelBufferGetPixelFormatType(pixels) == kCVPixelFormatType_32BGRA else { return 0 }
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }
        let width = CVPixelBufferGetWidth(pixels), height = CVPixelBufferGetHeight(pixels), stride = CVPixelBufferGetBytesPerRow(pixels), row = width * 4
        guard Int(capacity) >= row * height, let base = CVPixelBufferGetBaseAddress(pixels) else { return 0 }
        for y in 0..<height { memcpy(destination + y * row, base + y * stride, row) }
        return 1
    }
}

// Envío de la entrada HDMI a las Vision Pro (TraiggerHDMIReceiver.swift en el host): H.264 por hardware y sonido PCM.
final class TraiggerHDMISender: @unchecked Sendable {
    static let shared = TraiggerHDMISender()
    // Calidad del envío. «Juego»: H.264 1080p60 con el codificador en baja latencia; HEVC da más calidad con la misma
    // tasa de bits a cambio de unos milisegundos más.
    struct Quality: Codable, Equatable {
        var codec = "h264"   // «h264» o «hevc»
        var height = 1080    // 720, 1080 o 0 (la de la fuente)
        var fps = 60
        var mbps = 15.0
        var keySeconds = 2.0
        // Camino hacia las Vision Pro: «router» (por defecto), «direct» (AWDL, como AirDrop) o «auto» (los dos con relevo).
        var path: String? = "router"
        var direct = false   // compatibilidad con ajustes antiguos
        // Seguir la cara con la cámara frontal también mientras se envía (multicámara: algo más de latencia).
        var faceWhileSending: Bool? = false
        // Experimento: intentar Face ID (ARKit) a la vez que la capturadora (acceso multitarea a la cámara).
        var forceFaceID: Bool? = false
        var multiCamProbe: Bool? = false
        var hybridFace: Bool? = false
        // Destino: «vision» (por defecto desde el iPad), «ipad» o «both» (los dos a la vez; lo usa el Mac).
        var destination: String? = nil
    }
    private(set) var quality: Quality = {
        if let data = UserDefaults.standard.data(forKey: "traigger.hdmi.quality"), let q = try? JSONDecoder().decode(Quality.self, from: data) { return q }
        return Quality()
    }()
    func setQuality(_ json: String) {
        guard var q = try? JSONDecoder().decode(Quality.self, from: Data(json.utf8)) else { return }
        let fields = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any]
        let hasPriority = fields?["forceFaceID"] is Bool
        if hasPriority, let priority = q.forceFaceID { UserDefaults.standard.set(priority, forKey: "traigger.hdmi.forceFaceID") }
        if fields?["multiCamProbe"] is Bool, let probe = q.multiCamProbe { UserDefaults.standard.set(probe, forKey: "traigger.hdmi.multicamProbe") }
        if fields?["hybridFace"] is Bool, let hybrid = q.hybridFace { UserDefaults.standard.set(hybrid, forKey: "traigger.hdmi.hybridFace") }
        q.hybridFace = UserDefaults.standard.bool(forKey: "traigger.hdmi.hybridFace")
        q.multiCamProbe = UserDefaults.standard.bool(forKey: "traigger.hdmi.multicamProbe")
        q.forceFaceID = UserDefaults.standard.bool(forKey: "traigger.hdmi.forceFaceID")
        UserDefaults.standard.set(try? JSONEncoder().encode(q), forKey: "traigger.hdmi.quality")
        let updated = q
        queue.async {
            let previous = self.quality
            let q = updated
            self.quality = q
            if let encoder = self.encoder { VTCompressionSessionInvalidate(encoder) }
            self.encoder = nil   // se rehace con la nueva calidad en el siguiente fotograma
            let faceChanged = previous.faceWhileSending != q.faceWhileSending || previous.forceFaceID != q.forceFaceID || previous.multiCamProbe != q.multiCamProbe || previous.hybridFace != q.hybridFace
            if previous.destination != q.destination {
                self.links.forEach { $0.stop() }; self.links = self.buildLinks()
                if self.enabled { self.links.forEach { $0.start() } }
            }
            self.links.forEach { $0.mode = TraiggerLink.Mode(rawValue: q.path ?? "router") ?? .router }
            if faceChanged { TraiggerCapture.shared.reconfigure() }
            else if hasPriority { TraiggerCapture.shared.reconfigurePriority() }
        }
    }
    // Transporte de doble camino (Wi-Fi de casa + conexión directa) con diagnóstico: TraiggerLink.swift.
    private let queue = DispatchQueue(label: "traigger.hdmi.send")
    // Un enlace por destino (iPad y/o Vision Pro): el mismo vídeo codificado una vez va a cada uno.
    private func makeLink(_ target: String?) -> TraiggerLink {
        // UDP: un paquete perdido no frena a los demás (con TCP, cada reenvío por la Wi-Fi metía picos de decenas de ms).
        let link = TraiggerLink(service: "_traigger-hdmiu._udp", queue: queue, transport: .udp)
        link.targetName = target
        link.onKeyRequest = { [weak self] in self?.queue.async { self?.needsKey = true } }
        link.mode = TraiggerLink.Mode(rawValue: quality.path ?? "router") ?? .router
        link.onSwitch = { [weak self] in self?.needsKey = true }   // al cambiar de camino, fotograma clave
        link.onStatus = { [weak self] text in self?.linkStatus = text }
        return link
    }
    private lazy var links: [TraiggerLink] = buildLinks()
    private func buildLinks() -> [TraiggerLink] {
        switch quality.destination ?? "vision" {
        case "ipad": [makeLink("iPad")]
        case "both": [makeLink("Vision Pro"), makeLink("iPad")]
        default: [makeLink("Vision Pro")]
        }
    }
    private var link: TraiggerLink { links[0] }
    private var anyReady: Bool { links.contains { $0.ready } }
    private var linkStatus = ""
    private var busy = false, needsKey = true
    private var encoder: VTCompressionSession?
    private var size = (0, 0)
    private var index: Int64 = 0
    private var enabled = false
    var latencyMs: Double { enabled ? link.diagnose().latencyMs : 0 }
    var status: String {
        guard enabled else { return "Sin enviar" }
        // Un renglón por destino (iPad y/o Vision Pro) con su veredicto y latencia.
        return links.map { link -> String in
            let d = link.diagnose(), name = link.targetName ?? "receptor"
            return d.latencyMs > 0 ? "\(name): \(d.verdict) · \(String(format: "%.0f ms", d.latencyMs))\n\(d.advice)" : "\(name): buscando… (abre Traigger en él)"
        }.joined(separator: "\n")
    }
    var diagnosis: TraiggerLink.Diagnosis? { enabled ? link.diagnose() : nil }

    var sending: Bool { enabledFlag.withLock { $0 } }
    private let enabledFlag = OSAllocatedUnfairLockShim(false)
    func setEnabled(_ on: Bool) {
        enabledFlag.withLock { $0 = on }
        queue.async {
            self.enabled = on
            if on { self.needsKey = true; self.links.forEach { $0.start() } } else { self.links.forEach { $0.stop() } }
        }
        TraiggerCapture.shared.reconfigure()
    }

    // Medición por tramos (registro cada 3 s): captura, espera, codificación, envío y red.
    private var stats = (frames: 0, skipped: 0, captureMs: 0.0, encodeMs: 0.0, sendMs: 0.0, bytes: 0, since: Date())
    func send(_ pixels: CVPixelBuffer, age: Double = 0) {
        let queued = CACurrentMediaTime()
        queue.async {
            guard self.enabled, self.anyReady else { return }
            if self.busy { self.stats.skipped += 1; return }
            self.busy = true
            self.stats.captureMs += max(0, age) * 1000 + (CACurrentMediaTime() - queued) * 1000
            self.encode(pixels)
        }
    }
    private func logStats() {
        let elapsed = Date().timeIntervalSince(stats.since)
        guard elapsed > 3, stats.frames > 0 else { return }
        let n = Double(stats.frames), d = link.diagnose()
        let active = d.paths.first { $0.active }
        let paths = d.paths.map { String(format: "%@ %.1f ms ±%.1f %.0f%%", $0.name, $0.rttMs / 2, $0.jitterMs, $0.lossPercent) }.joined(separator: " | ")
        NSLog("Traigger: HDMI %.0f fps (%d saltados) · captura %.1f ms · codificar %.1f ms · enviar %.1f ms · %.0f KB/fotograma · red %.1f ms por %@ [%@] · total ≈ %.0f ms",
              n / elapsed, stats.skipped, stats.captureMs / n, stats.encodeMs / n, stats.sendMs / n, Double(stats.bytes) / n / 1024,
              d.latencyMs, active?.name ?? "—", paths, stats.captureMs / n + stats.encodeMs / n + stats.sendMs / n + d.latencyMs)
        stats = (0, 0, 0, 0, 0, 0, Date())
    }
    func send(audio buffer: AVAudioPCMBuffer) {
        guard let channels = buffer.floatChannelData else { return }
        let frames = Int(buffer.frameLength)
        var body = Data(capacity: 1 + frames * 4); body.append(3)
        for i in 0..<frames {
            for c in 0..<2 {
                let value = Int16(max(-1, min(1, channels[min(c, Int(buffer.format.channelCount) - 1)][i])) * 32767).littleEndian
                withUnsafeBytes(of: value) { body.append(contentsOf: $0) }
            }
        }
        queue.async {
            guard self.enabled else { return }
            var size = UInt32(body.count).bigEndian
            var packet = Data(bytes: &size, count: 4); packet.append(body)
            self.links.forEach { $0.send(packet, droppable: true) }
        }
    }

    private func encode(_ pixels: CVPixelBuffer) {
        let sourceWidth = CVPixelBufferGetWidth(pixels), sourceHeight = CVPixelBufferGetHeight(pixels)
        // Tamaño de salida: el pedido (sin agrandar), con la proporción de la fuente; el codificador escala por hardware.
        let q = quality
        let height = q.height > 0 ? min(q.height, sourceHeight) : sourceHeight
        let width = (sourceWidth * height / max(1, sourceHeight)) & ~1
        if encoder == nil || size != (width, height) {
            if let encoder { VTCompressionSessionInvalidate(encoder) }
            encoder = nil; size = (width, height)
            let hevc = q.codec == "hevc"
            VTCompressionSessionCreate(allocator: nil, width: Int32(width), height: Int32(height), codecType: hevc ? kCMVideoCodecType_HEVC : kCMVideoCodecType_H264,
                                       encoderSpecification: [kVTVideoEncoderSpecification_EnableLowLatencyRateControl: true] as CFDictionary,
                                       imageBufferAttributes: nil, compressedDataAllocator: nil, outputCallback: nil, refcon: nil, compressionSessionOut: &encoder)
            guard let encoder else { busy = false; return }
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_RealTime, value: kCFBooleanTrue)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_ProfileLevel, value: hevc ? kVTProfileLevel_HEVC_Main_AutoLevel : kVTProfileLevel_H264_High_AutoLevel)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_AllowFrameReordering, value: kCFBooleanFalse)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_MaxFrameDelayCount, value: 0 as CFNumber)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_PrioritizeEncodingSpeedOverQuality, value: kCFBooleanTrue)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_ExpectedFrameRate, value: q.fps as CFNumber)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_AverageBitRate, value: Int(q.mbps * 1_000_000) as CFNumber)
            VTSessionSetProperty(encoder, key: kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration, value: q.keySeconds as CFNumber)
            needsKey = true
        }
        guard let encoder else { busy = false; return }
        let options = needsKey ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary : nil
        needsKey = false; index += 1
        let started = CACurrentMediaTime()
        let status = VTCompressionSessionEncodeFrame(encoder, imageBuffer: pixels, presentationTimeStamp: CMTime(value: index, timescale: 60),
                                                     duration: .invalid, frameProperties: options, infoFlagsOut: nil) { [weak self] status, _, sample in
            guard let self else { return }
            let encoded = CACurrentMediaTime()
            self.queue.async {
                guard status == noErr, let sample, let packet = Self.packet(sample) else { self.busy = false; return }
                self.stats.encodeMs += (encoded - started) * 1000; self.stats.bytes += packet.count; self.stats.frames += 1
                // Los fotogramas clave se mandan siempre; el resto se salta si el camino va cargado (y se pide uno clave).
                let key = packet.count > 5 && packet[5] == 1
                let sending = CACurrentMediaTime()
                var sent = false
                for (i, link) in self.links.enumerated() {
                    sent = link.send(packet, droppable: !key, done: i == 0 ? { [weak self] in
                        guard let self else { return }
                        self.stats.sendMs += (CACurrentMediaTime() - sending) * 1000; self.busy = false; self.logStats()
                    } : nil) || sent
                }
                if !sent && !key { self.needsKey = true }
            }
        }
        if status != noErr { busy = false }
    }

    // H.264: [tamaño][2][clave?][SPS/PPS si es clave][fotograma AVCC] (MacCamera/Shared/TraiggerVideo.swift).
    // HEVC:  [tamaño][5][clave?][VPS/SPS/PPS si es clave][fotograma]: mismo esquema con tres parámetros.
    static func packet(_ sample: CMSampleBuffer) -> Data? {
        guard let data = CMSampleBufferGetDataBuffer(sample) else { return nil }
        let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false) as? [[CFString: Any]]
        let key = !(attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool ?? false)
        let format = CMSampleBufferGetFormatDescription(sample)
        let hevc = format.map { CMFormatDescriptionGetMediaSubType($0) == kCMVideoCodecType_HEVC } ?? false
        var body = Data([hevc ? 5 : 2, key ? 1 : 0])
        if key, let format {
            for index in 0..<(hevc ? 3 : 2) {
                var pointer: UnsafePointer<UInt8>?, size = 0
                let status = hevc
                    ? CMVideoFormatDescriptionGetHEVCParameterSetAtIndex(format, parameterSetIndex: index, parameterSetPointerOut: &pointer,
                                                                         parameterSetSizeOut: &size, parameterSetCountOut: nil, nalUnitHeaderLengthOut: nil)
                    : CMVideoFormatDescriptionGetH264ParameterSetAtIndex(format, parameterSetIndex: index, parameterSetPointerOut: &pointer,
                                                                         parameterSetSizeOut: &size, parameterSetCountOut: nil, nalUnitHeaderLengthOut: nil)
                guard status == noErr, let pointer else { return nil }
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

// Funciones para Unity (VisionCapture, CaptureBackground.cs).
@_cdecl("TraiggerCaptureList") public func TraiggerCaptureList() -> UnsafeMutablePointer<CChar>? {
    let list = TraiggerCapture.devices().map { ["id": $0.uniqueID, "name": $0.localizedName] }
    let data = (try? JSONSerialization.data(withJSONObject: list)) ?? Data("[]".utf8)
    return strdup(String(data: data, encoding: .utf8) ?? "[]")
}
@_cdecl("TraiggerCaptureStart") public func TraiggerCaptureStart(_ identifier: UnsafePointer<CChar>, _ audio: Int32) {
    TraiggerCapture.shared.start(String(cString: identifier), audio: audio != 0)
}
@_cdecl("TraiggerCaptureStop") public func TraiggerCaptureStop() { TraiggerCapture.shared.stop() }
@_cdecl("TraiggerCaptureStatus") public func TraiggerCaptureStatus() -> UnsafeMutablePointer<CChar>? {
    let sender = TraiggerHDMISender.shared, mode = TraiggerCapture.shared.modeText
    let latency = ""
    return strdup(TraiggerCapture.shared.status + (mode.isEmpty ? "" : " (\(mode))") + " · " + sender.status + latency)
}
@_cdecl("TraiggerCaptureFrameInfo") public func TraiggerCaptureFrameInfo(_ width: UnsafeMutablePointer<Int32>, _ height: UnsafeMutablePointer<Int32>) -> Int64 {
    TraiggerCapture.shared.frameInfo(width, height)
}
@_cdecl("TraiggerCaptureCopyFrame") public func TraiggerCaptureCopyFrame(_ destination: UnsafeMutablePointer<UInt8>, _ capacity: Int32) -> Int32 {
    TraiggerCapture.shared.copyFrame(destination, capacity)
}
@_cdecl("TraiggerCaptureSendToVision") public func TraiggerCaptureSendToVision(_ on: Int32) { TraiggerHDMISender.shared.setEnabled(on != 0) }
@_cdecl("TraiggerCaptureFree") public func TraiggerCaptureFree(_ pointer: UnsafeMutableRawPointer?) { free(pointer) }
@_cdecl("TraiggerCaptureSetQuality") public func TraiggerCaptureSetQuality(_ json: UnsafePointer<CChar>) {
    TraiggerHDMISender.shared.setQuality(String(cString: json))
}
#endif

#if os(iOS)
import Vision
// Seguimiento de cara con Vision desde la cámara frontal, para cuando la capturadora ocupa las cámaras y Face ID
// (ARKit) no puede funcionar. Da giro de cabeza, parpadeos, boca y sonrisa (menos detalle que Face ID) y los entrega
// por el mismo camino que ARKit (TraiggerFaceTracking.mm), así que Unity no nota la diferencia.
@_silgen_name("TraiggerFacePushExternal") func TraiggerFacePushExternal(_ values: UnsafePointer<Float>, _ count: Int32)
@_silgen_name("TraiggerFaceSetExternal") func TraiggerFaceSetExternal(_ on: Int32)
@_silgen_name("TraiggerFaceForegroundGrace") func TraiggerFaceForegroundGrace()
@_silgen_name("TraiggerFaceReleaseCapture") func TraiggerFaceReleaseCapture()
@_silgen_name("TraiggerFaceVisionPriority") func TraiggerFaceVisionPriority()
@_silgen_name("TraiggerFaceHybridPriority") func TraiggerFaceHybridPriority()
@_silgen_name("TraiggerFaceCapturePriority") func TraiggerFaceCapturePriority(_ on: Int32)
@_silgen_name("TraiggerFaceLocalProbe") func TraiggerFaceLocalProbe(_ active: Int32)
@_silgen_name("TraiggerFaceCaptureConflict") func TraiggerFaceCaptureConflict() -> Int32
@_silgen_name("TraiggerFaceResumeRequested") func TraiggerFaceResumeRequested() -> Int32
final class TraiggerVisionFace: @unchecked Sendable {
    static let shared = TraiggerVisionFace()
    var status = ""
    private var frames = 0, found = 0, logAt = Date()
    private let request = VNDetectFaceLandmarksRequest()
    // La detección de puntos no siempre trae los ángulos de la cabeza: la de caras (revisión 3) sí.
    private let poseRequest: VNDetectFaceRectanglesRequest = {
        let r = VNDetectFaceRectanglesRequest()
        if #available(iOS 15.0, macOS 12.0, *) { r.revision = VNDetectFaceRectanglesRequestRevision3 }
        return r
    }()
    // Filtro «One Euro» por valor: quita el temblor en reposo y sigue rápido los movimientos de verdad.
    private struct OneEuro {
        var minCutoff: Double, beta: Double
        var x: Double?, dx = 0.0, last = 0.0
        mutating func callAsFunction(_ value: Double, _ t: Double) -> Double {
            guard let previous = x else { x = value; last = t; return value }
            let dt = max(1e-3, t - last); last = t
            func alpha(_ cutoff: Double) -> Double { let r = 2 * .pi * cutoff * dt; return r / (r + 1) }
            dx += alpha(1) * ((value - previous) / dt - dx)
            let filtered = previous + alpha(minCutoff + beta * abs(dx)) * (value - previous)
            x = filtered; return filtered
        }
    }
    private var yaw = OneEuro(minCutoff: 0.6, beta: 0.03), pitch = OneEuro(minCutoff: 0.4, beta: 0.02), roll = OneEuro(minCutoff: 0.6, beta: 0.03)
    private var eyeL = OneEuro(minCutoff: 4, beta: 0.5), eyeR = OneEuro(minCutoff: 4, beta: 0.5)
    private var closedFramesL = 0, closedFramesR = 0
    private var gazeX = OneEuro(minCutoff: 1.5, beta: 0.3), gazeY = OneEuro(minCutoff: 1.5, beta: 0.3)
    private var nextBlink = 0.0, blinkStart = -1.0
    private func naturalBlink(_ t: Double) -> Float {
        if nextBlink == 0 { nextBlink = t + Double.random(in: 2...4) }
        if blinkStart < 0, t >= nextBlink { blinkStart = t }
        guard blinkStart >= 0 else { return 0 }
        let phase = (t - blinkStart) / 0.16   // cierre y apertura en ~160 ms
        if phase >= 1 { blinkStart = -1; nextBlink = t + Double.random(in: 3...5); return 0 }
        return Float(sin(phase * .pi))
    }
    private var mouth = OneEuro(minCutoff: 2.5, beta: 0.3), smileF = OneEuro(minCutoff: 1.5, beta: 0.1)
    // Referencias propias: ojo abierto (alto/ancho) y labios en reposo, aprendidas sobre la marcha.
    private var openL = 0.0, openR = 0.0, lipsRest = 1.0, widthRest = 0.0
    private var blinkL = false, blinkR = false

    func process(_ sample: CMSampleBuffer) {
        guard let pixels = CMSampleBufferGetImageBuffer(sample) else { return }
        let t = CMSampleBufferGetPresentationTimeStamp(sample).seconds
        let handler = VNImageRequestHandler(cvPixelBuffer: pixels, orientation: .up)
        frames += 1
        defer {
            if Date().timeIntervalSince(logAt) > 3 {
                NSLog("Traigger: cara por Vision: %d de %d imágenes (%dx%d)", found, frames, CVPixelBufferGetWidth(pixels), CVPixelBufferGetHeight(pixels))
                frames = 0; found = 0; logAt = Date()
            }
        }
        guard (try? handler.perform([request, poseRequest])) != nil, let face = request.results?.first, let marks = face.landmarks else { return }
        let pose = poseRequest.results?.first
        found += 1
        func size(_ region: VNFaceLandmarkRegion2D?) -> CGSize {
            guard let points = region?.normalizedPoints, !points.isEmpty else { return .zero }
            let xs = points.map(\.x), ys = points.map(\.y)
            return CGSize(width: xs.max()! - xs.min()!, height: ys.max()! - ys.min()!)
        }
        let degrees: (NSNumber?) -> Double = { Double(truncating: $0 ?? 0) * 57.29578 }
        var v = [Float](repeating: 0, count: 40)
        // Misma convención que ARKit (giro positivo hacia la izquierda del usuario, arriba positivo). El cabeceo de
        // Vision es lo más ruidoso: va con menos ganancia.
        // Ángulos de la detección de caras; si faltan, se calculan con los puntos (ojos y nariz).
        func center(_ region: VNFaceLandmarkRegion2D?) -> CGPoint? {
            guard let points = region?.normalizedPoints, !points.isEmpty else { return nil }
            return CGPoint(x: points.map(\.x).reduce(0, +) / CGFloat(points.count), y: points.map(\.y).reduce(0, +) / CGFloat(points.count))
        }
        var yawDeg = degrees(pose?.yaw ?? face.yaw), pitchDeg = degrees(pose?.pitch ?? face.pitch), rollDeg = degrees(pose?.roll ?? face.roll)
        if let l = center(marks.leftEye), let rgt = center(marks.rightEye), let nose = center(marks.nose) {
            let mid = CGPoint(x: (l.x + rgt.x) / 2, y: (l.y + rgt.y) / 2), span = max(0.05, hypot(rgt.x - l.x, rgt.y - l.y))
            if (pose?.roll ?? face.roll) == nil { rollDeg = Double(atan2(rgt.y - l.y, rgt.x - l.x)) * 57.29578 }
            if (pose?.yaw ?? face.yaw) == nil { yawDeg = Double((nose.x - mid.x) / span) * 70 }
            if (pose?.pitch ?? face.pitch) == nil { pitchDeg = Double((mid.y - nose.y) / span - 0.55) * -60 }
        }
        v[0] = Float(yaw(-yawDeg, t)); v[1] = Float(pitch(-pitchDeg * 0.6, t)); v[2] = Float(roll(rollDeg, t))
        if frames % 90 == 1 { NSLog("Traigger: cara por la cámara frontal: giro %.0f°, cabeceo %.0f°, inclinación %.0f° (ángulos de Vision: %@)", v[0], v[1], v[2], pose?.yaw == nil ? "no" : "sí") }
        // Parpadeo: proporción del ojo frente a la de tu ojo abierto (aprendida), con margen y histéresis.
        // Parpadeo con confirmación: el ojo tiene que verse muy cerrado en dos imágenes seguidas; los cierres parciales
        // (ruido de unos píxeles) se ignoran. Así no hay espasmos.
        func eye(_ region: VNFaceLandmarkRegion2D?, _ open: inout Double, _ closed: inout Bool, _ frames: inout Int, _ filter: inout OneEuro) -> Float {
            let s = size(region); guard s.width > 0 else { return closed ? 1 : 0 }
            let ratio = Double(s.height / s.width)
            open = open == 0 ? ratio : (ratio > open ? open + (ratio - open) * 0.05 : open + (ratio - open) * 0.002)
            let closure = 1 - ratio / max(0.05, open)
            frames = closure > 0.6 ? frames + 1 : 0
            if closed { if closure < 0.35 { closed = false } } else if frames >= 2 { closed = true }
            return Float(max(0, min(1, filter(closed ? 1 : 0, t))))
        }
        // El parpadeo medido con la forma del ojo en esta cámara es poco fiable (espasmos, o los ojos cerrados todo el
        // rato): con Vision, Traigger parpadea de forma natural él solo, cada 3–5 s.
        _ = eye
        let blink = naturalBlink(t)
        v[4] = blink; v[5] = blink
        // Mirada: posición de cada pupila dentro de su ojo (−0,5 … 0,5), media de los dos ojos y suavizada.
        func pupil(_ eye: VNFaceLandmarkRegion2D?, _ pupil: VNFaceLandmarkRegion2D?) -> CGPoint? {
            guard let e = eye?.normalizedPoints, e.count > 2, let p = pupil?.normalizedPoints.first else { return nil }
            let xs = e.map(\.x), ys = e.map(\.y)
            let w = max(0.001, xs.max()! - xs.min()!), h = max(0.001, ys.max()! - ys.min()!)
            return CGPoint(x: (p.x - xs.min()!) / w - 0.5, y: (p.y - ys.min()!) / h - 0.5)
        }
        let pupils = [pupil(marks.leftEye, marks.leftPupil), pupil(marks.rightEye, marks.rightPupil)].compactMap { $0 }
        if !pupils.isEmpty {
            let px = gazeX(Double(pupils.map(\.x).reduce(0, +)) / Double(pupils.count), t)
            let py = gazeY(Double(pupils.map(\.y).reduce(0, +)) / Double(pupils.count), t)
            let gx = Float(max(-1, min(1, px * 3))), gy = Float(max(-1, min(1, py * 3)))
            // Imagen sin espejo: el ojo izquierdo del usuario queda a la derecha. Hacia la izquierda de la imagen es
            // «hacia dentro» para el ojo izquierdo y «hacia fuera» para el derecho (mismos canales que ARKit).
            v[8] = max(0, -gx); v[9] = max(0, gx)     // ojo izquierdo: dentro / fuera
            v[10] = max(0, gx); v[11] = max(0, -gx)   // ojo derecho: dentro / fuera
            v[12] = max(0, gy); v[13] = max(0, gy)    // arriba
            v[14] = max(0, -gy); v[15] = max(0, -gy)  // abajo
        }
        // Boca: apertura de los labios interiores por encima de su reposo.
        let lips = Double(size(marks.innerLips).height)
        lipsRest = lips < lipsRest ? lips : lipsRest + (lips - lipsRest) * 0.001
        v[3] = Float(max(0, min(1, mouth((lips - lipsRest - 0.015) / 0.12, t))))
        let width = Double(size(marks.outerLips).width)
        widthRest = widthRest == 0 ? width : widthRest + (width - widthRest) * 0.002
        let smile = Float(max(0, min(1, smileF((width - widthRest - 0.02) / 0.1, t))))
        v[6] = smile; v[7] = smile
        v[16] = 1   // cara rastreada
        v.withUnsafeBufferPointer { TraiggerFacePushExternal($0.baseAddress!, Int32($0.count)) }
    }
}
#if os(iOS)
// Contrato de 40 canales compartido con ARKitFaceSource; izquierda/derecha son las del usuario.
enum TraiggerHybridMapping {
    static func coefficients(_ c: [String: Float]) -> [Float] {
        var v = [Float](repeating: 0, count: 40)
        let names = ["jawOpen", "eyeBlinkLeft", "eyeBlinkRight", "mouthSmileLeft", "mouthSmileRight", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookInRight", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight", "eyeLookDownLeft", "eyeLookDownRight"]
        for (i, name) in names.enumerated() { v[i + 3] = max(0, min(1, c[name] ?? 0)) }
        v[16] = 1
        let expressions = ["mouthFunnel", "mouthPucker", "browInnerUp", "browDownLeft", "browDownRight", "browOuterUpLeft", "browOuterUpRight", "eyeWideLeft", "eyeWideRight", "eyeSquintLeft", "eyeSquintRight", "cheekPuff", "cheekSquintLeft", "cheekSquintRight", "mouthFrownLeft", "mouthFrownRight", "mouthLeft", "mouthRight", "mouthPressLeft", "mouthPressRight", "noseSneerLeft", "noseSneerRight"]
        for (i, name) in expressions.enumerated() { v[i + 17] = max(0, min(1, c[name] ?? 0)) }
        return v // 39 tongueOut no existe en este modelo.
    }
    // La mandíbula puede moverse muy poco al hablar. La abertura real de los labios
    // rescata esas vocales; mouthClose evita abrir sólo porque bajó la mandíbula con labios juntos.
    static func mouth(jaw: Float, close: Float, aperture: Float?, rest: Float = 0.02, gain: Float = 1.6) -> Float {
        let sensitivity = max(0.5, min(3, gain))
        let lip = aperture.map { max(0, min(1, ($0 - rest - 0.012) / 0.5)) }
        var coefficient = max(0, jaw - 0.008) * (1 - max(0, min(1, close)) * 0.8)
        if let lip, lip < 0.015, close > 0.55 { coefficient *= 0.2 }
        let opening = max(coefficient, (lip ?? 0) * 0.85)
        return max(0, min(1, opening * sensitivity))
    }
    static func pose(forward: [Float], up: [Float]) -> [Float] {
        let degrees: Float = 180 / .pi
        return [atan2(forward[0], forward[2]) * degrees,
                atan2(forward[1], hypot(forward[0], forward[2])) * degrees,
                atan2(-up[0], up[1]) * degrees]
    }
}
// Un único trabajo en vuelo: nunca acumular imágenes ni frenar los callbacks de la capturadora.
// La profundidad no entra en este modelo. Se conserva ARKit para el modo sin HDMI.
final class TraiggerHybridFace: @unchecked Sendable {
    static let shared = TraiggerHybridFace()
    static var available: Bool {
        #if canImport(MediaPipeTasksVision)
        return true
        #else
        return false
        #endif
    }
    private let queue = DispatchQueue(label: "traigger.hybrid.inference", qos: .userInitiated)
    private let lock = NSLock()
    private var enabled = false, busy = false
    private var generation = 0
    private var lastAdmission = 0.0
    private var message = "Preparando MediaPipe"
    var status: String { lock.withLock { message } }
    func start() { lock.withLock { generation += 1; enabled = true; lastAdmission = 0; message = "Preparando MediaPipe" } }
    func stop() { lock.withLock { generation += 1; enabled = false } }
    func invalidateFace() {
        lock.withLock {
            guard enabled else { return }
            generation += 1
            let empty = [Float](repeating: 0, count: 40)
            empty.withUnsafeBufferPointer { TraiggerFacePushExternal($0.baseAddress!, 40) }
        }
    }
    #if canImport(MediaPipeTasksVision)
    private var model: FaceLandmarker?
    private var lastTimestamp = 0
    private var mouthRevision = -1
    private var mouthRest: Float = 0.02
    private var centerRevision = -1
    private var center = [Float](repeating: 0, count: 4)
    private var readings = 0
    private var reportAt = 0.0
    private var reportedReadings = 0
    #endif
    func process(_ sample: CMSampleBuffer) {
        #if canImport(MediaPipeTasksVision)
        let now = CACurrentMediaTime()
        let ticket: Int? = lock.withLock {
            let requested = UserDefaults.standard.object(forKey: "traigger.hybrid.analysisFPS") as? Double ?? 30
            let interval = 1.0 / max(15, min(30, requested))
            guard enabled, !busy, now - lastAdmission >= interval - 0.001 else { return nil }
            busy = true; lastAdmission = max(lastAdmission + interval, now - interval); return generation
        }
        guard let ticket else { return }
        queue.async {
            defer { self.lock.withLock { self.busy = false } }
            guard self.lock.withLock({ self.enabled && ticket == self.generation }) else { return }
            autoreleasepool {
                do {
                    if self.model == nil {
                        guard let path = Bundle.main.path(forResource: "face_landmarker", ofType: "task", inDirectory: "Data/Raw") else {
                            throw NSError(domain: "Traigger", code: 1, userInfo: [NSLocalizedDescriptionKey: "Falta Data/Raw/face_landmarker.task; exporta de nuevo desde Unity"])
                        }
                        let options = FaceLandmarkerOptions()
                        options.baseOptions.modelAssetPath = path
                        options.runningMode = .video
                        options.numFaces = 1
                        options.outputFaceBlendshapes = true
                        options.outputFacialTransformationMatrixes = true
                        self.model = try FaceLandmarker(options: options)
                    }
                    // Timestamps siempre crecientes, incluso después de detener y reabrir la captura.
                    self.lastTimestamp = max(self.lastTimestamp + 1, Int(now * 1000))
                    let image = try MPImage(sampleBuffer: sample, orientation: .up)
                    let result = try self.model!.detect(videoFrame: image, timestampInMilliseconds: self.lastTimestamp)
                    let buffer = CMSampleBufferGetImageBuffer(sample)!
                    let frame = self.convert(result, aspect: Float(CVPixelBufferGetHeight(buffer)) / Float(CVPixelBufferGetWidth(buffer)))
                    let elapsed = CACurrentMediaTime() - now
                    self.lock.withLock {
                        // Invalidar también el resultado de una inferencia que terminó después de cambiar de modo.
                        guard self.enabled, ticket == self.generation else { return }
                        frame.withUnsafeBufferPointer { TraiggerFacePushExternal($0.baseAddress!, Int32($0.count)) }
                        self.readings += 1
                        if now - self.reportAt >= 1 {
                            let rate = Double(self.readings - self.reportedReadings) / max(1, now - self.reportAt)
                            self.message = frame[16] > 0 ? String(format: "MediaPipe %.1f Hz · %.0f ms · ojos activos", rate, elapsed * 1000) : "MediaPipe · no veo la cara; necesita luz"
                            self.reportedReadings = self.readings; self.reportAt = now
                        }
                    }
                } catch {
                    self.lock.withLock {
                        guard self.enabled, ticket == self.generation else { return }
                        self.message = "MediaPipe: " + error.localizedDescription
                        // No dejar una expresión antigua congelada si el detector falla.
                        let empty = [Float](repeating: 0, count: 40)
                        empty.withUnsafeBufferPointer { TraiggerFacePushExternal($0.baseAddress!, 40) }
                    }
                }
            }
        }
        #endif
    }
    #if canImport(MediaPipeTasksVision)
    private func convert(_ result: FaceLandmarkerResult, aspect: Float) -> [Float] {
        var v = [Float](repeating: 0, count: 40)
        guard let face = result.faceBlendshapes.first, !result.faceLandmarks.isEmpty else { return v }
        let coefficients = Dictionary(face.categories.compactMap { category in category.categoryName.map { ($0, category.score) } }, uniquingKeysWith: { _, b in b })
        v = TraiggerHybridMapping.coefficients(coefficients)
        if let m = result.facialTransformationMatrixes.first, m.rows >= 3, m.columns >= 3 {
            let pose = TraiggerHybridMapping.pose(forward: [m.value(atRow: 0, column: 2), m.value(atRow: 1, column: 2), m.value(atRow: 2, column: 2)], up: [m.value(atRow: 0, column: 1), m.value(atRow: 1, column: 1), m.value(atRow: 2, column: 1)])
            v[0] = pose[0]; v[1] = pose[1]; v[2] = pose[2]
        }
        let defaults = UserDefaults.standard
        // Distancia entre labios / anchura de boca: independiente de distancia a la cámara.
        // Ajustar el aspecto del vídeo antes de medir (las coordenadas vienen normalizadas).
        let points = result.faceLandmarks[0]
        var aperture: Float?
        if points.count > 308 {
            func distance(_ a: Int, _ b: Int) -> Float {
                let x = points[a].x - points[b].x, y = (points[a].y - points[b].y) * aspect
                let z = points[a].z - points[b].z
                return sqrt(x*x + y*y + z*z)
            }
            let width = distance(78, 308)
            if width > 0.01 { aperture = distance(13, 14) / width }
        }
        let mouthCalibration = defaults.integer(forKey: "traigger.hybrid.mouthCenter")
        if mouthRevision < 0 { mouthRevision = mouthCalibration }
        if mouthRevision != mouthCalibration, let aperture {
            mouthRest = max(0, min(0.15, aperture)); mouthRevision = mouthCalibration
        }
        let mouthGain = Float(defaults.object(forKey: "traigger.hybrid.mouthGain") as? Double ?? 1.6)
        v[3] = TraiggerHybridMapping.mouth(jaw: v[3], close: coefficients["mouthClose"] ?? 0, aperture: aperture, rest: mouthRest, gain: mouthGain)
        let raw: [Float] = [v[9] - v[8], v[10] - v[11], v[12] - v[14], v[13] - v[15]]
        let revision = defaults.integer(forKey: "traigger.hybrid.center")
        // Calibración explícita; nunca recentrar silenciosamente al mover los ojos.
        if centerRevision < 0 { centerRevision = revision }
        if revision != centerRevision { center = raw; centerRevision = revision }
        let gain = Float(defaults.object(forKey: "traigger.hybrid.gazeGain") as? Double ?? 1.5)
        let xSign: Float = defaults.bool(forKey: "traigger.hybrid.invertX") ? -1 : 1
        let ySign: Float = defaults.bool(forKey: "traigger.hybrid.invertY") ? -1 : 1
        let gaze = raw.enumerated().map { i, value in max(-1, min(1, (value - center[i]) * gain * (i < 2 ? xSign : ySign))) }
        v[9] = max(0, gaze[0]); v[8] = max(0, -gaze[0]); v[10] = max(0, gaze[1]); v[11] = max(0, -gaze[1])
        v[12] = max(0, gaze[2]); v[14] = max(0, -gaze[2]); v[13] = max(0, gaze[3]); v[15] = max(0, -gaze[3])
        // El modelo estándar no mide lengua: conservar 39=0, sin inventar un tongueOut.
        return v
    }
    #endif
}
#endif

#endif


// Cerrojo mínimo para leer un valor desde cualquier hilo.
final class OSAllocatedUnfairLockShim<Value>: @unchecked Sendable {
    private var value: Value; private let lock = NSLock()
    init(_ value: Value) { self.value = value }
    func withLock<T>(_ body: (inout Value) -> T) -> T { lock.lock(); defer { lock.unlock() }; return body(&value) }
}
