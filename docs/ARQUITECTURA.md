# Arquitectura

```
          ┌──────────────── Interfaz SwiftUI (Plugins/Apple/UI) ────────────────┐
          │  iPhone/iPad (en el proceso) · Vision Pro (host nativo) · Mac (TCP)  │
          └───────────────┬──────────────────────────────▲───────────────────────┘
                 órdenes {cmd,i,f,b,s}            estado JSON (~5/s)
                          ▼                              │
┌──────────────────────── Unity (Runtime) ─────────────────────────────────────────┐
│ StudioController: rig, seguimiento, voz → emoción, escenas, layout, carteles     │
│ TraiggerBridge: transporte de órdenes y estado                                   │
│ Salidas: VirtualCameraOutput · StreamOutput · AvatarOverlayFeed                  │
└───────┬──────────────────────┬───────────────────────────┬───────────────────────┘
        │ fotogramas            │ fotogramas + sonido       │ imagen recortada con alfa
        ▼                       ▼                           ▼
 Cámara «Traigger» (Mac)  TraiggerStream (RTMP)     Capas nativas (iPad) /
 por Wi-Fi o cable        Twitch · YouTube          ventana para compartir (Vision Pro)
```

## Piezas principales

| Pieza | Dónde | Qué hace |
|---|---|---|
| Rig 2D | `RigDefinition`, `AutoRig`, `AvatarRig` | Huesos + piezas PNG → mallas con pesos y blendshapes (parpadeo, boca, giros) |
| Paquetes de personaje | `CharacterPack.cs` | Carga personajes desde `character.json` + PNG ([guía](PAQUETES_DE_PERSONAJE.md)) |
| Seguimiento | `ARKitFaceSource`, `FaceReceiver`, `VisionMotionInput`, `TraiggerBodySensor` | Cara (TrueDepth), cuerpo, manos, Vision Pro |
| Voz | `MicMouth`, `VoiceEmotion`, `SniffDetector`, `TraiggerListen` | Boca, emoción, palabras (Speech + Apple Intelligence), olfateo |
| Composición | `StudioController.Layout/Scenes`, `StreamCards`, `MoviePlayer`, `CaptureBackground`, `BackgroundImage` | Capas con posición, escenas y carteles |
| Vídeo | `TraiggerMoviePlayer.m` (libVLC 4) | Reproducción, pistas, SMB; el sonido va a un motor propio |
| Capturadora | `TraiggerCapture.swift` | UVC por USB-C y envío H.264/HEVC de baja latencia a Vision Pro |
| Emisión | `TraiggerStream.swift` | Cliente RTMP propio, H.264 + AAC, mezcla de voz, película y capturadora |
| Vision Pro | `Editor/VisionHost~/Sources` | Ventanas, espacios inmersivos, HDRI, Traigger libre, ventana para compartir |
| Mac | `MacCamera` | Extensión CMIO, controlador de audio HAL, puente por cable (usbmuxd) |

## Reglas del proyecto

- Toda función nueva existe en **todas las plataformas** posibles (iPhone/iPad, Vision Pro y Mac).
- La interfaz es **SwiftUI nativa**; OnGUI solo existe dentro del editor de Unity para depurar.
- Unity solo usa los módulos imprescindibles (sin física): ver `Packages/manifest.json`.
- Todo el procesamiento de voz, cara y sonido es **en el dispositivo**.
