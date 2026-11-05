using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// El bloque console de config.json como viaja en Settings 0x44: siempre las claves 1 a 8, con los valores por defecto de 02 §4.10 donde
/// falten, y su huella (plan de la 9a, decisión 7). La consola devuelve la huella en el Hello cuando los aceptó.
/// </summary>
public static class ConsoleSettingsWire
{
    // La clave 7, no_pc: bit 0 la tira y bit 1 el zumbador; por defecto sólo la tira (04 §6, consola.py:24).
    public static IReadOnlyList<(byte Key, ushort Value)> Pairs(ConsoleSettings? settings)
    {
        var noPc = settings?.Alerts?.GetValueOrDefault(AlertTexts.NoPc);
        var noPcChannels = (noPc?.Light ?? true ? 1 : 0) | (noPc?.Sound ?? false ? 2 : 0);
        return
        [
            (1, Word(settings?.Screen?.Brightness ?? 80)),
            (2, Word(settings?.Screen?.DimBrightness ?? 15)),
            (3, Word(settings?.Screen?.DimAfterSec ?? 60)),
            (4, Word(settings?.Screen?.OffAfterSec ?? 300)),
            (5, Word(settings?.SleepAfterSec ?? 0)),
            (6, Word(settings?.HoldMs ?? Gestures.DefaultHoldMs)),
            (7, Word(noPcChannels)),
            (8, Word(StripBrightness(settings) ?? 25)),
        ];
    }

    /// <summary>CRC-16/XMODEM de los pares en orden (el crc_hqx de Python con 0, consola.py:68), y 0 pasa a 1: 0 es "nunca recibió ajustes".</summary>
    public static ushort Fingerprint(IReadOnlyList<(byte Key, ushort Value)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var bytes = new byte[3 * pairs.Count];
        for (var i = 0; i < pairs.Count; i++)
        {
            bytes[3 * i] = pairs[i].Key;
            bytes[(3 * i) + 1] = (byte)pairs[i].Value;
            bytes[(3 * i) + 2] = (byte)(pairs[i].Value >> 8);
        }

        var crc = Crc16Xmodem(bytes);
        return crc == 0 ? (ushort)1 : crc;
    }

    /// <summary>La línea entera de los ajustes, con su huella.</summary>
    public static string Line(ConsoleSettings? settings)
    {
        var pairs = Pairs(settings);
        return ConsoleProtocol.SettingsLine(pairs, Fingerprint(pairs));
    }

    /// <summary>Polinomio 0x1021, arranque 0, sin reflejar.</summary>
    public static ushort Crc16Xmodem(ReadOnlySpan<byte> bytes)
    {
        var crc = 0;
        foreach (var value in bytes)
        {
            crc ^= value << 8;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
            }
        }

        return (ushort)crc;
    }

    // strip todavía no tiene record propio: cae en Extra (ConsoleSettings.cs). Sólo vale un número entero.
    private static int? StripBrightness(ConsoleSettings? settings) =>
        settings?.Extra.GetValueOrDefault("strip") is { ValueKind: JsonValueKind.Object } strip
        && strip.TryGetProperty("brightness", out var brightness)
        && brightness.ValueKind == JsonValueKind.Number
        && brightness.TryGetInt32(out var value)
            ? value
            : null;

    private static ushort Word(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
}
