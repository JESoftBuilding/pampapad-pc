using Microsoft.Win32;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// Arranque con la sesión: un valor en la clave Run del usuario con el ejecutable de arranque de la raíz de instalación,
/// que no aplica solo una versión bajada (Medido en E0-6). No apunta a current, como el acceso directo del menú Inicio:
/// a ese lo reescribe Velopack en cada actualización, y a un valor de Run no lo mantiene nadie. Como IStartup es el interruptor de la
/// tarjeta Programa de Ajustes (etapa 8): en una copia sin instalar no lee ni escribe el registro, para no tocar el de la instalada.
/// </summary>
internal sealed class StartWithWindows(bool installed) : IStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool? IsEnabled
    {
        get
        {
            if (!installed)
            {
                return null;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppIdentity.Name) is not null;
        }
    }

    public Result Set(bool on)
    {
        if (!installed)
        {
            return Result.Fail(ErrorCodes.InvalidData, "El arranque con Windows está " + SettingsRequests.NotInstalled + ".");
        }

        try
        {
            if (on)
            {
                Enable();
            }
            else
            {
                Disable();
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Result.Fail(ErrorCodes.InternalError, "Windows no dejó cambiar el arranque: " + error.Message);
        }

        return on && IsEnabled != true
            ? Result.Fail(ErrorCodes.InternalError, "No se encontró el ejecutable de arranque de la instalación: el arranque con Windows quedó apagado.")
            : Result.Success;
    }

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
