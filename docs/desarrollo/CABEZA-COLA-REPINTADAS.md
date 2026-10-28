# Cabeza y cola con el acabado del pelo

Repintado realizado con la herramienta integrada image_gen. Se conservan la identidad kemono, amarillo/crema de la cara, orejas rosadas y patrón marrón/crema de la cola. Las nuevas sombras miel/caramelo y reflejos cálidos armonizan con el pelo HD.

## Recursos activos

- Assets/Traigger/Resources/Coat/CoatAtlasHD.png: atlas 2508 × 2508 de cabeza, carrillos, orejas y cola, en las mismas coordenadas UV del atlas original. Los demás sprites no se modifican.
- Los materiales de HeadShadingMaterials 3.asset y el material de cola en TraiggerMeshData 127.asset apuntan a ese PNG; se desactiva el sombreado anterior para no aplicarlo dos veces.
- La cola también se incorpora como material Tail_celShaded al conjunto que el exportador reaplica por nombre. No hace falta un componente nuevo en ejecución.

La tirita se usa como punto de registro para mantener alineados nariz y rasgos de la cara. El contorno útil se conserva a partir de los sprites originales, con antialiasing subpíxel y limpieza de motas aisladas. El color procede del repintado, no sólo de efectos del shader. Los PNG fuente originales y el pelo HD no se sobrescriben.

## Rig y validación

Se han comparado los bloques serializados: sólo cambian Material; mallas, pesos, UV, huesos y definiciones del rig permanecen idénticos. La escena no se reconstruye.

Probado en Unity 6000.5.1f1: atlas cargado en HeadBase, CheekLeft/Right, EarLeft/Right y Tail; pelo HD conservado; capturas de cara neutral, mofletes hinchados, cola y cuerpo completo. Prueba de registro del alfa y del atlas superada, ZIP y hashes de los originales verificados. Las capturas y resultados están en Validation/CoatRepaint. Sigue pendiente evaluar el resultado en iPad/Vision Pro.

Para repetir las capturas: Traigger → Arte → Comprobar cabeza y cola repintadas. Para verlo en los dispositivos se necesita una nueva compilación.

## Copia de seguridad y rollback

**Rollback/Traigger-originales-pelo-cabeza-cola-2026-10-25.zip** contiene:

1. Originales anteriores al repintado del pelo, con los atlas de cabeza/cola y sus materiales.
2. Estado exacto anterior a este cambio de cabeza y cola, conservando el pelo HD.
3. PNG, metadatos, materiales, script del pelo, instantánea de escena, manifiesto SHA-256 e instrucciones.

Para volver al acabado anterior de cabeza y cola sin tocar el pelo ni el rig: salir de Play y ejecutar **Traigger → Arte → Rollback cabeza y cola (conservar pelo y rig)**. La herramienta restaura sólo los bloques Material del ZIP y guarda otra copia del acabado actual antes de hacerlo.

No sustituir la escena ni TraiggerMeshData 127.asset completos si se han editado los huesos o las mallas después de esta copia. El ZIP incluye una guía específica para la restauración completa anterior al pelo.

## Prompts

Los tres prompts exactos y el método están guardados en Validation/CoatRepaint/PROMPTS.json. Se usaron imágenes separadas como objetivo de edición y la captura del pelo HD como referencia de estilo.
