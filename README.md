# PampaPad, programa de la PC

Programa de Windows que acompaña al PampaPad, una botonera propia de 12 teclas, perilla y pantalla para grabar con OBS. Vive en la bandeja, habla con OBS por obs-websocket y se actualiza solo desde las versiones publicadas en este repo.

## Desarrollar

Hace falta el SDK de .NET que fija `global.json`. Desde la raíz del repo:

    dotnet build PampaPad.slnx
    dotnet test PampaPad.slnx

El código se parte en un núcleo sin ventanas (`src/PampaPad.Core`), la cáscara de Windows que sale como `PampaPad.exe` (`src/PampaPad.App`) y las pruebas del núcleo (`tests/PampaPad.Tests`). La copia de desarrollo guarda sus datos en `%APPDATA%\PampaPad-dev`, aparte de la instalada.
