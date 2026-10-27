using System.Collections.Concurrent;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El diálogo de Windows y el Explorador, de mentira: devuelve lo que se le encola y anota qué carpeta se pidió abrir.</summary>
internal sealed class FakeShellDialogs : IShellDialogs
{
    /// <summary>Lo que devuelve cada PickFiles, en orden. Vacía, es "se canceló el diálogo".</summary>
    public ConcurrentQueue<IReadOnlyList<string>> Picks { get; } = new();

    public ConcurrentQueue<string> Revealed { get; } = new();

    public Task<IReadOnlyList<string>> PickFiles() =>
        Task.FromResult(Picks.TryDequeue(out IReadOnlyList<string>? files) ? files : (IReadOnlyList<string>)[]);

    public void Reveal(string path) => Revealed.Enqueue(path);
}
