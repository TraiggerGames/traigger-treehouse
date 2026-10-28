import AVFoundation
import CoreMedia
import Foundation
import Speech
#if canImport(Translation) && !os(visionOS)
import Translation
#endif

// Subtítulos generados para el cine de Traigger: el audio de la película llega por delante de la imagen
// (TraiggerMoviePlayer.m, escucha adelantada) con su posición en la película; el reconocedor de voz de Apple
// (SpeechAnalyzer, en el dispositivo) lo transcribe con marcas de tiempo y, si se pide, el traductor de Apple
// (Translation) traduce cada frase. Unity pregunta qué texto toca en cada momento (TraiggerSubtitlesText).
// Requiere iOS, visionOS o macOS 26.
final class TraiggerSubtitles: @unchecked Sendable {
    static let shared = TraiggerSubtitles()
    struct Cue { var start: Double; var end: Double; var text: String; var translated: String? }
    private let lock = NSLock()
    private var cues: [Cue] = []
    private var statusText = "Apagados"
    private var language = "", translateTo = ""
    private var recognized = ""   // idioma que de verdad usa el reconocedor (origen de la traducción)
    func setRecognized(_ identifier: String) { lock.withLock { recognized = identifier } }
    private var engine: AnyObject?
    private var generation = 0

    var status: String { lock.withLock { statusText } }
    func setStatus(_ value: String) { lock.withLock { statusText = value } }

    func configure(language: String, translateTo: String) {
        let retranslate = lock.withLock { () -> [Cue] in
            if language != self.language { cues.removeAll() }
            let changed = translateTo != self.translateTo
            self.language = language; self.translateTo = translateTo
            if changed { for i in cues.indices { cues[i].translated = nil } }
            return changed && !translateTo.isEmpty ? cues : []
        }
        // Cambió el idioma de destino: se traducen también los subtítulos que ya había.
        if !retranslate.isEmpty { translate(retranslate, to: translateTo) }
    }
    func clear() { lock.withLock { cues.removeAll(); generation += 1 }; stopEngine() }

    // El audio empieza de nuevo en «time» (inicio o salto): se empieza otra transcripción con esa referencia.
    func reset(at time: Double) {
        stopEngine()
        let (code, id) = lock.withLock { generation += 1; return (language, generation) }
        guard #available(iOS 26.0, visionOS 26.0, macOS 26.0, *) else { setStatus("Los subtítulos generados requieren iOS, visionOS o macOS 26"); return }
        let engine = SubtitleEngine(start: time, language: code) { [weak self] start, end, text in self?.add(start, end, text, id) } status: { [weak self] in self?.setStatus($0) }
        lock.withLock { self.engine = engine }
        engine.begin()
    }
    private func stopEngine() {
        let old = lock.withLock { () -> AnyObject? in let e = engine; engine = nil; return e }
        if #available(iOS 26.0, visionOS 26.0, macOS 26.0, *), let old = old as? SubtitleEngine { old.stop() }
    }

    func finish() {
        let engine = lock.withLock { self.engine }
        if #available(iOS 26.0, visionOS 26.0, macOS 26.0, *), let engine = engine as? SubtitleEngine { engine.finish() }
    }

    func feed(_ samples: UnsafePointer<Int16>, _ count: Int, _ time: Double) {
        let engine = lock.withLock { self.engine }
        if #available(iOS 26.0, visionOS 26.0, macOS 26.0, *), let engine = engine as? SubtitleEngine { engine.feed(samples, count, time) }
    }

    // Frase reconocida: se parte en trozos de dos líneas como mucho, repartiendo su tiempo.
    private func add(_ start: Double, _ end: Double, _ text: String, _ id: Int) {
        // Primero por frases (. ? ! …) y, si alguna sigue siendo larga, en trozos de dos líneas.
        var sentences: [String] = [], current = ""
        for character in text {
            current.append(character)
            if ".?!…".contains(character) { sentences.append(current.trimmingCharacters(in: .whitespaces)); current = "" }
        }
        if !current.trimmingCharacters(in: .whitespaces).isEmpty { sentences.append(current.trimmingCharacters(in: .whitespaces)) }
        var pieces: [String] = []
        for sentence in sentences {
            var line = ""
            for word in sentence.split(separator: " ") {
                if line.count + word.count + 1 > 84, !line.isEmpty { pieces.append(line); line = "" }
                line += (line.isEmpty ? "" : " ") + word
            }
            if !line.isEmpty { pieces.append(line) }
        }
        guard !pieces.isEmpty else { return }
        let total = Double(pieces.reduce(0) { $0 + $1.count }), span = max(end - start, 0.8)
        var cursor = start
        var added: [Cue] = []
        for piece in pieces {
            let length = span * Double(piece.count) / max(total, 1)
            added.append(Cue(start: cursor, end: cursor + length, text: piece)); cursor += length
        }
        let target = lock.withLock { () -> String? in
            guard id == generation else { return nil }
            cues.removeAll { $0.start < cursor && $0.end > start }
            cues.append(contentsOf: added); cues.sort { $0.start < $1.start }
            return translateTo
        }
        guard let target, !target.isEmpty else { return }
        translate(added, to: target)
    }

    private func translate(_ added: [Cue], to target: String) {
        // visionOS no tiene el traductor de Apple: allí los subtítulos salen en el idioma de la película.
        #if canImport(Translation) && !os(visionOS)
        guard #available(iOS 26.0, macOS 26.0, *) else { return }
        // Origen: el idioma que reconoce el transcriptor (no se supone ninguno). Si coincide con el destino, no se traduce.
        let source = lock.withLock { recognized.isEmpty ? language : recognized }
        let from = Locale.Language(identifier: source.isEmpty ? Locale.current.identifier : source)
        let to = Locale.Language(identifier: target)
        guard from.languageCode != to.languageCode else { setStatus("La película ya está en ese idioma: no hace falta traducir"); return }
        Task {
            let session = TranslationSession(installedSource: from, target: to)
            for cue in added {
                let result: TranslationSession.Response
                do { result = try await session.translate(cue.text) }
                catch { self.setStatus("No se pudo traducir (\(from.minimalIdentifier) → \(to.minimalIdentifier)): \(error.localizedDescription). Comprueba en Ajustes del sistema → General → Idioma y región → Idiomas de traducción que estén descargados."); return }
                self.setStatus("Traduciendo \(from.minimalIdentifier) → \(to.minimalIdentifier)")
                lock.withLock { if let index = cues.firstIndex(where: { $0.start == cue.start && $0.text == cue.text }) { cues[index].translated = result.targetText } }
            }
        }
        #else
        setStatus("En Vision Pro no hay traductor de Apple: subtítulos en el idioma original")
        #endif
    }

    // Texto que toca en este momento de la película (o nil).
    func text(at time: Double) -> String? {
        lock.withLock {
            guard let cue = cues.last(where: { $0.start - 0.15 <= time && time <= $0.end + 0.8 }) else { return nil }
            return translateTo.isEmpty ? cue.text : (cue.translated ?? cue.text)
        }
    }
}

// Transcripción con SpeechAnalyzer: el audio entra con su posición en la película como marca de tiempo, así cada
// frase reconocida trae directamente su momento.
@available(iOS 26.0, visionOS 26.0, macOS 26.0, *)
final class SubtitleEngine: @unchecked Sendable {
    private let start: Double, language: String
    private let output: (Double, Double, String) -> Void
    private let status: (String) -> Void
    private let lock = NSLock()
    private var continuation: AsyncStream<AnalyzerInput>.Continuation?
    private var converter: AVAudioConverter?
    private var target: AVAudioFormat?
    private var analyzer: SpeechAnalyzer?
    private var task: Task<Void, Never>?
    private var stamped = false
    // Audio que llega mientras el reconocedor arranca (1–2 s): se guarda para no perder la primera frase.
    private var pending: [(Data, Double)] = []
    private let source = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16000, channels: 1, interleaved: true)!

    init(start: Double, language: String, output: @escaping (Double, Double, String) -> Void, status: @escaping (String) -> Void) {
        self.start = start; self.language = language; self.output = output; self.status = status
    }

    func begin() {
        task = Task {
            do {
                let wanted = language.isEmpty ? Locale.current : Locale(identifier: language)
                guard let locale = await SpeechTranscriber.supportedLocale(equivalentTo: wanted) else {
                    status("El reconocedor de Apple no admite el idioma «\(language)»"); return
                }
                let transcriber = SpeechTranscriber(locale: locale, transcriptionOptions: [], reportingOptions: [], attributeOptions: [.audioTimeRange])
                if let install = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
                    status("Descargando el idioma para los subtítulos…")
                    try await install.downloadAndInstall()
                }
                guard let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber]) else { status("Sin formato de audio compatible"); return }
                let analyzer = SpeechAnalyzer(modules: [transcriber])
                let (stream, continuation) = AsyncStream<AnalyzerInput>.makeStream()
                let waiting = lock.withLock { () -> [(Data, Double)] in
                    self.target = format; self.converter = AVAudioConverter(from: source, to: format); self.continuation = continuation; self.analyzer = analyzer
                    defer { pending.removeAll() }; return pending }
                try await analyzer.start(inputSequence: stream)
                for (data, time) in waiting { data.withUnsafeBytes { feed($0.bindMemory(to: Int16.self).baseAddress!, data.count / 2, time) } }
                TraiggerSubtitles.shared.setRecognized(locale.identifier)
                status("Generando subtítulos (\(locale.identifier))")
                for try await result in transcriber.results {
                    let text = String(result.text.characters).trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !text.isEmpty else { continue }
                    output(result.range.start.seconds, result.range.end.seconds, text)
                }
            } catch is CancellationError {
            } catch { status("Subtítulos: \(error.localizedDescription)") }
        }
    }

    // No llega más audio: el reconocedor cierra la frase que tuviera a medias.
    func finish() {
        let (continuation, analyzer) = lock.withLock { () -> (AsyncStream<AnalyzerInput>.Continuation?, SpeechAnalyzer?) in
            let c = self.continuation; self.continuation = nil; return (c, self.analyzer) }
        continuation?.finish()
        Task { try? await analyzer?.finalizeAndFinishThroughEndOfInput() }
    }

    func stop() {
        let (continuation, analyzer) = lock.withLock { () -> (AsyncStream<AnalyzerInput>.Continuation?, SpeechAnalyzer?) in
            let c = self.continuation, a = self.analyzer; self.continuation = nil; self.analyzer = nil; return (c, a) }
        continuation?.finish(); task?.cancel()
        Task { await analyzer?.cancelAndFinishNow() }
    }

    // Hilo de audio de VLC: convierte al formato del reconocedor y lo entrega con su momento de la película.
    func feed(_ samples: UnsafePointer<Int16>, _ count: Int, _ time: Double) {
        lock.lock(); defer { lock.unlock() }
        if continuation == nil, pending.count < 2000 { pending.append((Data(bytes: samples, count: count * 2), time)); return }
        guard let continuation, let converter, let target, count > 0,
              let input = AVAudioPCMBuffer(pcmFormat: source, frameCapacity: AVAudioFrameCount(count)) else { return }
        input.frameLength = AVAudioFrameCount(count)
        memcpy(input.int16ChannelData![0], samples, count * 2)
        let capacity = AVAudioFrameCount(Double(count) * target.sampleRate / 16000) + 64
        guard let converted = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        var given = false, error: NSError?
        converter.convert(to: converted, error: &error) { _, state in
            if given { state.pointee = .noDataNow; return nil }
            given = true; state.pointee = .haveData; return input
        }
        guard error == nil, converted.frameLength > 0 else { return }
        // Solo el primer trozo lleva su momento de la película; los siguientes van seguidos (el reconocedor lleva la cuenta).
        if stamped { continuation.yield(AnalyzerInput(buffer: converted)) }
        else { stamped = true; continuation.yield(AnalyzerInput(buffer: converted, bufferStartTime: CMTime(seconds: time, preferredTimescale: 16000))) }
    }
}

@_cdecl("TraiggerSubtitleFeed") public func TraiggerSubtitleFeed(_ samples: UnsafePointer<Int16>, _ count: Int32, _ time: Double) {
    TraiggerSubtitles.shared.feed(samples, Int(count), time)
}
@_cdecl("TraiggerSubtitleFinish") public func TraiggerSubtitleFinish() { TraiggerSubtitles.shared.finish() }
@_cdecl("TraiggerSubtitleReset") public func TraiggerSubtitleReset(_ time: Double) { TraiggerSubtitles.shared.reset(at: time) }
@_cdecl("TraiggerSubtitlesConfigure") public func TraiggerSubtitlesConfigure(_ language: UnsafePointer<CChar>, _ translate: UnsafePointer<CChar>) {
    TraiggerSubtitles.shared.configure(language: String(cString: language), translateTo: String(cString: translate))
}
@_cdecl("TraiggerSubtitlesClear") public func TraiggerSubtitlesClear() { TraiggerSubtitles.shared.clear() }
@_cdecl("TraiggerSubtitlesText") public func TraiggerSubtitlesText(_ time: Double) -> UnsafeMutablePointer<CChar>? {
    TraiggerSubtitles.shared.text(at: time).flatMap { strdup($0) }
}
@_cdecl("TraiggerSubtitlesStatus") public func TraiggerSubtitlesStatus() -> UnsafeMutablePointer<CChar>? { strdup(TraiggerSubtitles.shared.status) }
