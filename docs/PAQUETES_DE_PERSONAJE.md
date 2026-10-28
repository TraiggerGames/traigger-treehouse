# Paquetes de personaje: crea tu propio VTuber

Traigger puede cargar cualquier personaje 2D dibujado por piezas. No necesitas Unity ni programar: solo tus dibujos en PNG y un archivo de texto que describe cómo se montan.

**Empieza por la plantilla libre** [`Examples/Plantilla`](../Examples/Plantilla) (CC0): tiene una imagen por pieza con el mismo nombre que su capa; sustituye cada PNG por tu dibujo y ajusta `rect` si cambian las proporciones. `Examples/Traigger` sirve como referencia técnica de un personaje real (un único atlas con todas las piezas), pero **Traigger es un personaje propio y no se puede reutilizar** ([LICENSE-ART.md](../LICENSE-ART.md)).

## 1. Qué hay en un paquete

```
MiPersonaje/
├── character.json      ← cómo se monta (este documento)
├── piezas.png          ← tus dibujos con transparencia (uno o varios PNG)
└── preview.png         ← opcional: miniatura para el selector (cuadrada, ~256 px)
```

Para compartirlo, comprime la carpeta en un `.zip` y cámbiale la extensión a `.traigger` (o déjalo como `.zip`: valen los dos). En la app: **Inicio → Personaje → Importar…**, elige la carpeta o el archivo y tócalo para usarlo. Cambiar de personaje reinicia el estudio. El encuadre acepta `cameraX` (centro horizontal, por defecto `0`) y `cameraHeight` (centro vertical), además de `cameraSize`.

## 2. Dibuja las piezas

El personaje se mueve deformando cada pieza con huesos, así que cada parte que se mueve por separado va en su propia pieza:

- **Cabeza base** (sin ojos, boca, cejas, pelo ni orejas), **ojos** (cada uno aparte), **boca abierta**, **cejas**.
- **Pelo delantero** (flequillo) y **pelo trasero**, **orejas**, **cola**.
- **Torso con ropa**, **brazos** (cada uno entero: hombro → mano), **piernas**, **pies**.

Consejos:
- **Fondo transparente de verdad** (PNG con canal alfa), sin halos de color en los bordes.
- Dibuja de frente, con la pose neutra (brazos algo separados del cuerpo).
- Pinta un poco más de lo que se ve donde las piezas se solapan (bajo el pelo, en hombros y cuello): al moverse no quedarán huecos.
- Puedes juntar todas las piezas en una sola imagen (un *atlas*) o usar una imagen por pieza.

## 3. Coordenadas

- **Unidades del personaje:** el suelo está en `y = 0` y el centro horizontal en `x = 0`. Traigger mide unas 4,4 unidades de alto (cabeza en `y ≈ 3,5`). Usa la escala que quieras y ajusta el encuadre con `cameraSize` y `cameraHeight`.
- **`y` crece hacia arriba.**
- **`uv`** (la zona de la imagen): `[x, y, ancho, alto]` de 0 a 1, **origen abajo a la izquierda** de la imagen.
- **`rect`** (dónde va la pieza): `[x, y, ancho, alto]` en unidades del personaje; `x, y` es la esquina inferior izquierda.

## 4. `character.json`

```json
{
  "format": 1,
  "name": "Mi personaje",
  "author": "Tu nombre",
  "version": "1.0",
  "license": "CC BY 4.0",
  "description": "Una frase sobre tu personaje",
  "website": "https://…",
  "cameraSize": 2.8,
  "cameraHeight": 2.45,
  "bones": [
    { "name": "Hips",  "parent": -1, "x": 0, "y": 1.95 },
    { "name": "Spine", "parent": 0,  "x": 0, "y": 2.28 },
    { "name": "Chest", "parent": 1,  "x": 0, "y": 2.62 },
    { "name": "Neck",  "parent": 2,  "x": 0, "y": 2.95 },
    { "name": "Head",  "parent": 3,  "x": 0, "y": 3.2 }
  ],
  "layers": [
    {
      "name": "HeadBase",
      "texture": "piezas.png",
      "uv": [0.0, 0.75, 0.25, 0.25],
      "rect": [-0.6, 2.9, 1.2, 1.2],
      "bones": [4],
      "order": 10
    }
  ]
}
```

### Datos del personaje

| Campo | Qué es |
|---|---|
| `format` | Versión del formato (ahora `1`). |
| `name`, `author`, `version`, `description`, `website` | Se muestran en el selector. |
| `license` | Cómo pueden usar otros tu personaje (p. ej. `CC BY 4.0`, `Todos los derechos reservados`). Se muestra al mantener pulsado el personaje. |
| `cameraSize` | Alto visible en el encuadre, en unidades (2,8 para Traigger). |
| `cameraHeight` | Altura del centro del encuadre (2,45 para Traigger). |

### Huesos (`bones`)

Cada hueso tiene `name`, `parent` (el índice de su padre en la lista, o `-1` para la raíz) y su posición de reposo `x, y` **absoluta**. **El padre siempre va antes que el hijo** y los nombres no se repiten.

### Capas (`layers`)

| Campo | Por defecto | Qué es |
|---|---|---|
| `name` | — | Nombre de la pieza (mira los nombres estándar abajo). |
| `texture` | — | Archivo PNG dentro del paquete. |
| `uv` | `[0,0,1,1]` | Zona de la imagen. |
| `rect` | `[-0.5,0,1,1]` | Dónde va en el personaje. |
| `bones` | — | Índices de los huesos que mueven la pieza (uno o varios: un brazo usa hombro, codo y mano). |
| `order` | `0` | Orden de dibujo: mayor = delante. |
| `face` | `None` | `BlinkLeft` / `BlinkRight` para un ojo (parpadea) o `Mouth` para la boca abierta (se abre y se cierra). |
| `columns`, `rows` | `12`, `16` | Densidad de la malla (2–40). Más alto = deformación más suave. |
| `falloff` | `2` | Cómo de rápido pasa el peso de un hueso al siguiente. |
| `tint` | `#FFFFFF` | Color multiplicado (`#RRGGBB` o `#RRGGBBAA`). |
| `topCenterExtension` | `0` | Alarga el centro superior (para solapar cuello y cabeza). |
| `bottomCenterCutout` | `[0,0]` | Hueco central inferior (ancho, alto normalizados): evita pelo sobre el cuello. |
| `cheekSpread` | `0` | Abre los lados (carrillos) para que asome el pelo. |
| `sideCutout` | `[0,0,0]` | Quita los laterales (ancho, alto mín., alto máx.) cuando otra pieza los sustituye (mangas). |

## 5. Nombres estándar (activan las mejoras)

Con estos nombres, Traigger activa sus animaciones especiales; sin ellos el personaje funciona igual, pero sin esa mejora.

**Huesos** (convención humanoide de Unity): `Hips`, `Spine`, `Chest`, `Neck`, `Head`, `Hair`, `LeftEar`, `RightEar`, `LeftEye`, `RightEye`, `LeftShoulder`, `LeftUpperArm`, `LeftLowerArm`, `LeftHand`, `RightShoulder`, `RightUpperArm`, `RightLowerArm`, `RightHand`, `LeftUpperLeg`, `LeftLowerLeg`, `LeftFoot`, `LeftToes`, `RightUpperLeg`, `RightLowerLeg`, `RightFoot`, `RightToes`, `Tail1`, `Tail2`, `Tail3`.

**Capas:**

| Capa | Mejora |
|---|---|
| `HeadBase` | Giros de cabeza, carrillos, arrugar la nariz y olfateo |
| `FrontHair`, `BackHair` | Pelo con inercia (flequillo, lados y parte de atrás) |
| `EarLeft`, `EarRight` | Orejas flexibles con emoción |
| `EyeLeft`, `EyeRight` (con `face: BlinkLeft/BlinkRight`) | Parpadeo y mirada |
| `Eyebrows` | Cejas con las expresiones |
| `Mouth` (con `face: Mouth`) | Boca con la voz y la cara |
| `Tail` (huesos `Tail1…3`) | Cola con inercia y emoción |
| `CheekLeft`, `CheekRight` | Mofletes (inflar carrillos) |
| Brazos y piernas con sus huesos estándar | Seguimiento de brazos, manos y piernas, y gestos con la voz |

**Ojo con los lados:** en los **huesos**, izquierda y derecha son **las del personaje** (como frente a un espejo: `RightEar` es la oreja que ves a la izquierda de la pantalla). En las **capas**, son **las de la pantalla**. Por eso en el ejemplo la capa `EarLeft` usa el hueso `RightEar`.

## 6. Comprobar y arreglar

Si algo está mal, la app lo dice al importar o al cargar, y vuelve a Traigger:

| Mensaje | Arreglo |
|---|---|
| Falta character.json | El archivo tiene que estar en la raíz del paquete (o en una única carpeta dentro del zip). |
| Falta la imagen X | El nombre de `texture` tiene que coincidir exactamente con el archivo (mayúsculas incluidas). |
| Nombres únicos y padres anteriores al hijo | Revisa `parent`: siempre un índice anterior; ningún nombre repetido. |
| Capa no válida | `rect` con ancho y alto > 0, `columns/rows` entre 2 y 40 y al menos un hueso en `bones`. |
| Índice de hueso | Un número de `bones` no existe en la lista de huesos. |

## 7. Desde Unity (opcional)

Si prefieres editar con herramientas visuales: en Unity, **Traigger → AutoRig** permite colocar huesos, recortar las piezas y ver la malla, y **Traigger → Exportar personaje como paquete…** guarda la receta seleccionada como paquete listo para compartir.
