import SwiftUI

// Traigger Control: la interfaz nativa de Traigger como app de Mac, conectada por red local a Traigger en el editor de
// Unity (modo Play) o en Traigger para Mac. Sirve para usar y depurar la misma interfaz que ven el iPad y Vision Pro.
@main struct TraiggerControlApp: App {
    @StateObject private var store = TraiggerStore(transport: TraiggerSocketTransport())
    var body: some Scene {
        WindowGroup("Traigger Control") {
            Group {
                if store.hidden {
                    Button { store.hidden = false } label: { Label("Mostrar la interfaz", systemImage: "eye") }.padding(40)
                } else {
                    TraiggerPanel(store: store)
                }
            }
            // Controles de reproducción abajo, como en QuickTime: aparecen al mover el ratón y se ocultan solos.
            .overlay(alignment: .bottom) {
                if store.playerControlsVisible { TraiggerPlayerControls(store: store).padding(14).transition(.move(edge: .bottom).combined(with: .opacity)) }
            }
            .animation(.easeOut(duration: 0.25), value: store.playerControlsVisible)
            .onContinuousHover { phase in if case .active = phase, store.state.moviePlaying == true, !store.playerControlsShown { store.showPlayerControls() } }
            .frame(minWidth: 420, minHeight: 640)
        }
        .defaultSize(width: 460, height: 780)
    }
}
