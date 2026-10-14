namespace PampaPad.Core;

/// <summary>La mitad de SerialConsole que parte lo que llega en líneas y saca los eventos crudos.</summary>
public sealed partial class SerialConsole
{
    // Un carácter más que la línea más larga: una línea que se pasa queda cortada en 56 y cae sola en su clase,
    // "mal formada" si empieza con '#' y texto si no.
    private readonly char[] _line = new char[ConsoleProtocol.MaxLineLength + 1];
    private int _lineLength;
    private int _keys;
    private long _malformed;
    private long _unknown;

    /// <summary>Líneas con '#' que no cumplían el formato, desde que arrancó el programa.</summary>
    public long MalformedLines => Interlocked.Read(ref _malformed);

    /// <summary>Mensajes bien formados de un tipo que esta versión no conoce, desde que arrancó el programa.</summary>
    public long UnknownMessages => Interlocked.Read(ref _unknown);

    private void ResetLine() => _lineLength = 0;

    /// <summary>Sin enlace no se sabe qué teclas están apretadas: al volver, las que lo estén entran como nuevas.</summary>
    private void ForgetKeys() => _keys = 0;

    /// <summary>Junta bytes hasta el fin de línea. Lo que queda a medias espera a la próxima lectura.</summary>
    private void Consume(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value == '\n')
            {
                HandleLine(_line.AsSpan(0, _lineLength));
                _lineLength = 0;
            }
            else if (value != '\r' && _lineLength < _line.Length)
            {
                _line[_lineLength++] = (char)value;
            }
        }
    }

    private void HandleLine(ReadOnlySpan<char> line)
    {
        var kind = ConsoleProtocol.Parse(line, out var message);
        if (kind == ConsoleLineKind.Text)
        {
            return;
        }

        if (kind == ConsoleLineKind.Malformed)
        {
            CountMalformed();
            return;
        }

        if (message.Type != ConsoleProtocol.InputStateType)
        {
            // Un tipo desconocido se ignora y se cuenta (04 §7): es lo que deja crecer el protocolo sin recargar firmware.
            if (Interlocked.Increment(ref _unknown) == 1)
            {
                _log.Info(SourceId, $"Llegó un mensaje de tipo 0x{message.Type:X2}, que esta versión no conoce. Se ignora; los siguientes sólo se cuentan.");
            }

            return;
        }

        if (!ConsoleProtocol.TryDecodeInputState(message.Payload, out var input))
        {
            CountMalformed();
            return;
        }

        // Por nivel: un Press por cada tecla apretada, aunque ya lo estuviera. El repetido es el refresco de Gestures.
        var events = Gestures.FromKeys(SourceId, _keys, input.Keys);
        _keys = input.Keys;
        foreach (var e in events)
        {
            Raise(e);
        }
    }

    private void CountMalformed()
    {
        if (Interlocked.Increment(ref _malformed) == 1)
        {
            _log.Warning(SourceId, "Llegó una línea con '#' que no cumple el formato. Se ignora; las siguientes sólo se cuentan.");
        }
    }

    private void Raise(InputEvent e)
    {
        try
        {
            Input?.Invoke(e);
        }
        catch (Exception error)
        {
            _log.Error(SourceId, $"Quien escucha a la consola falló con {e.Control} ({e.Gesture}): {error.Message}", ErrorCodes.InternalError);
        }
    }
}
