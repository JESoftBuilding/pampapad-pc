namespace PampaPad.Core;

/// <summary>
/// Decide qué combinaciones se registran y traduce cada pulsación a un InputEvent. Las reglas están acá;
/// el adaptador de Windows no tiene ninguna (01 §3.2). Entrega el toque ya reconocido: por teclado no hay "mantener".
/// </summary>
public sealed class HotkeySource(ISystemHotkeys system, StateStore state, Log log) : IInputSource
{
    public const string SourceId = "hotkeys";

    private const int LastId = 0xBFFF;   // RegisterHotKey admite ids de 0x0000 a 0xBFFF

    private static readonly IReadOnlyDictionary<int, InputEvent> Nothing = new Dictionary<int, InputEvent>();

    private readonly Lock _applying = new();
    private volatile IReadOnlyDictionary<int, InputEvent> _registered = Nothing;
    private int _nextId = 1;
    private bool _listening;

    public event Action<InputEvent>? Input;

    public string Id => SourceId;

    public Task StartAsync(CancellationToken ct)
    {
        lock (_applying)
        {
            if (!_listening)
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
            Release();
            var next = new Dictionary<int, InputEvent>();
            var seen = new HashSet<Hotkey>();
            foreach ((int index, Key key) in page.Keys.Indexed())
            {
                if (key.Position is { } position)
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

    public ValueTask DisposeAsync()
    {
        lock (_applying)
        {
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
        if (_registered.TryGetValue(id, out InputEvent? input))
        {
            Input?.Invoke(input);
        }
    }
}
