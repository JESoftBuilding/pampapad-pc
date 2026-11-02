using System.Globalization;

namespace PampaPad.Core;

/// <summary>Qué es una línea que llegó por el cable de la consola (04 §7).</summary>
public enum ConsoleLineKind
{
    /// <summary>No empieza con '#': texto de arranque o de depuración. Se ignora.</summary>
    Text,

    /// <summary>Empieza con '#' y no cumple el formato. Se ignora y se cuenta.</summary>
    Malformed,

    /// <summary>Un mensaje bien formado. Si el tipo se conoce o no lo decide quien lo recibe.</summary>
    Message,
}

/// <summary>Un mensaje del cable ya partido: tipo, origen, destino y la carga en claro.</summary>
public readonly record struct ConsoleMessage(byte Type, byte Source, byte Target, byte[] Payload);

/// <summary>El estado de los controles de la consola. De sus 8 bytes, esta etapa usa los dos primeros.</summary>
public readonly record struct InputState(ushort Buttons)
{
    /// <summary>Bits 0 a 11: teclas 1 a 12. Los bits 12 y 13, perilla y tecla del módulo, entran en la etapa 9.</summary>
    public int Keys => Buttons & ConsoleProtocol.KeyMask;
}

/// <summary>Un comando para la tira de luces: los 5 bytes de LightCommand en DeviceProtocol.h (04 §7). Cmd va de 1 a 5.</summary>
public sealed record LightCommand(byte Cmd, byte R, byte G, byte B, byte Value);

/// <summary>El único códec de las cargas de DeviceProtocol.h, tal como viajan por el cable (04 §7).</summary>
public static class ConsoleProtocol
{
    public const byte InputStateType = 0x30;
    public const byte LightCommandType = 0x10;
    public const int KeyCount = 12;
    public const int KeyMask = (1 << KeyCount) - 1;
    public const int InputStateLength = 8;

    /// <summary>'#', seis dígitos hexadecimales y hasta 48 caracteres de base64, que son 35 bytes de carga.</summary>
    public const int MaxLineLength = 55;

    private const int HeaderLength = 7;
    private const int MaxDecodedLength = 36;

    // El origen es el lado de la PC (Receiver, informativo) y el destino, la tira (Lights); la consola reenvía por el destino (04 §7).
    private const byte FromPc = 0x10;
    private const byte ToLights = 0x13;

    public static ConsoleLineKind Parse(ReadOnlySpan<char> line, out ConsoleMessage message)
    {
        message = new ConsoleMessage(0, 0, 0, []);
        if (line.IsEmpty || line[0] != '#')
        {
            return ConsoleLineKind.Text;
        }

        if (line.Length < HeaderLength || line.Length > MaxLineLength)
        {
            return ConsoleLineKind.Malformed;
        }

        if (!TryHex(line.Slice(1, 2), out var type)
            || !TryHex(line.Slice(3, 2), out var source)
            || !TryHex(line.Slice(5, 2), out var target))
        {
            return ConsoleLineKind.Malformed;
        }

        Span<byte> decoded = stackalloc byte[MaxDecodedLength];
        if (!Convert.TryFromBase64Chars(line[HeaderLength..], decoded, out var written))
        {
            return ConsoleLineKind.Malformed;
        }

        message = new ConsoleMessage(type, source, target, decoded[..written].ToArray());
        return ConsoleLineKind.Message;
    }

    /// <summary>La regla para crecer sin recargar firmware: vale un largo mayor o igual a 8 y no se mira la cola.</summary>
    public static bool TryDecodeInputState(ReadOnlySpan<byte> payload, out InputState state)
    {
        if (payload.Length < InputStateLength)
        {
            state = default;
            return false;
        }

        state = new InputState((ushort)(payload[0] | (payload[1] << 8)));
        return true;
    }

    /// <summary>La línea de un comando de luces, sin el fin de línea: #101013 y la carga en base64 (04 §7).</summary>
    public static string LightLine(LightCommand command)
    {
        ReadOnlySpan<byte> payload = [command.Cmd, command.R, command.G, command.B, command.Value];
        return string.Create(CultureInfo.InvariantCulture, $"#{LightCommandType:X2}{FromPc:X2}{ToLights:X2}{Convert.ToBase64String(payload)}");
    }

    private static bool TryHex(ReadOnlySpan<char> digits, out byte value) =>
        byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
