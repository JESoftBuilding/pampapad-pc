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

    private readonly TimeProvider _clock;
    private readonly TimeSpan _hold;
    private readonly TimeSpan _freshness;
    private readonly Lock _gate = new();
    private readonly List<Held> _held = [];

    public Gestures(TimeProvider clock, TimeSpan hold, TimeSpan freshness)
    {
        _clock = clock;
        _hold = hold;
        _freshness = freshness;
    }

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
                    _held.Add(new Held(raw.Source, raw.Control, now));
                    output.Add(raw);
                    break;

                case Gesture.Press:
                    _held[index].SeenAt = now;
                    break;

                case Gesture.Release when index >= 0:
                    var wasHeld = _held[index].HoldFired;
                    _held.RemoveAt(index);
                    output.Add(raw);
                    if (!wasHeld)
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

    /// <summary>Hace correr el reloj sin que haya llegado nada: da por soltado lo que perdió frescura y saca los mantenidos que llegaron al umbral.</summary>
    public IReadOnlyList<InputEvent> Tick()
    {
        lock (_gate)
        {
            if (_held.Count == 0)
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
        for (var i = 0; i < _held.Count; i++)
        {
            var held = _held[i];
            if (_clock.GetElapsedTime(held.SeenAt, now) >= _freshness)
            {
                // Sin estado fresco se da por soltada y lo pendiente no dispara: sale Release, nunca Tap.
                _held.RemoveAt(i--);
                output.Add(new InputEvent(held.Source, held.Control, Gesture.Release));
            }
            else if (!held.HoldFired && _clock.GetElapsedTime(held.PressedAt, now) >= _hold)
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
    }
}
