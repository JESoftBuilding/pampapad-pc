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

/// <summary>El estado de los controles de la consola: los botones en los bytes 0 y 1 y la perilla en el byte 7 (ConsoleWire.h:126-136).</summary>
public readonly record struct InputState(ushort Buttons, byte Knob = 0)
{
    /// <summary>Bits 0 a 11: teclas 1 a 12.</summary>
    public int Keys => Buttons & ConsoleProtocol.KeyMask;

    /// <summary>Bit 12: el clic de la perilla.</summary>
    public bool Push => (Buttons & ConsoleProtocol.PushBit) != 0;

    /// <summary>Bit 13: la tecla del módulo (KO).</summary>
    public bool ModuleKey => (Buttons & ConsoleProtocol.ModuleKeyBit) != 0;
}

/// <summary>
/// El saludo de la consola, 0x31 (ConsoleWire.h:147-160). Version es "0.1.5"; Energy, 0 activa, 1 atenuada y 2 pantalla apagada;
/// JustBooted sigue prendido hasta el primer ScreenState; SettingsFingerprint es 0 si nunca recibió ajustes.
/// </summary>
public readonly record struct Hello(byte Level, string Version, byte Capabilities, byte Energy, bool JustBooted, ushort SettingsFingerprint);

/// <summary>
/// El saludo del receptor, 0x32 (04 §7): la versión de su firmware y si escuchó a la consola en los últimos 3 s (bit 0 del estado).
/// </summary>
public readonly record struct ReceiverHello(string Version, bool HearsConsole);

/// <summary>Un comando para la tira de luces: los 5 bytes de LightCommand en DeviceProtocol.h (04 §7). Cmd va de 1 a 5.</summary>
public sealed record LightCommand(byte Cmd, byte R, byte G, byte B, byte Value);

/// <summary>El único códec de las cargas de DeviceProtocol.h, tal como viajan por el cable (04 §7).</summary>
public static partial class ConsoleProtocol
{
    public const byte InputStateType = 0x30;
    public const byte HelloType = 0x31;
    public const byte ReceiverHelloType = 0x32;
    public const byte LightCommandType = 0x10;
    public const int KeyCount = 12;
    public const int KeyMask = (1 << KeyCount) - 1;
    public const int PushBit = 1 << 12;
    public const int ModuleKeyBit = 1 << 13;
    public const int InputStateLength = 8;
    public const int HelloLength = 8;
    public const int ReceiverHelloLength = 5;

    /// <summary>El tope del firmware en los dos sentidos (ConsoleWire.h:32-34): '#', seis dígitos y base64. La válida más larga tiene 63.</summary>
    public const int MaxLineLength = 64;

    /// <summary>Lo que entra en 56 caracteres de base64, los grupos de 4 que caben en los 57 que quedan.</summary>
    public const int MaxDecodedLength = 42;

    private const int HeaderLength = 7;

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

        state = new InputState((ushort)(payload[0] | (payload[1] << 8)), payload[7]);
        return true;
    }

    /// <summary>Con la misma regla de crecimiento que el InputState: 8 bytes o más, y la cola (los candidatos de 04:447) no se mira.</summary>
    public static bool TryDecodeHello(ReadOnlySpan<byte> payload, out Hello hello)
    {
        if (payload.Length < HelloLength)
        {
            hello = default;
            return false;
        }

        hello = new Hello(
            payload[0],
            string.Create(CultureInfo.InvariantCulture, $"{payload[1]}.{payload[2]}.{payload[3]}"),
            payload[4],
            (byte)(payload[5] & 0x03),
            (payload[5] & 0x80) != 0,
            (ushort)(payload[6] | (payload[7] << 8)));
        return true;
    }

    /// <summary>Justo 5 bytes: el saludo del receptor no tiene regla de crecimiento en 04 §7, y otro largo es una línea mal formada.</summary>
    public static bool TryDecodeReceiverHello(ReadOnlySpan<byte> payload, out ReceiverHello hello)
    {
        if (payload.Length != ReceiverHelloLength)
        {
            hello = default;
            return false;
        }

        hello = new ReceiverHello(
            string.Create(CultureInfo.InvariantCulture, $"{payload[0]}.{payload[1]}.{payload[2]}"),
            (payload[3] & 0x01) != 0);
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
