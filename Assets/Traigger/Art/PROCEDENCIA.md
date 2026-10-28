# Procedencia del arte

Herramienta: image_gen integrada. Las dos ilustraciones originales están en References/ sin modificar. El atlas es una reconstrucción generada, no una separación exacta de píxeles del original. Las zonas ocultas y algunas proporciones son interpretaciones.

Primer prompt:

Use case: identity-preserve. Asset type: a single production texture atlas for a 2D deformable Unity VTuber.
Use attached reference 1 front turnaround as exact identity/clothing/proportion reference, reference 2 only detail guide. Reconstruct Traigger's hidden surfaces as necessary. Create ONE transparent PNG atlas, square 2048x2048, exactly 4 equal columns and 4 equal rows, sixteen isolated parts, one per cell, generous transparent gutters, no text, labels, checkerboard, grid lines or shadows.
Every part must be front facing, neutral orthographic, smooth thin dark outlines and flat anime colors exactly matching the first reference: pale golden fur, dark nearly black hair with cyan tips and streaks, turquoise eyes, blue oversized shirt, mint shorts, blue fingerless gloves and ankle wraps.
CELL CONTENTS reading left to right top to bottom:
row 1: (1) bald furry head base with cheek tufts, pale cream muzzle area, tiny brown nose, white nasal bandage and pink cheeks, NO eyes eyebrows mouth hair or ears; (2) front fringe and side locks together, hollow transparent eye/face region, black with cyan tips, shape same frontal reference; (3) left on-screen triangular fox ear with pink interior and cream tufts, brown tip; (4) right on-screen ear matching.
row 2: (5) oversized blue shirt torso with short golden neck visible and open short sleeves, dark teal collar and sleeve/hem trim, NO arms hands or shorts; (6) mint shorts front with two leg openings and pale cuffs, NO body legs tail; (7) entire left on-screen arm from rounded upper arm to relaxed gloved hand fingertips, pale golden fur with blue fingerless glove, slopes down slightly left; (8) entire right on-screen arm analogous slopes down slightly right.
row 3: (9) left on-screen lower leg and furry paw foot together, golden fur blue ankle wrap, straight vertical; (10) right on-screen lower leg and paw foot together; (11) large fluffy fox tail curved outward right, pale gold base and warm brown outer tip with jagged boundary; (12) back hair silhouette, black crown and cyan tips, matching bob length.
row 4: (13) left on-screen open turquoise eye complete white sclera black outline pupil highlights; (14) right on-screen eye; (15) small open smiling mouth interior with pink tongue and little fang, NO muzzle skin surrounding; (16) two thin dark eyebrows as a pair.
Each shape centered and entirely contained within its cell. Eye and mouth parts large within their own cells, they will be scaled down when assembled. Draw only requested detached component, never a complete assembled person. Actual transparent background. Preserve character identity; no redesign.

Prompt de corrección:

Edit this texture atlas only. Preserve all sixteen detached Traigger components exactly in their existing positions and proportions. Clean the transparent edges meticulously: remove ALL stray colored pixels, yellow/red/cyan halos, scattered speckles and white residue outside the drawn components. All outlines should be clean dark anime outlines adjoining truly transparent background, no bright fringe. Correct only the left and right ARMS (row two columns three and four): swap their orientation horizontally, so the on-screen LEFT arm has shoulder at upper RIGHT and fingertips at lower LEFT, and the on-screen RIGHT arm has shoulder at upper LEFT and fingertips at lower RIGHT. Leave all other cells, colors, content and positions unchanged. Transparent PNG, not a checkerboard.

El segundo resultado corrige la orientación de los brazos, pero aún conserva halos/píxeles aislados que requieren retoque artístico. Los archivos fuente conservan su canal alfa. Las UV delimitan las piezas sin modificar el atlas. La carpeta Art contiene la primera propuesta y la corregida; la receta usa TraiggerParts-clean.png.


## Pelo posterior v2 — 19 de octubre de 2026

Generado con image_gen integrada; guardado como BackHair-v2.png. Referencias: ilustración original IMG_1833.JPG y atlas TraiggerParts-clean.png. Reemplaza la pieza posterior anterior, manteniendo el flequillo original del atlas. Se ajustaron los límites UV sin editar los píxeles.

Prompt utilizado:

Create one clean transparent PNG game asset: ONLY the back hair mass for the fox VTuber Traigger in reference 1, with reference 2 (the current parts atlas) for exact color matching. Front view of the BACK HAIR layer as it would be seen from behind the face: a single continuous soft bob silhouette, rounded crown, narrow slightly inward curving lower sides framing the nape, black/dark brown hair, cyan lower ends as in the reference. All strands connect to the crown. NO face, skin, eyes, ears, neck, clothing, torso, labels or surrounding objects. Rounded overall shape slightly taller than wide. The cyan ends taper into a few fine downward tufts along one cohesive curved bottom silhouette, not several stacked horizontal bands. Avoid an abrupt horizontal cut, stepped shoulders, disconnected locks, large spikes, duplicate outer outlines or concentric rows of tips. Anime thin dark line style matching reference 1. Sides fairly narrow so this layer can sit mostly concealed behind the existing front hair. No shine bands or decorative elements. Centered isolated asset, generous transparent margin, actual transparent background. Do not draw the front fringe/bangs. This replaces only the separate rear hair layer; exact frontal hair remains unchanged.

## Retoques del usuario y conexión actual

El usuario ha recortado partes del pelo en TraiggerParts-clean.png. Ese archivo se conserva íntegro y es ahora la fuente de FrontHair y BackHair. BackHair-v2.png ya no se referencia en assembly.json ni en la receta regenerada. Los cambios de esta revisión son deformaciones de malla/controladores y conexión de texturas; no se han generado ni editado imágenes nuevas. Validation/user-atlas-preserved.txt registra el hash conservado.
