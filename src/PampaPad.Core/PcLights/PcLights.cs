using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PampaPad.Core;

/// <summary>Una zona que da OpenRGB, para elegirla en Ajustes. Label es "dispositivo · zona".</summary>
public sealed record PcLightZone(string Device, string Zone)
{
    public string Label => $"{Device} · {Zone}";
}

/// <summary>
/// Las luces de la PC siguen a las de la sala (plan de la 1.6.0, decisiones 2 a 9). Cada comando cambia el modelo en el acto y deja una
/// escritura pendiente; un solo trabajador escribe el último estado, como mucho cada 50 ms, y llama a IOpenRgb de a una vez. Sin
/// conexión, o si una llamada falla, queda unreachable y reintenta cada 5 s. No lee ni corrige lo que haga otro programa: escribe sólo
/// cuando hay algo pendiente, un comando o el estado guardado al arrancar. La zona se lee de local.json cada vez, así que cambiarla en
/// Ajustes vale sin reiniciar.
/// </summary>
public sealed class PcLights
{
    public static readonly TimeSpan WriteEvery = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    private readonly IOpenRgb _openRgb;
    private readonly LocalSettings _settings;
    private readonly StateStore _state;
    private readonly Log _log;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    // Un solo aviso pendiente alcanza: el trabajador lee el último estado, no cada comando.
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly ConcurrentQueue<TaskCompletionSource<IReadOnlyList<PcLightZone>?>> _zoneRequests = new();
    private PcLightState _model;
    private bool _pending;
    private PcLightState _saved;

    public PcLights(IOpenRgb openRgb, LocalSettings settings, StateStore state, Log log, TimeProvider clock)
    {
        _openRgb = openRgb;
        _settings = settings;
        _state = state;
        _log = log;
        _clock = clock;

        // Al arrancar, el último estado guardado es lo pendiente: se pone una vez, cuando OpenRGB responda (decisión 6). Sin él, nada.
        PcLightState? last = settings.Current.PcLights?.Last;
        _model = last ?? PcLightState.Initial;
        _saved = _model;
        _pending = last is not null;
    }

    public PcLightState Model
    {
        get
        {
            lock (_gate)
            {
                return _model;
            }
        }
    }

    /// <summary>La copia de un LightCommand del paso lights o de la perilla. No espera ni tira: el trabajador escribe después.</summary>
    public void Apply(LightCommand command)
    {
        lock (_gate)
        {
            _model = _model.Apply(command);
            _pending = true;
        }

        _wake.Writer.TryWrite(true);
    }

    /// <summary>Las zonas que da OpenRGB, por la misma cola que las escrituras. null si OpenRGB no responde (el motivo va al registro).</summary>
    public Task<IReadOnlyList<PcLightZone>?> ZonesAsync(CancellationToken ct)
    {
        var request = new TaskCompletionSource<IReadOnlyList<PcLightZone>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _zoneRequests.Enqueue(request);
        _wake.Writer.TryWrite(true);
        return request.Task.WaitAsync(ct);
    }

    /// <summary>El trabajador y el guardado cada 2 s, hasta que se cancele. Al salir falta SaveAsync, que lo llama la composición.</summary>
    public Task RunAsync(CancellationToken ct) => Task.WhenAll(Task.Run(() => WriteLoopAsync(ct)), Task.Run(() => SaveLoopAsync(ct)));

    /// <summary>Guarda pcLights.last si cambió desde el último guardado. Toca sólo Last: device, zone y lo demás quedan como están.</summary>
    public async Task SaveAsync()
    {
        PcLightState now = Model;
        if (now == _saved || _settings.Current.PcLights is not { Chosen: true })
        {
            return;
        }

        Result result = await _settings.SaveAsync(
            data => data.PcLights is { } pc ? data with { PcLights = pc with { Last = now } } : data,
            CancellationToken.None).ConfigureAwait(false);
        if (result.Ok)
        {
            _saved = now;
        }
    }

    private async Task SaveLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SaveEvery, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await SaveAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        IReadOnlyList<OpenRgbDevice>? devices = null;
        DateTimeOffset lastWrite = DateTimeOffset.MinValue;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                PcLightsSettings? chosen = _settings.Current.PcLights is { Chosen: true } pc ? pc : null;
                if (chosen is null && _zoneRequests.IsEmpty)
                {
                    Publish(PcLightsLink.Off, null);
                    await _wake.Reader.ReadAsync(ct).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    if (devices is null)
                    {
                        _openRgb.Connect();
                        devices = _openRgb.Devices();
                    }

                    ServeZones();
                    if (chosen is not null)
                    {
                        lastWrite = await WriteAsync(chosen, devices, lastWrite, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Un solo ciclo de reintento: lo que llegue mientras tanto queda pendiente y sale en el próximo intento.
                    devices = null;
                    FailZones();
                    if (chosen is null)
                    {
                        // Sólo se pedían las zonas, sin zona elegida: el estado sigue off y no hay nada que reintentar.
                        _log.Warning("pclights", $"Luces de la PC: no se pudieron leer las zonas de OpenRGB: {error.Message}");
                        continue;
                    }

                    Publish(new PcLightsLink(PcLightsLinkState.Unreachable, chosen?.Label), error.Message);
                    await Task.Delay(RetryEvery, _clock, ct).ConfigureAwait(false);
                    continue;
                }

                await _wake.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Busca la zona por nombre y, si hay algo pendiente, escribe el último estado respetando los 50 ms. Connected se publica recién
    // cuando la zona está y la escritura, si la hubo, anduvo: si no, un reintento fallido dejaría dos líneas en el registro.
    private async Task<DateTimeOffset> WriteAsync(PcLightsSettings chosen, IReadOnlyList<OpenRgbDevice> devices, DateTimeOffset lastWrite, CancellationToken ct)
    {
        OpenRgbDevice device = devices.FirstOrDefault(d => d.Name == chosen.Device)
            ?? throw new InvalidOperationException($"OpenRGB no tiene el dispositivo \"{chosen.Device}\"");
        int zone = device.Zones.ToList().FindIndex(z => z.Name == chosen.Zone);
        if (zone < 0)
        {
            throw new InvalidOperationException($"OpenRGB no tiene la zona \"{chosen.Zone}\" en \"{chosen.Device}\"");
        }

        var connected = new PcLightsLink(PcLightsLinkState.Connected, chosen.Label);
        bool pending;
        lock (_gate)
        {
            pending = _pending;
        }

        if (!pending)
        {
            Publish(connected, null);
            return lastWrite;
        }

        TimeSpan wait = lastWrite + WriteEvery - _clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _clock, ct).ConfigureAwait(false);
        }

        PcLightState state;
        lock (_gate)
        {
            state = _model;
            _pending = false;
        }

        try
        {
            PcLightsFrame frame = state.Frame();
            _openRgb.SetMode(device.Index, IndexOf(device, frame.Mode));
            if (frame.ZoneColor is { } color)
            {
                // Los LEDs del dispositivo van zona tras zona: la elegida en el color y el resto en negro (decisión 5).
                var leds = new Rgb[device.Zones.Sum(z => z.LedCount)];
                Array.Fill(leds, color, device.Zones.Take(zone).Sum(z => z.LedCount), device.Zones[zone].LedCount);
                _openRgb.SetLeds(device.Index, leds);
            }
        }
        catch
        {
            lock (_gate)
            {
                _pending = true;
            }

            throw;
        }

        Publish(connected, null);
        return _clock.GetUtcNow();
    }

    private static int IndexOf(OpenRgbDevice device, string mode)
    {
        int index = device.Modes.ToList().IndexOf(mode);
        return index >= 0 ? index : throw new InvalidOperationException($"\"{device.Name}\" no tiene el modo {mode}");
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

    // Una línea en el registro al conectarse y otra al perderlo, con el motivo; no una por comando ni por reintento (decisión 8).
    private void Publish(PcLightsLink link, string? reason)
    {
        PcLightsLink previous = _state.Current.PcLights;
        if (link == previous)
        {
            return;
        }

        _state.Update(s => s with { PcLights = link });
        if (link.State == PcLightsLinkState.Connected)
        {
            _log.Info("pclights", $"Luces de la PC: conectadas a OpenRGB, zona {link.Zone}");
        }
        else if (link.State == PcLightsLinkState.Unreachable && previous.State != PcLightsLinkState.Unreachable)
        {
            _log.Warning("pclights", $"Luces de la PC: OpenRGB no responde: {reason}");
        }
    }
}
