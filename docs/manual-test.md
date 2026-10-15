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

## Etapa 2: OBS y estado

Se repasa con la copia de desarrollo y el OBS real, sin grabar nada que importe:

```powershell
dotnet run --project src/PampaPad.App
```

El texto de ayuda se lee dejando el mouse quieto sobre el ícono de la bandeja. Cada grabación de prueba se
mueve de `D:\Canal\grabaciones` a `D:\Canal\pruebas` al terminar, para que `marcas.cmd` no la tome como la
más nueva.

| N | Qué se hace | Qué se tiene que ver en la bandeja |
|---|---|---|
| 1 | Con OBS cerrado, arrancar el programa | X gris. "Sin OBS: no está abierto" |
| 2 | Abrir OBS y esperar | En menos de 10 s, contorno de círculo gris y "PampaPad · OBS conectado". Mientras OBS arranca puede pasar por "Sin OBS: está abierto, sin WebSocket" y "Sin OBS: conectando" |
| 3 | En OBS, "Iniciar grabación" | Círculo rojo lleno. "Grabando 0:00:0N", y el tiempo avanza de a un segundo |
| 4 | Ctrl+Shift+Alt+F10, el atajo de OBS para pausar | Dos barras ámbar. "En pausa: las marcas no entran" |
| 5 | Ctrl+Shift+Alt+F11, el atajo de OBS para reanudar | Círculo rojo otra vez. El tiempo sigue desde donde estaba: es tiempo grabado, no de reloj |
| 6 | En el mezclador de OBS, silenciar "Mic/Aux" y volver a activarlo | Al silenciar, el texto suma " · micrófono silenciado". Al activarlo, se va |
| 7 | En OBS, "Detener grabación" | Contorno gris. "PampaPad · OBS conectado" |
| 8 | "Salir" en el menú de PampaPad. Iniciar una grabación en OBS. Arrancar el programa | En menos de 5 s, círculo rojo con el tiempo que OBS lleva grabado, no desde cero. Detener la grabación |
| 9 | Cerrar OBS con el programa andando | "Sin OBS: se está cerrando" mientras OBS termina, y después X gris con "Sin OBS: no está abierto" |
| 10 | Volver a abrir OBS, sin tocar el programa. Iniciar y detener una grabación | Vuelve solo a "PampaPad · OBS conectado" y sigue la grabación como en las filas 3 y 7 |
| 11 | En OBS, Herramientas, ajustes del servidor WebSocket: en la lista de sesiones conectadas, expulsar la de PampaPad. Si hay otra sesión abierta (el MCP de OBS de Claude), cerrarla antes para no confundirlas | X gris. "Sin OBS: OBS cerró la sesión". No vuelve sola aunque se espere 10 s. En el menú aparece "Reconectar con OBS": al elegirlo vuelve a "PampaPad · OBS conectado" y el ítem desaparece |
| 12 | Suspender la PC desde el menú Inicio, esperar un minuto y despertarla | En menos de 15 s el ícono vuelve a "PampaPad · OBS conectado". Iniciar y detener una grabación: el ícono la sigue |
| 13 | Correr la búsqueda de la clave en el registro, que está abajo | El número que imprime es 0 |

Búsqueda de la fila 13. Lee la clave del archivo de OBS a una variable y cuenta en cuántas líneas del
registro aparece, sin mostrarla:

```powershell
$clave = (Get-Content "$env:APPDATA\obs-studio\plugin_config\obs-websocket\config.json" -Raw | ConvertFrom-Json).server_password
(Select-String -Path "$env:APPDATA\PampaPad-dev\log\*" -SimpleMatch $clave | Measure-Object).Count
```

En la fila 12, el registro del día tiene que tener además la línea "la PC volvió de la suspensión: se
rearma la conexión con OBS".

No se repasa a mano la clave rechazada: el programa lee la clave del archivo de OBS en cada intento, así
que para provocarla habría que tocar la configuración de OBS. La cubre la prueba automática
`A_rejected_password_does_not_retry_until_the_obs_file_changes`.

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

## Etapa 4: escena, grabar, pausa y micrófono

Se repasa con la copia de desarrollo de la rama `etapa-4`, que lee la biblioteca de ejemplo: ya tiene grabar, pausa, micrófono y la página de escenas. La biblioteca real no se toca hasta instalar la v0.3.0. Hace falta OBS abierto, sin grabar nada que importe, y para las filas marcadas `(placa)` el prototipo con el firmware `banco`.

**Estado al 2026-10-15.** Emmanuel no usa el prototipo hasta que la consola esté terminada, así que las filas `(placa)` (2 a 8, 11 y 12) quedan **pendientes de cuando lo use**: están escritas y no se repasaron. Las que se hacen con el teclado de la PC y la bandeja (1, 9, 10, 13 y 14) se pueden repasar ya; sin la placa, el `local.json` lleva sólo `"hotkeysEnabled": true`, sin `consolePort`.

Antes: si OBS quedó con la grabación colgada de la corrida de Claude del 2026-10-15 (marca "Grabando 0:00:00" y un `D:\Canal\grabaciones\2026-10-15 13-27-08.mp4` de 0 bytes: ver las notas de abajo), detenerla desde OBS o cerrar OBS y borrar ese archivo. Después, "Salir" en la copia instalada, que tiene los atajos y el puerto. Y en `%APPDATA%\PampaPad-dev\local.json`, que la copia de desarrollo tome los dos (05 §10):

```json
{
  "version": 1,
  "hotkeysEnabled": true,
  "consolePort": "COM20"
}
```

Para la fila 11 hace falta una copia de la biblioteca a la que se le pueda cambiar la página principal. Desde la raíz del worktree:

```powershell
New-Item -ItemType Directory -Force D:\repos\pampapad-construccion\E4-11-biblioteca | Out-Null
Copy-Item examples\library\config.json D:\repos\pampapad-construccion\E4-11-biblioteca\config.json
$env:PAMPAPAD_LIBRARY = "D:\repos\pampapad-construccion\E4-11-biblioteca"
dotnet run --project src\PampaPad.App
```

Las teclas se nombran por lo que tienen impreso, como en la etapa 3b: `1 2 3 A` son las posiciones 1 a 4, `4 5 6 B` las 5 a 8 y `7 8 9 C` las 9 a 12. En `grabacion`: `8` micrófono, `9` pausa, `C` grabar. Cada toma de prueba se mueve de `D:\Canal\grabaciones` a `D:\Canal\pruebas` al terminar.

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Mirar el texto de ayuda de la bandeja | "PampaPad · OBS conectado", sin problema de configuración. El registro dice "Atajos tomados: 8; rechazados por Windows: 0." |
| 2 (placa) | Un toque en `C` | OBS empieza a grabar. El ícono pasa a círculo rojo, "Grabando 0:00:0N", cuando OBS avisa |
| 3 (placa) | Otro toque en `C` | Nada: sigue grabando. El registro dice "grabar: OBS ya está grabando, no se manda nada." |
| 4 (placa) | Mantener `9` un segundo, sin soltar | Al segundo OBS pausa: dos barras ámbar, "En pausa: las marcas no entran". Soltar no hace nada más |
| 5 (placa) | Un toque en `9` | OBS sigue: círculo rojo; el tiempo sigue desde donde estaba |
| 6 (placa) | Un toque en `8`, mirar el texto de ayuda y el mezclador de OBS; otro toque | El texto suma " · micrófono silenciado" y "Mic/Aux" queda silenciado en OBS; el segundo toque lo abre |
| 7 (placa) | Mantener `C` un segundo | Al segundo OBS detiene: contorno gris |
| 8 (placa) | Con la grabación detenida, mantener `9` un segundo | Triángulo rojo, con un texto de ayuda que empieza con "Falló Pausar la grabación: paso 1 (obs.record)" (la bandeja lo recorta: `Tray.cs:89`), y el sonido y el cartel, porque no hay toma en curso. El cartel dice "No estás grabando: no hay grabación para pausar." |
| 9 | Ctrl+Shift+Alt+G; Ctrl+Shift+Alt+M dos veces; Ctrl+Shift+Alt+D | Graba; silencia y abre el micrófono; para. El atajo de parar no pide mantener: por teclado no existe (02 §6, pregunta 39) |
| 10 | Grabando, Ctrl+Shift+Alt+F10 y después F11, los atajos de OBS | OBS pausa y sigue, y la bandeja lo muestra: pausar y seguir no tienen atajo propio (decisión 19). Detener la toma |
| 11 (placa) | En `D:\repos\pampapad-construccion\E4-11-biblioteca\config.json`, cambiar `"mainPage": "grabacion"` por `"mainPage": "escenas"` y elegir "Recargar biblioteca". Apretar de a una, mirando OBS, `1 2 3 A`, `4 5`, `B`, `7 8 9 C`; después `6` y esperar 15 s | Cada tecla pone al aire su escena de la grilla: Cámara, Pantalla, Banco, Ya arrancamos, Cámara (zoom), Pantalla + Cámara, Ya vuelvo, Vertical Cámara, Vertical SolidWorks, Vertical Pantalla + Banco, Fin. Las verticales se ven reescaladas en el perfil horizontal, y es lo esperado. `6` es Intro: suena y a los 12 s salta sola a Cámara |
| 12 (placa) | Volver a `"mainPage": "grabacion"` y "Recargar biblioteca" | Las teclas vuelven a ser las de grabación: `4` marca `short` si se graba |
| 13 | "Salir" | El registro termina en "Salida ordenada" |
| 14 | Con el teclado: Ctrl+Shift+Alt+G; esperar 3 s; Ctrl+Shift+Alt+F10 (pausa de OBS); esperar 3 s; Ctrl+Shift+Alt+D | OBS detiene la toma en pausa, la bandeja pasa a detenida en menos de un segundo y el archivo queda legible. Es lo que la corrida de Claude no pudo medir (ver las notas): si la bandeja se queda en "Deteniendo" más de 2 s, anotar la hora y no insistir con la tecla |

Al terminar: en `%APPDATA%\PampaPad-dev\local.json`, `hotkeysEnabled` en `false` y sin `consolePort`, o borrar el archivo; volver a abrir la copia instalada.

## Notas de la etapa 4

- **Lo que repasó Claude solo, el 2026-10-15**, con `D:\repos\pampapad-construccion\E4-11-repaso` (plan de la etapa 4, E4-11), una sola corrida contra el OBS real 32.2.2 (13:26 a 13:27). **Valió:** las doce escenas por sus acciones (304 a 351 ms hasta `CurrentProgramSceneChanged`), el salto solo de la intro a Cámara (12,9 s), la escena que ya está al aire con "Desvanecimiento" (el paso termina bien y no llega evento), una toma completa con pausa (arrancar 115 ms, parar 302 ms), los códigos de carrera 500, 502 y 503, el micrófono, `SetInputMute` al estado en que ya está (OBS manda el evento igual), el 604 de una entrada sin audio y que la clave de transmisión no sale del paso. El informe está en `D:\repos\pampapad-construccion\E4-11-repaso.txt`, con la lectura de cada línea, y lo medido está en 03 §3, §5 e Incierto y en 02, Incierto.
- **Lo que no valió, y OBS quedó colgado.** Las tomas 2 y 3 salieron mal. Un `StartRecord` mandado en los ~30 ms siguientes al `STOPPED` de la toma anterior contestó 100 y OBS no arrancó. Después el script pidió pausa y parada a los ~30 ms de que OBS avisó que grababa: OBS mandó `STOPPING` y nunca `STOPPED`; la salida de grabación quedó activa y con el archivo en 0 bytes (`D:\Canal\grabaciones\2026-10-15 13-27-08.mp4`). El programa se comportó como está diseñado (`obs_timeout` a los 2 s sin mandar). No se pudo separar si lo colgó parar desde la pausa o parar casi al arrancar; la etapa 0 detuvo en pausa una toma de 3 s sin problema. Quedaron sin medir el 501 y `PauseRecord`/`ResumeRecord` con la grabación detenida. La fila 14 es la prueba con tiempos humanos.
- **Lo que repasó Emmanuel con el teclado, el 2026-10-15** (14:42 a 14:58), con la copia de desarrollo de la rama y la biblioteca de ejemplo, con la copia instalada cerrada: fila 1, "Atajos tomados: 8; rechazados por Windows: 0." y la bandeja conectada; fila 9, grabar, micrófono dos veces y parar por sus atajos (parar detuvo en 0,3 s); filas 10 y 14, pausar y seguir con los atajos de OBS y parar en pausa con Ctrl+Shift+Alt+D, que detuvo en 0,3 s sin colgarse, con la toma legible (12,1 s grabados); fila 13, "Salida ordenada". Las filas `(placa)` (2 a 8, 11 y 12) esperan a que use el prototipo.
- **Lo que no se repasa a mano:** renombrar, borrar o crear una escena en OBS. Cambia OBS, y con el tope de 12 escenas es una decisión de Emmanuel, no una prueba. Lo cubren las pruebas contra el OBS falso (`ObsSceneTests`, `ObsMirrorListsTests`).
- **Lo que repasa la prueba automática y no hace falta repetir a mano:** la tabla de `obs.record` fila por fila (`RecordRulesTests`), la espera mientras OBS arranca o detiene (`ObsRecordTests`), la tecla que para o pausa con un toque (`DispatcherHoldRuleTests`), y de la tecla y el atajo al estado del OBS falso (`ObsKeysEndToEndTests`).
