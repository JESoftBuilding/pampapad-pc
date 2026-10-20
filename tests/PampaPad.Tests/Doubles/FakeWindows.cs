using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Las ventanas y los procesos de Windows de mentira (01 §7: "esperas y condiciones de ventana, con FakeWindows y reloj falso").
/// Una ventana puede aparecer en la lista número N que se pida, o cuando se cumpla algo (un archivo que dejó un script).
/// </summary>
internal sealed class FakeWindows : IWindows
{
    private readonly Lock _gate = new();
    private readonly List<WindowInfo> _open = [];
    private readonly List<(Func<int, bool> When, WindowInfo Window)> _later = [];
    private int _lists;

    /// <summary>Los procesos que IsProcessRunning da por vivos, sin ".exe".</summary>
    public HashSet<string> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cuántas veces se pidió la lista: cada sondeo de wait.window es una.</summary>
    public int Lists => Volatile.Read(ref _lists);

    public void Open(WindowInfo window)
    {
        lock (_gate)
        {
            _open.Add(window);
        }
    }

    /// <summary>La ventana aparece a partir de la lista número <paramref name="list"/> (1 es la primera que se pida).</summary>
    public void OpenFrom(int list, WindowInfo window)
    {
        lock (_gate)
        {
            _later.Add((asked => asked >= list, window));
        }
    }

    /// <summary>La ventana aparece la primera vez que se pida la lista con la condición cumplida, y queda.</summary>
    public void OpenWhen(Func<bool> condition, WindowInfo window)
    {
        lock (_gate)
        {
            _later.Add((_ => condition(), window));
        }
    }

    public void Close(string title)
    {
        lock (_gate)
        {
            _open.RemoveAll(window => window.Title == title);
        }
    }

    public IReadOnlyList<WindowInfo> List()
    {
        lock (_gate)
        {
            int asked = ++_lists;
            foreach ((Func<int, bool> when, WindowInfo window) in _later.ToList())
            {
                if (when(asked))
                {
                    _open.Add(window);
                    _later.Remove((when, window));
                }
            }

            return [.. _open];
        }
    }

    public bool IsProcessRunning(string name) => Processes.Contains(name);
}
