# Compilar Traigger

## Requisitos

- macOS con **Unity 6000.5.1f1** (módulos iOS, visionOS y macOS) y **Xcode 26** o posterior.
- Una **cuenta de desarrollador de Apple** (la gratuita vale para tus propios aparatos; la extensión de cámara del Mac y iCloud necesitan la de pago).
- Las bibliotecas de terceros, instaladas con `Tools/fetch_thirdparty.sh` (VLCKit obligatorio, MediaPipe opcional). Ver [TERCEROS.md](TERCEROS.md).

## Identificadores de la app

Los identificadores `com.traigger.*` están registrados por el autor en Apple: para firmar con tu cuenta tienes que usar los tuyos.

- **iPhone/iPad y Vision Pro:** Unity → **Edit → Project Settings → Player → iOS / visionOS → Bundle Identifier** (por ejemplo `com.tunombre.vtuberstudio`). El contenedor de iCloud se deriva de él (`iCloud.<identificador>`).
- **Traigger Control** (Mac): `PRODUCT_BUNDLE_IDENTIFIER` en `NativeUI/Control/make_project.rb`.
- **Traigger Camera** (Mac): `com.traigger.camera` y `com.traigger.camera.extension` en `MacCamera/make_project.rb`, el `identifier` de `MacCamera/App/TraiggerCameraApp.swift` y los `Info.plist`/`.entitlements` de `MacCamera/App` y `MacCamera/Extension` (busca `com.traigger` en esa carpeta).

## Firma

Copia `signing.example` como **`signing.local`** (en la raíz del proyecto) y pon tu equipo y tus identidades de Apple. No se sube al repositorio y lo leen Unity (al compilar para iPhone/iPad y Vision Pro) y los scripts del Mac. También puedes usar variables de entorno con los mismos nombres (`TRAIGGER_TEAM`, `TRAIGGER_SIGN_IDENTITY`, `TRAIGGER_DEVELOPER_ID`). Sin identidad, los scripts usan la primera «Apple Development» de tu llavero.

## iPhone / iPad

1. En Unity: **Traigger → 4 · Compilar iPhone / iPad**. Genera `Builds/iOS` con todo configurado (permisos, marcos, firma).
2. Abre `Builds/iOS/Unity-iPhone.xcodeproj` y ejecuta en tu aparato, o desde la terminal:

```bash
xcodebuild -project Builds/iOS/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release -destination 'generic/platform=iOS' -allowProvisioningUpdates
```

## Vision Pro

1. En Unity: **Traigger → 5 · Compilar Vision Pro** (no el 5b, que es para el simulador). Genera `Builds/visionOS` con el host nativo (`NativeHost`) integrado.
2. Compila el esquema **NativeHost**:

```bash
xcodebuild -project Builds/visionOS/Unity-VisionOS.xcodeproj -scheme NativeHost -configuration Release -destination 'generic/platform=visionOS' -allowProvisioningUpdates
```

## Mac

- **Editor / app de Mac:** el plugin nativo se compila con `MacCamera/MoviePlugin/build.sh` (reproductor, capturadora, emisión y clips en `Assets/Traigger/Plugins/macOS/TraiggerMoviePlayer.bundle`).
- **Cámara y micrófono virtuales:** `cd MacCamera && ruby make_project.rb`, compila «Traigger Camera», cópiala a `/Applications` y usa su menú **Instalar cámara** / **Instalar micrófono**. Tras cambiar la extensión, sube `CFBundleVersion` en `Extension/Info.plist` y `App/Info.plist`.
- **Traigger Control** (la interfaz para el editor): `cd NativeUI/Control && ruby make_project.rb` y compila el esquema «Traigger Control».

## Comprobaciones rápidas

- C#: Unity compila al abrir el proyecto.
- Swift de la interfaz (sin Unity), en las tres plataformas:

```bash
cd Assets/Traigger/Plugins/Apple/UI && xcrun --sdk iphoneos swiftc -typecheck -target arm64-apple-ios15.0 *.swift
```
