namespace PampaPad.Core;

public enum UpdatePhase
{
    Idle,
    Checking,
    Available,
    Downloading,
    Applying,
}

/// <summary>
/// En qué anda la actualización. Version es la que se ofrece; Detail, la última falla al bajarla o al dejarla instalada.
/// Que RestartPolicy no deje instalar no va acá: se calcula del estado cada vez (TraySummary.UpdateItems).
/// </summary>
public sealed record UpdateState(UpdatePhase Phase, string? Version = null, string? Detail = null)
{
    public static UpdateState Idle { get; } = new(UpdatePhase.Idle);
}

/// <summary>De qué carpeta es la biblioteca y qué pasó al cargarla. Folder está vacía si no hay carpeta elegida.</summary>
public sealed record LibraryState(LibraryStatus Status, string Folder);

/// <summary>La única foto de lo que el programa sabe (01 §3.1). Hasta ahora: la actualización, OBS y la biblioteca; las etapas siguientes suman el resto.</summary>
public sealed record AppState
{
    public static AppState Initial { get; } = new();

    /// <summary>Siempre creciente. Lo pone StateStore; quien arma un estado nuevo no lo toca.</summary>
    public long Seq { get; init; }

    public UpdateState Update { get; init; } = UpdateState.Idle;

    public ObsState Obs { get; init; } = ObsState.Initial;

    /// <summary>Qué pasó con la última carga de la biblioteca, y de qué carpeta.</summary>
    public LibraryState Library { get; init; } = new(LibraryStatus.NotLoaded, "");

    /// <summary>Todos los problemas vigentes: los de la carga, los de los pasos y los atajos que Windows rechazó.</summary>
    public IReadOnlyList<Problem> Problems { get; init; } = [];
}
