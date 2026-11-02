using System.Globalization;
using System.Text.RegularExpressions;

namespace PampaPad.Core;

/// <summary>Lo que el registro de Windows dice de un dispositivo con puerto serie: su clave bajo Enum, el puerto y los dos nombres.</summary>
public sealed record SerialPortEntry(string InstancePath, string Port, string? FriendlyName, string? DeviceDescription);

/// <summary>
/// Arma la lista de Conexión con la consola (05 §4.5) con lo que lee la cáscara: los puertos que están y lo que el registro dice de cada
/// uno. El registro guarda también los dispositivos que se desenchufaron: por eso manda la lista de los que están (plan de la 8b, decisión 7).
/// </summary>
public static partial class SerialPortList
{
    public static IReadOnlyList<ConsolePortView> Build(IEnumerable<string> present, IEnumerable<SerialPortEntry> registry)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(registry);
        var entries = new Dictionary<string, SerialPortEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (SerialPortEntry entry in registry)
        {
            entries.TryAdd(entry.Port, entry);
        }

        return present
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(port => entries.TryGetValue(port, out SerialPortEntry? entry)
                ? new ConsolePortView(port, Description(entry), UsbId(entry.InstancePath))
                : new ConsolePortView(port, "", null))
            .OrderBy(view => Number(view.Port))
            .ThenBy(view => view.Port, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // "USB-Enhanced-SERIAL CH343 (COM20)" sin el puerto, que la tarjeta ya muestra. Sin nombre amigable, DeviceDesc trae el texto
    // después del último punto y coma: "@oem15.inf,%ch343ser.devicedesc%;USB-Enhanced-SERIAL CH343".
    private static string Description(SerialPortEntry entry)
    {
        string suffix = $" ({entry.Port})";
        if (!string.IsNullOrWhiteSpace(entry.FriendlyName))
        {
            return entry.FriendlyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? entry.FriendlyName[..^suffix.Length] : entry.FriendlyName;
        }

        return entry.DeviceDescription?[(entry.DeviceDescription.LastIndexOf(';') + 1)..] ?? "";
    }

    // Sólo bajo USB\: la clave del Bluetooth también trae VID&, y no es un identificador USB.
    private static string? UsbId(string instancePath)
    {
        Match match = UsbIdPattern().Match(instancePath);
        return match.Success ? $"{match.Groups[1].Value}:{match.Groups[2].Value}".ToUpperInvariant() : null;
    }

    private static int Number(string port) =>
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(port.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;

    [GeneratedRegex(@"^USB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsbIdPattern();
}
