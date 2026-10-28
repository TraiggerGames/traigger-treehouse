import AppKit
import ServiceManagement
import SwiftUI
import SystemExtensions

// «Traigger Camera»: instala la cámara virtual «Traigger» y vive en la barra de menús, arrancando al iniciar sesión,
// para pasar a la cámara el vídeo que llega por cable desde el iPhone/iPad (CableBridge). Debe estar en /Aplicaciones.
// La primera vez macOS pide permitir la cámara en Ajustes del Sistema → General → Inicio de sesión y extensiones.
@main struct TraiggerCameraApp: App {
    @StateObject private var installer = Installer()
    @StateObject private var cable = CableBridge()
    @StateObject private var mixer = AudioMixer.shared
    init() {
        // Arranca sola al iniciar sesión (se puede quitar en Ajustes del Sistema → Ítems de inicio).
        try? SMAppService.mainApp.register()
    }
    var body: some Scene {
        MenuBarExtra {
            Text("Cámara virtual Traigger").font(.headline)
            Text(cable.status)
            Text(installer.status)
            Divider()
            if !mixer.driverInstalled { Button("Instalar micrófono «Traigger»…") { mixer.installDriver() } }
            Toggle("Micrófono «Traigger» (tu voz + la película)", isOn: $mixer.enabled)
            Toggle("Oír la película en este Mac", isOn: $mixer.monitor)
            Text(mixer.status)
            Divider()
            HDMIMenu()
            Divider()
            FaceStatus()
            Divider()
            Button("Instalar cámara") { installer.run(activate: true) }
            Button("Desinstalar cámara") { installer.run(activate: false) }
            Divider()
            Button("Salir") { NSApplication.shared.terminate(nil) }
        } label: {
            Image(systemName: "video.fill").onAppear { cable.start(); mixer.start(); HDMIStation.shared.refresh(); FaceForwarder.shared.start() }
        }
    }
}

@MainActor final class Installer: NSObject, ObservableObject, OSSystemExtensionRequestDelegate {
    @Published var status = Bundle.main.bundlePath.hasPrefix("/Applications") ? "Cámara lista. Elige «Traigger» en FaceTime." : "Mueve Traigger Camera a /Aplicaciones antes de instalar."
    private let identifier = "com.traigger.camera.extension"

    func run(activate: Bool) {
        let request = activate ? OSSystemExtensionRequest.activationRequest(forExtensionWithIdentifier: identifier, queue: .main)
                               : OSSystemExtensionRequest.deactivationRequest(forExtensionWithIdentifier: identifier, queue: .main)
        request.delegate = self
        OSSystemExtensionManager.shared.submitRequest(request)
        status = activate ? "Instalando…" : "Desinstalando…"
    }

    nonisolated func request(_ request: OSSystemExtensionRequest, actionForReplacingExtension existing: OSSystemExtensionProperties,
                             withExtension ext: OSSystemExtensionProperties) -> OSSystemExtensionRequest.ReplacementAction { .replace }
    nonisolated func requestNeedsUserApproval(_ request: OSSystemExtensionRequest) {
        Task { @MainActor in self.status = "Permítela en Ajustes del Sistema → General → Inicio de sesión y extensiones → Extensiones de cámara." }
    }
    nonisolated func request(_ request: OSSystemExtensionRequest, didFinishWithResult result: OSSystemExtensionRequest.Result) {
        Task { @MainActor in
            self.status = result == .completed ? "Hecho. Si FaceTime estaba abierto, ciérralo y ábrelo para ver la cámara «Traigger»."
                                               : "Hecho; se completará al reiniciar el Mac."
        }
    }
    nonisolated func request(_ request: OSSystemExtensionRequest, didFailWithError error: Error) {
        Task { @MainActor in self.status = "Error: \(error.localizedDescription)" }
    }
}

private struct FaceStatus: View {
    @ObservedObject var face = FaceForwarder.shared
    var body: some View { Text(face.status) }
}
