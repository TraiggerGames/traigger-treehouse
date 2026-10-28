# Guía de uso

Cómo se usa Traigger una vez instalado. La interfaz es la misma en iPhone/iPad, Vision Pro y Mac (en el Mac, con la app
**Traigger Control**; `Esc` la abre desde Traigger para Mac). Las capturas y nombres de botones están en español, como la app.

## La barra lateral

| Pestaña | Para qué sirve |
|---|---|
| **Inicio** | Expresión, encuadre, posición de Traigger, personaje y outfit, y el estado en vivo (boca, cara, frases) |
| **Fuentes** | Lo que se ve detrás o junto al personaje: vídeo, capturadora HDMI, web, fondo. Escenas, layout y carteles |
| **Ajustar** | Deslizadores finos: física de orejas y cola, pelo, micrófono, muecas… Se guardan en el aparato |
| **Seguimiento** | Cámara, cuerpo, Vision Pro, recentrar y calibrar, y «Cara desde otro aparato» |
| **Voz** | Emoción por la voz, idioma de las frases, perfil de tu voz (sincronizado por iCloud) |
| **Directo** | Emitir a Twitch, YouTube u otro servidor RTMP |
| **Ajustes** | Fondo, cámara virtual del Mac e interfaz |

## Personaje y outfit

- **Inicio → Personaje:** elige entre los personajes instalados o **Importar…** un paquete `.traigger` o una carpeta
  ([cómo se hace uno](PAQUETES_DE_PERSONAJE.md)). Los **skins 3D** aparecen aquí también (experimentales,
  [más información](PIELES_3D.md)).
- **Inicio → Outfit:** cambia la ropa del personaje sin duplicarlo (por ejemplo, el traje de calabaza de Traigger).
  Algunos outfits tienen opciones, como **Capucha**. Al cambiar de outfit el personaje se vuelve a montar (tarda un
  segundo). Cómo crear uno: [TRAJES.md](TRAJES.md).

## Seguimiento de la cara

- **iPhone/iPad con Face ID:** la cámara TrueDepth sigue cara, ojos, boca y lengua (ARKit) en el propio aparato.
- **Cara desde otro aparato** (Seguimiento): un iPhone o iPad puede **enviar su cara** a otro aparato con Traigger (por
  ejemplo, el iPhone sujeto al monitor y Traigger en el iPad o en el Mac). Activa «Enviar mi cara» en el emisor y elige
  el destino («El primero que aparezca» o uno concreto). En el Mac lo recibe la app **Traigger Camera**.
- **Recentrar:** mira de frente y pulsa **Recentrar cabeza** (Seguimiento). **Calibrar boca cerrada:** relaja los labios
  y púlsalo para que la boca no se quede entreabierta. La **intensidad de las muecas** está en Ajustar → Cara (por
  defecto 1,25).
- **Modo espejo:** apagado por defecto; está en Ajustar → Cámara de cara, junto a «Invertir giro».
- **Vision Pro:** no hay cámara de cara para apps, así que usa cabeza y manos y la voz para la boca y la expresión.

## Voz

Todo se procesa en el aparato: la boca sigue el volumen y las vocales, la **emoción por la voz** cambia la expresión
según la entonación y las palabras, y se detectan sonidos como el olfateo. En **Voz → Tu voz** puedes entrenar tu perfil
(cada emoción se calibra hablando con ese tono) para que la detección se ajuste a ti; se sincroniza por iCloud entre tus aparatos.

## Fuentes (solo una activa a la vez)

Al activar una fuente se apaga la anterior: si pones un vídeo, la capturadora se detiene, y al revés.

### Vídeo

1. **Fuentes → Vídeo → Explorar.** Elige **Archivos…**, **Fotos…** o un servidor de la red.
2. **Servidores SMB/NAS:** «Añadir servidor» busca solos los equipos de la red (Bonjour); pon usuario y contraseña si
   hacen falta. Si un servidor falla aunque la red funcione, Traigger reintenta solo dos veces y te dice el motivo:
   «no respondió a tiempo» (equipo dormido o fuera de la red) o «rechazó la conexión» (usuario, contraseña o ruta).
   Usar la IP fija del servidor en lugar de su nombre `.local` suele evitar los fallos al despertar.
3. **Opciones:** pista de audio y de subtítulos, subtítulos de un archivo de la carpeta, **subtítulos generados por
   Apple** (y traducidos), volumen, tamaño, opacidad, y si va **de fondo detrás de Traigger** o en su propia ventana.

El vídeo usa libVLC (MKV, HEVC, AC3, subtítulos ASS…), lee 3 s por delante en red y 1,5 s en el aparato, y su sonido
pasa por un colchón propio de 0,25 s que se mantiene sincronizado con la imagen (también con AirPods y altavoces
Bluetooth). Suena aunque el interruptor de silencio esté puesto.

### Capturadora HDMI

- **iPhone/iPad con USB-C** o **Mac:** conecta una capturadora UVC y elígela en **Fuentes → HDMI**.
- **Enviar a Vision Pro / iPad:** la imagen se codifica (H.264 o HEVC, 720p/1080p, 30/60 fps, perfiles Juego, Calidad,
  Ahorro o A medida) y se envía por la red local. El estado muestra la latencia de cada destino.
- **En el Mac**, la app **Traigger Camera** tiene una estación de captura: elige la capturadora, el destino (iPad,
  Vision Pro o ambos) y activa «Enviar».
- **Seguir mi cara mientras envío:** con la cámara frontal ocupada, el modo híbrido sigue la cara con MediaPipe (si lo
  instalaste, ver [TERCEROS.md](TERCEROS.md)).

### Web

**Abrir el navegador**: se muestra debajo de Traigger como una capa más. En Vision Pro puede ir en pantalla grande
inmersiva. Guarda tus páginas en **Favoritos**.

### Fondo

Color (oscuro, verde para croma, transparente), una imagen, o en Vision Pro una **imagen 360° / HDRI inmersiva**.

## Escenas, layout y carteles

- **Layout:** dónde va Traigger respecto a la fuente: centro, junto a la película (como en una llamada) o en una esquina,
  y el formato (16:9, vertical o pantalla).
- **Escenas:** «Guardar como escena» guarda fuente, layout, fondo y carteles; se aplican con un toque. Se sincronizan por
  iCloud entre iPhone, iPad, Vision Pro y Mac.
- **Carteles:** «Volvemos enseguida», «Empezamos pronto»… con texto e imagen propios, y portada.
- **Editor de escena:** mueve y escala cada capa con los dedos.

## Salidas

- **Cámara virtual «Traigger» en el Mac:** instala **Traigger Camera** (ver [COMPILAR.md](COMPILAR.md)), usa su menú
  *Instalar cámara* / *Instalar micrófono*, y elige «Traigger» en FaceTime, Zoom, Meet, OBS… El iPhone/iPad envía la
  imagen por Wi-Fi o, si hay cable, por USB.
- **SharePlay / FaceTime:** «Empezar el modo compartir» (pantalla completa en iPhone/iPad, ventana para compartir en
  Vision Pro).
- **Directo (RTMP):** elige Twitch, YouTube u «Otro», pega tu clave de emisión y la calidad (720p/1080p, 30/60). Se
  mezclan tu voz, el vídeo y la capturadora; el nivel de cada uno (micrófono, vídeo, capturadora) se ajusta por separado.

## Ajustar el pelo por mechones

**Ajustar → Pelo**: recorrido del flequillo y de los pelillos de arriba, curva de las puntas, rigidez, amortiguación,
variedad entre mechones y la reacción a girar, inclinar, asentir y moverte de lado. **Restablecer el pelo** vuelve a los
valores de fábrica. Todo se guarda en el aparato.

## Sincronización con iCloud

Con la misma cuenta de iCloud se comparten escenas, carteles, el perfil de voz y los archivos de iCloud Drive usados en
escenas (rutas `icloud://`). Requiere que la app esté firmada con iCloud activado (ver [COMPILAR.md](COMPILAR.md)).

Si algo no va como esperas: [SOLUCION_DE_PROBLEMAS.md](SOLUCION_DE_PROBLEMAS.md).
