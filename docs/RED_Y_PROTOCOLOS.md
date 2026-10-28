# Red y protocolos

Referencia para quien quiera depurar, escribir un emisor/receptor propio o portar Traigger. Todo va por la **red local**
(Bonjour) o por cable; nada pasa por internet salvo la emisión RTMP y el vídeo de URLs remotas.

## Servicios Bonjour

Declarados en `NSBonjourServices` por `Editor/IOSBuild.cs` (iOS), `VisionOSBuild.cs` (visionOS) y los `Info.plist` del Mac.

| Servicio | Quién anuncia → quién busca | Para qué | Código |
|---|---|---|---|
| `_traigger-cam._tcp` | Mac (extensión de cámara) ← iPhone/iPad | Vídeo de Traigger a la cámara virtual por Wi-Fi | `Plugins/iOS/TraiggerMacCamera.swift`, `MacCamera/Extension/PhoneReceiver.swift` |
| `_traigger-audio._tcp` | Mac ← iPhone/iPad | Sonido al micrófono virtual por Wi-Fi | `TraiggerMacCamera.swift`, `MacCamera/App/AudioMixer.swift` |
| `_traigger-sensor._udp` | Vision Pro ← iPhone/iPad | Cuerpo y manos (LiDAR/Vision) hacia Vision Pro | `Plugins/iOS/TraiggerBodySensor.swift` |
| `_traigger-face._udp` | Cualquier Traigger ← emisor de cara | «Cara desde otro aparato» | `Plugins/iOS/TraiggerFaceRelay.swift`, `MacCamera/App/FaceForwarder.swift` |
| `_traigger-hdmiu._udp` | Receptor (iPad «Traigger iPad», Vision Pro) ← emisor | Capturadora HDMI (UDP, el normal) | `Plugins/Apple/TraiggerLink.swift`, `TraiggerCapture.swift`, `TraiggerMacReceiver.swift` |
| `_traigger-hdmi._tcp` | Receptor ← emisor | Capturadora HDMI por TCP (alternativa) | igual |
| `_smb._tcp` | Servidores de archivos ← Traigger | Descubrir NAS para el vídeo | `UI/TraiggerFileBrowser.swift` |

En iOS, la primera vez el sistema pide permiso de **Red local**; sin él nada de esto funciona.

## Puertos fijos

| Puerto | Protocolo | Uso |
|---|---|---|
| 47815 | TCP (por USB, usbmuxd) | iPhone/iPad → Mac por cable: vídeo y sonido para la cámara y el micrófono virtuales (`MacCamera/App/CableBridge.swift`) |
| 47820 | TCP local | Unity (editor o Traigger para Mac) ↔ **Traigger Control** (`Runtime/TraiggerBridge.cs`) |
| 49983 | UDP local | Receptor de cara de Unity (`FaceReceiver.cs`); los relés de cara le reenvían lo que llega por Bonjour |
| 49984 | UDP | Movimiento de Vision Pro (`MotionRelayReceiver.cs`) |

## Puente interfaz ↔ Unity

La interfaz SwiftUI manda **órdenes** y recibe el **estado** (JSON, unas 5 veces por segundo):

```json
{ "cmd": "movie.volume", "i": 80, "f": 0, "b": false, "s": "" }
```

En Swift: `store.send("movie.volume", i: 80)` (los argumentos van en el orden `i:`, `f:`, `b:`, `s:`). En iPhone/iPad y
Vision Pro va dentro del proceso; en el Mac, por el puerto 47820. Las órdenes se atienden en
`Runtime/StudioController.Bridge.cs` (y `.Scenes.cs`). Familias principales:

| Prefijo | Ejemplos |
|---|---|
| `movie.*` | `browse`, `open`, `pause`, `seek`, `skip`, `stop`, `volume`, `audio`, `subtitles`, `generated`, `translate`, `scale`, `opacity`, `background` |
| `capture.*` | `start`, `stop`, `quality`, `vision`, `audio`, `front`, `opacity`, `scale`, `window`, `background` |
| `stream.*` | `start`, `stop`, `gains` |
| `web.*` | `open`, `page`, `show`, `overlay`, `immersive` |
| `scene.*`, `card.*` | `scene.save`/`apply`/`delete`; `card.show`/`hide`/`text`/`image`/`cover` |
| `character.*`, `outfit.*` | `character.load`/`import`/`delete`; `outfit.select`, `outfit.option` |
| otros | `expression`, `layout`, `tracking`, `sensors`, `recenter`, `calibrate`, `control` (deslizadores de Ajustar), `do:*` (acciones) |

## Formato del vídeo HDMI (UDP)

Cada mensaje (fotograma, sonido o ping) se trocea en datagramas:

```
[ 'T' ][ tipo ][ trozo UInt16 ][ total UInt16 ][ id del mensaje UInt32 ][ datos ≤ 1200 bytes ]
```

Todos los enteros en big endian. Tipos: `2` vídeo (H.264/HEVC), `3` sonido, `4` ping (`[4][UInt64 hora en ns]`, el
receptor lo devuelve igual). El receptor pide un fotograma clave enviando un datagrama de tipo `'K'`. Un mensaje
incompleto se descarta. Detalle en `Plugins/Apple/TraiggerLink.swift` y los receptores (`TraiggerMacReceiver.swift`,
`Editor/VisionHost~/Sources/TraiggerHDMIReceiver.swift`).
