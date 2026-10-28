# Traigger VTuber Studio

**Un estudio VTuber todo en uno para el ecosistema Apple, sin OBS.** Tu personaje 2D sigue tu cara, tu voz y tu cuerpo; compones la escena con vídeos, una capturadora HDMI o la web; y lo envías a FaceTime, Zoom o Meet como cámara, lo compartes por SharePlay o lo emites a Twitch y YouTube.

Funciona en **iPhone, iPad, Vision Pro y Mac**. Está hecho por y para **Traigger**, un VTuber zorro furro (personaje propio de su autor, **no reutilizable**), pero puedes **meter tu propio personaje** sin programar: parte de la [plantilla libre](Examples/Plantilla) y [crea un paquete de personaje](docs/PAQUETES_DE_PERSONAJE.md).

> **English summary.** Traigger VTuber Studio is an all-in-one VTuber app for iPhone, iPad, Apple Vision Pro and Mac, built with Unity 6 and native SwiftUI. It tracks your face (TrueDepth/ARKit), voice and body; composes scenes with videos (VLC-powered, SMB/NAS), HDMI capture cards, a web browser and overlays/BRB cards; and outputs to a macOS virtual camera + virtual microphone, SharePlay, or RTMP streaming (Twitch/YouTube) — no OBS needed. Bring your own character with a simple PNG + JSON [character pack](docs/PAQUETES_DE_PERSONAJE.md), starting from the CC0 [template](Examples/Plantilla). Note: the Traigger character and art are **all rights reserved** and may not be reused. The UI and docs are currently in Spanish; translations are welcome.

## Qué hace

| | iPhone / iPad | Vision Pro | Mac |
|---|---|---|---|
| Seguimiento de cara (TrueDepth / ARKit) | ✅ | — (no hay cámara de cara para apps) | con iPhone/iPad |
| Cuerpo, manos y piernas (Vision / LiDAR) | ✅ | ✅ cabeza y manos | con iPhone/iPad |
| Boca, emociones y gestos por la voz (en el dispositivo) | ✅ | ✅ | ✅ |
| Olfateo de gato, orejas, cola y gestos inferidos | ✅ | ✅ | ✅ |
| Escenas, layout y carteles (BRB, «empezamos pronto»…) | ✅ | ✅ | ✅ |
| Vídeo (MKV, SMB/NAS, fototeca) con subtítulos generados y traducidos | ✅ | ✅ | ✅ |
| Capturadora HDMI (UVC) con baja latencia | ✅ captura | ✅ recibe por Wi-Fi | ✅ captura |
| Navegador web | ✅ | ✅ inmersivo | — |
| Cámara virtual «Traigger» y micrófono virtual | envía al Mac | — | ✅ |
| Compartir en FaceTime / SharePlay | ✅ pantalla completa | ✅ ventana para compartir | ✅ cámara virtual |
| Emisión a Twitch / YouTube (RTMP, sin OBS) | ✅ | ✅ | ✅ |
| Fondos 360° / HDRI inmersivos | — | ✅ | — |
| Paquetes de personaje (tu propio VTuber) | ✅ | ✅ | ✅ |

Todo el reconocimiento de voz, emociones y sonidos se hace **en el dispositivo**; nada de tu voz o tu cara sale del aparato salvo lo que tú emitas.

## Empezar

1. Instala **Unity 6000.5.1f1** con los módulos de iOS, visionOS y macOS, y **Xcode 26** o posterior.
2. Descarga las bibliotecas de terceros que no van en el repositorio (VLCKit y, opcional, MediaPipe). Las baja de su web oficial, comprueba su firma SHA-256 y las coloca en su sitio:

   ```bash
   Tools/fetch_thirdparty.sh
   ```

   Detalles, licencias y cómo sustituirlas por tu propia versión: [docs/TERCEROS.md](docs/TERCEROS.md).
3. Abre la carpeta del proyecto en Unity Hub y la escena `Assets/Traigger/Scenes/TraiggerStudio.unity`.
4. Pulsa Play para probar en el editor (con **Traigger Control**, la app de Mac de `NativeUI/Control`, tienes la misma interfaz que en el iPhone).
5. Para compilar en cada plataforma sigue [docs/COMPILAR.md](docs/COMPILAR.md). Necesitas tu propia cuenta de desarrollador de Apple para firmar.

## Tu propio personaje

Un personaje es una carpeta con sus piezas en PNG (cabeza, ojos, boca, pelo, brazos…) y un `character.json` que dice dónde va cada una y qué huesos la mueven. No hace falta Unity: se importa desde la propia app (Inicio → Personaje → Importar). Empieza por la **plantilla libre** [`Examples/Plantilla`](Examples/Plantilla) (CC0: úsala como quieras) y sustituye cada PNG por tu dibujo; la guía está en [docs/PAQUETES_DE_PERSONAJE.md](docs/PAQUETES_DE_PERSONAJE.md). `Examples/Traigger` es solo una referencia técnica: Traigger no se puede reutilizar.

## Estructura

| Carpeta | Contenido |
|---|---|
| `Assets/Traigger/Runtime` | El estudio en Unity (C#): rig, seguimiento, emociones, escenas, salidas |
| `Assets/Traigger/Plugins/Apple` | Código nativo compartido: reproductor (libVLC), capturadora, emisión RTMP, clips, subtítulos |
| `Assets/Traigger/Plugins/Apple/UI` | La interfaz SwiftUI, igual en iPhone/iPad, Vision Pro y Mac |
| `Assets/Traigger/Plugins/iOS`, `visionOS` | Partes propias de cada plataforma |
| `Assets/Traigger/Editor/VisionHost~` | Host nativo de Vision Pro (ventanas, espacios inmersivos, SharePlay) |
| `MacCamera` | Cámara virtual (CMIO), micrófono virtual (HAL) y app «Traigger Camera» del Mac |
| `NativeUI/Control` | Traigger Control: la interfaz para el editor de Unity en el Mac |
| `Examples` | `Plantilla` (CC0, para empezar el tuyo) y `Traigger` (referencia técnica, todos los derechos reservados) |
| `docs` | Documentación |

Más detalle en [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md).

## Documentación

Todo está en [`docs/`](docs/README.md): [guía de uso](docs/GUIA_DE_USO.md), [compilar](docs/COMPILAR.md), [bibliotecas de terceros](docs/TERCEROS.md), [personajes](docs/PAQUETES_DE_PERSONAJE.md), [outfits](docs/TRAJES.md), [skins 3D](docs/PIELES_3D.md), [arquitectura](docs/ARQUITECTURA.md), [red y protocolos](docs/RED_Y_PROTOCOLOS.md) y [solución de problemas](docs/SOLUCION_DE_PROBLEMAS.md).

## Colaborar

Ideas, errores, traducciones y personajes nuevos son bienvenidos: lee [CONTRIBUTING.md](CONTRIBUTING.md).

## Licencias

- Código: **MIT**, ver [`LICENSE`](LICENSE). Úsalo, modifícalo y publícalo libremente, también en tus propios proyectos.
- **Traigger** (personaje, nombre y arte): **todos los derechos reservados**, ver [`LICENSE-ART.md`](LICENSE-ART.md). No se permite usarlo ni hacerse pasar por él.
- Plantilla `Examples/Plantilla`: CC0 1.0 (dominio público).
- Terceros: VLCKit / libVLC (LGPL 2.1, enlazado dinámicamente) y MediaPipe (Apache 2.0, opcional). No se incluyen: se descargan con `Tools/fetch_thirdparty.sh`. Ver [docs/TERCEROS.md](docs/TERCEROS.md).
