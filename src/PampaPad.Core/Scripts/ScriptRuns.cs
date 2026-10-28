using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Las últimas corridas de cada script, sacadas de sus líneas de cierre (las que llevan End, etapa 8). Al armarse lee las de los
/// archivos del registro de los últimos Log.DaysKept días; después suma las que escribe el registro. La salida entera no se guarda: la
/// "última salida" de la ficha es el mensaje de la corrida.
/// </summary>
public sealed class ScriptRuns : IDisposable
{
    /// <summary>Cuántas corridas se guardan por script: más de las cinco que muestra scripts.read (Propuesto, sin fuente).</summary>
    public const int KeptPerScript = 20;

    private readonly Log _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<LogEntry>> _runs = new(StringComparer.Ordinal);

    public ScriptRuns(Log log, string logFolder, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        _log = log;

        // Primero se escucha y después se leen los archivos: una línea que caiga entre los dos se repite en vez de perderse, y Add la descarta.
        _log.Written += Add;
        foreach (LogEntry entry in ReadFiles(logFolder, DateOnly.FromDateTime(clock.GetLocalNow().DateTime)))
        {
            Add(entry);
        }
    }

    /// <summary>Las últimas count corridas del script, primero la más nueva.</summary>
    public IReadOnlyList<LogEntry> Last(string id, int count)
    {
        lock (_gate)
        {
            return _runs.TryGetValue(id, out List<LogEntry>? runs) ? [.. runs.OrderByDescending(run => run.At).Take(count)] : [];
        }
    }

    public void Dispose() => _log.Written -= Add;

    private void Add(LogEntry entry)
    {
        if (entry is not { Script: { } id, End: not null })
        {
            return;
        }

        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out List<LogEntry>? runs))
            {
                _runs[id] = runs = [];
            }

            if (runs.Contains(entry))
            {
                return;
            }

            runs.Add(entry);
            if (runs.Count > KeptPerScript)
            {
                runs.Remove(runs.MinBy(run => run.At)!);
            }
        }
    }

    // Los archivos de los días que guarda el registro, del más viejo al más nuevo. Una línea que no se lee (otra versión, un corte a
    // mitad de escritura) se saltea: el registro no puede dejar sin la lista de scripts.
    private static IEnumerable<LogEntry> ReadFiles(string folder, DateOnly today)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        for (DateOnly day = today.AddDays(1 - Log.DaysKept); day <= today; day = day.AddDays(1))
        {
            string file = Path.Combine(folder, Log.FileName(day));
            string[] lines;
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string line in lines)
            {
                LogEntry? entry = Parse(line);
                if (entry is not null)
                {
                    yield return entry;
                }
            }
        }
    }

    private static LogEntry? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<LogEntry>(line, Json.Compact);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
