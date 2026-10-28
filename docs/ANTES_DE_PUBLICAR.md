# Antes de publicar en GitHub

Lista de lo que queda por decidir o hacer antes de que el repositorio sea público.

## Decisiones del autor

- [x] **Licencia del código: MIT** (`LICENSE`), que excluye expresamente el arte de Traigger.
- [x] **Arte de Traigger: todos los derechos reservados** (`LICENSE-ART.md`). Para otros creadores hay una plantilla libre CC0 en `Examples/Plantilla`.
- [x] **Nombre del repositorio: Traigger Treehouse** (y si «Traigger» queda como nombre del programa o solo del personaje).

## Datos personales (revisado)

- [x] Firma fuera del código: `signing.local` (no se sube), `signing.example` como plantilla.
- [x] Dejar vacío *Signing Team ID* en Unity (**Player Settings → iOS y visionOS**) y guardar el proyecto: `ProjectSettings.asset` aún contiene el equipo.
- [x] Revisar `Library/`, `UserSettings/`, `Builds/` y `Logs/`: ya están en `.gitignore`.
- [x] (movidos a `docs/desarrollo/`) Revisar los documentos antiguos de la raíz (`LLAMADAS-FACETIME.md`, `MUECAS-KEMONO.md`, `OJOS-Y-MIRADA.md`, `PANEL-Y-CAMARAS.md`, `PELO-Y-FLEQUILLO.md`, `Vista-previa-Traigger.html`) y `Validation/`: moverlos a `docs/` o retirarlos.

## Código reservado para la app comercial

- [x] (versión abierta de un solo camino en el repositorio; la completa guardada fuera) **No publicar el envío HDMI de baja latencia** (`Assets/Traigger/Plugins/Apple/TraiggerLink.swift` y la parte de envío de `TraiggerCapture.swift`): se reutilizará en una app de pago aparte. Sacarlo a un paquete privado o excluirlo del repositorio público antes de subirlo (con MIT, cualquiera podría usarlo).

## Binarios

- [x] (script `Tools/fetch_thirdparty.sh`) **VLCKit** (`Plugins/iOS`, `Plugins/visionOS`, dentro del plugin de Mac): son binarios grandes con licencia LGPL. Elegir entre subirlos con **Git LFS** o un script que los descargue (`Tools/fetch_vlckit.sh`).
- [x] (se compilan con `build.sh`, no se suben) Plugins compilados del Mac (`TraiggerMoviePlayer.bundle`, `TraiggerVirtualCam.dylib`): subirlos con LFS o pedir compilarlos con `build.sh` (llevan la firma de quien los compila).

## Para que otros empiecen fácil

- [x] README, guía de paquetes de personaje, compilación, arquitectura, terceros y cómo colaborar.
- [x] Paquete de ejemplo en `Examples/Traigger` (y `Traigger.traigger`).
- [ ] Capturas o un vídeo corto para el README.
- [ ] Plantillas de *issues* y de *pull requests* (`.github/`).

## Pendiente de confirmar

- [x] **Piel 3D SkyPaw**: generada con un plan de pago de Meshy (el modelo es del autor); se menciona Meshy en `LICENSE-ART.md`.
