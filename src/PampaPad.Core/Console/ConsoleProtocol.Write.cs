using System.Globalization;
using System.Text;

namespace PampaPad.Core;

/// <summary>
/// Los mensajes de la PC a la consola (04 §7, etapa 9a): de un registro a la línea, sin el fin de línea. Viajan sólo por cable y su
/// verdad es el firmware de la consola 0.1.5 (WireMessages.h), no DeviceProtocol.h.
/// </summary>
public static partial class ConsoleProtocol
{
    public const byte ScreenStateType = 0x40;
    public const byte KeyLabelsType = 0x41;
    public const byte ScreenTextType = 0x42;
    public const byte SettingsType = 0x44;
    public const byte StripStateType = 0x46;

    public const int MaxScreenTextLength = 28;
    public const int MaxSettingsKeys = 10;
    public const int StripLeds = 8;

    /// <summary>La carga más larga que viaja por radio en una trama: 35 cifrados menos el tipo interno (04 §7, decisión 46).</summary>
    public const int MaxConsolePayload = 34;

    private const int LabelLetters = 6;

    // El destino que exige la consola (ConsoleWire.h:28-29): con otro, descarta la línea.
    private const byte ToConsole = 0x11;

    public static string ScreenStateLine(ScreenFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var recorded = Clamp(frame.RecordedSec, 0xFFFFFF);
        var paused = Clamp(frame.PausedSec, ushort.MaxValue);
        var span = Clamp(frame.SpanSec, ushort.MaxValue);
        var active = frame.ActiveMask & KeyMask;
        var off = frame.OffMask & KeyMask;
        byte[] payload =
        [
            Byte(frame.Page, nameof(frame.Page)), (byte)frame.Recording, (byte)frame.Flags,
            (byte)recorded, (byte)(recorded >> 8), (byte)(recorded >> 16),
            (byte)paused, (byte)(paused >> 8),
            Byte(frame.SpanKey, nameof(frame.SpanKey)), Byte(frame.Canvas, nameof(frame.Canvas)), 0,
            (byte)active, (byte)(active >> 8),
            (byte)off, (byte)(off >> 8),
            Byte(frame.Alert, nameof(frame.Alert)), Byte(frame.LabelsVersion, nameof(frame.LabelsVersion)), (byte)frame.Flags2,
            (byte)span, (byte)(span >> 8),
        ];
        return ToConsoleLine(ScreenStateType, payload);
    }

    /// <summary>Dos teclas por mensaje: first es 1, 3, 5, 7, 9 u 11, y la consola descarta el resto (WireMessages.h:97).</summary>
    public static string KeyLabelsLine(int page, int first, KeyFace a, KeyFace b, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (first is < 1 or > 11 || first % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(first), first, "La primera tecla de un KeyLabels es 1, 3, 5, 7, 9 u 11.");
        }

        var payload = new byte[3 + (2 * (1 + (2 * LabelLetters)))];
        payload[0] = Byte(page, nameof(page));
        payload[1] = (byte)first;
        payload[2] = Byte(version, nameof(version));
        WriteFace(payload.AsSpan(3), a);
        WriteFace(payload.AsSpan(3 + 1 + (2 * LabelLetters)), b);
        return ToConsoleLine(KeyLabelsType, payload);
    }

    /// <summary>target: 0 la línea de escena, 1 el renglón chico, 2 a 5 el aviso. tone: 0 normal, 1 ámbar, 2 rojo. El texto va en ASCII y hasta 28.</summary>
    public static string ScreenTextLine(int target, int tone, int reference, string text)
    {
        var letters = Ascii(text, MaxScreenTextLength);
        byte[] payload = [Byte(target, nameof(target)), Byte(tone, nameof(tone)), Byte(reference, nameof(reference)), (byte)letters.Length, .. letters];
        return ToConsoleLine(ScreenTextType, payload);
    }

    /// <summary>
    /// Los pares van como vienen, en un solo mensaje (parte 1 de 1). La huella la calcula ConsoleSettingsWire: va aparte porque el vector
    /// del firmware lleva una de mentira, 0x1234.
    /// </summary>
    public static string SettingsLine(IReadOnlyList<(byte Key, ushort Value)> pairs, ushort fingerprint)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pairs.Count, MaxSettingsKeys);
        var payload = new byte[4 + (3 * pairs.Count)];
        payload[0] = (byte)fingerprint;
        payload[1] = (byte)(fingerprint >> 8);
        payload[2] = 0x11;
        payload[3] = (byte)pairs.Count;
        for (var i = 0; i < pairs.Count; i++)
        {
            payload[4 + (3 * i)] = pairs[i].Key;
            payload[5 + (3 * i)] = (byte)pairs[i].Value;
            payload[6 + (3 * i)] = (byte)(pairs[i].Value >> 8);
        }

        return ToConsoleLine(SettingsType, payload);
    }

    /// <summary>La tira desde el LED 0, el de la izquierda, hasta 8.</summary>
    public static string StripLine(IReadOnlyList<LedColor> leds)
    {
        ArgumentNullException.ThrowIfNull(leds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(leds.Count, StripLeds);
        var payload = new byte[2 + (4 * leds.Count)];
        payload[1] = (byte)leds.Count;
        for (var i = 0; i < leds.Count; i++)
        {
            payload[2 + (4 * i)] = leds[i].R;
            payload[3 + (4 * i)] = leds[i].G;
            payload[4 + (4 * i)] = leds[i].B;
            payload[5 + (4 * i)] = (byte)leds[i].Effect;
        }

        return ToConsoleLine(StripStateType, payload);
    }

    /// <summary>
    /// Sin acentos y sólo ASCII imprimible, 0x20 a 0x7E: la fuente de la consola no tiene más (04 §2). La ñ queda n; lo que no se
    /// descompone en una letra ASCII, como ¿, se cae.
    /// </summary>
    private static byte[] Ascii(string? text, int max)
    {
        var plain = new StringBuilder();
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (c is >= ' ' and <= '~')
            {
                plain.Append(c);
            }
        }

        return Encoding.ASCII.GetBytes(plain.ToString(0, Math.Min(plain.Length, max)));
    }

    private static void WriteFace(Span<byte> target, KeyFace face)
    {
        target[0] = (byte)(((byte)face.Role & 0x0F) | (face.Hold ? 1 << 6 : 0) | (face.Big ? 1 << 7 : 0));
        Ascii(face.Line1, LabelLetters).CopyTo(target[1..]);
        Ascii(face.Line2, LabelLetters).CopyTo(target[(1 + LabelLetters)..]);
    }

    /// <summary>Una carga de más de 34 no viaja por radio: es un error de programación y no de datos, por eso tira.</summary>
    public static string ToConsoleLine(byte type, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, MaxConsolePayload, nameof(payload));
        return string.Create(CultureInfo.InvariantCulture, $"#{type:X2}{FromPc:X2}{ToConsole:X2}{Convert.ToBase64String(payload)}");
    }

    private static int Clamp(int value, int max) => Math.Clamp(value, 0, max);

    private static byte Byte(int value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, byte.MaxValue, name);
        return (byte)value;
    }
}
