import Foundation
import SwiftUI
import UIKit
import UnityFramework

// Unity se ejecuta incrustado y dibuja a Traigger en su propia UIWindow. Al arrancar, Unity coge la escena que esté
// activa en ese momento (a veces el menú o el cine: Traigger «se perdía» al cerrarla). TraiggerUnityAnchor, dentro de
// la ventana «traigger», lleva la ventana de Unity a su escena cada vez que aparece y la deja transparente: así
// Traigger se puede poner encima de la película, la capturadora o el navegador.
@MainActor final class TraiggerUnity: ObservableObject {
    static let shared = TraiggerUnity()
    @Published private(set) var ready = false
    private var framework: UnityFramework?
    private weak var home: UIWindowScene?
    func start() {
        guard framework == nil, let instance = UnityFramework.getInstance() else { return }
        instance.runEmbedded(withArgc: CommandLine.argc, argv: CommandLine.unsafeArgv, appLaunchOpts: nil)
        framework = instance
        adopt()
    }
    func send(_ json: String) {
        TraiggerMotionRelay.shared.send(json)
        guard let framework else { return }
        "TraiggerVisionInput".withCString { object in
            "OnNative".withCString { method in
                json.withCString { payload in framework.sendMessageToGO(withName: object, functionName: method, message: payload) }
            }
        }
    }

    // Modo «libre»: la ventana de Unity se vuelve invisible (y deja pasar los toques) pero sigue dibujando.
    @Published private(set) var windowVisible = true
    func setWindowVisible(_ visible: Bool) {
        windowVisible = visible
        framework?.appController()?.window?.alpha = visible ? 1 : 0
    }

    // Escena de la ventana de Traigger (la avisa TraiggerUnityAnchor al aparecer).
    func setHome(_ scene: UIWindowScene?) { home = scene; adopt() }

    // Lleva la ventana de Unity a la escena de Traigger y la vuelve transparente; reintenta hasta que Unity la crea.
    private func adopt() {
        guard let home else { return }
        guard let controller = framework?.appController(), let window = controller.window, let view = controller.rootView else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in self?.adopt() }; return
        }
        if window.windowScene !== home { window.windowScene = home }
        window.isHidden = false
        window.alpha = windowVisible ? 1 : 0
        window.backgroundColor = .clear
        window.isOpaque = false
        window.rootViewController?.view.backgroundColor = .clear
        view.backgroundColor = .clear
        view.isOpaque = false
        view.layer.isOpaque = false
        view.layer.sublayers?.forEach { $0.isOpaque = false }
        ready = true
    }
}

// Vista vacía que solo sirve para saber en qué escena está la ventana de Traigger.
struct TraiggerUnityAnchor: UIViewRepresentable {
    final class Probe: UIView {
        override func didMoveToWindow() {
            super.didMoveToWindow()
            if let scene = window?.windowScene { Task { @MainActor in TraiggerUnity.shared.setHome(scene) } }
        }
    }
    func makeUIView(context: Context) -> Probe { let view = Probe(); view.backgroundColor = .clear; view.isUserInteractionEnabled = false; return view }
    func updateUIView(_ view: Probe, context: Context) {}
}
