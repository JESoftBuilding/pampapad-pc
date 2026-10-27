# Repaso manual antes de etiquetar

Lo que no tiene prueba automática: todo `PampaPad.App`. Se repasa con la copia instalada antes de empujar una etiqueta `v*`, y el resultado se anota con fecha en el backlog del proyecto, no acá.

`PampaPad.exe` es un programa de ventanas: desde PowerShell se lo lanza con `Start-Process`, y con `-Wait -PassThru` cuando hace falta su código de salida.

## Etapa 1: arranque, bandeja y actualización

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Correr `PampaPad.exe --smoke-test` sobre la carpeta publicada (`build\publish.ps1 -Version 999.0.0` lo hace solo, sin subir nada) | Sale con código 0 |
| 2 | Abrir el programa | Aparece el ícono: X gris con "Sin OBS: no está abierto" si OBS está cerrado, o contorno gris con "PampaPad · OBS conectado". La versión está en la línea "<título> <versión>: compuesto" del registro y, desde la etapa 6, en la ventana: Estado, tarjeta Programa |
| 3 | Abrirlo de nuevo con la primera copia corriendo | La segunda copia sale sola; sigue habiendo un solo proceso `PampaPad`; el registro dice "Otra copia pidió abrir la ventana" y, desde la etapa 6, se abre la ventana en Estado |
| 4 | Clic derecho en el ícono, con la copia instalada | El menú tiene "Abrir PampaPad", "Estado", una línea, "Recargar biblioteca", "Elegir carpeta de datos", una línea, "Buscar actualización", una línea y "Salir", en ese orden. Debajo de "Estado", y en este orden, "Chequeo previo" aparece sólo con OBS conectado, "Reconectar con OBS" sólo con la clave rechazada o la sesión cerrada por OBS y "Cancelar todo" sólo con una acción en curso; el ítem de la consola, debajo de "Elegir carpeta de datos", sólo con un puerto en `local.json`. Los dos primeros son de la etapa 6; "Chequeo previo" y "Cancelar todo", de la etapa 5 |
| 5 | Clic derecho en el ícono, con la copia de desarrollo | El menú tiene "Abrir PampaPad", "Estado", una línea, "Recargar biblioteca", "Elegir carpeta de datos", una línea y "Salir", con los ítems que dependen del estado como en la fila 4, y el registro dice "Copia sin instalar: no busca actualizaciones" |
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

## Etapa 5: ir al banco y el chequeo previo

Se repasa con la copia de desarrollo de la rama `etapa-5`, que lee la biblioteca de ejemplo: ya tiene "ir al banco" en la tecla 3 (`Ctrl+Shift+Alt+B`), "Escenas" en la 4 y `scripts/bancocam`, el de `D:\Canal` con la consola de scrcpy oculta (decisión 34, `8ee76db`). La biblioteca real no se toca hasta instalar la v0.4.0. Hace falta OBS abierto, sin grabar nada que importe; el celular, para las filas 2 a 5. Es con el teclado de la PC: las filas de la placa están al final, para cuando se use el prototipo.

Antes: "Salir" en la copia instalada, que tiene los atajos. Y en `%APPDATA%\PampaPad-dev\local.json`, que la copia de desarrollo los tome (05 §10):

```json
{
  "version": 1,
  "hotkeysEnabled": true
}
```

Desde la raíz del worktree:

```powershell
dotnet run --project src\PampaPad.App
```

Cada toma de prueba se mueve de `D:\Canal\grabaciones` a `D:\Canal\pruebas` al terminar.

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | Mirar el texto de ayuda de la bandeja y el menú | "PampaPad · OBS conectado", sin problema de configuración; el registro dice "Atajos tomados: 9; rechazados por Windows: 0." (con `grabacion` de base: `B` y `M` de sus teclas y los siete de la lista general, `D`, `G` y `F5` a `F9`). El menú tiene "Chequeo previo" y no tiene "Cancelar todo" |
| 2 | Celular enchufado y desbloqueado, sin el espejo abierto y con la escena Pantalla al aire, sin grabar: Ctrl+Shift+Alt+B | Arranca el espejo y, con la ventana `bancocam` en 1823 x 850, OBS pone Banco. El registro dice "bancocam: arrancó (bancocam.cmd, tope 30 s).", "bancocam terminó con 0 en … s: bancocam corriendo fuera de pantalla en x=…" e "ir-al-banco terminó bien en … ms." (unos 5,5 s: medido el 2026-10-19). No aparece ninguna consola de scrcpy (desde `8ee76db`); mirar la pantalla principal mientras tanto y, si aparece una consola o el espejo antes de irse de la pantalla, anotar cuánto dura: con Pantalla al aire grabando, saldría en el video ("Lo que puede morder", última fila) |
| 3 | Con el espejo abierto, Ctrl+Shift+Alt+B otra vez | Banco enseguida, sin relanzar `bancocam`: el registro no tiene un "bancocam: arrancó" nuevo, sólo "ir-al-banco terminó bien en … ms.". Ninguna consola de scrcpy en la barra de tareas |
| 4 | Cerrar scrcpy, desenchufar el celular y Ctrl+Shift+Alt+B | Unos 22 s después (los 20 s que espera `scripts/bancocam/bancocam.ps1:19`, más el arranque): triángulo rojo, "Falló Ir al banco: paso 1 (script.run)", el sonido y el cartel con "scrcpy no abrio la ventana (celu conectado? depuracion USB? celu desbloqueado?)". La escena no cambia, y no queda ninguna consola ni ningún scrcpy vivo |
| 5 | Ctrl+Shift+Alt+B como en la fila 4 y, mientras espera, abrir el menú | Figura "Cancelar todo"; elegirlo corta la cadena: el registro dice "Cancelar todo, desde la bandeja." y "Se canceló el script "bancocam": se cortó, con lo que había lanzado.", y la bandeja lo muestra como una falla del paso 1, con su sonido y su cartel |
| 6 | Con OBS libre y la escena Cámara al aire, "Chequeo previo" | Un globo: "Chequeo previo: todo bien" o la primera falla con cuántas más hay. El registro tiene siete líneas `obs.…: bien.`, `falla.` o `sin comprobar.` y una de resumen |
| 7 | Tapar la cámara y "Chequeo previo" | El globo nombra "La cámara está tapada o en negro" con el desvío que midió. Destapar |
| 8 | Poner "Vertical SolidWorks" al aire desde OBS y "Chequeo previo" | "La escena no llena el lienzo. "Vertical SolidWorks" llena el 31,8 % del lienzo de 1920 x 1080; se pide 90 %." Ninguna falla "Una fuente de la escena no tiene imagen" (la "Captura de pantalla" en 0 x 0 era la falla falsa del 2026-10-19, ver las notas). Dejar "Vertical SolidWorks" al aire para la fila 9 |
| 9 | Con "Vertical SolidWorks" todavía al aire (o Pantalla, que también tiene la "Captura de pantalla"; con Cámara no hay captura, el código de antes tampoco preguntaba por el monitor y la fila no prueba el arreglo): Ctrl+Shift+Alt+G (graba); con la toma en curso, "Chequeo previo"; después, Ctrl+Shift+Alt+D | Ningún globo (hay toma en curso); el registro tiene las siete líneas: la de la cámara dice "No aplica: con una toma en curso no se pide una captura de la cámara." y la del monitor, "No aplica: con una toma en curso no se pregunta por el monitor, que haría parpadear la captura." La toma no tiene ningún cuadro negro de la captura. Ctrl+Shift+Alt+D para enseguida: pasó más de un segundo desde el arranque y el registro no tiene una línea "(E4-11)". Volver a la escena de antes |
| 10 | Con OBS detenido desde hace más de un segundo: Ctrl+Shift+Alt+G y enseguida, antes de que pase un segundo, Ctrl+Shift+Alt+D | Graba y para, un poco después: el registro dice "parar: OBS avisó un cambio de la grabación hace menos de 1 s; se espera … ms antes de mandar (E4-11)." OBS termina en STOPPED, sin quedar en STOPPING |
| 11 | Enseguida del STOPPED de la fila 10, antes de que pase un segundo, Ctrl+Shift+Alt+G | Graba después de esperar lo que falte del segundo: el registro dice "grabar: OBS avisó un cambio de la grabación hace menos de 1 s; se espera … ms antes de mandar (E4-11)." Si OBS contestara sin arrancar, sale "OBS aceptó el pedido de grabar pero no avisó que arrancaba en 2 s: puede que no esté grabando." Detener con Ctrl+Shift+Alt+D |
| 12 | "Salir" | El registro termina en "Salida ordenada" |

Filas de la placa, para cuando se use el prototipo (las teclas por lo que tienen impreso: `1 2 3 A` son las posiciones 1 a 4): `3` hace lo de la fila 2; `A` pasa a la página `escenas`, el texto de ayuda suma "página escenas" y el registro dice "Atajos tomados: 7; rechazados por Windows: 0." (sólo los de la lista general): con `escenas` de base, Ctrl+Shift+Alt+G graba y F5 a F9 marcan igual (pregunta 7), y `M` y `B` no andan por teclado hasta elegir una escena; una escena, con la grabación detenida, vuelve a `grabacion` y la pone al aire, y el registro vuelve a "Atajos tomados: 9"; grabando, `A` falla con `navigation_blocked` y la base sigue en `grabacion`.

Al terminar: en `%APPDATA%\PampaPad-dev\local.json`, `hotkeysEnabled` en `false`, o borrar el archivo; volver a abrir la copia instalada.

## Notas de la etapa 5

- **Lo que repasó Claude solo, el 2026-10-19**, con `D:\repos\pampapad-construccion\E5-22-repaso` (plan de la etapa 5, E5-22): "ir al banco" con una ventana de prueba y sin OBS, `Win32Windows` contra esa ventana (también minimizada), un `.py` con acentos y `page.go`. El informe está en `D:\repos\pampapad-construccion\E5-22-repaso.txt`.
- **Lo que repasó Emmanuel con el teclado, el celular y OBS, el 2026-10-19** (20:08 a 21:27), con la copia de desarrollo de la rama y la biblioteca de ejemplo; el registro es `%APPDATA%\PampaPad-dev\log\2026-10-19.jsonl`. Dieron como dice la tabla las filas 1 ("Atajos tomados: 9; rechazados por Windows: 0." y OBS en Ready), 3 (Banco en 41 ms, sin relanzar `bancocam`), 5 ("Cancelar todo" en el menú; a las 20:20:56, "Cancelar todo, desde la bandeja." y "Se canceló el script "bancocam": se cortó, con lo que había lanzado."), 7 (tapada: desvío 1,3, y otra vez 0,4; se pide 5,0), 10 y 12 ("Salida ordenada" a las 20:46:24).
- **Lo medido.** Ir al banco con el celular tarda unos 5,5 s de punta a punta: 5473 ms (20:12:34), 5594 ms (20:49:29) y 5394 ms (21:18:16, ya con `8ee76db`), con la ventana en x=3840 y en 1823 x 850. Sin el celular falla a los 21 a 22 s (21,9 s a las 20:10:50, 21,3 s a las 20:11:46 y a las 20:52:51, 21,8 s a las 21:16:10 con `8ee76db`): los 20 s que espera el script más su arranque. La guarda de E4-11, fila 10: grabar a las 20:37:54.871 y parar unos 50 ms después; "parar: … se espera 1000 ms antes de mandar (E4-11)." y STOPPED a las 20:37:56.273, sin quedar en STOPPING. Fila 11, tercer intento: STOPPED a las 20:45:30.336, grabar a las 20:45:30.419, "se espera 916 ms" y OBS grabando a las 20:45:31.456.
- **Filas 2 y 4, dieron distinto: la consola de scrcpy.** Con el celular se vio un rato una consola de scrcpy en la barra de tareas; sin el celular quedaba una consola esperando Enter después de la falla. Arreglado en `8ee76db`, como eligió Emmanuel (decisión 34): `scripts/bancocam/bancocam.ps1` lanza scrcpy por un `cmd` oculto, con `--pause-on-exit=false`, y si no abre la ventana en 20 s cierra el `cmd` y el scrcpy que lanzó. Repetidas con ese commit: la fila 2 a las 21:18 (5,4 s, sin consola) y la 4 a las 21:16 (falla a los 22 s y ningún scrcpy vivo después). Desde ese commit, el `bancocam.ps1` de la biblioteca difiere a propósito del de `D:\Canal`.
- **Fila 6, una falla real de OBS.** El primer chequeo con Cámara (20:22:49) dio "La cámara manda su propio audio": "Dispositivo de captura de video" no estaba silenciado. Emmanuel lo silenció y a las 20:26:22 dio todo bien.
- **Fila 8, dio distinto: una falla falsa.** "Vertical SolidWorks" dio el 31,8 % del lienzo, como se esperaba, pero los cinco chequeos (20:27:54 a 20:31:38) dieron también "Una fuente de la escena no tiene imagen: "Captura de pantalla" mide 0 x 0", y uno dio el lienzo en 31,6 %. Una sonda de sólo lectura midió la causa: después de `GetInputPropertiesListPropertyItems` de `monitor_id` sobre la captura, el `GetSceneItemList` siguiente la da en 0 x 0 (a los 3 a 6 ms) y vuelve a 1920 x 1080 antes de 150 ms, tres de tres; sin ese pedido, cinco de cinco en 1920 x 1080. `obs.captureHasMonitor`, que corría primera, hacía que OBS reiniciara la captura.
- **Fila 9, dio distinto: un cuadro negro en la toma.** Grabando (20:35:54 a 20:36:02), el chequeo de las 20:35:58 no mostró globo ("Cartel: no"), la cámara dio "No aplica" y al parar no hubo ninguna línea "(E4-11)". Pero la toma tiene un cuadro negro de la captura de pantalla a los 4,15 s, justo cuando corrió el chequeo (ffmpeg `signalstats`: YAVG 16 contra 193 en la región de la captura); la toma de control de las 20:32:05, sin chequeo, no tiene ninguno. Las tomas están en `D:\Canal\pruebas\2026-10-19-pampapad-etapa-5`.
- **El arreglo de las filas 8 y 9: `523a21f`**, como eligió Emmanuel (decisión 34): no preguntar grabando. Con una toma en curso, `obs.captureHasMonitor` da "no aplica" sin mandar el pedido, como `obs.cameraNotCovered`; detenida, corre última de las siete, para no engañar a las demás. El parpadeo queda en lo que muestra OBS con la grabación detenida, que no se graba. El OBS falso imita el reinicio (`tests/PampaPad.Tests/Doubles/FakeObs.Capture.cs`). Con una lista `preflight` propia corre en el orden de la lista: ahí una `obs.captureHasMonitor` antes de otra que mida la imagen la puede engañar.
- **Fila 11, un hallazgo que no se arregla ahora.** En el primer intento (20:40:55) Emmanuel apretó parar y grabar enseguida después de arrancar: el parar quedó retenido por la guarda, el grabar llegó antes ("grabar: OBS ya está grabando, no se manda nada.", 20:40:56.059) y el parar salió después (STOPPED a las 20:40:57.242): terminó detenido aunque lo último que apretó fue grabar. El segundo intento (20:43:31) llegó más de un segundo después del STOPPED; el tercero dio bien.
- **Lo que queda para Emmanuel:** repetir las filas 8 y 9 con `523a21f` (sin la falla falsa y sin el cuadro negro), seguidas y con "Vertical SolidWorks" al aire en las dos: en el repaso la 9 corrió con esa escena (el registro de las 20:35:58 la nombra), aunque la 8 decía volver a la de antes, y con Cámara no hay captura de pantalla que pueda parpadear. Las de la placa, cuando use el prototipo.
- **Lo que no se repasa a mano:** el tope y la cancelación de un script (`ScriptRunnerTests`), el `onlyIf` que no se puede evaluar (`ExecutorOnlyIfTests`), la regla 2 de navegación (`DispatcherPagesTests`), las once condiciones contra el OBS falso (`ConditionsTests`, `ObsConditionsTests`, `SceneConditionsTests`) y "ir al banco" por la tecla 3 de la placa con un cable de mentira (`BancoEndToEndTests`).

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
| 3 | Mirar la ventana | En la barra de título y en la barra de tareas, los lentes del canal, el mismo ícono del menú Inicio, y no el genérico de WinForms. A la izquierda, Teclas, Acciones, Scripts, Estado y Ajustes; las que no existen dicen "etapa 7" u "etapa 8" y, al elegirlas, en qué etapa llegan. Arriba, OBS, la grabación y la consola. En Estado: Conexiones, Acciones en curso, Problemas de la configuración, Programa (versión 0.0.0, esquema 1), Chequeo previo ("llega en la etapa 5") y Registro |
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
- **Lo que miró Emmanuel, el 2026-10-20**, con `0a9c833`, antes de mezclar `main`: dijo que la ventana va bien. No quedó anotado fila por fila.
- **Lo que queda para Emmanuel:** mirar la ventana (fila 3), el frente desde la bandeja (fila 2), las teclas apagadas (fila 5), OBS de verdad (filas 6 y 7), el filtro (fila 8), "Abrir PampaPad" (fila 10) y la salida con la ventana abierta (fila 11).
- **Lo que queda para Emmanuel después de la mezcla:** repetir las filas 2 y 3 con `6ca19c6` o una posterior, dos veces: con la biblioteca de ejemplo y con la real (`PAMPAPAD_LIBRARY`, como dice arriba). Desde la mezcla con `main` (`5274391`, las etapas 4 y 5) y E5-23a (`4d7d1f1`) nadie abrió la ventana real con esos catálogos, y es justo lo que rompía E6-7: la página rechazaba la foto y Estado no cargaba. El arreglo se comprobó sólo con las muestras y Playwright. Estado tiene que cargar con Conexiones, Problemas de la configuración y Registro llenos. Si la página rechaza la foto, arriba dice "Sin respuesta del programa" (`ui/src/Frame.tsx:44`) y Estado, "No se pudo leer el estado del programa" (`ui/src/screens/status/Status.tsx:16`). En la fila 3, además, el ícono de la ventana (`6ca19c6`).
- **Fila 2, "al frente".** Show y Activate no siempre la traen al frente (E0-3: 2 de 3). El registro dice en cada apertura si quedó al frente; si no queda, se anota cuándo pasó. En el repaso de Claude, las tres veces ("Ventana mostrada, pero Windows no la dejó al frente"): la segunda copia, la tercera y la cuarta, lanzadas con `Start-Process` desde una herramienta, sin un clic en el menú de la bandeja que diera permiso de primer plano. Con el clic del menú, que es lo que supone el plan, no se midió.
- **Fila 3, el aro de foco.** En la captura de la ventana real, "Teclas" (el primer botón de la columna) se ve con un aro blanco de foco al abrir. Mirar si molesta.
- **La página sola, sin el programa:** `pnpm.cmd -C ui dev` y `http://localhost:5173/#/status` en el navegador. Contesta el puente falso con las muestras de `ui/src/bridge/contract.samples.json`.
- **La ventana abierta mientras se graba sale en el video** si está en el monitor que se captura. No hay regla todavía (05, Incierto).
- **Lo que repasa la prueba automática y no hace falta repetir a mano:** los sobres y los permisos del puente, la foto y los eventos con cadenas (`BridgeTests`, `StatusRequestsTests`), el contrato entre C# y la página (`Contract.cs` y `contract.test.ts`), la pantalla contra el puente falso, con un evento simulado (`ui/tests/status.spec.ts`), y que el programa real mande en `app.snapshot` los tipos de paso y de condición que corre (la prueba de humo sale con 5 si no; `SnapshotCheckTests`). La ventana real con esa foto no la abre ninguna: son las filas 2 y 3.

## Etapa 7: la edición desde la ventana, Teclas y Acciones

Se repasa con la copia de desarrollo de la rama `etapa-7` (worktree `D:\Conocimiento\Sistemas\electronica\pampapad-pc-etapa-7`), armada con la página, y **sobre la biblioteca real**: desde esta etapa la ventana escribe `config.json`. La real la versiona git: antes de empezar, `git -C D:\Conocimiento status --short -- Areas/marca-personal/pampapad` tiene que estar vacío, y si algo sale mal se vuelve con `git -C D:\Conocimiento checkout -- Areas/marca-personal/pampapad/config.json`. Las filas 1 a 4, 10 y 11 andan con los atajos apagados; las 5 a 9 piden que la copia de desarrollo tome los atajos y la instalada esté cerrada ("Salir" en la instalada), porque la captura suspende y reanuda los atajos globales de verdad. OBS sólo hace falta en las filas 12 y 13, sin grabar nada que importe.

Antes de las filas 5 a 9, en `%APPDATA%\PampaPad-dev\local.json` (si el archivo tiene otras claves, cambiar sólo `hotkeysEnabled`):

```json
{
  "version": 1,
  "hotkeysEnabled": true
}
```

Desde la raíz del worktree:

```powershell
pnpm.cmd -C ui build
$env:PAMPAPAD_LIBRARY = 'D:\Conocimiento\Areas\marca-personal\pampapad'
dotnet run --project src\PampaPad.App
```

| N | Qué hacer | Qué tiene que pasar |
|---|---|---|
| 1 | "Abrir PampaPad" en el ícono de la copia de desarrollo | La ventana abre en **Teclas**, en gris sobrio (sin el violeta), con las dos páginas arriba (Grabación, con "principal", y Escenas) y la grilla de 12 de Grabación: BANCO en la 3, ESCEN. en la 4, SHORT a LARGO en la 5 a la 9, MICRO., PAUSA y GRABAR en la 10 a la 12 ("Escenas" y "Micrófono" no traen etiqueta corta: la página recorta a seis con punto, `shortLabelFor`); las teclas 1 y 2 con un guion |
| 2 | Clic en la tecla 1, escribir "Prueba" en Etiqueta y elegir "Marca: short" en Qué hace | La tecla 1 de la grilla pasa a decir PRUEBA en el momento; abajo aparece "cambios sin guardar" y Guardar se enciende. El título de la ventana no cambia |
| 3 | Guardar | "Guardado." sin avisos. `git -C D:\Conocimiento diff -- Areas/marca-personal/pampapad/config.json` muestra sólo las líneas de la tecla nueva, con sangría de dos y sin cambios en el resto del archivo |
| 4 | Con la ventana abierta, en otra terminal: `git -C D:\Conocimiento checkout -- Areas/marca-personal/pampapad/config.json`. Volver a la ventana, cambiar la etiqueta de la tecla 1 y Guardar | Rojo: "No se guardó: config.json cambió en el disco desde que se leyó…" con el botón Recargar biblioteca. Al apretarlo, la tecla 1 vuelve a estar vacía: la ventana muestra lo del disco y el borrador se descartó |
| 5 | Con los atajos encendidos y la instalada cerrada: clic en la tecla 1, Etiqueta "Prueba", Qué hace "Marca: short", "Grabar atajo", y apretar Ctrl+Shift+Alt+P en el teclado | La caja dice "Apretá la combinación…"; al apretarla el campo pasa a `Ctrl+Shift+Alt+P` y la caja se cierra. El registro dice "Atajos suspendidos para grabar una combinación en la ventana." y "Atajos reanudados." |
| 6 | "Grabar atajo" otra vez y apretar F8 sola; después Esc | "Hace falta por lo menos un modificador: Ctrl, Shift o Alt." y la captura sigue; con Esc se cierra y el campo sigue en `Ctrl+Shift+Alt+P`. El registro vuelve a decir suspendidos y reanudados |
| 7 | Guardar, y apretar Ctrl+Shift+Alt+P con OBS cerrado | "Guardado." sin avisos y el registro dice "Atajos tomados: 10; rechazados por Windows: 0." (los 9 de antes más el nuevo). La combinación dispara `marca-short`: con OBS cerrado suena "falló", sale el cartel "Sin OBS: la marca no entró." y el ícono pasa al triángulo (regla de la etapa 3) |
| 8 | "Salir" en la copia de desarrollo; abrir la instalada (menú Inicio), que toma las nueve de la biblioteca; volver a abrir la de desarrollo, con `hotkeysEnabled` todavía encendido. En la ventana: tecla 1, "Grabar atajo", apretar Ctrl+Shift+Alt+F9 (una marca, que ahora tiene la instalada); Esc. Después "Quitar atajo" en la tecla 1 y Guardar | Al abrir, el registro de la de desarrollo dice "Atajos tomados: 1; rechazados por Windows: 9": la instalada tiene las nueve de la biblioteca y la de desarrollo sólo la P. Mientras la caja espera, Ctrl+Shift+Alt+F9 **no llega** a la página (la tiene la instalada: lo que midió E0-3 con `SendInput`), y la instalada dispara `marca-short`: con OBS cerrado suena "falló" y sale su cartel. La caja sigue esperando hasta Esc. Al guardar sin el atajo, "Guardado." con 9 avisos en la barra (`hotkey_taken`, uno por combinación). "Salir" en la instalada y "Recargar biblioteca" en la de desarrollo: "Atajos tomados: 9; rechazados por Windows: 0" |
| 9 | Tecla 1: en Qué hace elegir "Parar" y Guardar | Rojo: "No se guardó: La tecla 1 de Grabación para o pausa la grabación con un toque." con la ubicación `pages[0].keys[0].action`; la tecla 1 se ve rota (borde punteado). Pasar "Parar" a "Al mantener" y poner "Grabar" en Qué hace: Guardar anda. Después volver a "Marca: short" sin "Al mantener" y Guardar |
| 10 | Acciones: "Nombre de la acción nueva" = "Ir al banco"; Crear acción | Aparece "id: ir-al-banco-2": el id sale del nombre y no repite el que ya está. Agregar un paso `wait.window`: el formulario trae Título de la ventana (con las ventanas abiertas de la PC para elegir, o escribirlo), Ancho, Alto, Cuánto esperar y Proceso; "Sólo si" con las once condiciones. Elegir "Marca: short" en la lista y "Quitar la acción": apagado, con "La usan: Grabación, tecla 1 · Grabación, tecla 5" |
| 11 | En Acciones, con "Ir al banco" (la original) elegida, Probar | Con OBS cerrado: en el acto figura en curso y, a los 5 s, "falló" en el paso 2 (`wait.window`: no hay ventana bancocam) y "no corrió" en el 3; el paso 1 dice "salteado" o "bien" según haya corrido `bancocam` (si corre, abre scrcpy: cerrarlo). Sin sonido, sin cartel y el ícono de la bandeja no cambia (la prueba se ve en la pantalla) |
| 12 | Con OBS abierto, Ctrl+Shift+Alt+G (graba): en Teclas, tecla 5, y en Acciones, cualquier acción | Probar y Grabar atajo están apagados con "Se apaga mientras grabás, estás en pausa o transmitís."; Guardar con un cambio da "No se guardó: …read_only_while_recording…". Ctrl+Shift+Alt+D para |
| 13 | Con OBS detenido: Teclas, tecla 1, "Quitar la tecla"; Acciones, la "Ir al banco" nueva de la fila 10 (`ir-al-banco-2`), que no usa nadie: "Quitar la acción"; Guardar | "Guardado." y `git -C D:\Conocimiento diff -- Areas/marca-personal/pampapad/config.json` queda vacío: el archivo volvió byte a byte al de la v0.4.0 |
| 14 | Cambiar una etiqueta sin guardar y cerrar la ventana con la X | Un diálogo nativo "Hay cambios sin guardar", con "Seguir editando" por defecto y "Cerrar sin guardar". Seguir editando deja la ventana; Cerrar sin guardar la cierra, y el registro dice "Ventana cerrada con cambios sin guardar, a pedido." |
| 15 | Cambiar una etiqueta sin guardar y "Salir" en la bandeja | El mismo diálogo. "Seguir editando" no sale del programa; "Cerrar sin guardar" sale, y el registro termina en "Salida ordenada" |

Al terminar: en `%APPDATA%\PampaPad-dev\local.json`, `hotkeysEnabled` en `false`, o borrar el archivo; volver a abrir la copia instalada; `git -C D:\Conocimiento status --short -- Areas/marca-personal/pampapad` vacío (si quedó algo, es lo que el repaso dejó a propósito: se mira y se commitea o se descarta).

## Notas de la etapa 7

- **Lo que repasó Claude solo, el 2026-10-26**, con `ui/scripts/repaso-ventana.mjs` sobre la ventana real y una copia de la biblioteca de ejemplo (plan de la etapa 7, E7-13): asignar la tecla 1 y guardar, el archivo canónico y el respaldo en la carpeta local, el guardado atrasado que falla y recarga, y una acción nueva desde Acciones. El informe está en `D:\repos\pampapad-construccion\E7-13-repaso.txt`.
- **Lo que queda para Emmanuel:** las filas 1 a 15, con el teclado y la biblioteca real; las 12 y 13 con OBS. La captura con el teclado real (filas 5 a 8) nunca se midió: E0-3 fue con `SendInput`.
- **La ventana abierta mientras se graba sale en el video** si está en el monitor que se captura. Sigue sin regla (05, Incierto).
- **Lo que repasa la prueba automática y no hace falta repetir a mano:** el guardado con huella, el respaldo, el temporal y lo desconocido conservado (`LibrarySaveTests`), la configuración candidata y el orden de los parámetros (`ConfigCheckTests`, `ConfigCanonTests`), los pedidos del puente con sus puertas (`ConfigRequestsTests`, `KeysRequestsTests`, `ActionsRequestsTests`, `OptionsListTests`), la suspensión y el reloj de los 30 s con el Windows de mentira (`HotkeySourceSuspendTests`), el borrador que corre (`DispatcherDraftTests`), el avisador que ignora una prueba (`NotifierWindowTests`), el contrato (`Contract.cs`, `contract.test.ts`), los modelos de la página (`model/*.test.ts`) y las dos pantallas contra el puente falso (`keys.spec.ts`, `actions.spec.ts`).
- **El repaso de Emmanuel, el 2026-10-27**, con Claude al lado (las filas de la página, 2 a 4, 9 a 11 y 13, las manejó Claude por el puerto de depuración; las del teclado real, el diálogo nativo y la bandeja, Emmanuel). Dieron lo esperado salvo:
  - **Filas 3 y 13:** guardar desde la ventana reordena `"uuid"` y `"name"` de las 13 referencias a escenas (`{ uuid, name }` pasa a `{ name, uuid }`), aunque nadie las tocó; en la 13 el diff no queda vacío por eso y nada más. Lo mismo pasó con la biblioteca de ejemplo en el repaso de Claude. Probable causa: el paso de objetos entre la página y WebView2 ordena las claves alfabéticamente. Se arregla antes de E7-14.
  - **Fila 8:** al abrir, la instalada tomó 10 atajos y la de desarrollo 0 (no 9 y 1): la P ya estaba guardada en la biblioteca real, que las dos leen. Lo demás de la fila, como se esperaba; "Recargar biblioteca" es la del menú de la bandeja (en la ventana sólo aparece después de un `config_changed_on_disk`).
  - **Fila 9:** el guardado con "Parar" en un toque se frena con el mensaje y la ubicación esperados, pero la tecla 1 no se ve rota en la grilla: la grilla marca los problemas del archivo cargado, no los de un guardado rechazado.
  - **Fila 10:** el campo se llama "Programa", no "Proceso"; "La usan" suma el atajo general `Ctrl+Shift+Alt+F9`, que también usa `marca-short`.
  - **Fila 11:** el paso 1 falló porque `bancocam` no pudo abrir scrcpy (el celular no estaba conectado), así que los pasos 2 y 3 no corrieron. Sin sonido, sin cartel y sin cambio en el ícono, como se esperaba.
  - **Fila 15:** "Salir" es el del menú del ícono de la bandeja; con la X es la fila 14.
