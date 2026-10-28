import Foundation
import Security

// Servidores SMB guardados para el cine de Traigger. Los datos van en las preferencias; la contraseña, en el llavero
// del dispositivo (nunca en texto plano ni en Unity). Unity solo recibe la dirección ya montada al abrir la carpeta.
struct TraiggerServer: Codable, Identifiable, Hashable {
    var id = UUID()
    var name = ""
    var host = ""          // servidor o IP, p. ej. «nas.local» o «192.168.1.20»
    var share = ""         // carpeta compartida y ruta, p. ej. «Peliculas» o «Media/Cine»
    var user = ""

    // smb://usuario:contraseña@servidor/carpeta/ con los datos codificados para una URL.
    func url(path: String? = nil) -> String {
        let allowed = CharacterSet.urlUserAllowed.subtracting(CharacterSet(charactersIn: ":@/"))
        var credentials = ""
        if !user.isEmpty {
            credentials = (user.addingPercentEncoding(withAllowedCharacters: allowed) ?? user)
            if let password = TraiggerKeychain.password(for: self), !password.isEmpty {
                credentials += ":" + (password.addingPercentEncoding(withAllowedCharacters: allowed) ?? password)
            }
            credentials += "@"
        }
        let folder = (path ?? share).split(separator: "/").map { $0.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? String($0) }.joined(separator: "/")
        return "smb://\(credentials)\(host)/\(folder)\(folder.isEmpty ? "" : "/")"
    }
}

enum TraiggerKeychain {
    private static func query(_ server: TraiggerServer) -> [String: Any] {
        [kSecClass as String: kSecClassInternetPassword, kSecAttrServer as String: server.host,
         kSecAttrAccount as String: server.user, kSecAttrProtocol as String: kSecAttrProtocolSMB,
         kSecAttrLabel as String: "Traigger · \(server.name)"]
    }
    static func password(for server: TraiggerServer) -> String? {
        var item: CFTypeRef?
        var search = query(server); search[kSecReturnData as String] = true; search[kSecMatchLimit as String] = kSecMatchLimitOne
        search.removeValue(forKey: kSecAttrLabel as String)
        guard SecItemCopyMatching(search as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }
    static func save(_ password: String, for server: TraiggerServer) {
        var search = query(server); search.removeValue(forKey: kSecAttrLabel as String)
        SecItemDelete(search as CFDictionary)
        guard !password.isEmpty else { return }
        var item = query(server); item[kSecValueData as String] = Data(password.utf8)
        SecItemAdd(item as CFDictionary, nil)
    }
    static func delete(for server: TraiggerServer) {
        var search = query(server); search.removeValue(forKey: kSecAttrLabel as String)
        SecItemDelete(search as CFDictionary)
    }
}

// Lista de servidores y recientes (con el punto donde se dejó cada película).
@MainActor final class TraiggerLibrary: ObservableObject {
    struct Recent: Codable, Hashable { var title: String; var url: String; var position: Double; var date: Date }
    @Published var servers: [TraiggerServer] = [] { didSet { save() } }
    @Published var recents: [Recent] = [] { didSet { save() } }
    init() {
        let defaults = UserDefaults.standard
        if let data = defaults.data(forKey: "traigger.servers"), let list = try? JSONDecoder().decode([TraiggerServer].self, from: data) { servers = list }
        if let data = defaults.data(forKey: "traigger.recents"), let list = try? JSONDecoder().decode([Recent].self, from: data) { recents = list }
    }
    private func save() {
        let defaults = UserDefaults.standard
        defaults.set(try? JSONEncoder().encode(servers), forKey: "traigger.servers")
        defaults.set(try? JSONEncoder().encode(recents), forKey: "traigger.recents")
    }
    func remember(title: String, url: String, position: Double) {
        guard !title.isEmpty, !url.isEmpty else { return }
        recents.removeAll { $0.url == url }
        recents.insert(Recent(title: title, url: url, position: position, date: Date()), at: 0)
        if recents.count > 20 { recents.removeLast(recents.count - 20) }
    }
    // Nombre de servidor sin la contraseña, para mostrar una dirección en pantalla.
    static func display(_ url: String) -> String {
        guard var components = URLComponents(string: url) else { return url }
        components.password = nil; components.user = nil
        return (components.string ?? url).removingPercentEncoding ?? url
    }
}

enum TraiggerFiles {
    static let videoExtensions: Set<String> = ["mkv", "mp4", "m4v", "mov", "avi", "webm", "ts", "m2ts", "wmv", "mpg", "mpeg", "flv", "ogv", "3gp"]
    static let subtitleExtensions: Set<String> = ["srt", "ass", "ssa", "vtt", "sub", "idx"]
    static func kind(_ name: String) -> String { (name as NSString).pathExtension.lowercased() }
    static func isVideo(_ name: String) -> Bool { videoExtensions.contains(kind(name)) }
    static func isSubtitle(_ name: String) -> Bool { subtitleExtensions.contains(kind(name)) }
}
