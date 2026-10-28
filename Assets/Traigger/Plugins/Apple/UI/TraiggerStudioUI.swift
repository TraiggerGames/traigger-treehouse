import SwiftUI
import UniformTypeIdentifiers

// Pantallas «de estudio» de Traigger, iguales en iPhone/iPad, Vision Pro y Traigger Control (Mac):
//  · Fuentes: tarjetas grandes para elegir qué mostrar (vídeo, capturadora, navegador); cada una abre su pantalla con
//    «Explorar» (elegir qué) y «Opciones» (cómo se ve), separadas.
//  · Editor de escena: lienzo grande con capas, guías magnéticas e inspector, para componer lo que se ve y se emite.

// MARK: - Presentación a pantalla completa (hoja grande en Mac y Vision Pro)
extension View {
    @ViewBuilder func studioScreen<Content: View>(isPresented: Binding<Bool>, @ViewBuilder content: @escaping () -> Content) -> some View {
        #if os(iOS)
        fullScreenCover(isPresented: isPresented, content: content)
        #else
        sheet(isPresented: isPresented) { content().frame(minWidth: 900, minHeight: 680) }
        #endif
    }
    // Ventana centrada que no ocupa toda la pantalla (configuración de cada fuente). En iPad es una hoja en el centro
    // con Traigger visible alrededor; en iPhone, una hoja que se cierra deslizando hacia abajo.
    @ViewBuilder func studioModal<Content: View>(isPresented: Binding<Bool>, @ViewBuilder content: @escaping () -> Content) -> some View {
        #if os(iOS)
        sheet(isPresented: isPresented) {
            if #available(iOS 18.0, *) { content().presentationSizing(.page) } else { content() }
        }
        #else
        sheet(isPresented: isPresented) { content().frame(minWidth: 720, idealWidth: 780, minHeight: 560, idealHeight: 620) }
        #endif
    }
}

// Barra superior común: icono, título y «Listo».
private struct StudioHeader<Trailing: View>: View {
    let icon: String, title: String, tint: Color
    var close: () -> Void
    @ViewBuilder var trailing: Trailing
    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon).font(.title2).foregroundStyle(.white).frame(width: 44, height: 44)
                .background(LinearGradient(colors: [tint, tint.opacity(0.7)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            Text(title).font(.title2.bold())
            Spacer()
            trailing
            Button("Listo", action: close).buttonStyle(.borderedProminent)
        }
        .padding(.horizontal, 20).padding(.vertical, 14)
    }
}

// MARK: - Pestaña Fuentes
struct SourcesTab: View {
    @ObservedObject var store: TraiggerStore
    @ObservedObject var library: TraiggerLibrary
    @State private var open: String?
    @State private var editor = false
    var body: some View {
        let st = store.state
        Page {
            VStack(alignment: .leading, spacing: 4) {
                Text("¿Qué quieres mostrar?").font(.title2.bold())
                Text("Elige una fuente. Aparece detrás de Traigger, en su propia ventana o en lo que compartas y emitas.").font(.callout).foregroundStyle(.secondary)
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 12)], spacing: 12) {
                SourceCard(icon: "film", title: "Vídeo", tint: .blue,
                           status: st.moviePlaying == true ? (st.movieTitle ?? "Reproduciendo") : "Películas, fototeca o tu NAS",
                           live: st.moviePlaying == true) { open = "video" }
                if st.captureAvailable == true || st.platform == "VisionOS" {
                    SourceCard(icon: "cable.connector", title: "Capturadora", tint: .orange,
                               status: st.captureOn == true ? "Capturando" : st.platform == "VisionOS" ? "Recibe la del iPad o el Mac" : "Consola o cámara por HDMI",
                               live: st.captureOn == true) { open = "hdmi" }
                }
                SourceCard(icon: "globe", title: "Navegador", tint: .teal,
                           status: (st.webTitle?.isEmpty == false ? st.webTitle! : "Webs, vídeos, chats…"),
                           live: st.webUrl?.isEmpty == false) { open = "web" }
            }
            Button { editor = true } label: {
                HStack(spacing: 14) {
                    Image(systemName: "rectangle.3.group").font(.title2)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Editor de escena").font(.headline)
                        Text("Coloca a Traigger y cada fuente donde quieras").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Image(systemName: "chevron.right").foregroundStyle(.secondary)
                }
                .padding(16).frame(maxWidth: .infinity)
                .background(Color.purple.opacity(0.15), in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            }
            .buttonStyle(.plain)
        }
        .studioModal(isPresented: Binding(get: { open != nil }, set: { if !$0 { open = nil } })) {
            SourceScreen(kind: open ?? "video", store: store, library: library) { open = nil }
        }
        .studioScreen(isPresented: $editor) { SceneEditor(store: store) { editor = false } }
    }
}

private struct SourceCard: View {
    let icon: String, title: String, tint: Color, status: String, live: Bool
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Image(systemName: icon).font(.title).foregroundStyle(.white)
                    Spacer()
                    if live {
                        Text("EN USO").font(.caption2.bold()).padding(.horizontal, 7).padding(.vertical, 3)
                            .background(.white.opacity(0.9), in: Capsule()).foregroundStyle(tint)
                    }
                }
                Spacer(minLength: 6)
                Text(title).font(.headline).foregroundStyle(.white)
                Text(status).font(.caption).foregroundStyle(.white.opacity(0.85)).lineLimit(2)
            }
            .padding(16).frame(maxWidth: .infinity, minHeight: 140, alignment: .leading)
            .background(LinearGradient(colors: [tint, tint.opacity(0.7)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 20, style: .continuous))
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Pantalla de cada fuente: «Explorar» y «Opciones» separadas
struct SourceScreen: View {
    let kind: String
    @ObservedObject var store: TraiggerStore
    @ObservedObject var library: TraiggerLibrary
    var close: () -> Void
    @State private var page = "browse"
    private var info: (String, String, Color) {
        switch kind {
        case "hdmi": ("cable.connector", "Capturadora", .orange)
        case "web": ("globe", "Navegador", .teal)
        default: ("film", "Vídeo", .blue)
        }
    }
    var body: some View {
        let st = store.state
        VStack(spacing: 0) {
            StudioHeader(icon: info.0, title: info.1, tint: info.2, close: close) {
                if kind == "video" {
                    Picker("", selection: $page) { Text("Explorar").tag("browse"); Text("Opciones").tag("options") }
                        .pickerStyle(.segmented).frame(width: 240)
                }
            }
            Divider()
            Group {
                switch kind {
                case "hdmi": Page { HDMISection(store: store) }
                case "web":
                    Page {
                        Button { store.send("web.show") } label: { Label("Abrir el navegador", systemImage: "safari").frame(maxWidth: .infinity) }
                            .buttonStyle(.borderedProminent).controlSize(.large)
                    }
                    WebTab(store: store)
                default:
                    if page == "browse" { CinemaTab(store: store, library: library, browseOnly: true) }
                    else if st.moviePlaying == true { Page { NowPlaying(store: store) } }
                    else {
                        EmptyHint(icon: "film", title: "Aún no hay nada reproduciéndose", text: "Elige un vídeo en «Explorar»; aquí podrás cambiar el idioma, los subtítulos, el tamaño y dónde se ve.") { page = "browse" }
                    }
                }
            }
            .frame(maxHeight: .infinity, alignment: .top)
        }
        .background(.background)
        // Al empezar un vídeo, se pasa solo a sus opciones.
        .onChange(of: st.moviePlaying ?? false) { playing in if playing && kind == "video" { page = "options" } }
    }
}

private struct EmptyHint: View {
    let icon: String, title: String, text: String
    var action: (() -> Void)?
    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: icon).font(.system(size: 46)).foregroundStyle(.secondary)
            Text(title).font(.title3.bold())
            Text(text).multilineTextAlignment(.center).foregroundStyle(.secondary).frame(maxWidth: 420)
            if let action { Button("Ir a Explorar", action: action).buttonStyle(.bordered) }
        }
        .padding(40).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Editor de escena
struct SceneEditor: View {
    @ObservedObject var store: TraiggerStore
    var close: () -> Void
    @State private var selected = "avatar"
    @State private var live: [String: CGRect] = [:]
    @State private var start: [String: CGRect] = [:]
    @State private var guides: (x: CGFloat?, y: CGFloat?) = (nil, nil)
    @State private var locked: Set<String> = []
    @State private var safeAreas = true
    @State private var sceneName = ""
    // Formato del lienzo: la salida 16:9 (lo que se emite, se comparte y ve la cámara «Traigger»), vertical o la pantalla.
    @AppStorage("traigger.editor.format") private var format = "16:9"
    private var canvasAspect: Double {
        switch format { case "9:16": 9.0 / 16.0; case "screen": max(0.3, store.state.screenAspect ?? 16.0 / 9.0); default: 16.0 / 9.0 }
    }
    private let layers: [(String, String, String, Color)] = [("avatar", "Traigger", "pawprint.fill", .pink), ("capture", "Capturadora", "cable.connector", .orange), ("movie", "Vídeo", "film", .blue)]

    private func rect(_ key: String) -> CGRect {
        if let r = live[key] { return r }
        let v = key == "avatar" ? store.state.layerAvatar : key == "movie" ? store.state.layerMovie : store.state.layerCapture
        guard let v, v.count == 4 else { return CGRect(x: 0, y: 0, width: 1, height: 1) }
        return CGRect(x: v[0], y: v[1], width: v[2], height: v[3])
    }
    private func visible(_ key: String) -> Bool {
        let st = store.state
        switch key {
        case "avatar": return st.avatarHidden != true
        case "movie": return st.movieBackground != false
        default: return st.captureBackground != false
        }
    }
    private func setVisible(_ key: String, _ on: Bool) {
        switch key {
        case "avatar": store.send("avatarHidden", b: !on)
        case "movie": store.send("movie.background", b: on)
        default: store.send("capture.background", b: on)
        }
    }
    private func send(_ key: String, _ r: CGRect) {
        live[key] = r
        // La ventana para compartir de Vision Pro (y el modo compartir) usan la misma colocación de Traigger.
        if key == "avatar" {
            UserDefaults.standard.set(Double(r.midX), forKey: "traigger.share.x")
            UserDefaults.standard.set(Double(r.midY), forKey: "traigger.share.y")
            UserDefaults.standard.set(Double(r.height), forKey: "traigger.share.size")
        }
        store.send("layer.rect", s: String(format: "%@|%.4f,%.4f,%.4f,%.4f", key, r.minX, r.minY, r.width, r.height))
    }

    var body: some View {
        VStack(spacing: 0) {
            StudioHeader(icon: "rectangle.3.group", title: "Editor de escena", tint: .purple, close: close) {
                Picker("Formato", selection: $format) {
                    Text("16:9").tag("16:9"); Text("Vertical").tag("9:16"); Text("Pantalla").tag("screen")
                }.pickerStyle(.segmented).frame(width: 240)
                Toggle("Guías", isOn: $safeAreas).toggleStyle(.button)
                Button("Restablecer") { live = [:]; store.send("layer.reset") }
            }
            Divider()
            GeometryReader { geometry in
                let wide = geometry.size.width > 760
                if wide {
                    HStack(spacing: 0) { canvas.padding(20).frame(maxWidth: .infinity, maxHeight: .infinity); Divider(); sidebar.frame(width: 320) }
                } else {
                    VStack(spacing: 0) { canvas.padding(20).frame(maxWidth: .infinity, maxHeight: .infinity); Divider(); sidebar.frame(maxHeight: 360) }
                }
            }
        }
        .background(.background)
    }

    // Lienzo: la pantalla en grande. Arrastrar mueve, la esquina escala (sin deformar); se ajusta a bordes y centro.
    private var canvas: some View {
        let aspect = canvasAspect
        return GeometryReader { geometry in
            let size = geometry.size
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 14).fill(Color.black.opacity(0.85))
                // Zonas seguras: lo que queda dentro de un 16:9 y de un 9:16 centrados (emisión horizontal y vertical).
                if safeAreas && format != "9:16" { safeArea(9.0 / 16.0, size, "9:16") }
                if safeAreas && format == "9:16" { safeArea(1, size, "1:1") }
                ForEach(layers.reversed(), id: \.0) { layer in layerBox(layer, size) }
                if let x = guides.x { Rectangle().fill(Color.yellow).frame(width: 1, height: size.height).position(x: x * size.width, y: size.height / 2) }
                if let y = guides.y { Rectangle().fill(Color.yellow).frame(width: size.width, height: 1).position(x: size.width / 2, y: y * size.height) }
            }
            .contentShape(Rectangle())
        }
        .aspectRatio(aspect, contentMode: .fit)
    }
    private func safeArea(_ ratio: Double, _ size: CGSize, _ label: String) -> some View {
        let canvasRatio = size.width / max(1, size.height)
        let w = ratio < canvasRatio ? size.height * ratio : size.width, h = w / ratio
        return RoundedRectangle(cornerRadius: 4).stroke(Color.white.opacity(0.25), style: StrokeStyle(lineWidth: 1, dash: [6, 5]))
            .frame(width: w, height: h).overlay(alignment: .topLeading) { Text(label).font(.caption2).foregroundStyle(.white.opacity(0.4)).padding(4) }
            .position(x: size.width / 2, y: size.height / 2)
    }
    private func layerBox(_ layer: (String, String, String, Color), _ size: CGSize) -> some View {
        let r = rect(layer.0), isSelected = selected == layer.0, shown = visible(layer.0)
        let frame = CGRect(x: r.minX * size.width, y: r.minY * size.height, width: r.width * size.width, height: r.height * size.height)
        return ZStack(alignment: .bottomTrailing) {
            RoundedRectangle(cornerRadius: 8).fill(layer.3.opacity(shown ? 0.25 : 0.06))
                .overlay(RoundedRectangle(cornerRadius: 8).stroke(layer.3, style: StrokeStyle(lineWidth: isSelected ? 3 : 1.5, dash: shown ? [] : [5, 4])))
                .overlay { Label(layer.1, systemImage: layer.2).font(.caption.bold()).foregroundStyle(.white).opacity(shown ? 1 : 0.5) }
            if isSelected && !locked.contains(layer.0) {
                Image(systemName: "arrow.up.left.and.arrow.down.right").font(.caption.bold()).foregroundStyle(.white)
                    .frame(width: 30, height: 30).background(layer.3, in: Circle()).offset(x: 10, y: 10)
                    .gesture(drag(layer.0, size, resize: true))
            }
        }
        .frame(width: max(14, frame.width), height: max(14, frame.height))
        .position(x: frame.midX, y: frame.midY)
        .onTapGesture { selected = layer.0 }
        .gesture(locked.contains(layer.0) ? nil : drag(layer.0, size, resize: false))
    }
    // Ajuste magnético: bordes y centro de la pantalla (a menos de un 2 %), con la guía amarilla a la vista.
    private func snap(_ value: CGFloat, _ targets: [CGFloat]) -> (CGFloat, CGFloat?) {
        for t in targets where abs(value - t) < 0.02 { return (t, t) }
        return (value, nil)
    }
    private func drag(_ key: String, _ size: CGSize, resize: Bool) -> some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { value in
                selected = key
                let base = start[key] ?? rect(key); start[key] = base
                var r = base
                let dx = value.translation.width / size.width, dy = value.translation.height / size.height
                if resize {
                    var factor = max(0.08, (base.width + dx) / max(0.01, base.width))
                    let (right, gx) = snap(base.minX + base.width * factor, [0.5, 1])
                    if gx != nil { factor = (right - base.minX) / base.width }
                    r.size = CGSize(width: base.width * factor, height: base.height * factor)
                    guides = (gx, nil)
                } else {
                    let (cx, gx) = snap(base.midX + dx, [0.5]), (cy, gy) = snap(base.midY + dy, [0.5])
                    var x = cx - base.width / 2, y = cy - base.height / 2
                    var ex: CGFloat? = gx, ey: CGFloat? = gy
                    if gx == nil { let (l, g1) = snap(x, [0]); let (rr, g2) = snap(x + base.width, [1]); if g1 != nil { x = l; ex = 0 } else if g2 != nil { x = rr - base.width; ex = 1 } }
                    if gy == nil { let (t, g1) = snap(y, [0]); let (b, g2) = snap(y + base.height, [1]); if g1 != nil { y = t; ey = 0 } else if g2 != nil { y = b - base.height; ey = 1 } }
                    r.origin = CGPoint(x: x, y: y)
                    guides = (ex, ey)
                }
                send(key, r)
            }
            .onEnded { _ in start[key] = nil; guides = (nil, nil); DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { live[key] = nil } }
    }

    // Capas e inspector.
    private var sidebar: some View {
        let st = store.state
        return ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text(format == "16:9" ? "Estás viendo la imagen que se emite, se comparte y recibe la cámara «Traigger»." : format == "9:16" ? "Vista vertical (para directos en vertical)." : "Vista con la forma de esta pantalla.")
                    .font(.caption).foregroundStyle(.secondary)
                Text("Capas").font(.headline)
                VStack(spacing: 6) {
                    ForEach(layers, id: \.0) { layer in
                        HStack(spacing: 10) {
                            Image(systemName: layer.2).foregroundStyle(layer.3).frame(width: 24)
                            Text(layer.1).fontWeight(selected == layer.0 ? .semibold : .regular)
                            Spacer()
                            Button { if locked.contains(layer.0) { locked.remove(layer.0) } else { locked.insert(layer.0) } } label: {
                                Image(systemName: locked.contains(layer.0) ? "lock.fill" : "lock.open").foregroundStyle(.secondary)
                            }.buttonStyle(.plain).help("Bloquear")
                            Button { setVisible(layer.0, !visible(layer.0)) } label: {
                                Image(systemName: visible(layer.0) ? "eye" : "eye.slash").foregroundStyle(visible(layer.0) ? .primary : .secondary)
                            }.buttonStyle(.plain).help("Mostrar u ocultar")
                        }
                        .padding(10)
                        .background(selected == layer.0 ? layer.3.opacity(0.18) : Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .contentShape(Rectangle())
                        .onTapGesture { selected = layer.0 }
                    }
                }
                Toggle("Capturadora delante del vídeo", isOn: Binding(get: { st.captureFront ?? false }, set: { store.send("capture.front", b: $0) }))

                Divider()
                Text(layers.first { $0.0 == selected }?.1 ?? "").font(.headline)
                let r = rect(selected)
                HStack {
                    quick("Centrar", "plus.viewfinder") { var n = r; n.origin = CGPoint(x: 0.5 - r.width / 2, y: 0.5 - r.height / 2); send(selected, n) }
                    quick("Llenar", "arrow.up.left.and.arrow.down.right") { send(selected, CGRect(x: 0, y: 0, width: 1, height: 1)) }
                }
                HStack {
                    quick("Izquierda", "rectangle.lefthalf.filled") { send(selected, CGRect(x: 0, y: 0, width: 0.5, height: 1)) }
                    quick("Derecha", "rectangle.righthalf.filled") { send(selected, CGRect(x: 0.5, y: 0, width: 0.5, height: 1)) }
                }
                if selected == "avatar" {
                    HStack {
                        quick("Esquina", "rectangle.inset.bottomright.filled") { send("avatar", CGRect(x: 0.72, y: 0.55, width: 0.26, height: 0.43)) }
                        quick("Abajo", "rectangle.bottomhalf.filled") { send("avatar", CGRect(x: 0.3, y: 0.45, width: 0.4, height: 0.55)) }
                    }
                }
                field("Horizontal", r.minX, -0.5...1) { var n = r; n.origin.x = $0; send(selected, n) }
                field("Vertical", r.minY, -0.5...1) { var n = r; n.origin.y = $0; send(selected, n) }
                field("Tamaño", r.width, 0.05...1.5) { value in
                    let k = value / max(0.01, r.width); send(selected, CGRect(x: r.minX, y: r.minY, width: r.width * k, height: r.height * k))
                }
                if selected == "movie" { field("Opacidad", CGFloat(st.movieOpacity ?? 1), 0.2...1) { store.send("movie.opacity", f: Double($0)) } }
                if selected == "capture" { field("Opacidad", CGFloat(st.captureOpacity ?? 1), 0...1) { store.send("capture.opacity", f: Double($0)) } }

                Divider()
                Text("Guardar como escena").font(.headline)
                HStack {
                    TextField("Nombre", text: $sceneName).textFieldStyle(.roundedBorder)
                    Button("Guardar") { store.send("scene.save", s: sceneName); sceneName = "" }
                        .buttonStyle(.borderedProminent).disabled(sceneName.trimmingCharacters(in: .whitespaces).isEmpty)
                }
                Text("La encontrarás en Directo → Escenas, para cambiar a ella con un toque.").font(.caption).foregroundStyle(.secondary)
            }
            .padding(20)
        }
    }
    private func quick(_ title: String, _ icon: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) { Label(title, systemImage: icon).frame(maxWidth: .infinity) }.buttonStyle(.bordered).disabled(locked.contains(selected))
    }
    private func field(_ title: String, _ value: CGFloat, _ range: ClosedRange<CGFloat>, _ set: @escaping (CGFloat) -> Void) -> some View {
        HStack {
            Text(title).frame(width: 90, alignment: .leading)
            Slider(value: Binding(get: { min(max(value, range.lowerBound), range.upperBound) }, set: set), in: range).disabled(locked.contains(selected))
            Text("\(Int((value * 100).rounded())) %").monospacedDigit().font(.caption).frame(width: 50, alignment: .trailing)
        }
    }
}

// MARK: - Personajes (paquetes de personaje)
// Traigger y los personajes que hayas importado. Cambiar de personaje reinicia el estudio con él.
struct CharactersSection: View {
    @ObservedObject var store: TraiggerStore
    @State private var importing = false
    @State private var confirm: TraiggerCharacter?
    @State private var confirmOutfit: TraiggerOutfit?
    var body: some View {
        let st = store.state, current = st.character ?? ""
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Personaje").font(.headline)
                Spacer()
                Button { importing = true } label: { Label("Importar…", systemImage: "square.and.arrow.down") }
            }
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 12) {
                    ForEach(st.characters ?? [], id: \.self) { character in
                        let active = (character.path ?? "") == current
                        Button { if !active { confirm = character } } label: {
                            VStack(spacing: 6) {
                                preview(character).frame(width: 96, height: 110)
                                    .background(Color.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                                    .overlay(RoundedRectangle(cornerRadius: 14).stroke(active ? Color.accentColor : .clear, lineWidth: 3))
                                Text(character.name ?? "").font(.caption.bold()).lineLimit(1)
                                Text(active ? "En uso" : (character.author ?? "")).font(.caption2).foregroundStyle(active ? Color.accentColor : .secondary).lineLimit(1)
                            }
                            .frame(width: 104)
                        }
                        .buttonStyle(.plain)
                        .contextMenu {
                            if character.builtIn != true { Button("Borrar", role: .destructive) { store.send("character.delete", s: character.path ?? "") } }
                            if let license = character.license, !license.isEmpty { Text("Licencia: " + license) }
                        }
                    }
                }
            }
            // Outfits del personaje en uso: la misma cara, pelo, orejas y cola con otra ropa (y sus opciones).
            if let outfits = st.outfits, outfits.count > 1 {
                Text("Outfit").font(.headline).padding(.top, 4)
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 12) {
                        ForEach(outfits, id: \.self) { outfit in
                            let active = (outfit.id ?? "") == (st.outfit ?? "")
                            Button { if !active { confirmOutfit = outfit } } label: {
                                VStack(spacing: 6) {
                                    outfitPreview(outfit).frame(width: 84, height: 96)
                                        .background(Color.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                                        .overlay(RoundedRectangle(cornerRadius: 14).stroke(active ? Color.accentColor : .clear, lineWidth: 3))
                                    Text(outfit.name ?? "").font(.caption.bold()).lineLimit(1)
                                    if active { Text("Puesto").font(.caption2).foregroundStyle(Color.accentColor) }
                                }
                                .frame(width: 92)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
                ForEach(st.outfitOptions ?? [], id: \.self) { option in
                    Toggle(option.label ?? "", isOn: Binding(get: { option.on ?? false }, set: { store.send("outfit.option", b: $0, s: option.id ?? "") }))
                }
                if !(st.outfitOptions ?? []).isEmpty { Text("Cambiar el outfit o sus opciones reinicia el estudio un momento.").font(.caption2).foregroundStyle(.secondary) }
            }
            if let message = st.characterMessage, !message.isEmpty { Text(message).font(.caption).foregroundStyle(.secondary) }
            Text("Importa un paquete de personaje (carpeta o .zip/.traigger) hecho por ti o por otro creador. Cómo crear el tuyo: docs/PAQUETES_DE_PERSONAJE.md en el repositorio.")
                .font(.caption).foregroundStyle(.secondary)
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.folder, .zip, .data]) { result in
            guard case .success(let url) = result, let copy = TraiggerMediaStore.copy(url, folder: "Importar") else { return }
            store.send("character.import", s: copy)
        }
        .alert("¿Cambiar a «\(confirm?.name ?? "")»?", isPresented: Binding(get: { confirm != nil }, set: { if !$0 { confirm = nil } })) {
            Button("Cambiar") { store.send("character.load", s: confirm?.path ?? ""); confirm = nil }
            Button("Cancelar", role: .cancel) { confirm = nil }
        } message: { Text("El estudio se reinicia con el nuevo personaje (la película o el directo en curso se detienen).") }
        .alert("¿Ponerse el outfit «\(confirmOutfit?.name ?? "")»?", isPresented: Binding(get: { confirmOutfit != nil }, set: { if !$0 { confirmOutfit = nil } })) {
            Button("Cambiar") { store.send("outfit.select", s: confirmOutfit?.id ?? ""); confirmOutfit = nil }
            Button("Cancelar", role: .cancel) { confirmOutfit = nil }
        } message: { Text("El estudio se reinicia un momento con la ropa nueva.") }
    }
    @ViewBuilder private func outfitPreview(_ outfit: TraiggerOutfit) -> some View {
        #if os(macOS)
        if let path = outfit.preview, !path.isEmpty, let image = NSImage(contentsOfFile: path) { Image(nsImage: image).resizable().scaledToFit().padding(6) }
        else { Image(systemName: "tshirt.fill").font(.largeTitle).foregroundStyle(.secondary) }
        #else
        if let path = outfit.preview, !path.isEmpty, let image = UIImage(contentsOfFile: path) { Image(uiImage: image).resizable().scaledToFit().padding(6) }
        else { Image(systemName: "tshirt.fill").font(.largeTitle).foregroundStyle(.secondary) }
        #endif
    }
    @ViewBuilder private func preview(_ character: TraiggerCharacter) -> some View {
        #if os(macOS)
        if let path = character.preview, !path.isEmpty, let image = NSImage(contentsOfFile: path) { Image(nsImage: image).resizable().scaledToFit().padding(6) }
        else { Image(systemName: character.builtIn == true ? "pawprint.fill" : "person.crop.square").font(.largeTitle).foregroundStyle(.secondary) }
        #else
        if let path = character.preview, !path.isEmpty, let image = UIImage(contentsOfFile: path) { Image(uiImage: image).resizable().scaledToFit().padding(6) }
        else { Image(systemName: character.builtIn == true ? "pawprint.fill" : "person.crop.square").font(.largeTitle).foregroundStyle(.secondary) }
        #endif
    }
}
