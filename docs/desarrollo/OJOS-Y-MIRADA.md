# Ojos y mirada de Traigger

Implementado: composición de tres capas de renderizado por ojo (blanco/contorno, iris y pupila), movimiento suavizado por ojo, recorte al parpadear y ajuste manual del tamaño de pupila. Se conserva el atlas original, el estilo kemono, el orden de dibujo y los huesos existentes. Las capas se componen mediante un material; no son tres PNG nuevos ni tres sprites para editar por separado. El blanco oculto tras el iris se reconstruye con sombreado suave; la zona bajo la pupila se aproxima a partir del color vecino del iris.

## Probar

1. Abre la escena `Assets/Traigger/Scenes/TraiggerStudio-KemonoBody.unity` y pulsa Play.
2. Abre la pestaña **Cara**, sección **Mirada · iris y pupilas**.
3. Activa **Mirada manual** y mueve los controles horizontal/vertical de cada ojo.
4. Ajusta **Recorrido horizontal**, **Recorrido vertical** y **Tamaño de la pupila**.
5. Pulsa **Mirada desde el iPhone** o **Seguir mi cara · iPhone** para volver al seguimiento. **Centrar mirada** fija manualmente ambos ojos al centro.

Los ajustes de esta sección son temporales durante Play. El tamaño de pupila es un control artístico manual; no representa una medida de tu pupila física.

## Seguimiento

`ARKitFaceSource` reutiliza los coeficientes de mirada ya presentes en el búfer nativo y conserva las direcciones de ambos ojos por separado. Respeta las opciones de espejo e intercambio de ojos existentes. `FaceFrame` y el emisor Swift incluyen cuatro valores nuevos y la bandera `independentEyes`. Los paquetes antiguos siguen funcionando con su mirada conjunta. No se transmite vídeo ni se cambia el protocolo binario nativo de 40 valores.

Para verlo en una aplicación ya instalada hay que compilar e instalar una versión actualizada. La dirección, ganancia y respuesta con el iPhone físico siguen pendientes de calibración. No se ha validado este cambio en un dispositivo iOS o Vision Pro físico.

## Validación realizada

C# compilado, emisor Swift comprobado por el compilador y shader probado con Metal en Unity 6000.5.1f1. El menú **Traigger → Pruebas → Comprobar iris y pupilas separados** comprueba las direcciones independientes, la conversión ARKit y que la mirada no arrastre la geometría del blanco. Fuera de Play guarda centro, cuatro extremos, giros y parpadeo; dentro de Play captura el renderer animado real. Capturas revisadas en `Validation/Eyes`.

La reconstrucción del fondo del ojo y la extracción de iris/pupila son máscaras basadas en el dibujo actual. Conviene revisar combinaciones extremas de muecas y cierre parcial; si se redibuja el atlas habrá que ajustar sus máscaras. No se han regenerado el cuerpo, la cabeza ni la escena.

Durante la prueba en Play apareció además un error de la salida de vídeo existente: `EntryPointNotFoundException: TraiggerStreamFree` en `StreamOutput.Status`. No pertenece al sistema de ojos y no se ha modificado esa integración en este cambio.
