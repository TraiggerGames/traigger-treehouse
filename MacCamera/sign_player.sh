#!/bin/sh
# Firma la app de Traigger para Mac que compila Unity (menú 3) con los permisos de iCloud: perfil de voz, escenas y
# carteles compartidos con iPhone, iPad y Vision Pro. Unity la deja firmada «ad hoc», y iCloud exige firmar con tu
# equipo y un perfil de aprovisionamiento. El perfil se consigue compilando una app mínima con el mismo
# identificador (Xcode lo crea solo con -allowProvisioningUpdates) y se copia dentro de la app de Unity.
# Uso: sign_player.sh "<ruta a Traigger VTuber Studio.app>" <identificador> <almacén clave-valor> <contenedor iCloud>
set -e
APP="$1"; ID="$2"; KV="$3"; CONTAINER="$4"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
[ -f "$ROOT/signing.local" ] && . "$ROOT/signing.local"
TEAM="${TRAIGGER_TEAM:?Falta TRAIGGER_TEAM en signing.local}"
IDENTITY="${TRAIGGER_SIGN_IDENTITY:-$(security find-identity -v -p codesigning | awk -F'"' '/Apple Development/{print $2; exit}')}"
KV=$(echo "$KV" | sed "s/\$(TeamIdentifierPrefix)/$TEAM./")
WORK="$(cd "$(dirname "$0")" && pwd)/build/PlayerProfile"; rm -rf "$WORK"; mkdir -p "$WORK"
cat > "$WORK/Player.entitlements" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.application-identifier</key><string>$TEAM.$ID</string>
  <key>com.apple.developer.team-identifier</key><string>$TEAM</string>
  <key>com.apple.developer.ubiquity-kvstore-identifier</key><string>$KV</string>
  <key>com.apple.developer.icloud-container-identifiers</key><array><string>$CONTAINER</string></array>
  <key>com.apple.developer.ubiquity-container-identifiers</key><array><string>$CONTAINER</string></array>
  <key>com.apple.developer.icloud-services</key><array><string>CloudDocuments</string></array>
  <key>com.apple.security.device.camera</key><true/>
  <key>com.apple.security.device.audio-input</key><true/>
</dict></plist>
PLIST
# App mínima con el mismo identificador y permisos, solo para que Xcode cree el perfil.
echo 'print("perfil")' > "$WORK/main.swift"
ruby - "$WORK" "$ID" "$TEAM" <<'RUBY'
require 'xcodeproj'
work, id, team = ARGV
project = Xcodeproj::Project.new(File.join(work, 'Profile.xcodeproj'))
target = project.new_target(:application, 'Profile', :osx, '13.0')
target.source_build_phase.add_file_reference(project.main_group.new_file(File.join(work, 'main.swift')))
target.build_configurations.each do |c|
  s = c.build_settings
  s['PRODUCT_BUNDLE_IDENTIFIER'] = id; s['DEVELOPMENT_TEAM'] = team; s['CODE_SIGN_STYLE'] = 'Automatic'
  s['CODE_SIGN_ENTITLEMENTS'] = File.join(work, 'Stub.entitlements'); s['GENERATE_INFOPLIST_FILE'] = 'YES'; s['SWIFT_VERSION'] = '5.0'
end
project.save
RUBY
# El perfil de la app mínima solo necesita los permisos de iCloud (Xcode añade el resto).
cat > "$WORK/Stub.entitlements" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.developer.ubiquity-kvstore-identifier</key><string>\$(TeamIdentifierPrefix)$ID</string>
  <key>com.apple.developer.icloud-container-identifiers</key><array><string>$CONTAINER</string></array>
  <key>com.apple.developer.ubiquity-container-identifiers</key><array><string>$CONTAINER</string></array>
  <key>com.apple.developer.icloud-services</key><array><string>CloudDocuments</string></array>
</dict></plist>
PLIST
xcodebuild -project "$WORK/Profile.xcodeproj" -scheme Profile -configuration Debug -derivedDataPath "$WORK/dd" -allowProvisioningUpdates -quiet build
PROFILE=$(find "$WORK/dd" -name embedded.provisionprofile | head -1)
[ -n "$PROFILE" ] || { echo "Xcode no creó el perfil de aprovisionamiento"; exit 1; }
cp "$PROFILE" "$APP/Contents/embedded.provisionprofile"
# Por dentro primero (plugins, librerías y Traigger Control), luego la app con los permisos.
find "$APP/Contents/Frameworks" "$APP/Contents/PlugIns" -maxdepth 1 \( -name "*.dylib" -o -name "*.bundle" \) -exec codesign --force --sign "$IDENTITY" {} \;
[ -d "$APP/Contents/Helpers/Traigger Control.app" ] && codesign --force --sign "$IDENTITY" "$APP/Contents/Helpers/Traigger Control.app"
codesign --force --sign "$IDENTITY" --entitlements "$WORK/Player.entitlements" "$APP"
codesign --verify --strict "$APP" && echo "App de Mac firmada con iCloud: $APP"
