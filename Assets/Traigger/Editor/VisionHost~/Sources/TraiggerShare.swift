import AVFoundation
import SwiftUI
import UIKit

// Vision Pro: Traigger sin ventana ni fondo, y la ventana para compartir por SharePlay/FaceTime.
// La imagen de Traigger llega ya recortada a su silueta y con transparencia (TraiggerUIBridge.mm, «Traigger.Avatar»);
// Unity solo la produce mientras alguien la usa (orden «avatar.feed»).
@MainActor final class TraiggerAvatarFeed: ObservableObject {
    static let shared = TraiggerAvatarFeed()
    @Published private(set) var image: CGImage?
    @Published private(set) var lastFrame = Date.distantPast
    var receiving: Bool { Date().timeIntervalSince(lastFrame) < 1.5 }
    private var users = 0
    private var observer: NSObjectProtocol?
    var aspect: CGFloat { guard let image, image.height > 0 else { return 0.7 }; return CGFloat(image.width) / CGFloat(image.height) }
    func retain() {
        users += 1
        if observer == nil {
            observer = NotificationCenter.default.addObserver(forName: Notification.Name("Traigger.Avatar"), object: nil, queue: .main) { note in
                let image = note.object as! CGImage
                MainActor.assumeIsolated { TraiggerAvatarFeed.shared.image = image; TraiggerAvatarFeed.shared.lastFrame = Date() }
            }
        }
        if users == 1 { TraiggerPanelHost.store.send("avatar.feed", b: true) }
    }
    func release() {
        users = max(0, users - 1)
        if users == 0 { TraiggerPanelHost.store.send("avatar.feed", b: false) }
    }
}

struct TraiggerAvatarImage: View {
    @ObservedObject var feed = TraiggerAvatarFeed.shared
    var body: some View {
        Group {
            if let image = feed.image { Image(decorative: image, scale: 1).resizable().interpolation(.high).aspectRatio(contentMode: .fit) }
            else { Color.clear }
        }
        .onAppear { feed.retain() }
        .onDisappear { feed.release() }
    }
}

// Traigger «libre»: un volumen sin cristal ni fondo; se coge por la barra de abajo y se lleva a cualquier sitio, y se
// agranda por las esquinas. Mientras tanto la ventana de Unity se vuelve invisible (sigue dibujando la imagen).
struct TraiggerFreeVolume: View {
    @Environment(\.dismissWindow) private var dismissWindow
    @ObservedObject private var feed = TraiggerAvatarFeed.shared
    @State private var waited = false
    var body: some View {
        TraiggerAvatarImage()
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .overlay {
                if waited && !feed.receiving {
                    Text("No llega la imagen de Traigger. Vuelve a compilar con el menú 5 de Unity (aparato).")
                        .padding().glassBackgroundEffect()
                }
            }
            // La ventana de Unity solo se oculta cuando ya llegan imágenes: así Traigger nunca desaparece.
            .onReceive(feed.$lastFrame) { _ in if feed.receiving { TraiggerUnity.shared.setWindowVisible(false) } }
            .task {
                try? await Task.sleep(for: .seconds(2)); waited = true
                // Si con la ventana oculta dejan de llegar imágenes, la ventana de Unity vuelve a verse.
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(1))
                    if !feed.receiving { TraiggerUnity.shared.setWindowVisible(true) }
                }
            }
            .ornament(attachmentAnchor: .scene(.bottom)) {
                Button { NotificationCenter.default.post(name: Notification.Name("Traigger.Request"), object: nil, userInfo: ["request": "free:off"]) } label: {
                    Label("Volver a la ventana", systemImage: "rectangle.inset.filled")
                }
                .padding(10).glassBackgroundEffect()
            }
            .onDisappear { TraiggerUnity.shared.setWindowVisible(true) }
    }
}

// Ventana para compartir: la actividad (cine, HDMI o navegador) con Traigger encima como el logo de un canal, o a su
// lado como el avatar de una llamada. Es la ventana que se comparte en FaceTime/SharePlay. No lleva ningún botón ni
// adorno (también saldrían en lo emitido): las opciones están en el menú (Ajustes → Espacio y SharePlay), y a Traigger
// se le mueve arrastrándolo y se le escala pellizcando, directamente sobre la ventana.
struct TraiggerShareWindow: View {
    @AppStorage("traigger.share.source") private var source = "cinema"
    @AppStorage("traigger.share.mode") private var mode = "logo"
    @AppStorage("traigger.share.webControls") private var webControls = true
    // Posición (centro, 0–1 de la ventana) y alto (fracción del alto) de Traigger en el modo logo.
    @AppStorage("traigger.share.x") private var x = 0.86
    @AppStorage("traigger.share.y") private var y = 0.78
    @AppStorage("traigger.share.size") private var size = 0.32
    @State private var dragStart: CGPoint?
    @State private var sizeStart: Double?
    @Environment(\.dismissWindow) private var dismissWindow
    @ObservedObject private var spatial = TraiggerSpatial.shared
    @ObservedObject private var hdmi = TraiggerHDMIReceiver.shared
    @ObservedObject private var feed = TraiggerAvatarFeed.shared
    @ObservedObject private var store = TraiggerPanelHost.store
    private var showingCard: Bool { store.state.cardKind?.isEmpty == false }
    private var cardCovers: Bool { store.state.cardCover == true }

    private var aspect: CGFloat { source == "hdmi" ? hdmi.aspect : source == "web" ? 16.0 / 10.0 : spatial.aspect }

    var body: some View {
        Group {
            if mode == "side" {
                // Avatar de llamada: la actividad y, al lado, Traigger en su recuadro.
                HStack(spacing: 16) {
                    content.aspectRatio(aspect, contentMode: .fit)
                        .overlay { if showingCard && !cardCovers { TraiggerShareCard(state: store.state) } }
                        .clipShape(RoundedRectangle(cornerRadius: 24, style: .continuous))
                    TraiggerAvatarImage()
                        .padding(12)
                        .frame(maxWidth: 360, maxHeight: .infinity)
                        .background(Color.black.opacity(0.35), in: RoundedRectangle(cornerRadius: 24, style: .continuous))
                }
                .padding(16)
            } else {
                content.aspectRatio(aspect, contentMode: .fit)
                    .overlay { if showingCard && !cardCovers { TraiggerShareCard(state: store.state) } }
                    .overlay { GeometryReader { geometry in logo(in: geometry.size) } }
            }
        }
        // Cartel que tapa también a Traigger.
        .overlay { if showingCard && cardCovers { TraiggerShareCard(state: store.state) } }
        .background(.black)
        // Doble toque: muestra la ventanita de opciones (va aparte, así no sale en lo emitido).
        .simultaneousGesture(TapGesture(count: 2).onEnded { TraiggerRouter.request("share:options:on") })
        // El navegador y la imagen HDMI solo pueden estar en un sitio: su ventana propia se cierra mientras se comparte.
        .onAppear { closeSingleUse(); TraiggerRouter.request("share:options:on") }
        .onDisappear { TraiggerRouter.request("share:options:off") }
        .onChange(of: source) { _, _ in closeSingleUse() }
    }

    // Traigger como logo: arrastrar lo mueve, pellizcar lo escala (se recuerda para la próxima vez).
    private func logo(in area: CGSize) -> some View {
        let height = area.height * size, width = height * max(0.2, feed.aspect)
        return TraiggerAvatarImage()
            .frame(width: width, height: height)
            .contentShape(Rectangle())
            .position(x: area.width * x, y: area.height * y)
            .gesture(DragGesture()
                .onChanged { value in
                    let start = dragStart ?? CGPoint(x: x, y: y); dragStart = start
                    x = min(max(start.x + value.translation.width / area.width, 0.05), 0.95)
                    y = min(max(start.y + value.translation.height / area.height, 0.05), 0.95)
                }
                .onEnded { _ in dragStart = nil })
            .simultaneousGesture(MagnifyGesture()
                .onChanged { value in
                    let start = sizeStart ?? size; sizeStart = start
                    size = min(max(start * value.magnification, 0.08), 1)
                }
                .onEnded { _ in sizeStart = nil })
    }

    @ViewBuilder private var content: some View {
        switch source {
        case "hdmi": TraiggerHDMIScreen()
        case "web":
            if webControls { TraiggerBrowserView { EmptyView() } } else { TraiggerWebContent(webView: TraiggerBrowser.shared.webView) }
        default: TraiggerMovieScreen { aspect in Task { @MainActor in TraiggerSpatial.shared.aspect = aspect } }
        }
    }

    private func closeSingleUse() {
        if source == "hdmi" { dismissWindow(id: "traigger-hdmi", value: "main") }
        if source == "web" { dismissWindow(id: "traigger-browser", value: "main") }
    }
}

// Ventanita de opciones de la ventana para compartir (fuera de ella, para que no salga en lo emitido).
struct TraiggerShareOptionsWindow: View {
    var body: some View {
        ScrollView { SpaceAndShareSection(compact: true).padding() }
    }
}

// Sonido del host hacia la emisión en directo (TraiggerStream.swift, dentro de UnityFramework): micrófono y capturadora.
// Solo se copia mientras hay directo.
enum TraiggerStreamAudio {
    private typealias Push = @convention(c) (UnsafePointer<Float>, Int32, Int32, Double, Int32) -> Void
    private typealias IsLive = @convention(c) () -> Int32
    private static let pushFunction: Push? = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "TraiggerStreamPushPCM").map { unsafeBitCast($0, to: Push.self) }
    private static let liveFunction: IsLive? = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "TraiggerStreamIsLive").map { unsafeBitCast($0, to: IsLive.self) }
    static func push(_ buffer: AVAudioPCMBuffer, source: Int32) {
        guard let pushFunction, liveFunction?() == 1, let channels = buffer.floatChannelData else { return }
        let frames = Int(buffer.frameLength), count = Int(buffer.format.channelCount)
        var interleaved = [Float](repeating: 0, count: frames * count)
        for i in 0..<frames { for c in 0..<count { interleaved[i * count + c] = channels[c][i] } }
        interleaved.withUnsafeBufferPointer { pushFunction($0.baseAddress!, Int32(frames), Int32(count), buffer.format.sampleRate, source) }
    }
}

// Cartel («Vuelvo enseguida», «Pausa»…) dibujado en la ventana para compartir, con el texto y la imagen de los
// carteles de Unity (StreamCards.cs).
struct TraiggerShareCard: View {
    let state: TraiggerState
    var body: some View {
        GeometryReader { geometry in
            ZStack {
                Color(red: 0.06, green: 0.07, blue: 0.12).opacity(0.92)
                if let path = state.cardImage, !path.isEmpty, let image = UIImage(contentsOfFile: path) {
                    Image(uiImage: image).resizable().aspectRatio(contentMode: .fill).frame(width: geometry.size.width, height: geometry.size.height).clipped()
                }
                VStack(spacing: geometry.size.height * 0.03) {
                    Text(state.cardTitle ?? "").font(.system(size: geometry.size.height * 0.11, weight: .bold))
                    if let subtitle = state.cardSubtitle, !subtitle.isEmpty { Text(subtitle).font(.system(size: geometry.size.height * 0.055)) }
                }
                .foregroundStyle(.white).multilineTextAlignment(.center).shadow(radius: 8).padding()
            }
        }
        .transition(.opacity)
    }
}
