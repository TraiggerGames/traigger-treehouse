# Prueba experimental USB + frontal

Esta versión añade una prueba de hardware de dos entradas en una sola AVCaptureMultiCamSession. No incorpora MediaPipe, ARKit facial simultáneo ni interpolación nueva. La frontal se mide pero no se muestra ni se analiza; el vídeo USB puede seguir viéndose como fondo del avatar. No se guarda vídeo ni se envía al exterior mediante este modo.

## Ejecutar

1. Exporta una nueva versión desde Unity e instálala mediante Xcode en el iPad. Un proyecto Xcode exportado anteriormente no contiene estos cambios.
2. En Seguimiento, selecciona Frontal · cara para dejar fuera las sesiones de seguimiento corporal. La prueba pausará ARKit mientras esté activa.
3. En Capturadora, desactiva Enviar a Vision Pro. Activa **Experimental · probar USB + frontal**.
4. Selecciona la capturadora y pulsa Capturar. Si ya estaba capturando, el cambio de modo la reconfigura.
5. Lee Estado durante al menos un minuto, inicialmente sin abrir ventanas adicionales. En esta prueba se omite el audio aunque el ajuste normal Con sonido estuviera encendido.
6. Apaga la prueba para volver al modo normal. Detener Capturar recupera el seguimiento facial sólo si seguía solicitado.

La opción Priorizar expresiones completas queda deshabilitada mientras la prueba está seleccionada, ya que aquí se mide una pareja de vídeo y no se ejecuta ningún estimador facial.

## Interpretación

- **No anuncia una pareja compatible:** iPadOS no ha incluido USB y frontal juntas en supportedMultiCamDeviceSets. No se fuerza la combinación ni se utiliza una alternativa oculta. Se registra la lista de dispositivos y conjuntos para revisión.
- **USB … fps (…); frontal … fps (…)**: son fotogramas efectivamente recibidos de cada salida, no los fps solicitados al configurar. Los totales deben seguir creciendo. El estado muestra también formatos y coste de hardware.
- **Prueba detenida / flujo detenido:** no llegó una de las entradas o una se quedó sin imágenes. El diagnóstico incluye totales y tiempo desde la última imagen. Espera ocho segundos de arranque y considera congelado un flujo si después lleva más de tres segundos sin fotogramas. No se interpreta como incompatibilidad universal ni se reinicia automáticamente.
- La pausa de segundo plano conserva la sesión. Al regresar se reinicia el margen de medición; el minuto de prueba debe hacerse con la app activa.

Objetivos iniciales: USB cercano a 720p y frontal cercana a 480p, hasta 30 fps respetando los formatos reales. Ambos formatos deben admitir MultiCam. Las entradas y conexiones se añaden secuencialmente; se comprueba el presupuesto hardware antes de arrancar. No se abre una segunda sesión ni se cambia de cámara por turnos.

Un minuto con ambas entradas continuas acredita sólo esta prueba de vídeo. Integrar después un estimador, renderizar a mayor frecuencia y validar el coste sostenido y la precisión de expresiones sigue siendo trabajo posterior.

## Validación realizada

Comprobación de tipos del código real de captura/transporte/emisión para iOS y macOS. Comprobación aislada del control SwiftUI modificado con el modelo TraiggerStore y su extensión de reproducción reales. Prueba ejecutada de la política: pareja en el mismo conjunto, rechazo de conjuntos separados, arranque con margen, frontal ausente, USB ausente, primer fotograma congelado y dos flujos saludables. Todas pasan.

Pendiente: exportación y enlace de la aplicación completa, instalación y prueba de cámaras en el iPad físico. Los contadores no son resultados medidos aquí; aparecerán al ejecutar esta nueva versión.
