# Sesión VTuber completa en el iPad

## Comportamiento implementado

La prioridad se elige con **Priorizar expresiones completas**, tanto en local como al enviar. La versión anterior forzaba la prioridad de cara en local aunque el interruptor estuviera apagado; era una regresión de nuestra implementación y queda corregida.

- **Activado:** mantiene ARKit completo e intenta abrir USB sin sustituir las expresiones por Vision. Si detecta conflicto, detiene USB y recupera la cara.
- **Desactivado:** prioriza vídeo USB; pausa ARKit local antes de abrir la capturadora. No detiene USB por perder la cámara facial. Al parar la captura recupera ARKit sólo si seguía solicitado por el usuario.

El cambio de propietario de la cámara se ordena mediante el hilo principal y después se abre USB en su cola; no se inicia USB mientras está pendiente la pausa frontal. Los arranques antiguos se descartan si entretanto se ha parado o reconfigurado la captura. Tras una detención por prioridad de cara, cambiar el interruptor permite reabrir el mismo dispositivo. No hay reintento automático de ese conflicto.

Con prioridad de cara, la entrada USB elige un formato cercano a 720p y una frecuencia de 30 fps, respetando los límites reales del accesorio. No se garantiza que exista ese modo exacto. Es un punto de partida de compatibilidad; el envío remoto conserva sus ajustes anteriores.

Se dejan tres segundos de arranque y se vigilan interrupciones, errores de ARKit y llegada de fotogramas. La vigilancia utiliza fotogramas de cámara ARKit: dejar de ver tu cara no se interpreta como conflicto. Si ARKit se interrumpe, deja de recibir cámara durante más de 2,5 s o falla USB, se detiene la capturadora y se solicita recuperar ARKit únicamente si el usuario todavía tenía activado el seguimiento facial. No se reintenta automáticamente en bucle ni se sustituye por Vision.

Las notificaciones de error de captura se filtran por la sesión concreta. ARKit registra sus errores completos, y durante esta prueba no se vuelve a arrancar automáticamente mientras USB sigue ocupando su sesión. Se ignoran transiciones antiguas de modo que lleguen tarde.

## Prueba en dispositivo

Hay que exportar una nueva compilación desde Unity e instalarla en el iPad; editar los archivos no actualiza una aplicación ya instalada ni un proyecto de Xcode exportado antes.

1. Sin enviar a Vision Pro, selecciona **Frontal · cara** y activa el seguimiento. Comprueba que el estado diga **ARKit completo · siguiendo tu cara**.
2. Comprueba nariz, lengua, mirada y carrillos sin capturadora.
3. Conecta la capturadora y actívala inicialmente sin audio, con **Priorizar expresiones completas apagado**. Comprueba vídeo continuo durante un minuto. ARKit debe indicar pausa por prioridad de vídeo.
4. Activa **Priorizar expresiones completas**: se reconfigura la captura. Mantén visible tu cara durante un minuto y prueba las mismas expresiones.
5. Si ambos flujos continúan, prueba después con audio. Comprueba también desconectar y reconectar USB.
6. Si aparece **Capturadora detenida para conservar las expresiones completas**, conserva el mensaje y los registros de ARKit/USB. El botón de inicio de captura permite un nuevo intento explícito. No es una prueba de incompatibilidad universal: sólo documenta el fallo de esa ejecución.
7. Después de detener USB, comprueba que vuelva ARKit y que desactivar el seguimiento manualmente no lo reactive por una recuperación tardía.

El estado de cámara frontal se muestra en Seguimiento y se transmite al panel nativo existente. Distingue ARKit completo, Vision aproximado, cara fuera de encuadre y el diagnóstico cuando faltan datos.

## Validado y pendiente

- Comprobación de tipos de Swift iOS 17+ con los archivos reales de captura, transporte y emisión: sin errores.
- Comprobación de tipos de los mismos archivos en macOS: sin errores.
- Plugin Objective-C++ ARKit comprobado con el SDK de iPhone: sin errores.
- C# comprobado tanto con la configuración general como con `UNITY_IOS`: sin errores.
- Política real de selección/recuperación ejecutada sin hardware: prioridad local, prioridad explícita, espera de arranque, conflicto, silencio de vídeo, coexistencia sana y recuperación del envío: PASS. Fuente de prueba en `Validation/LocalFaceCapture`.
- No se ha exportado, enlazado ni instalado una aplicación nueva en esta intervención.
- No se ha verificado la coexistencia física de ARKit y USB en tu iPad. La consulta de dispositivos del Mac falló al inicializar CoreDeviceService; eso no demuestra que el iPad esté desconectado ni sea incompatible.

Este cambio elimina la degradación automática de nuestra implementación y prepara una prueba protegida. No elimina una restricción de iPadOS si el sistema impide las dos sesiones de cámara. Las expresiones se conservan deteniendo USB en ese caso; no debe presentarse como compatibilidad simultánea ya resuelta.

## Corrección del primer fotograma

Se corrigió el interruptor ignorado en local, la reapertura después de una suspensión, la lectura de un valor de prioridad explícitamente false frente a preferencias antiguas y la secuencia de pausa de ARKit antes de abrir USB. También se desconectan delegados al retirar sesiones y se rechazan callbacks de vídeo de salidas antiguas. Los registros contabilizan los fotogramas USB recibidos para distinguir una entrada detenida de un problema posterior de envío.

Comprobaciones de Swift iOS/macOS, Objective-C++ iOS y C# iOS correctas; política ejecutada para ambos estados del interruptor tanto localmente como enviando. Esto corrige regresiones demostrables del código. No se ha reproducido físicamente el síntoma del primer fotograma ni se certifica aún vídeo continuo en el dispositivo.

## Interrupción «motivo 1» y estado del interruptor

`videoDeviceNotAvailableInBackground` (valor 1) se trata ahora como una pausa temporal. No se ejecuta `stopRunning`, no se borra el dispositivo solicitado y el vigilante no detiene ni reinicia USB mientras dura la pausa. Se espera `interruptionEndedNotification` o la llegada real de un nuevo fotograma. Al volver a primer plano se concede un margen nuevo para que la cámara entregue imagen; una pausa por segundo plano no se conserva como prueba de conflicto ARKit. Los errores reales de ARKit no se borran mediante este margen.

Se registran los cambios de actividad y los estados de las escenas UIKit para investigar si la ventana adicional dispara la transición. El fin de la interrupción se filtra por la sesión USB actual. Una detención manual o una sesión sustituida no se reabre por una notificación antigua.

El valor persistido del interruptor es la fuente de prioridad. Una actualización de calidad que omita `forceFaceID` ya no la apaga por su valor predeterminado. Cuando un mensaje sí incluye prioridad se comprueba también la prioridad aplicada a la sesión, aunque el objeto de calidad guardado coincida: así se reconfigura una sesión que se hubiera quedado desincronizada. El estado indica «Aplicando…» durante el cambio.

Comprobado: pruebas de política con pausas largas en ambos modos, margen al retornar, conflicto real posterior y recuperación de vídeo; comprobación de tipos Swift iOS/macOS y plugin Objective-C++ iOS. Pendiente: compilar/instalar y comprobar las transiciones de ventanas en el iPad físico. No se ha validado una compilación instalada en esta intervención.

Prueba recomendada: iniciar en la ventana principal, dejar vídeo continuo un minuto, abrir/cerrar la ventana adicional, pasar la app al fondo y volver. Debe aparecer pausa temporal y después continuar sin volver a pulsar Capturar. Repetir con las dos prioridades; comparar el interruptor con el modo indicado una vez que desaparezca «Aplicando…».
