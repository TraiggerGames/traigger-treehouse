import CoreVideo
import Foundation
import Network
import os.log

// Recibe el vídeo de Traigger desde un iPhone o iPad en la misma Wi-Fi: la cámara se anuncia por Bonjour
// («_traigger-cam._tcp») y el vídeo llega en H.264 (formato en Shared/TraiggerVideo.swift).
// El personaje lo dibuja y anima el propio iPhone, así que en el Mac no hace falta abrir nada.
final class PhoneReceiver {
    private let queue = DispatchQueue(label: "traigger.phone")
    private var listener: NWListener?
    private var connection: NWConnection?
    private let pool: CVPixelBufferPool
    private let width: Int, height: Int
    private let deliver: (CVPixelBuffer) -> Void
    private lazy var decoder = TraiggerH264Decoder { [weak self] pixels in self?.deliver(pixels) }

    init(pool: CVPixelBufferPool, width: Int, height: Int, deliver: @escaping (CVPixelBuffer) -> Void) {
        self.pool = pool; self.width = width; self.height = height; self.deliver = deliver
    }

    func start() {
        do {
            let parameters = NWParameters.tcp
            parameters.includePeerToPeer = true
            let listener = try NWListener(using: parameters)
            listener.service = NWListener.Service(name: Host.current().localizedName ?? "Mac", type: "_traigger-cam._tcp")
            listener.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
            // Si el receptor cae (cambio de red, Mac que despierta), se vuelve a abrir solo a los 2 s.
            listener.stateUpdateHandler = { [weak self, weak listener] state in
                guard case .failed(let error) = state else { return }
                log.error("Receptor del iPhone: \(error.localizedDescription); reabriendo")
                listener?.cancel()
                self?.queue.asyncAfter(deadline: .now() + 2) { self?.listener = nil; self?.start() }
            }
            listener.start(queue: queue)
            self.listener = listener
        } catch { log.error("No se pudo abrir el receptor del iPhone: \(error.localizedDescription)") }
    }

    // Una conexión a la vez: la más reciente sustituye a la anterior.
    private func accept(_ new: NWConnection) {
        connection?.cancel()
        connection = new
        decoder = TraiggerH264Decoder { [weak self] pixels in self?.deliver(pixels) }
        new.start(queue: queue)
        readFrame(new)
    }

    private func readFrame(_ link: NWConnection) {
        link.receive(minimumIncompleteLength: 4, maximumLength: 4) { [weak self] header, _, done, error in
            guard let self, let header, header.count == 4, error == nil else { link.cancel(); return }
            let size = Int(header.withUnsafeBytes { $0.load(as: UInt32.self).bigEndian })
            guard size > 0, size < 8_000_000 else { link.cancel(); return }
            link.receive(minimumIncompleteLength: size, maximumLength: size) { body, _, _, error in
                guard let body, body.count == size, error == nil else { link.cancel(); return }
                self.decoder.decode(body)
                if !done { self.readFrame(link) }
            }
        }
    }
}
