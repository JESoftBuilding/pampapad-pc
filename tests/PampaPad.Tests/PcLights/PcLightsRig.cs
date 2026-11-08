using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>PcLights con su local.json en una carpeta temporal, el doble de OpenRGB y reloj falso: ninguna espera usa el reloj real.</summary>
internal sealed class PcLightsRig : IAsyncDisposable
{
    public const string Chosen = $$"""{ "pcLights": { "device": "{{FakeOpenRgb.Board}}", "zone": "{{FakeOpenRgb.Header}}" } }""";

    private readonly TempFolder _temp = new();
    private readonly CancellationTokenSource _stop = new();
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
        Lights = new PcLights(OpenRgb, Settings, State, Log, Clock);
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
}
