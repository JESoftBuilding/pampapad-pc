using PampaPad.Core;
using Sdk = OpenRGB.NET;

namespace PampaPad.App;

/// <summary>
/// IOpenRgb sobre OpenRGB.NET 3.1.1, el cliente del servidor SDK de OpenRGB en 127.0.0.1:6742 (plan de la 1.6.0, decisión 3). Sólo
/// traduce: las reglas están en PcLights, en el núcleo y con prueba. Nunca llama a SaveMode, que graba en la memoria de la placa.
/// </summary>
internal sealed class OpenRgbClient : IOpenRgb
{
    private const int TimeoutMs = 1000;

    private Sdk.OpenRgbClient? _client;

    private Sdk.OpenRgbClient Client => _client ?? throw new InvalidOperationException("No hay conexión con OpenRGB");

    public void Connect()
    {
        Dispose();
        var client = new Sdk.OpenRgbClient("127.0.0.1", 6742, "PampaPad", autoConnect: false, timeoutMs: TimeoutMs);
        try
        {
            client.Connect();
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
    }

    public IReadOnlyList<OpenRgbDevice> Devices() =>
        [.. Client.GetAllControllerData().Select(device => new OpenRgbDevice(
            device.Index,
            device.Name,
            [.. device.Zones.Select(zone => new OpenRgbZone(zone.Name, (int)zone.LedCount))],
            [.. device.Modes.Select(mode => mode.Name)]))];

    public void SetMode(int device, int mode) => Client.UpdateMode(device, mode);

    public void SetLeds(int device, IReadOnlyList<Rgb> colors) =>
        Client.UpdateLeds(device, colors.Select(color => new Sdk.Color(color.R, color.G, color.B)).ToArray());

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
