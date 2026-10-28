import AVFoundation
import Combine
import SwiftUI
import UIKit

// Interfaz SwiftUI de Traigger dentro de la app de iPhone/iPad, encima de la vista de Unity: un panel lateral (o
// inferior en iPhone) que solo recoge los toques que caen sobre él; el resto llega a Unity (arrastrar a Traigger).
// Ocultar la interfaz la quita del todo; vuelve con un doble toque de dos dedos en cualquier parte.
// Unity registra su función de órdenes (TraiggerUIRegister) y le empuja el estado (TraiggerUIPushState).
final class TraiggerInProcessTransport: TraiggerTransport {
    static let shared = TraiggerInProcessTransport()
    var onMessage: ((String) -> Void)?
    var onConnection: ((Bool) -> Void)?
    var command: (@convention(c) (UnsafePointer<CChar>) -> Void)?
    func send(_ json: String) { DispatchQueue.main.async { json.withCString { self.command?($0) } } }
}

@MainActor final class TraiggerOverlay: NSObject, ObservableObject, UIGestureRecognizerDelegate {
    static let shared = TraiggerOverlay()
    let store = TraiggerStore(transport: TraiggerInProcessTransport.shared)
    private var host: UIHostingController<OverlayRoot>?
    // El menú y los controles van en su propia ventana, por encima de Unity y del navegador: así siempre se ven y se
    // pueden mover y redimensionar. Los toques fuera de ellos atraviesan la ventana.
    private var uiWindow: UIWindow?
    private var player: UIHostingController<TraiggerPlayerControls>?
    private var watch: AnyCancellable?
    private var playerWatch: AnyCancellable?
    private var playerTrailing: NSLayoutConstraint?

    func install() {
        guard host == nil, let window = UIApplication.shared.connectedScenes.compactMap({ ($0 as? UIWindowScene)?.windows.first { $0.isKeyWindow } }).first,
              let scene = window.windowScene else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { self.install() }; return
        }
        let ui = TraiggerPassThroughWindow(windowScene: scene)
        ui.windowLevel = UIWindow.Level(rawValue: window.windowLevel.rawValue + 2)
        let root = UIViewController()
        root.view = TraiggerPassThroughView()
        root.view.backgroundColor = .clear
        ui.rootViewController = root
        ui.isHidden = false
        uiWindow = ui
        let controller = UIHostingController(rootView: OverlayRoot(store: store))
        controller.view.backgroundColor = .clear
        root.addChild(controller)
        root.view.addSubview(controller.view)
        controller.didMove(toParent: root)
        let view = controller.view!
        view.translatesAutoresizingMaskIntoConstraints = false
        let guide = root.view.safeAreaLayoutGuide
        if root.traitCollection.horizontalSizeClass == .compact {
            // iPhone: hoja inferior que ocupa la mitad de la pantalla.
            NSLayoutConstraint.activate([view.leadingAnchor.constraint(equalTo: guide.leadingAnchor), view.trailingAnchor.constraint(equalTo: guide.trailingAnchor),
                                         view.bottomAnchor.constraint(equalTo: root.view.bottomAnchor), view.heightAnchor.constraint(equalTo: root.view.heightAnchor, multiplier: 0.55)])
        } else {
            // iPad: ventana flotante (o acoplada a la derecha); se coloca más abajo, cuando ya existe «host».
            panelRoot = root.view
        }
        view.layer.cornerRadius = 24; view.clipsToBounds = true
        host = controller
        if panelRoot != nil {
            applyPanelLayout()
            // Mover y redimensionar con UIKit: en esta ventana el arrastre de SwiftUI no llega a dispararse.
            let pan = UIPanGestureRecognizer(target: self, action: #selector(panelPan(_:)))
            pan.delegate = self
            view.addGestureRecognizer(pan)
        }
        watch = store.$hidden.sink { hidden in view.isHidden = hidden }
        // Doble toque con dos dedos: vuelve a mostrar la interfaz (sin quitar los toques a Unity).
        let reveal = UITapGestureRecognizer(target: self, action: #selector(toggle))
        reveal.numberOfTouchesRequired = 2; reveal.numberOfTapsRequired = 2
        reveal.cancelsTouchesInView = false; reveal.delegate = self
        window.addGestureRecognizer(reveal)
        installPlayerControls(root: root, window: window, panel: view)
        // Peticiones de la interfaz compartida (p. ej. «Modo compartir»), igual que en Vision Pro.
        NotificationCenter.default.addObserver(forName: Notification.Name("Traigger.Request"), object: nil, queue: .main) { note in
            guard let request = note.userInfo?["request"] as? String else { return }
            MainActor.assumeIsolated { TraiggerOverlay.shared.handle(request) }
        }
        // Capturadora conectada al Mac: se recibe aquí y se abre su ventana al llegar la imagen.
        TraiggerMacReceiver.shared.start()
        NotificationCenter.default.addObserver(forName: TraiggerMacReceiver.firstFrame, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { TraiggerOverlay.shared.showVideoWindow("hdmi", true) }
        }
    }

    // Controles de reproducción abajo, sobre la película; un toque en la pantalla los muestra.
    private func installPlayerControls(root: UIViewController, window: UIWindow, panel: UIView) {
        let controller = UIHostingController(rootView: TraiggerPlayerControls(store: store))
        controller.view.backgroundColor = .clear
        root.addChild(controller); root.view.addSubview(controller.view); controller.didMove(toParent: root)
        let view = controller.view!
        view.translatesAutoresizingMaskIntoConstraints = false
        let guide = root.view.safeAreaLayoutGuide
        let trailing = view.trailingAnchor.constraint(equalTo: guide.trailingAnchor, constant: -16)
        NSLayoutConstraint.activate([view.leadingAnchor.constraint(greaterThanOrEqualTo: guide.leadingAnchor, constant: 16), trailing,
                                     view.widthAnchor.constraint(lessThanOrEqualToConstant: 760),
                                     view.bottomAnchor.constraint(equalTo: guide.bottomAnchor, constant: -16)])
        playerTrailing = trailing; player = controller
        view.isHidden = true
        // Visible solo con película, mostrada y no ocultada; deja libre el panel lateral del iPad si está abierto.
        playerWatch = store.objectWillChange.receive(on: RunLoop.main).sink { [weak self] _ in
            DispatchQueue.main.async {
                guard let self else { return }
                let visible = self.store.playerControlsVisible
                if visible != !view.isHidden { UIView.transition(with: view, duration: 0.25, options: .transitionCrossDissolve) { view.isHidden = !visible } }
                let panelOpen = !self.store.hidden && root.traitCollection.horizontalSizeClass != .compact && !self.floating
                self.playerTrailing?.constant = panelOpen ? -(16 + panel.bounds.width + 12) : -16
            }
        }
        let tap = UITapGestureRecognizer(target: self, action: #selector(showControls))
        tap.cancelsTouchesInView = false; tap.delegate = self
        window.addGestureRecognizer(tap)
    }

    // Navegador con Traigger: «junto a» (la web a la izquierda, Traigger a la derecha) o «encima» (la web a pantalla
    // completa detrás de Unity, que se dibuja transparente, con Traigger flotando; los toques fuera de él van a la web).
    private var browser: UIHostingController<AnyView>?
    private var overlay = UserDefaults.standard.object(forKey: "traigger.web.overlay") as? Bool ?? true // por defecto, Traigger encima de la web
    // MARK: Modo compartir (iPhone/iPad). FaceTime comparte la pantalla entera, así que la «ventana para compartir» de
    // Vision Pro aquí es la pantalla: el contenido (cine, HDMI o web) a pantalla completa —o al 70 % con Traigger al
    // lado, como en una llamada— con Traigger encima, que se mueve arrastrándolo y se escala pellizcándolo. El menú y
    // los controles se ocultan (el doble toque con dos dedos devuelve el menú) para que no salgan en lo compartido.
    private var shareFrame: CGRect?
    private var shareSource = ""
    func startShare(_ request: String) {
        let parts = request.split(separator: ":").map(String.init)
        let source = parts.count > 2 ? parts[2] : (UserDefaults.standard.string(forKey: "traigger.share.source") ?? "cinema")
        let mode = parts.count > 3 ? parts[3] : (UserDefaults.standard.string(forKey: "traigger.share.mode") ?? "logo")
        UserDefaults.standard.set(source, forKey: "traigger.share.source"); UserDefaults.standard.set(mode, forKey: "traigger.share.mode")
        if !shareSource.isEmpty && shareSource != source { closeShareContent() }
        guard let bounds = uiWindow?.rootViewController?.view.bounds else { return }
        // Zona del contenido: la que se dibujó en el editor de escena (vídeo o capturadora); si ocupa toda la pantalla,
        // la de siempre (pantalla completa, o el 70 % con Traigger al lado).
        let editorRect = (source == "hdmi" ? store.state.layerCapture : store.state.layerMovie).flatMap { $0.count == 4 ? CGRect(x: $0[0], y: $0[1], width: $0[2], height: $0[3]) : nil }
        var frame = mode == "side" ? CGRect(x: 0, y: 0, width: bounds.width * 0.7, height: bounds.height) : bounds
        if let r = editorRect, r != CGRect(x: 0, y: 0, width: 1, height: 1) {
            frame = CGRect(x: r.minX * bounds.width, y: r.minY * bounds.height, width: r.width * bounds.width, height: r.height * bounds.height).intersection(bounds)
        }
        shareFrame = frame; shareSource = source
        store.send("layout", s: mode == "side" ? "side" : "corner")
        switch source {
        case "hdmi": store.send("capture.window", b: true)    // Unity enciende la captura si hace falta y pide la ventana
        case "web":
            presentBrowser()
            if let page = browser?.view { page.frame = frame; page.layer.cornerRadius = 0 }
        default: store.send("cinema.window", b: true)
        }
        for window in videoWindows.values { window.present(frame) }
        store.playerControlsHidden = true
        store.hidden = true
    }
    func stopShare() {
        closeShareContent()
        shareFrame = nil; shareSource = ""
        store.send("layout", s: "center")
        store.hidden = false
    }
    private func closeShareContent() {
        switch shareSource {
        case "hdmi": store.send("capture.window", b: false); showVideoWindow("hdmi", false)
        case "web": removeBrowser(); store.send("web.overlay", b: false)
        case "": break
        default: store.send("cinema.window", b: false); showVideoWindow("movie", false)
        }
    }

    // Ventanas de vídeo flotantes (iPad/iPhone): la película y la capturadora, cada una en la suya, por encima de todo.
    private var videoWindows: [String: TraiggerVideoWindow] = [:]
    func showVideoWindow(_ kind: String, _ on: Bool) {
        if !on { videoWindows[kind]?.removeFromSuperview(); videoWindows[kind] = nil; updateVideoAvatar(); return }
        guard videoWindows[kind] == nil, let root = uiWindow?.rootViewController?.view else { return }
        let content: UIView = kind == "hdmi" ? TraiggerCapturePreview() : TraiggerMovieLayerView()
        let window = TraiggerVideoWindow(kind: kind, title: kind == "hdmi" ? "Entrada HDMI / USB-C" : "Película", content: content) { [weak self] in
            self?.videoWindows[kind] = nil
            self?.updateVideoAvatar()
            self?.store.send(kind == "hdmi" ? "capture.window" : "cinema.window", b: false)
        }
        // Debajo de Traigger y del menú: las ventanas de vídeo, luego la capa de Traigger, luego el menú.
        if let panel = host?.view { root.insertSubview(window, belowSubview: panel) } else { root.addSubview(window) }
        window.place(in: root.bounds)
        if let frame = shareFrame { window.present(frame) }
        videoWindows[kind] = window
        updateVideoAvatar()
    }
    // Capa transparente de Traigger por encima de todo (navegador, ventanas de vídeo, controles) salvo del menú de
    // configuración (la imagen la manda AvatarOverlayFeed.cs). Los toques sobre el menú o los controles son suyos.
    private var videoAvatar: TraiggerAvatarLayer?
    private func updateVideoAvatar() {
        guard let root = uiWindow?.rootViewController?.view else { return }
        if videoWindows.isEmpty && browserWindow == nil {
            videoAvatar?.removeFromSuperview(); videoAvatar = nil
            store.send("avatar.feed", b: false)
            return
        }
        if videoAvatar == nil {
            let layer = TraiggerAvatarLayer(frame: root.bounds)
            layer.autoresizingMask = [.flexibleWidth, .flexibleHeight]
            layer.blocked = { [weak self] point in
                guard let self else { return false }
                return [self.host?.view, self.player?.view].contains { view in
                    guard let view, !view.isHidden, let superview = view.superview else { return false }
                    return view.frame.contains(superview.convert(point, from: layer))
                }
            }
            TraiggerAvatarLayer.register(layer)
            videoAvatar = layer
            store.send("avatar.feed", b: true)
        }
        guard let layer = videoAvatar else { return }
        // Navegador con «Traigger encima»: va en su propia ventana por encima de la de Unity, así que la capa de
        // Traigger tiene que ir dentro de esa ventana, delante de la página (si no, el navegador la tapa).
        if let container = browserWindow?.rootViewController?.view {
            if layer.superview !== container { layer.removeFromSuperview(); layer.frame = container.bounds; container.addSubview(layer) }
            container.bringSubviewToFront(layer)
            return
        }
        if layer.superview !== root { layer.removeFromSuperview(); layer.frame = root.bounds }
        // Por encima de todo salvo del menú de configuración, que queda delante.
        if let panel = host?.view { root.insertSubview(layer, belowSubview: panel) } else { root.bringSubviewToFront(layer) }
    }
    func handle(_ request: String) {
        if request.hasPrefix("web:open:") { TraiggerBrowser.shared.open(String(request.dropFirst(9))); presentBrowser() }
        else if request == "web:show" { presentBrowser() }
        else if request.hasPrefix("share:open") { startShare(request) }
        else if request == "share:close" { stopShare() }
        else if request == "cinema:window:on" || request == "cinema:window:off" { showVideoWindow("movie", request.hasSuffix("on")) }
        else if request == "hdmi:window:on" || request == "hdmi:window:off" { showVideoWindow("hdmi", request.hasSuffix("on")) }
        else if request == "web:close" { removeBrowser(); store.send("web.overlay", b: false); store.send("layout", s: "center") }
        else if request == "web:overlay:on" || request == "web:overlay:off" {
            let on = request.hasSuffix("on")
            TraiggerTouchPassthrough.enabled = on && browser != nil
        }
    }
    // MARK: Ventana flotante del navegador (modo «Traigger encima»)
    private weak var browserContainer: UIView?
    private var browserFull = false
    private var browserFrameAtStart = CGRect.zero
    private func savedBrowserFrame(in bounds: CGRect) -> CGRect {
        UserDefaults.standard.string(forKey: "traigger.web.frame").map { NSCoder.cgRect(for: $0) }
            ?? CGRect(x: bounds.width * 0.05, y: bounds.height * 0.08, width: bounds.width * 0.62, height: bounds.height * 0.8)
    }
    private func clampBrowser(_ frame: CGRect, in bounds: CGRect) -> CGRect {
        var f = frame
        f.size.width = min(max(f.width, 360), bounds.width); f.size.height = min(max(f.height, 300), bounds.height)
        f.origin.x = min(max(f.minX, 0), bounds.width - f.width); f.origin.y = min(max(f.minY, 0), bounds.height - f.height)
        return f
    }
    func browserGesture(_ translation: CGSize, resize: Bool, ended: Bool) {
        guard let page = browser?.view, let container = browserContainer, !browserFull else { return }
        if browserFrameAtStart == .zero { browserFrameAtStart = page.frame }
        var frame = browserFrameAtStart
        if resize { frame.size.width += translation.width; frame.size.height += translation.height }
        else { frame.origin.x += translation.width; frame.origin.y += translation.height }
        page.frame = clampBrowser(frame, in: container.bounds)
        if ended { UserDefaults.standard.set(NSCoder.string(for: page.frame), forKey: "traigger.web.frame"); browserFrameAtStart = .zero }
    }
    func toggleBrowserFull() {
        guard let page = browser?.view, let container = browserContainer else { return }
        browserFull.toggle()
        UIView.animate(withDuration: 0.25) { page.frame = self.browserFull ? container.bounds : self.clampBrowser(self.savedBrowserFrame(in: container.bounds), in: container.bounds) }
    }

    private var browserWindow: UIWindow?
    private func removeBrowser() {
        browser?.willMove(toParent: nil); browser?.view.removeFromSuperview(); browser?.removeFromParent(); browser = nil
        browserWindow?.isHidden = true; browserWindow = nil; updateVideoAvatar()
        TraiggerTouchPassthrough.enabled = false
    }
    private func setOverlay(_ on: Bool) {
        overlay = on; UserDefaults.standard.set(on, forKey: "traigger.web.overlay")
        removeBrowser(); presentBrowser()
    }
    private func presentBrowser() {
        TraiggerBrowser.shared.pageChanged = { url, title in TraiggerOverlay.shared.store.send("web.page", s: url + "\n" + title) }
        guard browser == nil, let window = UIApplication.shared.connectedScenes.compactMap({ ($0 as? UIWindowScene)?.windows.first { $0.isKeyWindow } }).first,
              let root = window.rootViewController else { return }
        let view = TraiggerBrowserView {
            if TraiggerOverlay.shared.overlay {
                // Asa para mover la ventana flotante del navegador.
                Image(systemName: "line.3.horizontal").font(.title3).foregroundStyle(.secondary).padding(.horizontal, 6)
                    .contentShape(Rectangle())
                    .gesture(DragGesture(coordinateSpace: .global)
                        .onChanged { TraiggerOverlay.shared.browserGesture($0.translation, resize: false, ended: false) }
                        .onEnded { TraiggerOverlay.shared.browserGesture($0.translation, resize: false, ended: true) })
                Button { TraiggerOverlay.shared.toggleBrowserFull() } label: { Image(systemName: "arrow.up.left.and.arrow.down.right") }
            }
            Button { TraiggerOverlay.shared.setOverlay(!TraiggerOverlay.shared.overlay) } label: {
                Image(systemName: TraiggerOverlay.shared.overlay ? "rectangle.split.2x1" : "person.crop.rectangle.stack")
            }
            .accessibilityLabel(TraiggerOverlay.shared.overlay ? "Traigger al lado" : "Traigger encima")
            Button { TraiggerOverlay.shared.handle("web:close") } label: { Image(systemName: "xmark.circle.fill").font(.title2) }
        }
        .background(.regularMaterial)
        .overlay(alignment: .bottomTrailing) {
            if TraiggerOverlay.shared.overlay {
                // Asa de la esquina: redimensionar la ventana del navegador.
                Image(systemName: "arrow.up.left.and.arrow.down.right").font(.caption.bold()).padding(10).background(.thinMaterial, in: Circle()).padding(6)
                    .gesture(DragGesture(coordinateSpace: .global)
                        .onChanged { TraiggerOverlay.shared.browserGesture($0.translation, resize: true, ended: false) }
                        .onEnded { TraiggerOverlay.shared.browserGesture($0.translation, resize: true, ended: true) })
            }
        }
        let controller = UIHostingController(rootView: AnyView(view))
        let page = controller.view!
        let compact = root.traitCollection.horizontalSizeClass == .compact
        if overlay {
            // El navegador va en su propia ventana por ENCIMA de la de Unity: recibe sus toques como cualquier app.
            // Traigger se dibuja sobre él como una capa de imagen transparente (AvatarOverlayFeed.cs) que solo atiende los
            // toques que caen sobre Traigger (arrastrar / pellizcar). Fuera de la ventana del navegador, todo va a Unity.
            guard let scene = window.windowScene else { return }
            let holder = UIViewController(); holder.view = TraiggerPassThroughView(); holder.view.backgroundColor = .clear
            let browserWindow = TraiggerPassThroughWindow(windowScene: scene)
            browserWindow.windowLevel = UIWindow.Level(rawValue: window.windowLevel.rawValue + 1)
            browserWindow.backgroundColor = .clear
            browserWindow.rootViewController = holder
            holder.addChild(controller); holder.view.addSubview(page); controller.didMove(toParent: holder)
            page.layer.cornerRadius = 22; page.clipsToBounds = true
            browserWindow.isHidden = false
            self.browserWindow = browserWindow
            updateVideoAvatar()
            browserContainer = holder.view
            page.frame = browserFull ? holder.view.bounds : clampBrowser(savedBrowserFrame(in: holder.view.bounds), in: holder.view.bounds)
            NSLog("Traigger web encima: ventana del navegador por encima de Unity, con la capa de Traigger")
            TraiggerTouchPassthrough.enabled = false
            browser = controller
            store.send("web.overlay", b: true)
            store.send("layout", s: "corner")
        } else {
            root.addChild(controller); root.view.addSubview(page); controller.didMove(toParent: root)
            page.translatesAutoresizingMaskIntoConstraints = false; page.layer.cornerRadius = 22; page.clipsToBounds = true
            let guide = root.view.safeAreaLayoutGuide
            NSLayoutConstraint.activate([
                page.leadingAnchor.constraint(equalTo: guide.leadingAnchor, constant: 12),
                page.topAnchor.constraint(equalTo: guide.topAnchor, constant: 12),
                // iPhone: la mitad de arriba; iPad: los 3/5 de la izquierda. El resto queda para Traigger.
                compact ? page.trailingAnchor.constraint(equalTo: guide.trailingAnchor, constant: -12) : page.widthAnchor.constraint(equalTo: guide.widthAnchor, multiplier: 0.6),
                compact ? page.heightAnchor.constraint(equalTo: guide.heightAnchor, multiplier: 0.55) : page.bottomAnchor.constraint(equalTo: guide.bottomAnchor, constant: -12)])
            store.send("web.overlay", b: false)
            store.send("layout", s: compact ? "corner" : "right")
        }
        if let panel = host?.view { root.view.bringSubviewToFront(panel) }
        if let controls = player?.view { root.view.bringSubviewToFront(controls) }
        browser = controller
    }

    @objc private func showControls() { if store.state.moviePlaying == true { store.showPlayerControls() } }

    @objc private func toggle() { store.hidden.toggle() }

    // MARK: Panel flotante (iPad): se mueve por su barra y se redimensiona por la esquina; recuerda posición y tamaño.
    @Published var floating = UserDefaults.standard.object(forKey: "traigger.ui.floating") as? Bool ?? true
    private weak var panelRoot: UIView?
    private var panelConstraints: [NSLayoutConstraint] = []
    private var frameAtGestureStart = CGRect.zero
    private var savedFrame: CGRect {
        get { UserDefaults.standard.string(forKey: "traigger.ui.frame").map { NSCoder.cgRect(for: $0) } ?? CGRect(x: 40, y: 60, width: 420, height: 640) }
        set { UserDefaults.standard.set(NSCoder.string(for: newValue), forKey: "traigger.ui.frame") }
    }
    private func applyPanelLayout() {
        guard let view = host?.view, let root = panelRoot else { return }
        NSLayoutConstraint.deactivate(panelConstraints)
        let guide = root.safeAreaLayoutGuide
        if floating {
            let frame = clamp(savedFrame, in: root.bounds)
            panelConstraints = [view.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: frame.minX), view.topAnchor.constraint(equalTo: root.topAnchor, constant: frame.minY),
                                view.widthAnchor.constraint(equalToConstant: frame.width), view.heightAnchor.constraint(equalToConstant: frame.height)]
            view.layer.shadowOpacity = 0.25
        } else {
            panelConstraints = [view.trailingAnchor.constraint(equalTo: guide.trailingAnchor, constant: -12), view.topAnchor.constraint(equalTo: guide.topAnchor, constant: 12),
                                view.bottomAnchor.constraint(equalTo: guide.bottomAnchor, constant: -12), view.widthAnchor.constraint(equalToConstant: 420)]
        }
        NSLayoutConstraint.activate(panelConstraints)
        root.layoutIfNeeded()
    }
    private func clamp(_ frame: CGRect, in bounds: CGRect) -> CGRect {
        var f = frame
        f.size.width = min(max(f.width, 320), bounds.width - 24); f.size.height = min(max(f.height, 360), bounds.height - 24)
        f.origin.x = min(max(f.minX, 12), bounds.width - f.width - 12); f.origin.y = min(max(f.minY, 12), bounds.height - f.height - 12)
        return f
    }
    func setFloating(_ on: Bool) { floating = on; UserDefaults.standard.set(on, forKey: "traigger.ui.floating"); applyPanelLayout() }
    // Arrastre de la barra (mover) o del asa de la esquina (redimensionar).
    func panelGesture(_ translation: CGSize, resize: Bool, ended: Bool) {
        guard floating, panelConstraints.count == 4, let view = host?.view, let root = panelRoot else { return }
        if frameAtGestureStart == .zero { frameAtGestureStart = view.frame }
        var frame = frameAtGestureStart
        if resize { frame.size.width += translation.width; frame.size.height += translation.height }
        else { frame.origin.x += translation.width; frame.origin.y += translation.height }
        frame = clamp(frame, in: root.bounds)
        panelConstraints[0].constant = frame.minX; panelConstraints[1].constant = frame.minY
        panelConstraints[2].constant = frame.width; panelConstraints[3].constant = frame.height
        if ended { savedFrame = frame; frameAtGestureStart = .zero }
    }
    nonisolated func gestureRecognizer(_ g: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
        MainActor.assumeIsolated { g !== panelPanRecognizer }
    }
    // El arrastre del panel solo empieza en la barra superior (mover) o en el asa de la esquina (redimensionar).
    private weak var panelPanRecognizer: UIGestureRecognizer?
    private var panelResizing = false
    nonisolated func gestureRecognizerShouldBegin(_ g: UIGestureRecognizer) -> Bool {
        MainActor.assumeIsolated {
            guard g is UIPanGestureRecognizer, let view = g.view, view === host?.view else { return true }
            panelPanRecognizer = g
            guard floating else { return false }
            let p = g.location(in: view), b = view.bounds
            panelResizing = p.x > b.width - 56 && p.y > b.height - 56
            return panelResizing || p.y < 56
        }
    }
    @objc private func panelPan(_ g: UIPanGestureRecognizer) {
        let t = g.translation(in: g.view?.superview)
        let ended = g.state == .ended || g.state == .cancelled || g.state == .failed
        panelGesture(CGSize(width: t.x, height: t.y), resize: panelResizing, ended: ended)
    }
}

struct OverlayRoot: View {
    @ObservedObject var store: TraiggerStore
    @ObservedObject private var overlay = TraiggerOverlay.shared
    @Environment(\.horizontalSizeClass) private var sizeClass
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 14) {
                if sizeClass != .compact && overlay.floating { Image(systemName: "line.3.horizontal").foregroundStyle(.secondary) }
                Text("Traigger").font(.title3.bold())
                Spacer()
                if sizeClass != .compact {
                    Button { overlay.setFloating(!overlay.floating) } label: { Image(systemName: overlay.floating ? "sidebar.right" : "macwindow").font(.title3) }
                        .accessibilityLabel(overlay.floating ? "Acoplar a la derecha" : "Ventana flotante")
                }
                Button { store.hidden = true } label: { Image(systemName: "eye.slash").font(.title3) }.accessibilityLabel("Ocultar la interfaz")
            }
            .padding(.horizontal, 18).padding(.vertical, 12)
            .contentShape(Rectangle())
            // Arrastrar la barra mueve la ventana flotante.
            .gesture(DragGesture(coordinateSpace: .global)
                .onChanged { overlay.panelGesture($0.translation, resize: false, ended: false) }
                .onEnded { overlay.panelGesture($0.translation, resize: false, ended: true) })
            TraiggerPanel(store: store)
        }
        .background(.ultraThinMaterial)
        .overlay(alignment: .bottomTrailing) {
            if sizeClass != .compact && overlay.floating {
                // Asa de la esquina: redimensionar.
                Image(systemName: "arrow.up.left.and.arrow.down.right").font(.caption.bold()).padding(10)
                    .background(.thinMaterial, in: Circle()).padding(6)
                    .gesture(DragGesture(coordinateSpace: .global)
                        .onChanged { overlay.panelGesture($0.translation, resize: true, ended: false) }
                        .onEnded { overlay.panelGesture($0.translation, resize: true, ended: true) })
            }
        }
    }
}

@_cdecl("TraiggerUIRegister") public func TraiggerUIRegister(_ command: @convention(c) (UnsafePointer<CChar>) -> Void) {
    TraiggerInProcessTransport.shared.command = command
    DispatchQueue.main.async { TraiggerOverlay.shared.install(); TraiggerAudioGuard.shared.start() }
}
@_cdecl("TraiggerUIPushState") public func TraiggerUIPushState(_ json: UnsafePointer<CChar>) {
    let text = String(cString: json)
    TraiggerInProcessTransport.shared.onMessage?(text)
}

// Peticiones de la API de Unity (VisionWeb) en iPhone/iPad.
@_cdecl("TraiggerNativeRequest") public func TraiggerNativeRequest(_ request: UnsafePointer<CChar>) {
    let text = String(cString: request)
    DispatchQueue.main.async { TraiggerOverlay.shared.handle(text) }
}

// Reparto de toques en el modo «Traigger encima de la web»: la vista de Unity solo se queda los toques que caen sobre
// Traigger (o sobre la interfaz nativa que lleva dentro); el resto sigue hacia la web, que está debajo.
enum TraiggerTouchPassthrough {
    static var enabled = false
    static var avatar = CGRect.zero          // zona de Traigger, 0–1 desde arriba a la izquierda
    private static var lastLog = Date.distantPast
    private static var probing = false
    // La ventana de Unity: si el toque solo da con sus contenedores (o con la vista de Unity fuera de Traigger),
    // devuelve nil y iOS lo entrega a la ventana de debajo (el navegador).
    private static var windowInstalled = false
    private static func installWindowPassthrough() {
        guard !windowInstalled, let window = unityWindow else { return }
        windowInstalled = true
        let type: AnyClass = object_getClass(window)!
        let selector = #selector(UIView.hitTest(_:with:))
        guard let method = class_getInstanceMethod(type, selector) else { return }
        typealias HitTest = @convention(c) (UIView, Selector, CGPoint, UIEvent?) -> UIView?
        let original = unsafeBitCast(method_getImplementation(method), to: HitTest.self)
        let replacement: @convention(block) (UIView, CGPoint, UIEvent?) -> UIView? = { me, point, event in
            let hit = original(me, selector, point, event)
            guard enabled, me === unityWindow else { return hit }
            guard let hit else { return nil }
            // Solo cuentan Unity (dentro de Traigger) y la interfaz nativa; los contenedores de iOS dejan pasar.
            let name = NSStringFromClass(Swift.type(of: hit))
            if hit === me || name.hasPrefix("UITransitionView") || name.hasPrefix("UIDropShadowView") || name.hasPrefix("UILayoutContainerView") { return nil }
            return hit
        }
        method_setImplementation(method, imp_implementationWithBlock(replacement))
    }

    // Diagnóstico: qué vista acaba recibiendo un toque que Unity deja pasar.
    private static func receiverName(_ me: UIView, _ point: CGPoint, _ event: UIEvent?) -> String {
        guard let parent = me.superview else { return "nadie" }
        probing = true
        let target: UIView? = parent.hitTest(me.convert(point, to: parent), with: event)
        probing = false
        guard let target else { return "nadie" }
        return NSStringFromClass(type(of: target))
    }
    private static var installed = false
    private static weak var unityWindow: UIWindow?
    static func install(on view: UIView, window: UIWindow) {
        unityWindow = window
        installWindowPassthrough()
        guard !installed else { return }
        installed = true
        let type: AnyClass = object_getClass(view)!
        let selector = #selector(UIView.hitTest(_:with:))
        guard let method = class_getInstanceMethod(type, selector) else { return }
        typealias HitTest = @convention(c) (UIView, Selector, CGPoint, UIEvent?) -> UIView?
        let original = unsafeBitCast(method_getImplementation(method), to: HitTest.self)
        let replacement: @convention(block) (UIView, CGPoint, UIEvent?) -> UIView? = { me, point, event in
            let hit = original(me, selector, point, event)
            guard enabled, me.bounds.width > 0 else { return hit }
            // La interfaz nativa que va dentro de la vista de Unity (panel, controles) se queda sus toques.
            if let hit, hit !== me, !(hit.layer is CAMetalLayer) { return hit }
            let p = CGPoint(x: point.x / me.bounds.width, y: point.y / me.bounds.height)
            let keep = avatar.contains(p)
            if event?.type == .touches, !probing, Date().timeIntervalSince(lastLog) > 0.5 {
                lastLog = Date()
                // Diagnóstico: qué vista acaba recibiendo el toque cuando se deja pasar.
                let receiver: String = keep ? "Unity" : receiverName(me, point, event)
                NSLog("Traigger toque %.2f,%.2f · zona Traigger %@ → %@", p.x, p.y, NSCoder.string(for: avatar), receiver)
            }
            return keep ? hit : nil
        }
        method_setImplementation(method, imp_implementationWithBlock(replacement))
    }
}
@_cdecl("TraiggerSetAvatarRect") public func TraiggerSetAvatarRect(_ x: Float, _ y: Float, _ width: Float, _ height: Float) {
    TraiggerTouchPassthrough.avatar = CGRect(x: CGFloat(x), y: CGFloat(y), width: CGFloat(width), height: CGFloat(height))
}

// Vista y ventana que no se quedan los toques que caen en su fondo vacío: pasan a la ventana de debajo (Unity).
final class TraiggerPassThroughView: UIView {
    override func hitTest(_ point: CGPoint, with event: UIEvent?) -> UIView? { let hit = super.hitTest(point, with: event); return hit === self ? nil : hit }
}
final class TraiggerPassThroughWindow: UIWindow {
    override func hitTest(_ point: CGPoint, with event: UIEvent?) -> UIView? {
        let hit = super.hitTest(point, with: event)
        return hit === self || hit === rootViewController?.view ? nil : hit
    }
}

// Capa con la imagen de Traigger (transparente) encima del navegador. Solo atiende los toques sobre Traigger:
// arrastrar lo mueve y pellizcar lo escala (órdenes a Unity); el resto llega a la web.
final class TraiggerAvatarLayer: UIView {
    // Puede haber varias a la vez (sobre el navegador y sobre las ventanas de vídeo): todas reciben cada fotograma.
    private static let all = NSHashTable<TraiggerAvatarLayer>.weakObjects()
    static func register(_ layer: TraiggerAvatarLayer) { all.add(layer) }
    var blocked: ((CGPoint) -> Bool)?
    static func showAll(_ image: CGImage) { for layer in all.allObjects where layer.window != nil { layer.show(image) } }
    private var lastPan = CGPoint.zero
    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = .clear
        layer.contentsGravity = .resize
        layer.setAffineTransform(CGAffineTransform(scaleX: 1, y: -1)) // Unity entrega las filas de abajo arriba
        addGestureRecognizer(UIPanGestureRecognizer(target: self, action: #selector(pan(_:))))
        addGestureRecognizer(UIPinchGestureRecognizer(target: self, action: #selector(pinch(_:))))
    }
    required init?(coder: NSCoder) { fatalError() }
    override func hitTest(_ point: CGPoint, with event: UIEvent?) -> UIView? {
        guard bounds.width > 0 else { return nil }
        // La imagen está volteada: «point» ya viene en las coordenadas de la vista (de arriba abajo).
        if blocked?(point) == true { return nil }
        let p = CGPoint(x: point.x / bounds.width, y: 1 - point.y / bounds.height)
        return TraiggerTouchPassthrough.avatar.contains(p) ? self : nil
    }
    @objc private func pan(_ gesture: UIPanGestureRecognizer) {
        let t = gesture.translation(in: self)
        if gesture.state == .began { lastPan = .zero }
        let dx = (t.x - lastPan.x) / bounds.width, dy = -(t.y - lastPan.y) / bounds.height
        lastPan = t
        TraiggerOverlay.shared.store.send("avatar.drag", s: String(format: "%.5f,%.5f", locale: Locale(identifier: "en_US_POSIX"), dx, dy))
    }
    @objc private func pinch(_ gesture: UIPinchGestureRecognizer) {
        TraiggerOverlay.shared.store.send("avatar.pinch", f: Double(gesture.scale)); gesture.scale = 1
    }
    func show(_ image: CGImage) { layer.contents = image }
}

// Fotogramas de Traigger (RGBA, filas de abajo arriba) para la capa sobre el navegador.
@_cdecl("TraiggerAvatarOverlayFrame") public func TraiggerAvatarOverlayFrame(_ pixels: UnsafeRawPointer, _ width: Int32, _ height: Int32) {
    let w = Int(width), h = Int(height)
    let data = Data(bytes: pixels, count: w * h * 4)
    guard let provider = CGDataProvider(data: data as CFData),
          let image = CGImage(width: w, height: h, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: w * 4, space: CGColorSpaceCreateDeviceRGB(),
                              bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.last.rawValue), provider: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent) else { return }
    DispatchQueue.main.async { TraiggerAvatarLayer.showAll(image) }
}


// Ventana de vídeo flotante: se mueve arrastrando, se escala pellizcando o con el asa de la esquina, y un doble toque
// la pasa a pantalla completa y la devuelve. Recuerda posición y tamaño. Todo con UIKit (el arrastre de SwiftUI no
// se dispara en la ventana de la interfaz).
final class TraiggerVideoWindow: UIView {
    private let kind: String, content: UIView, onClose: () -> Void
    private let bar = UIView(), handle = UIImageView(image: UIImage(systemName: "arrow.up.left.and.arrow.down.right"))
    private var start = CGRect.zero, resizing = false, full = false, beforeFull = CGRect.zero
    private var key: String { "traigger.video.\(kind).frame" }
    init(kind: String, title: String, content: UIView, onClose: @escaping () -> Void) {
        self.kind = kind; self.content = content; self.onClose = onClose
        super.init(frame: .zero)
        backgroundColor = .black
        layer.cornerRadius = 16; layer.cornerCurve = .continuous; clipsToBounds = true
        addSubview(content)
        bar.backgroundColor = UIColor.black.withAlphaComponent(0.45)
        let label = UILabel(); label.text = title; label.textColor = .white; label.font = .preferredFont(forTextStyle: .subheadline)
        let close = UIButton(type: .close); close.addAction(UIAction { [weak self] _ in self?.removeFromSuperview(); self?.onClose() }, for: .touchUpInside)
        let fullButton = UIButton(type: .system); fullButton.tintColor = .white
        fullButton.setImage(UIImage(systemName: "arrow.up.left.and.down.right.magnifyingglass"), for: .normal)
        fullButton.addAction(UIAction { [weak self] _ in self?.toggleFull() }, for: .touchUpInside)
        for (view, x) in [(label as UIView, 0), (fullButton, 1), (close, 2)] { view.tag = x; bar.addSubview(view) }
        addSubview(bar)
        handle.tintColor = .white; handle.contentMode = .center
        handle.backgroundColor = UIColor.black.withAlphaComponent(0.45); handle.layer.cornerRadius = 16
        addSubview(handle)
        addGestureRecognizer(UIPanGestureRecognizer(target: self, action: #selector(pan(_:))))
        addGestureRecognizer(UIPinchGestureRecognizer(target: self, action: #selector(pinch(_:))))
        let double = UITapGestureRecognizer(target: self, action: #selector(toggleFull)); double.numberOfTapsRequired = 2; addGestureRecognizer(double)
        (content as? TraiggerMovieLayerView)?.onAspect = { [weak self] aspect in self?.fit(aspect) }
    }
    required init?(coder: NSCoder) { fatalError() }

    override func layoutSubviews() {
        super.layoutSubviews()
        content.frame = bounds
        bar.frame = CGRect(x: 0, y: 0, width: bounds.width, height: 40)
        for view in bar.subviews {
            switch view.tag {
            case 0: view.frame = CGRect(x: 14, y: 0, width: max(0, bounds.width - 110), height: 40)
            case 1: view.frame = CGRect(x: bounds.width - 84, y: 4, width: 32, height: 32)
            default: view.frame = CGRect(x: bounds.width - 44, y: 6, width: 30, height: 30)
            }
        }
        handle.frame = CGRect(x: bounds.width - 40, y: bounds.height - 40, width: 32, height: 32)
    }

    // Modo compartir: ocupa el marco dado, sin barra ni asa (no deben salir en la pantalla compartida).
    private var presenting = false
    func present(_ rect: CGRect) {
        presenting = true; full = false
        bar.isHidden = true; handle.isHidden = true
        layer.cornerRadius = 0
        frame = rect
    }
    func place(in bounds: CGRect) {
        let saved = UserDefaults.standard.string(forKey: key).map { NSCoder.cgRect(for: $0) }
        frame = clamp(saved ?? CGRect(x: bounds.width * 0.08, y: bounds.height * 0.1, width: bounds.width * 0.5, height: bounds.width * 0.5 * 9 / 16), in: bounds)
    }
    // Ajusta el alto a la proporción del vídeo, manteniendo el ancho.
    private func fit(_ aspect: CGFloat) {
        guard aspect > 0, !full, let parent = superview else { return }
        var f = frame; f.size.height = f.width / aspect
        frame = clamp(f, in: parent.bounds)
    }
    private func clamp(_ frame: CGRect, in bounds: CGRect) -> CGRect {
        var f = frame
        f.size.width = min(max(f.width, 240), bounds.width); f.size.height = min(max(f.height, 150), bounds.height)
        f.origin.x = min(max(f.minX, 0), bounds.width - f.width); f.origin.y = min(max(f.minY, 0), bounds.height - f.height)
        return f
    }
    private func save() { if !full { UserDefaults.standard.set(NSCoder.string(for: frame), forKey: key) } }

    override func gestureRecognizerShouldBegin(_ g: UIGestureRecognizer) -> Bool {
        guard g is UIPanGestureRecognizer else { return true }
        resizing = handle.frame.insetBy(dx: -16, dy: -16).contains(g.location(in: self))
        return true
    }
    @objc private func pan(_ g: UIPanGestureRecognizer) {
        guard let parent = superview, !full, !presenting else { return }
        if g.state == .began { start = frame }
        let t = g.translation(in: parent)
        var f = start
        if resizing { f.size.width += t.x; f.size.height += t.y } else { f.origin.x += t.x; f.origin.y += t.y }
        frame = clamp(f, in: parent.bounds)
        if g.state == .ended || g.state == .cancelled { save() }
    }
    @objc private func pinch(_ g: UIPinchGestureRecognizer) {
        guard let parent = superview, !full, !presenting else { return }
        if g.state == .began { start = frame }
        let w = start.width * g.scale, h = start.height * g.scale
        frame = clamp(CGRect(x: start.midX - w / 2, y: start.midY - h / 2, width: w, height: h), in: parent.bounds)
        if g.state == .ended || g.state == .cancelled { save() }
    }
    @objc private func toggleFull() {
        guard let parent = superview, !presenting else { return }
        full.toggle()
        if full { beforeFull = frame }
        UIView.animate(withDuration: 0.25) { self.frame = self.full ? parent.bounds : self.beforeFull; self.layer.cornerRadius = self.full ? 0 : 16 }
    }
}

// Vista previa de la capturadora (latencia mínima: la imagen va directa de la sesión de captura a la pantalla).
final class TraiggerCapturePreview: UIView {
    override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
    private var preview: AVCaptureVideoPreviewLayer { layer as! AVCaptureVideoPreviewLayer }
    private var timer: Timer?
    private let label = UILabel()
    private let remote = TraiggerMacReceiver.shared.layer
    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = .black
        preview.videoGravity = .resizeAspect
        label.text = "Conecta la capturadora y actívala en Ajustes → Entrada HDMI, o envíala desde el Mac con «Traigger Camera»"
        label.textColor = .secondaryLabel; label.numberOfLines = 0; label.textAlignment = .center
        addSubview(label)
        // La sesión se crea al activar la captura (o cambia de aparato): se engancha sola cuando aparece.
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            guard let self else { return }
            let session = TraiggerCapture.shared.previewSession
            if self.preview.session !== session { self.preview.session = session }
            // Sin capturadora aquí pero llegando del Mac: se muestra lo recibido.
            let remote = session == nil && TraiggerMacReceiver.shared.connected
            if remote != (self.remote.superlayer != nil) { remote ? self.layer.addSublayer(self.remote) : self.remote.removeFromSuperlayer() }
            self.label.isHidden = session != nil || remote
        }
    }
    required init?(coder: NSCoder) { fatalError() }
    deinit { timer?.invalidate() }
    override func layoutSubviews() { super.layoutSubviews(); label.frame = bounds.insetBy(dx: 20, dy: 20); remote.frame = bounds }
}


// Sonido aunque el iPhone/iPad esté en silencio: Unity deja la sesión de audio en «ambient», que obedece al modo
// silencio (como los juegos). Mientras nadie use el micrófono, se pasa a «playback», que suena siempre. Si la voz o la
// capturadora ponen «playAndRecord» (que también ignora el silencio) no se toca. Se vuelve a comprobar tras cada
// interrupción, cambio de salida o vuelta a primer plano, porque Unity reconfigura el audio en esos momentos.
@MainActor final class TraiggerAudioGuard {
    static let shared = TraiggerAudioGuard()
    private var observers: [NSObjectProtocol] = []
    private var timer: Timer?
    func start() {
        guard observers.isEmpty else { return }
        let center = NotificationCenter.default
        for name in [AVAudioSession.interruptionNotification, AVAudioSession.routeChangeNotification, AVAudioSession.mediaServicesWereResetNotification,
                     UIApplication.didBecomeActiveNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { _ in
                // Unity rehace su audio 0,1 s después de volver: se comprueba un poco más tarde.
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { MainActor.assumeIsolated { TraiggerAudioGuard.shared.check() } }
            })
        }
        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { _ in MainActor.assumeIsolated { TraiggerAudioGuard.shared.check() } }
        check()
    }
    func check() {
        let session = AVAudioSession.sharedInstance()
        guard session.category == .ambient || session.category == .soloAmbient else { return }
        do {
            try session.setCategory(.playback, mode: .moviePlayback, options: [.mixWithOthers])
            try session.setActive(true)
        } catch { NSLog("Traigger: no se pudo activar el sonido en modo silencio: %@", error.localizedDescription) }
    }
}
