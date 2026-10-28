import Network
import SwiftUI

// Explorador de vídeos: inicio (este aparato, servidores, seguir viendo) y navegador de carpetas en red (SMB/NAS).
// Ruta en migas de pan, búsqueda, orden, lista o cuadrícula, y mensajes claros cuando la carpeta está vacía o falla
// la conexión. Los servidores de la red se encuentran solos (Bonjour «_smb._tcp») para no tener que escribir la IP.

// MARK: - Piezas comunes

private struct Card<Content: View>: View {
    var padding: CGFloat = 14
    @ViewBuilder var content: Content
    var body: some View {
        content.padding(padding).frame(maxWidth: .infinity, alignment: .leading)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
    }
}

private struct Heading: View {
    let title: String
    var subtitle: String? = nil
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.headline)
            if let subtitle { Text(subtitle).font(.caption).foregroundStyle(.secondary) }
        }
    }
}

private struct BigTile: View {
    let icon: String, title: String, subtitle: String, tint: Color
    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon).font(.title2).foregroundStyle(.white).frame(width: 46, height: 46)
                .background(LinearGradient(colors: [tint, tint.opacity(0.7)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline).lineLimit(1)
                Text(subtitle).font(.caption).foregroundStyle(.secondary).lineLimit(2)
            }
            Spacer(minLength: 0)
        }
        .padding(12).frame(maxWidth: .infinity, alignment: .leading)
        .background(tint.opacity(0.12), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .contentShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
    }
}

func clockShort(_ seconds: Double) -> String {
    let s = max(0, Int(seconds)); return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60) : String(format: "%d:%02d", s / 60, s % 60)
}

// MARK: - Inicio

struct MediaHome: View {
    @ObservedObject var store: TraiggerStore
    @ObservedObject var library: TraiggerLibrary
    var openServer: (TraiggerServer) -> Void
    var editServer: (TraiggerServer?) -> Void
    var play: (String, Double?) -> Void
    @State private var deleting: TraiggerServer?

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if !library.recents.isEmpty {
                VStack(alignment: .leading, spacing: 10) {
                    Heading(title: "Seguir viendo")
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 12) {
                            ForEach(library.recents.prefix(10), id: \.self) { recent in
                                Button { play(recent.url, recent.position) } label: {
                                    VStack(alignment: .leading, spacing: 8) {
                                        Image(systemName: "play.circle.fill").font(.largeTitle).foregroundStyle(.blue)
                                        Text(recent.title).font(.callout.weight(.semibold)).lineLimit(2).multilineTextAlignment(.leading)
                                        Text("Desde \(clockShort(recent.position))").font(.caption).foregroundStyle(.secondary)
                                    }
                                    .padding(12).frame(width: 170, height: 130, alignment: .topLeading)
                                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                                }
                                .buttonStyle(.plain)
                                .contextMenu { Button("Quitar de la lista", role: .destructive) { library.recents.removeAll { $0 == recent } } }
                            }
                        }
                    }
                }
            }

            VStack(alignment: .leading, spacing: 10) {
                Heading(title: "En este aparato", subtitle: "Fototeca, iCloud Drive, el propio aparato o un disco USB")
                Card { MediaSourceButtons(kind: .video) { play($0, nil) }.buttonStyle(.bordered).controlSize(.large) }
            }

            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Heading(title: "En tu red", subtitle: "Tu NAS, disco de red u ordenador con carpetas compartidas")
                    Spacer()
                    Button { editServer(nil) } label: { Label("Añadir", systemImage: "plus") }.buttonStyle(.bordered)
                }
                if library.servers.isEmpty {
                    Card {
                        VStack(spacing: 10) {
                            Image(systemName: "externaldrive.connected.to.line.below").font(.system(size: 40)).foregroundStyle(.secondary)
                            Text("Aún no has añadido ningún servidor").font(.headline)
                            Text("Conecta tu NAS o la carpeta compartida de otro ordenador para ver sus películas aquí.").font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
                            Button { editServer(nil) } label: { Label("Añadir servidor", systemImage: "plus") }.buttonStyle(.borderedProminent)
                        }
                        .frame(maxWidth: .infinity).padding(.vertical, 8)
                    }
                } else {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 240), spacing: 12)], spacing: 12) {
                        ForEach(library.servers) { server in
                            Button { openServer(server) } label: {
                                BigTile(icon: "externaldrive.fill.badge.wifi", title: server.name.isEmpty ? server.host : server.name,
                                        subtitle: server.share.isEmpty ? server.host : "\(server.host) · \(server.share)", tint: .indigo)
                            }
                            .buttonStyle(.plain)
                            .contextMenu {
                                Button { editServer(server) } label: { Label("Editar", systemImage: "pencil") }
                                Button(role: .destructive) { deleting = server } label: { Label("Eliminar", systemImage: "trash") }
                            }
                        }
                    }
                    Text("Mantén pulsado un servidor para editarlo o eliminarlo.").font(.caption).foregroundStyle(.secondary)
                }
            }
        }
        .confirmationDialog("¿Eliminar «\(deleting.map { $0.name.isEmpty ? $0.host : $0.name } ?? "")»?", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }), titleVisibility: .visible) {
            Button("Eliminar", role: .destructive) {
                if let server = deleting { TraiggerKeychain.delete(for: server); library.servers.removeAll { $0.id == server.id } }
                deleting = nil
            }
        } message: { Text("Se borra de la lista y su contraseña del llavero. Tus archivos no se tocan.") }
    }
}

// MARK: - Carpeta en red

struct NetworkBrowser: View {
    @ObservedObject var store: TraiggerStore
    let server: TraiggerServer
    var back: () -> Void
    var edit: () -> Void
    var play: (String) -> Void
    @AppStorage("traigger.browser.grid") private var grid = false
    @AppStorage("traigger.browser.sort") private var sort = "name"
    @AppStorage("traigger.browser.all") private var showAll = false
    @State private var search = ""

    private var title: String { server.name.isEmpty ? server.host : server.name }
    // Carpetas dentro del servidor, desde la carpeta compartida hasta la actual (sin usuario ni contraseña).
    private func crumbs(_ folder: String?) -> [String] {
        guard let folder, let url = URL(string: folder) else { return [] }
        return url.path.split(separator: "/").map { String($0).removingPercentEncoding ?? String($0) }
    }

    var body: some View {
        let st = store.state
        let parts = crumbs(st.movieFolder)
        let all = (st.movieItems ?? []).filter { item in
            let name = item.name ?? ""
            guard item.folder == true || showAll || TraiggerFiles.isVideo(name) else { return false }
            return search.isEmpty || name.localizedCaseInsensitiveContains(search)
        }
        let items = all.sorted { a, b in
            if (a.folder == true) != (b.folder == true) { return a.folder == true }   // carpetas primero
            return sort == "name" ? (a.name ?? "").localizedStandardCompare(b.name ?? "") == .orderedAscending
                                  : (a.name ?? "").localizedStandardCompare(b.name ?? "") == .orderedDescending
        }
        VStack(alignment: .leading, spacing: 12) {
            // Barra: volver, ruta y acciones.
            HStack(spacing: 8) {
                Button(action: back) { Label("Inicio", systemImage: "chevron.left") }.buttonStyle(.bordered)
                ScrollViewReader { proxy in
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 4) {
                            Button { store.send("movie.browse", s: server.url(path: "")) } label: { Label(title, systemImage: "externaldrive.fill.badge.wifi") }
                                .buttonStyle(.borderless)
                            ForEach(Array(parts.enumerated()), id: \.offset) { index, part in
                                Image(systemName: "chevron.right").font(.caption2).foregroundStyle(.tertiary)
                                Button(part) { store.send("movie.browse", s: server.url(path: parts[0...index].joined(separator: "/"))) }
                                    .buttonStyle(.borderless).font(.body.weight(index == parts.count - 1 ? .semibold : .regular))
                                    .foregroundStyle(index == parts.count - 1 ? Color.primary : Color.accentColor).id(index)
                            }
                        }
                    }
                    .onChange(of: parts.count) { count in withAnimation { proxy.scrollTo(count - 1, anchor: .trailing) } }
                }
                Spacer(minLength: 0)
                Button { store.send("movie.up") } label: { Image(systemName: "arrow.up") }.buttonStyle(.bordered).disabled(parts.isEmpty).help("Carpeta de arriba")
                Button { if let folder = st.movieFolder { store.send("movie.browse", s: folder) } } label: { Image(systemName: "arrow.clockwise") }.buttonStyle(.bordered).help("Volver a leer")
            }

            HStack(spacing: 8) {
                HStack {
                    Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                    TextField("Buscar en esta carpeta", text: $search).textFieldStyle(.plain)
                    if !search.isEmpty { Button { search = "" } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }.buttonStyle(.plain) }
                }
                .padding(.horizontal, 10).padding(.vertical, 8)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                Menu {
                    Picker("Orden", selection: $sort) { Text("Nombre (A–Z)").tag("name"); Text("Nombre (Z–A)").tag("reverse") }
                    Toggle("Mostrar todos los archivos", isOn: $showAll)
                } label: { Image(systemName: "line.3.horizontal.decrease.circle").font(.title3) }
                Picker("", selection: $grid) {
                    Image(systemName: "list.bullet").tag(false)
                    Image(systemName: "square.grid.2x2").tag(true)
                }
                .pickerStyle(.segmented).frame(width: 96)
            }

            if st.movieBrowsing == true {
                Card { HStack(spacing: 12) { ProgressView(); Text("Leyendo «\(parts.last ?? title)»…").foregroundStyle(.secondary) }.frame(maxWidth: .infinity).padding(.vertical, 20) }
            } else if items.isEmpty {
                let failed = (st.movieItems ?? []).isEmpty && !(st.movieStatus ?? "").isEmpty
                Card {
                    VStack(spacing: 10) {
                        Image(systemName: failed ? "wifi.exclamationmark" : search.isEmpty ? "film.stack" : "magnifyingglass").font(.system(size: 38)).foregroundStyle(.secondary)
                        Text(failed ? "No se puede abrir la carpeta" : search.isEmpty ? "No hay vídeos aquí" : "Nada coincide con «\(search)»").font(.headline)
                        if failed, let status = st.movieStatus { Text(status).font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center) }
                        if failed { Text("Comprueba que el servidor está encendido, en la misma red, y que el usuario y la contraseña son correctos.").font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center) }
                        HStack {
                            if failed {
                                Button { store.send("movie.browse", s: st.movieFolder ?? server.url()) } label: { Label("Reintentar", systemImage: "arrow.clockwise") }.buttonStyle(.borderedProminent)
                                Button(action: edit) { Label("Editar servidor", systemImage: "pencil") }.buttonStyle(.bordered)
                            } else if !showAll && search.isEmpty && !(st.movieItems ?? []).isEmpty {
                                Button { showAll = true } label: { Label("Mostrar todos los archivos", systemImage: "doc") }.buttonStyle(.bordered)
                            }
                        }
                    }
                    .frame(maxWidth: .infinity).padding(.vertical, 12)
                }
            } else if grid {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 130), spacing: 12)], spacing: 12) {
                    ForEach(items, id: \.self) { item in
                        Button { open(item) } label: {
                            VStack(spacing: 8) {
                                Image(systemName: icon(item)).font(.system(size: 38)).foregroundStyle(color(item)).frame(height: 56)
                                Text(item.name ?? "").font(.caption).lineLimit(3).multilineTextAlignment(.center)
                            }
                            .padding(10).frame(maxWidth: .infinity, minHeight: 130)
                            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                            .contentShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                        }
                        .buttonStyle(.plain)
                    }
                }
            } else {
                Card(padding: 4) {
                    LazyVStack(spacing: 0) {
                        ForEach(Array(items.enumerated()), id: \.element) { index, item in
                            Button { open(item) } label: {
                                HStack(spacing: 12) {
                                    Image(systemName: icon(item)).font(.title3).foregroundStyle(color(item)).frame(width: 32)
                                    Text(item.name ?? "").lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
                                    if item.folder == true { Image(systemName: "chevron.right").font(.caption).foregroundStyle(.tertiary) }
                                    else if let ext = (item.name as NSString?)?.pathExtension, !ext.isEmpty {
                                        Text(ext.uppercased()).font(.caption2.bold()).padding(.horizontal, 6).padding(.vertical, 2)
                                            .background(color(item).opacity(0.15), in: Capsule()).foregroundStyle(color(item))
                                    }
                                }
                                .padding(.horizontal, 10).padding(.vertical, 10).contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            if index < items.count - 1 { Divider().padding(.leading, 54) }
                        }
                    }
                }
            }
            if !items.isEmpty { Text("\(items.filter { $0.folder == true }.count) carpetas · \(items.filter { $0.folder != true }.count) archivos").font(.caption).foregroundStyle(.secondary) }
        }
        .onChange(of: st.movieFolder) { _ in search = "" }
    }

    private func open(_ item: TraiggerItem) {
        if item.folder == true { store.send("movie.browse", s: item.url ?? "") } else { play(item.url ?? "") }
    }
    private func icon(_ item: TraiggerItem) -> String {
        let name = item.name ?? ""
        return item.folder == true ? "folder.fill" : TraiggerFiles.isVideo(name) ? "film.fill" : TraiggerFiles.isSubtitle(name) ? "captions.bubble.fill" : "doc.fill"
    }
    private func color(_ item: TraiggerItem) -> Color {
        let name = item.name ?? ""
        return item.folder == true ? .blue : TraiggerFiles.isVideo(name) ? .purple : TraiggerFiles.isSubtitle(name) ? .orange : .gray
    }
}

// MARK: - Añadir o editar servidor

// Busca en la red los equipos que comparten carpetas (Bonjour «_smb._tcp»): NAS, Mac, Windows con SMB…
@MainActor final class TraiggerSMBFinder: ObservableObject {
    @Published var found: [String] = []
    private var browser: NWBrowser?
    func start() {
        guard browser == nil else { return }
        let browser = NWBrowser(for: .bonjour(type: "_smb._tcp", domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { results, _ in
            let names = results.compactMap { result -> String? in if case .service(let name, _, _, _) = result.endpoint { return name }; return nil }
            Task { @MainActor in self.found = Array(Set(names)).sorted() }
        }
        browser.start(queue: .main); self.browser = browser
    }
    func stop() { browser?.cancel(); browser = nil }
}

struct ServerEditorView: View {
    @State var server: TraiggerServer
    @Binding var password: String
    let save: (TraiggerServer) -> Void
    let cancel: () -> Void
    @StateObject private var finder = TraiggerSMBFinder()
    @State private var showPassword = false
    private var isNew: Bool { server.host.isEmpty }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "externaldrive.fill.badge.wifi").font(.title2).foregroundStyle(.white).frame(width: 44, height: 44)
                    .background(Color.indigo, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text(isNew ? "Añadir servidor" : "Editar servidor").font(.title2.bold())
                    Text("Tu NAS u ordenador con carpetas compartidas (SMB)").font(.caption).foregroundStyle(.secondary)
                }
            }
            .padding(20)
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if !finder.found.isEmpty {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Encontrados en tu red").font(.headline)
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack(spacing: 8) {
                                    ForEach(finder.found, id: \.self) { name in
                                        Button {
                                            server.host = name.replacingOccurrences(of: " ", with: "-") + ".local"
                                            if server.name.isEmpty { server.name = name }
                                        } label: { Label(name, systemImage: "server.rack") }
                                        .buttonStyle(.bordered).tint(server.name == name ? .accentColor : .secondary)
                                    }
                                }
                            }
                        }
                    }
                    field("Servidor", "nas.local o 192.168.1.20", text: $server.host, icon: "network",
                          help: "El nombre o la dirección IP del equipo. Lo ves en los ajustes de tu NAS.")
                    field("Carpeta compartida", "Peliculas o Media/Cine", text: $server.share, icon: "folder",
                          help: "Opcional: la carpeta donde están tus vídeos. Vacío = ver todas las carpetas compartidas.")
                    field("Nombre", "NAS de casa", text: $server.name, icon: "tag", help: "Cómo aparece en Traigger.")
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Acceso").font(.headline)
                        field(nil, "Usuario (vacío = invitado)", text: $server.user, icon: "person")
                        HStack {
                            Image(systemName: "key").frame(width: 22).foregroundStyle(.secondary)
                            Group {
                                if showPassword { TextField("Contraseña", text: $password) } else { SecureField("Contraseña", text: $password) }
                            }
                            .textFieldStyle(.plain)
                            Button { showPassword.toggle() } label: { Image(systemName: showPassword ? "eye.slash" : "eye") }.buttonStyle(.borderless)
                        }
                        .padding(10).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        Label("La contraseña se guarda en el llavero del aparato, nunca en texto plano.", systemImage: "lock.shield").font(.caption).foregroundStyle(.secondary)
                    }
                }
                .padding(20)
            }
            Divider()
            HStack {
                Button("Cancelar", role: .cancel, action: cancel).keyboardShortcut(.cancelAction)
                Spacer()
                Button(isNew ? "Añadir" : "Guardar") { save(server) }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
                    .disabled(server.host.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding(16)
        }
        .frame(minWidth: 420, idealWidth: 520, minHeight: 520)
        .onAppear { finder.start() }
        .onDisappear { finder.stop() }
    }

    @ViewBuilder private func field(_ title: String?, _ prompt: String, text: Binding<String>, icon: String, help: String? = nil) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            if let title { Text(title).font(.headline) }
            HStack {
                Image(systemName: icon).frame(width: 22).foregroundStyle(.secondary)
                TextField(prompt, text: text).textFieldStyle(.plain)
                    #if os(iOS) || os(visionOS)
                    .textInputAutocapitalization(.never).autocorrectionDisabled()
                    #endif
            }
            .padding(10).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            if let help { Text(help).font(.caption).foregroundStyle(.secondary) }
        }
    }
}
