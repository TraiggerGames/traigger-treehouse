# Traigger VTuber Studio — pelo y balance lateral, 19 de octubre de 2026

Proyecto Unity 6 para macOS. Avatar 2D provisional, reconstruido a partir de tus dos ilustraciones. El rig y los controles ya se han ejecutado en Unity, pero el acabado artístico no está terminado.

## Probarlo

1. Añade esta carpeta en Unity Hub y abre con Unity 6000.5.1f1.
2. Abre `Assets/Traigger/Scenes/TraiggerStudio.unity`. El avatar, sus mallas y cámaras están guardados en la escena.
3. Pulsa Play. «Movimiento orgánico y respiración» activa el movimiento de reposo. Los deslizadores «Mirar a los lados» y «Mirar arriba / abajo» permiten probar la nueva deformación.
4. «Moverse de lado a lado» controla el balance corporal y «Amplitud lateral» aumenta su recorrido. Los pies compensan mediante un solucionador de dos segmentos para mantener el apoyo durante el reposo. Los controles manuales de piernas tienen prioridad. «Suavidad de transición» controla cuánto tarda en acompañar el objetivo. «Pelo suelto / recorrido» ajusta la respuesta y la soltura del pelo (valor inicial 1,5). «Inercia» ajusta pelo, orejas y cola. «Pose neutra» apaga el movimiento de reposo y devuelve los controles al centro.
5. Tab oculta el panel; espacio abre/cierra la boca; R devuelve al reposo. 1 neutro, 2 orejas alegres, 3 ojos cerrados. Usa Input Manager clásico o Both.

## Qué cambia en esta revisión

- Se usa el atlas `Art/TraiggerParts-clean.png` modificado por el usuario para ambos grupos de pelo. Sus recortes y píxeles se conservan sin cambios; se ha retirado la referencia a BackHair-v2.png. La melena posterior conserva orden -100 y raíz en Head.
- Transiciones amortiguadas con velocidad continua para cabeza, torso, brazos, piernas y expresiones. El torso acompaña parte del gesto, la raíz se desplaza lateralmente y la cadera compensa suavemente. Las piernas ya no oscilan automáticamente como péndulos.
- Movimiento de reposo con respiración y ruido suave para pequeñas variaciones de atención. No intenta simular seguimiento corporal.
- El pelo flexiona con mallas y cuatro grupos de correctivos: lateral izquierdo, lateral derecho, flequillo y parte posterior. La raíz queda fija y la deformación crece hacia las puntas. Los grupos tienen resortes amortiguados con rigidez/masa aparente distintas y pequeños subpasos. Responden a velocidad de cabeza y cuerpo; ya no se gira toda la pieza como una tabla. Orejas y cola mantienen su inercia.
- Giro lateral y vertical mediante blendshapes de las mallas, asimetría leve de los ojos y desplazamiento relativo de cara/pelo/orejas. La boca cerrada acompaña el desplazamiento. El control lateral recibe hasta ±18 y el vertical ±12; son valores de entrada para una aproximación visual, no ángulos físicamente exactos del dibujo.
- Se recortó la parte superior oculta de las UV de los brazos para que sus remates no sobresalgan de las mangas al moverse.

## Rig y herramientas

23 huesos, 16 capas y cuadrículas deformables de 18 × 22 celdas. Hasta cuatro pesos normalizados por vértice, estimados por distancia al segmento y restringidos a cada cadena. `SkinnedMeshRenderer` aplica el skinning real; los ojos y boca tienen blendshapes. El giro añade cuatro correctivos a cabeza/cara/pelo/orejas.

Las capas son piezas lógicas independientes, aunque la mayoría reutiliza regiones del atlas; no es un PSD ni una separación exacta de píxeles del original. Los originales están intactos en `Art/References/`. La procedencia del arte y prompts están en `Art/PROCEDENCIA.md`.

`Traigger → AutoRig` permite editar huesos de reposo, rectángulos, UV, densidad y caída de pesos, visualizar triangulación y guardar prefab con mallas/materiales. No incluye pincel de pesos. La receta es `Art/TraiggerRig.asset`; `Art/assembly.json` sirve para regenerarla. `texturePath` permite usar un PNG independiente para una pieza.

`Traigger → Reparar y guardar escena con avatar visible` regenera la receta desde JSON y reemplaza el rig/cámaras de la escena. Utilízalo en una copia si has hecho ajustes manuales, porque esa reconstrucción no los conserva.

## Pruebas incluidas

- Fuera de Play: `Traigger → 2 · Comprobar deformación y guardar prueba` verifica pesos, deformación no rígida del brazo y boca, y genera results.txt y renders.
- En Play: `Traigger → Pruebas → Comprobar transiciones y giro en Play` prueba giro y después balance corporal de ambos lados; detecta transiciones, verifica vértices finitos, mide recorrido lateral y error de apoyo, comprueba que los correctivos no mueven la raíz del pelo y registra su respuesta. Guarda motion-results.txt y renders de giro y balance. Durante esa prueba se desactivan temporalmente el movimiento orgánico, la red y el parpadeo; se restauran al acabar.
- `Validation/` contiene los resultados reales de estas pruebas. No sustituyen la revisión artística ni pruebas de iPhone/OBS.
- `Vista-previa-Traigger.html` muestra una comparación estática de los renders reales de Unity de esta revisión. La animación se prueba en Unity.

## iPhone y salida

La app principal es para Mac. El iPhone es opcional como emisor de expresiones.

`FaceReceiver` recibe UDP JSON UTF-8 versión 1 en el puerto 49983. Por defecto sólo localhost. Para recibir de un iPhone, activa `Listen On LAN` en StudioBootstrap antes de Play y permite acceso de Unity a la red local. El receptor limita paquetes, rechaza valores no finitos y vuelve a controles locales cuando no llegan datos durante 0,5 s. UDP no tiene autenticación ni cifrado; usa una red de confianza y no abras el puerto al exterior.

Ejemplo:

```json
{"version":1,"sequence":1,"headRoll":8,"headYaw":12,"headPitch":0,"jawOpen":0.6,"eyeBlinkLeft":0,"eyeBlinkRight":0,"eyeLookX":0,"eyeLookY":0}
```

`Tools/send_face.py` envía datos de prueba locales. `iPhonePrototype/FaceSender.swift` es un ejemplo de ARKit y envío UDP para integrar en una app iOS de Xcode; no es una app compilada/firmada. **El adaptador de iFacialMocap sigue pendiente.** Los signos/ejes de captura necesitan calibración con el dispositivo.

La cámara puede enviarse a Display 2 y el panel permanece en Display 1. Para OBS se ofrece fondo verde y captura con clave de color. No hay Syphon, ventana nativa transparente ni alfa garantizado en la captura macOS. Monitores, OBS e iPhone reales no se han validado.

`Traigger → 3 · Compilar macOS` genera la aplicación en Builds si tienes el módulo correspondiente. No se ha compilado, firmado ni notarizado la app.

## Límites actuales

El giro 2,5D es pequeño y aproximado. No equivale a una cabeza 3D ni permite perfil/espalda; para ampliar su recorrido con fidelidad hacen falta nuevas vistas y correctivos artísticos. La forma de boca sigue siendo básica, sin visemas completos. La física no incluye colisiones ni tela. No hay micrófono ni webcam implementados, ni seguimiento corporal.

Quedan por pulir el parecido exacto, bordes y algunas uniones en poses extremas. El atlas actual incluye los retoques del usuario sobre el arte reconstruido inicial; se preserva con comprobación SHA-256. La imagen posterior BackHair-v2.png se conserva sólo como histórico y no interviene en el rig. Esta entrega se mantiene como prototipo funcional editable.

## Explicación de los scripts

- RigDefinition: receta de huesos, capas y parámetros.
- AutoRig: mallas, pesos y correctivos de giro.
- AvatarRig: aplica poses, expresiones y deformaciones de mirada.
- StudioController: suavizado, coordinación corporal, inercia y panel.
- StudioBootstrap: conecta el rig guardado con cámaras y receptor de red.
- FaceReceiver: datos faciales por red, separados del hilo de animación.
- RepairScene / RigEditor: generación y ajustes dentro del editor.
- MotionCheck / StudioSetup: comprobaciones y renders de validación.

## Giro más visible y pelo más suelto

Se amplificó la deformación lateral del rostro (antes ~0,065; ahora ~0,145 unidades en el correctivo de cara), con mayor asimetría de los ojos y paralaje de pelo/orejas. El yaw responde con un tiempo algo menor. La boca cerrada acompaña el desplazamiento ampliado.

Las puntas laterales del pelo tienen ahora hasta 0,16 unidades de desplazamiento base (antes 0,075), el flequillo 0,08 y la melena 0,125. Sus resortes son menos rígidos y menos amortiguados; responden más al movimiento de cabeza/cuerpo y admiten un rebote limitado al 115 % del correctivo. Las raíces permanecen fijas. El control de soltura tiene valor inicial 1,5 y rango 0,2–2,5.

Se conserva el atlas recortado por el usuario sin editar sus píxeles. El giro continúa siendo una deformación 2D limitada, no una vista de perfil real.

## Esqueleto humanoide y piezas rígidas

El esqueleto sigue la jerarquía y los nombres de Mecanim: Hips → Spine → Chest → Neck → Head; Left/RightShoulder → UpperArm → LowerArm → Hand; Left/RightUpperLeg → LowerLeg → Foot → Toes; Left/RightEye. Los lados son anatómicos: el brazo de la izquierda de la pantalla es el derecho del personaje. Huesos extra de furro: LeftEar, RightEar, Hair, Tail1–3.

El cuerpo funciona como un recortable: cada sprite está entero, sin cortes, y depende de un solo hueso, así que no se estira al moverse. Brazos y piernas giran donde salen de la ropa (LowerArm en la boca de la manga, LowerLeg bajo el pantalón) para que sus remates queden siempre tapados. El cuello asume el 40 % de la inclinación de cabeza y la columna el 40 % de la del torso. Cara y pelo conservan sus deformaciones de giro, parpadeo, boca y pelo. Renders reales en `Validation/rig-piezas-rigidas.png`.

## Orejas

La base de cada oreja se desliza por el arco del cráneo (centro medido sobre el contorno de la cara, 70 % del giro) y se hunde bajo el pelo, más al abrirse hacia fuera, para que su borde nunca asome. Esos valores se buscaron comprobando la silueta real del pelo de −25° a +25° en ambas orejas. El ángulo lo mueve un resorte con inercia y un rebote del ~12 %, con tope en ±25°. En el giro de cabeza las orejas tienen la misma paralaje que el pelo. Validación en `Validation/rig-orejas.png`.


Orejas: al pulsar Play, el controlador añade una flexión temporal sobre las mallas existentes, con base fija y punta flexible. Los controles Oreja izquierda/derecha combinan orientación en arco, giro de la base y flexión retardada de la punta. El control Arco de las orejas ajusta el desplazamiento de la inserción; Orejas: bajas / atentas y los botones Atento, Curioso y Orejas bajas permiten probar gestos. Flexibilidad de las orejas regula la respuesta del resorte. No requiere reconstruir el rig: conserva los huesos y pesos editados. Validación visual pendiente de esta revisión.


Cuerpo · revisión kemono: en Play, desplaza el panel hasta «Cuerpo · articulaciones independientes». Incluye hombros, brazos superiores, codos, muñecas, muslos, rodillas, tobillos y puntas de pies; botones «Probar saludo» y «Probar paso». El rig usa exclusivamente tu atlas original y conserva tus huesos. Las mangas y los pantalones se deforman con las articulaciones. «Sombras suaves · opcional» empieza en 0 para conservar el acabado original. No incluye poses nuevas de dedos. Estado y limitaciones: Validation/body-status.md.

## Cuerpo kemono segmentado (BodyV12): encaje de piezas

Cada pieza conserva la proporción con la que se dibujó. La malla se deforma donde el cuerpo real se dobla, para que no parezca una marioneta:
- **Camiseta:** pesos por altura. El tallo del cuello sigue al cuello, la parte alta al pecho, la media a la columna y el bajo acompaña a la cadera. Los hombros siguen al encogimiento, nunca a los brazos.
- **Perneras:** quedan unidas a la cintura (franja de cadera, `hipBlendFrom/To`) y su bajo sigue un 30 % el giro de la rodilla (`kneeClothFollow`). Usan los dibujos intercambiados (`swapThighs`): así el vuelo queda hacia fuera y la entrepierna se abre como en el model sheet.
- **Cintura:** la pieza con bolsillos se ve bajo la camiseta. El centro y la pretina siguen a la cadera. Cada lado se reparte con su muslo con la misma regla que la pernera, así que al mover el muslo esa parte del pantalón va con él. Es opaca y va detrás de las perneras (orden 2): su borde inferior queda tapado y la entrepierna asoma entre ellas. La boca superior de cada pernera (forro azul) se funde con la cintura (`thighTopFade*`). Los costados que sobresalen de las perneras se difuminan (`pelvisSideFade*`) para que la silueta la marquen las perneras.
- **Tobillo:** se dobla como el codo, y el trozo de pierna dibujado dentro del calcetín se desvanece (`ankleFade*`).
- **Cuello:** el torso está 0,09 más bajo respecto a la cabeza (`fit.neck` = 3,18), así que se ve el cuello entre la barbilla y el cuello de la camiseta. La cintura y las piernas no se mueven (`pelvisAt` compensado).
- **Manos y muñecas:** el puño del guante es tela rígida: la mano no se deforma y gira desde el centro del puño. `Tools/clean_paw_wrists.py` lee `KemonoPaws-v12.png` sin modificarlo y escribe `KemonoPaws-v12-wrist.png` sin el trozo de antebrazo dibujado sobre el puño. También mide la boca del puño y guarda en `layout-v12.json` su centro (articulación) y su eje, así el antebrazo entra centrado y perpendicular a la boca del guante. El extremo del antebrazo acompaña el 70 % del giro de la muñeca (`wristBendFollow`) para seguir dentro del puño, y se desvanece antes de su punta (`wristHide*`). Límite de muñeca: 40°. Validación en `Validation/kemono-munecas.png`.
- **Pies:** articulaciones medidas en el centro del tobillo y de la boca del calcetín.
- **Altura del hombro:** el punto de unión del brazo (`fit.shoulder`) está 0,05 más abajo, a la altura de la costura del hombro de la camiseta. La copa de la manga queda bajo esa costura en vez de sobresalir por encima.
- **Capas dinámicas del brazo:** con el brazo colgando o levantado, manga y brazo van detrás de la camiseta (órdenes `sleeveBackOrder` y `upperArmBackOrder`), así que la costura del hombro de la camiseta tapa la unión. Cuando el brazo cruza hacia el cuerpo más de `frontAngle` + `frontHysteresis` grados (11°), pasan delante; vuelven detrás por debajo de 5°. La histéresis evita parpadeos cerca del límite. Lo actualiza `KemonoBodyRig.UpdateLayering` en cada fotograma.
- **Hombros:** en Play, al levantar el brazo más de `shoulderFollowStart` grados, el hombro sube una parte (`shoulderFollow`, como máximo `shoulderFollowMax`) y el brazo compensa para conservar su dirección. Con eso la camiseta se deforma en el hombro. Sin brazo levantado, la camiseta no se mueve con los brazos.
- **Movimiento secundario (solo en Play):** huesos de muelle en el bajo de la camiseta (`ShirtSway`) y en las perneras (`*ShortsSway`). Al girar o desplazarse el cuerpo se quedan atrás, rebotan un poco y se asientan. Se ajustan con `springStiffness`, `springDamping`, `springInertia`, `springSway`, `springMax`, `shirtSway` y `shortsSway`.
- **Hombro, codo y muñeca:** una franja alrededor de cada pivote sigue a los dos huesos. Las dos piezas que se solapan reciben los mismos pesos, así que se doblan juntas.
- **Extremo de la pieza de encima:** se desvanece (codo del brazo y pelaje de la mano), así que no se ve su contorno.
- **Mezcla por ángulo:** depende del ángulo alrededor del pivote, con 5 huesos auxiliares por articulación (`*ShoulderBlend1-5`, `*ElbowBlend1-5`, `*WristBlend1-5`) que giran 1/6…5/6 del ángulo. `KemonoBodyRig.SyncHelpers` los actualiza en LateUpdate, también en el editor. La unión se curva sin plegarse en saludos, brazos abiertos y codos doblados hasta unos 110°.
- **Manga:** la costura queda unida al torso y el puño sigue al brazo, como tela.
- **Cadera, rodilla y tobillo:** son rígidas, porque la tela ya tapa la unión.
- **Extremo del brazo dentro de la manga:** se oculta (`shoulderFade*`) y no se generan triángulos totalmente transparentes, así que no asoma junto al cuello.

**Constraints (límites anatómicos).** `KemonoBodyRig.ApplyConstraints` limita cada articulación a su rango natural en cada fotograma, después del controlador y del IK. Si un control o el IK piden más, la articulación se queda en su tope. Rangos en `fit` (grados desde el reposo; «fuera» aleja el miembro del cuerpo):

| Articulación | Fuera / arriba | Dentro |
|---|---|---|
| Hombro (`shoulderRaiseLimit` / `shoulderInwardLimit`) | 165° | 40° |
| Encogimiento de hombro (`shrugLimit`) | 25° | 25° |
| Codo (`elbowLimit`) | 135° | 135° |
| Muñeca (`wristLimit`) | 70° | 70° |
| Cadera (`hipOutwardLimit` / `hipInwardLimit`) | 50° | 25° |
| Rodilla (`kneeLimit`) | 80° | 80° |
| Tobillo (`ankleLimit`) | 40° | 40° |
| Dedos del pie (`toesLimit`) | 30° | 30° |

Un valor 0 desactiva el límite de esa articulación.

**Comprobación del cuerpo.** Aplica el seguimiento del hombro como en Play. Fuera de un radio de 0,5 alrededor de los hombros, la camiseta no debe moverse al mover los brazos. Exige un área mínima de 0,10 en todos los triángulos de todas las poses, salvo una excepción: en el alcance extremo (brazo vertical y codo a ~120°) acepta el pliegue del interior del codo, solo en los triángulos del brazo y el antebrazo a menos de 0,15 del codo y nunca por debajo de 0,03. También verifica que el codo forzado a 170° se queda en su límite. Como cada pieza se generó a su propia escala, `Art/BodyV12/layout-v12.json` guarda por pieza:
- `scale`: unidades por píxel.
- `prox`/`dist`: articulaciones en la celda, en coordenadas normalizadas con v hacia abajo.
- `order`: capa de dibujo.

Las franjas se ajustan en `fit`: `shoulderBlendFrom/To` (grados hacia fuera desde el eje del brazo), `elbowBlend`/`wristBlend` (grados a cada lado de la perpendicular), `shoulderRadius` (radio desde el que el brazo bajo la manga vuelve a ser rígido) y `elbowFade*`/`wristFade*` (desvanecido, en unidades a lo largo del eje). El bloque `fit` define también el cuello del torso, los puntos de hombro y cadera y los ángulos de reposo. `KemonoBodyRig.Build` coloca los huesos de brazos y piernas donde caen esas articulaciones del dibujo. La cintura queda bajo la camiseta y las caderas giran ocultas. El orden de capas es: pernera sobre gemelo, calcetín sobre tobillo, manga sobre brazo, brazo sobre antebrazo y mano delante. Para aplicar cambios vale cualquiera de los dos menús: `Traigger → Reparar y guardar escena con avatar visible` reconstruye cabeza y cuerpo, y `Traigger → Cuerpo kemono → Reconstruir sólo el cuerpo de prueba` reconstruye solo el cuerpo. Ambos montan el cuerpo segmentado mientras exista `Art/BodyV12/layout-v12.json`. Validación en `Validation/kemono-v12-*.png`.

## Cola dinámica

La cola está 0,15 más arriba (`assembly.json`) y la mueve una cadena de tres muelles en `StudioController`:
- **Meneo:** una onda recorre la cola de la base a la punta, con más amplitud y retraso en cada tramo y un ritmo algo irregular.
- **Inercia:** al desplazarse el cuerpo o girar la cadera, cada tramo se queda atrás y da un pequeño latigazo antes de asentarse.

Controles en el panel: «Cola» (ángulo base), «Cola: meneo» (0–1,5; por defecto 0,8) y «Cola: velocidad del meneo». La inercia se escala con «Inercia de pelo, orejas y cola». Validación en `Validation/kemono-cola.png`.

## Reconstrucción y sombreado de la cabeza

`Traigger → Reparar y guardar escena con avatar visible` reconstruye cabeza y cuerpo. Después vuelve a asignar a la cabeza los materiales guardados por `Traigger → Arte → Sombrear cabeza sin modificar rig` (los `*_celShaded` del `HeadShadingMaterials*.asset` más reciente de `Art/HeadShading`). Así el estilo de la cabeza y sus ajustes de intensidad no se pierden al reparar. Comprobado: la cabeza reparada es idéntica, píxel a píxel, a la captura del sombreado original (`Validation/cabeza-sombreado-tras-reparar.png`).

## Física de la camiseta

El bajo de la camiseta lleva tres muelles (izquierda, centro y derecha: `ShirtSwayLeft`, `ShirtSway`, `ShirtSwayRight`) que se reparten el peso con una transición suave. Así el bajo ondula en vez de girar en bloque. Cada muelle de tela (camiseta y perneras) tiene:
- **Giro con inercia:** al girar el cuerpo, la tela conserva parte de su orientación.
- **Arrastre:** al desplazarse el cuerpo, la tela se queda atrás. El límite es suave, así que no choca de golpe contra él.
- **Rebote vertical:** al subir o bajar el cuerpo (`springBounce`, `springMaxOffset`).
- **Aleteo:** un pequeño movimiento extra cuando el cuerpo se mueve rápido (`springFlutter`).

Cada muelle tiene una rigidez algo distinta para que la tela no se mueva en bloque. Los valores se ajustan en `fit` de `layout-v12.json`. Solo actúa en Play.

## Agarrar y escalar con los dedos

`TouchManipulator`:
- **Un dedo** que empieza sobre el personaje lo arrastra.
- **Dos dedos** lo escalan con el pellizco (de 0,3 a 3 veces) y lo mueven con su punto medio, manteniendo bajo los dedos el mismo punto del personaje.
- **En Mac:** arrastrar con el ratón y rueda para escalar.

Los toques sobre el panel no agarran al personaje. El personaje no puede sacarse del todo de la pantalla, y los pies siguen plantados al moverlo. La posición y el tamaño se recuerdan entre sesiones. El panel tiene «Restablecer posición y tamaño».

## iPhone y iPad con seguimiento facial (Face ID / TrueDepth)

`Traigger → 4 · Compilar iPhone / iPad (proyecto de Xcode)` genera `Builds/iOS/Unity-iPhone.xcodeproj` a partir de `Scenes/TraiggerStudio.unity`. Configura iPhone e iPad, iOS 15 o posterior, rotación automática y el texto del permiso de cámara. También enlaza ARKit. Para instalarla:
1. Abre `Unity-iPhone.xcodeproj` en Xcode.
2. En *Signing & Capabilities*, elige tu equipo (Apple ID). Si hace falta, cambia el *Bundle Identifier*.
3. Conecta el iPhone o iPad y pulsa Run.

En el dispositivo, `ARKitFaceSource` (plugin nativo `Plugins/iOS/TraiggerFaceTracking.mm`) usa la cámara frontal TrueDepth para leer la pose de la cabeza, los parpadeos, la boca y la mirada. No muestra ni guarda imágenes. Tiene prioridad sobre el seguimiento por red. Requiere un dispositivo con Face ID o con chip A12 o posterior.

El panel se adapta a la pantalla y tiene un botón «Panel» para mostrarlo u ocultarlo. Incluye opciones para calibrar el seguimiento si algún eje sale al revés: modo espejo, invertir giro, inclinación o arriba/abajo, e intercambiar ojos.

Comprobado: el proyecto de Xcode compila y enlaza para iOS (arm64, sin firmar). No se ha probado en un dispositivo.
