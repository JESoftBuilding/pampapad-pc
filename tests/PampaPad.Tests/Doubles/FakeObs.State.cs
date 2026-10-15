using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

/// <summary>
/// El estado de OBS que imita el doble y lo que la prueba puede hacerle "desde la ventana de OBS". Códigos,
/// comentarios y eventos son los medidos el 2026-10-13 (Notas/pampapad/obs-datos-en-vivo.md y spec 03 §9). La
/// respuesta sale siempre antes que los eventos, como en OBS. Lo que contesta cada pedido está en FakeObs.Requests.cs.
/// </summary>
public sealed partial class FakeObs
{
    public const string ChapterComment = "Verify that the output being used supports chapter markers.";
    public const string TakePath = "D:/Canal/grabaciones/2026-10-13 11-10-25.mp4";
    public const string MicName = "Mic/Aux";
    public const string MicUuid = "506b0448-d6b6-443e-83d9-5dd76fbf1d60";
    private const int GeneralIntent = 1;
    private const int ScenesIntent = 4;
    private const int InputsIntent = 8;
    private const int OutputsIntent = 64;
    private const long MeasuredTakeBytes = 4689435;

    private readonly Lock _sync = new();
    private long _lastTakeBytes;

    public bool RecordingActive { get; private set; }

    public bool RecordingPaused { get; private set; }

    /// <summary>Lo que contesta outputDuration mientras graba.</summary>
    public long RecordedMs { get; set; }

    public bool Streaming { get; private set; }

    /// <summary>"Formato sin capítulos": 702 también grabando.</summary>
    public bool ChaptersUnsupported { get; set; }

    /// <summary>Los capítulos que entraron, en orden.</summary>
    public List<string> Chapters { get; } = [];

    public string SceneName { get; private set; } = "Pantalla";

    public string SceneUuid { get; private set; } = "f3637453-bc14-4e54-b4c5-1b76f4fb4a21";

    /// <summary>Falso: GetSpecialInputs contesta mic1 en null.</summary>
    public bool HasMic { get; set; } = true;

    public bool MicMuted { get; private set; }

    /// <summary>Medido: 90 ms entre STARTING y STARTED.</summary>
    public TimeSpan StartDelay { get; set; }

    /// <summary>Medido: 310 ms entre STOPPING y STOPPED.</summary>
    public TimeSpan StopDelay { get; set; }

    public List<string> AvailableRequests { get; } =
    [
        "GetVersion", "GetRecordStatus", "GetStreamStatus", "GetSceneList", "GetSpecialInputs", "GetInputList",
        "GetInputMute", "StartRecord", "StopRecord", "PauseRecord", "ResumeRecord", "CreateRecordChapter",
        "SetCurrentProgramScene", "SetInputMute", "GetStreamServiceSettings",
    ];

    // Como si se hiciera desde la ventana de OBS: cambia el estado y manda los eventos.
    public Task StartRecordingAsync() => ActAsync("StartRecord");

    public Task StopRecordingAsync() => ActAsync("StopRecord");

    public Task PauseRecordingAsync() => ActAsync("PauseRecord");

    public Task ResumeRecordingAsync() => ActAsync("ResumeRecord");

    /// <summary>
    /// Medido por WebSocket en la etapa 0 (E0-1, 2026-10-13): pedir la escena que ya está al aire contesta 100 y no manda
    /// CurrentProgramSceneChanged. Desde la ventana de OBS no se midió: el doble supone que es igual.
    /// </summary>
    public Task SetSceneAsync(string name, string uuid)
    {
        lock (_sync)
        {
            if (uuid == SceneUuid)
            {
                return Task.CompletedTask;
            }

            SceneName = name;
            SceneUuid = uuid;
        }

        return EmitAsync("CurrentProgramSceneChanged", ScenesIntent, new JsonObject { ["sceneName"] = name, ["sceneUuid"] = uuid });
    }

    public Task SetMicMutedAsync(bool muted)
    {
        lock (_sync)
        {
            MicMuted = muted;
        }

        return EmitAsync("InputMuteStateChanged", InputsIntent, new JsonObject { ["inputMuted"] = muted, ["inputName"] = MicName, ["inputUuid"] = MicUuid });
    }

    public Task SetStreamingAsync(bool on)
    {
        lock (_sync)
        {
            Streaming = on;
        }

        var state = on ? "OBS_WEBSOCKET_OUTPUT_STARTED" : "OBS_WEBSOCKET_OUTPUT_STOPPED";
        return EmitAsync("StreamStateChanged", OutputsIntent, new JsonObject { ["outputActive"] = on, ["outputState"] = state });
    }

    /// <summary>OBS avisa que se cierra (evento ExitStarted, de la suscripción General).</summary>
    public Task AnnounceExitAsync() => EmitAsync("ExitStarted", GeneralIntent);

    private async Task ActAsync(string requestType)
    {
        Reply reply;
        lock (_sync)
        {
            reply = Answer(requestType, null);
        }

        if (reply.Code != 100)
        {
            throw new InvalidOperationException($"{requestType} contestó {reply.Code}: la prueba pidió algo que este estado no admite");
        }

        await EmitAllAsync(reply.Events);
    }
}
