using System.Globalization;
using System.Text.Json;

namespace PampaPad.Core;

// Las condiciones de OBS que no miran la imagen: el perfil, el formato de grabación, el micrófono y el audio de la cámara (02 §5; 03 §7).
// Preguntan a OBS al evaluar (ObsQueries); sin el enlace en ready dan skipped con obs_disconnected.
public static partial class Conditions
{
    public const string ProfileIs = "obs.profileIs";
    public const string RecordFormatIs = "obs.recordFormatIs";
    public const string MicNotMuted = "obs.micNotMuted";
    public const string CameraAudioOff = "obs.cameraAudioOff";

    /// <summary>La clase de entrada de la cámara: las cámaras se reconocen por inputKind, no por nombre (03 §1).</summary>
    public const string CameraKind = "dshow_input";

    private static readonly Field Source = new("source", "Fuente de OBS", FieldKind.ObsInput, Optional: true);

    /// <summary>El perfil activo se llama así (02 §5). Con GetProfileList.</summary>
    public static ConditionType Profile(ObsQueries obs) => new(
        ProfileIs,
        "OBS está en el perfil",
        [new Field("profile", "Perfil", FieldKind.Text)],
        async (p, ct) =>
        {
            string wanted = ConditionRules.Text(p, "profile") ?? "";
            Result<JsonElement> list = await obs.AskAsync("GetProfileList", null, "el perfil", ct).ConfigureAwait(false);
            if (!list.Ok)
            {
                return list.Failure;
            }

            string current = ObsMessages.Text(list.Value, "currentProfileName") ?? "";
            return new Verdict(current == wanted, $"El perfil activo es \"{current}\"; se pedía \"{wanted}\".");
        })
    {
        Check = p => RequiredText(p, "profile"),
    };

    /// <summary>
    /// El perfil activo graba en ese formato (02 §5; 03 §7): Output/Mode y, según el modo, AdvOut/RecFormat2 o SimpleOutput/RecFormat2;
    /// sin parameterValue vale defaultParameterValue. Si alguien cambia el formato, todas las marcas fallan con un 702 que parece pausa.
    /// </summary>
    public static ConditionType RecordFormat(ObsQueries obs) => new(
        RecordFormatIs,
        "OBS graba en el formato",
        [new Field("format", "Formato", FieldKind.Text)],
        async (p, ct) =>
        {
            string wanted = ConditionRules.Text(p, "format") ?? "";
            Result<string> mode = await ParameterAsync(obs, "Output", "Mode", ct).ConfigureAwait(false);
            if (!mode.Ok)
            {
                return mode.Failure;
            }

            string category = mode.Value == "Advanced" ? "AdvOut" : "SimpleOutput";
            Result<string> format = await ParameterAsync(obs, category, "RecFormat2", ct).ConfigureAwait(false);
            return format.Ok
                ? new Verdict(format.Value == wanted, $"El perfil graba en {format.Value} (salida {mode.Value}); se pedía {wanted}.")
                : format.Failure;
        })
    {
        Check = p => RequiredText(p, "format"),
    };

    /// <summary>
    /// El micrófono no está silenciado (02 §5; 03 §7): el de source o, sin él, el mic1 que refleja el estado con sus eventos. Sin micrófono
    /// informado no aplica.
    /// </summary>
    public static ConditionType Mic(ObsQueries obs, StateStore state) => new(
        MicNotMuted,
        "El micrófono está abierto",
        [Source],
        async (p, ct) =>
        {
            ObsState known = state.Current.Obs;
            if (known.Link != ObsLinkState.Ready)
            {
                return new Failure(ErrorCodes.ObsDisconnected, "Sin OBS: no se pudo leer el micrófono.");
            }

            if (ConditionRules.Reference(p, "source") is not { } wanted)
            {
                return known.MicName is { } mic
                    ? new Verdict(!known.MicMuted, $"El micrófono \"{mic}\" está {(known.MicMuted ? "silenciado" : "abierto")}.")
                    : Verdict.NotApplicable("OBS no informa micrófono (mic1 vacío).");
            }

            Result<ObsItem> input = obs.FindInput(wanted);
            if (!input.Ok)
            {
                return input.Failure;
            }

            Result<bool> muted = await MutedAsync(obs, input.Value!, ct).ConfigureAwait(false);
            return muted.Ok ? new Verdict(!muted.Value, $"\"{input.Value!.Name}\" está {(muted.Value ? "silenciado" : "abierto")}.") : muted.Failure;
        })
    {
        Check = CheckSource,
    };

    /// <summary>
    /// La cámara no manda su audio (02 §5; 03 §7): silenciada y sin ninguna de las pistas 1 a 6. Sin source mira cada dshow_input de la
    /// escena al aire. No se aplica a una ventana capturada: "Celu (scrcpy)" está sin silenciar y con las seis pistas y fallaría siempre.
    /// </summary>
    public static ConditionType CameraAudio(ObsQueries obs) => new(
        CameraAudioOff,
        "La cámara no manda audio",
        [Source],
        async (p, ct) =>
        {
            Result<IReadOnlyList<SceneSource>> cameras = await CamerasAsync(obs, p, ct).ConfigureAwait(false);
            if (!cameras.Ok)
            {
                return cameras.Failure;
            }

            if (cameras.Value!.Count == 0)
            {
                return Verdict.NotApplicable(NotOnAir(obs, p, "cámara", "una cámara"));
            }

            foreach (SceneSource camera in cameras.Value)
            {
                var input = new ObsItem(camera.Uuid, camera.Name);
                Result<bool> muted = await MutedAsync(obs, input, ct).ConfigureAwait(false);
                if (!muted.Ok)
                {
                    return muted.Failure;
                }

                Result<JsonElement> tracks = await obs.AskAsync("GetInputAudioTracks", new { inputUuid = camera.Uuid }, $"las pistas de \"{camera.Name}\"", ct).ConfigureAwait(false);
                if (!tracks.Ok)
                {
                    return tracks.Failure;
                }

                ObsMessages.Has(tracks.Value, "inputAudioTracks", out JsonElement map);
                string[] on = [.. Enumerable.Range(1, 6).Select(track => track.ToString(CultureInfo.InvariantCulture)).Where(track => ObsMessages.Flag(map, track))];
                if (!muted.Value || on.Length > 0)
                {
                    string why = !muted.Value ? "no está silenciada" : $"manda audio por la pista {string.Join(", ", on)}";
                    return new Verdict(false, $"La cámara \"{camera.Name}\" {why}.");
                }
            }

            return new Verdict(true, $"La cámara de \"{obs.SceneName}\" está silenciada y sin pistas.");
        })
    {
        Check = CheckSource,
    };

    // Las cámaras que mira una condición: la de source si es una cámara, o las de la escena al aire.
    private static async Task<Result<IReadOnlyList<SceneSource>>> CamerasAsync(ObsQueries obs, JsonElement p, CancellationToken ct)
    {
        Result<IReadOnlyList<SceneSource>> visible = await obs.VisibleSourcesAsync(ct).ConfigureAwait(false);
        if (!visible.Ok)
        {
            return visible.Failure;
        }

        if (ConditionRules.Reference(p, "source") is not { } wanted)
        {
            return Result<IReadOnlyList<SceneSource>>.Success([.. visible.Value!.Where(source => source.InputKind == CameraKind)]);
        }

        Result<ObsItem> input = obs.FindInput(wanted);
        if (!input.Ok)
        {
            return input.Failure;
        }

        return Result<IReadOnlyList<SceneSource>>.Success([.. visible.Value!.Where(source => source.Uuid == input.Value!.Uuid && source.InputKind == CameraKind)]);
    }

    private static async Task<Result<bool>> MutedAsync(ObsQueries obs, ObsItem input, CancellationToken ct)
    {
        Result<JsonElement> answer = await obs.AskAsync("GetInputMute", new { inputUuid = input.Uuid }, $"el silencio de \"{input.Name}\"", ct).ConfigureAwait(false);
        return answer.Ok ? ObsMessages.Flag(answer.Value, "inputMuted") : answer.Failure;
    }

    private static async Task<Result<string>> ParameterAsync(ObsQueries obs, string category, string name, CancellationToken ct)
    {
        Result<JsonElement> answer = await obs.AskAsync("GetProfileParameter", new { parameterCategory = category, parameterName = name }, $"{category}/{name} del perfil", ct).ConfigureAwait(false);
        return answer.Ok
            ? ObsMessages.Text(answer.Value, "parameterValue") ?? ObsMessages.Text(answer.Value, "defaultParameterValue") ?? ""
            : answer.Failure;
    }

    // El motivo del "no aplica" cuando no quedó nada que mirar: con source, que esa no es de la clase que mira la condición o no está al
    // aire; sin él, que la escena al aire no tiene ninguna (revisión de E5-15: con source decía que no había, y Pantalla tiene la cámara).
    private static string NotOnAir(ObsQueries obs, JsonElement p, string none, string one) =>
        ConditionRules.Reference(p, "source") is { } wanted
            ? $"\"{wanted.Name}\" no es {one} al aire en \"{obs.SceneName}\"."
            : $"no hay {none} en \"{obs.SceneName}\".";

    // Un source que no se entiende como referencia de 02 §4.8 (un número, un texto vacío, un objeto sin name) se leería como si no
    // estuviera, y la condición miraría el mic1 o todas las cámaras sin avisar. Es un problema, como process (revisión de E5-15).
    private static IEnumerable<Problem> CheckSource(JsonElement p)
    {
        if (ObsMessages.Has(p, "source", out JsonElement value) && value.ValueKind != JsonValueKind.Null && ConditionRules.Reference(p, "source") is null)
        {
            yield return ConditionRules.Invalid("source", "source va como el nombre de la fuente, \"Mic/Aux\", o { \"uuid\": \"...\", \"name\": \"Mic/Aux\" }.");
        }
    }

    private static IEnumerable<Problem> RequiredText(JsonElement p, string name) =>
        string.IsNullOrWhiteSpace(ConditionRules.Text(p, name)) ? [ConditionRules.Invalid(name, $"Falta \"{name}\", como texto.")] : [];
}
