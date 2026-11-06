namespace PampaPad.Core;

/// <summary>
/// Reconoce toque y mantenido a partir de los eventos crudos de una consola (02 §6, 04 §8).
/// Lo crudo es por nivel: mientras una tecla sigue apretada, cada estado que llega trae otra
/// vez su Press, y ese Press repetido es el refresco que la mantiene viva.
/// </summary>
public sealed class Gestures
{
    /// <summary>El umbral de mantener si config.json no lo dice: el holdMs de 02 §4.10.</summary>
    public const int DefaultHoldMs = 1000;

    /// <summary>La ventana de frescura de 04 §8 (Propuesto, sin medir).</summary>
    public static readonly TimeSpan DefaultFreshness = TimeSpan.FromMilliseconds(150);

    /// <summary>La ventana del doble toque: del primer soltar al segundo apretar (plan de la 1.3.0, decisión 2; Propuesto, sin medir).</summary>
    public static readonly TimeSpan DoubleTapWindow = TimeSpan.FromMilliseconds(300);

    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan> _hold;
    private readonly TimeSpan _freshness;
    private readonly Func<string, bool> _wantsDoubleTap;
    private readonly Lock _gate = new();
    private readonly List<Held> _held = [];

    // Los toques soltados de un control con doble toque: esperan la ventana antes de salir como Tap. PressedAt es cuándo se soltó.
    private readonly List<Held> _saved = [];

    public Gestures(TimeProvider clock, TimeSpan hold, TimeSpan freshness)
        : this(clock, () => hold, freshness)
    {
    }

    /// <summary>Con el umbral leído en cada gesto: Composition lo saca de la biblioteca actual, así holdMs rige sin reiniciar (plan de la 9b, decisión 12).</summary>
    /// <summary>
    /// wantsDoubleTap se pregunta por control en cada gesto: sólo donde dice que sí el toque espera la ventana del doble toque.
    /// Sin él, o diciendo que no, nada cambia y no hay demora (plan de la 1.3.0, decisión 1).
    /// </summary>
    public Gestures(TimeProvider clock, Func<TimeSpan> hold, TimeSpan freshness, Func<string, bool>? wantsDoubleTap = null)
    {
        _clock = clock;
        _hold = hold;
        _freshness = freshness;
        _wantsDoubleTap = wantsDoubleTap ?? (_ => false);
    }

    /// <summary>El de Composition: el doble toque se pide sólo para la tecla del módulo y sólo si la biblioteca actual tiene moduleKey.doubleTap.</summary>
    public static Func<string, bool> ModuleKeyDoubleTap(Func<Config> config) =>
        control => control == Controls.ModuleKey && config().ModuleKey?.DoubleTap is not null;

    /// <summary>
    /// De un estado de teclas a eventos crudos: primero un Release por cada tecla que dejó de estar
    /// apretada y después un Press por cada una que lo está, aunque ya lo estuviera.
    /// </summary>
    public static IReadOnlyList<InputEvent> FromKeys(string source, int previous, int current)
    {
        var events = new List<InputEvent>();
        for (var key = 1; key <= ConsoleProtocol.KeyCount; key++)
        {
            var bit = 1 << (key - 1);
            if ((previous & bit) != 0 && (current & bit) == 0)
            {
                events.Add(new InputEvent(source, Controls.Key(key), Gesture.Release));
            }
        }

        for (var key = 1; key <= ConsoleProtocol.KeyCount; key++)
        {
            if ((current & (1 << (key - 1))) != 0)
            {
                events.Add(new InputEvent(source, Controls.Key(key), Gesture.Press));
            }
        }

        return events;
    }

    /// <summary>El umbral de mantener: el console.holdMs de config.json si es un número positivo; si no, un segundo.</summary>
    public static TimeSpan HoldFrom(int? holdMs) =>
        TimeSpan.FromMilliseconds(holdMs is > 0 ? holdMs.Value : DefaultHoldMs);

    /// <summary>Recibe un evento crudo y devuelve lo que sale de él, en orden.</summary>
    public IReadOnlyList<InputEvent> Feed(InputEvent raw)
    {
        lock (_gate)
        {
            var output = new List<InputEvent>();
            var now = _clock.GetTimestamp();
            Advance(now, output);

            var index = _held.FindIndex(held => held.Source == raw.Source && held.Control == raw.Control);
            switch (raw.Gesture)
            {
                case Gesture.Press when index < 0:
                    // Un toque guardado que sigue en la ventana (Advance ya sacó los vencidos) se descarta: éste es el segundo.
                    var second = _saved.RemoveAll(saved => saved.Source == raw.Source && saved.Control == raw.Control) > 0;
                    _held.Add(new Held(raw.Source, raw.Control, now) { Second = second });
                    output.Add(raw);
                    break;

                case Gesture.Press:
                    _held[index].SeenAt = now;
                    break;

                case Gesture.Release when index >= 0:
                    var released = _held[index];
                    _held.RemoveAt(index);
                    output.Add(raw);
                    if (released.HoldFired)
                    {
                        break;
                    }

                    if (released.Second)
                    {
                        output.Add(raw with { Gesture = Gesture.DoubleTap });
                    }
                    else if (_wantsDoubleTap(raw.Control))
                    {
                        // Sale como Tap desde Advance cuando pase la ventana sin otro Press.
                        _saved.Add(new Held(raw.Source, raw.Control, now));
                    }
                    else
                    {
                        output.Add(raw with { Gesture = Gesture.Tap });
                    }

                    break;

                case Gesture.Release:
                    break;

                default:
                    output.Add(raw);
                    break;
            }

            return output;
        }
    }

    /// <summary>
    /// Hace correr el reloj sin que haya llegado nada: da por soltado lo que perdió frescura, saca los mantenidos que llegaron al umbral
    /// y los toques guardados cuya ventana de doble toque pasó.
    /// </summary>
    public IReadOnlyList<InputEvent> Tick()
    {
        lock (_gate)
        {
            if (_held.Count == 0 && _saved.Count == 0)
            {
                return [];
            }

            var output = new List<InputEvent>();
            Advance(_clock.GetTimestamp(), output);
            return output;
        }
    }

    private void Advance(long now, List<InputEvent> output)
    {
        // Un toque guardado sale al pasar la ventana, o en el acto si el control dejó de pedir el doble toque (se recargó la biblioteca).
        for (var i = 0; i < _saved.Count; i++)
        {
            var saved = _saved[i];
            if (_clock.GetElapsedTime(saved.PressedAt, now) >= DoubleTapWindow || !_wantsDoubleTap(saved.Control))
            {
                _saved.RemoveAt(i--);
                output.Add(new InputEvent(saved.Source, saved.Control, Gesture.Tap));
            }
        }

        var hold = _hold();
        for (var i = 0; i < _held.Count; i++)
        {
            var held = _held[i];
            if (_clock.GetElapsedTime(held.SeenAt, now) >= _freshness)
            {
                // Sin estado fresco se da por soltada y lo pendiente no dispara: sale Release, nunca Tap.
                _held.RemoveAt(i--);

                // Pero si el umbral cayó mientras todavía estaba fresca (hasta SeenAt + freshness), el
                // mantenido ya valía: sale igual que si alguien hubiera llamado a Tick justo entonces.
                // Así lo que sale no depende de cada cuánto se llama a Tick (04 §8).
                if (!held.HoldFired && _clock.GetElapsedTime(held.PressedAt, held.SeenAt) + _freshness > hold)
                {
                    output.Add(new InputEvent(held.Source, held.Control, Gesture.Hold));
                }

                output.Add(new InputEvent(held.Source, held.Control, Gesture.Release));
            }
            else if (!held.HoldFired && _clock.GetElapsedTime(held.PressedAt, now) >= hold)
            {
                held.HoldFired = true;
                output.Add(new InputEvent(held.Source, held.Control, Gesture.Hold));
            }
        }
    }

    private sealed class Held(string source, string control, long at)
    {
        public string Source { get; } = source;

        public string Control { get; } = control;

        public long PressedAt { get; } = at;

        public long SeenAt { get; set; } = at;

        public bool HoldFired { get; set; }

        // Apretada dentro de la ventana de un toque guardado: si se suelta antes del umbral es un doble toque.
        public bool Second { get; init; }
    }
}
