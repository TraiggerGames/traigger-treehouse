import SwiftUI
import UniformTypeIdentifiers
import ImageIO
import PhotosUI

// Interfaz de Traigger en SwiftUI. Se ve igual en iPhone/iPad, Vision Pro y en Traigger Control (Mac).
// Las secciones están siempre a la vista: columna de iconos a la izquierda (iPad, Mac, Vision Pro) o fila arriba
// (iPhone), sin desplazarse para cambiar de una a otra.
struct TraiggerPanel: View {
    @ObservedObject var store: TraiggerStore
    @StateObject private var library = TraiggerLibrary()
    @AppStorage("traigger.ui.tab") private var tab = 0
    #if os(iOS)
    @Environment(\.horizontalSizeClass) private var sizeClass
    private var compact: Bool { sizeClass == .compact }
    #else
    private let compact = false
    #endif
    // Vídeo, capturadora y navegador están juntos en «Fuentes», cada uno con su propia pantalla.
    private let sections: [(String, String)] = [("Inicio", "face.smiling"), ("Fuentes", "square.stack.3d.up"), ("Ajustar", "slider.horizontal.3"), ("Seguimiento", "camera.viewfinder"), ("Voz", "waveform"), ("Directo", "dot.radiowaves.left.and.right"), ("Ajustes", "gearshape")]

    var body: some View {
        Group {
            if compact {
                VStack(spacing: 0) { bar(vertical: false).padding(.horizontal, 8).padding(.vertical, 6); Divider(); content }
            } else {
                HStack(spacing: 0) { bar(vertical: true).frame(width: 84).padding(.vertical, 10); Divider(); content }
            }
        }
        .overlay(alignment: .top) {
            if !store.connected {
                Label("Esperando a Traigger…", systemImage: "antenna.radiowaves.left.and.right")
                    .font(.callout).padding(.horizontal, 14).padding(.vertical, 8).background(.ultraThinMaterial, in: Capsule()).padding(.top, 8)
            }
        }
    }

    @ViewBuilder private func bar(vertical: Bool) -> some View {
        if vertical { VStack(spacing: 6) { barButtons; Spacer(minLength: 0) }.padding(.horizontal, 8) }
        else { HStack(spacing: 4) { barButtons } }
    }

    @ViewBuilder private var barButtons: some View {
            ForEach(Array(sections.enumerated()), id: \.offset) { index, section in
                Button { tab = index } label: {
                    VStack(spacing: 3) { Image(systemName: section.1).font(.title3); Text(section.0).font(.caption2).lineLimit(1).minimumScaleFactor(0.7) }
                        .frame(maxWidth: .infinity).padding(.vertical, 8)
                        .background(tab == index ? Color.accentColor.opacity(0.2) : .clear, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                        .foregroundStyle(tab == index ? Color.accentColor : Color.primary)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
    }

    @ViewBuilder private var content: some View {
        switch tab {
        case 1: SourcesTab(store: store, library: library)
        case 2: ControlsTab(store: store)
        case 3: TrackingTab(store: store)
        case 4: VoiceTab(store: store)
        case 5: ScenesTab(store: store)
        case 6: SettingsTab(store: store)
        default: HomeTab(store: store)
        }
    }
}

// MARK: - Piezas comunes

private struct Section<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.headline).foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 12) { content }
                .padding(12).frame(maxWidth: .infinity, alignment: .leading)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        }
    }
}

struct Page<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View { ScrollView { VStack(alignment: .leading, spacing: 16) { content }.padding(14) } }
}

struct StatusRow: View {
    let icon: String, title: String, value: String?
    var body: some View {
        if let value, !value.isEmpty {
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: icon).frame(width: 22).foregroundStyle(.tint)
                VStack(alignment: .leading, spacing: 2) { Text(title).font(.caption).foregroundStyle(.secondary); Text(value).font(.callout) }
            }
        }
    }
}

func binding(_ value: Bool?, _ set: @escaping (Bool) -> Void) -> Binding<Bool> { Binding(get: { value ?? false }, set: set) }
private func clock(_ seconds: Double) -> String {
    let s = Int(max(0, seconds)); return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60) : String(format: "%d:%02d", s / 60, s % 60)
}

// MARK: - Inicio

private struct HomeTab: View {
    @ObservedObject var store: TraiggerStore
    private let icons = ["face.smiling", "sun.max", "cloud.rain", "flame", "exclamationmark", "questionmark", "hand.raised.slash", "hand.wave"]
    var body: some View {
        let st = store.state
        Page {
            CharactersSection(store: store)
            Section(title: "Expresión") {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 84), spacing: 8)], spacing: 8) {
                    ForEach(Array((st.expressions ?? []).enumerated()), id: \.offset) { index, name in
                        Button { store.send("expression", i: index) } label: {
                            VStack(spacing: 4) { Image(systemName: icons[min(index, icons.count - 1)]).font(.title3); Text(name).font(.caption).lineLimit(1).minimumScaleFactor(0.8) }
                                .frame(maxWidth: .infinity, minHeight: 52)
                        }
                        .buttonStyle(.bordered).tint(index == st.expression ? .accentColor : .secondary)
                    }
                }
                if let emotion = st.emotion, !emotion.isEmpty { Label("Por la voz: \(emotion)", systemImage: "waveform").font(.callout).foregroundStyle(.secondary) }
            }
            Section(title: "Encuadre") {
                HStack {
                    Button { store.send("control", s: "do:frame.face") } label: { Label("Cara · videollamada", systemImage: "person.crop.square") }.buttonStyle(.bordered)
                    Button { store.send("control", s: "do:frame.body") } label: { Label("Cuerpo entero", systemImage: "figure.stand") }.buttonStyle(.bordered)
                }
            }
            Section(title: "Traigger") {
                Picker("Posición", selection: Binding(get: { st.layout ?? "center" }, set: { store.send("layout", s: $0) })) {
                    Text("Centro").tag("center"); Text("Junto a la película").tag("side"); Text("Esquina").tag("corner")
                }
                .pickerStyle(.segmented)
                Toggle("Ocultar a Traigger", isOn: binding(st.avatarHidden) { store.send("avatarHidden", b: $0) })
                Toggle("Ponerse junto a la película al reproducir", isOn: binding(st.cinemaLayout) { store.send("cinemaLayout", b: $0) })
                HStack {
                    Button("Pose neutra") { store.send("neutralPose") }
                    Spacer()
                    Button("Restablecer tamaño") { store.send("resetView") }
                }
            }
            Section(title: "Movimiento") {
                Toggle("Movimiento orgánico", isOn: binding(st.demo) { store.send("demo", b: $0) })
                Toggle("Animación natural con la voz", isOn: binding(st.naturalSpeech) { store.send("naturalSpeech", b: $0) })
                Toggle("Seguimiento (cara, sensores)", isOn: binding(st.tracking) { store.send("tracking", b: $0) })
            }
            Section(title: "Estado") {
                StatusRow(icon: "mouth", title: "Boca", value: st.mouth)
                StatusRow(icon: "text.bubble", title: "Frases", value: st.phrases)
                StatusRow(icon: "quote.bubble", title: "Última frase", value: st.heard)
                StatusRow(icon: "faceid", title: "Cara", value: st.face)
                StatusRow(icon: "visionpro", title: "Vision Pro", value: st.visionSensors)
            }
        }
    }
}

// MARK: - Cine

struct CinemaTab: View {
    @ObservedObject var store: TraiggerStore
    @ObservedObject var library: TraiggerLibrary
    var browseOnly = false   // en la pantalla de Vídeo, lo que se reproduce va en «Opciones»
    @State private var editing: TraiggerServer?
    @State private var password = ""
    @State private var current: TraiggerServer?

    var body: some View {
        let st = store.state
        Page {
            if st.movieAvailable == false {
                Text("El cine no está disponible en este dispositivo.").foregroundStyle(.secondary)
            }
            if st.moviePlaying == true && !browseOnly { NowPlaying(store: store) }
            if let server = current { browser(server, st) } else { serverList(st) }
        }
        .sheet(item: $editing) { server in ServerEditorView(server: server, password: $password) { saved in
            if let index = library.servers.firstIndex(where: { $0.id == saved.id }) { library.servers[index] = saved } else { library.servers.append(saved) }
            TraiggerKeychain.save(password, for: saved); editing = nil
        } cancel: { editing = nil } }
        .onChange(of: st.moviePlaying ?? false) { playing in
            if !playing, let title = st.movieTitle, let url = lastURL { library.remember(title: title, url: url, position: st.movieTime ?? 0) }
        }
    }

    @State private var lastURL: String?

    private func play(_ url: String, at position: Double? = nil) {
        lastURL = url; store.send("movie.open", s: url)
        if let position { DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { store.send("movie.seek", f: position) } }
    }
    private func edit(_ server: TraiggerServer?) {
        password = server.flatMap { TraiggerKeychain.password(for: $0) } ?? ""; editing = server ?? TraiggerServer()
    }

    @ViewBuilder private func serverList(_ st: TraiggerState) -> some View {
        MediaHome(store: store, library: library, openServer: { server in current = server; store.send("movie.browse", s: server.url()) },
                  editServer: edit, play: { play($0, at: $1) })
    }

    @ViewBuilder private func browser(_ server: TraiggerServer, _ st: TraiggerState) -> some View {
        NetworkBrowser(store: store, server: server, back: { current = nil }, edit: { edit(server) }, play: { play($0) })
    }
}

struct NowPlaying: View {
    @ObservedObject var store: TraiggerStore
    @State private var scrubbing: Double?
    var body: some View {
        let st = store.state
        Section(title: "Reproduciendo") {
            Text(st.movieTitle ?? "").font(.title3.bold()).lineLimit(2)
            let length = max(st.movieLength ?? 0, 1)
            Slider(value: Binding(get: { scrubbing ?? st.movieTime ?? 0 }, set: { scrubbing = $0 }), in: 0...length) { editing in
                if !editing, let value = scrubbing { store.send("movie.seek", f: value); scrubbing = nil }
            }
            HStack { Text(clock(scrubbing ?? st.movieTime ?? 0)); Spacer(); Text("-" + clock(length - (st.movieTime ?? 0))) }.font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            HStack(spacing: 28) {
                Spacer()
                Button { store.send("movie.skip", f: -30) } label: { Image(systemName: "gobackward.30") }
                Button { store.send("movie.pause", b: !(st.moviePaused ?? false)) } label: { Image(systemName: st.moviePaused == true ? "play.fill" : "pause.fill").font(.title) }
                Button { store.send("movie.skip", f: 30) } label: { Image(systemName: "goforward.30") }
                Button { store.send("movie.stop") } label: { Image(systemName: "stop.fill") }
                Spacer()
            }
            .font(.title2).buttonStyle(.borderless)
            HStack { Image(systemName: "speaker.fill"); Slider(value: Binding(get: { Double(st.movieVolume ?? 80) }, set: { store.send("movie.volume", i: Int($0)) }), in: 0...100); Image(systemName: "speaker.wave.3.fill") }
            TrackPicker(title: "Idioma del audio", icon: "speaker.wave.2", tracks: st.audioTracks ?? [], allowNone: false) { store.send("movie.audio", s: $0) }
            TrackPicker(title: "Subtítulos", icon: "captions.bubble", tracks: st.subtitleTracks ?? [], allowNone: true) { store.send("movie.subtitles", s: $0) }
            let subtitleFiles = (st.movieItems ?? []).filter { TraiggerFiles.isSubtitle($0.name ?? "") }
            if !subtitleFiles.isEmpty {
                Menu { ForEach(subtitleFiles, id: \.self) { file in Button(file.name ?? "") { store.send("movie.addSubtitle", s: file.url ?? "") } } }
                label: { Label("Cargar subtítulos de la carpeta", systemImage: "doc.text") }
            }
            GeneratedSubtitles(store: store)
            if st.platform == "IPhonePlayer" {
                // iPad/iPhone: ventana flotante que se mueve arrastrando y se escala pellizcando o por la esquina.
                Toggle(isOn: binding(st.cinemaWindow) { store.send("cinema.window", b: $0) }) { Label("En una ventana flotante (pellizca para escalar)", systemImage: "rectangle.on.rectangle") }
            }
            if st.platform == "VisionOS" {
                // Vision Pro: la película fuera de Unity, en su propia ventana o en una pantalla de cine inmersiva.
                Toggle(isOn: binding(st.cinemaWindow) { store.send("cinema.window", b: $0) }) { Label("En su propia ventana", systemImage: "rectangle.on.rectangle") }
                Toggle(isOn: binding(st.cinemaImmersive) { store.send("cinema.immersive", b: $0) }) { Label("Pantalla de cine inmersiva", systemImage: "visionpro") }
                HStack { Image(systemName: "arrow.down.right.and.arrow.up.left"); Slider(value: Binding(get: { st.cinemaScale ?? 1 }, set: { store.send("cinema.scale", f: $0) }), in: 0.5...4); Image(systemName: "arrow.up.left.and.arrow.down.right") }
            }
            Toggle("De fondo detrás de Traigger", isOn: binding(st.movieBackground) { store.send("movie.background", b: $0) })
            HStack { Text("Tamaño"); Slider(value: Binding(get: { st.movieScale ?? 1 }, set: { store.send("movie.scale", f: $0) }), in: 0.2...2) }
            HStack { Text("Opacidad"); Slider(value: Binding(get: { st.movieOpacity ?? 1 }, set: { store.send("movie.opacity", f: $0) }), in: 0.2...1) }
        }
    }
}

// Subtítulos que genera Apple a partir del audio (y su traducción, salvo en Vision Pro).
struct GeneratedSubtitles: View {
    @ObservedObject var store: TraiggerStore
    private let languages: [(String, String)] = [("", "Idioma del audio"), ("es-ES", "Español"), ("en-US", "English"), ("fr-FR", "Français"), ("de-DE", "Deutsch"),
                                                 ("it-IT", "Italiano"), ("pt-PT", "Português"), ("ja-JP", "日本語"), ("ko-KR", "한국어"), ("zh-CN", "中文")]
    private let targets: [(String, String)] = [("", "No traducir"), ("es", "Español"), ("en", "English"), ("fr", "Français"), ("de", "Deutsch"),
                                               ("it", "Italiano"), ("pt", "Português"), ("ja", "日本語"), ("ko", "한국어"), ("zh", "中文")]
    var body: some View {
        let st = store.state
        VStack(alignment: .leading, spacing: 10) {
            Toggle(isOn: binding(st.generatedSubtitles) { store.send("movie.generated", b: $0) }) {
                Label("Subtítulos generados por Apple", systemImage: "sparkles")
            }
            if st.generatedSubtitles == true {
                Picker("La película está en", selection: Binding(get: { st.subtitleLanguage ?? "" }, set: { store.send("movie.generatedLanguage", s: $0) })) {
                    ForEach(languages, id: \.0) { Text($0.1).tag($0.0) }
                }
                if st.platform != "VisionOS" {
                    Picker("Traducir a", selection: Binding(get: { st.translateTo ?? "" }, set: { store.send("movie.translate", s: $0) })) {
                        ForEach(targets, id: \.0) { Text($0.1).tag($0.0) }
                    }
                }
                if let status = st.generatedStatus, !status.isEmpty { Text(status).font(.caption).foregroundStyle(.secondary) }
            }
        }
    }
}

struct TrackPicker: View {
    let title: String, icon: String, tracks: [TraiggerTrack], allowNone: Bool
    let select: (String) -> Void
    var body: some View {
        if !tracks.isEmpty || allowNone {
            let current = tracks.first { $0.selected == true }
            Menu {
                if allowNone { Button("Ninguno") { select("") } }
                ForEach(tracks, id: \.self) { track in
                    Button { select(track.id ?? "") } label: {
                        if track.selected == true { Label(name(track), systemImage: "checkmark") } else { Text(name(track)) }
                    }
                }
            } label: {
                HStack { Label(title, systemImage: icon); Spacer(); Text(current.map(name) ?? "Ninguno").foregroundStyle(.secondary) }
            }
        }
    }
    private func name(_ track: TraiggerTrack) -> String {
        let label = track.name ?? ""
        if let code = track.language, !code.isEmpty, let language = Locale.current.localizedString(forLanguageCode: code), !label.localizedCaseInsensitiveContains(language) {
            return label.isEmpty ? language.capitalized : "\(language.capitalized) · \(label)"
        }
        return label.isEmpty ? "Pista" : label
    }
}

// MARK: - Web

struct WebTab: View {
    @ObservedObject var store: TraiggerStore
    @ObservedObject private var browser = TraiggerBrowser.shared
    @State private var address = ""
    var body: some View {
        let st = store.state
        Page {
            Section(title: "Navegador") {
                HStack {
                    TextField("Buscar o escribir dirección", text: $address).textFieldStyle(.roundedBorder).onSubmit { store.send("web.open", s: address) }
                        #if os(iOS) || os(visionOS)
                        .textInputAutocapitalization(.never).autocorrectionDisabled()
                        #endif
                    Button("Abrir") { store.send("web.open", s: address.isEmpty ? (browser.address.isEmpty ? "https://duckduckgo.com" : browser.address) : address) }.buttonStyle(.borderedProminent)
                }
                if let url = st.webUrl, !url.isEmpty { StatusRow(icon: "globe", title: st.webTitle?.isEmpty == false ? st.webTitle! : "Página", value: url) }
                if st.platform == "VisionOS" {
                    Toggle(isOn: binding(st.webImmersive) { store.send("web.immersive", b: $0) }) { Label("Navegar en pantalla grande inmersiva", systemImage: "visionpro") }
                }
            }
            Section(title: "Favoritos") {
                ForEach(browser.bookmarks, id: \.self) { mark in
                    Button { store.send("web.open", s: mark) } label: { Label(mark, systemImage: "star").lineLimit(1).frame(maxWidth: .infinity, alignment: .leading) }
                }
            }
        }
        .onAppear { address = browser.address }
    }
}

// MARK: - Ajustar (controles que describe Unity)

private struct ControlsTab: View {
    @ObservedObject var store: TraiggerStore
    @State private var open: Set<String> = ["Encuadre", "Fondo"]
    var body: some View {
        let controls = store.state.controls ?? []
        let groups = controls.reduce(into: [String]()) { if !$0.contains($1.group) { $0.append($1.group) } }
        Page {
            ForEach(groups, id: \.self) { group in
                let items = controls.filter { $0.group == group }
                VStack(alignment: .leading, spacing: 10) {
                    Button { if open.contains(group) { open.remove(group) } else { open.insert(group) } } label: {
                        HStack { Text(group).font(.headline); Spacer(); Image(systemName: open.contains(group) ? "chevron.up" : "chevron.down") }.contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    if open.contains(group) {
                        let buttons = items.filter { $0.kind == "button" }
                        if !buttons.isEmpty {
                            LazyVGrid(columns: [GridItem(.adaptive(minimum: 120), spacing: 8)], spacing: 8) {
                                ForEach(buttons, id: \.key) { control in Button(control.label) { store.send("control", s: control.key) }.buttonStyle(.bordered).frame(maxWidth: .infinity) }
                            }
                        }
                        ForEach(items.filter { $0.kind != "button" }, id: \.key) { control in ControlRow(store: store, control: control) }
                    }
                }
                .padding(12).frame(maxWidth: .infinity, alignment: .leading)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            }
            if controls.isEmpty { Text("Esperando los controles de Traigger…").foregroundStyle(.secondary) }
        }
    }
}

private struct ControlRow: View {
    @ObservedObject var store: TraiggerStore
    let control: TraiggerControl
    @State private var editing: Double?
    var body: some View {
        if control.kind == "toggle" {
            Toggle(control.label, isOn: Binding(get: { control.value > 0.5 }, set: { store.send("control", f: $0 ? 1 : 0, s: control.key) }))
        } else {
            VStack(alignment: .leading, spacing: 2) {
                HStack { Text(control.label).font(.callout); Spacer(); Text(String(format: "%.2f", editing ?? control.value)).font(.caption.monospacedDigit()).foregroundStyle(.secondary) }
                Slider(value: Binding(get: { editing ?? control.value }, set: { editing = $0; store.send("control", f: $0, s: control.key) }), in: control.min...max(control.max, control.min + 0.0001)) { active in if !active { editing = nil } }
            }
        }
    }
}

// MARK: - Seguimiento, Voz, Ajustes

private struct TrackingTab: View {
    @ObservedObject var store: TraiggerStore
    var body: some View {
        let st = store.state
        Page {
            if let cameras = st.cameras, !cameras.isEmpty {
                Section(title: "Cámara") {
                    ForEach(cameras, id: \.self) { camera in
                        Button { store.send("cameraMode", i: camera.id) } label: {
                            HStack { Text(camera.name); Spacer(); if camera.id == st.cameraMode { Image(systemName: "checkmark").foregroundStyle(.tint) } }.contentShape(Rectangle())
                        }
                    }
                    if (st.cameraMode ?? 0) > 0 {
                        StatusRow(icon: "figure.stand", title: "Cuerpo", value: st.body)
                        Toggle("Invertir piernas", isOn: binding(st.invertLegs) { store.send("invertLegs", b: $0) })
                        Toggle("Ahorrar batería (solo sensor para Vision Pro)", isOn: binding(st.saveBattery) { store.send("saveBattery", b: $0) })
                    }
                }
            }
            #if os(iOS)
            FaceRelaySection()
            #endif
            if st.visionAvailable == true {
                Section(title: "Vision Pro") {
                    Toggle("Sensores de cabeza y manos (oculta otras apps)", isOn: binding(st.visionImmersive) { store.send("sensors", b: $0) })
                    Button("Recentrar cabeza") { store.send("recenter") }
                    StatusRow(icon: "visionpro", title: "Estado", value: st.visionSensors)
                }
            }
            Section(title: "Estado") {
                StatusRow(icon: "faceid", title: "Cara", value: st.face)
                StatusRow(icon: "figure.stand", title: "Cuerpo", value: st.body)
            }
        }
    }
}

private struct VoiceTab: View {
    @ObservedObject var store: TraiggerStore
    var body: some View {
        let st = store.state
        Page {
            Section(title: "Emoción por la voz") {
                Toggle("Activada", isOn: binding(st.voiceEmotion) { store.send("voiceEmotion", b: $0) })
                HStack { Text("Intensidad"); Slider(value: Binding(get: { st.voiceEmotionStrength ?? 1 }, set: { store.send("voiceEmotionStrength", f: $0) }), in: 0...1.5) }
                StatusRow(icon: "waveform", title: "Entonación", value: st.intonation)
            }
            Section(title: "Idioma de las frases") {
                Picker("Idioma", selection: Binding(get: { st.speechLanguage ?? 0 }, set: { store.send("speechLanguage", i: $0) })) {
                    ForEach(Array((st.speechLanguages ?? []).enumerated()), id: \.offset) { index, name in Text(name).tag(index) }
                }
                .pickerStyle(.segmented)
                StatusRow(icon: "text.bubble", title: "Frases", value: st.phrases)
            }
            Section(title: "Tu voz") {
                Text(st.voiceStatus?.isEmpty == false ? st.voiceStatus! : "Pulsa una emoción y habla 5 segundos con ella. Empieza por Neutro.").font(.callout).foregroundStyle(.secondary)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 110), spacing: 10)], spacing: 10) {
                    ForEach(Array((st.voiceProfile ?? []).enumerated()), id: \.offset) { index, name in
                        let done = (st.voiceCalibrated ?? [])[safe: index] ?? false
                        Button { store.send("calibrate", i: index) } label: { Label(name, systemImage: done ? "checkmark.circle.fill" : "circle").frame(maxWidth: .infinity) }
                            .buttonStyle(.bordered)
                    }
                }
                Text(st.voiceUsesProfile == true ? "Usando tu perfil calibrado" : "Sin calibrar: reglas generales").font(.caption).foregroundStyle(.secondary)
                StatusRow(icon: "icloud", title: "Sincronización", value: st.voiceSync)
                Button("Borrar perfil de voz", role: .destructive) { store.send("resetVoiceProfile") }
            }
        }
    }
}

private struct SettingsTab: View {
    @ObservedObject var store: TraiggerStore
    @State private var debugPanel = false
    @State private var picking: String?
    var body: some View {
        let st = store.state
        Page {
            Section(title: "Fondo") {
                HStack {
                    Text("Imagen detrás de Traigger")
                    Spacer()
                    if st.backgroundImage?.isEmpty == false { Button("Quitar") { store.send("background.image", s: "") } }
                }
                MediaSourceButtons(kind: .image) { store.send("background.image", s: $0) }
                if let image = st.backgroundImage, !image.isEmpty { Text((image as NSString).lastPathComponent).font(.caption).foregroundStyle(.secondary) }
                if st.platform == "VisionOS" {
                    HStack {
                        Label("Imagen 360° / HDRI inmersiva", systemImage: "globe.europe.africa")
                        Spacer()
                        if st.backgroundSky?.isEmpty == false { Button("Quitar") { store.send("background.sky", s: "") } }
                    }
                    MediaSourceButtons(kind: .panorama) { store.send("background.sky", s: $0) }
                    Text("Panorámica equirectangular (.hdr, .exr, .jpg o .png). Te envuelve en el espacio inmersivo «Cine».").font(.caption).foregroundStyle(.secondary)
                }
            }
            .fileImporter(isPresented: Binding(get: { picking != nil }, set: { if !$0 { picking = nil } }), allowedContentTypes: [.image]) { result in
                guard let command = picking, case .success(let url) = result,
                      let path = command == "background.image" ? Self.keepAsImage(url) : Self.keep(url) else { return }
                store.send(command, s: path)
            }
            if st.platform == "VisionOS" || st.platform == "IPhonePlayer" { SpaceAndShareSection(vision: st.platform == "VisionOS") }
            if st.virtualCameraAvailable == true {
                Section(title: "Cámara virtual (FaceTime, Zoom, Meet…)") {
                    Toggle(st.platform == "IPhonePlayer" ? "Enviar al Mac como cámara «Traigger»" : "Enviar a la cámara «Traigger»", isOn: binding(st.virtualCamera) { store.send("virtualCamera", b: $0) })
                    StatusRow(icon: "video", title: "Estado", value: st.virtualCameraStatus)
                }
            }
            Section(title: "Interfaz") {
                Button { store.hidden = true } label: { Label("Ocultar la interfaz", systemImage: "eye.slash") }
                Text(hint).font(.caption).foregroundStyle(.secondary)
                if st.platform?.contains("Editor") == true {
                    Toggle("Panel de depuración de Unity (OnGUI)", isOn: $debugPanel).onChange(of: debugPanel) { store.send("debugPanel", b: $0) }
                }
            }
        }
    }
    // Copia la imagen elegida a la carpeta de la app (el permiso del selector es temporal) y devuelve su ruta.
    // Unity solo lee JPG y PNG: lo demás (HEIC de las fotos, TIFF, WebP…) se convierte a JPG al guardarlo.
    static func keepAsImage(_ url: URL) -> String? {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard let data = try? Data(contentsOf: url) else { return nil }
        return keepImage(data, name: url.deletingPathExtension().lastPathComponent)
    }
    static func keepImage(_ data: Data, name: String) -> String? {
        guard let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.appendingPathComponent("Fondos") else { return nil }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let isPNG = data.starts(with: [0x89, 0x50, 0x4E, 0x47]), isJPEG = data.starts(with: [0xFF, 0xD8, 0xFF])
        let destination = folder.appendingPathComponent(name + (isPNG ? ".png" : ".jpg"))
        try? FileManager.default.removeItem(at: destination)
        if isPNG || isJPEG { return (try? data.write(to: destination)) != nil ? destination.path : nil }
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, [kCGImageSourceShouldCacheImmediately: true] as CFDictionary),
              let output = CGImageDestinationCreateWithURL(destination as CFURL, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
        // Respeta la orientación de la foto (las del carrete suelen venir giradas).
        let orientation = (CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])?[kCGImagePropertyOrientation] ?? 1
        CGImageDestinationAddImage(output, image, [kCGImageDestinationLossyCompressionQuality: 0.92, kCGImagePropertyOrientation: orientation] as CFDictionary)
        return CGImageDestinationFinalize(output) ? destination.path : nil
    }
    static func keep(_ url: URL) -> String? {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.appendingPathComponent("Fondos") else { return nil }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let destination = folder.appendingPathComponent(url.lastPathComponent)
        try? FileManager.default.removeItem(at: destination)
        do { try FileManager.default.copyItem(at: url, to: destination) } catch { return url.path }
        return destination.path
    }

    private var hint: String {
        #if os(visionOS)
        "Para volver a verla, mantén el pellizco sobre Traigger."
        #elseif os(iOS)
        "Para volver a verla, toca dos veces con dos dedos en cualquier parte."
        #else
        "Para volver a verla, pulsa el icono de Traigger Control."
        #endif
    }
}

extension Array { subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil } }

// Entrada HDMI (capturadora USB, como en Orion): de fondo detrás de Traigger en el iPad o el Mac, con su sonido, y
// enviada por Wi-Fi a las Vision Pro, que la muestran en su propia ventana.
struct HDMISection: View {
    @ObservedObject var store: TraiggerStore
    @State private var device = ""
    @State private var audio = true
    var body: some View {
        let st = store.state
        Section(title: "Entrada HDMI") {
            if st.platform == "VisionOS" {
                Text("Conecta la capturadora al iPad o al Mac y activa allí «Enviar a Vision Pro»: la imagen aparece aquí en su propia ventana.")
                    .font(.caption).foregroundStyle(.secondary)
                HStack {
                    Button { store.send("capture.window", b: true) } label: { Label("Abrir la ventana", systemImage: "rectangle.on.rectangle") }
                    Button("Cerrar") { store.send("capture.window", b: false) }
                }
            } else if st.captureAvailable == true {
                let devices = st.captureDevices ?? []
                if devices.isEmpty {
                    Text("Conecta una capturadora HDMI por USB-C (UVC). En iPad hace falta iPadOS 17 o posterior.").font(.caption).foregroundStyle(.secondary)
                } else {
                    Picker("Capturadora", selection: $device) {
                        ForEach(devices, id: \.id) { Text($0.name).tag($0.id) }
                    }
                    .onAppear { if device.isEmpty { device = st.captureDevice?.isEmpty == false ? st.captureDevice! : devices[0].id } }
                    Toggle("Con sonido", isOn: $audio).disabled(st.captureOn == true)
                    Toggle(isOn: binding(st.captureOn) { $0 ? store.send("capture.start", b: audio, s: device) : store.send("capture.stop") }) {
                        Label("Capturar", systemImage: "cable.connector")
                    }
                    Toggle(isOn: binding(st.captureBackground) { store.send("capture.background", b: $0) }) { Label("De fondo detrás de Traigger", systemImage: "person.crop.rectangle") }
                    if st.captureBackground == true {
                        HStack { Text("Tamaño"); Slider(value: Binding(get: { st.captureScale ?? 1 }, set: { store.send("capture.scale", f: $0) }), in: 0.2...2) }
                    }
                    Toggle(isOn: binding(st.captureWindow) { store.send("capture.window", b: $0) }) { Label("Ver en una ventana (pellizca para escalar)", systemImage: "tv") }
                    if st.platform == "IPhonePlayer", audio { Text("Mientras suena la capturadora, la boca no sigue a tu voz (el iPad solo admite una entrada de sonido).").font(.caption).foregroundStyle(.secondary) }
                }
                Toggle(isOn: binding(st.captureToVision) { store.send("capture.vision", b: $0) }) { Label("Enviar a Vision Pro", systemImage: "visionpro") }
                if st.captureToVision == true { HDMIQuality(store: store) }
                ForceFaceIDToggle(store: store)
                StatusRow(icon: "cable.connector", title: "Estado", value: st.captureStatus)
            } else {
                Text("Sin entrada HDMI en este aparato.").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

// Elegir imágenes, panorámicas o vídeos desde la fototeca (Fotos…) o desde Archivos (iCloud Drive, el aparato, un
// disco USB…). Todo se copia a la carpeta de la app (los permisos de los selectores son temporales) y se devuelve su ruta.
enum TraiggerMediaKind { case image, panorama, video, imageOrVideo }
struct MediaSourceButtons: View {
    let kind: TraiggerMediaKind
    let picked: (String) -> Void
    @State private var files = false
    @State private var loading = false
    private var fileTypes: [UTType] {
        switch kind {
        case .image: [.image]
        case .panorama: [.image] + ["hdr", "exr"].compactMap { UTType(filenameExtension: $0) }
        case .video: [.movie, .video] + ["mkv", "avi", "webm"].compactMap { UTType(filenameExtension: $0) }
        case .imageOrVideo: [.image, .movie]
        }
    }
    var body: some View {
        HStack(spacing: 12) {
            if #available(iOS 16.0, macOS 13.0, *) { PhotoLibraryButton(kind: kind, loading: $loading, picked: picked) }
            Button { files = true } label: { Label("Archivos…", systemImage: "folder") }
            if loading { ProgressView().controlSize(.small) }
        }
        .fileImporter(isPresented: $files, allowedContentTypes: fileTypes) { result in
            guard case .success(let url) = result else { return }
            // Las imágenes se pasan a JPG/PNG (Unity no lee HEIC); panorámicas HDR/EXR y vídeos se copian tal cual.
            let isMovie = UTType(filenameExtension: url.pathExtension)?.conforms(to: .movie) ?? false
            let path = kind == .image || (kind == .imageOrVideo && !isMovie) || (kind == .panorama && !["hdr", "exr"].contains(url.pathExtension.lowercased()))
                ? SettingsTabImages.keepImage(url) : TraiggerMediaStore.copy(url, folder: kind == .video || isMovie ? "Videos" : "Fondos")
            if let path { picked(path) }
        }
    }
}
@available(iOS 16.0, macOS 13.0, *)
private struct PhotoLibraryButton: View {
    let kind: TraiggerMediaKind
    @Binding var loading: Bool
    let picked: (String) -> Void
    @State private var item: PhotosPickerItem?
    var body: some View {
        PhotosPicker(selection: $item, matching: kind == .video ? .videos : kind == .imageOrVideo ? .any(of: [.images, .videos]) : .images) { Label("Fotos…", systemImage: "photo.on.rectangle") }
            .onChange(of: item) { value in
                guard let value else { return }
                loading = true
                Task {
                    var path: String?
                    let isMovie = kind == .video || (kind == .imageOrVideo && value.supportedContentTypes.contains { $0.conforms(to: .movie) })
                    if isMovie {
                        if let movie = try? await value.loadTransferable(type: TraiggerPickedMovie.self) { path = movie.path }
                    } else if let data = try? await value.loadTransferable(type: Data.self) {
                        path = SettingsTabImages.keepImage(data, name: kind == .panorama ? "Panoramica" : "Foto")
                    }
                    await MainActor.run { loading = false; item = nil; if let path { picked(path) } }
                }
            }
    }
}
// Vídeo de la fototeca: llega como archivo temporal y se copia a «Videos».
@available(iOS 16.0, macOS 13.0, *)
struct TraiggerPickedMovie: Transferable {
    let path: String
    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(importedContentType: .movie) { received in
            guard let path = TraiggerMediaStore.copy(received.file, folder: "Videos") else { throw CocoaError(.fileWriteUnknown) }
            return TraiggerPickedMovie(path: path)
        }
    }
}
enum TraiggerMediaStore {
    static func copy(_ url: URL, folder: String) -> String? {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.appendingPathComponent(folder) else { return nil }
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        let destination = base.appendingPathComponent(url.lastPathComponent)
        try? FileManager.default.removeItem(at: destination)
        do { try FileManager.default.copyItem(at: url, to: destination) } catch { return nil }
        return destination.path
    }
}

// Vision Pro: Traigger libre en el espacio y la ventana para compartir por SharePlay/FaceTime (TraiggerShare.swift).
struct SpaceAndShareSection: View {
    var compact = false
    var vision = true
    @AppStorage("traigger.share.source") private var source = "cinema"
    @AppStorage("traigger.share.mode") private var mode = "logo"
    @AppStorage("traigger.share.webControls") private var webControls = true
    @State private var free = false
    var body: some View {
        Section(title: compact ? "Compartir" : vision ? "Espacio y SharePlay" : "Compartir en FaceTime") {
            if !compact && vision { Button { request("traigger:show") } label: { Label("Mostrar la ventana de Traigger", systemImage: "macwindow") } }
            if !compact && vision {
                Toggle(isOn: Binding(get: { free }, set: { free = $0; request($0 ? "free:on" : "free:off") })) {
                    Label("Traigger libre, sin ventana ni fondo", systemImage: "figure.stand")
                }
                Text("Llévalo a cualquier sitio cogiéndolo por la barra de abajo; agrándalo por las esquinas.").font(.caption).foregroundStyle(.secondary)
            }
            Picker("Compartir", selection: $source) {
                Text("Cine").tag("cinema"); Text("HDMI").tag("hdmi"); Text("Web").tag("web")
            }.pickerStyle(.segmented)
            Picker("Traigger", selection: $mode) {
                Text("Encima, como logo").tag("logo"); Text("Al lado, como en llamada").tag("side")
            }.pickerStyle(.segmented)
            .onChange(of: source) { _ in if vision { request("share:open:\(source):\(mode)") } }
            .onChange(of: mode) { _ in if vision { request("share:open:\(source):\(mode)") } }
            if source == "web" && vision { Toggle("Controles del navegador en lo compartido", isOn: $webControls) }
            HStack {
                if compact { Button { request("share:options:off") } label: { Label("Ocultar opciones", systemImage: "eye.slash") } }
                else if vision { Button { request("share:open:\(source):\(mode)") } label: { Label("Abrir la ventana para compartir", systemImage: "shareplay") } }
                else {
                    Button { request("share:open:\(source):\(mode)") } label: { Label("Empezar el modo compartir", systemImage: "shareplay") }
                    Button("Terminar") { request("share:close") }
                }
                Spacer()
                if vision { Button("Recolocar a Traigger") {
                    UserDefaults.standard.set(0.86, forKey: "traigger.share.x"); UserDefaults.standard.set(0.78, forKey: "traigger.share.y")
                    UserDefaults.standard.set(0.32, forKey: "traigger.share.size")
                } }
            }
            if !vision {
                Text("El contenido ocupa la pantalla con Traigger encima (arrástralo para moverlo, pellízcalo para escalarlo) y el menú se oculta. Comparte la pantalla en FaceTime; doble toque con dos dedos para volver al menú.").font(.caption).foregroundStyle(.secondary)
            } else {
            Text("En la ventana para compartir, arrastra a Traigger para moverlo y pellízcalo para escalarlo. Allí no hay botones: no salen en lo emitido; un doble toque sobre ella muestra estas opciones. En FaceTime, compártela con su botón de compartir.").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
    private func request(_ text: String) {
        NotificationCenter.default.post(name: Notification.Name("Traigger.Request"), object: nil, userInfo: ["request": text])
    }
}

// Calidad del envío de la entrada HDMI a Vision Pro (TraiggerHDMISender en TraiggerCapture.swift).
struct HDMIQuality: View {
    @ObservedObject var store: TraiggerStore
    @AppStorage("traigger.hdmi.preset") private var preset = "game"
    @AppStorage("traigger.hdmi.codec") private var codec = "h264"
    @AppStorage("traigger.hdmi.height") private var height = 1080
    @AppStorage("traigger.hdmi.fps") private var fps = 60
    @AppStorage("traigger.hdmi.mbps") private var mbps = 15.0
    @AppStorage("traigger.hdmi.path") private var path = "router"
    @AppStorage("traigger.hdmi.face") private var faceWhileSending = false
    var body: some View {
        Picker("Calidad", selection: $preset) {
            Text("Juego").tag("game"); Text("Calidad").tag("quality"); Text("Ahorro").tag("saver"); Text("A medida").tag("custom")
        }
        .pickerStyle(.segmented)
        .onChange(of: preset) { value in apply(value) }
        Text(hint).font(.caption).foregroundStyle(.secondary)
        if preset == "custom" {
            Picker("Códec", selection: $codec) { Text("H.264 (menos latencia)").tag("h264"); Text("HEVC (más calidad)").tag("hevc") }.onChange(of: codec) { _ in send() }
            Picker("Resolución", selection: $height) { Text("720p").tag(720); Text("1080p").tag(1080); Text("La de la fuente").tag(0) }.onChange(of: height) { _ in send() }
            Picker("Fotogramas", selection: $fps) { Text("30 fps").tag(30); Text("60 fps").tag(60) }.pickerStyle(.segmented).onChange(of: fps) { _ in send() }
            HStack { Text("Tasa"); Slider(value: $mbps, in: 4...40, step: 1) { editing in if !editing { send() } }; Text("\(Int(mbps)) Mbps").monospacedDigit() }
        }
        Toggle(isOn: $faceWhileSending) { Label("Seguir mi cara mientras envío", systemImage: "face.smiling") }
            .onChange(of: faceWhileSending) { _ in send() }
        Text(faceWhileSending ? "Traigger te sigue con la cámara frontal mientras envías; añade algo de latencia." : "Latencia mínima: mientras envías, Traigger no sigue tu cara (con las gafas puestas tampoco te vería).")
            .font(.caption).foregroundStyle(.secondary)
        Picker("Camino", selection: $path) {
            Text("Router").tag("router"); Text("Directa").tag("direct"); Text("Automático").tag("auto")
        }
        .pickerStyle(.segmented)
        .onChange(of: path) { _ in send() }
        Text(path == "router" ? "Por la red de casa. Lo más estable en la mayoría de casas."
             : path == "direct" ? "Enlace punto a punto con las Vision Pro (como AirDrop), sin perder internet. Útil sin router o con el router saturado."
             : "Los dos caminos con relevo automático. Puede dar más picos: la antena se reparte entre ambos.")
            .font(.caption).foregroundStyle(.secondary)
    }
    private var hint: String {
        switch preset {
        case "game": "1080p a 60 fps en H.264, codificador en baja latencia (15 Mbps). Lo mejor para jugar."
        case "quality": "1080p a 60 fps en HEVC (25 Mbps): más nitidez, unos milisegundos más."
        case "saver": "720p a 60 fps en H.264 (8 Mbps), para Wi-Fi saturadas."
        default: "Elige códec, resolución, fotogramas y tasa."
        }
    }
    private func apply(_ value: String) {
        switch value {
        case "game": codec = "h264"; height = 1080; fps = 60; mbps = 15
        case "quality": codec = "hevc"; height = 1080; fps = 60; mbps = 25
        case "saver": codec = "h264"; height = 720; fps = 60; mbps = 8
        default: break
        }
        send()
    }
    private func send() {
        let json = "{\"codec\":\"\(codec)\",\"height\":\(height),\"fps\":\(fps),\"mbps\":\(mbps),\"keySeconds\":2,\"path\":\"\(path)\",\"faceWhileSending\":\(faceWhileSending),\"forceFaceID\":\(UserDefaults.standard.bool(forKey: "traigger.hdmi.forceFaceID"))}"
        store.send("capture.quality", s: json)
    }
}

// Escenas (como las de OBS) y carteles para directos: un toque cambia la composición; lo que se ve sale igual en
// pantalla, en la cámara «Traigger» del Mac y en la emisión.
struct ScenesTab: View {
    @ObservedObject var store: TraiggerStore
    @State private var editor = false
    @State private var newName = ""
    @State private var editingKind = "brb"
    @State private var title = ""
    @State private var subtitle = ""
    @State private var picking = false
    private let cards: [(String, String, String)] = [("brb", "Vuelvo enseguida", "cup.and.saucer"), ("soon", "Empezamos pronto", "clock"), ("pause", "Pausa", "pause.circle"), ("end", "Fin", "hand.wave")]
    private let builtIn: Set<String> = ["Solo Traigger", "Contenido + logo", "Contenido + al lado", "Solo contenido", "Vuelvo enseguida", "Empezamos pronto", "Pausa", "Fin"]
    var body: some View {
        let st = store.state
        Page {
            if st.streamSupported == true { StreamSection(store: store) }
            Section(title: "Layout") {
                Button { editor = true } label: { Label("Abrir el editor de escena", systemImage: "rectangle.3.group").frame(maxWidth: .infinity) }
                    .buttonStyle(.borderedProminent)
                LayoutCanvas(store: store)
                HStack {
                    Text("Arrastra cada capa para moverla y su esquina para escalarla.").font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button("Restablecer") { store.send("layer.reset") }
                }
            }
            Section(title: "Escenas") {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 130), spacing: 10)], spacing: 10) {
                    ForEach(st.scenes ?? [], id: \.self) { name in
                        Button { store.send("scene.apply", s: name) } label: {
                            Text(name).font(.callout.weight(.medium)).multilineTextAlignment(.center)
                                .frame(maxWidth: .infinity, minHeight: 54).padding(6)
                                .background(st.scene == name ? Color.accentColor.opacity(0.3) : Color.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                        }
                        .buttonStyle(.plain)
                        .contextMenu { if !builtIn.contains(name) { Button("Borrar", role: .destructive) { store.send("scene.delete", s: name) } } }
                    }
                }
                HStack {
                    TextField("Nombre de la escena", text: $newName).textFieldStyle(.roundedBorder)
                    Button("Guardar la actual") { store.send("scene.save", s: newName); newName = "" }.disabled(newName.trimmingCharacters(in: .whitespaces).isEmpty)
                }
                Text("Guarda dónde está Traigger, si está oculto y el cartel que se ve. Mantén pulsada una escena tuya para borrarla.").font(.caption).foregroundStyle(.secondary)
            }
            Section(title: "Carteles") {
                HStack(spacing: 8) {
                    ForEach(cards, id: \.0) { card in
                        Button { store.send(st.cardKind == card.0 ? "card.hide" : "card.show", s: card.0) } label: {
                            VStack(spacing: 4) { Image(systemName: card.2).font(.title3); Text(card.1).font(.caption2).lineLimit(1).minimumScaleFactor(0.7) }
                                .frame(maxWidth: .infinity).padding(.vertical, 8)
                                .background(st.cardKind == card.0 ? Color.accentColor.opacity(0.3) : Color.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                        }.buttonStyle(.plain)
                    }
                }
                if st.cardKind?.isEmpty == false { Button("Quitar el cartel") { store.send("card.hide") } }
                Toggle("El cartel tapa también a Traigger", isOn: Binding(get: { st.cardCover ?? false }, set: { store.send("card.cover", b: $0) }))
                Picker("Editar el texto de", selection: $editingKind) { ForEach(cards, id: \.0) { Text($0.1).tag($0.0) } }
                TextField("Título", text: $title).textFieldStyle(.roundedBorder)
                TextField("Subtítulo", text: $subtitle).textFieldStyle(.roundedBorder)
                HStack {
                    Button("Guardar texto") { store.send("card.text", s: editingKind + "\n" + title + "\n" + subtitle) }.disabled(title.isEmpty)
                    Spacer()
                    if st.cardImage?.isEmpty == false { Button("Quitar imagen") { store.send("card.image", s: "") } }
                }
                Divider()
                HStack {
                    Text("Título como imagen o vídeo").font(.callout)
                    Spacer()
                    Button("Volver al texto") { store.send("card.titleMedia", s: editingKind + "\n") }
                }
                MediaSourceButtons(kind: .imageOrVideo) { store.send("card.titleMedia", s: editingKind + "\n" + $0) }
                Text("Un logo o rótulo animado en lugar del texto. Con transparencia: PNG, o vídeo .mov HEVC con canal alfa / ProRes 4444 (lo exportan Final Cut, Motion, After Effects o DaVinci).").font(.caption).foregroundStyle(.secondary)
                Divider()
                HStack { Text("Fondo del cartel (imagen o vídeo)").font(.callout); Spacer() }
                MediaSourceButtons(kind: .imageOrVideo) { store.send("card.image", s: $0) }
            }
        }
        .studioScreen(isPresented: $editor) { SceneEditor(store: store) { editor = false } }
    }
}
// Copia de imágenes elegidas a la carpeta de la app (JPG/PNG, convertidas si hace falta), compartida con los fondos.
private enum SettingsTabImages {
    static func keepImage(_ url: URL) -> String? { SettingsTab.keepAsImage(url) }
    static func keepImage(_ data: Data, name: String) -> String? { SettingsTab.keepImage(data, name: name) }
}

// Lienzo del modo layout: la pantalla en pequeño con una caja por capa. Arrastrar mueve, la esquina escala; cada
// cambio va a Unity («layer.rect»), que es quien compone la imagen que se ve y se emite.
struct LayoutCanvas: View {
    @ObservedObject var store: TraiggerStore
    @State private var live: [String: CGRect] = [:]
    @State private var start: [String: CGRect] = [:]
    private let layers: [(String, String, Color)] = [("movie", "Película", .blue), ("capture", "HDMI", .orange), ("avatar", "Traigger", .pink)]
    private func rect(_ key: String) -> CGRect {
        if let r = live[key] { return r }
        let v: [Double]? = key == "avatar" ? store.state.layerAvatar : key == "movie" ? store.state.layerMovie : store.state.layerCapture
        guard let v, v.count == 4 else { return CGRect(x: 0, y: 0, width: 1, height: 1) }
        return CGRect(x: v[0], y: v[1], width: v[2], height: v[3])
    }
    var body: some View {
        let aspect = 16.0 / 9.0   // la imagen que se emite y se comparte
        GeometryReader { geometry in
            let size = geometry.size
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 10).fill(Color.black.opacity(0.6))
                ForEach(layers, id: \.0) { layer in
                    let r = rect(layer.0)
                    let frame = CGRect(x: r.minX * size.width, y: r.minY * size.height, width: r.width * size.width, height: r.height * size.height)
                    ZStack(alignment: .bottomTrailing) {
                        RoundedRectangle(cornerRadius: 6).fill(layer.2.opacity(0.22))
                            .overlay(RoundedRectangle(cornerRadius: 6).stroke(layer.2, lineWidth: 2))
                            .overlay(Text(layer.1).font(.caption2.bold()).foregroundStyle(.white).padding(4), alignment: .topLeading)
                        Image(systemName: "arrow.up.left.and.arrow.down.right").font(.caption2.bold()).foregroundStyle(.white)
                            .frame(width: 26, height: 26).background(layer.2, in: Circle()).offset(x: 8, y: 8)
                            .gesture(drag(layer.0, size: size, resize: true))
                    }
                    .frame(width: max(12, frame.width), height: max(12, frame.height))
                    .position(x: frame.midX, y: frame.midY)
                    .gesture(drag(layer.0, size: size, resize: false))
                }
            }
        }
        .aspectRatio(aspect, contentMode: .fit)
        .frame(maxHeight: 260)
    }
    private func drag(_ key: String, size: CGSize, resize: Bool) -> some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { value in
                let base = start[key] ?? rect(key); start[key] = base
                var r = base
                let dx = value.translation.width / size.width, dy = value.translation.height / size.height
                if resize {
                    // Mantiene la proporción de la capa al escalar.
                    let factor = max(0.1, (base.width + dx) / max(0.01, base.width))
                    r.size = CGSize(width: base.width * factor, height: base.height * factor)
                } else { r.origin = CGPoint(x: base.minX + dx, y: base.minY + dy) }
                live[key] = r
                if key == "avatar" {   // misma colocación en la ventana para compartir de Vision Pro
                    UserDefaults.standard.set(Double(r.midX), forKey: "traigger.share.x"); UserDefaults.standard.set(Double(r.midY), forKey: "traigger.share.y")
                    UserDefaults.standard.set(Double(r.height), forKey: "traigger.share.size")
                }
                store.send("layer.rect", s: String(format: "%@|%.4f,%.4f,%.4f,%.4f", key, r.minX, r.minY, r.width, r.height))
            }
            .onEnded { _ in start[key] = nil; DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { live[key] = nil } }
    }
}

// Emisión en directo: Twitch, YouTube o un servidor RTMP propio. La clave se guarda en el Llavero, no en Unity.
private struct StreamSection: View {
    @ObservedObject var store: TraiggerStore
    @AppStorage("traigger.stream.platform") private var platform = "twitch"
    @AppStorage("traigger.stream.custom") private var customURL = "rtmp://"
    @AppStorage("traigger.stream.quality") private var quality = "720p30"
    @AppStorage("traigger.stream.mic") private var mic = 1.0
    @AppStorage("traigger.stream.movie") private var movie = 0.8
    @AppStorage("traigger.stream.capture") private var capture = 0.8
    @State private var key = ""
    private var url: String {
        switch platform {
        case "twitch": "rtmp://live.twitch.tv/app"
        case "youtube": "rtmps://a.rtmps.youtube.com/live2"
        default: customURL
        }
    }
    var body: some View {
        let st = store.state, live = st.streamLive == true
        Section(title: "Emitir en directo") {
            Picker("Plataforma", selection: $platform) { Text("Twitch").tag("twitch"); Text("YouTube").tag("youtube"); Text("Otro (RTMP)").tag("custom") }
                .pickerStyle(.segmented).disabled(live)
                .onChange(of: platform) { value in key = TraiggerKeychain.secret("stream." + value) ?? "" }
            if platform == "custom" { TextField("rtmp://servidor/app", text: $customURL).textFieldStyle(.roundedBorder).disabled(live) }
            SecureField(platform == "youtube" ? "Clave de emisión de YouTube Studio" : platform == "twitch" ? "Clave de retransmisión de Twitch" : "Clave", text: $key)
                .textFieldStyle(.roundedBorder).disabled(live)
                .onChange(of: key) { value in TraiggerKeychain.saveSecret(value, "stream." + platform) }
            Picker("Calidad", selection: $quality) {
                Text("720p 30").tag("720p30"); Text("720p 60").tag("720p60"); Text("1080p 30").tag("1080p30"); Text("1080p 60").tag("1080p60")
            }.pickerStyle(.segmented).disabled(live)
            HStack {
                Button { live ? store.send("stream.stop") : start() } label: {
                    Label(live ? "Terminar el directo" : "Empezar el directo", systemImage: live ? "stop.circle.fill" : "dot.radiowaves.left.and.right")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent).tint(live ? .red : .accentColor)
                .disabled(!live && (key.isEmpty || url.count < 10))
            }
            StatusRow(icon: "antenna.radiowaves.left.and.right", title: "Estado", value: st.streamStatus)
            Text("Volumen en el directo").font(.caption).foregroundStyle(.secondary)
            volume("Tu voz", "mic", $mic); volume("Película", "film", $movie); volume("Capturadora", "cable.connector", $capture)
            Text("Se emite lo que compones aquí abajo (escenas, layout y carteles). La clave se guarda en el Llavero del aparato.").font(.caption).foregroundStyle(.secondary)
        }
        .onAppear { key = TraiggerKeychain.secret("stream." + platform) ?? "" }
    }
    private func volume(_ title: String, _ icon: String, _ value: Binding<Double>) -> some View {
        HStack { Label(title, systemImage: icon).frame(width: 140, alignment: .leading); Slider(value: value, in: 0...1.5) { editing in if !editing { sendGains() } } }
    }
    private func sendGains() { store.send("stream.gains", s: String(format: "%.2f,%.2f,%.2f", mic, movie, capture)) }
    private func start() {
        let presets: [String: (Int, Int, Int, Double)] = ["720p30": (1280, 720, 30, 4.5), "720p60": (1280, 720, 60, 6), "1080p30": (1920, 1080, 30, 6), "1080p60": (1920, 1080, 60, 6)]
        let (w, h, fps, mbps) = presets[quality] ?? (1280, 720, 30, 4.5)
        let settings: [String: Any] = ["url": url, "key": key, "width": w, "height": h, "fps": fps, "mbps": mbps, "audioKbps": 160]
        guard let data = try? JSONSerialization.data(withJSONObject: settings), let json = String(data: data, encoding: .utf8) else { return }
        sendGains()
        store.send("stream.start", s: json)
    }
}

extension TraiggerKeychain {
    private static func secretQuery(_ account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "Traigger", kSecAttrAccount as String: account]
    }
    static func secret(_ account: String) -> String? {
        var query = secretQuery(account); query[kSecReturnData as String] = true
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }
    static func saveSecret(_ value: String, _ account: String) {
        SecItemDelete(secretQuery(account) as CFDictionary)
        guard !value.isEmpty else { return }
        var item = secretQuery(account); item[kSecValueData as String] = Data(value.utf8)
        SecItemAdd(item as CFDictionary, nil)
    }
}

// Experimento: intentar que Face ID siga funcionando con la capturadora activa (iOS suele impedirlo).
private struct ForceFaceIDToggle: View {
    @ObservedObject var store: TraiggerStore
    @AppStorage("traigger.hdmi.forceFaceID") private var force = false
    @AppStorage("traigger.hdmi.multicamProbe") private var probe = false
    @AppStorage("traigger.hdmi.hybridFace") private var hybrid = false
    @AppStorage("traigger.hybrid.gazeGain") private var gazeGain = 1.5
    @AppStorage("traigger.hybrid.center") private var centerRevision = 0
    @AppStorage("traigger.hybrid.invertX") private var invertX = false
    @AppStorage("traigger.hybrid.invertY") private var invertY = false
    @AppStorage("traigger.hdmi.codec") private var codec = "h264"
    @AppStorage("traigger.hdmi.height") private var height = 1080
    @AppStorage("traigger.hdmi.fps") private var fps = 60
    @AppStorage("traigger.hdmi.mbps") private var mbps = 15.0
    @AppStorage("traigger.hdmi.path") private var path = "router"
    @AppStorage("traigger.hdmi.face") private var faceWhileSending = false
    @AppStorage("traigger.hybrid.mouthGain") private var mouthGain = 1.6
    @AppStorage("traigger.hybrid.mouthCenter") private var mouthCenter = 0
    @AppStorage("traigger.hybrid.analysisFPS") private var analysisFPS = 30.0
    var body: some View {
        Toggle(isOn: $force) { Label("Priorizar expresiones completas", systemImage: "faceid") }
            .onChange(of: force) { _ in send() }
            .disabled(probe || hybrid)
        Text("Activado: conserva ARKit completo; si hay conflicto, detiene USB. Desactivado: prioriza el vídeo de la capturadora y pausa ARKit local hasta que detengas la captura.")
            .font(.caption).foregroundStyle(.secondary)
        if store.state.platform == "IPhonePlayer" {
            Toggle(isOn: $hybrid) { Label("HDMI + cara y mirada · MediaPipe", systemImage: "eye") }
                .onChange(of: hybrid) { active in if active { probe = false; store.send("cameraMode", i: 0) }; send() }
            Text("Una sesión USB + frontal, sin ARKit. Seguimiento aproximado con luz suficiente. Al detener la captura vuelve ARKit. Para este modo local, desactiva Enviar a Vision Pro y el seguimiento de cuerpo.")
                .font(.caption).foregroundStyle(.secondary)
            if hybrid {
                HStack { Text("Sensibilidad de boca"); Slider(value: $mouthGain, in: 0.5...3) }
                Button("Calibrar boca cerrada · relaja los labios y pulsa") { mouthCenter += 1 }
                Picker("Respuesta facial", selection: $analysisFPS) {
                    Text("Suave al hablar · hasta 30 Hz").tag(30.0)
                    Text("Menor consumo · hasta 20 Hz").tag(20.0)
                }
                Text("Se combinan mandíbula y separación de labios. Sube la sensibilidad si pierde sílabas; bájala si abre la boca en reposo. La frecuencia real depende del dispositivo.")
                    .font(.caption).foregroundStyle(.secondary)
                Button("Centrar mirada · mira al centro y pulsa") { centerRevision += 1 }
                HStack { Text("Amplitud de mirada"); Slider(value: $gazeGain, in: 0.5...3) }
                Toggle("Invertir mirada horizontal", isOn: $invertX)
                Toggle("Invertir mirada vertical", isOn: $invertY)
                Text("La lengua automática y el seguimiento en oscuridad requieren ARKit; la interpolación suaviza el movimiento, pero no recupera mediciones perdidas.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Toggle(isOn: $probe) { Label("Experimental · probar USB + frontal", systemImage: "testtube.2") }
                .onChange(of: probe) { active in if active { hybrid = false }; send() }
            Text("Activa esta prueba y pulsa Capturar. Prueba dos cámaras en una sesión, sin ARKit, análisis facial ni sonido. Desactiva el envío a Vision Pro y el seguimiento de cuerpo. Observa los contadores durante un minuto. Apágala para volver al modo normal.")
                .font(.caption).foregroundStyle(.secondary)
        }

    }
    private func send() {
        let json = "{\"codec\":\"\(codec)\",\"height\":\(height),\"fps\":\(fps),\"mbps\":\(mbps),\"keySeconds\":2,\"path\":\"\(path)\",\"faceWhileSending\":\(faceWhileSending),\"forceFaceID\":\(force),\"multiCamProbe\":\(probe),\"hybridFace\":\(hybrid)}"
        store.send("capture.quality", s: json)
    }
}

#if os(iOS)
// Un iPhone como Face ID de otro aparato: con la capturadora en el iPad, el iPhone sigue la cara con su TrueDepth
// (ojos, mofletes y expresiones completos) y se la manda por Wi-Fi (TraiggerFaceRelay.swift).
private struct FaceRelaySection: View {
    @AppStorage("traigger.face.send") private var send = false
    @State private var status = ""
    @State private var peers: [String] = []
    @AppStorage("traigger.face.target") private var target = ""
    // «Traigger iPad de Daniel 4821» → «iPad de Daniel».
    static func label(_ name: String) -> String {
        var words = name.replacingOccurrences(of: "Traigger ", with: "").split(separator: " ")
        if let last = words.last, Int(last) != nil { words.removeLast() }
        return words.joined(separator: " ")
    }
    var body: some View {
        Section(title: "Cara desde otro aparato") {
            Toggle(isOn: $send) { Label("Enviar mi cara a otro iPad, iPhone o Mac con Traigger", systemImage: "faceid") }
            if send {
                Picker("Enviar a", selection: Binding(get: { target }, set: { target = $0; TraiggerFaceRelay.shared.choose($0) })) {
                    Text("El primero que aparezca").tag("")
                    ForEach(Array(Set(peers + (target.isEmpty ? [] : [target]))).sorted(), id: \.self) { name in Text(Self.label(name)).tag(name) }
                }
            }
            Text(status).font(.footnote).foregroundStyle(.secondary)
        }
        .onReceive(Timer.publish(every: 1, on: .main, in: .common).autoconnect()) { _ in status = TraiggerFaceRelay.shared.status; peers = TraiggerFaceRelay.shared.peers }
    }
}
#endif
