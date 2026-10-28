"""Prepara las manos del cuerpo BodyV12 para que encajen con el antebrazo.

Lee KemonoPaws-v12.png (no lo modifica) y escribe KemonoPaws-v12-wrist.png, sin el trozo de antebrazo
dibujado encima del puño del guante. Mide en cada mano el borde superior del puño y guarda en
layout-v12.json la articulación (centro del puño) y el eje del puño, para que el antebrazo entre
centrado y perpendicular a la boca del guante. Uso: python3 Tools/clean_paw_wrists.py
"""
import json, os
import numpy as np
from PIL import Image

FOLDER = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Traigger', 'Art', 'BodyV12')
SOURCE, TARGET = 'KemonoPaws-v12', 'KemonoPaws-v12-wrist'
PIVOT_DEPTH = 40   # píxeles desde el borde superior del puño hasta la articulación
OUTLINE = 3        # píxeles de contorno oscuro que se conservan sobre el puño (grosor medido: 2 px)

layout_path = os.path.join(FOLDER, 'layout-v12.json')
layout = json.load(open(layout_path))
image = np.asarray(Image.open(os.path.join(FOLDER, SOURCE + '.png')).convert('RGBA')).copy()
H, W = image.shape[:2]

for part in layout['parts']:
    if part['name'] not in ('HandLeft', 'HandRight'):
        continue
    if part.get('texture') == TARGET:
        part['texture'] = SOURCE  # se mide siempre sobre el original
    L = round(part['x'] * W); T = round((1 - part['y'] - part['height']) * H)
    w = round(part['width'] * W); h = round(part['height'] * H)
    cell = image[T:T + h, L:L + w]
    r, g, b, a = [cell[..., i].astype(int) for i in range(4)]
    navy = (a > 100) & (b > r + 30) & (r < 120)
    # Borde superior del puño: primer píxel azul de cada columna en la mitad superior.
    tops = np.full(w, -1)
    for x in range(w):
        ys = np.where(navy[:int(.5 * h), x])[0]
        if len(ys):
            tops[x] = ys[0]
    cols = np.where((tops >= 0) & (tops < .42 * h))[0]
    lo, hi = cols.min(), cols.max()
    span = hi - lo
    fit_cols = cols[(cols > lo + .1 * span) & (cols < hi - .1 * span)]
    slope, intercept = np.polyfit(fit_cols, tops[fit_cols], 1)
    cx = (lo + hi) / 2; cy = slope * cx + intercept
    normal = np.array([-slope, 1.0]); normal /= np.linalg.norm(normal)   # hacia el interior del guante
    pivot = np.array([cx, cy]) + normal * PIVOT_DEPTH
    tip = pivot + normal * 200
    part['prox'] = {'x': round(float(pivot[0] / w), 4), 'y': round(float(pivot[1] / h), 4)}
    part['dist'] = {'x': round(float(tip[0] / w), 4), 'y': round(float(tip[1] / h), 4)}
    # Borra el trozo de antebrazo: todo lo que queda por encima del puño. En una franja de OUTLINE píxeles
    # se conserva el contorno oscuro, pero sólo si está unido al puño (no restos sueltos del dibujo).
    lum = .3 * r + .59 * g + .11 * b
    top_at = np.zeros(w, int)
    for x in range(w):
        top = tops[x] if tops[x] >= 0 and tops[x] < .42 * h else int(round(slope * x + intercept))
        top_at[x] = max(0, min(int(.5 * h), top))
    margin = np.zeros((h, w), bool)
    for x in range(w):
        lo_y = max(0, top_at[x] - OUTLINE)
        cell[:lo_y, x, 3] = 0
        margin[lo_y:top_at[x], x] = (lum[lo_y:top_at[x], x] < 90) & (a[lo_y:top_at[x], x] > 40)
    # Relleno desde el puño: se conserva el contorno conectado a una fila del puño.
    keep = np.zeros((h, w), bool); stack = []
    for x in range(w):
        y = top_at[x] - 1
        if 0 <= y < h and margin[y, x]:
            keep[y, x] = True; stack.append((y, x))
    while stack:
        y, x = stack.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and margin[yy, xx] and not keep[yy, xx]:
                keep[yy, xx] = True; stack.append((yy, xx))
    for x in range(w):
        lo_y = max(0, top_at[x] - OUTLINE)
        cell[lo_y:top_at[x], x, 3] = np.where(keep[lo_y:top_at[x], x], cell[lo_y:top_at[x], x, 3], 0)
    # Restos sueltos del dibujo que quedan separados de la mano: se quita todo salvo la pieza principal.
    solid = cell[..., 3] > 20; label = np.zeros((h, w), int); sizes = [0]
    for sy, sx in zip(*np.where(solid)):
        if label[sy, sx]:
            continue
        sizes.append(0); n = len(sizes) - 1; label[sy, sx] = n; stack = [(sy, sx)]
        while stack:
            y, x = stack.pop(); sizes[n] += 1
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < h and 0 <= xx < w and solid[yy, xx] and not label[yy, xx]:
                        label[yy, xx] = n; stack.append((yy, xx))
    main = int(np.argmax(sizes))
    cell[(label != main) & (label > 0), 3] = 0
    part['texture'] = TARGET
    print(part['name'], 'puño', lo, hi, 'inclinación', round(float(np.degrees(np.arctan(slope))), 1), 'prox', part['prox'], 'dist', part['dist'])

Image.fromarray(image).save(os.path.join(FOLDER, TARGET + '.png'))
json.dump(layout, open(layout_path, 'w'), indent=2, ensure_ascii=False)
print('escrito', TARGET + '.png')
