# Software de terceros

El código de Traigger es MIT (`LICENSE`). Las bibliotecas de terceros conservan su propia licencia y **no se suben al
repositorio**: unas por su tamaño (cientos de MB) y otras para no redistribuir binarios que cada uno puede bajar de su
fuente oficial. Todo se instala con un solo comando:

```bash
Tools/fetch_thirdparty.sh            # todo (VLCKit + MediaPipe)
Tools/fetch_thirdparty.sh vlckit     # solo VLCKit
Tools/fetch_thirdparty.sh mediapipe  # solo MediaPipe
```

El script descarga cada archivo de la web oficial, comprueba su **SHA-256** (si no coincide, se para sin instalar nada)
y lo coloca donde lo espera el proyecto. Las descargas quedan en `ThirdParty/` y no se repiten.

## Resumen

| Componente | Versión | Licencia | ¿Obligatorio? | Dónde acaba |
|---|---|---|---|---|
| **VLCKit / libVLC** (VideoLAN) | 4.0.0a19 | LGPL 2.1 o posterior | Sí, para el reproductor de vídeo | `Assets/Traigger/Plugins/iOS/VLCKit.framework`, `Assets/Traigger/Plugins/visionOS/VLCKit.framework`, `ThirdParty/VLCKit/macos/VLCKit.framework` |
| **MediaPipe Tasks Vision** (Google AI Edge) | 1.1.1 | Apache 2.0 | No: solo el modo HDMI híbrido en iPhone/iPad | `ThirdParty/MediaPipe-1.1.1/` (se copia al proyecto de Xcode al compilar) |
| Modelo `face_landmarker.task` (MediaPipe) | float16 v1 | Apache 2.0 | Incluido en el repositorio | `Assets/StreamingAssets/face_landmarker.task` |
| **Unity 6** | 6000.5.1f1 | Términos de Unity | Sí | Tu instalación (necesitas tu propia licencia de Unity) |
| Frameworks de Apple | — | Términos de Apple | Sí | Del sistema: ARKit, AVFoundation, VideoToolbox, Speech, SoundAnalysis, Translation, FoundationModels, CoreMediaIO, Network |

## VLCKit (reproductor de vídeo)

- **Origen:** <https://download.videolan.org/pub/cocoapods/unstable/VLCKit-4.0.0a19-d7597c1706-85a537d69.tar.xz>
  (SHA-256 `1172078a43150af202c31feb62db3d6687f242d3aa048cce1b899f51c4f14142`). Es un solo paquete con iOS, visionOS y macOS.
- **Qué hace el script:** copia las porciones `ios-arm64` y `xros-arm64` del `VLCKit.xcframework` a las carpetas de
  plugins de Unity (los `.meta` con su configuración sí están en el repositorio) y la de macOS (solo arm64) a
  `ThirdParty/VLCKit/macos`.
- **Después, en el Mac:** compila el plugin del editor y de la app de Mac, que mete VLCKit dentro de
  `TraiggerMoviePlayer.bundle` y lo firma con tu identidad:

  ```bash
  MacCamera/MoviePlugin/build.sh
  ```

- **Licencia LGPL:** VLCKit se enlaza **dinámicamente** (como `VLCKit.framework`), así que cualquiera puede sustituirlo
  por otra versión compilada por sí mismo. Si distribuyes una app compilada con Traigger, incluye el aviso LGPL de VLCKit
  (`COPYING.txt` dentro del paquete descargado) e indica dónde está su código: <https://code.videolan.org/videolan/VLCKit>.

### Usar otra versión de VLCKit

El código solo usa la API C de libVLC 4 (`TraiggerMoviePlayer.m`). Para probar otra versión 4.x:

1. Búscala en <https://download.videolan.org/pub/cocoapods/unstable/> (archivos `VLCKit-4.*.tar.xz`).
2. Cambia `VLC_VERSION`, `VLC_URL` y `VLC_SHA` al principio de `Tools/fetch_thirdparty.sh`
   (la suma sale de `shasum -a 256 archivo.tar.xz`) y vuelve a ejecutarlo.
3. Ejecuta `MacCamera/MoviePlugin/build.sh` y compila para iPhone/iPad o Vision Pro como siempre.

VLCKit 3.x **no** sirve: la API de libVLC 3 es distinta (sonido y exploración de carpetas).

## MediaPipe (opcional)

Solo hace falta para el **modo HDMI híbrido** del iPhone/iPad (seguir la cara con la cámara normal mientras la frontal
está ocupada). Sin él el proyecto compila igual: el Swift usa `#if canImport(MediaPipeTasksVision)` y
`Editor/MediaPipeBuild.cs` avisa en la consola y sigue.

| Archivo | Origen |
|---|---|
| MediaPipeTasksCommon | `https://dl.google.com/cpdc/20261013-213039/MediaPipeTasksCommon-1.1.1.xcframework.zip` |
| MediaPipeTaskGraphs | `https://dl.google.com/cpdc/20261013-213039/MediaPipeTaskGraphs-1.1.1.xcframework.zip` |
| MediaPipeTasksVision | `https://dl.google.com/cpdc/20261013-213039/MediaPipeTasksVision-1.1.1.xcframework.zip` |

Las sumas SHA-256 están en el script; son las del `Package.swift` oficial de MediaPipe
(<https://github.com/google-ai-edge/mediapipe/blob/master/Package.swift>). Para actualizar, copia de ese archivo las URL y
sumas de la versión nueva.

## Módulos no incluidos

- **Transporte HDMI de doble camino** (`TraiggerLink`): la versión completa (Wi-Fi + conexión directa AWDL, relevo
  automático entre caminos y diagnóstico con consejos) es parte de una app comercial aparte y no se publica. El
  repositorio trae una **versión abierta** en `Assets/Traigger/Plugins/Apple/TraiggerLink.swift` con la misma interfaz y
  el mismo formato de paquetes: envía por un solo camino (la red de casa), mide la latencia y funciona con los mismos
  receptores (iPad y Vision Pro). Puedes mejorarla o sustituirla manteniendo su interfaz pública.

## Arte

El arte de Traigger (personaje, trajes como el de Halloween, la piel 3D y sus texturas) **no** es software de terceros
ni está bajo MIT: ver [`LICENSE-ART.md`](../LICENSE-ART.md).
