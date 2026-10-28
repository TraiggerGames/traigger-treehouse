# Solución de problemas

## Compilar

| Síntoma | Causa y solución |
|---|---|
| Unity avisa de que falta `VLCKit.framework` o el vídeo no abre nada | No has instalado las bibliotecas: `Tools/fetch_thirdparty.sh` y, en el Mac, `MacCamera/MoviePlugin/build.sh`. Reinicia Unity para que cargue el plugin nuevo. |
| «Traigger: sin MediaPipe…» en la consola | Normal si no lo instalaste; solo afecta al modo HDMI híbrido. |
| «sin equipo de firma» | Copia `signing.example` como `signing.local` y pon tu equipo. |
| Xcode: el identificador `com.traigger…` no está disponible | Usa tus propios identificadores: [COMPILAR.md](COMPILAR.md#identificadores-de-la-app). |
| La sincronización con iCloud no funciona | El identificador de la app y el contenedor `iCloud.<identificador>` deben existir en tu cuenta de Apple, y la app firmada con iCloud. |
| `fetch_thirdparty.sh`: «la suma SHA-256 no coincide» | La descarga cambió o llegó corrupta. Vuelve a intentarlo; si persiste, revisa la versión en [TERCEROS.md](TERCEROS.md). |

## Vídeo

| Síntoma | Causa y solución |
|---|---|
| La carpeta SMB falla aunque haya red | El servidor estaba dormido o el disco arrancando. Traigger reintenta solo dos veces. Si sigue, usa la IP fija del servidor en «Editar servidor» en lugar de `nombre.local`. |
| «El servidor rechazó la conexión» | Usuario, contraseña o ruta incorrectos, o el servidor no permite SMB2/3. |
| Saltos o cortes en vídeos por red | Red lenta o Wi-Fi saturada; Traigger ya guarda 3 s por delante. Prueba con cable en el servidor o 5 GHz. |
| El sonido va desfasado | Se corrige solo en unos segundos. Con AirPods el retardo se compensa al conectarlos; si cambias de salida en mitad del vídeo, pausa y reanuda. |
| No suena con el modo silencio | No debería pasar: la app cambia la sesión de audio a reproducción. Si el micrófono está activo con otra app, puede que esta lo impida. |

## Red y aparatos

| Síntoma | Causa y solución |
|---|---|
| No aparece el Mac / iPad / Vision Pro | Comprueba el permiso de **Red local** (Ajustes → Privacidad → Red local) y que estén en la misma Wi-Fi. Algunas redes de invitados o de empresa bloquean Bonjour. |
| No llega la cara del iPhone al Mac | Abre **Traigger Camera** en el Mac (es la que recibe) y elige el Mac como destino en el iPhone. |
| La capturadora va con retraso | Usa el perfil «Juego», H.264 y 5 GHz; el estado muestra la latencia de cada destino. |
| La cámara virtual no aparece en FaceTime | Instálala desde el menú de Traigger Camera y aprueba la extensión en Ajustes del Sistema → General → Inicio de sesión y extensiones. Tras actualizarla, sube `CFBundleVersion`. |

## Personaje

| Síntoma | Causa y solución |
|---|---|
| Sale un avatar de prueba en vez del personaje | La escena compilada no es la del estudio: compila con los menús **Traigger → 4/5** (usan `IOSBuild.Scene`). |
| El pelo rebota demasiado o poco | Ajustar → Pelo; «Restablecer el pelo» vuelve a los valores de fábrica. |
| Una pieza del outfit queda descolocada | Ajusta su `pad` o su capa de `fit` ([TRAJES.md](TRAJES.md)). |
