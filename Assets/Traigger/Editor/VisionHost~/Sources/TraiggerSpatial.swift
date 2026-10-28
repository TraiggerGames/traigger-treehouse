import RealityKit
import SwiftUI

// Vision Pro: navegador y cine fuera de la ventana de Traigger, y el espacio inmersivo «Cine». Lo piden la API de
// Unity (VisionWeb, VisionCinema, fondos) o la interfaz, siempre como peticiones «web:…», «cinema:…» y «sky:…».
@MainActor final class TraiggerSpatial: ObservableObject {
    static let shared = TraiggerSpatial()
    @Published var cinemaScale: Double = 1
    @Published var cinemaInSpace = false
    @Published var browserInSpace = false
    @Published var skybox: URL?
    @Published var aspect: CGFloat = 16 / 9
    var wantsSpace: Bool { cinemaInSpace || browserInSpace || skybox != nil }
    // La carpeta de la app cambia al reinstalar: si la ruta guardada no existe, se busca por nombre en «Fondos».
    static func locate(_ path: String) -> URL? {
        if FileManager.default.fileExists(atPath: path) { return URL(fileURLWithPath: path) }
        guard let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.appendingPathComponent("Fondos") else { return nil }
        let candidate = folder.appendingPathComponent((path as NSString).lastPathComponent)
        return FileManager.default.fileExists(atPath: candidate.path) ? candidate : nil
    }
}

// Película en su propia ventana: se mueve y se redimensiona por separado; la API puede agrandarla (tamaño mínimo).
struct TraiggerCinemaWindow: View {
    @ObservedObject var spatial = TraiggerSpatial.shared
    var body: some View {
        TraiggerMovieScreen { aspect in Task { @MainActor in TraiggerSpatial.shared.aspect = aspect } }
            .aspectRatio(spatial.aspect, contentMode: .fit)
            .frame(minWidth: 480 * spatial.cinemaScale, minHeight: 480 * spatial.cinemaScale / spatial.aspect)
            .background(.black)
    }
}

// Navegador en su ventana, con el paso a pantalla grande inmersiva.
struct TraiggerBrowserWindow: View {
    var body: some View {
        TraiggerBrowserView {
            Button { NotificationCenter.default.post(name: Notification.Name("Traigger.Request"), object: nil, userInfo: ["request": "web:immersive:on"]) } label: {
                Image(systemName: "visionpro")
            }
            .help("Pantalla grande inmersiva")
        }
        .onAppear { TraiggerBrowser.shared.pageChanged = { url, title in TraiggerPanelHost.store.send("web.page", s: url + "\n" + title) } }
    }
}

// Espacio inmersivo «Cine»: pantalla de cine y navegador flotando delante, con fondo HDRI de 360° si se ha elegido.
struct TraiggerTheater: View {
    @ObservedObject var spatial = TraiggerSpatial.shared
    var body: some View {
        RealityView { content, attachments in
            let root = Entity(); root.name = "theater"; content.add(root)
            let sky = Entity(); sky.name = "sky"; root.addChild(sky)
            if let cinema = attachments.entity(for: "cinema") { cinema.name = "cinema"; cinema.position = [0, 1.5, -3]; root.addChild(cinema) }
            if let browser = attachments.entity(for: "browser") { browser.name = "browser"; browser.position = [-1.6, 1.4, -2.4]; browser.orientation = simd_quatf(angle: 0.35, axis: [0, 1, 0]); root.addChild(browser) }
        } update: { content, attachments in
            guard let root = content.entities.first(where: { $0.name == "theater" }) else { return }
            if let cinema = root.findEntity(named: "cinema") { cinema.isEnabled = spatial.cinemaInSpace; cinema.scale = .init(repeating: Float(spatial.cinemaScale)) }
            if let browser = root.findEntity(named: "browser") { browser.isEnabled = spatial.browserInSpace; browser.position.x = spatial.cinemaInSpace ? -1.9 : 0 }
            if let sky = root.findEntity(named: "sky") { TraiggerTheater.updateSky(sky, spatial.skybox) }
        } attachments: {
            Attachment(id: "cinema") {
                TraiggerMovieScreen { aspect in Task { @MainActor in TraiggerSpatial.shared.aspect = aspect } }
                    .frame(width: 3000, height: 3000 / spatial.aspect)
            }
            Attachment(id: "browser") {
                TraiggerBrowserView { Button { NotificationCenter.default.post(name: Notification.Name("Traigger.Request"), object: nil, userInfo: ["request": "web:immersive:off"]) } label: { Image(systemName: "xmark.circle.fill") } }
                    .frame(width: 1600, height: 1000).glassBackgroundEffect()
            }
        }
    }

    // Esfera vista desde dentro con la imagen panorámica (equirectangular). ImageIO lee también .hdr y .exr.
    private static var loadedSky: URL?
    static func updateSky(_ holder: Entity, _ url: URL?) {
        guard url != loadedSky else { return }
        loadedSky = url
        holder.children.removeAll()
        guard let url, let texture = try? TextureResource.load(contentsOf: url) else { return }
        var material = UnlitMaterial()
        material.color = .init(texture: .init(texture))
        let sphere = ModelEntity(mesh: .generateSphere(radius: 60), materials: [material])
        sphere.scale = [-1, 1, 1] // por dentro
        holder.addChild(sphere)
    }
}
