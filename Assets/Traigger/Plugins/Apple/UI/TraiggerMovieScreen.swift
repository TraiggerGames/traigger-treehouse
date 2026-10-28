import AVFoundation
import SwiftUI

// Pantalla de película nativa: muestra los fotogramas que libVLC deja en memoria (TraiggerMoviePlayer.m) con la capa
// de vídeo de Apple, sin pasar por Unity, para la ventana propia de Vision Pro y la pantalla de cine inmersiva.
// Las funciones del reproductor se buscan en el propio proceso (están en UnityFramework).
#if os(iOS) || os(visionOS)
import UIKit

private typealias FrameInfo = @convention(c) (UnsafeMutablePointer<Int32>, UnsafeMutablePointer<Int32>) -> Int64
private typealias CopyFrame = @convention(c) (UnsafeMutablePointer<UInt8>, Int32) -> Int32
private let frameInfo: FrameInfo? = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "TraiggerMovieFrameInfo").map { unsafeBitCast($0, to: FrameInfo.self) }
private let copyFrame: CopyFrame? = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "TraiggerMovieCopyFrame").map { unsafeBitCast($0, to: CopyFrame.self) }

final class TraiggerMovieLayerView: UIView {
    override class var layerClass: AnyClass { AVSampleBufferDisplayLayer.self }
    private var display: AVSampleBufferDisplayLayer { layer as! AVSampleBufferDisplayLayer }
    private var link: CADisplayLink?
    private var last: Int64 = -1
    private var scratch = [UInt8]()
    private var pool: CVPixelBufferPool?
    private var poolSize = (0, 0)
    var onAspect: ((CGFloat) -> Void)?

    override init(frame: CGRect) {
        super.init(frame: frame)
        display.videoGravity = .resizeAspect
        backgroundColor = .black
    }
    required init?(coder: NSCoder) { fatalError() }
    override func didMoveToWindow() {
        super.didMoveToWindow()
        link?.invalidate(); link = nil
        if window != nil { let link = CADisplayLink(target: self, selector: #selector(tick)); link.add(to: .main, forMode: .common); self.link = link }
    }

    @objc private func tick() {
        guard let frameInfo, let copyFrame else { return }
        var width: Int32 = 0, height: Int32 = 0
        let serial = frameInfo(&width, &height)
        guard serial >= 0, serial != last, width > 0, height > 0 else { return }
        last = serial
        let bytes = Int(width) * Int(height) * 4
        if scratch.count != bytes { scratch = [UInt8](repeating: 0, count: bytes) }
        guard scratch.withUnsafeMutableBufferPointer({ copyFrame($0.baseAddress!, Int32(bytes)) }) == 1 else { return }
        if pool == nil || poolSize != (Int(width), Int(height)) {
            let attributes: [String: Any] = [kCVPixelBufferWidthKey as String: Int(width), kCVPixelBufferHeightKey as String: Int(height),
                                             kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA, kCVPixelBufferIOSurfacePropertiesKey as String: [:]]
            pool = nil; CVPixelBufferPoolCreate(nil, nil, attributes as CFDictionary, &pool); poolSize = (Int(width), Int(height))
            onAspect?(CGFloat(width) / CGFloat(height))
        }
        var buffer: CVPixelBuffer?
        guard let pool, CVPixelBufferPoolCreatePixelBuffer(nil, pool, &buffer) == kCVReturnSuccess, let buffer else { return }
        CVPixelBufferLockBaseAddress(buffer, [])
        let row = Int(width) * 4, stride = CVPixelBufferGetBytesPerRow(buffer), base = CVPixelBufferGetBaseAddress(buffer)!
        scratch.withUnsafeBytes { source in for y in 0..<Int(height) { memcpy(base + y * stride, source.baseAddress! + y * row, row) } }
        CVPixelBufferUnlockBaseAddress(buffer, [])
        var format: CMFormatDescription?
        CMVideoFormatDescriptionCreateForImageBuffer(allocator: nil, imageBuffer: buffer, formatDescriptionOut: &format)
        var timing = CMSampleTimingInfo(duration: .invalid, presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()), decodeTimeStamp: .invalid)
        var sample: CMSampleBuffer?
        guard let format, CMSampleBufferCreateReadyWithImageBuffer(allocator: nil, imageBuffer: buffer, formatDescription: format, sampleTiming: &timing, sampleBufferOut: &sample) == noErr,
              let sample else { return }
        if let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true) as? [NSMutableDictionary] {
            attachments.first?[kCMSampleAttachmentKey_DisplayImmediately] = true
        }
        if display.status == .failed { display.flush() }
        display.enqueue(sample)
    }
}

struct TraiggerMovieScreen: UIViewRepresentable {
    var onAspect: ((CGFloat) -> Void)? = nil
    func makeUIView(context: Context) -> TraiggerMovieLayerView { let view = TraiggerMovieLayerView(); view.onAspect = onAspect; return view }
    func updateUIView(_ view: TraiggerMovieLayerView, context: Context) {}
}
#else
struct TraiggerMovieScreen: View {
    var onAspect: ((CGFloat) -> Void)? = nil
    var body: some View { ZStack { Color.black; Text("La película se ve en Traigger").foregroundStyle(.secondary) } }
}
#endif
