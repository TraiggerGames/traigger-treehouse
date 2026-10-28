# Sombreado de cabeza

Aplicado en TraiggerStudio-KemonoBody con materiales nuevos. Dos texturas generadas con la herramienta integrada image_gen, usadas únicamente como referencia RGB de luces y sombras: Traigger-head-shading-v1.png y BackHair-shading-v1.png. Prompts íntegros en PROMPTS.json.

No se sustituyó la transparencia original ni se movieron sprites, vértices, UV, huesos, pesos o blendshapes. El shader transfiere iluminación de forma limitada para mantener la paleta original y proteger la tinta. Los PNG generados NO deben sustituir directamente los atlas originales: su geometría y alfa generados no son la referencia del rig. Sólo se emplean en _HeadShadeTex, con la geometría y alfa originales.

Intensidad: 0.7 en pelo y 0.85 en piel/orejas. Ajustable mediante Head cel shading en cada material. Ponerla en cero restaura el aspecto original sin reconstruir. Ojos, boca y cejas mantienen sus materiales.

Validación en Unity: 7 materiales modificados; referencias de mallas y transformaciones de huesos idénticas. Capturas before/after e informe en Validation/HeadShading. Copia de escena anterior en scene-before-head-shading.unity.bak. No se ha reconstruido el rig. El sombreado se guarda en esta escena; una reconstrucción completa de los recursos originales requiere volver a aplicar el menú Traigger > Arte > Sombrear cabeza sin modificar rig.
