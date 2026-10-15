# Repaso manual antes de etiquetar

Lo que no tiene prueba automática: todo `PampaPad.App`. Se repasa con la copia instalada antes de empujar una etiqueta `v*`, y el resultado se anota con fecha en el backlog del proyecto, no acá.

`PampaPad.exe` es un programa de ventanas: desde PowerShell se lo lanza con `Start-Process`, y con `-Wait -PassThru` cuando hace falta su código de salida.

## Etapa 1: arranque, bandeja y actualización

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Correr `PampaPad.exe --smoke-test` sobre la carpeta publicada (`build\publish.ps1 -Version 999.0.0` lo hace solo, sin subir nada) | Sale con código 0 |
| 2 | Abrir el programa | Aparece el ícono: X gris con "Sin OBS: no está abierto" si OBS está cerrado, o contorno gris con "PampaPad · OBS conectado". La versión está en la línea "<título> <versión>: compuesto" del registro y, desde la etapa 6, en la ventana: Estado, tarjeta Programa |
| 3 | Abrirlo de nuevo con la primera copia corriendo | La segunda copia sale sola; sigue habiendo un solo proceso `PampaPad`; el registro dice "Otra copia pidió abrir la ventana" y, desde la etapa 6, se abre la ventana en Estado |
| 4 | Clic derecho en el ícono, con la copia instalada | El menú tiene "Abrir PampaPad", "Estado", una línea, "Recargar biblioteca", "Elegir carpeta de datos", una línea, "Buscar actualización", una línea y "Salir", en ese orden. "Reconectar con OBS" aparece debajo de "Estado" sólo con la clave rechazada o la sesión cerrada por OBS, y el ítem de la consola, debajo de "Elegir carpeta de datos", sólo con un puerto en `local.json`. Los dos primeros son de la etapa 6 |
| 5 | Clic derecho en el ícono, con la copia de desarrollo | El menú tiene "Abrir PampaPad", "Estado", una línea, "Recargar biblioteca", "Elegir carpeta de datos", una línea y "Salir", y el registro dice "Copia sin instalar: no busca actualizaciones" |
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
- **Pasos 4 y 5.** La copia de desarrollo (Debug) y la instalada conviven: tienen carpeta local y mutex distintos (`PampaPad-dev` y `PampaPad`). Los dos ítems de la biblioteca son de la etapa 3 (E3-3): con la v0.1.x el menú tiene sólo "Buscar actualización" y "Salir" en la instalada, y "Salir" en la de desarrollo. "Reconectar con OBS" aparecía arriba de todo (desde la etapa 6, debajo de "Estado") sólo con la clave rechazada o la sesión cerrada por OBS. Las filas 4 y 5 de arriba ya son las de la etapa 6.
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

## Etapa 3b: las teclas del prototipo de banco

Se repasa con la placa del prototipo enchufada por el USB-C del CH343, el firmware de banco cargado y el OBS real, sin grabar nada que importe. El puerto va a mano en `%APPDATA%\PampaPad\local.json`, en la clave `consolePort` (en esta PC, `COM20`; `pio device list` dice cuál es). Se corre el Release de la rama, después de salir de la copia instalada: usan la misma carpeta local y la misma biblioteca.

Para probar el mantenido hace falta una tecla con `onHold`. Durante el repaso se agrega a la biblioteca, en la página `grabacion`, la tecla 12 con `"action": "marca-inicio"` y `"onHold": { "action": "marca-fin" }`, y se saca al terminar (`git diff --stat -- Areas/marca-personal/pampapad/config.json`, desde `D:\Conocimiento`, no tiene que mostrar nada).

Las teclas se nombran por lo que tienen impreso. El teclado de membrana tiene tres filas en uso, y la posición es fila por cuatro más columna más uno:

| | | | | |
|---|---|---|---|---|
| Fila 1 | `1` (1) | `2` (2) | `3` (3) | `A` (4) |
| Fila 2 | `4` (5) `short` | `5` (6) `inicio` | `6` (7) `fin` | `B` (8) `sacar` |
| Fila 3 | `7` (9) `largo` | `8` (10) | `9` (11) | `C` (12) prueba |

El monitor serie se abre siempre con el puerto escrito: sin `-p`, `pio` elige el primero que encuentra, que puede ser otra placa.

```powershell
pio device monitor -p COM20 -b 115200 --rts 0 --dtr 0
```

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Abrir el programa y mirar el menú de la bandeja | `Soltar la consola (COM20)` entre "Elegir carpeta de datos" y la línea. El registro dice "La consola está conectada." |
| 2 | Grabando, apretar de a una, soltando enseguida: `4`, `5`, `6`, `B`, `7` | Cinco capítulos: `short`, `inicio`, `fin`, `sacar` y `largo`, en ese orden |
| 3 | Grabando, mantener `4` tres segundos | Un solo capítulo `short` |
| 4 | Grabando, un toque corto de `C`; después mantener `C` tres segundos | `inicio` al soltar el toque; `fin` al segundo de mantenerla, sin soltar, y nada al soltar |
| 5 | "Soltar la consola (COM20)". Abrir el monitor serie y apretar `3`. Cerrarlo con Ctrl+C, "Tomar la consola (COM20)", esperar un segundo y apretar `5` | El ítem pasa a `Tomar la consola (COM20)`. En el monitor salen líneas `#301110BAAAAAAAAAA=` y en OBS no entra nada. Después de tomarla, `inicio` |
| 6 | Con el programa teniendo el puerto, abrir el monitor serie | Falla con "Acceso denegado" (`PermissionError(13, ...)`). El registro no cambia |
| 7 | "Soltar la consola", abrir el monitor, y con el monitor abierto elegir "Tomar la consola". Mirar el menú. Cerrar el monitor, esperar tres segundos y mirarlo otra vez. Apretar `6` | Con el monitor abierto: `Soltar la consola (COM20, sin conexión)` y una sola línea "Sin consola: Access to the path 'COM20' is denied. Se reintenta cada 2 s.". Al cerrarlo vuelve solo a `Soltar la consola (COM20)`. `6` da `fin` |
| 8 | Grabando, Ctrl+Shift+Alt+F9 en el teclado de la PC | `short`: el teclado y la consola conviven |
| 9 | Apretar `C` y, antes de medio segundo, sacar el cable USB con la tecla apretada. Esperar tres segundos y mirar el menú. Volver a enchufar, esperar unos cinco segundos y mirarlo otra vez. Apretar `4` | Ninguna marca. `Soltar la consola (COM20, sin conexión)`, con "Sin consola: Access to the path 'COM20' is denied." y, dos segundos después, "Sin consola: Could not find file 'COM20'.". Al enchufar vuelve solo a `Soltar la consola (COM20)` y `4` llega |
| 10 | "Salir" | El registro termina en "Salida ordenada" y no hay un error de ".NET Runtime" en el registro de aplicaciones de Windows |

## Notas de la etapa 3b

- **Repasado el 2026-10-14 con la placa en COM20.** Las filas 1 a 8 dieron lo esperado; la toma tuvo 15 capítulos, uno por cada marca del registro, sin perdidos ni repetidos. La fila 9 encontró un defecto: al sacar el cable, cerrar el puerto tiraba `UnauthorizedAccessException`, el bucle de la consola se moría sin avisar, el menú se quedaba en "Soltar la consola (COM20)" y al salir el programa se caía. Se arregló en `SerialConsole` (cerrar el puerto ya no deja salir el error; pruebas en `SerialConsoleCloseFailureTests`) y las filas 9 y 10 se repitieron con el arreglo: dieron lo esperado.
- **Fila 4, el toque.** Un toque que dura cerca de un segundo ya cuenta como mantenido: desde 850 ms (`holdMs` menos la ventana de frescura de 150 ms) sale `fin` en lugar de `inicio`. En el repaso pasó una vez con un toque que pareció corto.
- **Fila 9, el `fin` de más.** Si `C` llega a estar apretada unos 850 ms antes de que salga el cable, entra `fin`: la tecla seguía fresca al llegar al umbral (E3b-2). No es un mantenido fantasma y se descuenta al leer los capítulos.
- **Fila 5, el arranque de la placa.** Tomar el puerto después de cerrar el monitor puede reiniciar la placa; por eso se espera un segundo antes de apretar.
- **Lo que repasa la prueba automática y no hace falta repetir a mano:** el códec de la línea, los gestos con su umbral al milisegundo, la reconexión y el cierre con un puerto que falla al cerrar. Lo que sólo se ve a mano es todo lo de arriba: la placa, el cable, el driver del CH343 y el OBS real.

## Etapa 6: la ventana de lectura y la pantalla Estado

Se repasa con la copia de desarrollo de la rama `etapa-6` (worktree `D:\Conocimiento\Sistemas\electronica\pampapad-pc-etapa-6`), armada con la página:

```powershell
pnpm.cmd -C ui build
dotnet run --project src\PampaPad.App
```

Usa `%APPDATA%\PampaPad-dev` y la biblioteca de ejemplo, sin atajos ni consola (05 §10); la instalada puede seguir abierta. Con `$env:PAMPAPAD_LIBRARY = "D:\Conocimiento\Areas\marca-personal\pampapad"` antes de `dotnet run`, lee la biblioteca real: esta etapa no escribe nada.

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Clic derecho en el ícono de la copia de desarrollo | El menú empieza con "Abrir PampaPad" y "Estado" |
| 2 | Elegir "Estado" | En unos segundos se abre "PampaPad (dev)", al frente y sin destello blanco, en la pantalla Estado. El registro dice "La página avisó que está lista (window.ready)" y "Ventana mostrada al frente" |
| 3 | Mirar la ventana | A la izquierda, Teclas, Acciones, Scripts, Estado y Ajustes; las que no existen dicen "etapa 7" u "etapa 8" y, al elegirlas, en qué etapa llegan. Arriba, OBS, la grabación y la consola. En Estado: Conexiones, Acciones en curso, Problemas de la configuración, Programa (versión 0.0.0, esquema 1), Chequeo previo ("llega en la etapa 5") y Registro |
| 4 | Con la ventana abierta, abrir el programa otra vez (otro `dotnet run`, o el `PampaPad.exe` de `bin\Debug`) | No se abre otra ventana: la que estaba vuelve al frente, y en el Registro aparece solo, sin recargar, "Otra copia pidió abrir la ventana" |
| 5 | En la página: F5, Ctrl+R, Ctrl con la rueda del mouse, Ctrl+más y clic derecho | Nada: ni recarga, ni zoom, ni menú |
| 6 | Con OBS abierto, empezar a grabar una toma de prueba; mirar la franja de arriba unos segundos; pausar; detener | "Grabando 0:00:0N", que avanza de a un segundo; "En pausa"; y vuelve a "OBS conectado" sin grabación, sin tocar la ventana |
| 7 | Cerrar OBS | En la franja y en Conexiones: "Sin OBS: no está abierto" |
| 8 | En Registro, elegir "Sólo errores" y después "Todo" | Con "Sólo errores" quedan sólo las líneas de error, o "Nada con este filtro."; con "Todo" vuelven todas |
| 9 | Cerrar la ventana con la X y mirar el Administrador de tareas | En un par de segundos no queda ningún `msedgewebview2.exe` del programa; el ícono sigue en la bandeja |
| 10 | "Abrir PampaPad" | Se abre en Estado: Teclas llega en la etapa 7 |
| 11 | Con la ventana abierta, "Salir" | La ventana se cierra, el ícono desaparece y el registro termina en "Salida ordenada" |

## Notas de la etapa 6

- **Lo que repasó Claude solo, el 2026-10-15** (filas 2 a 4 y 9, sin mirar): abrió la copia de desarrollo con `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`, abrió la ventana con una segunda copia, leyó la página con `node ui\scripts\capture-window.mjs <captura.png>`, vio llegar en vivo la línea de una tercera y una cuarta copia (la página no se recargó: un marcador puesto antes siguió ahí) y contó los `msedgewebview2.exe` antes y después de cerrar: seis y cero, con "Ventana cerrada" en el registro. Lo que dio va en el backlog, tarea E6-15.
- **Lo que queda para Emmanuel:** mirar la ventana (fila 3), el frente desde la bandeja (fila 2), las teclas apagadas (fila 5), OBS de verdad (filas 6 y 7), el filtro (fila 8), "Abrir PampaPad" (fila 10) y la salida con la ventana abierta (fila 11).
- **Fila 2, "al frente".** Show y Activate no siempre la traen al frente (E0-3: 2 de 3). El registro dice en cada apertura si quedó al frente; si no queda, se anota cuándo pasó. En el repaso de Claude, las tres veces ("Ventana mostrada, pero Windows no la dejó al frente"): la segunda copia, la tercera y la cuarta, lanzadas con `Start-Process` desde una herramienta, sin un clic en el menú de la bandeja que diera permiso de primer plano. Con el clic del menú, que es lo que supone el plan, no se midió.
- **Fila 3, el aro de foco.** En la captura de la ventana real, "Teclas" (el primer botón de la columna) se ve con un aro blanco de foco al abrir. Mirar si molesta.
- **La página sola, sin el programa:** `pnpm.cmd -C ui dev` y `http://localhost:5173/#/status` en el navegador. Contesta el puente falso con las muestras de `ui/src/bridge/contract.samples.json`.
- **La ventana abierta mientras se graba sale en el video** si está en el monitor que se captura. No hay regla todavía (05, Incierto).
- **Lo que repasa la prueba automática y no hace falta repetir a mano:** los sobres y los permisos del puente, la foto y los eventos con cadenas (`BridgeTests`, `StatusRequestsTests`), el contrato entre C# y la página (`Contract.cs` y `contract.test.ts`) y la pantalla contra el puente falso, con un evento simulado (`ui/tests/status.spec.ts`).
