namespace PampaPad.Core;

public enum UpdatePhase
{
    Idle,
    Checking,
    Available,
    Downloading,
    Applying,
}

/// <summary>En qué anda la actualización. Version es la que se ofrece; Detail, por qué no se instaló la última vez que se intentó.</summary>
public sealed record UpdateState(UpdatePhase Phase, string? Version = null, string? Detail = null)
{
    public static UpdateState Idle { get; } = new(UpdatePhase.Idle);
}

/// <summary>La única foto de lo que el programa sabe (01 §3.1). En la etapa 1 sólo sabe de la actualización; E2-1 le suma el resto.</summary>
public sealed record AppState
{
    public static AppState Initial { get; } = new();

    /// <summary>Siempre creciente. Lo pone StateStore; quien arma un estado nuevo no lo toca.</summary>
    public long Seq { get; init; }

    public UpdateState Update { get; init; } = UpdateState.Idle;
}
