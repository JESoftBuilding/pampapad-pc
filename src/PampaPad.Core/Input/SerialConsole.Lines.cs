namespace PampaPad.Core;

/// <summary>La mitad de SerialConsole que parte lo que llega en líneas y saca los eventos crudos.</summary>
public sealed partial class SerialConsole
{
    // Las líneas de texto van al registro, y la de arranque y la de estado pasan de 64 (src/consola/main.cpp:150, :241): el lugar es
    // para ellas. Una con '#' más larga que 64 sigue siendo mal formada; una de texto más larga se anota cortada.
    private readonly char[] _line = new char[MaxTextLength];
    private int _lineLength;
    private int _keys;
    private bool _push;
    private bool _moduleKey;
    private byte? _knobBase;
    private long _malformed;
    private long _unknown;

    /// <summary>Líneas con '#' que no cumplían el formato, desde que arrancó el programa.</summary>
    public long MalformedLines => Interlocked.Read(ref _malformed);

    /// <summary>Mensajes bien formados de un tipo que esta versión no conoce, desde que arrancó el programa.</summary>
    public long UnknownMessages => Interlocked.Read(ref _unknown);

    private void ResetLine() => _lineLength = 0;

    /// <summary>Hubo un Hello con "recién arrancó". Lo escucha el emisor, que vuelve a mandar todo (plan de la 9a, decisión 5).</summary>
    public event System.Action? ConsoleRebooted;

    /// <summary>
    /// Sin enlace no se sabe qué está apretado: al volver, lo que lo esté entra como nuevo. La perilla tampoco: la primera lectura
    /// del enlace nuevo es la base (plan de la 9a, decisión 8).
    /// </summary>
    private void ForgetKeys()
    {
        _keys = 0;
        _push = false;
        _moduleKey = false;
        _knobBase = null;
    }

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
            LogText(line);
            return;
        }

        if (kind == ConsoleLineKind.Malformed)
        {
            CountMalformed();
            return;
        }

        if (message.Type == ConsoleProtocol.HelloType)
        {
            HandleHello(message.Payload);
            return;
        }

        if (message.Type == ConsoleProtocol.ReceiverHelloType)
        {
            HandleReceiverHello(message.Payload);
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
        foreach (var e in Gestures.FromKeys(SourceId, _keys, input.Keys))
        {
            Raise(e);
        }

        _keys = input.Keys;
        RaiseLevel(Controls.Knob, _push, input.Push);
        RaiseLevel(Controls.ModuleKey, _moduleKey, input.ModuleKey);
        _push = input.Push;
        _moduleKey = input.ModuleKey;

        // La perilla cuenta detentes módulo 256: la diferencia como int8 da el signo bien en el cruce (de 250 a 3 es +9). Las tres
        // copias de cada cambio (WirePacer.h:12-17) dan diferencia 0 y no giran.
        var steps = _knobBase is { } knobBase ? (sbyte)(byte)(input.Knob - knobBase) : 0;
        _knobBase = input.Knob;
        if (steps != 0)
        {
            Raise(new InputEvent(SourceId, Controls.Knob, Gesture.Turn, steps));
        }
    }

    /// <summary>PUSH y la tecla del módulo van como las teclas: Release al soltar y Press en cada estado mientras siguen apretadas.</summary>
    private void RaiseLevel(string control, bool was, bool now)
    {
        if (was && !now)
        {
            Raise(new InputEvent(SourceId, control, Gesture.Release));
        }

        if (now)
        {
            Raise(new InputEvent(SourceId, control, Gesture.Press));
        }
    }

    private void HandleHello(byte[] payload)
    {
        if (!ConsoleProtocol.TryDecodeHello(payload, out var hello))
        {
            CountMalformed();
            return;
        }

        var previous = _state.Current.Console.Firmware;
        _state.Update(s => s with
        {
            Console = s.Console with
            {
                Firmware = hello.Version,
                Capabilities = hello.Capabilities,
                Energy = hello.Energy,
                SettingsFingerprint = hello.SettingsFingerprint,
            },
        });
        if (previous != hello.Version)
        {
            _log.Info(SourceId, $"La consola saluda: firmware {hello.Version}, nivel {hello.Level}, capacidades 0x{hello.Capabilities:X2}.");
        }

        if (!hello.JustBooted)
        {
            return;
        }

        // La placa se reinició y su perilla volvió a 0: con la base vieja la próxima lectura saltaría páginas (plan, "Lo que puede morder").
        _knobBase = null;
        try
        {
            ConsoleRebooted?.Invoke();
        }
        catch (Exception error)
        {
            _log.Error(SourceId, $"Quien escucha el reinicio de la consola falló: {error.Message}", ErrorCodes.InternalError);
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
