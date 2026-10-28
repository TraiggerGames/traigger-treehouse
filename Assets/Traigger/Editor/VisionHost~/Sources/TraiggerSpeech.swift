import AVFoundation
import Foundation

// Frases, emoción y sonidos en Vision Pro: usa TraiggerListen.swift (Assets/Traigger/Plugins/iOS), el mismo módulo
// que el iPhone. integrate_host.rb lo copia junto a este archivo. Todo ocurre en el casco.
@MainActor final class TraiggerSpeech {
    static let shared = TraiggerSpeech()
    nonisolated static let listen = TraiggerListen { message in
        guard let data = try? JSONSerialization.data(withJSONObject: message), let json = String(data: data, encoding: .utf8) else { return }
        Task { @MainActor in TraiggerUnity.shared.send(json) }
    }
    func start(format: AVAudioFormat) { Self.listen.start(format: format) }
    func status(_ value: String) { Self.listen.status(value) }
    func setLanguage(_ code: String) { Self.listen.setLanguage(code) }
}
