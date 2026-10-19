namespace PampaPad.Core;

/// <summary>
/// Lee la salida de un script por renglones sin guardarla entera: un script que escribe mucho no llena la memoria. Corta por "\n" y
/// saca el "\r" del final, porque un .ps1 corta con LF solo y un .cmd con CR y LF (Medido: E0-4 f). Guarda el último renglón no vacío,
/// que es el mensaje del paso (02 §9, punto 6), y le pasa cada renglón a OnLine. Un renglón de más de MaxLine caracteres se recorta.
/// </summary>
public sealed class ScriptOutput(System.Action<string>? onLine)
{
    public const int MaxLine = 500;

    private string? _last;
    private int _lines;

    /// <summary>El último renglón no vacío, o null si no escribió nada.</summary>
    public string? Last => Volatile.Read(ref _last);

    public int Lines => Volatile.Read(ref _lines);

    /// <summary>Lee hasta el final del flujo o hasta que lo cierren. Nunca tira: lo que falle al leer corta la lectura y queda lo leído.</summary>
    public async Task ReadAsync(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var buffer = new char[4096];
        var line = new System.Text.StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
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
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Se cerró el flujo mientras se leía: lo cierra ScriptRunner cuando el script ya terminó y un hijo vivo lo sigue teniendo.
        }

        Emit(line);
    }

    private void Emit(System.Text.StringBuilder line)
    {
        string text = line.ToString().TrimEnd('\r');
        line.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Interlocked.Increment(ref _lines);
        Volatile.Write(ref _last, text.Trim());
        onLine?.Invoke(text);
    }
}
