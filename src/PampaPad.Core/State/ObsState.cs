namespace PampaPad.Core;

// Estado de la grabación según OBS (spec 03 §5). Sale de RecordStateChanged.outputState, nunca de outputActive.
public enum RecordingState { Stopped, Starting, Recording, Paused, Stopping }

// Estado del enlace con OBS (spec 03 §2). En JSON viajan con guion bajo: closed, no_websocket, auth_failed...
public enum ObsLinkState { Closed, NoWebsocket, AuthFailed, Loading, Ready, Switching, Lost, Kicked, Exiting }

// Lo que el programa sabe de OBS. Inmutable: se cambia la referencia entera.
public sealed record ObsState
{
    public static ObsState Initial { get; } = new();

    public ObsLinkState Link { get; init; } = ObsLinkState.Closed;
    public RecordingState Recording { get; init; } = RecordingState.Stopped;
    public bool Streaming { get; init; }
    public TimeSpan Recorded { get; init; }             // tiempo grabado (outputDuration), no de reloj
    public string? SceneName { get; init; }             // escena al aire
    public string? SceneUuid { get; init; }
    public string? MicName { get; init; }               // mic1 de GetSpecialInputs; null si no hay
    public string? MicUuid { get; init; }
    public bool MicMuted { get; init; }
    public bool SupportsMarkers { get; init; }          // este OBS tiene CreateRecordChapter

    // "Se graba o se está en pausa" (spec 03 §5): todo estado distinto de stopped.
    public bool TakeInProgress => Recording != RecordingState.Stopped;

    // Cambia el enlace. En closed ni se graba ni se transmite; en los demás cortes vale lo último que se supo.
    public ObsState WithLink(ObsLinkState link) => link == ObsLinkState.Closed
        ? this with { Link = link, Recording = RecordingState.Stopped, Streaming = false, Recorded = TimeSpan.Zero }
        : this with { Link = link };
}
