import SwiftUI
import UIKit
import UnityFramework

// Interfaz de Traigger en Vision Pro: una ventana SwiftUI de cristal junto a la de Traigger. El estado y las órdenes
// van por avisos internos con Unity (Plugins/visionOS/TraiggerUIBridge.mm). Ocultarla cierra la ventana del todo;
// mantener el pellizco sobre Traigger la vuelve a abrir.
final class TraiggerVisionTransport: TraiggerTransport {
    var onMessage: ((String) -> Void)?
    var onConnection: ((Bool) -> Void)?
    private var observer: NSObjectProtocol?
    init() {
        observer = NotificationCenter.default.addObserver(forName: Notification.Name("Traigger.State"), object: nil, queue: .main) { [weak self] note in
            if let json = note.userInfo?["json"] as? String { self?.onMessage?(json) }
        }
    }
    func send(_ json: String) {
        NotificationCenter.default.post(name: Notification.Name("Traigger.Command"), object: nil, userInfo: ["json": json])
    }
}

@MainActor enum TraiggerPanelHost {
    static let store = TraiggerStore(transport: TraiggerVisionTransport())
    static let showRequest = Notification.Name("Traigger.ShowPanel")
    static let showPlayerRequest = Notification.Name("Traigger.ShowPlayer")
    private static var installed = false

    // Pellizco mantenido sobre la ventana de Traigger (la de Unity): muestra la interfaz.
    static func installRevealGesture() {
        guard !installed else { return }
        guard let view = UnityFramework.getInstance()?.appController()?.rootView else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { installRevealGesture() }; return
        }
        installed = true
        let press = UILongPressGestureRecognizer(target: Reveal.shared, action: #selector(Reveal.pressed(_:)))
        press.minimumPressDuration = 0.8; press.cancelsTouchesInView = false
        view.addGestureRecognizer(press)
        // Un toque sobre Traigger con una película puesta: vuelven los controles de reproducción.
        let tap = UITapGestureRecognizer(target: Reveal.shared, action: #selector(Reveal.tapped))
        tap.cancelsTouchesInView = false
        view.addGestureRecognizer(tap)
    }
    final class Reveal: NSObject {
        static let shared = Reveal()
        @objc func tapped() {
            Task { @MainActor in
                guard TraiggerPanelHost.store.state.moviePlaying == true else { return }
                TraiggerPanelHost.store.showPlayerControls()
                NotificationCenter.default.post(name: TraiggerPanelHost.showPlayerRequest, object: nil)
            }
        }
        @objc func pressed(_ gesture: UILongPressGestureRecognizer) {
            guard gesture.state == .began else { return }
            Task { @MainActor in TraiggerPanelHost.store.hidden = false; NotificationCenter.default.post(name: TraiggerPanelHost.showRequest, object: nil) }
        }
    }
}

struct TraiggerVisionPanel: View {
    @ObservedObject var store = TraiggerPanelHost.store
    @Environment(\.dismissWindow) private var dismissWindow
    @Environment(\.openWindow) private var openWindow
    var body: some View {
        TraiggerPanel(store: store)
            // Si se había cerrado la ventana de Traigger, abrir el menú la recupera (es de una sola instancia).
            .onAppear { openWindow(id: "traigger", value: "main") }
            .onChange(of: store.hidden) { _, hidden in if hidden { dismissWindow(id: "traigger-panel", value: "main") } }
    }
}

// Controles de reproducción en Vision Pro: barra de cristal bajo la ventana de Traigger (adorno), como la del
// reproductor de visionOS. Se desvanecen sin uso y vuelven al tocar.
struct TraiggerVisionPlayer: View {
    @ObservedObject var store = TraiggerPanelHost.store
    var body: some View {
        TraiggerPlayerControls(store: store)
            .padding(8)
            .glassBackgroundEffect()
            .opacity(store.playerControlsShown ? 1 : 0.0)
            .animation(.easeOut(duration: 0.3), value: store.playerControlsShown)
            .contentShape(Rectangle())
            .onTapGesture { store.showPlayerControls() }
    }
}
