# Pelo, mechas y flequillo

El cambio conserva el dibujo kemono y los PNG originales. Se aplica al iniciar Play o al compilar una versión nueva de la aplicación; no hay que reconstruir el personaje.

## Ajustes

En **Física**, junto a **Pelo suelto / recorrido**:

- **Mechoncitos superiores** regula los cuatro mechones pequeños de la coronilla.
- **Flexibilidad del flequillo** regula tres zonas independientes del pelo sobre la frente.
- **Nitidez del pelo** ajusta un realce local moderado del color y el borde.
- **Degradado de mechas** mezcla tonos azules algo más profundos arriba y cian más claro en las puntas. La máscara conserva el pelo oscuro, la tinta y los brillos originales.

Los ajustes son temporales durante Play. La física general y el recorrido del pelo también afectan la respuesta. Sin movimiento de cabeza, los mechones regresan suavemente al reposo; no hay un vaivén constante que simule viento.

## Implementado

`HairDetailRig` copia y subdivide la malla del pelo frontal durante la sesión: 437 → 6497 vértices en la escena actual. Interpola UV, pesos de huesos y todos los correctivos existentes, conservando los vértices originales y el dibujo neutral. Añade cuatro campos de flexión de coronilla y tres de flequillo, con raíces fijas y distinta respuesta amortiguada. El movimiento responde a las velocidades de inclinación, giro, asentimiento y desplazamiento del controlador. Sustituye el antiguo correctivo común de flequillo; los mechones laterales y el pelo posterior mantienen su sistema anterior.

La nitidez y el degradado se aplican sólo a los materiales de pelo mediante el shader existente. Se revisó la importación de las texturas utilizadas para conservar sus dimensiones, evitar compresión y mipmaps y mantener el filtrado bilineal. Una textura posterior alternativa tenía reducción a potencia de dos/mipmaps; la escena actual utiliza el atlas compartido. Se han guardado copias de los metadatos previos en `Validation/Hair`.

La mejora de nitidez es moderada: el pelo frontal ocupa aproximadamente 310 × 320 píxeles dentro del atlas de 1254 × 1254. El filtro no recupera detalles que no existen en ese dibujo. Ampliaciones muy grandes aún pueden mostrar suavidad; no se ha presentado este cambio como arte redibujado en alta resolución.

## Comprobado en Unity

Compilación C# y shader Metal correctos en Unity 6000.5.1f1. **Traigger → Arte → Mejorar nitidez del pelo y comprobar mechones** verifica la subdivisión, conservación de UV y correctivos, normalización de pesos, las siete zonas flexibles, aislamiento de las raíces y que los muelles se asienten. Guarda capturas antes/después y con movimiento en ambos sentidos. En Play guarda el renderer animado real. Se revisaron las capturas del editor y de Play en `Validation/Hair`.

Pendiente: calibrar la intensidad con el seguimiento de un iPhone físico y comprobar coste/rendimiento en Vision Pro. El cambio no requiere ni añade captura de datos del dispositivo. Para verlo en una aplicación instalada hay que compilar e instalar una versión actualizada.

La prueba en Play sigue mostrando el error ajeno `TraiggerStreamFree` de la salida de vídeo existente; no se ha modificado esa integración.
