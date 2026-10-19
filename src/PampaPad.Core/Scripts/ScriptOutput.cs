namespace PampaPad.Core;

/// <summary>
/// Lee la salida de un script por renglones sin guardarla entera: un script que escribe mucho no llena la memoria. Corta por "\n" y
/// saca el "\r" del final, porque un .ps1 corta con LF solo y un .cmd con CR y LF (Medido: E0-4 f). Guarda el último renglón no vacío,
/// que es el mensaje del paso (02 §9, punto 6), y le pasa cada renglón a OnLine. Un renglón de más de MaxLine caracteres se recorta.
/// </summary>
public sealed class ScriptOutput(System.Action<string>? onLine)
{
    public const int MaxLine = 500;

    private System.Action<string>? _onLine = onLine;
    private string? _last;
    private int _lines;
    private volatile bool _stopped;
    private Exception? _listenerError;

    /// <summary>El último renglón no vacío, o null si no escribió nada.</summary>
    public string? Last => Volatile.Read(ref _last);

    public int Lines => Volatile.Read(ref _lines);

    /// <summary>Lo que tiró OnLine la primera vez, o null. Desde ahí no se lo llamó más y se siguió leyendo.</summary>
    public Exception? ListenerError => Volatile.Read(ref _listenerError);

    /// <summary>Desde acá lo que llegue se descarta: no cambia Last ni se le pasa a OnLine. Lo llama ScriptRunner al dejar de leer.</summary>
    public void Stop() => _stopped = true;

    /// <summary>
    /// Lee hasta el final del flujo, hasta que lo cierren o hasta que se cancele stop. Nunca tira: lo que falle al leer corta la lectura y
    /// queda lo leído.
    /// </summary>
    public async Task ReadAsync(TextReader reader, CancellationToken stop = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var buffer = new char[4096];
        var line = new System.Text.StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), stop).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        Emit(line);
                    }
                    else if (line.Length < MaxLine)
                    {
                        line.Append(buffer[i]);
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            // Se dejó de leer: lo corta ScriptRunner cuando el script ya terminó y un hijo vivo sigue teniendo la salida.
        }

        Emit(line);
    }

    private void Emit(System.Text.StringBuilder line)
    {
        string text = line.ToString().TrimEnd('\r');
        line.Clear();
        if (_stopped || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Interlocked.Increment(ref _lines);
        Volatile.Write(ref _last, text.Trim());
        if (_onLine is not { } listener)
        {
            return;
        }

        try
        {
            listener(text);
        }
        catch (Exception error)
        {
            // Quien escucha no corta la lectura: sin leer, el script se trabaría con la salida llena (unos 4 KB) y terminaría en un tope
            // que no pasó. Se lo deja de llamar y ScriptRunner lo anota.
            _onLine = null;
            Volatile.Write(ref _listenerError, error);
        }
    }
}
