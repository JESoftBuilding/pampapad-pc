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

## Notas de la etapa 1

- **Paso 1.** Con una versión ya publicada en GitHub, `build\publish.ps1` baja la última y `vpk pack` rechaza una versión menor o igual (`There is a release in channel win which is equal or greater`). Por eso el ejemplo usa `999.0.0`, que es mayor que cualquier versión real, y es lo que usa el trabajo `package` del CI. El instalador que deja en `artifacts\releases` es de prueba: no se ejecuta, porque instalaría una versión 999.0.0 que nunca se actualizaría.
- **Versión del ejecutable.** `(Get-Item (Get-Process PampaPad).Path).VersionInfo.ProductVersion` da `0.1.0+<sha>`: el sufijo lo pone SourceLink. El menú y el registro dicen `0.1.0`. Al comparar, se mira cómo empieza.
- **Pasos 7 y 8** necesitan una versión publicada más nueva que la instalada: con la v0.1.0 recién instalada, se hacen cuando exista la v0.1.1. El programa nunca instala solo: si se actualizó sin que alguien elija "Instalar versión X", es un error.
- **Pasos 4 y 5.** La copia de desarrollo (Debug) y la instalada conviven: tienen carpeta local y mutex distintos (`PampaPad-dev` y `PampaPad`). Los dos ítems de la biblioteca son de la etapa 3 (E3-3): con la v0.1.x el menú tiene sólo "Buscar actualización" y "Salir" en la instalada, y "Salir" en la de desarrollo. "Reconectar con OBS" aparece arriba de todo sólo con la clave rechazada o la sesión cerrada por OBS.
- **Paso 9.** Es una búsqueda que cae, no una falla del programa: no tiene que aparecer ningún cartel ni ícono de error. La siguiente búsqueda periódica (a las 6 horas) reintenta sola; con "Buscar actualización" se reintenta en el momento.
- **Paso 12.** La desinstalación sin ventana, para repetirla sin un clic de más, es `& "$env:LOCALAPPDATA\PampaPad\Update.exe" uninstall --silent`; sin `--silent` queda una ventana esperando un clic.

## Etapa 3: marcas con tipo desde el teclado

Se repasa con el OBS real, sin grabar nada que importe. Las marcas se leen del archivo con:

```powershell
ffprobe -v error -show_chapters -of json "D:\Canal\grabaciones\<archivo>.mp4"
```

Cada marca es un capítulo, y su tipo es `tags.title`. El capítulo "Iniciar", en 0, lo pone OBS. Cada grabación de prueba se mueve a `D:\Canal\pruebas` al terminar.

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Con la variable de usuario `CONOCIMIENTO` sin definir, abrir el programa y cancelar la pregunta | Pregunta "¿Dónde están tus datos?". Al cancelar no sale: queda en la bandeja con el signo de admiración ámbar y "Sin biblioteca: elegí la carpeta de datos" |
| 2 | "Elegir carpeta de datos" en el menú, elegir `D:\Conocimiento`, aceptar el cartel, "Salir" y volver a abrir | No vuelve a preguntar y queda sin signo de admiración; `[Environment]::GetEnvironmentVariable('CONOCIMIENTO', 'User')` da esa carpeta |
| 3 | En OBS, iniciar una grabación. Apretar Ctrl+Shift+Alt con F5, F6, F7, F8 y F9, con unos segundos entre una y otra. Detener | El ícono no cambia de "Grabando". El archivo tiene cinco capítulos: `largo`, `inicio`, `fin`, `sacar` y `short`, en ese orden |
| 4 | Grabando, mantener Ctrl+Shift+Alt+F6 apretada tres segundos | Un solo capítulo `inicio` |
| 5 | Grabando, apretar Ctrl+Shift+Alt+F9 dos veces seguidas | Dos capítulos `short` |
| 6 | Grabando, con SolidWorks al frente, apretar Ctrl+Shift+Alt con F5, F6, F7, F8 y F9, con unos segundos entre una y otra. Después, lo mismo con OBS al frente | Cada vez, cinco capítulos: `largo`, `inicio`, `fin`, `sacar` y `short`, en ese orden. Ninguno de los dos programas reacciona a las combinaciones (salvo el "Sin nombre N" que OBS suma con F9 mientras la tenga: ver Notas) |
| 7 | Grabando, pausar con Ctrl+Shift+Alt+F10 (el atajo de OBS) y apretar Ctrl+Shift+Alt+F7 | Triángulo rojo. El texto de ayuda empieza con "Falló Marca: fin: paso 1 (obs.marker). Estás en pausa: la marca no entró.". No hay cartel ni sonido. Al abrir el menú vuelven las dos barras ámbar. El archivo no tiene ese capítulo |
| 8 | Con OBS abierto y sin grabar, apretar Ctrl+Shift+Alt+F9 | Suena, sale el cartel "No estás grabando: la marca no entró." y queda el triángulo rojo |
| 9 | Con OBS cerrado, apretar Ctrl+Shift+Alt+F9 | El cartel dice "Sin OBS: la marca no entró." |
| 10 | En `config.json`, cambiar `"action": "marca-inicio"` de la tecla 6 por `"marca-inicoi"`. "Recargar biblioteca" | Signo de admiración ámbar y "1 tecla con problema". F5, F7, F8 y F9 siguen marcando; F6 da el aviso "La acción "marca-inicoi" no existe.". Deshacer el cambio y recargar: el signo se va |
| 11 | En `config.json`, agregar una coma después del último `]`. "Recargar biblioteca" | "config.json no se puede leer: error de sintaxis en la línea N". Ninguna combinación responde. El archivo quedó como se lo dejó. Sacar la coma y recargar: todo vuelve |
| 12 | Con la copia instalada corriendo, abrir la de desarrollo con `"hotkeysEnabled": true` en su `local.json` | La de desarrollo muestra "5 teclas con problema": Windows no le dio las combinaciones. La instalada sigue marcando |
| 13 | Clic derecho en el ícono | El menú tiene "Recargar biblioteca" y "Elegir carpeta de datos", arriba de los de actualización |

## Notas de la etapa 3

- **Filas 3, 5 y 6, antes de quitar F9 de OBS.** OBS tiene todavía la combinación Ctrl+Shift+Alt+F9 en "Añadir marcador de capítulo": cada F9 deja dos capítulos en el mismo punto, `short` y "Sin nombre N" (el segundo lo pone OBS). Se corrige sacando ese atajo de OBS (paso 8 de E3-9), y después de eso queda un solo `short`.
- **Fila 6.** Es la que cubre lo que la etapa 0 dejó sin medir (E0-2): con el teclado real llegaron F5 a F8 con SolidWorks al frente y sólo F6 con OBS, y la prueba con teclas inyectadas, que cubría el resto, no se corrió. F9, la de `short` y la del botón del mouse, no la vio llegar nadie con ninguno de los dos al frente. Si alguna no llega, o SolidWorks u OBS hacen algo con ella, no se sigue con el paso 8 de E3-9: cambian los atajos de la biblioteca y lo que se le quita a OBS.
- **Fila 8, el sonido.** Suena `failed.wav` (220 ms, dos tonos que bajan). Apretar F9 dos veces: tiene que sonar las dos. Si el cartel no aparece pero el sonido y el ícono sí, mirar el modo "No molestar" de Windows 11.
- **Fila 12.** La copia de desarrollo no toma los atajos por defecto (en Debug, `hotkeysEnabled` vale falso). Se enciende en `%APPDATA%\PampaPad-dev\local.json`; si ya tiene otras claves, se agrega `hotkeysEnabled` a mano y no se pisa el archivo. Al terminar, se vuelve a apagar:

  ```powershell
  Set-Content -Path "$env:APPDATA\PampaPad-dev\local.json" -Value '{ "version": 1, "hotkeysEnabled": true }' -Encoding ASCII
  # ... la prueba ...
  Set-Content -Path "$env:APPDATA\PampaPad-dev\local.json" -Value '{ "version": 1 }' -Encoding ASCII
  ```
- **Lo que repasa la prueba automática y no hace falta repetir a mano:** del atajo simulado al capítulo con su título en un OBS falso (`MarkersEndToEndTests`). Lo que sólo se ve a mano es todo lo de arriba: el teclado y las ventanas de verdad, el sonido, el cartel y el OBS real.
