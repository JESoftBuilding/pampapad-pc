# Repaso manual antes de etiquetar

Lo que no tiene prueba automática: todo `PampaPad.App`. Se repasa con la copia instalada antes de empujar una etiqueta `v*`, y el resultado se anota con fecha en el backlog del proyecto, no acá.

`PampaPad.exe` es un programa de ventanas: desde PowerShell se lo lanza con `Start-Process`, y con `-Wait -PassThru` cuando hace falta su código de salida.

## Etapa 1: arranque, bandeja y actualización

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Correr `PampaPad.exe --smoke-test` sobre la carpeta publicada (`build\publish.ps1 -Version 999.0.0` lo hace solo, sin subir nada) | Sale con código 0 |
| 2 | Abrir el programa | Aparece el ícono: X gris con "Sin OBS: no está abierto" si OBS está cerrado, o contorno gris con "PampaPad · OBS conectado". La versión está en la línea "<título> <versión>: compuesto" del registro |
| 3 | Abrirlo de nuevo con la primera copia corriendo | La segunda copia sale sola; sigue habiendo un solo proceso `PampaPad`; el registro dice "Otra copia pidió abrir la ventana" |
| 4 | Clic derecho en el ícono, con la copia instalada | El menú tiene "Recargar biblioteca", "Elegir carpeta de datos", "Buscar actualización" y "Salir", en ese orden y con una línea separadora antes de "Buscar actualización" y otra antes de "Salir" |
| 5 | Clic derecho en el ícono, con la copia de desarrollo | El menú tiene "Recargar biblioteca", "Elegir carpeta de datos" y "Salir", y el registro dice "Copia sin instalar: no busca actualizaciones" |
| 6 | Elegir "Salir" | El ícono desaparece, no queda proceso y la última línea del registro dice "Salida ordenada" |
| 7 | Con una versión más nueva publicada, abrir el programa y esperar medio minuto sin tocar nada | Con OBS abierto y sin grabar, el ícono pasa a la flecha y el texto dice "Versión X lista para instalar". Con OBS cerrado queda la X y el texto suma la versión. El menú ofrece "Instalar versión X"; si OBS está grabando, queda el círculo rojo y el ítem figura apagado, como "Instalar versión X (Se está grabando)". El programa sigue en la versión anterior: nada se bajó ni se instaló solo |
| 8 | Elegir "Instalar versión X" | El ítem dice "Bajando la versión X…", el programa se cierra y vuelve a abrirse solo en la versión X; el registro, en orden, "La versión X se instala al salir" y "PampaPad X: compuesto" |
| 9 | Desconectar la red y elegir "Buscar actualización" | No aparece ningún cartel; el registro tiene una línea de nivel `warning` con `no_response` |
| 10 | Cerrar la sesión de Windows y volver a entrar | El programa está corriendo, en la última versión instalada |
| 11 | Mirar el valor `PampaPad` de `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Apunta, entre comillas, a `%LOCALAPPDATA%\PampaPad\PampaPad.exe`, sin `current` |
| 12 | Desinstalar desde Configuración, Aplicaciones | Desaparece `%LOCALAPPDATA%\PampaPad` y el valor de `Run`; `%APPDATA%\PampaPad` queda |

El registro está en `%APPDATA%\PampaPad\log\`, un archivo por día. El de la copia de desarrollo, en `%APPDATA%\PampaPad-dev\log\`. Cada línea es un JSON con `at`, `level`, `module`, `message` y, si la falla tiene código, `code`. Para leerlo con los acentos bien, en PowerShell 5.1 hace falta `-Encoding UTF8`:

```powershell
Get-Content "$env:APPDATA\PampaPad\log\$(Get-Date -Format yyyy-MM-dd).jsonl" -Tail 5 -Encoding UTF8
```

## Notas

- **Paso 1.** Con una versión ya publicada en GitHub, `build\publish.ps1` baja la última y `vpk pack` rechaza una versión menor o igual (`There is a release in channel win which is equal or greater`). Por eso el ejemplo usa `999.0.0`, que es mayor que cualquier versión real, y es lo que usa el trabajo `package` del CI. El instalador que deja en `artifacts\releases` es de prueba: no se ejecuta, porque instalaría una versión 999.0.0 que nunca se actualizaría.
- **Versión del ejecutable.** `(Get-Item (Get-Process PampaPad).Path).VersionInfo.ProductVersion` da `0.1.0+<sha>`: el sufijo lo pone SourceLink. El menú y el registro dicen `0.1.0`. Al comparar, se mira cómo empieza.
- **Pasos 7 y 8** necesitan una versión publicada más nueva que la instalada: con la v0.1.0 recién instalada, se hacen cuando exista la v0.1.1. El programa nunca instala solo: si se actualizó sin que alguien elija "Instalar versión X", es un error.
- **Pasos 4 y 5.** La copia de desarrollo (Debug) y la instalada conviven: tienen carpeta local y mutex distintos (`PampaPad-dev` y `PampaPad`). Los dos ítems de la biblioteca son de la etapa 3 (E3-3): con la v0.1.x el menú tiene sólo "Buscar actualización" y "Salir" en la instalada, y "Salir" en la de desarrollo. "Reconectar con OBS" aparece arriba de todo sólo con la clave rechazada o la sesión cerrada por OBS.
- **Paso 9.** Es una búsqueda que cae, no una falla del programa: no tiene que aparecer ningún cartel ni ícono de error. La siguiente búsqueda periódica (a las 6 horas) reintenta sola; con "Buscar actualización" se reintenta en el momento.
- **Paso 12.** La desinstalación sin ventana, para repetirla sin un clic de más, es `& "$env:LOCALAPPDATA\PampaPad\Update.exe" uninstall --silent`; sin `--silent` queda una ventana esperando un clic.
