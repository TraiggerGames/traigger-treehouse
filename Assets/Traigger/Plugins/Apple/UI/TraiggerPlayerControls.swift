import SwiftUI

// Controles de reproducción del cine de Traigger, al estilo del reproductor de Apple: barra de cristal con título,
// −10 s / reproducir / +10 s, progreso, idioma y subtítulos, volumen y ocultar. Se esconden solos a los 3 s sin tocarlos
// y vuelven al tocar la pantalla (TraiggerStore.showPlayerControls). Van por encima de Unity, así que no salen en la
// cámara «Traigger» de la videollamada.
extension TraiggerStore {
    // Mostrar los controles un rato (al tocar la pantalla o al usarlos).
    func showPlayerControls() {
        playerControlsHidden = false
        playerControlsShown = true
        playerControlsTouched = Date()
        let touched = playerControlsTouched
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.2) { [weak self] in
            guard let self, self.playerControlsTouched == touched, self.state.moviePaused != true else { return }
            withAnimation(.easeOut(duration: 0.3)) { self.playerControlsShown = false }
        }
    }
    var playerControlsVisible: Bool { state.moviePlaying == true && playerControlsShown && !playerControlsHidden }
}

struct TraiggerPlayerControls: View {
    @ObservedObject var store: TraiggerStore
    @State private var scrubbing: Double?
    @State private var showVolume = false

    var body: some View {
        let st = store.state
        let length = max(st.movieLength ?? 0, 1)
        let time = scrubbing ?? st.movieTime ?? 0
        VStack(spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text(st.movieTitle ?? "").font(.headline).lineLimit(1)
                Spacer()
                Button { withAnimation { store.playerControlsHidden = true } } label: { Image(systemName: "chevron.down.circle.fill").font(.title2) }
                    .accessibilityLabel("Ocultar controles")
            }
            HStack(spacing: 14) {
                Text(clockText(time)).font(.caption.monospacedDigit()).frame(minWidth: 52, alignment: .leading)
                Slider(value: Binding(get: { time }, set: { scrubbing = $0; store.showPlayerControls() }), in: 0...length) { editing in
                    if !editing, let value = scrubbing { store.send("movie.seek", f: value); scrubbing = nil }
                }
                Text("-" + clockText(length - time)).font(.caption.monospacedDigit()).frame(minWidth: 58, alignment: .trailing)
            }
            HStack(spacing: 0) {
                volume
                Spacer()
                HStack(spacing: 34) {
                    Button { store.send("movie.skip", f: -10); store.showPlayerControls() } label: { Image(systemName: "gobackward.10").font(.title2) }
                    Button { store.send("movie.pause", b: st.moviePaused != true); store.showPlayerControls() } label: {
                        Image(systemName: st.moviePaused == true ? "play.fill" : "pause.fill").font(.system(size: 34)).frame(width: 44)
                    }
                    Button { store.send("movie.skip", f: 10); store.showPlayerControls() } label: { Image(systemName: "goforward.10").font(.title2) }
                }
                Spacer()
                tracksMenu(st)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 20).padding(.vertical, 14)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 26, style: .continuous))
        .shadow(color: .black.opacity(0.25), radius: 18, y: 6)
        .onTapGesture { store.showPlayerControls() }
    }

    private var volume: some View {
        HStack(spacing: 8) {
            Button { withAnimation { showVolume.toggle() }; store.showPlayerControls() } label: {
                Image(systemName: (store.state.movieVolume ?? 80) == 0 ? "speaker.slash.fill" : "speaker.wave.2.fill").font(.title3)
            }
            if showVolume {
                Slider(value: Binding(get: { Double(store.state.movieVolume ?? 80) }, set: { store.send("movie.volume", i: Int($0)); store.showPlayerControls() }), in: 0...100)
                    .frame(width: 120)
            }
        }
        .frame(minWidth: 44, alignment: .leading)
    }

    // Bocadillo de idioma y subtítulos, como el del reproductor de Apple.
    private func tracksMenu(_ st: TraiggerState) -> some View {
        Menu {
            Section("Audio") {
                ForEach(st.audioTracks ?? [], id: \.self) { track in
                    Button { store.send("movie.audio", s: track.id ?? "") } label: { check(trackName(track), track.selected == true) }
                }
            }
            Section("Subtítulos") {
                Button { store.send("movie.subtitles", s: ""); store.send("movie.generated", b: false) } label: {
                    check("No", (st.subtitleTracks ?? []).allSatisfy { $0.selected != true } && st.generatedSubtitles != true)
                }
                ForEach(st.subtitleTracks ?? [], id: \.self) { track in
                    Button { store.send("movie.subtitles", s: track.id ?? "") } label: { check(trackName(track), track.selected == true) }
                }
                Button { store.send("movie.generated", b: st.generatedSubtitles != true) } label: { check("Generados por Apple", st.generatedSubtitles == true) }
            }
        } label: { Image(systemName: "captions.bubble.fill").font(.title3).frame(minWidth: 44, alignment: .trailing) }
        .menuStyle(.borderlessButton)
    }

    @ViewBuilder private func check(_ title: String, _ on: Bool) -> some View {
        if on { Label(title, systemImage: "checkmark") } else { Text(title) }
    }
    private func trackName(_ track: TraiggerTrack) -> String {
        if let code = track.language, !code.isEmpty, let language = Locale.current.localizedString(forLanguageCode: code) { return language.capitalized }
        return track.name ?? "Pista"
    }
    private func clockText(_ seconds: Double) -> String {
        let s = Int(max(0, seconds)); return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60) : String(format: "%d:%02d", s / 60, s % 60)
    }
}
