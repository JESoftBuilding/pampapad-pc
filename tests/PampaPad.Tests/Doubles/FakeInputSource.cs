using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Una fuente de entrada de mentira (la consola de la etapa 3b, por ejemplo): anota a qué página y a qué lista general se la aplicó,
/// devuelve los problemas que la prueba le diga o tira, y deja levantar un evento.
/// </summary>
public sealed class FakeInputSource(string id) : IInputSource
{
    public event Action<InputEvent>? Input;

    public string Id => id;

    public List<(Page Page, IReadOnlyList<HotkeyBinding> Hotkeys)> Applied { get; } = [];

    /// <summary>Lo que Apply devuelve como "no pude tomar esto".</summary>
    public List<Problem> Rejects { get; } = [];

    public bool ThrowOnApply { get; set; }

    /// <summary>Se llama al entrar a Apply, antes de anotar nada: deja a una prueba frenar a quien llama o contar cuántos hay adentro a la vez.</summary>
    public System.Action? OnApply { get; set; }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public IReadOnlyList<Problem> Apply(Page page, IReadOnlyList<HotkeyBinding> hotkeys)
    {
        OnApply?.Invoke();
        if (ThrowOnApply)
        {
            throw new InvalidOperationException("Apply roto a propósito");
        }

        Applied.Add((page, hotkeys));
        return [.. Rejects];
    }

    public void Raise(InputEvent e) => Input?.Invoke(e);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
