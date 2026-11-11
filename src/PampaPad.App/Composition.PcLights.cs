using PampaPad.Core;

namespace PampaPad.App;

// Las luces de la PC por OpenRGB (plan de la 1.6.0), aparte por el tope de 300 líneas (01 §1).
internal sealed partial class Composition
{
    private PcLights? _pcLights;
    private OpenRgbClient? _openRgb;

    // Lo corre StartAsync: la cola de escrituras y el guardado cada 2 s. Sin zona en local.json, ninguno toca OpenRGB.
    private void StartPcLights() => _loops.Add(_pcLights!.RunAsync(_life.Token));

    // Al salir, después de los bucles: el último estado a local.json, una vez, y recién después se suelta la conexión.
    private async Task DisposePcLightsAsync()
    {
        if (_pcLights is not null)
        {
            await _pcLights.SaveAsync().ConfigureAwait(false);
        }

        _openRgb?.Dispose();
    }
}
