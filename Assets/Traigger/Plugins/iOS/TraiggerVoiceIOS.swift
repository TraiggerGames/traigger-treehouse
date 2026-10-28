import AVFoundation
import Foundation

// Voz en iPhone y iPad (sustituye al micrófono de Unity, porque el reconocimiento de frases necesita el mismo audio):
// nivel y rasgos para la boca y tono para la entonación. Las frases,
// su emoción y los sonidos (risa, llanto…) los resuelve TraiggerListen.swift, compartido con Vision Pro.
// Unity lee los rasgos cada fotograma (TraiggerVoiceRead) y recoge los mensajes (TraiggerVoicePoll).
final class TraiggerVoiceIOS: @unchecked Sendable {
    static let shared = TraiggerVoiceIOS()
    private let engine = AVAudioEngine()
    private let lock = NSLock()
    private var features: (Float, Float, Float, Float) = (0, 0, 0, 0)
    private var messages: [String] = []
    private var started = false
    private var ring = [Float](repeating: 0, count: 2048)
    private var write = 0
    private var sampleRate: Float = 48000
    // Frases, emoción y sonidos: módulo compartido con Vision Pro (TraiggerListen.swift).
    private lazy var listen = TraiggerListen { [unowned self] message in self.post(message) }

    func setLanguage(_ code: String) { listen.setLanguage(code) }

    func start() {
        DispatchQueue.main.async {
            guard !self.started else { return }
            self.started = true
            self.listen.status("Pidiendo permiso de micrófono…")
            AVAudioSession.sharedInstance().requestRecordPermission { allowed in
                DispatchQueue.main.async {
                    guard allowed else {
                        self.post(["voiceStatus": "Permiso de micrófono denegado"])
                        self.listen.status("Sin frases: falta el permiso de micrófono (Ajustes → Traigger)"); return
                    }
                    self.runAudio()
                }
            }
        }
    }

    func stop() {
        DispatchQueue.main.async {
            guard self.started else { return }
            self.started = false
            self.engine.inputNode.removeTap(onBus: 0); self.engine.stop()
        }
    }

    private func runAudio() {
        do {
            let session = AVAudioSession.sharedInstance()
            // Sin el modo de llamada: su procesado de voz (VPIO) choca con la salida de audio de Unity en iPhone/iPad
            // («vpio render err -1» en bucle) y deja al reconocedor sin audio. El modo medición entrega la voz limpia.
            try session.setCategory(.playAndRecord, mode: .measurement, options: [.mixWithOthers, .defaultToSpeaker, .allowBluetoothA2DP])
            try session.setActive(true)
            let input = engine.inputNode
            let format = input.outputFormat(forBus: 0)
            sampleRate = Float(format.sampleRate)
            input.installTap(onBus: 0, bufferSize: 1024, format: format) { buffer, _ in self.process(buffer) }
            try engine.start()
            post(["voiceStatus": "Voz aislada activa"])
            listen.start(format: format)
        } catch {
            post(["voiceStatus": "Micrófono: \(error.localizedDescription)"])
            listen.status("Sin frases: el micrófono no arrancó (\(error.localizedDescription))")
        }
    }

    // Hilo de audio.
    private func usbSilence() { lock.lock(); features = (0, 0, 0, 0); lock.unlock() }
    private func process(_ buffer: AVAudioPCMBuffer) {
        // Con la entrada de la capturadora (USB), lo que llega es el sonido del juego, no tu voz: Traigger no lo usa para
        // la boca ni la emoción, y tampoco va al directo como micrófono (ya va por su canal de capturadora).
        if AVAudioSession.sharedInstance().currentRoute.inputs.contains(where: { $0.portType == .usbAudio }) { usbSilence(); return }
        TraiggerStream.shared.mixer.push(.mic, buffer) // tu voz en la emisión en directo
        guard let samples = buffer.floatChannelData?[0] else { return }
        listen.feed(buffer)
        let n = Int(buffer.frameLength)
        var sum: Float = 0, low: Float = 0, lowState: Float = 0, crossings = 0
        for i in 0..<n {
            let s = samples[i]; sum += s * s
            lowState += 0.08 * (s - lowState); low += lowState * lowState
            if i > 0 && (samples[i - 1] < 0) != (s < 0) { crossings += 1 }
            ring[write] = s; write = (write + 1) % ring.count
        }
        let count = Float(max(n, 1))
        let rms = sqrt(sum / count)
        let f0 = rms > 0.003 ? pitch() : 0
        lock.lock(); features = (rms, Float(crossings) / count, sum > 0 ? low / sum : 0, f0); lock.unlock()
    }

    // Tono (Hz) por autocorrelación sobre los últimos ~43 ms reducidos a 12 kHz. 0 = sin tono.
    private func pitch() -> Float {
        let step = max(1, Int(sampleRate / 12000)), rate = sampleRate / Float(step)
        let count = ring.count / step
        var x = [Float](repeating: 0, count: count), mean: Float = 0
        for i in 0..<count {
            var s: Float = 0
            for k in 0..<step { s += ring[(write + i * step + k) % ring.count] }
            x[i] = s / Float(step); mean += x[i]
        }
        mean /= Float(count)
        for i in 0..<count { x[i] -= mean }
        let minLag = Int(rate / 400), maxLag = min(count / 2, Int(rate / 70) + 1)
        var best: Float = 0, bestLag = 0
        for lag in minLag...maxLag {
            var c: Float = 0, e0: Float = 0, e1: Float = 0
            for i in 0..<(count - lag) { c += x[i] * x[i + lag]; e0 += x[i] * x[i]; e1 += x[i + lag] * x[i + lag] }
            let r = c / (e0 * e1 + 1e-12).squareRoot()
            if r > best { best = r; bestLag = lag }
        }
        return best > 0.55 && bestLag > 0 ? rate / Float(bestLag) : 0
    }

    func read(_ out: UnsafeMutablePointer<Float>) {
        lock.lock(); out[0] = features.0; out[1] = features.1; out[2] = features.2; out[3] = features.3; lock.unlock()
    }
    func poll() -> String? {
        lock.lock(); defer { lock.unlock() }
        return messages.isEmpty ? nil : messages.removeFirst()
    }
    private func post(_ object: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: object), let json = String(data: data, encoding: .utf8) else { return }
        lock.lock(); messages.append(json); if messages.count > 32 { messages.removeFirst() }; lock.unlock()
    }
}

@_cdecl("TraiggerVoiceStart") public func TraiggerVoiceStart() { TraiggerVoiceIOS.shared.start() }
@_cdecl("TraiggerVoiceLanguage") public func TraiggerVoiceLanguage(_ code: UnsafePointer<CChar>) { TraiggerVoiceIOS.shared.setLanguage(String(cString: code)) }
@_cdecl("TraiggerVoiceStop") public func TraiggerVoiceStop() { TraiggerVoiceIOS.shared.stop() }
@_cdecl("TraiggerVoiceRead") public func TraiggerVoiceRead(_ out: UnsafeMutablePointer<Float>) { TraiggerVoiceIOS.shared.read(out) }
@_cdecl("TraiggerVoicePoll") public func TraiggerVoicePoll() -> UnsafeMutablePointer<CChar>? {
    guard let message = TraiggerVoiceIOS.shared.poll() else { return nil }
    return strdup(message)
}
