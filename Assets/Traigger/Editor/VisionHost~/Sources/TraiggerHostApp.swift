import SwiftUI
import RealityKit

// Host nativo mínimo de Traigger para Vision Pro (basado en UnityRealityKitBridge). Unity dibuja a Traigger en su
// ventana. Por defecto la app vive en el espacio compartido, junto a FaceTime y otras apps. Con «Activar sensores»
// se abre un espacio inmersivo mixto: es el único lugar donde visionOS da a las apps la cabeza y las manos, y
// mientras está abierto las demás apps se ocultan. Ni la mirada ni la cara están disponibles para apps.
@main struct TraiggerHostApp: App {
    @State private var style: ImmersionStyle = .mixed
    @ObservedObject private var spatial = TraiggerSpatial.shared
    // Con un HDRI elegido, el «Cine» es una sala propia (inmersión total); sin él, mezcla con la habitación (la corona
    // ajusta cuánto). Las ventanas de Traigger se siguen viendo dentro.
    private var theaterStyle: Binding<any ImmersionStyle> {
        Binding(get: { spatial.skybox != nil ? (FullImmersionStyle.full as any ImmersionStyle) : ProgressiveImmersionStyle.progressive }, set: { _ in })
    }
    var body: some SwiftUI.Scene {
        // Ventana de Traigger: sin cristal, para que el personaje se vea encima del vídeo o la web que tenga detrás.
        // Todas las ventanas son de una sola instancia (valor «main»): volver a pedirlas trae la que ya hay.
        WindowGroup(id: "traigger", for: String.self) { _ in TraiggerLaunchView().traiggerRouter() } defaultValue: { "main" }
            .windowStyle(.plain)
            .defaultSize(width: 900, height: 1100)
        // Interfaz de Traigger (SwiftUI), junto a su ventana. Se puede cerrar del todo.
        WindowGroup("Traigger", id: "traigger-panel", for: String.self) { _ in TraiggerVisionPanel().traiggerRouter() }
            .defaultSize(width: 520, height: 860)
        WindowGroup("Conexión con el Mac", id: "traigger-relay", for: String.self) { _ in TraiggerRelaySettings().traiggerRouter() }
            .defaultSize(width: 480, height: 260)
        ImmersiveSpace(id: "traigger-sensors") { RealityView { _ in } }
            .immersionStyle(selection: $style, in: .mixed)
        // Navegador y película en sus ventanas, y el espacio inmersivo «Cine» (con fondo HDRI opcional).
        WindowGroup("Navegador", id: "traigger-browser", for: String.self) { _ in TraiggerBrowserWindow().traiggerRouter() }
            .defaultSize(width: 1280, height: 860)
        WindowGroup("Película", id: "traigger-cinema", for: String.self) { _ in TraiggerCinemaWindow().traiggerRouter() }
            .windowResizability(.contentMinSize)
            .defaultSize(width: 960, height: 540)
        // Entrada HDMI recibida del iPad o del Mac (capturadora USB).
        WindowGroup("Entrada HDMI", id: "traigger-hdmi", for: String.self) { _ in TraiggerHDMIWindow().traiggerRouter() }
            .windowResizability(.contentMinSize)
            .defaultSize(width: 960, height: 540)
        // Traigger «libre»: volumen sin fondo que se coloca en cualquier sitio del espacio.
        WindowGroup("Traigger", id: "traigger-free", for: String.self) { _ in TraiggerFreeVolume().traiggerRouter() }
            .windowStyle(.volumetric)
            .defaultSize(width: 0.6, height: 0.9, depth: 0.05, in: .meters)
        // Ventana para compartir: actividad + Traigger (logo de canal o avatar de llamada).
        WindowGroup("Compartir", id: "traigger-share", for: String.self) { _ in TraiggerShareWindow().traiggerRouter() }
            .defaultSize(width: 1280, height: 720)
        WindowGroup("Opciones para compartir", id: "traigger-share-options", for: String.self) { _ in TraiggerShareOptionsWindow().traiggerRouter() }
            .defaultSize(width: 460, height: 520)
            .defaultWindowPlacement { _, context in
                if let share = context.windows.first(where: { $0.id == "traigger-share" }) { return WindowPlacement(.trailing(share)) }
                return WindowPlacement(nil)
            }
        ImmersiveSpace(id: "traigger-theater") { TraiggerTheater() }
            .immersionStyle(selection: theaterStyle, in: .progressive, .full)
    }
}

// Arranque y ventana de Traigger. Unity arranca una sola vez aunque esta ventana se cierre y se vuelva a abrir.
struct TraiggerLaunchView: View {
    @ObservedObject private var store = TraiggerPanelHost.store
    @ObservedObject private var unity = TraiggerUnity.shared
    @Environment(\.openWindow) private var openWindow
    private static var started = false
    private var playerVisible: Bool { store.state.moviePlaying == true && !store.playerControlsHidden }
    var body: some View {
        TraiggerUnityAnchor()
            .overlay {
                if !unity.ready { Text("Cargando a Traigger…").padding().glassBackgroundEffect() }
                else if !unity.windowVisible {
                    VStack(spacing: 12) {
                        Text("Traigger está libre en el espacio")
                        Button("Volver a esta ventana") { TraiggerRouter.request("free:off") }
                    }.padding(24).glassBackgroundEffect()
                }
            }
            // Controles de reproducción en la propia ventana de Traigger (no en una ventana aparte).
            .ornament(visibility: playerVisible ? .visible : .hidden, attachmentAnchor: .scene(.bottom), contentAlignment: .top) {
                TraiggerVisionPlayer().frame(width: 760)
            }
            .task {
                guard !Self.started else { return }
                Self.started = true
                TraiggerUnity.shared.start()
                TraiggerVoice.shared.start()
                TraiggerBodyReceiver.shared.start()
                TraiggerHDMIReceiver.shared.start()
                TraiggerSensors.shared.reportMode(false)
                TraiggerPanelHost.installRevealGesture()
                if !TraiggerPanelHost.store.hidden { openWindow(id: "traigger-panel", value: "main") }
            }
    }
}

// Controlador central de ventanas y espacios. Antes las peticiones las atendía la ventana de Traigger, y si se
// cerraba ya no se podía abrir nada (había que forzar la salida). Ahora cada ventana, al aparecer, le presta sus
// acciones de abrir/cerrar, y las peticiones se atienden mientras quede cualquier ventana abierta.
@MainActor final class TraiggerRouter {
    static let shared = TraiggerRouter()
    static func request(_ text: String) {
        NotificationCenter.default.post(name: Notification.Name("Traigger.Request"), object: nil, userInfo: ["request": text])
    }
    private var openWindowAction: OpenWindowAction?
    private var dismissWindowAction: DismissWindowAction?
    private var openSpaceAction: OpenImmersiveSpaceAction?
    private var dismissSpaceAction: DismissImmersiveSpaceAction?
    private var theaterOpen = false, sensorsWanted = false
    private var observers: [NSObjectProtocol] = []

    func adopt(_ open: OpenWindowAction, _ dismiss: DismissWindowAction, _ openSpace: OpenImmersiveSpaceAction, _ dismissSpace: DismissImmersiveSpaceAction) {
        openWindowAction = open; dismissWindowAction = dismiss; openSpaceAction = openSpace; dismissSpaceAction = dismissSpace
        guard observers.isEmpty else { return }
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: Notification.Name("Traigger.Request"), object: nil, queue: .main) { note in
            guard let request = note.userInfo?["request"] as? String else { return }
            Task { @MainActor in await TraiggerRouter.shared.handle(request) }
        })
        observers.append(center.addObserver(forName: TraiggerHDMIReceiver.firstFrame, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { TraiggerRouter.shared.open("traigger-hdmi") }
        })
        observers.append(center.addObserver(forName: TraiggerPanelHost.showRequest, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { TraiggerRouter.shared.open("traigger-panel") }
        })
    }
    func open(_ id: String) { openWindowAction?(id: id, value: "main") }
    func close(_ id: String) { dismissWindowAction?(id: id, value: "main") }

    func handle(_ request: String) async {
        let spatial = TraiggerSpatial.shared
        switch request {
        case "traigger:show": open("traigger")
        case "relay:settings": open("traigger-relay")
        case let r where r.hasPrefix("speech:lang:"): TraiggerSpeech.shared.setLanguage(String(r.dropFirst(12)))
        case "sensors:on":
            // Los sensores funcionan en cualquier espacio inmersivo: si el «Cine» ya está abierto, se usan ahí.
            sensorsWanted = true
            if theaterOpen { await TraiggerSensors.shared.start() }
            else if case .opened? = await openSpaceAction?(id: "traigger-sensors") { await TraiggerSensors.shared.start() }
            else { TraiggerSensors.shared.reportMode(false) }
        case "sensors:off":
            sensorsWanted = false
            TraiggerSensors.shared.stop()
            if !theaterOpen { await dismissSpaceAction?() }
        case let r where r.hasPrefix("web:open:"): TraiggerBrowser.shared.open(String(r.dropFirst(9))); if !spatial.browserInSpace { open("traigger-browser") }
        case "web:show": open("traigger-browser")
        case "web:close": close("traigger-browser"); spatial.browserInSpace = false
        case "web:immersive:on": spatial.browserInSpace = true; close("traigger-browser")
        case "web:immersive:off": spatial.browserInSpace = false; open("traigger-browser")
        case "cinema:window:on": open("traigger-cinema")
        case "cinema:window:off": close("traigger-cinema")
        case "hdmi:window:on": open("traigger-hdmi")
        case "hdmi:window:off": close("traigger-hdmi")
        case "cinema:immersive:on": spatial.cinemaInSpace = true
        case "cinema:immersive:off": spatial.cinemaInSpace = false
        case "free:on": open("traigger-free")
        case "free:off": close("traigger-free"); TraiggerUnity.shared.setWindowVisible(true)
        case let r where r.hasPrefix("share:open"):
            // «share:open:fuente:modo» (VisionShare.Open); las partes que falten conservan lo último elegido.
            let parts = r.split(separator: ":").map(String.init)
            if parts.count > 2 { UserDefaults.standard.set(parts[2], forKey: "traigger.share.source") }
            if parts.count > 3 { UserDefaults.standard.set(parts[3], forKey: "traigger.share.mode") }
            open("traigger-share")
        case "share:close": close("traigger-share"); close("traigger-share-options")
        case "share:options:on": open("traigger-share-options")
        case "share:options:off": close("traigger-share-options")
        case "sky:off": spatial.skybox = nil
        case let r where r.hasPrefix("cinema:scale:"): spatial.cinemaScale = Double(r.dropFirst(13)) ?? 1
        case let r where r.hasPrefix("sky:"): spatial.skybox = TraiggerSpatial.locate(String(r.dropFirst(4)))
        default: return
        }
        // El espacio «Cine» se abre si algo lo necesita y se cierra cuando ya nada lo usa (solo cabe uno inmersivo a la
        // vez); los sensores pasan de un espacio a otro sin apagarse.
        if spatial.wantsSpace && !theaterOpen {
            TraiggerSensors.shared.stop(); await dismissSpaceAction?()
            if case .opened? = await openSpaceAction?(id: "traigger-theater") { theaterOpen = true }
            if sensorsWanted { await TraiggerSensors.shared.start() }
        } else if !spatial.wantsSpace && theaterOpen {
            TraiggerSensors.shared.stop(); await dismissSpaceAction?(); theaterOpen = false
            if sensorsWanted, case .opened? = await openSpaceAction?(id: "traigger-sensors") { await TraiggerSensors.shared.start() }
        }
    }
}

private struct TraiggerRouterModifier: ViewModifier {
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismissWindow) private var dismissWindow
    @Environment(\.openImmersiveSpace) private var openSpace
    @Environment(\.dismissImmersiveSpace) private var dismissSpace
    func body(content: Content) -> some View {
        content.onAppear { TraiggerRouter.shared.adopt(openWindow, dismissWindow, openSpace, dismissSpace) }
    }
}
extension View {
    func traiggerRouter() -> some View { modifier(TraiggerRouterModifier()) }
}

struct TraiggerRelaySettings: View {
    @AppStorage("TraiggerRelayHost") private var host = ""
    var body: some View {
        Form {
            Text("Enviar movimiento de cabeza, manos y voz al Mac")
            TextField("IP local del Mac (vacío = desactivado)", text: $host)
                .textInputAutocapitalization(.never).autocorrectionDisabled()
            Text("Misma red Wi-Fi. En el Mac: Traigger > Llamadas > Preparar manos y salida. Puerto UDP 49984. Esta ventana configura sensores; no sustituye la Persona de FaceTime.")
        }.padding()
    }
}
