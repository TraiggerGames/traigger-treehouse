# Traigger en llamadas: estado y pruebas

Actualizado el 20 de octubre de 2026. Prioridad: Mac y Apple Vision Pro.

## Qué se puede usar

| Plataforma de la llamada | Salida del avatar | Estado real |
|---|---|---|
| Mac | Unity → ventana de avatar → OBS → cámara virtual → FaceTime | Ventana y controles implementados. OBS instalado. Falta autorizar captura y validar cámara virtual en FaceTime. |
| Vision Pro | Compartir la ventana de la aplicación en FaceTime | Vía documentada por Apple; no probada físicamente con esta app. No sustituye Persona por Traigger. |
| iPhone / iPad | Compartir pantalla, donde el sistema lo permita | No se ha implementado una cámara virtual para FaceTime en iOS/iPadOS. |
| Windows | Salida Unity y OBS como propuesta | No compilado ni validado en esta entrega. FaceTime se usa mediante enlace web; compatibilidad pendiente. |

En Vision Pro el seguimiento completo de manos de ARKit requiere una sesión inmersiva. Compartir la ventana y mantener esa sesión simultáneamente tiene restricciones del sistema: no se promete que ambas funciones funcionen juntas en una llamada. La opción prioritaria es capturar movimientos con Vision Pro y presentar el avatar desde el Mac.

## Prueba en el Mac

1. Abrir `Assets/Traigger/Scenes/TraiggerStudio-KemonoBody.unity` y pulsar Play.
2. Abrir **Traigger → Llamadas → Ventana de avatar para OBS**. Presenta únicamente al personaje con fondo opaco; no es una salida con alfa ni una cámara virtual por sí sola.
3. En OBS, añadir una captura de ventana de macOS y elegir **Traigger · Cámara**. Recortar la decoración de ventana si aparece. No capturar todo el escritorio para la llamada.
4. Iniciar **Cámara virtual** en OBS. macOS puede solicitar activar el componente de cámara en Ajustes del Sistema. Ese paso aún no está validado aquí.
5. En FaceTime del Mac, seleccionar **OBS Virtual Camera** en el menú Vídeo. Comprobar la vista previa antes de llamar.
6. Elegir el micrófono habitual en FaceTime: la cámara virtual transporta vídeo, no el audio del micrófono.

OBS 32.2.2 para Apple Silicon está instalado en `/Applications/OBS.app`, descargado de su sitio oficial y con firma/notarización verificadas. La grabación de pantalla es necesaria para capturar Unity; cámara, micrófono y monitorización del teclado no son necesarios para esta ruta de vídeo. No se ha iniciado ninguna llamada ni emisión.

En una compilación de escritorio, F8 oculta los controles y Escape los restaura. La ventana flotante descrita arriba pertenece al editor de Unity.

## Manos de Vision Pro → Mac

El host nativo recoge las articulaciones de ARKit y deriva cinco cierres de dedos humanos. El rig de Traigger conserva cuatro dedos kemono: pulgar, índice, medio y un dedo exterior que combina anular/meñique.

1. Hay que volver a exportar y compilar la aplicación de Vision Pro: una instalación anterior no contiene este nuevo envío.
2. En Vision Pro, usar **Conectar sensores con el Mac…** y escribir la IP local del Mac en la ventana **Conexión con el Mac**. Dejar el destino vacío desactiva el envío.
3. En Unity del Mac, con Play activo, abrir **Traigger → Llamadas → Controles y conexión** y pulsar **Recibir Vision Pro por la red local · UDP 49984**.
4. Ambos dispositivos deben estar en una red local de confianza. El envío está desactivado hasta configurar el destino; el receptor se abre únicamente al pulsar el botón. Son datagramas sin cifrado ni autenticación, por lo que no se debe publicar ese puerto en Internet.
5. El panel muestra si llegan datos de cada mano. Las muestras con más de 0,3 segundos, cierres inválidos o articulaciones ausentes se rechazan. Al perder señal los dedos vuelven suavemente al reposo.

La ruta es solo de datos de movimiento; no envía imágenes de cámara. El seguimiento facial existente del iPhone sigue siendo un canal independiente. LiDAR y mandos PSVR2 no se han implementado como fuentes de dedos.

## Dedos: prototipo, no acabado artístico

Se han añadido 12 huesos por mano, pesos y mallas deformables manteniendo los sprites, la muñeca y el rig corporal existentes. Hay control manual y poses de prueba. El movimiento es una flexión 2D limitada, no una reconstrucción tridimensional de los dedos.

**La deformación de dedos está desactivada por defecto.** Se activa en el panel con **Dedos experimentales**. La prueba visual detecta cortes al cerrar: el sprite original no tiene las superficies ocultas necesarias para un puño correcto. No se considera un rig de manos terminado. Para cerrar plenamente, girar la palma y mostrar dorso hacen falta dibujos adicionales con el mismo estilo kemono y nuevas pruebas visuales.

No se ha cambiado el estilo artístico ni se han repintado los recursos originales en esta fase. La escena previa a los dedos está guardada en `Validation/Hands/scene-before-fingers.unity.bak`; no sustituir toda la escena si se quieren conservar modificaciones posteriores.

## Validación

**Traigger → Pruebas → Comprobar dedos y datos de manos** comprueba pesos normalizados, índices y bindposes, deformación finita, datos caducados o malformados y un datagrama UDP real por loopback. Guarda capturas y resultados en `Validation/Hands`. Pasar estas comprobaciones no implica calidad artística.

El código C# compila contra Unity 6000.5.1f1. Sensores y relay Swift se han comprobado con el SDK de visionOS; la interfaz del host se ha comprobado con sustitutos de los componentes de Unity. Eso no equivale a compilar y ejecutar el proyecto completo en Vision Pro.

Pendiente: prueba real entre dispositivos, permisos y activación de cámara virtual, vista previa de FaceTime y validación artística de manos. No hay una llamada completa validada todavía.

## Referencias oficiales

- [OBS: cámara virtual](https://obsproject.com/kb/virtual-camera-guide)
- [OBS: compatibilidad y resolución de problemas](https://obsproject.com/kb/virtual-camera-troubleshooting)
- [Apple: extensiones de cámara de macOS](https://developer.apple.com/documentation/coremediaio/creating-a-camera-extension-with-core-media-i-o)
- [Apple: articulaciones de manos de ARKit](https://developer.apple.com/documentation/arkit/handskeleton/jointname)
- [Apple: compartir la vista en FaceTime de Vision Pro](https://support.apple.com/en-az/guide/apple-vision-pro/tanc2cd081bb/visionos)
