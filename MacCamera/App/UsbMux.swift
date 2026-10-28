import Foundation

// Cliente de usbmuxd (/var/run/usbmuxd), el servicio de macOS con el que Finder y Xcode hablan con el iPhone por el
// cable. Avisa cuando se conecta o desconecta un iPhone/iPad por USB y abre túneles TCP hacia un puerto del aparato.
// Protocolo: cabecera de 16 bytes (longitud total, versión 1, tipo 8 = plist, etiqueta) + plist XML, en little endian.
final class UsbMux {
    struct Device: Hashable { let id: Int; let serial: String }
    private static let path = "/var/run/usbmuxd"

    private static func connectSocket() -> Int32? {
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else { return nil }
        var address = sockaddr_un()
        address.sun_family = sa_family_t(AF_UNIX)
        _ = withUnsafeMutableBytes(of: &address.sun_path) { buffer in path.utf8CString.withUnsafeBytes { buffer.copyMemory(from: UnsafeRawBufferPointer(rebasing: $0.prefix(buffer.count))) } }
        let size = socklen_t(MemoryLayout<sockaddr_un>.size)
        let result = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, size) } }
        guard result == 0 else { close(fd); return nil }
        return fd
    }

    private static func send(_ fd: Int32, _ message: [String: Any], tag: UInt32) -> Bool {
        var body: [String: Any] = ["ClientVersionString": "Traigger Camera", "ProgName": "TraiggerCamera"]
        message.forEach { body[$0] = $1 }
        guard let plist = try? PropertyListSerialization.data(fromPropertyList: body, format: .xml, options: 0) else { return false }
        var header = [UInt32(16 + plist.count), 1, 8, tag].map { $0.littleEndian }
        var packet = Data(bytes: &header, count: 16); packet.append(plist)
        return packet.withUnsafeBytes { Darwin.send(fd, $0.baseAddress, packet.count, 0) } == packet.count
    }

    static func readExactly(_ fd: Int32, _ count: Int) -> Data? {
        var data = Data(count: count), read = 0
        while read < count {
            let n = data.withUnsafeMutableBytes { recv(fd, $0.baseAddress! + read, count - read, 0) }
            if n <= 0 { return nil }
            read += n
        }
        return data
    }

    private static func receive(_ fd: Int32) -> [String: Any]? {
        guard let header = readExactly(fd, 16) else { return nil }
        let length = Int(header.withUnsafeBytes { $0.load(as: UInt32.self).littleEndian })
        guard length >= 16, length < 1_000_000, let body = readExactly(fd, length - 16) else { return nil }
        return (try? PropertyListSerialization.propertyList(from: body, format: nil)) as? [String: Any]
    }

    // Escucha conexiones y desconexiones en un hilo propio (bloqueante). Vuelve a empezar si usbmuxd se reinicia.
    static func watch(attached: @escaping (Device) -> Void, detached: @escaping (Int) -> Void) {
        Thread.detachNewThread {
            while true {
                guard let fd = connectSocket() else { sleep(2); continue }
                if send(fd, ["MessageType": "Listen"], tag: 1) {
                    while let message = receive(fd) {
                        let id = message["DeviceID"] as? Int ?? -1
                        switch message["MessageType"] as? String {
                        case "Attached":
                            let properties = message["Properties"] as? [String: Any] ?? [:]
                            // Solo por cable: usbmuxd también anuncia aparatos emparejados por Wi-Fi.
                            if (properties["ConnectionType"] as? String) == "USB" { attached(Device(id: id, serial: properties["SerialNumber"] as? String ?? "")) }
                        case "Detached": detached(id)
                        default: break
                        }
                    }
                }
                close(fd); sleep(1)
            }
        }
    }

    // Túnel a un puerto TCP del aparato. Devuelve el socket ya conectado con el iPhone, o nil si allí no hay nadie escuchando.
    static func tunnel(to device: Int, port: UInt16) -> Int32? {
        guard let fd = connectSocket() else { return nil }
        // usbmuxd quiere el puerto en orden de red (big endian) dentro de un entero.
        let swapped = Int((port & 0xff) << 8 | (port >> 8))
        guard send(fd, ["MessageType": "Connect", "DeviceID": device, "PortNumber": swapped], tag: 2),
              let reply = receive(fd), (reply["Number"] as? Int) == 0 else { close(fd); return nil }
        var on: Int32 = 1
        setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &on, socklen_t(MemoryLayout<Int32>.size))
        return fd
    }
}
