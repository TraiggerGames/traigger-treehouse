#!/bin/sh
# Compila el plugin de Unity para Mac (arm64 + x86_64) y lo deja en Assets/Traigger/Plugins/macOS.
set -e
# Firma: signing.local (no se sube) o, si no existe, la primera identidad de desarrollo del llavero.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
[ -f "$ROOT/signing.local" ] && . "$ROOT/signing.local"
IDENTITY="${TRAIGGER_SIGN_IDENTITY:-$(security find-identity -v -p codesigning | awk -F'"' '/Apple Development/{print $2; exit}')}"
cd "$(dirname "$0")"
OUT=../../Assets/Traigger/Plugins/macOS
mkdir -p "$OUT" build
for ARCH in arm64 x86_64; do
  xcrun swiftc -O -emit-library -module-name TraiggerVirtualCam -target $ARCH-apple-macos13.0 \
    -framework CoreMediaIO -framework CoreMedia -framework CoreVideo -o build/libTraiggerVirtualCam-$ARCH.dylib TraiggerVirtualCam.swift
done
lipo -create build/libTraiggerVirtualCam-arm64.dylib build/libTraiggerVirtualCam-x86_64.dylib -output "$OUT/TraiggerVirtualCam.dylib"
codesign --force --sign "$IDENTITY" "$OUT/TraiggerVirtualCam.dylib"
echo "Plugin listo: $OUT/TraiggerVirtualCam.dylib"
