# Pelo repintado y flequillo central

Se han creado PNG nuevos mediante la herramienta integrada de edición de imágenes, no sólo efectos de material:

- Assets/Traigger/Resources/Hair/FrontHairHD.png: atlas 2508 × 2508, doble resolución; conserva las coordenadas de la capa FrontHair.
- Assets/Traigger/Resources/Hair/BackHairAtlasHD.png: misma disposición UV del pelo trasero del atlas actual.
- Assets/Traigger/Resources/Hair/BackHairHD.png: variante compatible con la textura trasera independiente BackHair-v2.

Los dos materiales del pelo utilizados en la escena actual ya apuntan a los nuevos PNG. HairDetailRig también los carga al ejecutar, eligiendo la variante trasera correspondiente. Se desactivan para estos PNG el teñido y afilado anteriores del shader; el degradado está pintado en el archivo.

La integración conserva el contorno útil de los sprites originales con suavizado subpíxel y elimina motas desconectadas del recorte. El repintado aporta el color nuevo; no se usan sus posibles cambios de silueta para mover la pieza. Las imágenes originales del usuario permanecen disponibles.

El mechón central Fringe1 ahora describe un arco desde la raíz, con amortiguación próxima a crítica, menor fuerza y una máscara más estrecha para no arrastrar mechones vecinos. Se conservan huesos, UV, pesos y correctivos originales. La subdivisión y los correctivos adicionales se crean sobre una copia temporal durante la ejecución.

## Validación

Comprobado en Unity 6000.5.1f1: sprites reales cargados, resolución frontal 2508, vértices y UV originales sin cambios, correctivos previos y huesos conservados, raíz central fija, deformación acotada y retorno a reposo. Capturas neutral.png, right.png y left.png en Validation/HairRepaint. Compilación C# comprobada aparte. Falta evaluar el movimiento con seguimiento real en iPad/Vision Pro.

Para repetir: Traigger → Arte → Comprobar sprite repintado y flequillo. Para probar el movimiento, entrar en Play. Para verlo en el iPad/Vision Pro hace falta una nueva compilación.

Backups del script y de los materiales anteriores: Validation/HairRepaint/Backup.

## Prompts del repintado (herramienta integrada image_gen)

Frontal: editar el sprite de pelo frontal kemono conservando exactamente silueta, peinado, posición, tamaño, márgenes transparentes, cuatro mechones superiores, flequillo central y límites de las mechas azules. Limpiar contornos y píxeles sueltos. Pintar degradado azul petróleo en la raíz → turquesa en medios → cian claro en puntas, con luces cel shading contenidas y marrón chocolate oscuro; sin pelo realista, sin mechones nuevos, cara, orejas ni objetos añadidos.

Trasero: repintar el mismo sprite kemono de pelo trasero, conservando contorno, posición, márgenes y puntas originales. Contornos oscuros definidos y sin halos; degradado azul petróleo → turquesa → cian claro dentro de las zonas azules existentes, marrón chocolate oscuro y sombreado ilustrado del original. Fondo transparente, sin rediseñar el peinado ni añadir cara, orejas o texto.
