# Camara del banco: espejo del S23 Ultra para la escena "Banco" de OBS.
# - Usa el adb de SuperDisplay (v1.0.40) porque su servicio lo relanza siempre; si scrcpy usara
#   otro adb, los dos servidores se matan entre si y el espejo se cae a los pocos minutos.
# - La captura va rotada porque la app de camara de Samsung queda fija en vertical.
# - La ventana se manda fuera de la pantalla: OBS la captura igual (WGC) y no estorba.
# - scrcpy corre con la consola oculta, como su scrcpy-noconsole.vbs, y sin pausa al salir: un
#   error (celu desconectado) no deja una consola esperando Enter. Si no abre la ventana, se cierra el
#   scrcpy que se lanzo: oculto, quedaria colgado para siempre. Repaso de la etapa 5, 2026-10-19.
$env:ADB = 'C:\Program Files\SuperDisplay\adb\adb.exe'
& $env:ADB shell input keyevent KEYCODE_WAKEUP | Out-Null
& $env:ADB shell am start -n com.sec.android.app.camera/.Camera | Out-Null
$lanzado = Start-Process cmd -WindowStyle Hidden -PassThru -ArgumentList '/c scrcpy --pause-on-exit=false --stay-awake --video-bit-rate 16M --max-fps 30 --window-title bancocam --window-borderless --no-audio --capture-orientation=270'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class BancoWin { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); }
"@
$p = $null
for ($i = 0; $i -lt 40 -and -not $p; $i++) { Start-Sleep -Milliseconds 500; $p = Get-Process scrcpy -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq 'bancocam' } }
if (-not $p) {
    if ($lanzado) {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($lanzado.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $lanzado.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Host "scrcpy no abrio la ventana (celu conectado? depuracion USB? celu desbloqueado?)"; exit 1
}
Start-Sleep 2
$x = [System.Windows.Forms.SystemInformation]::VirtualScreen.Right
[BancoWin]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $x, 0, 1823, 850, 0x0004 -bor 0x0010) | Out-Null
Start-Sleep 1
[BancoWin]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $x, 0, 1823, 850, 0x0004 -bor 0x0010) | Out-Null
Write-Host "bancocam corriendo fuera de pantalla en x=$x (1823x850). Para traerla: cerrar scrcpy y relanzar."
