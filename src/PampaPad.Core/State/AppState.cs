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

/// <summary>La única foto de lo que el programa sabe (01 §3.1). Hasta ahora: la actualización y OBS; las etapas siguientes suman el resto.</summary>
public sealed record AppState
{
    public static AppState Initial { get; } = new();

    /// <summary>Siempre creciente. Lo pone StateStore; quien arma un estado nuevo no lo toca.</summary>
    public long Seq { get; init; }

    public UpdateState Update { get; init; } = UpdateState.Idle;

    public ObsState Obs { get; init; } = ObsState.Initial;
}
