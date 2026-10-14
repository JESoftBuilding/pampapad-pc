# PampaPad, programa de la PC

Programa de Windows que acompaña al PampaPad, una botonera propia de 12 teclas, perilla y pantalla para grabar con OBS. Vive en la bandeja, habla con OBS por obs-websocket y se actualiza solo desde las versiones publicadas en este repo.

## Instalar

Bajar `PampaPad-win-Setup.exe` de la [última versión](https://github.com/JESoftBuilding/pampapad-pc/releases/latest) y ejecutarlo. El instalador no está firmado: Windows avisa, y hay que elegir "Más información" y "Ejecutar de todas formas". Instala para el usuario, sin permisos de administrador, y si faltan instala .NET 10 de escritorio y WebView2.

El programa queda en la bandeja, arranca con la sesión y avisa en su menú cuando hay una versión nueva. Nunca la instala solo.

## Desarrollar

Hace falta el SDK de .NET que fija `global.json`. Desde la raíz del repo:

    dotnet build PampaPad.slnx
    dotnet test PampaPad.slnx

El código se parte en un núcleo sin ventanas (`src/PampaPad.Core`), la cáscara de Windows que sale como `PampaPad.exe` (`src/PampaPad.App`) y las pruebas del núcleo (`tests/PampaPad.Tests`). La copia de desarrollo guarda sus datos en `%APPDATA%\PampaPad-dev`, aparte de la instalada.
