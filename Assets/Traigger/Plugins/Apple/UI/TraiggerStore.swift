import Combine
import Foundation
import Network

// Modelo de la interfaz nativa de Traigger. El estado llega en JSON desde Unity (StudioController.Bridge.cs) y las
// órdenes vuelven como {"cmd","i","f","b","s"}. La lógica vive en C#; aquí solo se muestra y se pide.
// Compartido por iPhone/iPad (dentro de Unity), Vision Pro (host SwiftUI) y la app Traigger Control del Mac.

struct TraiggerItem: Decodable, Hashable { var name: String?; var url: String?; var folder: Bool? }
struct TraiggerTrack: Decodable, Hashable { var id: String?; var name: String?; var language: String?; var selected: Bool? }
struct TraiggerCamera: Decodable, Hashable { var id: Int; var name: String }
struct TraiggerCaptureDevice: Decodable, Hashable { var id: String; var name: String }
struct TraiggerOutfit: Decodable, Hashable { var id: String?; var name: String?; var preview: String? }
struct TraiggerOutfitSwitch: Decodable, Hashable { var id: String?; var label: String?; var on: Bool? }
struct TraiggerCharacter: Decodable, Hashable { var name: String?; var path: String?; var author: String?; var version: String?; var license: String?; var preview: String?; var builtIn: Bool? }
struct TraiggerControl: Decodable, Hashable { var group: String; var key: String; var label: String; var kind: String; var min: Double; var max: Double; var value: Double }

struct TraiggerState: Decodable {
    var platform: String?
    var expressions: [String]?; var expression: Int?
    var demo: Bool?; var tracking: Bool?; var naturalSpeech: Bool?; var voiceEmotion: Bool?; var voiceEmotionStrength: Double?
    var emotion: String?; var intonation: String?
    var speechLanguage: Int?; var speechLanguages: [String]?
    var mouth: String?; var phrases: String?; var heard: String?; var face: String?; var body: String?; var visionSensors: String?
    var visionAvailable: Bool?; var visionImmersive: Bool?
    var cameras: [TraiggerCamera]?; var cameraMode: Int?; var saveBattery: Bool?; var invertLegs: Bool?
    var virtualCameraAvailable: Bool?; var virtualCamera: Bool?; var virtualCameraStatus: String?
    var voiceProfile: [String]?; var voiceCalibrated: [Bool]?; var voiceStatus: String?; var voiceSync: String?; var voiceUsesProfile: Bool?
    var layout: String?; var cinemaLayout: Bool?; var avatarHidden: Bool?
    var movieAvailable: Bool?; var moviePlaying: Bool?; var moviePaused: Bool?; var movieBrowsing: Bool?; var movieBackground: Bool?
    var movieStatus: String?; var movieTitle: String?; var movieFolder: String?
    var movieTime: Double?; var movieLength: Double?; var movieVolume: Int?; var movieOpacity: Double?
    var movieItems: [TraiggerItem]?; var audioTracks: [TraiggerTrack]?; var subtitleTracks: [TraiggerTrack]?
    var generatedSubtitles: Bool?; var generatedStatus: String?; var subtitleLanguage: String?; var translateTo: String?
    var controls: [TraiggerControl]?; var backgroundImage: String?; var backgroundSky: String?
    var webUrl: String?; var webTitle: String?; var webImmersive: Bool?; var cinemaWindow: Bool?; var cinemaImmersive: Bool?; var cinemaScale: Double?
    var characters: [TraiggerCharacter]?; var character: String?; var characterMessage: String?
    var outfits: [TraiggerOutfit]?; var outfit: String?; var outfitOptions: [TraiggerOutfitSwitch]?
    var streamSupported: Bool?; var streamLive: Bool?; var streamStatus: String?
    var screenAspect: Double?; var layerAvatar: [Double]?; var layerMovie: [Double]?; var layerCapture: [Double]?
    var scenes: [String]?; var scene: String?; var cardKind: String?; var cardTitle: String?; var cardSubtitle: String?; var cardImage: String?; var cardCover: Bool?
    var captureAvailable: Bool?; var captureOn: Bool?; var captureAudio: Bool?; var captureToVision: Bool?; var captureBackground: Bool?; var captureFront: Bool?; var captureOpacity: Double?; var captureScale: Double?; var movieScale: Double?; var captureWindow: Bool?; var captureStatus: String?; var captureDevice: String?
    var captureDevices: [TraiggerCaptureDevice]?
}

// Cómo llegan y salen los mensajes: dentro de la app (funciones nativas) o por red (editor de Unity).
protocol TraiggerTransport: AnyObject {
    var onMessage: ((String) -> Void)? { get set }
    var onConnection: ((Bool) -> Void)? { get set }
    func send(_ json: String)
}

@MainActor final class TraiggerStore: ObservableObject {
    @Published private(set) var state = TraiggerState()
    @Published private(set) var connected = false
    // Interfaz oculta del todo (nada en pantalla ni en la cámara «Traigger»).
    @Published var hidden = UserDefaults.standard.bool(forKey: "traigger.ui.hidden") { didSet { UserDefaults.standard.set(hidden, forKey: "traigger.ui.hidden") } }
    // Controles de reproducción (TraiggerPlayerControls): visibles un rato tras tocar; ocultos a mano hasta volver a tocar.
    @Published var playerControlsShown = false
    @Published var playerControlsHidden = false
    var playerControlsTouched = Date.distantPast
    let transport: TraiggerTransport
    private let decoder = JSONDecoder()

    init(transport: TraiggerTransport) {
        self.transport = transport
        transport.onMessage = { [weak self] json in Task { @MainActor in self?.receive(json) } }
        transport.onConnection = { [weak self] value in Task { @MainActor in self?.connected = value } }
    }

    func receive(_ json: String) {
        guard let data = json.data(using: .utf8), let decoded = try? decoder.decode(TraiggerState.self, from: data) else { return }
        let started = decoded.moviePlaying == true && state.moviePlaying != true
        state = decoded; connected = true
        if started { showPlayerControls() }
    }

    func send(_ cmd: String, i: Int = 0, f: Double = 0, b: Bool = false, s: String = "") {
        let message: [String: Any] = ["cmd": cmd, "i": i, "f": f, "b": b, "s": s]
        guard let data = try? JSONSerialization.data(withJSONObject: message), let json = String(data: data, encoding: .utf8) else { return }
        transport.send(json)
    }
}

// Conexión por red con Traigger en el editor de Unity o en el Mac (puerto 47820, una línea JSON por mensaje).
final class TraiggerSocketTransport: TraiggerTransport, @unchecked Sendable {
    var onMessage: ((String) -> Void)?
    var onConnection: ((Bool) -> Void)?
    private let queue = DispatchQueue(label: "traigger.ui.socket")
    private var connection: NWConnection?
    private var buffer = Data()
    let host: String, port: UInt16

    init(host: String = "127.0.0.1", port: UInt16 = 47820) { self.host = host; self.port = port; queue.async { self.connect() } }

    private func connect() {
        let link = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!, using: .tcp)
        link.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready: self.onConnection?(true); self.read(link)
            case .failed, .cancelled, .waiting:
                self.onConnection?(false); link.cancel()
                if self.connection === link { self.connection = nil; self.queue.asyncAfter(deadline: .now() + 1) { self.connect() } }
            default: break
            }
        }
        connection = link; buffer.removeAll()
        link.start(queue: queue)
    }

    private func read(_ link: NWConnection) {
        link.receive(minimumIncompleteLength: 1, maximumLength: 1 << 20) { [weak self] data, _, done, error in
            guard let self else { return }
            if let data { self.buffer.append(data) }
            while let newline = self.buffer.firstIndex(of: 10) {
                let line = self.buffer.subdata(in: self.buffer.startIndex..<newline)
                self.buffer.removeSubrange(self.buffer.startIndex...newline)
                if let text = String(data: line, encoding: .utf8), !text.isEmpty { self.onMessage?(text) }
            }
            if done || error != nil { link.cancel() } else { self.read(link) }
        }
    }

    func send(_ json: String) {
        queue.async { self.connection?.send(content: Data((json + "\n").utf8), completion: .idempotent) }
    }
}
