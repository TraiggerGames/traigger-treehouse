import CoreVideo
import Foundation

// Vídeo de Traigger por el cable: cuando conectas un iPhone/iPad por USB, abre un túnel al puerto de Traigger
// (TraiggerMacCamera.swift en iOS, 47815), decodifica el vídeo H.264 (Shared/TraiggerVideo.swift) y lo pasa a la cámara
// «Traigger» por su entrada. Si Traigger no está abierto en el aparato, lo reintenta cada segundo.
@MainActor final class CableBridge: ObservableObject {
    static let port: UInt16 = 47815
    @Published var status = "Conecta tu iPhone o iPad por cable"
    private var active = Set<Int>()

    func start() {
        UsbMux.watch(attached: { device in Task { @MainActor in self.attached(device) } },
                     detached: { id in Task { @MainActor in self.active.remove(id); if self.active.isEmpty { self.status = "Conecta tu iPhone o iPad por cable" } } })
    }

    private func attached(_ device: UsbMux.Device) {
        guard active.insert(device.id).inserted else { return }
        status = "Aparato conectado: abre Traigger y activa «Enviar al Mac»"
        let id = device.id
        Thread.detachNewThread {
            while true {
                let still = DispatchQueue.main.sync { MainActor.assumeIsolated { self.active.contains(id) } }
                guard still else { return }
                guard let fd = UsbMux.tunnel(to: id, port: Self.port) else { sleep(1); continue }
                // Si el aparato se duerme o pasa a segundo plano, el túnel no se cierra: sin datos en 3 s se da por
                // muerto y se vuelve a abrir cada segundo hasta que Traigger vuelva a enviar.
                var timeout = timeval(tv_sec: 3, tv_usec: 0)
                setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
                Task { @MainActor in self.status = "Recibiendo por cable" }
                // Diagnóstico: paquetes de vídeo recibidos, fotogramas decodificados y entregados a la cámara.
                var frames = 0, packets = 0, decoded = 0, since = Date()
                let decoder = TraiggerH264Decoder { pixels in decoded += 1; frames += Int(VirtualCamSender.shared.send(pixels)) }
                while let header = UsbMux.readExactly(fd, 4) {
                    let size = Int(header.withUnsafeBytes { $0.load(as: UInt32.self).bigEndian })
                    guard size > 0, size < 8_000_000, let packet = UsbMux.readExactly(fd, size) else { break }
                    // Tipo 3: audio de la película (para el micrófono «Traigger»); el resto, vídeo.
                    if packet.first == 3 { AudioMixer.shared.movie.push(packet: packet) } else { packets += 1; decoder.decode(packet) }
                    if Date().timeIntervalSince(since) > 2 {
                        let seconds = Date().timeIntervalSince(since)
                        let line: String
                        if packets == 0 { line = "Conectado por cable, pero el aparato no envía vídeo (¿«Enviar al Mac» activado en Traigger?)" }
                        else if decoded == 0 { line = "Llega vídeo por cable (\(packets) paquetes) pero no se puede decodificar: actualiza Traigger en el aparato" }
                        else if frames == 0 { line = "Vídeo decodificado, pero la cámara «Traigger» no lo acepta: pulsa «Instalar cámara»" }
                        else { line = String(format: "Recibiendo por cable · %.0f fps", Double(frames) / seconds) }
                        frames = 0; packets = 0; decoded = 0; since = Date()
                        Task { @MainActor in self.status = line }
                    }
                }
                close(fd)
                Task { @MainActor in if self.active.contains(id) { self.status = "Esperando a Traigger en el aparato (¿en reposo o en segundo plano?); reconectando…" } }
                sleep(1)
            }
        }
    }
}
