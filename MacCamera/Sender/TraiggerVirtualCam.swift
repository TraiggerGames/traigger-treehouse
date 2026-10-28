import CoreMediaIO
import CoreVideo
import Foundation

// Plugin de Unity para Mac: busca la cámara virtual «Traigger» (MacCamera/Extension) y encola en su entrada cada
// imagen del personaje. Unity entrega píxeles BGRA de abajo arriba; aquí se copian al derecho en un CVPixelBuffer.
final class VirtualCamSender {
    static let shared = VirtualCamSender()
    private var device: CMIODeviceID = 0
    private var stream: CMIOStreamID = 0
    private var queue: CMSimpleQueue?
    private var pool: CVPixelBufferPool?
    private var poolSize = (0, 0)
    private var format: CMFormatDescription?

    private func property<T>(_ object: CMIOObjectID, _ selector: Int, _ scope: Int = kCMIOObjectPropertyScopeGlobal, _ type: T.Type) -> [T] {
        var address = CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(selector), mScope: CMIOObjectPropertyScope(scope),
                                                mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
        var size: UInt32 = 0
        guard CMIOObjectGetPropertyDataSize(object, &address, 0, nil, &size) == noErr, size > 0 else { return [] }
        let count = Int(size) / MemoryLayout<T>.stride
        let pointer = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<T>.alignment)
        defer { pointer.deallocate() }
        var used: UInt32 = 0
        guard CMIOObjectGetPropertyData(object, &address, 0, nil, size, &used, pointer) == noErr else { return [] }
        return (0..<count).map { pointer.load(fromByteOffset: $0 * MemoryLayout<T>.stride, as: T.self) }
    }

    private func name(_ object: CMIOObjectID) -> String {
        var address = CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(kCMIOObjectPropertyName), mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal),
                                                mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
        var value: Unmanaged<CFString>?
        var used: UInt32 = 0
        let status = withUnsafeMutablePointer(to: &value) { CMIOObjectGetPropertyData(object, &address, 0, nil, UInt32(MemoryLayout<Unmanaged<CFString>?>.size), &used, $0) }
        guard status == noErr, let value else { return "" }
        return value.takeRetainedValue() as String
    }

    // 1 = conectado; 0 = la cámara «Traigger» no está instalada; -1 = no se pudo abrir su entrada.
    func start() -> Int32 {
        if queue != nil { return 1 }
        // Las cámaras de extensiones aparecen al pedirlo explícitamente.
        var allow: UInt32 = 1
        var address = CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(kCMIOHardwarePropertyAllowScreenCaptureDevices),
                                                mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal), mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
        CMIOObjectSetPropertyData(CMIOObjectID(kCMIOObjectSystemObject), &address, 0, nil, UInt32(MemoryLayout<UInt32>.size), &allow)
        guard let found = property(CMIOObjectID(kCMIOObjectSystemObject), kCMIOHardwarePropertyDevices, kCMIOObjectPropertyScopeGlobal, CMIODeviceID.self)
            .first(where: { name($0) == "Traigger" }) else { return 0 }
        // La entrada es el flujo que no es de salida: dirección 0 vista desde la app.
        let streams = property(found, kCMIODevicePropertyStreams, kCMIOObjectPropertyScopeGlobal, CMIOStreamID.self)
        guard let sink = streams.first(where: { (property($0, kCMIOStreamPropertyDirection, kCMIOObjectPropertyScopeGlobal, UInt32.self).first ?? 1) == 0 }) ?? streams.last else { return -1 }
        var unmanaged: Unmanaged<CMSimpleQueue>?
        guard CMIOStreamCopyBufferQueue(sink, { _, _, _ in }, nil, &unmanaged) == noErr, let created = unmanaged?.takeRetainedValue() else { return -1 }
        guard CMIODeviceStartStream(found, sink) == noErr else { return -1 }
        device = found; stream = sink; queue = created
        return 1
    }

    func stop() {
        if queue != nil { CMIODeviceStopStream(device, stream) }
        queue = nil
    }

    // Píxeles BGRA de Unity (fila 0 = abajo). Devuelve 1 si se encoló, 0 si la cámara iba llena o no está conectada.
    func send(_ pixels: UnsafeRawPointer, _ width: Int, _ height: Int) -> Int32 {
        guard let queue else { return 0 }
        guard CMSimpleQueueGetCount(queue) < CMSimpleQueueGetCapacity(queue) else { return 0 }
        if pool == nil || poolSize != (width, height) {
            let attributes: [String: Any] = [kCVPixelBufferWidthKey as String: width, kCVPixelBufferHeightKey as String: height,
                                             kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                                             kCVPixelBufferIOSurfacePropertiesKey as String: [:]]
            pool = nil; CVPixelBufferPoolCreate(kCFAllocatorDefault, nil, attributes as CFDictionary, &pool); poolSize = (width, height); format = nil
        }
        var buffer: CVPixelBuffer?
        guard let pool, CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &buffer) == kCVReturnSuccess, let buffer else { return 0 }
        CVPixelBufferLockBaseAddress(buffer, [])
        let destination = CVPixelBufferGetBaseAddress(buffer)!, stride = CVPixelBufferGetBytesPerRow(buffer), row = width * 4
        for y in 0..<height { memcpy(destination + y * stride, pixels + (height - 1 - y) * row, row) }
        CVPixelBufferUnlockBaseAddress(buffer, [])
        if format == nil { CMVideoFormatDescriptionCreateForImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: buffer, formatDescriptionOut: &format) }
        var timing = CMSampleTimingInfo(duration: CMTime(value: 1, timescale: 30), presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()), decodeTimeStamp: .invalid)
        var sample: CMSampleBuffer?
        guard let format, CMSampleBufferCreateReadyWithImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: buffer, formatDescription: format,
                                                                   sampleTiming: &timing, sampleBufferOut: &sample) == noErr, let sample else { return 0 }
        CMSimpleQueueEnqueue(queue, element: Unmanaged.passRetained(sample).toOpaque())
        return 1
    }

    // Imagen ya preparada (Traigger Camera, vídeo que llega por el cable). 1 = encolada.
    func send(_ buffer: CVPixelBuffer) -> Int32 {
        guard start() == 1, let queue, CMSimpleQueueGetCount(queue) < CMSimpleQueueGetCapacity(queue) else { return 0 }
        var description: CMFormatDescription?
        CMVideoFormatDescriptionCreateForImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: buffer, formatDescriptionOut: &description)
        var timing = CMSampleTimingInfo(duration: CMTime(value: 1, timescale: 30), presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()), decodeTimeStamp: .invalid)
        var sample: CMSampleBuffer?
        guard let description, CMSampleBufferCreateReadyWithImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: buffer, formatDescription: description,
                                                                        sampleTiming: &timing, sampleBufferOut: &sample) == noErr, let sample else { return 0 }
        CMSimpleQueueEnqueue(queue, element: Unmanaged.passRetained(sample).toOpaque())
        return 1
    }
}

@_cdecl("TraiggerVCamStart") public func TraiggerVCamStart() -> Int32 { VirtualCamSender.shared.start() }
@_cdecl("TraiggerVCamStop") public func TraiggerVCamStop() { VirtualCamSender.shared.stop() }
@_cdecl("TraiggerVCamSend") public func TraiggerVCamSend(_ pixels: UnsafeRawPointer, _ width: Int32, _ height: Int32) -> Int32 {
    VirtualCamSender.shared.send(pixels, Int(width), Int(height))
}
