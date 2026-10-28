#!/bin/sh
# Compila el reproductor de películas (Plugins/Apple/TraiggerMoviePlayer.m) como plugin de Unity para Mac.
# VLCKit.framework (arm64, en ThirdParty/VLCKit/macos) va dentro del propio .bundle, en Contents/Frameworks.
set -e
# Firma: signing.local (no se sube) o, si no existe, la primera identidad de desarrollo del llavero.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
[ -f "$ROOT/signing.local" ] && . "$ROOT/signing.local"
IDENTITY="${TRAIGGER_SIGN_IDENTITY:-$(security find-identity -v -p codesigning | awk -F'"' '/Apple Development/{print $2; exit}')}"
cd "$(dirname "$0")"
OUT=../../Assets/Traigger/Plugins/macOS
B="$OUT/TraiggerMoviePlayer.bundle/Contents"
rm -rf "$OUT/TraiggerMoviePlayer.bundle"; mkdir -p "$B/MacOS"
VLC=../../ThirdParty/VLCKit/macos
mkdir -p "$B/Frameworks" && ditto "$VLC/VLCKit.framework" "$B/Frameworks/VLCKit.framework"
# Reproductor (C) + subtítulos generados (Swift) en un mismo .bundle.
OBJ=build; mkdir -p $OBJ
xcrun clang -O2 -fobjc-arc -c -target arm64-apple-macos13.0 -o $OBJ/movie.o ../../Assets/Traigger/Plugins/Apple/TraiggerMoviePlayer.m
# iCloud: perfil de voz, escenas y carteles (VoiceProfileStore.cs, StudioCloud.cs).
xcrun clang++ -O2 -fobjc-arc -c -target arm64-apple-macos13.0 -x objective-c++ -o $OBJ/cloud.o ../../Assets/Traigger/Plugins/Apple/TraiggerCloud.mm
xcrun swiftc -O -c -parse-as-library -module-name TraiggerSubtitles -target arm64-apple-macos13.0 -o $OBJ/subtitles.o ../../Assets/Traigger/Plugins/Apple/TraiggerSubtitles.swift
# Capturadora y emisión en un mismo módulo (la capturadora pasa su sonido al emisor).
xcrun swiftc -O -c -parse-as-library -module-name TraiggerCapture -wmo -target arm64-apple-macos13.0 -o $OBJ/capture.o ../../Assets/Traigger/Plugins/Apple/TraiggerCapture.swift ../../Assets/Traigger/Plugins/Apple/TraiggerStream.swift ../../Assets/Traigger/Plugins/Apple/TraiggerClip.swift ../../Assets/Traigger/Plugins/Apple/TraiggerLink.swift
xcrun swiftc -target arm64-apple-macos13.0 -emit-library -Xlinker -bundle -Xlinker -undefined -Xlinker error $OBJ/movie.o $OBJ/subtitles.o $OBJ/capture.o $OBJ/cloud.o \
  -F "$VLC" -framework VLCKit -framework AVFoundation -lc++ -Xlinker -rpath -Xlinker @loader_path/../Frameworks -o "$B/MacOS/TraiggerMoviePlayer" 2>&1 | grep -v "warning: -dylib" || true
test -f "$B/MacOS/TraiggerMoviePlayer"
cat > "$B/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>TraiggerMoviePlayer</string>
  <key>CFBundleIdentifier</key><string>com.traigger.movieplayer</string>
  <key>CFBundlePackageType</key><string>BNDL</string>
</dict></plist>
PLIST
codesign --force --sign "$IDENTITY" "$B/Frameworks/VLCKit.framework"
codesign --force --sign "$IDENTITY" "$OUT/TraiggerMoviePlayer.bundle"
echo "Plugin listo: $OUT/TraiggerMoviePlayer.bundle"
