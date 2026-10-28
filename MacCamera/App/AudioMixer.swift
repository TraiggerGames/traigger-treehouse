import AVFoundation
import CoreAudio
import Foundation
import Network

// Micrófono «Traigger»: mezcla tu voz (el micrófono real del Mac) con el sonido de la película que llega del
// iPhone/iPad, y la reproduce en el dispositivo de audio virtual «Traigger» (MacCamera/AudioDriver), que FaceTime usa
// como micrófono. Además puede reproducir la película por tu salida normal para que tú también la oigas.
// Audio de la película: paquetes [UInt32 tamaño][UInt8 3][Int16 estéreo 48 kHz intercalado], por el cable
// (CableBridge) o por la Wi-Fi (canal propio «_traigger-audio._tcp»).
@MainActor final class AudioMixer: ObservableObject {
    static let shared = AudioMixer()
    @Published var enabled = UserDefaults.standard.object(forKey: "traigger.mic") as? Bool ?? true { didSet { UserDefaults.standard.set(enabled, forKey: "traigger.mic"); restart() } }
    @Published var monitor = UserDefaults.standard.object(forKey: "traigger.monitor") as? Bool ?? true { didSet { UserDefaults.standard.set(monitor, forKey: "traigger.monitor"); restart() } }
    @Published var status = "Micrófono Traigger apagado"
    nonisolated let movie = AudioRing()
    private var micEngine: AVAudioEngine?
    private var monitorEngine: AVAudioEngine?
    private var listener: NWListener?
    private var connections: [NWConnection] = []
    private static let format = AVAudioFormat(standardFormatWithSampleRate: 48000, channels: 2)!

    func start() { listenWiFi(); restart() }

    // Instala el dispositivo de audio «Traigger» (va dentro de la app) con la ventana de contraseña de macOS.
    func installDriver() {
        guard let driver = Bundle.main.url(forResource: "TraiggerAudio", withExtension: "driver") else { status = "Falta el controlador dentro de la app"; return }
        let path = driver.path.replacingOccurrences(of: "'", with: "'\\''")
        let script = "do shell script \"rm -rf /Library/Audio/Plug-Ins/HAL/TraiggerAudio.driver && cp -R '\(path)' /Library/Audio/Plug-Ins/HAL/ && killall coreaudiod\" with administrator privileges with prompt \"Traigger quiere instalar su micrófono virtual.\""
        var error: NSDictionary?
        NSAppleScript(source: script)?.executeAndReturnError(&error)
        if let error { status = "No se instaló: \(error[NSAppleScript.errorMessage] ?? "cancelado")"; return }
        status = "Micrófono instalado; arrancando…"
        DispatchQueue.main.asyncAfter(deadline: .now() + 4) { self.restart() }
    }
    var driverInstalled: Bool { Self.device(named: "Traigger", input: false) != nil }

    private func restart() {
        micEngine?.stop(); micEngine = nil
        monitorEngine?.stop(); monitorEngine = nil
        guard enabled else { status = "Micrófono Traigger apagado"; return }
        guard let traigger = Self.device(named: "Traigger", input: false) else {
            status = "Falta el dispositivo de audio «Traigger»: instálalo (MacCamera/AudioDriver/install.sh)"; return
        }
        do {
            // Tu voz + película → salida «Traigger» (que FaceTime oye como micrófono).
            let engine = AVAudioEngine()
            try Self.setDevice(engine.outputNode, traigger)
            if let mic = Self.realMicrophone() { try Self.setDevice(engine.inputNode, mic) }
            let movieNode = AVAudioSourceNode(format: Self.format, renderBlock: movie.render(mirror: false))
            engine.attach(movieNode)
            engine.connect(movieNode, to: engine.mainMixerNode, format: Self.format)
            engine.connect(engine.inputNode, to: engine.mainMixerNode, format: engine.inputNode.inputFormat(forBus: 0))
            try engine.start()
            micEngine = engine
            // Tú también oyes la película por tu salida de siempre (mejor con auriculares, para que tu micrófono no la recoja).
            if monitor {
                let speaker = AVAudioEngine()
                let node = AVAudioSourceNode(format: Self.format, renderBlock: movie.render(mirror: true))
                speaker.attach(node); speaker.connect(node, to: speaker.mainMixerNode, format: Self.format)
                try speaker.start()
                monitorEngine = speaker
            }
            status = "Micrófono «Traigger» activo: tu voz + la película"
        } catch { status = "Micrófono Traigger: \(error.localizedDescription)" }
    }

    // Canal de audio por Wi-Fi (por cable llega dentro del túnel del vídeo).
    private func listenWiFi() {
        guard listener == nil, let listener = try? NWListener(using: .tcp) else { return }
        listener.service = NWListener.Service(name: Host.current().localizedName ?? "Mac", type: "_traigger-audio._tcp")
        listener.newConnectionHandler = { [weak self] link in
            Task { @MainActor in
                guard let self else { return }
                self.connections.append(link)
                link.start(queue: .global(qos: .userInteractive))
                Self.read(link, into: self.movie)
            }
        }
        listener.start(queue: .global())
        self.listener = listener
    }
    nonisolated private static func read(_ link: NWConnection, into ring: AudioRing) {
        link.receive(minimumIncompleteLength: 4, maximumLength: 4) { header, _, done, error in
            guard let header, header.count == 4, error == nil else { link.cancel(); return }
            let size = Int(header.withUnsafeBytes { $0.load(as: UInt32.self).bigEndian })
            guard size > 1, size < 4_000_000 else { link.cancel(); return }
            link.receive(minimumIncompleteLength: size, maximumLength: size) { body, _, _, error in
                guard let body, body.count == size, error == nil else { link.cancel(); return }
                ring.push(packet: body)
                if !done { read(link, into: ring) }
            }
        }
    }

    // MARK: Dispositivos de Core Audio
    nonisolated static func devices() -> [AudioDeviceID] {
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDevices, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size)
        var list = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &list)
        return list
    }
    nonisolated static func name(_ device: AudioDeviceID) -> String {
        var address = AudioObjectPropertyAddress(mSelector: kAudioObjectPropertyName, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var value: Unmanaged<CFString>?; var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        guard AudioObjectGetPropertyData(device, &address, 0, nil, &size, &value) == noErr, let value else { return "" }
        return value.takeRetainedValue() as String
    }
    nonisolated static func hasStreams(_ device: AudioDeviceID, input: Bool) -> Bool {
        var address = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams, mScope: input ? kAudioObjectPropertyScopeInput : kAudioObjectPropertyScopeOutput, mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        return AudioObjectGetPropertyDataSize(device, &address, 0, nil, &size) == noErr && size > 0
    }
    nonisolated static func device(named wanted: String, input: Bool) -> AudioDeviceID? {
        devices().first { name($0) == wanted && hasStreams($0, input: input) }
    }
    // El micrófono por defecto, salvo que sea «Traigger» (se haría un bucle): entonces el primero real con entrada.
    nonisolated static func realMicrophone() -> AudioDeviceID? {
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultInputDevice, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var device = AudioDeviceID(0); var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &device)
        if device != 0, name(device) != "Traigger" { return device }
        return devices().first { name($0) != "Traigger" && hasStreams($0, input: true) }
    }
    nonisolated static func setDevice(_ node: AVAudioIONode, _ device: AudioDeviceID) throws {
        guard let unit = node.audioUnit else { return }
        var id = device
        let status = AudioUnitSetProperty(unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &id, UInt32(MemoryLayout<AudioDeviceID>.size))
        if status != noErr { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
    }
}

// Anillo con el audio de la película (estéreo float, 48 kHz). Lo leen dos motores: el del micrófono «Traigger» y el
// de tus altavoces; cada uno con su propio cursor de lectura.
final class AudioRing: @unchecked Sendable {
    private let lock = NSLock()
    private var samples = [Float](repeating: 0, count: 48000 * 2 * 4)
    private var write = 0
    private var reads = [0, 0]

    func push(packet: Data) {
        guard packet.first == 3 else { return }
        let body = packet.dropFirst()
        lock.lock(); defer { lock.unlock() }
        body.withUnsafeBytes { raw in
            let pcm = raw.bindMemory(to: Int16.self)
            for value in pcm { samples[write] = Float(Int16(littleEndian: value)) / 32768; write = (write + 1) % samples.count }
        }
        // Si un lector se queda muy atrás (más de 0,5 s), se le acerca para no acumular retraso.
        for i in reads.indices {
            let behind = (write - reads[i] + samples.count) % samples.count
            if behind > 48000 { reads[i] = (write - 9600 + samples.count) % samples.count }
        }
    }

    func render(mirror: Bool) -> AVAudioSourceNodeRenderBlock {
        let reader = mirror ? 1 : 0
        return { [weak self] _, _, frames, buffers in
            guard let self else { return noErr }
            let list = UnsafeMutableAudioBufferListPointer(buffers)
            self.lock.lock(); defer { self.lock.unlock() }
            for frame in 0..<Int(frames) {
                let available = (self.write - self.reads[reader] + self.samples.count) % self.samples.count
                for channel in 0..<min(list.count, 2) {
                    let out = list[channel].mData!.assumingMemoryBound(to: Float.self)
                    out[frame] = available >= 2 ? self.samples[(self.reads[reader] + channel) % self.samples.count] : 0
                }
                if available >= 2 { self.reads[reader] = (self.reads[reader] + 2) % self.samples.count }
            }
            return noErr
        }
    }
}
