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

    // El tope más largo que acepta un CancellationTokenSource, unos 49,7 días. El de la ficha no tiene máximo (lo corta igual el de la
    // acción), pero uno más largo hacía tirar al armar el tope, con el script ya lanzado y sin nada que lo cortara.
    private static readonly TimeSpan LongestLimit = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

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
        TimeSpan asked = request.Timeout ?? (script.Manifest.TimeoutSec is int seconds and > 0 ? TimeSpan.FromSeconds(seconds) : DefaultTimeout);
        TimeSpan limit = asked > LongestLimit ? LongestLimit : asked;
        Invocation invocation = request.Invocation;

        // Una cadena ya cancelada no lanza el script: lo que hace pasaría antes de que se lo pudiera cortar.
        if (ct.IsCancellationRequested)
        {
            return Note(LogLevel.Info, invocation, ErrorCodes.Cancelled, $"Se canceló el script \"{script.Id}\" antes de arrancar.");
        }

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
        var streams = new Streams(process, request.OnLine);
        log.Write(LogLevel.Info, Module, $"{script.Id}: arrancó ({Path.GetFileName(script.EntryPath)}, tope {limit.TotalSeconds:0} s).", null, invocation.RequestedBy, invocation.RunId);

        if (!wait)
        {
            // Termina bien apenas arranca; el tope sigue corriendo y el código de salida queda en el registro (punto 11).
            _ = WatchAsync(process, streams, script.Id, limit, invocation);
            return new ScriptResult(null, null, clock.GetElapsedTime(started));
        }

        using (process)
        using (streams)
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
                await DrainAsync(process, streams, script.Id, invocation).ConfigureAwait(false);
                return timeout.IsCancellationRequested && !ct.IsCancellationRequested
                    ? Note(LogLevel.Warning, invocation, ErrorCodes.ScriptTimeout, $"El script \"{script.Id}\" pasó el tope de {limit.TotalSeconds:0} s: se cortó, con lo que había lanzado.")
                    : Note(LogLevel.Info, invocation, ErrorCodes.Cancelled, $"Se canceló el script \"{script.Id}\": se cortó, con lo que había lanzado.");
            }

            await DrainAsync(process, streams, script.Id, invocation).ConfigureAwait(false);
            int exit = process.ExitCode;
            string? message = streams.Output.Last ?? streams.Errors.Last;
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
    private async Task WatchAsync(Process process, Streams streams, string id, TimeSpan limit, Invocation invocation)
    {
        try
        {
            using (process)
            using (streams)
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

                await DrainAsync(process, streams, id, invocation).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            // Nadie espera esta tarea (RunAsync la suelta): lo que tire queda en el registro y no se pierde callado.
            log.Write(LogLevel.Error, Module, $"{id} (sin esperar): no se lo pudo seguir: {error.Message}", ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId);
        }
    }

    // Lo que siga escribiendo un hijo vivo que heredó la salida no traba el paso: pasado DrainLimit se deja de leer. Cancelar la lectura
    // la corta en el acto y suelta su hilo; cerrar el flujo solo no alcanzaba: la lectura pendiente lo tenía tomado y seguía hasta que el
    // hijo soltaba la salida, y le pasaba a OnLine renglones de un paso ya terminado (revisión de E5-6, 2026-10-19). Si ni cancelada se
    // corta en DrainLimit, queda en el registro: el renglón tarde ya se descarta y no se notaría. Después se cierran los flujos, que
    // Process no cierra solo cuando se leen de a bloques: un hijo que siga escribiendo no se traba con la salida llena.
    private async Task DrainAsync(Process process, Streams streams, string id, Invocation invocation)
    {
        try
        {
            await streams.Reading.WaitAsync(DrainLimit, clock).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            streams.Stop();
            log.Info(Module, $"La salida siguió abierta {DrainLimit.TotalSeconds:0} s después de que el script salió: un proceso que lanzó la tiene. Se deja de leer.");
            await streams.Reading.WaitAsync(DrainLimit, clock).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!streams.Reading.IsCompleted)
            {
                log.Write(LogLevel.Warning, Module, $"{id}: la lectura de su salida no se cortó al cancelarla: un hilo queda tomado hasta que el proceso que la tiene la suelte.", ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId);
            }

            process.StandardOutput.Close();
            process.StandardError.Close();
        }

        if (streams.Output.ListenerError is { } error)
        {
            log.Write(LogLevel.Error, Module, $"{id}: quien escuchaba su salida tiró ({error.Message}): se lo dejó de llamar y se siguió leyendo.", ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId);
        }
    }

    // Mata el script y lo que lanzó y todavía es hijo suyo (01 §4.5). Lo ya desprendido, como un scrcpy de un script que ya salió, no. Un
    // descendiente que no se deja matar (uno elevado, por ejemplo) llega como AggregateException.
    private void KillTree(Process process, string id, Invocation invocation)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            log.Write(LogLevel.Warning, Module, $"No se pudo cortar el script \"{id}\": {error.Message}", ErrorCodes.ScriptTimeout, invocation.RequestedBy, invocation.RunId);
        }
    }

    private Failure Note(LogLevel level, Invocation invocation, string code, string message)
    {
        log.Write(level, Module, message, code, invocation.RequestedBy, invocation.RunId);
        return new Failure(code, message);
    }

    // Las dos salidas de un script mientras se leen, y cómo dejar de leerlas.
    private sealed class Streams : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();

        public Streams(Process process, System.Action<string>? onLine)
        {
            Output = new ScriptOutput(onLine);
            Errors = new ScriptOutput(null);
            Reading = Task.WhenAll(Output.ReadAsync(process.StandardOutput, _stop.Token), Errors.ReadAsync(process.StandardError, _stop.Token));
        }

        public ScriptOutput Output { get; }

        public ScriptOutput Errors { get; }

        public Task Reading { get; }

        // Desde acá no llega nada más, ni a Last ni a OnLine, y la lectura pendiente se corta (Medido el 2026-10-19: sale enseguida y el
        // hilo que leía queda libre, aunque el hijo siga vivo).
        public void Stop()
        {
            Output.Stop();
            Errors.Stop();
            _stop.Cancel();
        }

        public void Dispose() => _stop.Dispose();
    }
}
