using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>PcLights con su local.json en una carpeta temporal, el doble de OpenRGB y reloj falso: ninguna espera usa el reloj real.</summary>
internal sealed class PcLightsRig : IAsyncDisposable
{
    public const string Chosen = $$"""{ "pcLights": { "device": "{{FakeOpenRgb.Board}}", "zone": "{{FakeOpenRgb.Header}}" } }""";

    private readonly TempFolder _temp = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly WallClock _wall;
    private Task _running = Task.CompletedTask;

    /// <summary>localJson es lo que hay en local.json al arrancar; null, sin archivo.</summary>
    public PcLightsRig(string? localJson, int failConnects = 0)
    {
        Path = _temp.Combine("local.json");
        if (localJson is not null)
        {
            File.WriteAllText(Path, localJson);
        }

        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Settings = new LocalSettings(Path, Log);
        Settings.Load();
        OpenRgb.FailConnects = failConnects;
        _wall = new WallClock(Clock);
        Lights = new PcLights(OpenRgb, Settings, State, Log, _wall);
    }

    /// <summary>Cuánto corrigió Windows la hora para atrás: retrocede la hora de pared, no el reloj monotónico ni los temporizadores.</summary>
    public TimeSpan WallClockBack
    {
        get => _wall.Back;
        set => _wall.Back = value;
    }

    public string Path { get; }

    public FakeTimeProvider Clock { get; } = new();

    public FakeOpenRgb OpenRgb { get; } = new();

    public StateStore State { get; } = new();

    public Log Log { get; }

    public LocalSettings Settings { get; }

    public PcLights Lights { get; }

    public PcLightsLinkState Link => State.Current.PcLights.State;

    public PcLightsRig Start()
    {
        _running = Lights.RunAsync(_stop.Token);
        return this;
    }

    public Task UntilAsync(Func<bool> condition, string what) => Eventually.UntilAsync(condition, what);

    /// <summary>Deja correr al trabajador sin mover el reloj: lo que tenga que pasar sin esperar, ya pasó.</summary>
    public static Task SettleAsync() => Task.Delay(100);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running;
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
        _stop.Dispose();
    }

    /// <summary>El reloj falso con la hora de pared corrida para atrás, como cuando Windows corrige la hora. Lo demás es el falso.</summary>
    private sealed class WallClock(FakeTimeProvider inner) : TimeProvider
    {
        public TimeSpan Back { get; set; }

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() - Back;

        public override long GetTimestamp() => inner.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }
}
