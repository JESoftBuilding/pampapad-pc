namespace PampaPad.Core;

/// <summary>
/// Decide qué combinaciones se registran y traduce cada pulsación a un InputEvent. Las reglas están acá;
/// el adaptador de Windows no tiene ninguna (01 §3.2). Entrega el toque ya reconocido: por teclado no hay "mantener".
/// </summary>
public sealed class HotkeySource(ISystemHotkeys system, StateStore state, Log log, TimeProvider? clock = null) : IInputSource
{
    public const string SourceId = "hotkeys";

    /// <summary>Lo más que pueden quedar suspendidos los atajos mientras la ventana graba una combinación (05 §6, punto 3; Propuesto, sin medir).</summary>
    public static readonly TimeSpan SuspendLimit = TimeSpan.FromSeconds(30);

    private const int LastId = 0xBFFF;   // RegisterHotKey admite ids de 0x0000 a 0xBFFF

    private static readonly IReadOnlyDictionary<int, InputEvent> Nothing = new Dictionary<int, InputEvent>();

    private readonly Lock _applying = new();
    private volatile IReadOnlyDictionary<int, InputEvent> _registered = Nothing;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Page _page = new();
    private IReadOnlyList<HotkeyBinding> _general = [];
    private ITimer? _limit;
    private int _nextId = 1;
    private bool _listening;
    private bool _disposed;
    private bool _suspended;

    public event Action<InputEvent>? Input;

    public string Id => SourceId;

    public Task StartAsync(CancellationToken ct)
    {
        lock (_applying)
        {
            if (!_listening && !_disposed)
            {
                system.Pressed += OnPressed;
                _listening = true;
            }
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<Problem> Apply(Page page, IReadOnlyList<HotkeyBinding> hotkeys)
    {
        var rejected = new List<Problem>();
        int registered;
        lock (_applying)
        {
            if (_disposed)
            {
                // Cerrado el programa, un aviso tardío de la biblioteca no vuelve a tomar combinaciones.
                return [];
            }

            // Lo último que llegó se guarda siempre: al reanudar se vuelve a aplicar. Suspendida, no se registra nada.
            _page = page;
            _general = hotkeys;
            if (_suspended)
            {
                return [];
            }

            Release();
            var next = new Dictionary<int, InputEvent>();
            var seen = new HashSet<Hotkey>();
            var positions = new HashSet<int>();
            foreach ((int index, Key key) in page.Keys.Indexed())
            {
                // Una tecla rota (posición fuera de 1 a 12 o repetida en la página, 02 §10) no toma su atajo: no hay tecla a la que llevar
                // el toque y, si la posición se repite, caería en la primera tecla que la tiene. El validador ya la informó.
                if (key.Position is { } position && position is >= 1 and <= Validator.KeyCount && positions.Add(position))
                {
                    Register(key.Hotkey, $"keys[{index}].hotkey", new InputEvent(SourceId, Controls.Key(position), Gesture.Tap), next, seen, rejected);
                }
            }

            foreach ((int index, HotkeyBinding binding) in hotkeys.Indexed())
            {
                Register(binding.Hotkey, $"hotkeys[{index}].hotkey", new InputEvent(SourceId, Controls.Hotkey, Gesture.Tap, index), next, seen, rejected);
            }

            _registered = next;
            registered = next.Count;
            state.Update(s => s with { Hotkeys = new HotkeysState(registered, rejected.Count) });
        }

        log.Info("hotkeys", $"Atajos tomados: {registered}; rechazados por Windows: {rejected.Count}.");
        return rejected;
    }

    /// <summary>Si los atajos están suspendidos porque la ventana está grabando una combinación.</summary>
    public bool Suspended
    {
        get
        {
            lock (_applying)
            {
                return _suspended;
            }
        }
    }

    /// <summary>
    /// Suspender suelta todos los atajos globales y deja de registrar hasta que se reanude; reanudar vuelve a aplicar la última página y
    /// la última lista (05 §6). La ventana lo pide mientras graba una combinación: una combinación ya registrada no llega a la página (E0-3).
    /// Nunca quedan suspendidos más de SuspendLimit: si nadie reanuda, lo hace el reloj, porque una captura a medias deja la botonera
    /// muda. Suspender de nuevo vuelve a contar los 30 s. La consola no se suspende: es otra fuente.
    /// </summary>
    public void Suspend(bool on)
    {
        lock (_applying)
        {
            if (_disposed)
            {
                return;
            }

            if (on)
            {
                if (!_suspended)
                {
                    _suspended = true;
                    Release();
                    state.Update(s => s with { Hotkeys = new HotkeysState(0, 0) });
                    log.Info("hotkeys", "Atajos suspendidos para grabar una combinación en la ventana.");
                }

                if (_limit is null)
                {
                    _limit = _clock.CreateTimer(_ => OnLimit(), null, SuspendLimit, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _limit.Change(SuspendLimit, Timeout.InfiniteTimeSpan);
                }

                return;
            }

            if (!_suspended)
            {
                return;
            }

            _suspended = false;
            _limit?.Dispose();
            _limit = null;
            log.Info("hotkeys", "Atajos reanudados.");
            Apply(_page, _general);
        }
    }

    // El reloj de los 30 s. Si ya se reanudó, el aviso llegó tarde: no hay nada que hacer ni que anotar.
    private void OnLimit()
    {
        lock (_applying)
        {
            if (!_suspended)
            {
                return;
            }

            log.Warning("hotkeys", $"Los atajos llevaban {SuspendLimit.TotalSeconds:0} s suspendidos sin que la ventana los reanudara: se reanudan solos.");
            Suspend(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_applying)
        {
            _disposed = true;
            _limit?.Dispose();
            _limit = null;
            if (_listening)
            {
                system.Pressed -= OnPressed;
                _listening = false;
            }

            Release();
        }

        return ValueTask.CompletedTask;
    }

    private void Release()
    {
        // Primero se deja de atender: un aviso viejo que quedó en la cola no puede disparar una tecla nueva.
        IReadOnlyDictionary<int, InputEvent> previous = _registered;
        _registered = Nothing;
        foreach (int id in previous.Keys)
        {
            system.Unregister(id);
        }
    }

    private void Register(string? text, string where, InputEvent input, Dictionary<int, InputEvent> next, HashSet<Hotkey> seen, List<Problem> rejected)
    {
        // Sin atajo, con un texto que no se entiende o repetido: no se registra. Los dos últimos casos ya los informó el validador.
        if (Hotkey.Parse(text) is not { } hotkey || !seen.Add(hotkey))
        {
            return;
        }

        int id = _nextId;
        _nextId = _nextId >= LastId ? 1 : _nextId + 1;
        if (system.TryRegister(id, hotkey))
        {
            next[id] = input;
        }
        else
        {
            rejected.Add(new Problem(where, ErrorCodes.HotkeyTaken, $"Windows no dejó registrar {hotkey}: la tiene otro programa. Desde el teclado esta acción no anda.", Severity.Warning));
        }
    }

    // Llega en el hilo del adaptador y no toma _applying: Apply puede estar esperando a ese mismo hilo.
    private void OnPressed(int id)
    {
        if (!_registered.TryGetValue(id, out InputEvent? input))
        {
            return;
        }

        try
        {
            Input?.Invoke(input);
        }
        catch (Exception error)
        {
            // Una excepción que sale de acá termina el hilo de los atajos y, con él, el programa: ninguna combinación volvería a andar.
            log.Error("hotkeys", $"Quien atiende {input.Control} tiró una excepción: {error.Message}", ErrorCodes.InternalError);
        }
    }
}
