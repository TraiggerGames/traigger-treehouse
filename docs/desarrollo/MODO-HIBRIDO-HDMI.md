# HDMI + cara y mirada en el iPad

## Qué cambia

Se incorpora un modo local MediaPipe Face Landmarker 1.1.1 sobre la sesión MultiCam USB + frontal que ya funcionó en la prueba del iPad. ARKit se pausa antes de abrir esa sesión y vuelve al detenerla si el seguimiento facial seguía solicitado. ARKit y TrueDepth siguen disponibles en el modo normal.

El vídeo HDMI no espera al analizador: hay como máximo una inferencia en curso, sin cola de imágenes acumuladas, con un objetivo seleccionable de hasta 30 análisis por segundo (20 en menor consumo). El rig conserva su interpolación existente. Los fps reales dependen del iPad, del formato de la capturadora y del tiempo de inferencia; no se garantizan 20 ni 30 fps.

Mirada horizontal y vertical independiente por ojo, parpadeos separados, boca, cejas, mofletes y nariz se convierten al mismo búfer de 40 canales que usa el rig ARKit. No se han cambiado ilustraciones, estilo kemono, huesos ni pesos.

La selección de formatos prioriza hasta 30 fps antes de la resolución: evita elegir un formato de 10 fps sólo por coincidir en tamaño. Se reduce la frecuencia si el presupuesto de hardware de la sesión supera 1; si sigue sin caber, la app lo indica y no fuerza la sesión. El contador muestra los fps reales recibidos.

Si sólo deja de llegar la frontal, se mantiene HDMI y se retira la pose facial vieja. Si se detiene USB o la sesión entera, se muestra el fallo sin hacer un bucle continuo de reinicios entre ARKit y USB. Se puede detener y reintentar manualmente.

## Instalar y probar

1. En Unity espera a que termine la importación. Sal del modo Play.
2. Usa **Traigger → 4 · Compilar iPhone / iPad (proyecto de Xcode)**. Es necesaria una nueva exportación; recompilar el proyecto Xcode antiguo por sí solo no incorpora todos los cambios.
3. Abre Builds/iOS/Unity-iPhone.xcodeproj, selecciona el iPad y ejecuta. MediaPipe y el modelo se incluyen automáticamente, sin CocoaPods ni descargas al abrir la app.
4. En Capturadora, desactiva **Enviar a Vision Pro**. Activa **HDMI + cara y mirada · MediaPipe**. Ese interruptor selecciona el modo de cara y desactiva la prueba sin análisis; Priorizar expresiones completas queda deshabilitado mientras se usa el híbrido.
5. Pulsa Capturar con buena iluminación. Comprueba que avanzan los contadores de USB y frontal y que aparece MediaPipe con Hz y milisegundos.
6. Mira al centro y pulsa **Centrar mirada**. Ajusta Amplitud de mirada. Si un eje responde al revés, usa su interruptor de inversión. Prueba mover únicamente los ojos, guiñar cada ojo, abrir la boca y girar la cabeza. La calibración se aplica cuando se recibe una cara válida.
7. Primero prueba un minuto sin sonido; después activa Con sonido y repite. Prueba fondo y ventana, vertical/horizontal, pérdida y recuperación de la cara y detener/iniciar.
8. Detén la captura: confirma que vuelve ARKit y que funcionan las expresiones completas habituales.

## Límites reales

El modelo procesa imagen de color. No usa el vídeo infrarrojo ni el mapa TrueDepth y necesita luz. La mirada es una estimación, pendiente de calibración y pruebas físicas; no equivale a la precisión de TrueDepth. El modelo estándar no mide tongueOut: la lengua manual sigue disponible y el seguimiento automático se conserva en ARKit.

Esta primera integración está dedicada al uso local en el iPad. No permite simultáneamente el envío a Vision Pro en este modo y conserva por separado la prueba USB + frontal sin análisis. La entrada de audio USB utiliza la ruta de sonido existente; su estabilidad con la inferencia aún debe probarse en hardware.

## Validación realizada

- Comprobación de Swift con el SDK real de iOS y los frameworks MediaPipe 1.1.1, incluida la interfaz del panel.
- Comprobación de Swift para macOS sin importar MediaPipe.
- Comprobación de Objective-C++ del puente ARKit y compilación C# con símbolos iOS.
- Enlace real arm64 iOS de FaceLandmarker y los tres componentes del SDK: sin símbolos sin resolver.
- Prueba del postproceso sobre una copia de un proyecto PBX real, incluida una segunda ejecución incremental; bibliotecas, parámetros de enlace y modelo presentes.
- Prueba del contrato de canales: mirada y parpadeos independientes, muecas, límites y rotación de cabeza en grados.

No se ha instalado ni ejecutado esta nueva versión en el iPad desde esta sesión. La prueba física anterior acredita ambos flujos sin análisis; no acredita todavía la estabilidad con MediaPipe, audio o la calidad final del seguimiento. Evidencias de compilación en Validation/HybridHDMI/.

## Ajuste de boca al hablar

La boca ahora combina jawOpen con la separación de los labios centrales (13/14), dividida por la anchura de la boca (78/308) y corregida por el aspecto del vídeo. Así puede responder cuando se habla sin bajar mucho la mandíbula. mouthClose reduce aperturas falsas con los labios juntos. No se analiza el audio HDMI para animar la boca.

En Capturadora → HDMI + cara y mirada:

- **Sensibilidad de boca**: 1,6 por defecto. Subir si se pierden sílabas; bajar si se abre en reposo.
- **Calibrar boca cerrada**: relajar los labios, mirar a cámara y pulsar. Se aplica al siguiente resultado válido; repetir tras reiniciar la aplicación si hace falta.
- **Respuesta facial**: hasta 30 Hz al hablar, o hasta 20 Hz para menor consumo. Sigue habiendo un solo análisis en vuelo y ningún análisis bloquea el vídeo HDMI.

La comprobación de Swift y del panel pasa; los casos sintéticos verifican reposo, sílabas con poca mandíbula, labios cerrados, calibración y límites. La mejora perceptiva y el coste de 30 Hz aún requieren prueba en el iPad. Conviene probar «pa-pe-pi-po-pu», «ma-me-mi-mo-mu» y voz suave, observando que no se abra en silencio.
