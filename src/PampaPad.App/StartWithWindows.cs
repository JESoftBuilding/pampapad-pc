using Microsoft.Win32;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// Arranque con la sesión: un valor en la clave Run del usuario con el ejecutable de arranque de la raíz de instalación,
/// que no aplica solo una versión bajada (Medido en E0-6). No apunta a current, como el acceso directo del menú Inicio:
/// a ese lo reescribe Velopack en cada actualización, y a un valor de Run no lo mantiene nadie.
/// </summary>
internal static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Lo llama Velopack al instalar. Fuera de current no hay ejecutable de arranque y no escribe nada.</summary>
    public static void Enable()
    {
        var launcher = InstallLayout.LauncherPath(AppContext.BaseDirectory);
        if (launcher is null)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(AppIdentity.Name, $"\"{launcher}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(AppIdentity.Name, false);
    }
}
