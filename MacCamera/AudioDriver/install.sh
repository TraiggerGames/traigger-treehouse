#!/bin/sh
# Instala el dispositivo de audio «Traigger» y reinicia el servicio de audio (pide la contraseña de administrador).
set -e
cd "$(dirname "$0")"
sudo rm -rf /Library/Audio/Plug-Ins/HAL/TraiggerAudio.driver
sudo cp -R build/TraiggerAudio.driver /Library/Audio/Plug-Ins/HAL/
sudo killall coreaudiod
echo "«Traigger» instalado. Aparecerá como micrófono y salida de audio en unos segundos."
