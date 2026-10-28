import AVFoundation
import Foundation
import NaturalLanguage
import SoundAnalysis
import Speech
#if canImport(FoundationModels)
import FoundationModels
#endif

// Escucha de Traigger, compartida por iPhone/iPad (plugin de Unity) y Vision Pro (el host la copia al integrarse).
// Recibe el audio del micrófono ya procesado y produce mensajes para Unity:
//  · {"speechText"}: la frase mientras la dices (reconocimiento de voz de Apple, en el dispositivo).
//  · {"emotion","emotionLevel","speechText"}: al acabar cada frase, la emoción que le asigna Apple Intelligence
//    (o un respaldo con sentimiento y palabras clave).
//  · {"emotion","emotionLevel","emotionCue"}: sonidos sin palabras (risa, llanto, grito, suspiro) con el
//    clasificador de sonidos de Apple.
//  · {"speechStatus"}: qué está pasando, incluidos los errores; se repite cada 2 s por si Unity aún no escuchaba.
final class TraiggerListen: @unchecked Sendable {
    private let post: ([String: Any]) -> Void
    private let queue = DispatchQueue(label: "traigger.listen")
    private let lock = NSLock()
    init(post: @escaping ([String: Any]) -> Void) { self.post = post }

    // Estado (solo en `queue`).
    private var started = false
    private var language = "auto"
    private var lastStatus = ""
    private var heartbeat: DispatchSourceTimer?
    private var committed = "", volatile = "", consumed = 0
    private var pause: DispatchWorkItem?
    private var generation = 0
    private var format: AVAudioFormat?
    // Destinos del audio (los toca el hilo de audio, con `lock`).
    private var legacyRequest: SFSpeechAudioBufferRecognitionRequest?
    private var legacyTask: SFSpeechRecognitionTask?
    private var modern: AnyObject?
    private var sound: SNAudioStreamAnalyzer?
    private var soundObserver: SoundObserver?
    private var framePosition: AVAudioFramePosition = 0
    private let soundQueue = DispatchQueue(label: "traigger.sound")

    func status(_ value: String) {
        queue.async {
            self.lastStatus = value
            self.post(["speechStatus": value])
            if self.heartbeat == nil {
                let timer = DispatchSource.makeTimerSource(queue: self.queue)
                timer.schedule(deadline: .now() + 2, repeating: 2)
                timer.setEventHandler { self.post(["speechStatus": self.lastStatus]) }
                timer.resume(); self.heartbeat = timer
            }
        }
    }

    // Llamar cuando el motor de audio ya está en marcha, con el formato de los buffers que se van a entregar.
    func start(format: AVAudioFormat) {
        queue.async {
            // Tras un reinicio del micrófono el formato puede cambiar: se rehacen los analizadores con el nuevo.
            if self.started {
                guard format != self.format else { return }
                self.format = format; self.startSound(format); self.beginRecognition(); return
            }
            self.started = true; self.format = format
            self.startSound(format)
            self.status("Pidiendo permiso de reconocimiento de voz…")
            SFSpeechRecognizer.requestAuthorization { status in
                self.queue.async {
                    guard status == .authorized else {
                        self.status("Sin frases: falta el permiso de reconocimiento de voz (Ajustes → Traigger)"); return
                    }
                    self.beginRecognition()
                }
            }
        }
    }

    // "auto" (el idioma del sistema si es español o inglés), "es-ES" o "en-US".
    func setLanguage(_ code: String) {
        queue.async {
            guard code != self.language else { return }
            self.language = code
            if self.started { self.beginRecognition() }
        }
    }

    private var locale: Locale {
        let code = language != "auto" ? language : (Locale.preferredLanguages.first?.hasPrefix("en") == true ? "en-US" : "es-ES")
        return Locale(identifier: code)
    }
    private var languageName: String { locale.identifier.hasPrefix("en") ? "inglés" : "español" }

    // Hilo de audio.
    func feed(_ buffer: AVAudioPCMBuffer) {
        lock.lock()
        legacyRequest?.append(buffer)
        let modern = self.modern
        let sound = self.sound
        let position = framePosition; framePosition += AVAudioFramePosition(buffer.frameLength)
        lock.unlock()
        if #available(iOS 26.0, visionOS 26.0, *), let modern = modern as? ModernTranscriber { modern.feed(buffer) }
        if let sound { soundQueue.async { sound.analyze(buffer, atAudioFramePosition: position) } }
    }

    // MARK: Frases

    private func beginRecognition() {
        generation += 1
        stopRecognition()
        committed = ""; volatile = ""; consumed = 0; volatileIsWhole = false
        let generation = self.generation
        if #available(iOS 26.0, visionOS 26.0, *), SpeechTranscriber.isAvailable, let format {
            let transcriber = ModernTranscriber()
            lock.lock(); modern = transcriber; lock.unlock()
            let locale = self.locale, name = languageName
            Task {
                do {
                    try await transcriber.start(locale: locale, input: format,
                        status: { message in self.status(message) },
                        text: { final, text in self.queue.async { if generation == self.generation { self.received(text, final: final) } } })
                    self.status("Entendiendo frases en \(name) (en el dispositivo)")
                } catch {
                    // Si el sistema nuevo no puede con este idioma o este aparato, se usa el clásico.
                    self.queue.async {
                        guard generation == self.generation else { return }
                        self.lock.lock(); self.modern = nil; self.lock.unlock()
                        self.beginLegacy(generation, reason: error.localizedDescription)
                    }
                }
            }
        } else {
            beginLegacy(generation, reason: nil)
        }
    }

    private func stopRecognition() {
        lock.lock()
        legacyRequest?.endAudio(); legacyRequest = nil
        let modern = self.modern; self.modern = nil
        lock.unlock()
        legacyTask?.cancel(); legacyTask = nil
        if #available(iOS 26.0, visionOS 26.0, *), let modern = modern as? ModernTranscriber { modern.stop() }
    }

    // Reconocedor clásico (iOS 15+, visionOS 1+). Cada frase va en su propia tarea; los errores se muestran y se
    // reintenta con calma, sin bucles.
    private func beginLegacy(_ generation: Int, reason: String?) {
        guard generation == self.generation else { return }
        guard let recognizer = SFSpeechRecognizer(locale: locale) else {
            status("Sin frases: el reconocimiento de voz no admite \(languageName)"); return
        }
        guard recognizer.isAvailable else {
            status("Sin frases: reconocimiento de voz no disponible ahora; reintentando…")
            queue.asyncAfter(deadline: .now() + 3) { self.beginLegacy(generation, reason: reason) }
            return
        }
        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        request.requiresOnDeviceRecognition = recognizer.supportsOnDeviceRecognition
        if #available(iOS 16.0, *) { request.addsPunctuation = true }
        lock.lock(); legacyRequest = request; lock.unlock()
        committed = ""; volatile = ""; consumed = 0; volatileIsWhole = true
        legacyId += 1
        let id = legacyId, started = Date()
        legacyTask = recognizer.recognitionTask(with: request) { result, error in
            self.queue.async {
                guard generation == self.generation, id == self.legacyId else { return }
                if let result { self.received(result.bestTranscription.formattedString, final: false) }
                let finished = (result?.isFinal ?? false) || error != nil || Date().timeIntervalSince(started) > 50
                guard finished else { return }
                self.flush()
                if let error, ![216, 301, 1110].contains((error as NSError).code) {
                    // 216/301 = cancelada por nosotros; 1110 = no se oyó nada. El resto se enseña en el panel.
                    self.status("Error de reconocimiento: \(error.localizedDescription). Reintentando…")
                    self.queue.asyncAfter(deadline: .now() + 2) { self.beginLegacy(generation, reason: nil) }
                } else {
                    self.beginLegacy(generation, reason: nil)
                }
            }
        }
        var message = "Entendiendo frases en \(languageName)"
        message += recognizer.supportsOnDeviceRecognition ? " (en el dispositivo)" : " (con los servidores de Apple: descarga el idioma de dictado para usarlo sin conexión)"
        if let reason { message += " · modo clásico: \(reason)" }
        status(message)
    }

    // El reconocedor clásico entrega siempre la frase entera; el nuevo, trozos (provisionales y definitivos).
    private var volatileIsWhole = false
    private var legacyId = 0
    // Clásico: al acabar una frase se cierra su tarea y empieza otra, para que el texto no se acumule.
    private func nextLegacyPhrase() {
        lock.lock(); legacyRequest?.endAudio(); legacyRequest = nil; lock.unlock()
        legacyTask?.cancel(); legacyTask = nil
        beginLegacy(generation, reason: nil)
    }
    private func received(_ text: String, final: Bool) {
        if volatileIsWhole { volatile = text }
        else if final { committed += text; volatile = "" } else { volatile = text }
        let full = committed + volatile
        let phrase = String(full.dropFirst(min(consumed, full.count))).trimmingCharacters(in: .whitespaces)
        guard !phrase.isEmpty else { return }
        post(["speechText": phrase])
        // Fin de frase: casi un segundo sin palabras nuevas.
        pause?.cancel()
        let item = DispatchWorkItem { self.flush(); if self.volatileIsWhole { self.nextLegacyPhrase() } }
        pause = item
        queue.asyncAfter(deadline: .now() + 0.9, execute: item)
    }

    private func flush() {
        pause?.cancel(); pause = nil
        let full = committed + volatile
        let phrase = String(full.dropFirst(min(consumed, full.count))).trimmingCharacters(in: .whitespaces)
        consumed = full.count
        if volatileIsWhole { committed = ""; volatile = ""; consumed = 0 }
        guard phrase.count > 2 else { return }
        Task {
            let (emotion, level) = await Self.classify(phrase)
            self.post(["emotion": emotion, "emotionLevel": level, "speechText": phrase])
        }
    }

    // MARK: Sonidos sin palabras

    private func startSound(_ format: AVAudioFormat) {
        guard let request = try? SNClassifySoundRequest(classifierIdentifier: .version1) else { return }
        request.windowDuration = CMTime(seconds: 1.0, preferredTimescale: 48000)
        request.overlapFactor = 0.5
        let analyzer = SNAudioStreamAnalyzer(format: format)
        let observer = SoundObserver { label, confidence in
            guard let emotion = Self.soundEmotion(label) else { return }
            self.post(["emotion": emotion.0, "emotionLevel": min(1, confidence * emotion.1), "emotionCue": emotion.2])
        }
        guard (try? analyzer.add(request, withObserver: observer)) != nil else { return }
        lock.lock(); sound = analyzer; soundObserver = observer; lock.unlock()
    }

    // Etiquetas del clasificador de Apple → expresión, fuerza y nombre para el panel.
    static func soundEmotion(_ label: String) -> (String, Double, String)? {
        if ["laugh", "giggl", "chuckl", "chortl"].contains(where: label.contains) { return ("Contento", 1, "risa") }
        if ["crying", "sobbing", "whimper"].contains(where: label.contains) { return ("Triste", 1, "llanto") }
        if label.contains("sigh") { return ("Triste", 0.6, "suspiro") }
        if ["shout", "yell", "battle_cry"].contains(where: label.contains) { return ("Enfado", 0.9, "grito") }
        if label.contains("scream") { return ("Sorpresa", 0.9, "chillido") }
        if label.contains("gasp") { return ("Sorpresa", 0.8, "sobresalto") }
        return nil
    }

    // Nombres iguales a los botones de expresión de Traigger.
    static let names = ["Neutro", "Contento", "Triste", "Enfado", "Sorpresa", "Curioso", "Vergüenza"]

    static func classify(_ phrase: String) async -> (String, Double) {
        #if canImport(FoundationModels)
        if #available(visionOS 26.0, iOS 26.0, *), case .available = SystemLanguageModel.default.availability {
            do {
                let session = LanguageModelSession(instructions: """
                    Eres el sistema de expresiones de un avatar. Lee lo que acaba de decir la persona y elige la emoción \
                    con la que lo dice. La frase puede estar en español o en inglés. Ten en cuenta negaciones e ironía. Si es una frase informativa sin emoción, elige neutro.
                    """)
                let guess = try await session.respond(to: phrase, generating: EmotionGuess.self).content
                return (guess.emotion.name, Double(guess.intensity) / 10)
            } catch {}
        }
        #endif
        return fallback(phrase)
    }

    // Respaldo sin Apple Intelligence: sentimiento positivo/negativo y palabras que delatan la emoción.
    static func fallback(_ phrase: String) -> (String, Double) {
        let lower = phrase.lowercased().folding(options: .diacriticInsensitive, locale: Locale(identifier: "es"))
        let keywords: [(String, [String])] = [
            ("Enfado", ["harto", "odio", "mierda", "joder", "enfad", "cabre", "molest", "idiota", "basta", "estoy hasta",
                        "angry", "hate", "annoy", "furious", "sick of", "damn", "stupid", "enough"]),
            ("Sorpresa", ["no me digas", "en serio", "increible", "madre mia", "anda ya", "que fuerte", "wow", "hala",
                          "no way", "seriously", "really?", "oh my god", "omg", "unbelievable", "what?!"]),
            ("Triste", ["triste", "pena", "llorar", "fatal", "se ha muerto", "murio", "echo de menos", "lo siento", "solo", "cansad", "mal",
                        "sad", "cry", "miss you", "sorry", "lonely", "tired", "unhappy", "depress"]),
            ("Vergüenza", ["verguenza", "perdon", "ups", "que corte", "embarrass", "awkward", "oops", "my bad"]),
            ("Curioso", ["por que", "como funciona", "que es", "me pregunto", "curios", "why", "how does", "what is", "i wonder"]),
            ("Contento", ["genial", "guay", "me encanta", "gracias", "feliz", "jaja", "que bien", "chulo", "chido", "bien",
                          "great", "awesome", "love it", "thanks", "thank you", "happy", "haha", "cool", "amazing", "nice"])]
        // Se buscan palabras enteras (o su comienzo: «enfad» vale para enfadado), no trozos sueltos dentro de otras.
        let padded = " " + String(lower.map { $0.isLetter || $0 == "'" ? $0 : " " }) + " "
        for (name, words) in keywords where words.contains(where: { padded.contains(" " + $0.filter { $0.isLetter || $0 == " " || $0 == "'" }) }) { return (name, 0.7) }
        let tagger = NLTagger(tagSchemes: [.sentimentScore])
        tagger.string = phrase
        let score = Double(tagger.tag(at: phrase.startIndex, unit: .paragraph, scheme: .sentimentScore).0?.rawValue ?? "0") ?? 0
        if score > 0.5 { return ("Contento", min(1, score)) }
        if score < -0.5 { return ("Triste", min(1, -score)) }
        return ("Neutro", 0)
    }
}

final class SoundObserver: NSObject, SNResultsObserving {
    private let found: (String, Double) -> Void
    private var last = Date.distantPast
    init(found: @escaping (String, Double) -> Void) { self.found = found }
    func request(_ request: SNRequest, didProduce result: SNResult) {
        guard let result = result as? SNClassificationResult else { return }
        for item in result.classifications.prefix(5) where item.confidence > 0.55 {
            guard TraiggerListen.soundEmotion(item.identifier) != nil, Date().timeIntervalSince(last) > 1 else { continue }
            last = Date(); found(item.identifier, item.confidence); return
        }
    }
}

// Reconocedor nuevo de Apple (SpeechAnalyzer, iOS/visionOS 26): mejor con frases largas y voces lejanas, sin
// límite de duración. Descarga el modelo del idioma la primera vez.
@available(iOS 26.0, visionOS 26.0, *)
final class ModernTranscriber: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: AsyncStream<AnalyzerInput>.Continuation?
    private var converter: AVAudioConverter?
    private var target: AVAudioFormat?
    private var analyzer: SpeechAnalyzer?
    private var results: Task<Void, Never>?

    func start(locale: Locale, input: AVAudioFormat, status: @escaping (String) -> Void, text: @escaping (Bool, String) -> Void) async throws {
        guard let supported = await SpeechTranscriber.supportedLocale(equivalentTo: locale) else {
            throw NSError(domain: "Traigger", code: 1, userInfo: [NSLocalizedDescriptionKey: "idioma no admitido por el reconocedor nuevo"])
        }
        let transcriber = SpeechTranscriber(locale: supported, transcriptionOptions: [], reportingOptions: [.volatileResults], attributeOptions: [])
        if let install = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
            status("Descargando el idioma para entender frases…")
            try await install.downloadAndInstall()
        }
        guard let target = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber]) else {
            throw NSError(domain: "Traigger", code: 2, userInfo: [NSLocalizedDescriptionKey: "sin formato de audio compatible"])
        }
        let analyzer = SpeechAnalyzer(modules: [transcriber])
        let (stream, continuation) = AsyncStream<AnalyzerInput>.makeStream()
        lock.withLock {
            self.target = target; self.converter = AVAudioConverter(from: input, to: target)
            self.continuation = continuation; self.analyzer = analyzer
        }
        results = Task {
            do {
                for try await result in transcriber.results { text(result.isFinal, String(result.text.characters)) }
            } catch { status("Error de reconocimiento: \(error.localizedDescription)") }
        }
        try await analyzer.start(inputSequence: stream)
    }

    func stop() {
        lock.lock(); continuation?.finish(); continuation = nil; let analyzer = self.analyzer; self.analyzer = nil; lock.unlock()
        results?.cancel()
        Task { await analyzer?.cancelAndFinishNow() }
    }

    // Hilo de audio: convierte al formato que pide el modelo y lo entrega.
    func feed(_ buffer: AVAudioPCMBuffer) {
        lock.lock(); defer { lock.unlock() }
        guard let continuation, let converter, let target else { return }
        let ratio = target.sampleRate / buffer.format.sampleRate
        guard let output = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 32) else { return }
        var given = false
        var error: NSError?
        converter.convert(to: output, error: &error) { _, state in
            if given { state.pointee = .noDataNow; return nil }
            given = true; state.pointee = .haveData; return buffer
        }
        if error == nil, output.frameLength > 0 { continuation.yield(AnalyzerInput(buffer: output)) }
    }
}


#if canImport(FoundationModels)
@available(visionOS 26.0, iOS 26.0, *)
@Generable enum EmotionKind {
    case neutro, contento, triste, enfado, sorpresa, curioso, verguenza
    var name: String {
        switch self {
        case .neutro: "Neutro"; case .contento: "Contento"; case .triste: "Triste"; case .enfado: "Enfado"
        case .sorpresa: "Sorpresa"; case .curioso: "Curioso"; case .verguenza: "Vergüenza"
        }
    }
}
@available(visionOS 26.0, iOS 26.0, *)
@Generable struct EmotionGuess {
    @Guide(description: "La emoción con la que la persona dice la frase")
    var emotion: EmotionKind
    @Guide(description: "Intensidad de la emoción, de 0 (nada) a 10 (máxima)", .range(0...10))
    var intensity: Int
}
#endif

