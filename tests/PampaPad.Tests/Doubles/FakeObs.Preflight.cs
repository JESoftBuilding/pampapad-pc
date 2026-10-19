using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

/// <summary>
/// Lo que lee el chequeo previo (03 §7 y §9): los elementos de cada escena tal como los midió el relevamiento del 2026-10-13
/// (Data/obs-scene-items.json), el lienzo, el perfil, los ajustes de las entradas de video, la lista de monitores, las pistas de audio, si
/// una fuente está activa y su captura. Activa es lo que se llega a ver desde la escena al aire por elementos visibles, como en OBS.
/// </summary>
public sealed partial class FakeObs
{
    public const string CameraInputUuid = "668dffa0-e0b7-4ae5-b469-a99fd220fb96";
    public const string CeluUuid = "63a7c2d4-051d-4b09-8a60-91bd90d1998e";
    public const string VerticalSolidWorksUuid = "67178d64-00f6-4b61-be02-7364b8f5cfe3";
    public const string MonitorId = @"\\?\DISPLAY#AUS2752#5&2ab973b7&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    public const string NoScreenshot = "Failed to render screenshot.";       // medido con "Celu (scrcpy)" sin ventana

    private static readonly Lazy<JsonObject> Measured = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "obs-scene-items.json")))!.AsObject());

    private readonly Dictionary<string, JsonArray> _items = [];
    private readonly Dictionary<string, (string Name, JsonArray Items)> _groups = [];
    private readonly Dictionary<string, bool[]> _tracks = new()
    {
        [MicUuid] = [true, true, false, false, false, false],
        [CameraInputUuid] = [false, false, false, false, false, false],
        [CeluUuid] = [true, true, true, true, true, true],
        ["364259ab-1ec2-4e92-9a4e-5faca2f47d6f"] = [false, false, false, false, false, false],
        [StingUuid] = [true, false, true, false, false, false],
    };

    public int BaseWidth { get; set; } = 1920;

    public int BaseHeight { get; set; } = 1080;

    public string Profile { get; set; } = "Sin Título";

    public string OutputMode { get; set; } = "Advanced";

    public string RecordFormat { get; set; } = "hybrid_mp4";

    /// <summary>Los monitores que OBS lista hoy para monitor_id. Sacar el de la captura es el ASUS que se fue a la notebook.</summary>
    public List<string> Monitors { get; } = [MonitorId];

    /// <summary>La cámara con la tapa: su captura sale pareja, desvío 0.</summary>
    public bool CameraCovered { get; set; }

    /// <summary>El espejo del celular abierto: "Celu (scrcpy)" mide 1823 x 850 en lugar de 0 x 0 y tiene captura.</summary>
    public bool MirrorOpen { get; set; }

    /// <summary>Los elementos de una escena, para que la prueba los cambie (ocultar uno, agregar otro). Sin datos medidos, vacía.</summary>
    public JsonArray ItemsOf(string sceneUuid)
    {
        lock (_sync)
        {
            return Items(sceneUuid);
        }
    }

    /// <summary>Agrega un grupo con sus elementos a una escena: hoy no hay grupos en OBS (03 §1), así que no hay datos medidos.</summary>
    public void AddGroup(string sceneUuid, string name, string uuid, JsonArray items)
    {
        lock (_sync)
        {
            _groups[uuid] = (name, items);
            Items(sceneUuid).Add(new JsonObject
            {
                ["sceneItemId"] = 99,
                ["sceneItemEnabled"] = true,
                ["sourceName"] = name,
                ["sourceUuid"] = uuid,
                ["sourceType"] = "OBS_SOURCE_TYPE_SCENE",
                ["inputKind"] = null,
                ["isGroup"] = true,
                ["sceneItemTransform"] = Transform(BaseWidth, BaseHeight),
            });
        }
    }

    private static JsonObject Transform(double width, double height) => new()
    {
        ["sourceWidth"] = width,
        ["sourceHeight"] = height,
        ["scaleX"] = 1.0,
        ["scaleY"] = 1.0,
        ["cropLeft"] = 0,
        ["cropRight"] = 0,
        ["cropTop"] = 0,
        ["cropBottom"] = 0,
        ["boundsType"] = "OBS_BOUNDS_NONE",
        ["boundsWidth"] = 0.0,
        ["boundsHeight"] = 0.0,
        ["positionX"] = 0.0,
        ["positionY"] = 0.0,
        ["alignment"] = 5,
        ["rotation"] = 0.0,
    };

    private JsonArray Items(string uuid)
    {
        if (!_items.TryGetValue(uuid, out JsonArray? items))
        {
            items = Measured.Value[uuid]?["sceneItems"]?.DeepClone().AsArray() ?? [];
            _items[uuid] = items;
        }

        return items;
    }

    // Una copia de los elementos, con el espejo del celular medido como esté.
    private JsonArray Answered(JsonArray items)
    {
        var copy = items.DeepClone().AsArray();
        foreach (JsonNode? item in copy)
        {
            if ((string?)item?["sourceUuid"] == CeluUuid && MirrorOpen)
            {
                item!["sceneItemTransform"]!["sourceWidth"] = 1823.0;
                item["sceneItemTransform"]!["sourceHeight"] = 850.0;
            }
        }

        return copy;
    }

    private Reply SceneItemList(JsonObject? data)
    {
        var uuid = (string?)data?["sceneUuid"];
        var name = (string?)data?["sceneName"];
        int at = _scenes.FindIndex(scene => uuid is not null ? scene.Uuid == uuid : scene.Name == name);
        return at < 0
            ? Reply.Fail(600, $"No source was found by the name of `{uuid ?? name}`.")
            : Reply.Ok(new JsonObject { ["sceneItems"] = Answered(Items(_scenes[at].Uuid)) });
    }

    private Reply GroupItemList(JsonObject? data)
    {
        var uuid = (string?)data?["sceneUuid"];
        return uuid is not null && _groups.TryGetValue(uuid, out var group)
            ? Reply.Ok(new JsonObject { ["sceneItems"] = Answered(group.Items) })
            : Reply.Fail(600, $"No source was found by the name of `{uuid}`.");
    }

    private Reply VideoSettings() => Reply.Ok(new JsonObject
    {
        ["baseHeight"] = BaseHeight,
        ["baseWidth"] = BaseWidth,
        ["fpsDenominator"] = 1,
        ["fpsNumerator"] = 60,
        ["outputHeight"] = BaseHeight,
        ["outputWidth"] = BaseWidth,
    });

    private Reply ProfileList() => Reply.Ok(new JsonObject
    {
        ["currentProfileName"] = Profile,
        ["profiles"] = new JsonArray("Reels vertical", "Sin Título"),
    });

    // Medido el 2026-10-13 (perfil_activo): Output/Mode da Advanced con Simple por defecto; RecFormat2 da hybrid_mp4.
    private Reply ProfileParameter(JsonObject? data)
    {
        string key = $"{(string?)data?["parameterCategory"]}/{(string?)data?["parameterName"]}";
        (string? value, string? fallback) = key switch
        {
            "Output/Mode" => (OutputMode, "Simple"),
            "AdvOut/RecFormat2" => (OutputMode == "Advanced" ? RecordFormat : "hybrid_mp4", "hybrid_mp4"),
            "SimpleOutput/RecFormat2" => (OutputMode == "Simple" ? RecordFormat : "hybrid_mp4", "hybrid_mp4"),
            "General/Name" => (Profile, null),
            _ => (null, null),
        };
        return Reply.Ok(new JsonObject { ["defaultParameterValue"] = fallback, ["parameterValue"] = value });
    }

    private Reply InputSettings(JsonObject? data)
    {
        if (FindInput(data) is not { } input)
        {
            return Reply.Fail(600, "No source was found.");
        }

        JsonObject settings = input.Kind switch
        {
            "monitor_capture" => new JsonObject { ["method"] = 1, ["monitor_id"] = MonitorId },
            "window_capture" => new JsonObject { ["client_area"] = true, ["cursor"] = false, ["method"] = 2, ["priority"] = 1, ["window"] = "bancocam:SDL_app:scrcpy.exe" },
            "dshow_input" => new JsonObject { ["last_resolution"] = "1920x1080", ["res_type"] = 1, ["resolution"] = "1920x1080" },
            _ => [],
        };
        return Reply.Ok(new JsonObject { ["inputKind"] = input.Kind, ["inputSettings"] = settings });
    }

    private Reply PropertyItems(JsonObject? data)
    {
        if (FindInput(data) is not { Kind: "monitor_capture" } || (string?)data?["propertyName"] != "monitor_id")
        {
            return Reply.Fail(600, "No source was found.");
        }

        var items = new JsonArray();
        foreach (string monitor in Monitors)
        {
            items.Add(new JsonObject { ["itemEnabled"] = true, ["itemName"] = "Monitor", ["itemValue"] = monitor });
        }

        return Reply.Ok(new JsonObject { ["propertyItems"] = items });
    }

    private Reply AudioTracks(JsonObject? data)
    {
        if (FindInput(data) is not { } input)
        {
            return Reply.Fail(600, "No source was found.");
        }

        if (!input.HasAudio || !_tracks.TryGetValue(input.Uuid, out bool[]? tracks))
        {
            return Reply.Fail(604, NoAudio);
        }

        var answer = new JsonObject();
        for (int track = 0; track < tracks.Length; track++)
        {
            answer[(track + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)] = tracks[track];
        }

        return Reply.Ok(new JsonObject { ["inputAudioTracks"] = answer });
    }

    private Reply SourceActive(JsonObject? data)
    {
        string? uuid = (string?)data?["sourceUuid"] ?? FindInput(data)?.Uuid;
        bool active = uuid is not null && IsActive(uuid, SceneUuid, []);
        return Reply.Ok(new JsonObject { ["videoActive"] = active, ["videoShowing"] = active });
    }

    // Medido (03 §7): lo que no está al aire sale negro; "Celu (scrcpy)" sin ventana da 702.
    private Reply Screenshot(JsonObject? data)
    {
        string? uuid = (string?)data?["sourceUuid"] ?? FindInput(data)?.Uuid;
        if (uuid == CeluUuid && !MirrorOpen)
        {
            return Reply.Fail(702, NoScreenshot);
        }

        bool flat = uuid is null || !IsActive(uuid, SceneUuid, []) || (uuid == CameraInputUuid && CameraCovered);
        return Reply.Ok(new JsonObject { ["imageData"] = "data:image/bmp;base64," + Convert.ToBase64String(Bmp(flat)) });
    }

    private bool IsActive(string uuid, string sceneUuid, HashSet<string> seen)
    {
        if (uuid == sceneUuid)
        {
            return true;
        }

        if (!seen.Add(sceneUuid))
        {
            return false;
        }

        JsonArray items = _groups.TryGetValue(sceneUuid, out var group) ? group.Items : Items(sceneUuid);
        return items.Any(item => (bool?)item?["sceneItemEnabled"] == true
            && ((string?)item["sourceUuid"] == uuid
                || ((string?)item["sourceType"] == "OBS_SOURCE_TYPE_SCENE" && IsActive(uuid, (string)item["sourceUuid"]!, seen))));
    }

    // Una captura de 160 x 90 en bmp de 24 bits, como la que pide el chequeo: pareja (tapada, o algo que no está al aire) o en degradé.
    private static byte[] Bmp(bool flat)
    {
        const int Width = 160;
        const int Height = 90;
        const int Header = 54;
        var bytes = new byte[Header + (Width * Height * 3)];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(Header).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(Width).CopyTo(bytes, 18);
        BitConverter.GetBytes(Height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte level = flat ? (byte)2 : (byte)(x * 255 / (Width - 1));
                int at = Header + (((y * Width) + x) * 3);
                bytes[at] = level;
                bytes[at + 1] = level;
                bytes[at + 2] = level;
            }
        }

        return bytes;
    }
}
