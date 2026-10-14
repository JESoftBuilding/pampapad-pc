using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// De lo que dice OBS al estado (spec 03 §5). Todo es puro: recibe un <see cref="ObsState"/> y devuelve otro.
/// <see cref="ObsClient"/> lo aplica en el hilo de recepción, en el orden en que llegó cada mensaje.
/// </summary>
public static class ObsMirror
{
    /// <summary>La lectura inicial, en un lote y en este orden (spec 03 §2, paso 5). OBS no manda el estado al conectar.</summary>
    public static IReadOnlyList<ObsRequest> InitialBatch { get; } =
    [
        new("GetRecordStatus"),
        new("GetStreamStatus"),
        new("GetSceneList"),
        new("GetSpecialInputs"),
        new("GetInputList"),
    ];

    public static ObsState Apply(ObsState state, ObsEvent e) => e.Type switch
    {
        "RecordStateChanged" => RecordChanged(state, ObsMessages.Text(e.Data, "outputState")),
        "StreamStateChanged" => StreamChanged(state, ObsMessages.Text(e.Data, "outputState")),
        "CurrentProgramSceneChanged" => state with
        {
            SceneName = ObsMessages.Text(e.Data, "sceneName"),
            SceneUuid = ObsMessages.Text(e.Data, "sceneUuid"),
        },
        "InputMuteStateChanged" when IsMic(state, e.Data) => state with { MicMuted = ObsMessages.Flag(e.Data, "inputMuted") },
        _ => state,
    };

    /// <summary>availableRequests dice si este OBS tiene CreateRecordChapter, que existe desde obs-websocket 5.5.0.</summary>
    public static ObsState ApplyVersion(ObsState state, ObsResponse getVersion) =>
        state with { SupportsMarkers = ObsMessages.Supports(getVersion, "CreateRecordChapter") };

    /// <summary>Las respuestas llegan en el orden de <see cref="InitialBatch"/>. Una que falló deja su dato como estaba.</summary>
    public static ObsState ApplyInitial(ObsState state, IReadOnlyList<ObsResponse> batch)
    {
        if (batch.Count != InitialBatch.Count)
        {
            return state;
        }

        var (record, stream, scenes, special, inputs) = (batch[0], batch[1], batch[2], batch[3], batch[4]);
        if (record.Ok)
        {
            var active = ObsMessages.Flag(record.Data, "outputActive");
            var paused = ObsMessages.Flag(record.Data, "outputPaused");
            state = state with
            {
                Recording = !active ? RecordingState.Stopped : paused ? RecordingState.Paused : RecordingState.Recording,
                Recorded = active ? TimeSpan.FromMilliseconds(ObsMessages.Number(record.Data, "outputDuration")) : TimeSpan.Zero,
            };
        }

        if (stream.Ok)
        {
            state = state with { Streaming = ObsMessages.Flag(stream.Data, "outputActive") || ObsMessages.Flag(stream.Data, "outputReconnecting") };
        }

        if (scenes.Ok)
        {
            state = state with
            {
                SceneName = ObsMessages.Text(scenes.Data, "currentProgramSceneName"),
                SceneUuid = ObsMessages.Text(scenes.Data, "currentProgramSceneUuid"),
            };
        }

        if (special.Ok)
        {
            // GetSpecialInputs da nombres, no identificadores: el uuid sale de cruzarlo con GetInputList (spec 03 §1).
            var mic = ObsMessages.Text(special.Data, "mic1");
            var uuid = mic is null || !inputs.Ok
                ? null
                : ObsMessages.Items(inputs.Data, "inputs")
                    .Where(input => ObsMessages.Text(input, "inputName") == mic)
                    .Select(input => ObsMessages.Text(input, "inputUuid"))
                    .FirstOrDefault();
            state = state with { MicName = mic, MicUuid = uuid, MicMuted = mic is not null && state.MicMuted };
        }

        return state;
    }

    public static ObsState ApplyMicMute(ObsState state, ObsResponse getInputMute) =>
        getInputMute.Ok ? state with { MicMuted = ObsMessages.Flag(getInputMute.Data, "inputMuted") } : state;

    /// <summary>
    /// El sondeo trae el tiempo grabado. Detenido, OBS contesta duración 0 y los bytes de la toma anterior
    /// (medido): no pisa nada. El estado de la grabación lo dan los eventos, no este pedido.
    /// </summary>
    public static ObsState ApplyRecordStatus(ObsState state, ObsResponse getRecordStatus) =>
        getRecordStatus.Ok && ObsMessages.Flag(getRecordStatus.Data, "outputActive")
            ? state with { Recorded = TimeSpan.FromMilliseconds(ObsMessages.Number(getRecordStatus.Data, "outputDuration")) }
            : state;

    // El estado sale de outputState. outputActive viene en false en PAUSED, STARTING y STOPPING (medido):
    // una luz guiada por outputActive se apagaría al pausar.
    private static ObsState RecordChanged(ObsState state, string? outputState) => outputState switch
    {
        "OBS_WEBSOCKET_OUTPUT_STARTING" => state with { Recording = RecordingState.Starting },
        "OBS_WEBSOCKET_OUTPUT_STARTED" => state with { Recording = RecordingState.Recording, Recorded = TimeSpan.Zero },
        "OBS_WEBSOCKET_OUTPUT_PAUSED" => state with { Recording = RecordingState.Paused },
        "OBS_WEBSOCKET_OUTPUT_RESUMED" => state with { Recording = RecordingState.Recording },
        "OBS_WEBSOCKET_OUTPUT_STOPPING" => state with { Recording = RecordingState.Stopping },
        "OBS_WEBSOCKET_OUTPUT_STOPPED" => state with { Recording = RecordingState.Stopped },
        _ => state,
    };

    // "Se transmite" va de STARTING a STOPPED, también en RECONNECTING, donde outputActive viene en false (spec 03 §5).
    private static ObsState StreamChanged(ObsState state, string? outputState) => outputState switch
    {
        "OBS_WEBSOCKET_OUTPUT_STARTING" or "OBS_WEBSOCKET_OUTPUT_STARTED" or "OBS_WEBSOCKET_OUTPUT_RECONNECTING"
            or "OBS_WEBSOCKET_OUTPUT_RECONNECTED" or "OBS_WEBSOCKET_OUTPUT_STOPPING" => state with { Streaming = true },
        "OBS_WEBSOCKET_OUTPUT_STOPPED" => state with { Streaming = false },
        _ => state,
    };

    private static bool IsMic(ObsState state, JsonElement data) => state.MicUuid is not null
        ? ObsMessages.Text(data, "inputUuid") == state.MicUuid
        : state.MicName is not null && ObsMessages.Text(data, "inputName") == state.MicName;
}
