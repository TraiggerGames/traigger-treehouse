# Expresiones faciales de Traigger

Se conservan las texturas kemono, los huesos y las posiciones de reposo. Los nuevos correctivos se generan en copias temporales al pulsar Play. No se reconstruye la cabeza ni se repintan los sprites.

## Probar sin iPhone

Pulsa Play y abre **Traigger → Cara → Muecas y seguimiento facial**. Hay ocho estados: neutral, sorpresa, enfado, pícaro, carrillos inflados, tristeza, alegría y lengua con guiño. Puedes ajustar cada rasgo y la intensidad. **Seguir mi cara · iPhone** desactiva el control manual.

Los estados son pruebas de los nuevos correctivos de cara; no cambian las poses corporales existentes. El parpadeo y la apertura de boca mantienen sus controles habituales. Los correctivos de párpados disminuyen al cerrar los ojos para evitar invertir la malla.

## Captura del iPhone

La cámara frontal con ARKit entrega, además de los canales anteriores, 21 coeficientes: cejas, apertura/entrecerrado de ojos, carrillos, comisuras, desplazamiento lateral de boca, presión de labios, nariz y lengua. No se consulta la identidad biométrica de Face ID. El LiDAR trasero no se utiliza.

Hay que recompilar la app de iPhone para incorporar el búfer nativo ampliado de 19 a 40 valores. En el Mac se siguen recibiendo paquetes JSON de versión 1: los emisores antiguos continúan funcionando con los canales nuevos a cero. El emisor de ejemplo `iPhonePrototype/FaceSender.swift` también incluye los campos nuevos; sigue siendo un prototipo que necesita su aplicación iOS de Xcode y firma. No es una app lista para instalar.

Vision Pro no proporciona a esta app las expresiones de los ojos y la cara bajo el visor. Para las muecas detalladas hace falta una fuente facial compatible. No se ha validado usar el iPhone mientras se lleva puesto el visor.

## Alcance y validación

- Implementado: 24 correctivos 2D adicionales, transición suave, comisuras independientes de boca cerrada, controles de prueba y extensión del protocolo nativo/UDP.
- Comprobado: compilación C#, sintaxis del plugin Objective-C++ con SDK iOS, emisor Swift con SDK iOS, ocho poses en Unity, valores finitos, lectura de búfer antiguo/nuevo y restauración de mallas originales.
- Pendiente: calibrar intensidades en el iPhone físico y revisar todas las combinaciones con giros y parpadeos. Son deformaciones del dibujo frontal, no un nuevo rig facial 3D.
- Sin dibujar todavía: dentadura adicional y bocas extremas. Necesitan sprites específicos basados en las referencias de Traigger. No se han añadido dibujos de otro estilo.

Las capturas de prueba y el informe están en `Validation/FaceExpressions`. Se pueden regenerar fuera de Play con **Traigger → Pruebas → Comprobar muecas kemono**.

[Apple: coeficientes faciales de ARKit](https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation/)

## Lengua y expresiones reforzadas

El sprite nuevo `Assets/Traigger/Resources/TraiggerTongue-v2.png` se generó con la herramienta integrada de imágenes tomando la cara actual como referencia de estilo. El prompt íntegro está en `Assets/Traigger/Resources/TraiggerTongue-v2-prompt.txt`. No se ha repintado el atlas original. La raíz de la lengua se funde con el interior de la boca mediante alfa.

En el panel hay **Sacar lengua**, además de boca, labios y guiños; la pose **Lengua + guiño** combina estos controles. El coeficiente `tongueOut` nativo o UDP controla la extensión; el desplazamiento lateral de boca inclina la lengua. Es una pieza 2D con extensión y giro limitados, no una simulación muscular tridimensional. Requiere recompilar el emisor iPhone para obtener la lengua desde ARKit.

Los gestos de cejas, párpados, comisuras y carrillos tienen ahora mayor recorrido. Las poses manuales también abren la boca y hacen guiños; antes esos canales no intervenían en las poses del panel. Se limita la compresión conjunta de párpados para evitar inversiones. Pendiente la validación física de TrueDepth y de combinaciones extremas.

La versión activa v2 es más corta y ancha, con punta felina redondeada y surco mínimo. La v1 permanece como alternativa, sin sobrescribirla.

Al extender la lengua v2, una copia temporal del material de boca sustituye el rosa y la línea interior de la lengua pintada por la cavidad, conservando dientes, contorno y alfa. Al recogerla se restaura el dibujo original. El atlas no se modifica. Se han comprobado extensión completa, media y recogida, además de restauración del material original.

El tamaño predeterminado de la lengua se amplió un 40%; se ajusta con **Tamaño de lengua** (0,75–2). La extensión sigue `tongueOut` (0 dentro, 1 máxima extensión detectada), con suavizado temporal. ARKit no da aquí la dirección o forma de la lengua: el movimiento lateral se deriva del desplazamiento de la boca y es una aproximación. Sigue pendiente comprobar el emisor recompilado en iPhone físico.

La raíz de la lengua ahora entra en la cavidad y comparte gradualmente su sombra. El desvanecimiento cubre un tramo más amplio de la raíz, eliminando su borde superior visible; se compensa la longitud oculta para mantener el tamaño exterior. El giro lateral también se suaviza en el tiempo. Revisado en Unity a media extensión y extensión completa.

## Carrillos sin pelaje duplicado

Los recortes CheekLeft/CheekRight ahora siguen los correctivos de mofletes de HeadBase. Como sus mallas tienen distinta subdivisión, se muestrean por UV los desplazamientos reales de la cara para conservar el solapamiento. No se alteran texturas, huesos ni posiciones de reposo. La prueba **Comprobar mofletes sin pelaje duplicado** verifica inflación máxima a intensidad 1,5 y giros de −18/0/18 grados; resultados y capturas en `Validation/CheekPuff`. Para llevar la corrección a un dispositivo es necesario recompilar e instalar la aplicación.
