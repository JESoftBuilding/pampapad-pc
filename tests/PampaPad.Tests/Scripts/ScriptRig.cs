using System.Diagnostics;
using System.Text;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>Una biblioteca temporal con scripts de verdad y un ScriptRunner que los corre (01 §7: "procesos reales de cmd").</summary>
internal sealed class ScriptRig : IAsyncDisposable
{
    /// <summary>
    /// La raíz que se le pasa a cada script. No es la real: en esta PC CONOCIMIENTO ya está en el entorno del usuario, y un script la
    /// heredaría aunque ScriptLaunch no la pasara.
    /// </summary>
    public const string Root = @"X:\raiz-de-prueba";

    /// <summary>Un .ps1 que lanza un ping de 60 s con Start-Process, como bancocam.ps1:9 lanza scrcpy, anota su número en hijo.txt y lo espera.</summary>
    public const string LaunchesPing =
        "$p = Start-Process -PassThru -NoNewWindow ping.exe -ArgumentList '-n','60','127.0.0.1'\nSet-Content -Path (Join-Path $PSScriptRoot 'hijo.txt') -Value $p.Id\nWait-Process -Id $p.Id\n";

    public static readonly Invocation Key = new(Guid.NewGuid(), Trigger.Key, "console", "key.3", Gesture.Press, 0, "ir-al-banco");

    private readonly TempFolder _temp = new();

    public ScriptRig(TimeProvider? clock = null)
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca", "scripts"));
        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        Runner = new ScriptRunner(Library, Log, clock ?? TimeProvider.System, Root);
    }

    public Log Log { get; }

    public CoreLibrary Library { get; }

    public ScriptRunner Runner { get; }

    public string LibraryFolder => _temp.Combine("biblioteca");

    /// <summary>El número del proceso que lanzó el script, cuando lo termina de escribir: mientras PowerShell lo escribe, el archivo está tomado.</summary>
    public static async Task<int> ChildAsync(string folder)
    {
        int? child = null;
        await Poll.Until(() => (child = ReadChild(Path.Combine(folder, "hijo.txt"))) is not null, 20_000);
        return child!.Value;
    }

    /// <summary>El proceso terminó o ya no existe. Un número de proceso reusado en estos segundos daría un falso "vivo": no un falso "muerto".</summary>
    public static bool Gone(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Crea scripts/&lt;id&gt;/ con su ficha y su entry, con los bytes que se le pasan. Devuelve la carpeta.</summary>
    public string Add(string id, string entry, byte[] content, string manifestExtra = "")
    {
        string folder = _temp.Combine("biblioteca", "scripts", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "script.json"), $$"""{ "entry": "{{entry}}"{{manifestExtra}} }""");
        File.WriteAllBytes(Path.Combine(folder, entry), content);
        return folder;
    }

    /// <summary>Un .cmd guardado en la página OEM, como pide el contrato para un .cmd con acentos (02 §9, punto 9).</summary>
    public string AddCmd(string id, string text, string manifestExtra = "") =>
        Add(id, id + ".cmd", ScriptLaunch.Oem.GetBytes(text.ReplaceLineEndings("\r\n")), manifestExtra);

    /// <summary>Un .ps1 guardado en UTF-8 con marca de orden de bytes (02 §9, punto 9).</summary>
    public string AddPs1(string id, string text, string manifestExtra = "") =>
        Add(id, id + ".ps1", [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\r\n"))], manifestExtra);

    public Task<Result<ScriptResult>> RunAsync(string id, CancellationToken ct = default, System.Action<string>? onLine = null, bool? wait = null) =>
        Runner.RunAsync(new ScriptRequest(id, wait, null, Key, onLine), ct);

    public async ValueTask DisposeAsync()
    {
        await Log.DisposeAsync();
        TryDelete();
    }

    private static int? ReadChild(string file)
    {
        try
        {
            return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // Un hijo que quedó vivo puede tener tomado un archivo un rato: la carpeta temporal se intenta borrar y, si no se puede, queda.
    private void TryDelete()
    {
        try
        {
            _temp.Dispose();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"No se pudo borrar la carpeta de la prueba: {error.Message}");
        }
    }
}
