using System.Text.Json.Nodes;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

/// <summary>El OBS falso contesta lo que lee el chequeo previo como lo midió el relevamiento del 2026-10-13 (03 §7 y §9).</summary>
public class FakeObsPreflightTests
{
    private static async Task<RawObs> OpenAsync(FakeObs fake)
    {
        RawObs raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();
        return raw;
    }

    [Fact]
    public async Task The_items_of_pantalla_are_the_measured_ones_with_the_camera_two_pieces_inside()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var pantalla = await raw.AskAsync("1", "GetSceneItemList", new { sceneUuid = fake.SceneUuid });
        var small = await raw.AskAsync("2", "GetSceneItemList", new { sceneName = "Cámara chica" });
        var unknown = await raw.AskAsync("3", "GetSceneItemList", new { sceneUuid = "00000000-0000-0000-0000-000000000000" });

        JsonArray items = pantalla["responseData"]!["sceneItems"]!.AsArray();
        Assert.Equal(7, items.Count);
        Assert.Contains(items, item => (string?)item!["sourceName"] == "Cámara chica" && (string?)item["sourceType"] == "OBS_SOURCE_TYPE_SCENE");
        Assert.Equal("Cámara chica (recorte)", (string?)small["responseData"]!["sceneItems"]![0]!["sourceName"]);
        Assert.Equal(600, (int)unknown["requestStatus"]!["code"]!);
    }

    [Fact]
    public async Task The_canvas_the_profile_and_the_record_format_are_the_measured_ones()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var video = await raw.AskAsync("1", "GetVideoSettings");
        var profiles = await raw.AskAsync("2", "GetProfileList");
        var mode = await raw.AskAsync("3", "GetProfileParameter", new { parameterCategory = "Output", parameterName = "Mode" });
        var format = await raw.AskAsync("4", "GetProfileParameter", new { parameterCategory = "AdvOut", parameterName = "RecFormat2" });

        Assert.Equal(1920, (int)video["responseData"]!["baseWidth"]!);
        Assert.Equal(1080, (int)video["responseData"]!["baseHeight"]!);
        Assert.Equal("Sin Título", (string?)profiles["responseData"]!["currentProfileName"]);
        Assert.Equal("Advanced", (string?)mode["responseData"]!["parameterValue"]);
        Assert.Equal("Simple", (string?)mode["responseData"]!["defaultParameterValue"]);
        Assert.Equal("hybrid_mp4", (string?)format["responseData"]!["parameterValue"]);
    }

    [Fact]
    public async Task The_screen_capture_points_to_a_listed_monitor_until_the_monitor_leaves()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var settings = await raw.AskAsync("1", "GetInputSettings", new { inputUuid = FakeObs.ScreenCaptureUuid });
        var listed = await raw.AskAsync("2", "GetInputPropertiesListPropertyItems", new { inputUuid = FakeObs.ScreenCaptureUuid, propertyName = "monitor_id" });
        fake.Monitors.Clear();
        var gone = await raw.AskAsync("3", "GetInputPropertiesListPropertyItems", new { inputUuid = FakeObs.ScreenCaptureUuid, propertyName = "monitor_id" });

        Assert.Equal(FakeObs.MonitorId, (string?)settings["responseData"]!["inputSettings"]!["monitor_id"]);
        Assert.Equal(FakeObs.MonitorId, (string?)listed["responseData"]!["propertyItems"]![0]!["itemValue"]);
        Assert.Empty(gone["responseData"]!["propertyItems"]!.AsArray());
    }

    [Fact]
    public async Task The_camera_is_muted_without_tracks_and_the_phone_has_all_six()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var muted = await raw.AskAsync("1", "GetInputMute", new { inputUuid = FakeObs.CameraInputUuid });
        var camera = await raw.AskAsync("2", "GetInputAudioTracks", new { inputUuid = FakeObs.CameraInputUuid });
        var phone = await raw.AskAsync("3", "GetInputAudioTracks", new { inputUuid = FakeObs.CeluUuid });
        var capture = await raw.AskAsync("4", "GetInputAudioTracks", new { inputUuid = FakeObs.ScreenCaptureUuid });

        Assert.True((bool)muted["responseData"]!["inputMuted"]!);
        Assert.All(camera["responseData"]!["inputAudioTracks"]!.AsObject(), track => Assert.False((bool)track.Value!));
        Assert.All(phone["responseData"]!["inputAudioTracks"]!.AsObject(), track => Assert.True((bool)track.Value!));
        Assert.Equal(604, (int)capture["requestStatus"]!["code"]!);
    }

    [Fact]
    public async Task What_is_reached_from_the_scene_on_air_is_active_and_the_rest_is_not()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var camera = await raw.AskAsync("1", "GetSourceActive", new { sourceUuid = FakeObs.CameraInputUuid });
        var phone = await raw.AskAsync("2", "GetSourceActive", new { sourceUuid = FakeObs.CeluUuid });
        var hidden = await raw.AskAsync("3", "GetSourceActive", new { sourceUuid = "cd627aa1-7d7b-4ae4-9c9e-0a4973b38d0d" });     // "Marca de agua", oculta en Pantalla
        var frame = await raw.AskAsync("4", "GetSourceActive", new { sourceUuid = "2a919cb4-c22d-4f5d-a196-3b53fdd4e22d" });      // "Marco cámara chica", oculto en Pantalla y visible en Cámara chica

        Assert.True((bool)camera["responseData"]!["videoActive"]!);
        Assert.False((bool)phone["responseData"]!["videoActive"]!);
        Assert.False((bool)hidden["responseData"]!["videoActive"]!);
        Assert.True((bool)frame["responseData"]!["videoActive"]!);
    }

    [Fact]
    public async Task The_phone_without_its_window_has_no_screenshot_and_measures_zero()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var shot = await raw.AskAsync("1", "GetSourceScreenshot", new { sourceUuid = FakeObs.CeluUuid, imageFormat = "bmp", imageWidth = 160 });
        var closed = await raw.AskAsync("2", "GetSceneItemList", new { sceneUuid = FakeObs.BancoUuid });
        fake.MirrorOpen = true;
        var open = await raw.AskAsync("3", "GetSceneItemList", new { sceneUuid = FakeObs.BancoUuid });

        Assert.Equal(702, (int)shot["requestStatus"]!["code"]!);
        Assert.Equal(FakeObs.NoScreenshot, (string?)shot["requestStatus"]!["comment"]);
        Assert.Equal(0.0, (double)closed["responseData"]!["sceneItems"]![0]!["sceneItemTransform"]!["sourceWidth"]!);
        Assert.Equal(1823.0, (double)open["responseData"]!["sceneItems"]![0]!["sceneItemTransform"]!["sourceWidth"]!);
    }

    // Medido el 2026-10-13: el micrófono, el audio del escritorio y los stings dan 702 (03 §9, "Rechazos medidos").
    [Theory]
    [InlineData(FakeObs.MicUuid)]
    [InlineData("364259ab-1ec2-4e92-9a4e-5faca2f47d6f")]
    [InlineData(FakeObs.StingUuid)]
    public async Task An_input_with_only_audio_has_no_screenshot(string uuid)
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var shot = await raw.AskAsync("1", "GetSourceScreenshot", new { sourceUuid = uuid, imageFormat = "bmp", imageWidth = 160 });

        Assert.Equal(702, (int)shot["requestStatus"]!["code"]!);
        Assert.Equal(FakeObs.NoScreenshot, (string?)shot["requestStatus"]!["comment"]);
    }

    [Fact]
    public async Task The_screenshot_of_the_camera_is_a_160_by_90_bmp()
    {
        await using var fake = FakeObs.Start();
        await using RawObs raw = await OpenAsync(fake);

        var shot = await raw.AskAsync("1", "GetSourceScreenshot", new { sourceUuid = FakeObs.CameraInputUuid, imageFormat = "bmp", imageWidth = 160 });
        string data = (string)shot["responseData"]!["imageData"]!;
        byte[] bytes = Convert.FromBase64String(data["data:image/bmp;base64,".Length..]);

        Assert.StartsWith("data:image/bmp;base64,", data, StringComparison.Ordinal);
        Assert.Equal(43_254, bytes.Length);                    // lo medido: 43.254 bytes (03 §7)
        Assert.Equal(160, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(90, BitConverter.ToInt32(bytes, 22));
    }
}
