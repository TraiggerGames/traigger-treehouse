# Panel, cámaras y bloqueo de gestos

Los controles forman parte del panel de la aplicación; no dependen de las ventanas del editor. Para verlos en una instalación de Mac, iPhone/iPad o Vision Pro hay que recompilar e instalar la versión nueva.

- **Cara:** ocho muecas, controles de boca, sonrisa, cejas, párpados, carrillos, nariz y lengua; intensidad y tamaño de lengua. Mover un deslizador activa el control manual. **Seguir mi cara · iPhone** devuelve el control facial al seguimiento. Las expresiones corporales anteriores siguen en Inicio.
- **Cuerpo entero:** restablece el desplazamiento/tamaño manual y encuadra la geometría del personaje completo, con margen para orejas y cola.
- **Cara · llamada:** restablece el desplazamiento/tamaño manual y encuadra cabeza, orejas y parte de los hombros para una llamada. Este botón prepara el plano; no inicia FaceTime.
- **Bloquear arrastre y pellizco:** impide mover o escalar con dedos y también con ratón/rueda. Cancela el gesto en curso. No detiene el seguimiento de cara/manos, las animaciones ni los controles del panel. El estado se recuerda entre sesiones; la primera vez en Vision Pro viene bloqueado.

Los botones de encuadre y el bloqueo están en la cabecera, visibles desde todas las pestañas. Tras centrar, el área de imagen evita el panel y se adapta al formato de la ventana. La cámara no persigue la respiración ni recalcula el zoom continuamente. Si cambias mucho la pose, puedes pulsar el botón otra vez.

La ventana de avatar para OBS respeta el encuadre seleccionado y adapta su salida a 16:9. La cámara principal también puede enviarse a la pantalla secundaria mediante Ajustes.

La comprobación **Traigger → Pruebas → Comprobar encuadre y bloqueo** se ejecuta en Play. Prueba bloquear durante un arrastre, rechazar un pellizco y volver a escalar después de desbloquear, así como los dos encuadres. Guarda resultados y capturas en `Validation/Panel`. Sigue pendiente comprobar los gestos en el dispositivo Vision Pro físico.
