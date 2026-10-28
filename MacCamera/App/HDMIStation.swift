import AVFoundation
import SwiftUI

// Capturadora HDMI en el Mac: captura y envía el vídeo y el sonido al iPad y/o a las Vision Pro con el mismo emisor de
// baja latencia que el iPad (TraiggerCapture.swift + TraiggerLink.swift). Así el iPad no abre ninguna cámara y su
// Face ID sigue funcionando completo mientras se juega.
@MainActor final class HDMIStation: ObservableObject {
    static let shared = HDMIStation()
    @Published var devices: [AVCaptureDevice] = []
    @AppStorage("traigger.mac.hdmi.device") var deviceID = ""
    @AppStorage("traigger.mac.hdmi.destination") var destination = "ipad"
    @Published var running = false
    @Published var status = "Capturadora apagada"
    private var timer: Timer?

    func refresh() {
        devices = TraiggerCapture.devices()
        if deviceID.isEmpty || !devices.contains(where: { $0.uniqueID == deviceID }) { deviceID = devices.first?.uniqueID ?? "" }
    }
    func toggle() { running ? stop() : start() }
    func start() {
        refresh()
        guard !deviceID.isEmpty else { status = "Conecta una capturadora HDMI por USB"; return }
        applyDestination()
        TraiggerHDMISender.shared.setEnabled(true)        // enviar → captura en YUV a 60 fps, sin conversiones
        TraiggerCapture.shared.start(deviceID, audio: true)
        running = true
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in
            MainActor.assumeIsolated { HDMIStation.shared.status = TraiggerCapture.shared.status + "\n" + TraiggerHDMISender.shared.status }
        }
    }
    func stop() {
        TraiggerCapture.shared.stop(); TraiggerHDMISender.shared.setEnabled(false)
        running = false; timer?.invalidate(); status = "Capturadora apagada"
    }
    func applyDestination() {
        let json = "{\"codec\":\"h264\",\"height\":1080,\"fps\":60,\"mbps\":15,\"keySeconds\":2,\"path\":\"router\",\"destination\":\"\(destination)\"}"
        TraiggerHDMISender.shared.setQuality(json)
    }
}

struct HDMIMenu: View {
    @ObservedObject var station = HDMIStation.shared
    var body: some View {
        Text("Capturadora HDMI").font(.headline)
        if station.devices.isEmpty { Text("Conecta una capturadora por USB") }
        else {
            Picker("Capturadora", selection: $station.deviceID) { ForEach(station.devices, id: \.uniqueID) { Text($0.localizedName).tag($0.uniqueID) } }
        }
        Picker("Enviar a", selection: $station.destination) {
            Text("iPad").tag("ipad"); Text("Vision Pro").tag("vision"); Text("Los dos").tag("both")
        }
        .onChange(of: station.destination) { _ in if station.running { station.applyDestination() } }
        Button(station.running ? "Dejar de enviar la capturadora" : "Enviar la capturadora") { station.toggle() }
        Text(station.status)
    }
}
