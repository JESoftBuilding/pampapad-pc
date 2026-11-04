namespace PampaPad.Core;

/// <summary>
/// El vigía de los avisos graves (plan de la 9b, E9b-3): mira el estado con cada cambio y cada segundo, y deja en AppState.Alert el aviso a
/// la vista. Al empezar una toma lanza el chequeo de la decisión 7 en segundo plano, con un tope de 3 s. Attend es el clic de la perilla.
/// </summary>
public sealed class AlertWatch : IDisposable
{
    public const string Module = "alerts";

    /// <summary>Cada cuánto vuelve a mirar sin cambios: los relojes de la pausa y de la toma.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    /// <summary>El tope del chequeo al empezar a grabar: si se pasa, no avisa nada (decisión 7, "Lo que puede morder").</summary>
    public static readonly TimeSpan CheckLimit = TimeSpan.FromSeconds(3);

    private readonly StateStore _state;
    private readonly Func<Config> _config;
    private readonly Func<CancellationToken, Task<PreflightReport>> _checkTake;
    private readonly Log _log;
    private readonly TimeProvider _clock;
    private readonly Action<GraveAlert>? _appeared;
    private readonly Lock _gate = new();
    private AlertMemory _memory = AlertMemory.Initial;
    private IDisposable? _subscription;
    private ITimer? _timer;

    /// <summary>appeared se llama una vez por aviso nuevo, fuera del candado: es el sonido de la PC (Notifier.OnAlertAppeared).</summary>
    public AlertWatch(StateStore state, Func<Config> config, Func<CancellationToken, Task<PreflightReport>> checkTake, Log log, TimeProvider clock, Action<GraveAlert>? appeared = null)
    {
        _state = state;
        _config = config;
        _checkTake = checkTake;
        _log = log;
        _clock = clock;
        _appeared = appeared;
    }

    /// <summary>La última memoria: para las pruebas.</summary>
    public AlertMemory Memory
    {
        get
        {
            lock (_gate)
            {
                return _memory;
            }
        }
    }

    public void Start()
    {
        _subscription = _state.Subscribe((_, _) => Look());
        _timer = _clock.CreateTimer(_ => Look(), null, Every, Every);
        Look();
    }

    /// <summary>Una mirada al estado de ahora.</summary>
    public void Look() => Apply(memory => memory, "");

    /// <summary>El clic de la perilla: atiende el aviso a la vista; sin aviso no hace nada (decisión 8).</summary>
    public void Attend() => Apply(GraveAlerts.Attend, "atendido con un clic");

    public void Dispose()
    {
        _subscription?.Dispose();
        _timer?.Dispose();
    }

    private void Apply(Func<AlertMemory, AlertMemory> change, string why)
    {
        GraveAlert? appeared = null;
        GraveAlert? gone = null;
        int newTake = 0;
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            AlertMemory previous = _memory;
            AlertMemory next = GraveAlerts.Step(change(previous), _state.Current, _config(), now);
            _memory = next;
            newTake = next.Take != previous.Take ? next.Take : 0;
            if (next.Shown is { } shown && (previous.Shown is null || previous.Shown.Id != shown.Id || previous.Shown.Since != shown.Since))
            {
                appeared = shown;
            }

            if (previous.Shown is { } before && next.Shown?.Id != before.Id)
            {
                gone = before;
            }

            _state.Update(s => s.Alert == next.Shown && s.TakeCheckFailure == next.StartFailure ? s : s with { Alert = next.Shown, TakeCheckFailure = next.StartFailure });

            // marker_failed se apaga a los 2 s: la mirada siguiente va en ese momento y no un segundo después.
            TimeSpan due = next.Shown is { Id: AlertTexts.MarkerFailed } marker ? marker.Since + GraveAlerts.MarkerShown - now : Every;
            _timer?.Change(due < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : due > Every ? Every : due, Every);
        }

        if (gone is not null)
        {
            _log.Info(Module, $"Aviso {gone.Id} apagado{(why.Length > 0 ? $": {why}" : "")}.");
        }

        if (appeared is not null)
        {
            _log.Warning(Module, $"Aviso {appeared.Id}: {appeared.Title} / {appeared.Line1} / {appeared.Line2}. Luz: {(appeared.Light ? "sí" : "no")}; zumbador: {(appeared.Sound ? "sí" : "no")}.");
            try
            {
                _appeared?.Invoke(appeared);
            }
            catch (Exception error)
            {
                _log.Error(Module, $"No se pudo avisar en la PC: {error.Message}", ErrorCodes.InternalError);
            }
        }

        if (newTake > 0)
        {
            _ = CheckTakeAsync(newTake);
        }
    }

    // En segundo plano: no frena el arranque de la toma. Si pasa el tope, o algo quedó sin comprobar por el corte, no avisa nada.
    private async Task CheckTakeAsync(int take)
    {
        long started = _clock.GetTimestamp();
        using var limit = new CancellationTokenSource(CheckLimit, _clock);
        PreflightReport report;
        try
        {
            report = await _checkTake(limit.Token).WaitAsync(CheckLimit, _clock).ConfigureAwait(false);
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            Late();
            return;
        }
        catch (Exception error)
        {
            _log.Error(Module, $"El chequeo al empezar a grabar falló: {error.Message}", ErrorCodes.InternalError);
            return;
        }

        if (report.Checks.Any(check => check.Code == ErrorCodes.Cancelled))
        {
            Late();
            return;
        }

        TimeSpan took = _clock.GetElapsedTime(started);
        _log.Info(Module, $"Chequeo al empezar a grabar: {report.Checks.Count} comprobaciones y {report.Failures.Count} con falla, en {took.TotalMilliseconds:0} ms.");
        if (report.Failures.FirstOrDefault() is { } first)
        {
            string text = AlertTexts.Failure(first);
            Apply(memory => memory.Take == take ? memory with { StartFailure = text } : memory, "");
        }
    }

    private void Late() =>
        _log.Warning(Module, $"El chequeo al empezar a grabar pasó el tope de {CheckLimit.TotalSeconds:0} s: no avisa nada.");
}
