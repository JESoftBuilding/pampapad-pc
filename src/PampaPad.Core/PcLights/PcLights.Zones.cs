using System.Collections.Concurrent;

namespace PampaPad.Core;

// Las zonas para la tarjeta Luces de la PC (plan de la 1.6.0, RGB-4), aparte por el tope de 300 líneas (01 §1). Van por la misma cola
// que las escrituras: un solo hilo le habla a OpenRGB.
public sealed partial class PcLights
{
    private readonly ConcurrentQueue<TaskCompletionSource<IReadOnlyList<PcLightZone>?>> _zoneRequests = new();

    /// <summary>Las zonas que da OpenRGB, por la misma cola que las escrituras. null si OpenRGB no responde (el motivo va al registro).</summary>
    public Task<IReadOnlyList<PcLightZone>?> ZonesAsync(CancellationToken ct)
    {
        var request = new TaskCompletionSource<IReadOnlyList<PcLightZone>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _zoneRequests.Enqueue(request);
        Wake(cutRetry: true);
        return request.Task.WaitAsync(ct);
    }

    private void ServeZones()
    {
        if (_zoneRequests.IsEmpty)
        {
            return;
        }

        PcLightZone[] zones = [.. _openRgb.Devices().SelectMany(d => d.Zones.Select(z => new PcLightZone(d.Name, z.Name)))];
        while (_zoneRequests.TryDequeue(out var request))
        {
            request.TrySetResult(zones);
        }
    }

    private void FailZones()
    {
        while (_zoneRequests.TryDequeue(out var request))
        {
            request.TrySetResult(null);
        }
    }
}
