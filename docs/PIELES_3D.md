# Skins 3D (experimental)

Además del rig 2D, Traigger puede mover un **modelo 3D con esqueleto**. Aparece en **Inicio → Personaje** con el prefijo
`3d:`. El ejemplo incluido es `SkyPaw` (generado con Meshy y preparado en Blender; todos los derechos reservados).

## Archivos

```
Assets/Traigger/Resources/Skins3D/<Nombre>/
    <Nombre>.fbx             modelo con esqueleto y formas (blendshapes)
    <Nombre>_color.png       textura de color
    <Nombre>_normal.png      (opcional) mapa de normales
    <Nombre>_closed.png      (opcional) la misma textura con ojos y boca cerrados pintados
    <Nombre>_blinkmask.png   (opcional) máscara: R = ojo derecho, G = ojo izquierdo, B = boca
```

El importador (`Editor/Skin3DImport.cs`) configura el FBX solo: tipo Generic (conserva la piel), lectura activada,
blendshapes y texturas ASTC. El sombreado es toon (`Skins3D/Skin3D.shader`).

## Lo que espera el modelo

| | Nombres |
|---|---|
| Huesos del cuerpo | `Hips`, `Spine`, `Neck`, `Head`, `LeftUpperArm`, `LeftLowerArm`, `LeftHand`, `RightUpperArm`, `RightLowerArm`, `RightHand` |
| Física (opcional) | `Tail1`…`Tail5`, `LeftEar`, `RightEar` (muelles) |
| Referencia | Un nodo `headfront` delante de la cara (marca hacia dónde mira) |
| Formas de la boca | `MouthClosed`, `MouthNarrow` (boca en «o»), `MouthFill` (rellena el hueco de la boca), `Smile` |

El parpadeo y la boca cerrada del todo se hacen **por textura** (`_closed` + `_blinkmask`) para no deformar mallas muy
troceadas. La cabeza y el cuello siguen la cara; los brazos, la expresión y el seguimiento; la cola y las orejas, sus
muelles.

## Preparar un modelo con Blender

Los scripts de `Tools/Skins3D` (ver su README) hacen el trabajo con Blender 5 en segundo plano:

1. `shapes.py`: crea las formas de la boca.
2. `closed.py`: pinta la textura de ojos y boca cerrados y su máscara.
3. `rig.py`: añade los huesos de cola y orejas con sus pesos y exporta el FBX.
4. `render.py`, `posetest.py`: renders para comprobarlo.

Las posiciones de ojos y boca están medidas para SkyPaw: para otro modelo hay que volver a medirlas en los scripts.

## Problemas conocidos

- Si la barbilla se hunde al hablar, la piel de la barbilla tiene pesos del cuello o los hombros: el script la pasa a
  `Head` al cargar (`FixCollar` en `Skin3DAvatar.cs`) y lo indica en la consola («skin 3D cuello: …»).
- Los skins 3D solo están en el rig incluido en la app, no en paquetes `.traigger`.
