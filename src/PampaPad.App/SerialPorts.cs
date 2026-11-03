using System.IO.Ports;
using System.Security;
using Microsoft.Win32;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>Abre el puerto serie de la consola y le entrega el flujo al núcleo, y lista los que hay. Sin reglas (01 §3.2).</summary>
internal static class SerialPorts
{
    // El cable va a 115200 baudios, 8N1 (04 §9; monitor_speed del firmware).
    private const int BaudRate = 115200;

    // Depende de E0-5. Con las dos líneas sin activar, el circuito de reinicio automático de la placa no la reinicia.
    private const bool Dtr = false;
    private const bool Rts = false;

    // Una escritura trabada no puede colgar el paso lights: falla en medio segundo con console_disconnected (plan de la 8b, decisión 8).
    // Rige sólo en el Write sincrónico, que es el que usa SerialConsole: el WriteAsync de SerialStream lo pone en infinito.
    private const int WriteTimeoutMs = 500;

    /// <summary>La función que SerialConsole llama en cada intento. Si el puerto no abre, tira, y SerialConsole reintenta.</summary>
    public static Func<CancellationToken, Task<Stream>> Opener(string portName) =>
        ct => Task.Run(() => Open(portName), ct);

    /// <summary>
    /// Los puertos que están, con lo que dice de cada uno el registro de Windows (plan de la 8b, decisión 7): sin WMI ni paquetes, y sin
    /// abrir ninguno, porque abrir un puerto puede reiniciar la placa del otro lado. Nunca tira: devuelve lo que pudo leer.
    /// </summary>
    public static IReadOnlyList<ConsolePortView> List()
    {
        string[] present;
        try
        {
            present = SerialPort.GetPortNames();
        }
        catch (Exception e) when (e is IOException or SecurityException or UnauthorizedAccessException)
        {
            return [];
        }

        return SerialPortList.Build(present, Entries(present));
    }

    // Enum\<bus>\<dispositivo>\<instancia>\Device Parameters\PortName. Se corta al encontrar todos los que están: el resto del árbol
    // son dispositivos desenchufados o sin puerto.
    private static List<SerialPortEntry> Entries(string[] wanted)
    {
        var found = new List<SerialPortEntry>();
        var missing = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        using RegistryKey? root = OpenQuietly(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Enum");
        foreach (string bus in Names(root))
        {
            using RegistryKey? busKey = OpenQuietly(root, bus);
            foreach (string device in Names(busKey))
            {
                using RegistryKey? deviceKey = OpenQuietly(busKey, device);
                foreach (string instance in Names(deviceKey))
                {
                    if (missing.Count == 0)
                    {
                        return found;
                    }

                    using RegistryKey? instanceKey = OpenQuietly(deviceKey, instance);
                    if (Read(instanceKey, missing, $@"{bus}\{device}\{instance}") is { } entry)
                    {
                        missing.Remove(entry.Port);
                        found.Add(entry);
                    }
                }
            }
        }

        return found;
    }

    private static SerialPortEntry? Read(RegistryKey? instance, HashSet<string> wanted, string path)
    {
        try
        {
            using RegistryKey? parameters = instance?.OpenSubKey("Device Parameters");
            return parameters?.GetValue("PortName") is string port && wanted.Contains(port)
                ? new SerialPortEntry(path, port, instance!.GetValue("FriendlyName") as string, instance.GetValue("DeviceDesc") as string)
                : null;
        }
        catch (Exception e) when (e is IOException or SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Una clave sin permiso o que se borró mientras se recorre (se desenchufó algo) se saltea.
    private static RegistryKey? OpenQuietly(RegistryKey? parent, string name)
    {
        try
        {
            return parent?.OpenSubKey(name);
        }
        catch (Exception e) when (e is IOException or SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string[] Names(RegistryKey? key)
    {
        try
        {
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception e) when (e is IOException or SecurityException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Stream Open(string portName)
    {
        var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = Dtr,
            RtsEnable = Rts,
            WriteTimeout = WriteTimeoutMs,
        };
        try
        {
            port.Open();

            // Cerrar este flujo cierra el puerto: es lo mismo que hace SerialPort.Close por dentro.
            return port.BaseStream;
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }
}
