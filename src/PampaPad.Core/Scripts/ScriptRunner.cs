using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PampaPad.Core;

/// <summary>
/// El único lugar que ejecuta un script (01 §3.1, Scripts/): lo encuentra por id, lo lanza sin ventana con las variables del contrato,
/// lee la salida de a renglones y, al pasar el tope o al cancelarse la cadena, mata el árbol de procesos del script (02 §9, punto 8;
/// 01 §4.5). Nunca espera bloqueando. El paso termina cuando el script sale, aunque un hijo que quedó vivo siga teniendo la salida: se
/// espera DrainLimit a que termine de llegar y se deja de leer. Las fallas esperadas vuelven como Result con los códigos de 01 §4.1.
/// Cada línea de una corrida lleva el id del script, y cada corrida termina con una sola línea de cierre, con End (etapa 8).
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

    // Por id, cuántas corridas hay en curso: el mismo script puede correr dos veces a la vez desde dos acciones. Sin distinguir mayúsculas,
    // como el disco: "Hola" encuentra la carpeta hola (ScriptFolder.Inspect), y tiene que verse corriendo.
    private readonly Dictionary<string, int> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Los ids de los scripts que están corriendo ahora, también los lanzados sin esperar. Una copia: no cambia después.</summary>
    public IReadOnlySet<string> Running
    {
        get
        {
            lock (_gate)
            {
                return _running.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public async Task<Result<ScriptResult>> RunAsync(ScriptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Invocation invocation = request.Invocation;
        Result<ScriptInfo> found = library.ReadScript(request.Id);
        if (!found.Ok)
        {
            return Close(request, LogLevel.Warning, found.Failure!.Code, found.Failure.Message, Ended.NotStarted, counted: false);
        }

        ScriptInfo script = found.Value!;
        bool wait = request.Wait ?? script.Manifest.Wait ?? true;
        TimeSpan asked = request.Timeout ?? (script.Manifest.TimeoutSec is int seconds and > 0 ? TimeSpan.FromSeconds(seconds) : DefaultTimeout);
        TimeSpan limit = asked > LongestLimit ? LongestLimit : asked;

        // Una cadena ya cancelada no lanza el script: lo que hace pasaría antes de que se lo pudiera cortar.
        if (ct.IsCancellationRequested)
        {
            return Close(request, LogLevel.Info, ErrorCodes.Cancelled, $"Se canceló el script \"{script.Id}\" antes de arrancar.", Ended.NotStarted, counted: false);
        }

        long started = clock.GetTimestamp();
        var process = new Process { StartInfo = ScriptLaunch.StartInfo(script, library.Folder, invocation, root) };
        Count(script.Id, +1);
        try
        {
            process.Start();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            return Close(request, LogLevel.Warning, ErrorCodes.ScriptFailed, $"El script \"{script.Id}\" no pudo arrancar: {error.Message}", Ended.NotStarted);
        }

        process.StandardInput.Close();
        var streams = new Streams(process, request.OnLine);
        log.Write(LogLevel.Info, Module, $"{script.Id}: arrancó ({Path.GetFileName(script.EntryPath)}, tope {limit.TotalSeconds:0} s).", null, invocation.RequestedBy, invocation.RunId, script.Id);

        if (!wait)
        {
            // Termina bien apenas arranca; el tope sigue corriendo y el código de salida queda en el registro (punto 11).
            _ = WatchAsync(process, streams, request, limit, started);
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
                var cut = new Ended(null, streams.Last, clock.GetElapsedTime(started));
                return timeout.IsCancellationRequested && !ct.IsCancellationRequested
                    ? Close(request, LogLevel.Warning, ErrorCodes.ScriptTimeout, $"El script \"{script.Id}\" pasó el tope de {limit.TotalSeconds:0} s: se cortó, con lo que había lanzado.", cut)
                    : Close(request, LogLevel.Info, ErrorCodes.Cancelled, $"Se canceló el script \"{script.Id}\": se cortó, con lo que había lanzado.", cut);
            }

            await DrainAsync(process, streams, script.Id, invocation).ConfigureAwait(false);
            int exit = process.ExitCode;
            string? message = streams.Last;
            var ended = new Ended(exit, message, clock.GetElapsedTime(started));
            if (exit != 0)
            {
                return Close(request, LogLevel.Warning, ErrorCodes.ScriptFailed, $"El script \"{script.Id}\" salió con {exit}{(message is null ? "" : $": {message}")}", ended);
            }

            return Close(request, LogLevel.Info, null,
                $"{script.Id} terminó con 0 en {ended.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s{(message is null ? "." : $": {message}")}", ended);
        }
    }

    // Sin esperar: el script corre solo hasta que sale o hasta el tope. Lo que pase queda en el registro y no corta ninguna cadena.
    private async Task WatchAsync(Process process, Streams streams, ScriptRequest request, TimeSpan limit, long started)
    {
        string id = request.Id;
        Invocation invocation = request.Invocation;
        bool closed = false;
        try
        {
            using (process)
            using (streams)
            {
                using var timeout = new CancellationTokenSource(limit, clock);
                bool cut = false;
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    KillTree(process, id, invocation);
                    cut = true;
                }

                await DrainAsync(process, streams, id, invocation).ConfigureAwait(false);
                int? exit = cut ? null : process.ExitCode;
                var ended = new Ended(exit, streams.Last, clock.GetElapsedTime(started));
                closed = true;
                _ = cut
                    ? Close(request, LogLevel.Warning, ErrorCodes.ScriptTimeout, $"{id} (sin esperar) pasó el tope de {limit.TotalSeconds:0} s: se cortó.", ended)
                    : Close(request, LogLevel.Info, null, $"{id} (sin esperar) salió con {exit}.", ended, ok: exit == 0);
            }
        }
        catch (Exception error)
        {
            // Nadie espera esta tarea (RunAsync la suelta): lo que tire queda en el registro y no se pierde callado.
            string message = $"{id} (sin esperar): no se lo pudo seguir: {error.Message}";
            if (closed)
            {
                log.Write(LogLevel.Error, Module, message, ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId, id);
            }
            else
            {
                Close(request, LogLevel.Error, ErrorCodes.InternalError, message, new Ended(null, null, clock.GetElapsedTime(started)));
            }
        }
    }

    // La línea de cierre, la única con End, y OnFinished: el final común de todos los caminos. Sin código es una corrida que salió con 0,
    // salvo que ok diga otra cosa (sin esperar, el código de salida no corta nada). Saca el id de Running si lo había contado.
    private Result<ScriptResult> Close(ScriptRequest request, LogLevel level, string? code, string message, Ended ended, bool counted = true, bool? ok = null)
    {
        Invocation invocation = request.Invocation;
        bool fine = ok ?? code is null;
        var result = new ScriptResult(ended.ExitCode, ended.Message ?? (fine ? null : message), ended.Duration, fine);
        log.Write(level, Module, message, code, invocation.RequestedBy, invocation.RunId, request.Id,
            new ScriptEnd(fine, result.ExitCode, (long)result.Duration.TotalMilliseconds, result.Message));
        if (counted)
        {
            Count(request.Id, -1);
        }

        try
        {
            request.OnFinished?.Invoke(result);
        }
        catch (Exception error)
        {
            log.Write(LogLevel.Error, Module, $"{request.Id}: quien esperaba el final tiró ({error.Message}).", ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId, request.Id);
        }

        return code is null ? result : new Failure(code, message);
    }

    private void Count(string id, int delta)
    {
        lock (_gate)
        {
            int now = _running.GetValueOrDefault(id) + delta;
            if (now > 0)
            {
                _running[id] = now;
            }
            else
            {
                _running.Remove(id);
            }
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
            log.Write(LogLevel.Info, Module, $"La salida siguió abierta {DrainLimit.TotalSeconds:0} s después de que el script salió: un proceso que lanzó la tiene. Se deja de leer.", null, invocation.RequestedBy, invocation.RunId, id);
            await streams.Reading.WaitAsync(DrainLimit, clock).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!streams.Reading.IsCompleted)
            {
                log.Write(LogLevel.Warning, Module, $"{id}: la lectura de su salida no se cortó al cancelarla: un hilo queda tomado hasta que el proceso que la tiene la suelte.", ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId, id);
            }

            process.StandardOutput.Close();
            process.StandardError.Close();
        }

        if ((streams.Output.ListenerError ?? streams.Errors.ListenerError) is { } error)
        {
            log.Write(LogLevel.Error, Module, $"{id}: quien escuchaba su salida tiró ({error.Message}): se lo dejó de llamar y se siguió leyendo.", ErrorCodes.InternalError, invocation.RequestedBy, invocation.RunId, id);
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
            log.Write(LogLevel.Warning, Module, $"No se pudo cortar el script \"{id}\": {error.Message}", ErrorCodes.ScriptTimeout, invocation.RequestedBy, invocation.RunId, id);
        }
    }

    // Cómo terminó una corrida, para la línea de cierre: sin código si no salió sola, con su última línea si escribió alguna.
    private sealed record Ended(int? ExitCode, string? Message, TimeSpan Duration)
    {
        public static Ended NotStarted { get; } = new(null, null, TimeSpan.Zero);
    }

    // Las dos salidas de un script mientras se leen, y cómo dejar de leerlas.
    private sealed class Streams : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();

        public Streams(Process process, System.Action<string>? onLine)
        {
            Output = new ScriptOutput(onLine);
            Errors = new ScriptOutput(onLine is null ? null : line => onLine(ScriptRequest.ErrorMark + line));
            Reading = Task.WhenAll(Output.ReadAsync(process.StandardOutput, _stop.Token), Errors.ReadAsync(process.StandardError, _stop.Token));
        }

        public ScriptOutput Output { get; }

        public ScriptOutput Errors { get; }

        /// <summary>La última línea de la corrida: la de la salida normal o, si no hubo, la de los errores.</summary>
        public string? Last => Output.Last ?? Errors.Last;

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
