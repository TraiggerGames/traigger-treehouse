# Capturadora USB y seguimiento facial en iPhone/iPad

Revisión del 25 de octubre de 2026. Se inspeccionó el código actual y documentación pública de Apple. No se reprodujo el fallo en un iPhone/iPad físico, ni se confirmó modelo de capturadora, versión exacta del sistema o ruta USB. No se ha cambiado el comportamiento de las sesiones de cámara en esta investigación.

## Conclusión

Hay una restricción documentada sobre varias sesiones de captura simultáneas en iOS. Eso hace plausible el conflicto entre una ARSession facial y una sesión independiente para vídeo USB. Sin embargo, no prueba que toda capturadora USB sea incompatible con seguimiento facial en todo iPhone/iPad. La aplicación actual fuerza además la pausa de ARKit en una de sus rutas, e intenta sustituirlo por estimaciones 2D de Vision. Hay que distinguir ambas causas.

La tecnología utilizada para animar a Traigger es ARKit/TrueDepth; no la autenticación Face ID. Una entrada HDMI/UVC se presenta a AVFoundation como cámara externa, aunque no utilice la óptica frontal del dispositivo.

## Evidencia en nuestro código

- `Plugins/Apple/TraiggerCapture.swift`, `startLocked`, líneas 100–117: el modo normal intenta capturadora + frontal mediante MultiCam/Vision. Al enviar a Vision Pro se omite este intento salvo que `faceWhileSending` sea true (su valor predeterminado es false).
- `makeMultiCam`, líneas 207–211: llama `TraiggerFaceSetExternal(1)` ANTES de comprobar compatibilidad, dispositivos y entradas.
- `Plugins/iOS/TraiggerFaceTracking.mm`, líneas 136–154: esa llamada establece `gExternal`, marca una interrupción y ejecuta `[gTracker.session pause]`. Mientras la bandera está activa, `TraiggerFaceStart` retorna sin arrancar ARKit.
- `TraiggerVisionFace.process` crea un búfer de 40 ceros y sólo rellena pose, boca, sonrisa, mirada aproximada, presencia y parpadeo automático. No produce los coeficientes de nariz, lengua, ceño o carrillos de ARKit. La pérdida de estas muecas es esperable al sustituir la fuente.
- «Forzar Face ID», líneas 136–147: activa `isMultitaskingCameraAccessEnabled` si se admite y vuelve a pedir ARKit tras un segundo. Apple describe esa propiedad para acceso durante multitarea; no garantiza dos sesiones independientes concurrentes. Reiniciar en distinto orden no es una solución acreditada.

## Problemas de implementación que requieren revisión

1. La pausa de ARKit se programa de forma asíncrona en el hilo principal, pero la configuración de la capturadora continúa en otra cola sin esperar a que la pausa termine. Existe una posible carrera de arranque.
2. Si MultiCam falla, se vuelve a pedir ARKit y se inicia la sesión USB normal. Ambas operaciones pueden competir; el vigilante de la capturadora puede reiniciar después la captura.
3. Los observadores de error/interrupción de AVCaptureSession usan `object: nil`, por lo que no están acotados a la sesión de la capturadora. Un error de otra sesión puede provocar una recuperación incorrecta.
4. El mensaje de interrupción de ARKit atribuye siempre el problema a otra captura, sin registrar el error completo. `didFailWithError` sólo trata permiso denegado. Ese mensaje no demuestra por sí mismo la causa.
5. La ruta MultiCam debe comprobar la combinación concreta de dispositivos y formatos, incluyendo `supportedMultiCamDeviceSets`, en el aparato real. `isMultiCamSupported` por sí solo no confirma capturadora USB + frontal.

Son observaciones del código, no una reproducción física ni prueba de que arreglarlas haga coexistir ARKit y USB.

## Qué dice Apple

- [Introducing Multi-Camera Capture for iOS, WWDC19](https://developer.apple.com/videos/play/wwdc2019/249/): explica que iOS admite múltiples cámaras dentro de una sesión MultiCam y documenta la restricción de varias sesiones con varias cámaras. Es una fuente histórica explícita; no una prueba del comportamiento de cada accesorio y versión actual.
- [AVCaptureMultiCamSession](https://developer.apple.com/documentation/avfoundation/avcapturemulticamsession/): API pública para varias entradas simultáneas, con restricciones de hardware y presión del sistema.
- [ARConfiguration](https://developer.apple.com/documentation/arkit/arconfiguration): ARKit administra su propio flujo de captura. No se encontró una API pública documentada para insertar una ARSession facial dentro de nuestra sesión MultiCam o entregarle arbitrariamente los fotogramas de una sesión propia.
- [isMultitaskingCameraAccessEnabled](https://developer.apple.com/documentation/avfoundation/avcapturesession/ismultitaskingcameraaccessenabled): acceso durante multitarea; no declara habilitar convivencia de dos sesiones independientes.
- [Support external cameras in your iPadOS app, WWDC23](https://developer.apple.com/videos/play/wwdc2023/10106/): soporte UVC en iPad USB-C. No certifica esta combinación con ARKit, ni debe extrapolarse automáticamente a todos los iPhone.

## Prueba que resolvería la duda del dispositivo

Preparar una prueba pequeña sin Unity, Vision, codificación, envío ni audio. Registrar modelo/sistema/accesorio, dispositivos y formatos, motivo y objeto de cada interrupción, errores completos y número de fotogramas/anchors por segundo. Comparar: ARKit solo; USB solo; ambos con ARKit primero; ambos con USB primero; una sola MultiCam USB + frontal con formatos admitidos y carga reducida. Observar ambos flujos durante al menos un minuto, no sólo que las llamadas de inicio retornen.

La prueba debe evitar pausas automáticas nuestras y bucles de reinicio que oculten el resultado. Si el sistema impide dos sesiones en ese aparato, una combinación MultiCam + estimación facial externa puede conservar seguimiento aproximado, pero no equivale a los coeficientes ARKit. Para conservar todas las muecas, una arquitectura con ARKit en un iPhone dedicado y la capturadora en otro equipo evita competir por las cámaras del mismo dispositivo; requiere integrar y probar el envío de coeficientes por red.
