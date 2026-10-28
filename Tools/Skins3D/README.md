# Skins 3D: preparación en Blender

Scripts que convirtieron el modelo de Meshy (`SkyPaw`) en un skin animable por Traigger. Se ejecutan con Blender 5
en segundo plano (`Blender -b --python script.py -- argumentos`).

1. `shapes.py modelo.fbx salida.blend textura.png`: formas de la boca. `MouthClosed` junta los labios bajando el de
   arriba (el morro blanco acaba justo bajo la boca: si sube el de abajo, arrastra la barbilla); Traigger la usa como
   mucho al 60 % para la boca entreabierta. `MouthNarrow`: boca en «o». `MouthFill`: la boca es un hueco de 3–5 cm con la lengua al
   fondo; cerrada, se rellena hasta el morro para que de lado no se vea el hueco. `Smile`: comisuras.
2. `closed.py modelo.fbx textura.png cerrados.png mascara.png`: textura con los ojos cerrados (piel + línea de
   pestañas) y la boca cerrada (morro + boca de gato «ω»), y su máscara (R = ojo derecho del personaje, G = izquierdo,
   B = boca). El shader cambia de imagen al parpadear y al callarse; así no se deforma la malla, que en los ojos está
   muy troceada y se rasgaba.
3. `rig.py salida.blend final.fbx copia.blend`: huesos de la cola (`Tail1`…`Tail5`) y las orejas (`LeftEar`,
   `RightEar`) con sus pesos, y exporta el FBX.
4. `render.py` / `posetest.py`: renders de comprobación.

Las posiciones de ojos y boca (en metros, el modelo mira a -Y) están medidas sobre un render con rejilla; para otro
modelo hay que volver a medirlas.
