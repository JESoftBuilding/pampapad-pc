namespace PampaPad.Core;

/// <summary>
/// Una línea del evento script.output: { runId, line, stream } por cada renglón de la salida, en orden, y al final { runId, end } con
/// la línea de cierre de la corrida (plan de la etapa 8, E8-5).
/// </summary>
public sealed record ScriptOutputEvent(Guid RunId, string? Line = null, string? Stream = null, ScriptEnd? End = null);

/// <summary>
/// La salida en vivo de un script que se prueba desde la ventana (05 §4.3). ScriptRun le pasa los renglones sólo cuando la invocación es
/// de la ventana; Composition conecta Published con el evento script.output del puente. Corta en MaxLines renglones por corrida, con un
/// aviso: un script que escribe miles inundaría la página. End llega siempre y libera la cuenta de esa corrida.
/// </summary>
public sealed class ScriptOutputSink
{
    public const int MaxLines = 2000;
    public const string Stdout = "stdout";
    public const string Stderr = "stderr";

    /// <summary>La línea que avisa el corte: no es del script.</summary>
    public const string Notice = "notice";

    private readonly Dictionary<Guid, int> _lines = [];
    private readonly Lock _gate = new();

    /// <summary>Cada renglón y cada final, en el hilo de quien lee la salida. Las dos salidas se leen en hilos distintos.</summary>
    public event Action<ScriptOutputEvent>? Published;

    public void Line(Guid runId, string line, bool error)
    {
        int count;
        lock (_gate)
        {
            count = _lines.GetValueOrDefault(runId) + 1;
            _lines[runId] = count;
        }

        if (count <= MaxLines)
        {
            Published?.Invoke(new ScriptOutputEvent(runId, line, error ? Stderr : Stdout));
        }
        else if (count == MaxLines + 1)
        {
            Published?.Invoke(new ScriptOutputEvent(runId, $"La salida pasó las {MaxLines} líneas: desde acá no se muestra. El script sigue corriendo.", Notice));
        }
    }

    public void End(Guid runId, ScriptEnd end)
    {
        lock (_gate)
        {
            _lines.Remove(runId);
        }

        Published?.Invoke(new ScriptOutputEvent(runId, End: end));
    }
}
