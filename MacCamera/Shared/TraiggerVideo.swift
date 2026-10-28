import CoreMedia
import CoreVideo
import Foundation
import VideoToolbox

// Formato del vídeo que manda Traigger desde el iPhone/iPad (por Wi-Fi o por cable). Cada paquete:
//   [UInt32 big endian: tamaño del resto][UInt8 tipo][datos]
//   tipo 2 = H.264: [UInt8 1 si es fotograma clave] (+ si es clave: [UInt16 tamaño SPS][SPS][UInt16 tamaño PPS][PPS])
//                   [datos AVCC del fotograma]
// Lo comparten la extensión de cámara (Wi-Fi) y Traigger Camera (cable).
enum TraiggerVideo {
    static let width = 1920, height = 1080
    static let h264: UInt8 = 2
}

// Decodificador H.264 por hardware: entrega cada fotograma como CVPixelBuffer BGRA listo para la cámara.
final class TraiggerH264Decoder {
    private var format: CMVideoFormatDescription?
    private var session: VTDecompressionSession?
    private let output: (CVPixelBuffer) -> Void
    init(output: @escaping (CVPixelBuffer) -> Void) { self.output = output }
    deinit { if let session { VTDecompressionSessionInvalidate(session) } }

    func decode(_ packet: Data) {
        guard packet.count > 2, packet[packet.startIndex] == TraiggerVideo.h264 else { return }
        var index = packet.startIndex + 1
        let key = packet[index] == 1; index += 1
        if key {
            func block() -> Data? {
                guard index + 2 <= packet.endIndex else { return nil }
                let size = Int(packet[index]) << 8 | Int(packet[index + 1]); index += 2
                guard index + size <= packet.endIndex else { return nil }
                defer { index += size }
                return packet.subdata(in: index..<index + size)
            }
            guard let sps = block(), let pps = block() else { return }
            var newFormat: CMFormatDescription?
            sps.withUnsafeBytes { s in pps.withUnsafeBytes { p in
                let pointers = [s.bindMemory(to: UInt8.self).baseAddress!, p.bindMemory(to: UInt8.self).baseAddress!]
                let sizes = [sps.count, pps.count]
                _ = CMVideoFormatDescriptionCreateFromH264ParameterSets(allocator: kCFAllocatorDefault, parameterSetCount: 2, parameterSetPointers: pointers,
                                                                        parameterSetSizes: sizes, nalUnitHeaderLength: 4, formatDescriptionOut: &newFormat)
            } }
            if let newFormat, format == nil || !CMFormatDescriptionEqual(newFormat, otherFormatDescription: format) {
                format = newFormat
                if let session { VTDecompressionSessionInvalidate(session); self.session = nil }
                // Salida siempre a 1080p (lo que anuncia la cámara), aunque el aparato mande otro tamaño.
                let attributes: [String: Any] = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                                                 kCVPixelBufferWidthKey as String: TraiggerVideo.width, kCVPixelBufferHeightKey as String: TraiggerVideo.height,
                                                 kCVPixelBufferIOSurfacePropertiesKey as String: [:]]
                VTDecompressionSessionCreate(allocator: kCFAllocatorDefault, formatDescription: newFormat, decoderSpecification: nil,
                                             imageBufferAttributes: attributes as CFDictionary, outputCallback: nil, decompressionSessionOut: &session)
            }
        }
        guard let session, let format, index < packet.endIndex else { return }
        let frame = packet.subdata(in: index..<packet.endIndex)
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(allocator: kCFAllocatorDefault, memoryBlock: nil, blockLength: frame.count, blockAllocator: kCFAllocatorDefault,
                                                 customBlockSource: nil, offsetToData: 0, dataLength: frame.count, flags: 0, blockBufferOut: &block) == noErr, let block else { return }
        frame.withUnsafeBytes { _ = CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block, offsetIntoDestination: 0, dataLength: frame.count) }
        var sample: CMSampleBuffer?
        var size = frame.count
        guard CMSampleBufferCreateReady(allocator: kCFAllocatorDefault, dataBuffer: block, formatDescription: format, sampleCount: 1, sampleTimingEntryCount: 0,
                                        sampleTimingArray: nil, sampleSizeEntryCount: 1, sampleSizeArray: &size, sampleBufferOut: &sample) == noErr, let sample else { return }
        VTDecompressionSessionDecodeFrame(session, sampleBuffer: sample, flags: [], infoFlagsOut: nil) { [output] status, _, image, _, _ in
            if status == noErr, let image { output(image) }
        }
    }
}
