namespace PampaPad.Core;

// Estado de la grabación según OBS (spec 03 §5). Sale de RecordStateChanged.outputState, nunca de outputActive.
public enum RecordingState { Stopped, Starting, Recording, Paused, Stopping }

// Estado del enlace con OBS (spec 03 §2). Por el puente viaja con guion bajo (no_websocket, auth_failed), convertido a mano en
// ObsView.LinkName: pregunta abierta 59, escrito (b). Con las opciones de Json.cs iría en camelCase.
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
    public IReadOnlyList<ObsItem> Scenes { get; init; } = [];   // todas las de GetSceneList, piezas incluidas (03 §1 y §5)
    public IReadOnlyList<ObsItem> Inputs { get; init; } = [];   // las de GetInputList; el silencio sólo se refleja del micrófono
    public long? RecordingChangedAt { get; init; }              // GetTimestamp() del último cambio de la grabación que avisó OBS; null si no avisó ninguno
    public DateTimeOffset? RecordingChangedOn { get; init; }    // la hora de ese mismo cambio, para lo que se compara con una hora (la pausa y el chequeo previo en la consola)
    public DateTimeOffset? OwnMuteAt { get; init; }             // el último SetInputMute sobre el micrófono pedido por PampaPad: un silencio propio no avisa (9b, decisión 7)
    public int TakeMarks { get; init; }                         // las marcas que entraron en esta toma: "7 MARCAS" del renglón chico (9b, decisión 11)

    // "Se graba o se está en pausa" (spec 03 §5): todo estado distinto de stopped.
    public bool TakeInProgress => Recording != RecordingState.Stopped;

    // OBS abierto es que corre obs64, se pueda hablar con él o no (02 §7, regla 9): todo enlace distinto de closed.
    public bool IsOpen => Link != ObsLinkState.Closed;

    // Cambia el enlace. En closed ni se graba ni se transmite; en los demás cortes vale lo último que se supo.
    public ObsState WithLink(ObsLinkState link) => link == ObsLinkState.Closed
        ? this with { Link = link, Recording = RecordingState.Stopped, Streaming = false, Recorded = TimeSpan.Zero }
        : this with { Link = link };
}
