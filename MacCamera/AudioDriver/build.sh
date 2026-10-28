#!/bin/sh
# Compila el dispositivo de audio «Traigger» (TraiggerAudio.driver) para Core Audio.
set -e
# Firma: signing.local (no se sube) o, si no existe, la primera identidad de desarrollo del llavero.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
[ -f "$ROOT/signing.local" ] && . "$ROOT/signing.local"
IDENTITY="${TRAIGGER_SIGN_IDENTITY:-$(security find-identity -v -p codesigning | awk -F'"' '/Apple Development/{print $2; exit}')}"
cd "$(dirname "$0")"
D=build/TraiggerAudio.driver/Contents
rm -rf build/TraiggerAudio.driver; mkdir -p "$D/MacOS"
xcrun clang -O2 -bundle -target arm64-apple-macos13.0 -arch arm64 -arch x86_64 -framework CoreAudio -framework CoreFoundation -o "$D/MacOS/TraiggerAudio" TraiggerAudio.c
cat > "$D/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>TraiggerAudio</string>
  <key>CFBundleIdentifier</key><string>com.traigger.audio</string>
  <key>CFBundleName</key><string>Traigger</string>
  <key>CFBundlePackageType</key><string>BNDL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFPlugInFactories</key><dict><key>8D6B0E2A-4C1F-4A7E-9B3D-2F5E6A7B8C9D</key><string>TraiggerAudio_Create</string></dict>
  <key>CFPlugInTypes</key><dict><key>443ABAB8-E7B3-491A-B985-BEB9187030DB</key><array><string>8D6B0E2A-4C1F-4A7E-9B3D-2F5E6A7B8C9D</string></array></dict>
  <key>AudioServerPlugIn_LoadingConditions</key><dict><key>IOService</key><dict/></dict>
</dict></plist>
PLIST
/usr/libexec/PlistBuddy -c "Delete :AudioServerPlugIn_LoadingConditions" "$D/Info.plist"
codesign --force --timestamp --options runtime --sign "${TRAIGGER_DEVELOPER_ID:-$IDENTITY}" build/TraiggerAudio.driver
echo "Listo: build/TraiggerAudio.driver"
