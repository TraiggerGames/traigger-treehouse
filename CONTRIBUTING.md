# Colaborar con Traigger

¡Gracias por querer ayudar! Todo suma: errores, ideas, traducciones, personajes nuevos y código.

## Antes de empezar

- Lee [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md), [docs/COMPILAR.md](docs/COMPILAR.md) y [docs/RED_Y_PROTOCOLOS.md](docs/RED_Y_PROTOCOLOS.md).
- Instala las bibliotecas de terceros: `Tools/fetch_thirdparty.sh` ([docs/TERCEROS.md](docs/TERCEROS.md)).
- Copia `signing.example` como `signing.local` con tus datos de firma de Apple (no se sube).

## Reglas del proyecto

1. **Todas las plataformas:** una función nueva tiene que funcionar en iPhone/iPad, Vision Pro y Mac, o explicar por qué no puede (y degradar con un mensaje claro).
2. **Interfaz nativa y amigable:** SwiftUI compartida en `Assets/Traigger/Plugins/Apple/UI`; nada de OnGUI fuera del editor. Textos claros, una acción principal por pantalla y estados vacíos que expliquen qué hacer.
3. **Privacidad:** voz, cara y sonido se procesan en el dispositivo. Nada sale del aparato salvo lo que el usuario emite o comparte.
4. **Unity mínimo:** sin módulos nuevos (no hay física); el C# debe compilar contra los módulos que ya usa el proyecto.
5. **Sin datos personales** en el código (equipos de firma, nombres, rutas de tu ordenador).

## Cómo enviar cambios

1. Haz un *fork* y una rama con un nombre descriptivo.
2. Comprueba que compila (C# en Unity y la interfaz Swift en las tres plataformas, ver COMPILAR.md).
3. Abre un *pull request* explicando qué cambia y cómo lo has probado (y en qué aparatos).

## Personajes

Para compartir tu personaje no hace falta tocar código: crea un [paquete de personaje](docs/PAQUETES_DE_PERSONAJE.md). Si quieres que aparezca como ejemplo en `Examples/`, indica su licencia en `character.json`.
