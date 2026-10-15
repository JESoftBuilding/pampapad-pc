namespace PampaPad.Core;

/// <summary>Algo que convierte lo que llega de afuera en InputEvent: los atajos de teclado y, desde la etapa 3b, la consola.</summary>
public interface IInputSource : IAsyncDisposable
{
    event Action<InputEvent>? Input;

    string Id { get; }

    Task StartAsync(CancellationToken ct);

    /// <summary>La página que rige y los atajos generales cambiaron. Devuelve lo que la fuente no pudo tomar, con Where relativo a la página o a la lista.</summary>
    IReadOnlyList<Problem> Apply(Page page, IReadOnlyList<HotkeyBinding> hotkeys);
}
