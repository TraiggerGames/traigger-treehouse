# Investigación: vídeo continuo y cara interpolada en un solo iPad

Revisión de documentación oficial de Apple y Google y del código de Traigger. No se ha integrado un estimador nuevo ni se ha medido el rendimiento en el iPad físico.

## Recomendación

La alternativa con mejor fundamento es una única AVCaptureMultiCamSession con USB + frontal, si el dispositivo admite esa pareja, y estimación facial RGB mediante MediaPipe Face Landmarker. Separar la frecuencia de inferencia de la captura y del render permite reducir trabajo sin arrancar y parar cámaras. No equivale a mantener ARKit/TrueDepth completo; son expresiones estimadas desde imagen.

El paso previo obligatorio es consultar supportedMultiCamDeviceSets en una DiscoverySession que incluya la cámara externa y la frontal. Ambas deben aparecer juntas en un mismo conjunto compatible; no basta con que el iPad indique isMultiCamSupported. También hay que verificar formatos MultiCam de ambos dispositivos, conexiones, hardwareCost y llegada continua de ambos vídeos. Si la pareja no está admitida, este diseño no puede darse por viable en ese iPad y accesorio.

No se encontró una garantía pública de Apple que permita afirmar que cualquier capturadora USB funciona con cualquier frontal. Las capturas proporcionadas anteriormente mostraron además motivo 1 (segundo plano), lo que por sí solo no prueba incompatibilidad de la pareja física.

## Qué aporta MediaPipe

La guía oficial iOS acepta CVPixelBuffer/CMSampleBuffer y dispone de modo LIVE_STREAM asíncrono. Entrega puntos faciales, coeficientes de expresión y matriz de transformación. Con una cara activa habilita suavizado y utiliza seguimiento para evitar ejecutar la detección inicial en todos los fotogramas.

La lista estándar del código fuente tiene 52 etiquetas, incluida _neutral. Comparte con nuestro búfer nombres de cejas, parpadeos, direcciones de ojos, apertura de mandíbula, sonrisa, fruncido, carrillos y noseSneerLeft/Right. Esta coincidencia facilita el adaptador, pero no garantiza amplitudes, precisión ni equivalencia con ARKit. La pose requiere calibrar ejes, orientación y espejo; no copiar índices de forma ciega.

La lista estándar no incluye tongueOut. La lengua necesitaría un detector adicional o activación manual; no se debe vender como paridad completa con ARKit. El rendimiento y precisión de mirada, nariz, carrillos y gestos rápidos deben compararse en condiciones reales de luz/pose. No requiere abrir ARSession para inferir esos coeficientes desde el vídeo recibido.

## Diseño propuesto, todavía sin implementar

- Sesión única, cámaras continuamente activas; ninguna conmutación por fotograma.
- USB: objetivo inicial 720p/30 fps; frontal con un formato de baja resolución compatible, inicialmente a 30 fps.
- Estimador facial: objetivo 20 resultados/s, una cara y como máximo una inferencia en curso; no acumular una cola de imágenes antiguas. Reducir frecuencia si no llega a tiempo.
- Traigger: objetivo 60 fps con marcas de tiempo y suavizado por canal. Filtrar menos boca/parpadeo que cabeza y hocico. Ante pérdida prolongada de cara, volver gradualmente al reposo.
- Comparar 15, 20 y 30 muestras/s. A 20 hay 50 ms entre muestras; a 15 hay 66,7 ms. Son periodos matemáticos, no medidas de latencia real. Interpolar estrictamente entre dos muestras requiere esperar la siguiente y añade demora; filtrado/predicción breve reducen esa espera a cambio de error. No presentar 60 fps dibujados como 60 muestras reales.
- Las animaciones físicas de pelo/orejas siguen ejecutándose a la frecuencia de render. La interpolación no recupera un parpadeo o gesto que el estimador no llegó a observar.

Estos valores son objetivos de prueba, no prestaciones acreditadas. Medir vídeo entregado, resultados faciales por segundo, antigüedad de muestras, cola, latencia, pérdida de cara y calentamiento durante una sesión sostenida antes de aprobar el modo.

## Hallazgos del código actual

makeMultiCam no consulta supportedMultiCamDeviceSets. Comprueba que se puedan añadir ambas entradas antes de añadir ninguna; debe comprobar la segunda después de añadir la primera. La frontal elige la máxima frecuencia del formato pequeño, hasta 60 fps, aunque el comentario diga 30. El estimador Vision actual no proporciona los coeficientes completos de ARKit. El arranque de ARKit actual utiliza ResetTracking y RemoveExistingAnchors: alternarlo repetidamente no conserva de forma continua su estado facial.

## Quick Relay por turnos

No lo elegiría como diseño principal. Apple documenta que arrancar/parar captura puede bloquear y tardar. Bajar la frecuencia de expresiones no elimina el coste de liberar y readquirir las cámaras. Conservar el último vídeo o interpolar la pose puede ocultar parte de los huecos, pero no producir vídeo nuevo durante el turno facial. Sólo tendría sentido como prueba separada que mida tiempos de transición y primer resultado válido, sin prometer vídeo continuo.

## Fuentes primarias

- Apple, conjuntos de dispositivos MultiCam: https://developer.apple.com/documentation/avfoundation/avcapturedevice/discoverysession/supportedmulticamdevicesets
- Apple, captura MultiCam: https://developer.apple.com/documentation/avfoundation/avcapturemulticamsession/
- Apple, coste de arranque y parada: https://developer.apple.com/videos/play/wwdc2026/303/
- Google, guía iOS Face Landmarker: https://developers.google.com/edge/mediapipe/solutions/vision/face_landmarker/ios
- Google, lista estándar de expresiones: https://github.com/google-ai-edge/mediapipe/blob/master/mediapipe/tasks/cc/vision/face_landmarker/face_blendshapes_graph.cc
