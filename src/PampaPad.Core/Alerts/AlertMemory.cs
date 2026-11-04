namespace PampaPad.Core;

/// <summary>Lo que se vio con el enlace listo la última vez que hubo una toma, para obs_lost: Closed borra el Recording (decisión 7).</summary>
public sealed record LostTake(TimeSpan Recorded, bool Exited);

/// <summary>
/// Lo que el vigía recuerda entre una mirada y la siguiente (plan de la 9b, E9b-3). Lo escribe sólo GraveAlerts. Take sube con cada toma
/// nueva y el chequeo al empezar trae el suyo, para no pegarle la falla a otra toma.
/// </summary>
public sealed record AlertMemory
{
    public static AlertMemory Initial { get; } = new();

    /// <summary>El aviso a la vista, o null.</summary>
    public GraveAlert? Shown { get; init; }

    public int Take { get; init; }

    /// <summary>Los atendidos con el clic en esta toma: no vuelven hasta la próxima (decisión 8).</summary>
    public IReadOnlySet<string> Attended { get; init; } = new HashSet<string>();

    /// <summary>La grabación y lo grabado la última vez que se vio el enlace listo; null si nunca se vio.</summary>
    public RecordingState? ReadyRecording { get; init; }

    public TimeSpan ReadyRecorded { get; init; }

    public bool WasReady { get; init; }

    public bool MicWasMuted { get; init; }

    /// <summary>mic_muted prendido hasta que se abra el micrófono o se lo atienda.</summary>
    public bool MicLatched { get; init; }

    /// <summary>Desde cuándo está en pausa: RecordingChangedOn, o la primera vez que se vio la pausa si era null.</summary>
    public DateTimeOffset? PauseSince { get; init; }

    /// <summary>Lo grabado a partir de lo cual still_recording vuelve después de un clic.</summary>
    public TimeSpan? StillQuietUntil { get; init; }

    /// <summary>obs_lost prendido hasta el clic, o hasta que OBS vuelve con una toma en curso.</summary>
    public LostTake? Lost { get; init; }

    /// <summary>La primera falla del chequeo al empezar esta toma, en letras de la consola.</summary>
    public string? StartFailure { get; init; }

    /// <summary>La hora de la falla de marca que se atendió: la misma no vuelve, una nueva sí.</summary>
    public DateTimeOffset? MarkerAttended { get; init; }
}
