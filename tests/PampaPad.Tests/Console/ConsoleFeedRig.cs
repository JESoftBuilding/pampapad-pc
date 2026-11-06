using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Un ConsoleFeed con reloj falso, una proyección de mentira (Frame) y un envío que anota cada línea con la hora del reloj. Las vueltas
/// se corren a mano con Round, que adelanta el reloj de a 1 ms sólo mientras la vuelta espera su pausa de 10 ms.
/// </summary>
internal sealed class ConsoleFeedRig : IAsyncDisposable
{
    public static readonly TimeSpan Tick = ConsoleFeed.RoundPeriod;

    private readonly TempFolder _temp = new();
    private readonly List<(DateTimeOffset At, string Line)> _sent = [];

    public ConsoleFeedRig()
    {
        Log = new Log(_temp.Combine("log"), Clock);
        Store.Update(s => s with { Console = new ConsoleLink(ConsoleLinkState.Waiting, "COM7") });
        Feed = new ConsoleFeed(
            Store,
            () =>
            {
                OnConfig?.Invoke();
                return Config;
            },
            (_, _, _) => Frame,
            Send,
            Log,
            Clock);
    }

    public FakeTimeProvider Clock { get; } = new();

    public StateStore Store { get; } = new();

    public Log Log { get; }

    public ConsoleFeed Feed { get; }

    public Config Config { get; set; } = new();

    public ConsoleFrame Frame { get; set; } = Example();

    /// <summary>Corre cada vez que la vuelta lee la configuración: ya tomó su foto del estado, así que sirve para meter algo en el medio.</summary>
    public System.Action? OnConfig { get; set; }

    /// <summary>Lo que devuelve cada envío. Por defecto, que salió bien.</summary>
    public Func<string, Task<Result>> Answer { get; set; } = _ => Task.FromResult(Result.Success);

    /// <summary>La huella de los ajustes de config.json, la que la consola tiene que devolver para quedar al día.</summary>
    public int OwnFingerprint => ConsoleSettingsWire.Fingerprint(ConsoleSettingsWire.Pairs(Config.Console));

    public IReadOnlyList<(DateTimeOffset At, string Line)> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public static ConsoleFrame Example(int page = 0) => new(
        new ScreenFrame(page, ConsoleRecording.Stopped),
        [.. Enumerable.Range(1, 12).Select(k => new KeyFace(KeyRole.Normal, $"P{page}", $"K{k}"))],
        new ConsoleText("CAMARA"),
        new ConsoleText(""),
        [.. Enumerable.Repeat(LedColor.Off, 8)]);

    public static byte Type(string line) => Convert.ToByte(line.Substring(1, 2), 16);

    public static byte[] Payload(string line)
    {
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(line, out var message));
        return message.Payload;
    }

    /// <summary>La consola conectada; con fingerprint, como si ya hubiera saludado con esa huella.</summary>
    public void Connect(int? fingerprint = null) =>
        Store.Update(s => s with { Console = new ConsoleLink(ConsoleLinkState.Connected, "COM7") { SettingsFingerprint = fingerprint } });

    public void Disconnect() => Store.Update(s => s with { Console = s.Console.Moved(ConsoleLinkState.Waiting) });

    public void Hello(int fingerprint) => Store.Update(s => s with { Console = s.Console with { SettingsFingerprint = fingerprint } });

    public void Clear()
    {
        lock (_sent)
        {
            _sent.Clear();
        }
    }

    /// <summary>Una vuelta del emisor. El tope es sólo para que una prueba rota no cuelgue el CI.</summary>
    public async Task Round()
    {
        var round = Feed.RoundAsync(CancellationToken.None);
        for (var spins = 0; !round.IsCompleted; spins++)
        {
            Assert.True(spins < 5000, "La vuelta del emisor no termina.");
            Clock.Advance(TimeSpan.FromMilliseconds(1));
            await Task.Yield();
        }

        await round;
    }

    /// <summary>Corre las vueltas de ese lapso, una cada Tick, como el bucle.</summary>
    public async Task Run(TimeSpan span)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < span; elapsed += Tick)
        {
            Clock.Advance(Tick);
            await Round();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Feed.DisposeAsync();
        await Store.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }

    private Task<Result> Send(string line, CancellationToken ct)
    {
        lock (_sent)
        {
            _sent.Add((Clock.GetUtcNow(), line));
        }

        return Answer(line);
    }
}
