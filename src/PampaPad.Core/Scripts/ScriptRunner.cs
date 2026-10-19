using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PampaPad.Core;

/// <summary>Lo que se le pide a ScriptRunner. Wait y Timeout pisan los de la ficha (02 §5). OnLine recibe cada renglón de la salida.</summary>
public sealed record ScriptRequest(string Id, bool? Wait, TimeSpan? Timeout, Invocation Invocation, System.Action<string>? OnLine = null);

/// <summary>Cómo terminó un script. Sin esperar, ExitCode y Message van vacíos: el código de salida queda en el registro (02 §9, punto 11).</summary>
public sealed record ScriptResult(int? ExitCode, string? Message, TimeSpan Duration);

/// <summary>
/// El único lugar que ejecuta un script (01 §3.1, Scripts/): lo encuentra por id, lo lanza sin ventana con las variables del contrato,
/// lee la salida de a renglones y, al pasar el tope o al cancelarse la cadena, mata el árbol de procesos del script (02 §9, punto 8;
/// 01 §4.5). Nunca espera bloqueando. El paso termina cuando el script sale, aunque un hijo que quedó vivo siga teniendo la salida: se
/// espera DrainLimit a que termine de llegar y se deja de leer. Las fallas esperadas vuelven como Result con los códigos de 01 §4.1.
/// </summary>
public sealed class ScriptRunner(Library library, Log log, TimeProvider clock, string? root)
{
    /// <summary>El tope si ni el paso ni la ficha dicen otro (02 §9, tabla de la ficha). Propuesto, sin medir.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Cuánto se espera la salida después de que el script salió (Propuesto, sin medir).</summary>
    public static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(1);

    private const string Module = "scripts";

    public async Task<Result<ScriptResult>> RunAsync(ScriptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<ScriptInfo> found = library.ReadScript(request.Id);
        if (!found.Ok)
        {
            return found.Failure;
        }

        ScriptInfo script = found.Value!;
        bool wait = request.Wait ?? script.Manifest.Wait ?? true;
        TimeSpan limit = request.Timeout ?? (script.Manifest.TimeoutSec is int seconds and > 0 ? TimeSpan.FromSeconds(seconds) : DefaultTimeout);
        Invocation invocation = request.Invocation;
        long started = clock.GetTimestamp();

        var process = new Process { StartInfo = ScriptLaunch.StartInfo(script, library.Folder, invocation, root) };
        try
        {
            process.Start();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            return Note(LogLevel.Warning, invocation, ErrorCodes.ScriptFailed, $"El script \"{script.Id}\" no pudo arrancar: {error.Message}");
        }

        process.StandardInput.Close();
        var output = new ScriptOutput(request.OnLine);
        var errors = new ScriptOutput(null);
        Task reading = Task.WhenAll(output.ReadAsync(process.StandardOutput), errors.ReadAsync(process.StandardError));
        log.Write(LogLevel.Info, Module, $"{script.Id}: arrancó ({Path.GetFileName(script.EntryPath)}, tope {limit.TotalSeconds:0} s).", null, invocation.RequestedBy, invocation.RunId);

        if (!wait)
        {
            // Termina bien apenas arranca; el tope sigue corriendo y el código de salida queda en el registro (punto 11).
            _ = WatchAsync(process, reading, script.Id, limit, invocation);
            return new ScriptResult(null, null, clock.GetElapsedTime(started));
        }

        using (process)
        {
            using var timeout = new CancellationTokenSource(limit, clock);
            using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                await process.WaitForExitAsync(both.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillTree(process, script.Id, invocation);
                await DrainAsync(process, reading).ConfigureAwait(false);
                return timeout.IsCancellationRequested && !ct.IsCancellationRequested
                    ? Note(LogLevel.Warning, invocation, ErrorCodes.ScriptTimeout, $"El script \"{script.Id}\" pasó el tope de {limit.TotalSeconds:0} s: se cortó, con lo que había lanzado.")
                    : Note(LogLevel.Info, invocation, ErrorCodes.Cancelled, $"Se canceló el script \"{script.Id}\": se cortó, con lo que había lanzado.");
            }

            await DrainAsync(process, reading).ConfigureAwait(false);
            int exit = process.ExitCode;
            string? message = output.Last ?? errors.Last;
            TimeSpan took = clock.GetElapsedTime(started);
            if (exit != 0)
            {
                return Note(LogLevel.Warning, invocation, ErrorCodes.ScriptFailed,
                    $"El script \"{script.Id}\" salió con {exit}{(message is null ? "" : $": {message}")}");
            }

            log.Write(LogLevel.Info, Module, $"{script.Id} terminó con 0 en {took.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s{(message is null ? "." : $": {message}")}", null, invocation.RequestedBy, invocation.RunId);
            return new ScriptResult(exit, message, took);
        }
    }

    // Sin esperar: el script corre solo hasta que sale o hasta el tope. Lo que pase queda en el registro y no corta ninguna cadena.
    private async Task WatchAsync(Process process, Task reading, string id, TimeSpan limit, Invocation invocation)
    {
        using (process)
        {
            using var timeout = new CancellationTokenSource(limit, clock);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                log.Write(LogLevel.Info, Module, $"{id} (sin esperar) salió con {process.ExitCode}.", null, invocation.RequestedBy, invocation.RunId);
            }
            catch (OperationCanceledException)
            {
                KillTree(process, id, invocation);
                log.Write(LogLevel.Warning, Module, $"{id} (sin esperar) pasó el tope de {limit.TotalSeconds:0} s: se cortó.", ErrorCodes.ScriptTimeout, invocation.RequestedBy, invocation.RunId);
            }

            await DrainAsync(process, reading).ConfigureAwait(false);
        }
    }

    // Lo que siga escribiendo un hijo vivo que heredó la salida no traba el paso: pasado DrainLimit se deja de leer y se cierran los flujos,
    // que Process no cierra solo cuando se leen de a bloques (02, Incierto: "la lectura puede no terminar aunque el script haya salido").
    private async Task DrainAsync(Process process, Task reading)
    {
        try
        {
            await reading.WaitAsync(DrainLimit, clock).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            log.Info(Module, $"La salida siguió abierta {DrainLimit.TotalSeconds:0} s después de que el script salió: un proceso que lanzó la tiene. Se deja de leer.");
            process.StandardOutput.Close();
            process.StandardError.Close();
        }
    }

    // Mata el script y lo que lanzó y todavía es hijo suyo (01 §4.5). Lo ya desprendido, como un scrcpy de un script que ya salió, no.
    private void KillTree(Process process, string id, Invocation invocation)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            log.Write(LogLevel.Warning, Module, $"No se pudo cortar el script \"{id}\": {error.Message}", ErrorCodes.ScriptTimeout, invocation.RequestedBy, invocation.RunId);
        }
    }

    private Failure Note(LogLevel level, Invocation invocation, string code, string message)
    {
        log.Write(level, Module, message, code, invocation.RequestedBy, invocation.RunId);
        return new Failure(code, message);
    }
}
