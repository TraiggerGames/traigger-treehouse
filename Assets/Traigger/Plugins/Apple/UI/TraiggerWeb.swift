import SwiftUI
import WebKit

// Navegador de Traigger (iPad, Vision Pro y Traigger Control). Un único navegador compartido: lo abren la interfaz
// (sección «Web») o la API de Unity (VisionWeb.Open). Avisa a Unity de cada página (VisionWeb.PageChanged).
@MainActor final class TraiggerBrowser: NSObject, ObservableObject, WKNavigationDelegate {
    static let shared = TraiggerBrowser()
    @Published var address = ""
    @Published var title = ""
    @Published var loading = false
    @Published var canGoBack = false
    @Published var canGoForward = false
    @Published var bookmarks: [String] = UserDefaults.standard.stringArray(forKey: "traigger.web.bookmarks") ?? ["https://www.youtube.com", "https://www.apple.com/es/"] {
        didSet { UserDefaults.standard.set(bookmarks, forKey: "traigger.web.bookmarks") }
    }
    // Quien quiera enterarse de cada página (la app lo conecta con Unity).
    var pageChanged: ((String, String) -> Void)?
    let webView: WKWebView

    override init() {
        let configuration = WKWebViewConfiguration()
        #if !os(macOS)
        configuration.allowsInlineMediaPlayback = true
        #endif
        configuration.mediaTypesRequiringUserActionForPlayback = []
        webView = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        webView.navigationDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        address = UserDefaults.standard.string(forKey: "traigger.web.last") ?? ""
    }

    // Abre una dirección o, si no lo parece, la busca.
    func open(_ text: String) {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return }
        var url: URL?
        if value.contains("://") { url = URL(string: value) }
        else if value.contains("."), !value.contains(" ") { url = URL(string: "https://" + value) }
        if url == nil, let query = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) { url = URL(string: "https://duckduckgo.com/?q=" + query) }
        guard let url else { return }
        webView.load(URLRequest(url: url))
    }
    func reopenLast() { if webView.url == nil, !address.isEmpty { open(address) } }

    nonisolated func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) { Task { @MainActor in self.update(loading: true) } }
    nonisolated func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { Task { @MainActor in self.update(loading: false) } }
    nonisolated func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { Task { @MainActor in self.update(loading: false) } }
    private func update(loading: Bool) {
        self.loading = loading
        address = webView.url?.absoluteString ?? address
        title = webView.title ?? ""
        canGoBack = webView.canGoBack; canGoForward = webView.canGoForward
        if !loading { UserDefaults.standard.set(address, forKey: "traigger.web.last"); pageChanged?(address, title) }
    }
    func toggleBookmark() {
        guard !address.isEmpty else { return }
        if let index = bookmarks.firstIndex(of: address) { bookmarks.remove(at: index) } else { bookmarks.append(address) }
    }
}

#if os(macOS)
struct TraiggerWebContent: NSViewRepresentable {
    let webView: WKWebView
    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ view: WKWebView, context: Context) {}
}
#else
struct TraiggerWebContent: UIViewRepresentable {
    let webView: WKWebView
    func makeUIView(context: Context) -> WKWebView { webView }
    func updateUIView(_ view: WKWebView, context: Context) {}
}
#endif

// Barra de navegador + página. «extras» permite a cada plataforma añadir botones (cerrar, inmersivo…).
struct TraiggerBrowserView<Extras: View>: View {
    @ObservedObject var browser = TraiggerBrowser.shared
    @State private var editing = ""
    @ViewBuilder var extras: Extras
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Button { browser.webView.goBack() } label: { Image(systemName: "chevron.backward") }.disabled(!browser.canGoBack)
                Button { browser.webView.goForward() } label: { Image(systemName: "chevron.forward") }.disabled(!browser.canGoForward)
                TextField("Buscar o escribir dirección", text: $editing)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit { browser.open(editing) }
                    #if os(iOS) || os(visionOS)
                    .textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.webSearch)
                    #endif
                Button { if browser.loading { browser.webView.stopLoading() } else { browser.webView.reload() } } label: { Image(systemName: browser.loading ? "xmark" : "arrow.clockwise") }
                Button { browser.toggleBookmark() } label: { Image(systemName: browser.bookmarks.contains(browser.address) ? "star.fill" : "star") }
                Menu { ForEach(browser.bookmarks, id: \.self) { mark in Button(mark) { browser.open(mark) } } } label: { Image(systemName: "book") }
                extras
            }
            .buttonStyle(.borderless)
            .padding(10)
            if browser.loading { ProgressView().progressViewStyle(.linear).frame(height: 2) }
            TraiggerWebContent(webView: browser.webView)
        }
        .onAppear { editing = browser.address; browser.reopenLast() }
        .onChange(of: browser.address) { value in editing = value }
    }
}
