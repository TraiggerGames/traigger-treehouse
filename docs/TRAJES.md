# Outfits (trajes)

Un outfit cambia la ropa de un personaje **sin duplicarlo**: la cara, el pelo, las orejas y la cola siguen siendo los del
personaje (si mejoras su cara, todos sus outfits lo heredan). Se elige en **Inicio → Outfit**.

Ejemplo completo: `Assets/StreamingAssets/Outfits/Traigger/Calabaza/` (arte con todos los derechos reservados: úsalo
solo como referencia técnica).

## Dónde va

```
Assets/StreamingAssets/Outfits/<Personaje>/<Outfit>/
    outfit.json
    preview.png          (opcional: miniatura para el selector)
    *.png                (las piezas)
```

`<Personaje>` es el nombre del personaje al que pertenece (por ejemplo `Traigger`).

## outfit.json

```json
{
  "format": 1,
  "name": "Calabaza",
  "character": "Traigger",
  "author": "Tu nombre",
  "license": "CC BY 4.0",
  "hide": ["Shirt", "Shorts", "Kemono_Torso"],
  "options": [ { "id": "hood", "label": "Capucha", "on": true } ],
  "pieces": [
    { "name": "PumpkinTorso", "texture": "PumpkinTorso.png", "fit": "Kemono_Torso", "pad": [0, 0, 0, -0.09], "orderOffset": 1 },
    { "name": "Hood", "texture": "Hood.png", "rect": [-0.5, 1.2, 1.0, 0.8], "weightsFrom": "HeadBase", "option": "hood" }
  ]
}
```

| Campo | Qué hace |
|---|---|
| `hide` | Capas del personaje que se ocultan (la ropa de siempre). Los nombres son los de las capas del rig. |
| `options` | Interruptores que ve el usuario (`id`, `label`, `on` = valor inicial). Se guardan por outfit. |
| `pieces` | Capas nuevas, una por PNG. |

### Campos de cada pieza

| Campo | Qué hace |
|---|---|
| `name` | Nombre de la capa nueva. |
| `texture` | El PNG (con transparencia). |
| `fit` | Se encaja sobre una capa existente: la parte opaca del PNG ocupa la caja de esa capa y **copia sus pesos de huesos y sus correctivos**, así se dobla igual que la ropa que sustituye. Es la forma recomendada. |
| `pad` | Margen en fracción del tamaño de la caja: `[izquierda, abajo, derecha, arriba]`. Negativo encoge. |
| `fullImage` | `true`: el PNG entero (no solo lo opaco) ocupa la caja. Para piezas recortadas del propio dibujo, como un cuello. |
| `fitTexture` | Otra imagen para medir la caja (cuando el PNG visible no tiene la misma silueta). |
| `rect` | En vez de `fit`: posición fija `[x, y, ancho, alto]` en el espacio del personaje. |
| `weightsFrom` | Con `rect`: de qué capa copia los pesos (por ejemplo `HeadBase` para una capucha que sigue a la cabeza). |
| `orderFrom` | Capa cuyo orden de dibujo se toma (si no, el de la capa encajada). |
| `orderOffset` | Se suma al orden: `1` delante, `-1` detrás. |
| `option` | `id` de una opción: la pieza solo sale si está activa. |
| `sleeveSide`, `sleevePart` | Mangas por capas: lado (`Left`/`Right`) y parte (`Cloth`, `Cuff`, `Lining`). Se doblan con el brazo y dibujan forro, tela y puño en su orden. |

## Consejos

- Empieza ocultando solo lo que tapas y añade piezas de una en una; pulsa Play en Unity para ver cada cambio.
- Si una pieza se ve desplazada, ajusta `pad` antes que mover el dibujo.
- Para ver los nombres de las capas, abre la escena en Unity y mira los objetos hijos del personaje.
- Si un traje cambia mucho la silueta (alas, faldas largas), pon las partes nuevas con `rect` + `weightsFrom`.
- El outfit elegido se guarda en el aparato; las opciones también, por outfit.
