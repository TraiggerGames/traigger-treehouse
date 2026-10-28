import AVFoundation
import Foundation

// Voz del usuario con el procesado de Apple (el mismo de FaceTime): cancelación de eco y supresión de ruido de
// fondo, centrado en quien lleva el casco. El nivel resultante mueve la boca; la televisión u otras voces lejanas
// quedan muy atenuadas. Funciona en el espacio compartido y en el inmersivo.
@MainActor final class TraiggerVoice {
    static let shared = TraiggerVoice()
    private let engine = AVAudioEngine()
    private var level: Float = 0
    private var started = false
    // Último buffer de audio recibido (hilo de audio) y estado que se repite a Unity cada 2 s.
    nonisolated(unsafe) private static var lastBuffer = 0.0
    private var status = "Iniciando micrófono…"
    private var watchdog: Task<Void, Never>?
    private var restarting = false

    func start() {
        guard !started else { return }
        started = true
        AVAudioApplication.requestRecordPermission { allowed in
            Task { @MainActor in
                guard allowed else {
                    self.report(status: "Permiso de micrófono denegado (Ajustes → Traigger)")
                    TraiggerSpeech.shared.status("Sin frases: permiso de micrófono denegado"); return
                }
                self.observe()
                self.run()
                self.startWatchdog()
            }
        }
    }

    private func report(status value: String) {
        status = value
        TraiggerUnity.shared.send("{\"voiceStatus\":\"\(value)\"}")
    }

    // El sistema para el motor si otra app (FaceTime) toma el micrófono o cambia la ruta de audio; se vuelve a arrancar.
    private func observe() {
        let center = NotificationCenter.default
        center.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { _ in
            Task { @MainActor in TraiggerVoice.shared.restart("cambio de audio") }
        }
        center.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { note in
            let type = (note.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt).flatMap(AVAudioSession.InterruptionType.init)
            Task { @MainActor in
                if type == .began { TraiggerVoice.shared.report(status: "Micrófono en pausa (lo usa otra app)") }
                else { TraiggerVoice.shared.restart("fin de la interrupción") }
            }
        }
        center.addObserver(forName: AVAudioSession.mediaServicesWereResetNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in TraiggerVoice.shared.restart("servicios de audio reiniciados") }
        }
    }

    // Si dejan de llegar buffers 3 s, se rearranca; además el estado se repite por si Unity aún no escuchaba.
    private func startWatchdog() {
        watchdog = Task { @MainActor in
            while true {
                try? await Task.sleep(for: .seconds(2))
                let silent = Date().timeIntervalSince1970 - Self.lastBuffer > 3
                if silent && !restarting { restart("sin audio") }
                TraiggerUnity.shared.send("{\"voiceStatus\":\"\(status)\"}")
            }
        }
    }

    private func restart(_ reason: String) {
        guard !restarting else { return }
        restarting = true
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        report(status: "Reiniciando micrófono (\(reason))…")
        Task { @MainActor in
            try? await Task.sleep(for: .milliseconds(400))
            self.run()
            self.restarting = false
        }
    }

    private func run() {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playAndRecord, mode: .voiceChat, options: [.mixWithOthers, .allowBluetoothA2DP])
            try session.setActive(true)
            let input = engine.inputNode
            if !input.isVoiceProcessingEnabled { try input.setVoiceProcessingEnabled(true) }
            // El procesado de voz atenúa el resto del sonido de la app (película, capturadora): sin atenuación.
            if #available(visionOS 1.0, *) { input.voiceProcessingOtherAudioDuckingConfiguration = .init(enableAdvancedDucking: false, duckingLevel: .min) }
            let format = input.outputFormat(forBus: 0)
            guard format.sampleRate > 0, format.channelCount > 0 else { throw NSError(domain: "Traigger", code: 1, userInfo: [NSLocalizedDescriptionKey: "sin entrada de audio"]) }
            let pitchTracker = TraiggerPitch(sampleRate: Float(format.sampleRate))
            input.installTap(onBus: 0, bufferSize: 512, format: format) { buffer, _ in
                guard let samples = buffer.floatChannelData?[0] else { return }
                TraiggerVoice.lastBuffer = Date().timeIntervalSince1970
                TraiggerSpeech.listen.feed(buffer)
                TraiggerStreamAudio.push(buffer, source: 0) // tu voz en el directo
                // Rasgos para las vocales: cruces por cero (agudo/sibilante → i, e, s) y energía de graves con un
                // filtro paso bajo (o, u). Baratos y suficientes para elegir la forma de la boca.
                var sum: Float = 0, low: Float = 0, lowState: Float = 0, crossings = 0
                let n = Int(buffer.frameLength)
                for i in 0..<n {
                    let s = samples[i]; sum += s * s
                    lowState += 0.08 * (s - lowState); low += lowState * lowState
                    if i > 0 && (samples[i - 1] < 0) != (s < 0) { crossings += 1 }
                }
                let count = Float(max(n, 1))
                let rms = sqrt(sum / count)
                let zcr = Float(crossings) / count
                let lowRatio = sum > 0 ? low / sum : 0
                let f0 = pitchTracker.feed(samples, n)
                Task { @MainActor in TraiggerVoice.shared.report(rms, zcr, lowRatio, f0) }
            }
            try engine.start()
            TraiggerVoice.lastBuffer = Date().timeIntervalSince1970
            report(status: "Voz aislada activa")
            TraiggerSpeech.shared.start(format: format)
        } catch {
            report(status: "Micrófono: \(error.localizedDescription)")
            TraiggerSpeech.shared.status("Sin frases: el micrófono no arrancó (\(error.localizedDescription))")
        }
    }
    private var lastSend = 0.0
    private func report(_ rms: Float, _ zcr: Float, _ low: Float, _ f0: Float) {
        level = rms
        let now = Date().timeIntervalSince1970
        guard now - lastSend > 1.0 / 60.0 else { return }
        lastSend = now
        TraiggerUnity.shared.send("{\"voice\":\(level),\"zcr\":\(zcr),\"low\":\(low),\"f0\":\(f0)}")
    }
}

// Tono de la voz (Hz) por autocorrelación sobre los últimos ~43 ms, reducidos a 12 kHz. 0 = sin tono.
// Lo usa Traigger para deducir si la entonación es alegre o triste. Solo se toca desde el hilo de audio.
final class TraiggerPitch: @unchecked Sendable {
    private var ring = [Float](repeating: 0, count: 2048)
    private var write = 0
    private let sampleRate: Float
    private var last: Float = 0
    init(sampleRate: Float) { self.sampleRate = sampleRate }
    func feed(_ samples: UnsafeMutablePointer<Float>, _ n: Int) -> Float {
        for i in 0..<n { ring[write] = samples[i]; write = (write + 1) % ring.count }
        let step = max(1, Int(sampleRate / 12000)), rate = sampleRate / Float(step)
        let count = ring.count / step
        var x = [Float](repeating: 0, count: count), mean: Float = 0, energy: Float = 0
        for i in 0..<count {
            var s: Float = 0
            for k in 0..<step { s += ring[(write + i * step + k) % ring.count] }
            x[i] = s / Float(step); mean += x[i]
        }
        mean /= Float(count)
        for i in 0..<count { x[i] -= mean; energy += x[i] * x[i] }
        guard energy / Float(count) > 1e-6 else { return 0 }
        let minLag = Int(rate / 400), maxLag = min(count / 2, Int(rate / 70) + 1)
        var best: Float = 0, bestLag = 0
        for lag in minLag...maxLag {
            var c: Float = 0, e0: Float = 0, e1: Float = 0
            for i in 0..<(count - lag) { c += x[i] * x[i + lag]; e0 += x[i] * x[i]; e1 += x[i + lag] * x[i + lag] }
            let r = c / (e0 * e1 + 1e-12).squareRoot()
            if r > best { best = r; bestLag = lag }
        }
        last = best > 0.55 && bestLag > 0 ? rate / Float(bestLag) : 0
        return last
    }
}
