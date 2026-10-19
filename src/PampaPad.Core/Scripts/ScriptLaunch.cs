using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Cómo se lanza cada script según el contrato de ejecución 1 (02 §9): con qué intérprete, con qué carpeta de trabajo, con qué variables
/// y con qué codificación se lee su salida. Es puro salvo la página OEM, que se le pide a Windows una vez. No lanza nada: eso es de
/// ScriptRunner.
/// </summary>
public static class ScriptLaunch
{
    /// <summary>La de un .cmd y un .ps1 lanzados sin ventana (Medido: E0-4 f; la fila .exe, sin probar).</summary>
    public static Encoding Oem { get; } = CreateOem();

    public static Encoding Utf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>La tabla del contrato (02 §9, puntos 1 y 9): un .py en UTF-8 con PYTHONUTF8=1; lo demás, en la página OEM.</summary>
    public static Encoding OutputEncoding(string entryPath) =>
        string.Equals(Path.GetExtension(entryPath), ".py", StringComparison.OrdinalIgnoreCase) ? Utf8 : Oem;

    public static ProcessStartInfo StartInfo(ScriptInfo script, string libraryFolder, Invocation invocation, string? root)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(invocation);
        Encoding encoding = OutputEncoding(script.EntryPath);

        // Sin consola visible: la escena Pantalla graba el monitor entero (punto 4). La entrada se cierra al arrancar (punto 7).
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,

            // La carpeta scripts/ y no la del script: un hijo vivo, como scrcpy, la dejaría tomada (punto 2). Fuera de current: Velopack mata
            // lo que corre ahí al actualizar (01 §9).
            WorkingDirectory = Path.Combine(libraryFolder, ScriptFolder.FolderName),
        };

        switch (Path.GetExtension(script.EntryPath).ToLowerInvariant())
        {
            case ".cmd":
                info.FileName = "cmd.exe";
                info.ArgumentList.Add("/d");
                info.ArgumentList.Add("/c");
                info.ArgumentList.Add(script.EntryPath);
                break;
            case ".ps1":
                // El PowerShell 5.1 de Windows, como bancocam.cmd:2.
                info.FileName = "powershell.exe";
                foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script.EntryPath })
                {
                    info.ArgumentList.Add(argument);
                }

                break;
            case ".py":
                // py -3 corre en esta PC el Python 3.12 del usuario, no el de Inkscape (Medido: E0-4). PYTHONIOENCODING se fija también: la
                // heredada puede no ser UTF-8 y cambia lo que escribe Python aunque esté PYTHONUTF8 (02, Incierto).
                info.FileName = "py.exe";
                info.ArgumentList.Add("-3");
                info.ArgumentList.Add(script.EntryPath);
                info.Environment["PYTHONUTF8"] = "1";
                info.Environment["PYTHONIOENCODING"] = "utf-8";
                break;
            default:
                info.FileName = script.EntryPath;
                break;
        }

        // Las variables del punto 3, siempre presentes. CONOCIMIENTO va aunque el entorno del programa no la tenga.
        info.Environment["PAMPAPAD_LIBRARY"] = libraryFolder;
        info.Environment["PAMPAPAD_SCRIPT_DIR"] = script.Folder;
        info.Environment["PAMPAPAD_ACTION"] = invocation.ActionId ?? "";
        info.Environment["PAMPAPAD_TRIGGER"] = JsonNamingPolicy.CamelCase.ConvertName(invocation.RequestedBy.ToString());
        info.Environment["PAMPAPAD_SOURCE"] = invocation.Source ?? "";
        if (root is not null)
        {
            info.Environment[DataRoot.Variable] = root;
        }

        return info;
    }

    // La página OEM de Windows (850 en esta PC: valor OEMCP del registro, 02 §9) no viene con .NET: la trae este proveedor, que sí viene
    // con el runtime.
    private static Encoding CreateOem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetOEMCP());
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
