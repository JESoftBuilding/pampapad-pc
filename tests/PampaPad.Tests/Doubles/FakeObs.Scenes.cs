using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

// Las escenas y las entradas del OBS falso, y los pedidos de la etapa 4 que las tocan (03 §9). El inventario es el del respaldo de la
// colección del 2026-10-15 (Areas/marca-personal/obs/respaldo/scenes_Sin_Título.json): 16 escenas, cuatro de ellas piezas, con su uuid real.
public sealed partial class FakeObs
{
    public const string CameraUuid = "3da67a13-6704-4c87-9e09-c8be28efdf20";
    public const string BancoUuid = "900ebc25-03ef-443e-80cc-2ad8991268ff";
    public const string ScreenCaptureUuid = "2f658171-ce61-4501-aacd-8d405ede9f53";
    public const string StingUuid = "de40d097-8990-4081-a027-f1601a3238bc";

    /// <summary>Inventada: está para probar que la clave de transmisión no sale de un paso ni llega al registro (03 §9, "Secreto").</summary>
    public const string StreamKey = "clave-inventada-de-prueba";

    private const string NoAudio = "The specified input does not support audio.";      // medido el 2026-10-13 con GetInputMute
    private const int TransitionsIntent = 16;

    // En el orden de sceneIndex: 0 es la de más abajo del panel de OBS (03 §1).
    private readonly List<(string Name, string Uuid)> _scenes =
    [
        ("Intro", "49ecf855-9581-4dc8-a4d0-4bd0aef50c29"),
        ("Banco", BancoUuid),
        ("Cámara (zoom)", "11b848c4-d5c6-4e0a-9746-a316341ab7db"),
        ("Marca de agua", "cd627aa1-7d7b-4ae4-9c9e-0a4973b38d0d"),
        ("Vertical SolidWorks", "67178d64-00f6-4b61-be02-7364b8f5cfe3"),
        ("Cámara", CameraUuid),
        ("Vertical Pantalla + Banco", "d0b27da7-0fd1-463d-bee2-e86e6ff4b3a9"),
        ("Ya arrancamos", "f221061d-d50a-4904-be00-a381efe74757"),
        ("Ya vuelvo", "e231ef9c-23aa-4957-a74b-27181da1d8cf"),
        ("Fin", "ed2f1482-848c-4500-840d-807aee465d1f"),
        ("Vertical Cámara", "8307a333-485e-404c-bea6-f9f91dd11dd1"),
        ("Pantalla", "f3637453-bc14-4e54-b4c5-1b76f4fb4a21"),
        ("Barra inferior", "05f2b10f-d5c3-47e3-bff6-dd006bc16244"),
        ("Cámara chica (recorte)", "7cc479f1-b7eb-4feb-918e-373bcbda8d21"),
        ("Cámara chica", "7e0319cb-a8d6-4b9b-a7ef-c68559e85d12"),
        ("Pantalla + Cámara", "5bcf806a-9dac-430c-a437-3eab544e8419"),
    ];

    // Las entradas que usan las pruebas, con lo medido el 2026-10-13: "Captura de pantalla" no tiene audio.
    private readonly List<FakeInput> _inputs =
    [
        new("Audio del escritorio", "364259ab-1ec2-4e92-9a4e-5faca2f47d6f", "wasapi_output_capture", HasAudio: true),
        new(MicName, MicUuid, "wasapi_input_capture", HasAudio: true),
        new("Captura de pantalla", ScreenCaptureUuid, "monitor_capture", HasAudio: false),
        new("Sting triste", StingUuid, "ffmpeg_source", HasAudio: true),
    ];

    // El silencio de las entradas que no son el micrófono: el del micrófono es MicMuted.
    private readonly Dictionary<string, bool> _muted = [];

    /// <summary>Lo que tarda CurrentProgramSceneChanged después del pedido. Medido: 300 ms con Desvanecimiento y 19 ms con Corte (E0-1).</summary>
    public TimeSpan TransitionDelay { get; set; }

    public IReadOnlyList<string> SceneNames
    {
        get
        {
            lock (_sync)
            {
                return [.. _scenes.Select(scene => scene.Name)];
            }
        }
    }

    public bool IsMuted(string uuid)
    {
        lock (_sync)
        {
            return uuid == MicUuid ? MicMuted : _muted.GetValueOrDefault(uuid);
        }
    }

    // Como si se hiciera desde la ventana de OBS. Cada cambio manda su evento (03 §1); ninguno se provocó en el OBS real (03 Incierto).
    public Task CreateSceneAsync(string name, string uuid)
    {
        lock (_sync)
        {
            _scenes.Add((name, uuid));
        }

        return EmitAsync("SceneCreated", ScenesIntent, new JsonObject { ["isGroup"] = false, ["sceneName"] = name, ["sceneUuid"] = uuid });
    }

    public Task RemoveSceneAsync(string uuid)
    {
        string name;
        lock (_sync)
        {
            name = _scenes[_scenes.FindIndex(scene => scene.Uuid == uuid)].Name;
            _scenes.RemoveAll(scene => scene.Uuid == uuid);
        }

        return EmitAsync("SceneRemoved", ScenesIntent, new JsonObject { ["isGroup"] = false, ["sceneName"] = name, ["sceneUuid"] = uuid });
    }

    public Task RenameSceneAsync(string uuid, string name)
    {
        string old;
        lock (_sync)
        {
            int at = _scenes.FindIndex(scene => scene.Uuid == uuid);
            old = _scenes[at].Name;
            _scenes[at] = (name, uuid);
            if (SceneUuid == uuid)
            {
                SceneName = name;
            }
        }

        return EmitAsync("SceneNameChanged", ScenesIntent, new JsonObject { ["oldSceneName"] = old, ["sceneName"] = name, ["sceneUuid"] = uuid });
    }

    public Task RemoveInputAsync(string uuid)
    {
        string name;
        lock (_sync)
        {
            name = _inputs.First(input => input.Uuid == uuid).Name;
            _inputs.RemoveAll(input => input.Uuid == uuid);
        }

        return EmitAsync("InputRemoved", InputsIntent, new JsonObject { ["inputName"] = name, ["inputUuid"] = uuid });
    }

    /// <summary>La escena desaparece sin que OBS avise: lo que pasa si se borra entre que el programa la busca y que llega el pedido.</summary>
    public void ForgetScene(string uuid)
    {
        lock (_sync)
        {
            _scenes.RemoveAll(scene => scene.Uuid == uuid);
        }
    }

    private JsonObject SceneList() => new()
    {
        ["currentPreviewSceneName"] = null,
        ["currentPreviewSceneUuid"] = null,
        ["currentProgramSceneName"] = SceneName,
        ["currentProgramSceneUuid"] = SceneUuid,
        ["scenes"] = new JsonArray(_scenes.Select((scene, index) => (JsonNode?)Scene(index, scene.Name, scene.Uuid)).ToArray()),
    };

    private JsonObject InputList() => new()
    {
        ["inputs"] = new JsonArray(_inputs.Select(input => (JsonNode?)Input(input.Name, input.Uuid, input.Kind)).ToArray()),
    };

    // Medido (E0-1): 100 y, al pasar la transición, CurrentProgramSceneChanged; con la escena que ya está al aire, 100 y ningún evento.
    // SceneTransitionStarted va con la intención 16 (Transitions), que el programa no pide (03 §6): no le llega.
    private Reply SetProgramScene(JsonObject? data)
    {
        var uuid = (string?)data?["sceneUuid"];
        var name = (string?)data?["sceneName"];
        int at = _scenes.FindIndex(scene => uuid is not null ? scene.Uuid == uuid : scene.Name == name);
        if (at < 0)
        {
            return Reply.Fail(600, $"No source was found by the name of `{uuid ?? name}`.");
        }

        var (sceneName, sceneUuid) = _scenes[at];
        if (sceneUuid == SceneUuid)
        {
            return Reply.Ok();
        }

        SceneName = sceneName;
        SceneUuid = sceneUuid;
        return Reply.Ok()
            .Then("SceneTransitionStarted", TransitionsIntent, new JsonObject { ["transitionName"] = "Desvanecimiento" })
            .Then("CurrentProgramSceneChanged", ScenesIntent, new JsonObject { ["sceneName"] = sceneName, ["sceneUuid"] = sceneUuid }, TransitionDelay);
    }

    // Medido por uuid (E0-1): 100 e InputMuteStateChanged a los 1,3 ms. Si OBS avisa también cuando el estado no cambia no se midió:
    // el doble avisa siempre.
    private Reply SetInputMute(JsonObject? data)
    {
        if (FindInput(data) is not { } input)
        {
            return Reply.Fail(600, $"No source was found by the name of `{(string?)data?["inputUuid"] ?? (string?)data?["inputName"]}`.");
        }

        if (!input.HasAudio)
        {
            return Reply.Fail(604, NoAudio);
        }

        var muted = (bool?)data?["inputMuted"] ?? false;
        if (input.Uuid == MicUuid)
        {
            MicMuted = muted;
        }
        else
        {
            _muted[input.Uuid] = muted;
        }

        return Reply.Ok().Then("InputMuteStateChanged", InputsIntent, new JsonObject { ["inputMuted"] = muted, ["inputName"] = input.Name, ["inputUuid"] = input.Uuid });
    }

    private Reply InputMute(JsonObject? data)
    {
        if (FindInput(data) is not { } input)
        {
            return Reply.Fail(600, $"No source was found by the name of `{(string?)data?["inputUuid"] ?? (string?)data?["inputName"]}`.");
        }

        return input.HasAudio
            ? Reply.Ok(new JsonObject { ["inputMuted"] = input.Uuid == MicUuid ? MicMuted : _muted.GetValueOrDefault(input.Uuid) })
            : Reply.Fail(604, NoAudio);
    }

    // OBS tiene Twitch como servicio (05 §5); el relevamiento del 2026-10-13 vio también bwtest en true.
    private static JsonObject StreamServiceSettings() => new()
    {
        ["streamServiceSettings"] = new JsonObject { ["bwtest"] = true, ["key"] = StreamKey, ["server"] = "auto", ["service"] = "Twitch" },
        ["streamServiceType"] = "rtmp_common",
    };

    private FakeInput? FindInput(JsonObject? data)
    {
        var uuid = (string?)data?["inputUuid"];
        var name = (string?)data?["inputName"];
        return _inputs.FirstOrDefault(input => uuid is not null ? input.Uuid == uuid : input.Name == name);
    }

    private sealed record FakeInput(string Name, string Uuid, string Kind, bool HasAudio);
}
