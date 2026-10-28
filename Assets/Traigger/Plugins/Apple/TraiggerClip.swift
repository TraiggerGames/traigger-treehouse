import AVFoundation
import CoreVideo
import Foundation

// Clips de vídeo para los carteles (título o fondo), en bucle y sin sonido, conservando la transparencia: HEVC con
// canal alfa o ProRes 4444 en .mov (lo que exportan Final Cut, Motion, After Effects o DaVinci). También vale
// cualquier MP4/MOV normal, sin transparencia. Unity (StreamCards.cs) recoge cada fotograma nuevo en BGRA.
final class TraiggerClip: @unchecked Sendable {
    private let player = AVPlayer()
    private var loopObserver: NSObjectProtocol?
    private let output = AVPlayerItemVideoOutput(pixelBufferAttributes: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA])
    private let lock = NSLock()
    private var latest: CVPixelBuffer?
    private var serial: Int64 = -1
    private var timer: DispatchSourceTimer?

    init?(path: String) {
        let url = URL(fileURLWithPath: path)
        guard FileManager.default.fileExists(atPath: path) else { return nil }
        let item = AVPlayerItem(url: url)
        item.add(output)
        player.replaceCurrentItem(with: item)
        player.actionAtItemEnd = .none
        // En bucle: al terminar vuelve al principio (una salida de vídeo solo puede ir en un elemento).
        loopObserver = NotificationCenter.default.addObserver(forName: AVPlayerItem.didPlayToEndTimeNotification, object: item, queue: nil) { [weak self] _ in
            self?.player.seek(to: .zero); self?.player.play()
        }
        player.isMuted = true
        player.play()
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue(label: "traigger.clip"))
        timer.schedule(deadline: .now(), repeating: .milliseconds(16))
        timer.setEventHandler { [weak self] in self?.poll() }
        timer.resume(); self.timer = timer
    }
    deinit { timer?.cancel(); player.pause(); if let loopObserver { NotificationCenter.default.removeObserver(loopObserver) } }

    private func poll() {
        let time = output.itemTime(forHostTime: CACurrentMediaTime())
        guard output.hasNewPixelBuffer(forItemTime: time), let pixels = output.copyPixelBuffer(forItemTime: time, itemTimeForDisplay: nil) else { return }
        lock.withLock { latest = pixels; serial += 1 }
    }
    func frameInfo(_ width: UnsafeMutablePointer<Int32>, _ height: UnsafeMutablePointer<Int32>) -> Int64 {
        lock.withLock {
            guard let latest else { width.pointee = 0; height.pointee = 0; return -1 }
            width.pointee = Int32(CVPixelBufferGetWidth(latest)); height.pointee = Int32(CVPixelBufferGetHeight(latest))
            return serial
        }
    }
    func copyFrame(_ destination: UnsafeMutablePointer<UInt8>, _ capacity: Int32) -> Int32 {
        guard let pixels = lock.withLock({ latest }) else { return 0 }
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }
        let width = CVPixelBufferGetWidth(pixels), height = CVPixelBufferGetHeight(pixels), stride = CVPixelBufferGetBytesPerRow(pixels), row = width * 4
        guard Int(capacity) >= row * height, let base = CVPixelBufferGetBaseAddress(pixels) else { return 0 }
        for y in 0..<height { memcpy(destination + y * row, base + y * stride, row) }
        return 1
    }
}

// Ranuras: 0 = título del cartel, 1 = fondo del cartel.
private let clipLock = NSLock()
nonisolated(unsafe) private var clips: [Int32: TraiggerClip] = [:]

@_cdecl("TraiggerClipOpen") public func TraiggerClipOpen(_ slot: Int32, _ path: UnsafePointer<CChar>) -> Int32 {
    let clip = TraiggerClip(path: String(cString: path))
    clipLock.withLock { clips[slot] = clip }
    return clip == nil ? 0 : 1
}
@_cdecl("TraiggerClipClose") public func TraiggerClipClose(_ slot: Int32) { clipLock.withLock { clips[slot] = nil } }
@_cdecl("TraiggerClipFrameInfo") public func TraiggerClipFrameInfo(_ slot: Int32, _ width: UnsafeMutablePointer<Int32>, _ height: UnsafeMutablePointer<Int32>) -> Int64 {
    guard let clip = clipLock.withLock({ clips[slot] }) else { width.pointee = 0; height.pointee = 0; return -1 }
    return clip.frameInfo(width, height)
}
@_cdecl("TraiggerClipCopyFrame") public func TraiggerClipCopyFrame(_ slot: Int32, _ destination: UnsafeMutablePointer<UInt8>, _ capacity: Int32) -> Int32 {
    guard let clip = clipLock.withLock({ clips[slot] }) else { return 0 }
    return clip.copyFrame(destination, capacity)
}
