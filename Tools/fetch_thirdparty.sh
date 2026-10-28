#!/bin/zsh
# Descarga e instala las bibliotecas de terceros que no van en el repositorio (ver docs/TERCEROS.md).
#
#   Tools/fetch_thirdparty.sh            VLCKit (obligatorio para el reproductor de vídeo) y MediaPipe (opcional)
#   Tools/fetch_thirdparty.sh vlckit     solo VLCKit
#   Tools/fetch_thirdparty.sh mediapipe  solo MediaPipe
#
# Todo se descarga de las fuentes oficiales y se comprueba con SHA-256 antes de instalarlo. Las descargas se
# guardan en ThirdParty/ (no se sube) y no se vuelven a bajar si ya están.
set -euo pipefail
ROOT="${0:A:h:h}"
cd "$ROOT"
WHAT="${1:-all}"

fetch() {   # fetch <url> <archivo> <sha256>
  local url="$1" file="$2" sum="$3"
  if [[ -f "$file" ]] && [[ "$(shasum -a 256 "$file" | cut -d' ' -f1)" == "$sum" ]]; then
    echo "  ya descargado: ${file:t}"; return
  fi
  echo "  descargando ${file:t}…"
  mkdir -p "${file:h}"
  curl -fL --retry 3 --progress-bar -o "$file.part" "$url"
  local got="$(shasum -a 256 "$file.part" | cut -d' ' -f1)"
  if [[ "$got" != "$sum" ]]; then
    rm -f "$file.part"
    echo "ERROR: la suma SHA-256 de ${file:t} no coincide (esperada $sum, obtenida $got)." >&2
    echo "       La versión publicada pudo cambiar: revisa docs/TERCEROS.md." >&2
    exit 1
  fi
  mv "$file.part" "$file"
}

# ── VLCKit 4 (VideoLAN, LGPL 2.1+) ──────────────────────────────────────────────────────────────────────
# Un solo paquete con las porciones de iOS, visionOS y macOS.
VLC_VERSION="4.0.0a19"
VLC_URL="https://download.videolan.org/pub/cocoapods/unstable/VLCKit-4.0.0a19-d7597c1706-85a537d69.tar.xz"
VLC_SHA="1172078a43150af202c31feb62db3d6687f242d3aa048cce1b899f51c4f14142"

vlckit() {
  echo "VLCKit $VLC_VERSION"
  local archive="ThirdParty/VLCKit/download/VLCKit-$VLC_VERSION.tar.xz" work="ThirdParty/VLCKit/extracted-4"
  fetch "$VLC_URL" "$archive" "$VLC_SHA"
  rm -rf "$work"; mkdir -p "$work"
  tar -xf "$archive" -C "$work"
  local xc="$(find "$work" -maxdepth 3 -name VLCKit.xcframework -type d | head -1)"
  [[ -n "$xc" ]] || { echo "ERROR: no se encuentra VLCKit.xcframework en el paquete." >&2; exit 1; }
  # iPhone/iPad y Vision Pro: los marcos van dentro de Assets (Unity los incluye al compilar; sus .meta sí se suben).
  for pair in "ios-arm64:Assets/Traigger/Plugins/iOS" "xros-arm64:Assets/Traigger/Plugins/visionOS"; do
    local slice="${pair%%:*}" dest="${pair#*:}/VLCKit.framework"
    rm -rf "$dest"; ditto "$xc/$slice/VLCKit.framework" "$dest"
    echo "  instalado $dest"
  done
  # Mac: solo arm64; MacCamera/MoviePlugin/build.sh lo mete dentro de TraiggerMoviePlayer.bundle.
  local mac="ThirdParty/VLCKit/macos/VLCKit.framework"
  rm -rf "$mac"; mkdir -p "${mac:h}"; ditto --arch arm64 "$xc/macos-arm64_x86_64/VLCKit.framework" "$mac"
  echo "  instalado $mac (luego: MacCamera/MoviePlugin/build.sh)"
}

# ── MediaPipe 1.1.1 (Google AI Edge, Apache 2.0) — opcional: modo HDMI híbrido en iPhone/iPad ──────────────
MP_DIR="ThirdParty/MediaPipe-1.1.1"
MP_BASE="https://dl.google.com/cpdc/20261013-213039"
mediapipe() {
  echo "MediaPipe 1.1.1 (opcional)"
  fetch "$MP_BASE/MediaPipeTasksCommon-1.1.1.xcframework.zip" "$MP_DIR/MediaPipeTasksCommonBinary.zip" 25690f3005c0405a7f27b6d273b4db241811d00fbb01c54004057ee1d0e983d8
  fetch "$MP_BASE/MediaPipeTaskGraphs-1.1.1.xcframework.zip" "$MP_DIR/MediaPipeTaskGraphsBinary.zip" e4d88ed65c101764ef90281a411b9942f369e48673d448eff1f934e1f68d9633
  fetch "$MP_BASE/MediaPipeTasksVision-1.1.1.xcframework.zip" "$MP_DIR/MediaPipeTasksVision.zip" 755390ce853b062536899f0729321156d4cdceb263aa92c4bf3b2dd05fe640e0
  for zip in "$MP_DIR"/*.zip; do unzip -oq "$zip" -d "$MP_DIR"; done
  curl -fsL -o "$MP_DIR/LICENSE" https://raw.githubusercontent.com/google-ai-edge/mediapipe/master/LICENSE || true
  echo "  instalado en $MP_DIR (MediaPipeBuild.cs lo copia al proyecto de Xcode de iOS)"
}

case "$WHAT" in
  all) vlckit; mediapipe ;;
  vlckit) vlckit ;;
  mediapipe) mediapipe ;;
  *) echo "Uso: $0 [all|vlckit|mediapipe]" >&2; exit 2 ;;
esac
echo "Listo."
