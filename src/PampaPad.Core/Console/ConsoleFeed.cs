namespace PampaPad.Core;

/// <summary>
/// El único que le escribe la pantalla a la consola, además de lights (plan de la 9a, decisión 6). Cada vuelta proyecta el estado y
/// manda lo que cambió y lo que toca por ritmo (decisión 5). Del otro lado nadie acusa, salvo la huella de los ajustes en el Hello:
/// por eso se repite todo, y al conectar o cuando la consola recién arrancó se manda entero. Sin consola conectada se calla.
/// </summary>
public sealed partial class ConsoleFeed : IAsyncDisposable
{
    public const string Module = "console";

    /// <summary>Cada cuánto da una vuelta: un cambio sale como mucho 100 ms después.</summary>
    public static readonly TimeSpan RoundPeriod = TimeSpan.FromMilliseconds(100);

    private const int PairCount = 6;

    // 04:417: la consola acumula lo que no lee y no conviene mandarle más de 7 seguidas.
    private const int Burst = 7;

    // Una huella que no coincide al conectar es lo normal hasta que la consola aplica los ajustes y saluda: se anota recién después
    // de unos envíos.
    private const int SettingsBeforeWarning = 3;

    private static readonly TimeSpan ScreenEvery = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LabelEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TextEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StripEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SettingsEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(10);

    // Una escritura que falló con la consola todavía "conectada" (trabada más que el WriteTimeout): se reintenta entero después de esto.
    private static readonly TimeSpan QuietAfterFailure = TimeSpan.FromSeconds(2);

    private readonly StateStore _state;
    private readonly Func<Config> _config;
    private readonly Func<AppState, Config, DateTimeOffset, ConsoleFrame> _project;
    private readonly Func<string, CancellationToken, Task<Result>> _send;
    private readonly Log _log;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly KeyFace?[] _lastKeys = new KeyFace?[PairCount * 2];
    private Task _loop = Task.CompletedTask;
    private int _sendAll;
    private bool _wasConnected;
    private DateTimeOffset _quietUntil;
    private ScreenFrame? _lastScreen;
    private ConsoleText? _lastScene;
    private ConsoleText? _lastSmall;
    private IReadOnlyList<LedColor>? _lastStrip;
    private DateTimeOffset _screenAt, _labelAt, _textAt, _stripAt, _settingsAt;
    private int _rotation;
    private int _settingsSent;
    private bool _warned;
    private bool _roundFailed;
    private int _inBurst;
    private int _disposed;

    public ConsoleFeed(
        StateStore state,
        Func<Config> config,
        Func<AppState, Config, DateTimeOffset, ConsoleFrame> project,
        Func<string, CancellationToken, Task<Result>> send,
        Log log,
        TimeProvider clock)
    {
        _state = state;
        _config = config;
        _project = project;
        _send = send;
        _log = log;
        _clock = clock;
    }

    /// <summary>Que la vuelta siguiente mande todo. Se cuelga de SerialConsole.ConsoleRebooted.</summary>
    public void SendEverything() => Volatile.Write(ref _sendAll, 1);

    /// <summary>Arranca el bucle y vuelve en el acto. Vive hasta que se cancela el token o se llama a DisposeAsync.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        var life = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        _loop = Task.Run(
            async () =>
            {
                using (life)
                {
                    await RunAsync(life.Token).ConfigureAwait(false);
                }
            },
            CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Una vuelta: lo que el bucle hace cada RoundPeriod. Pública para que las pruebas la corran con el reloj en la mano.</summary>
    public async Task RoundAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var current = _state.Current;
        if (current.Console.State != ConsoleLinkState.Connected)
        {
            _wasConnected = false;
            return;
        }

        if (!_wasConnected)
        {
            // Un enlace nuevo: la consola puede venir de "SIN PC", que borra la tira, o de otra placa.
            _wasConnected = true;
            _sendAll = 1;
            _quietUntil = DateTimeOffset.MinValue;
            _settingsSent = 0;
            _warned = false;
        }

        if (now < _quietUntil)
        {
            return;
        }

        var config = _config();
        var pairs = ConsoleSettingsWire.Pairs(config.Console);
        var fingerprint = ConsoleSettingsWire.Fingerprint(pairs);
        var inSync = CheckSettings(current.Console, fingerprint);
        var frame = _project(current, config, now);
        _inBurst = 0;

        if (Interlocked.Exchange(ref _sendAll, 0) == 1)
        {
            _ = await SendSettingsAsync(ConsoleProtocol.SettingsLine(pairs, fingerprint), now, ct).ConfigureAwait(false)
                && await SendScreenAsync(frame.Screen, now, ct).ConfigureAwait(false)
                && await SendPairsAsync(frame, all: true, now, ct).ConfigureAwait(false)
                && await SendTextsAsync(frame, all: true, now, ct).ConfigureAwait(false)
                && await SendAlertAsync(frame.Alert, all: true, now, ct).ConfigureAwait(false)
                && await SendStripAsync(frame.Strip, now, ct).ConfigureAwait(false);
            return;
        }

        if (inSync != true && now - _settingsAt >= SettingsEvery
            && !await SendSettingsAsync(ConsoleProtocol.SettingsLine(pairs, fingerprint), now, ct).ConfigureAwait(false))
        {
            return;
        }

        // Primero la banda con la página nueva y después sus etiquetas: la consola descarta en silencio las de otra página.
        var pageChanged = frame.Screen.Page != _lastScreen?.Page;
        if ((frame.Screen != _lastScreen || now - _screenAt >= ScreenEvery)
            && !await SendScreenAsync(frame.Screen, now, ct).ConfigureAwait(false))
        {
            return;
        }

        if (!await SendPairsAsync(frame, all: pageChanged, now, ct).ConfigureAwait(false)
            || !await SendTextsAsync(frame, all: now - _textAt >= TextEvery, now, ct).ConfigureAwait(false)
            || !await SendAlertAsync(frame.Alert, all: false, now, ct).ConfigureAwait(false))
        {
            return;
        }

        if (!SameStrip(frame.Strip) || now - _stripAt >= StripEvery)
        {
            await SendStripAsync(frame.Strip, now, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RoundPeriod, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await RoundAsync(ct).ConfigureAwait(false);
                    _roundFailed = false;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Una vez por racha: si la proyección tira, tiraría diez veces por segundo.
                    if (!_roundFailed)
                    {
                        _roundFailed = true;
                        _log.Error(Module, $"No se pudo armar lo que muestra la consola: {error.Message}", ErrorCodes.InternalError);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // DisposeAsync o el programa que cierra.
        }
    }

    /// <summary>Al día si la huella del Hello es la propia; sin Hello no se sabe. Lo deja en ConsoleLink y avisa una vez si no coincide.</summary>
    private bool? CheckSettings(ConsoleLink link, ushort fingerprint)
    {
        bool? inSync = link.SettingsFingerprint is { } hello ? hello == fingerprint : null;
        if (link.SettingsInSync != inSync)
        {
            _state.Update(s => s.Console.State == ConsoleLinkState.Connected ? s with { Console = s.Console with { SettingsInSync = inSync } } : s);
        }

        if (inSync == false && !_warned && _settingsSent >= SettingsBeforeWarning)
        {
            _warned = true;
            _log.Warning(Module, $"La consola no confirma los ajustes: su huella es 0x{link.SettingsFingerprint:X4} y la de la PC, 0x{fingerprint:X4}. Se siguen mandando cada {SettingsEvery.TotalSeconds:0} s.");
        }

        return inSync;
    }
}
