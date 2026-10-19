using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

/// <summary>Las lecturas del chequeo previo: las fuentes de la escena al aire, el brillo de una captura y cuánto llena una escena.</summary>
public class ObsReadsTests
{
    private static JsonElement Transform(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<CanvasRect> RectsOf(string sceneUuid)
    {
        JsonObject measured = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "obs-scene-items.json")))!.AsObject();
        return [.. measured[sceneUuid]!["sceneItems"]!.AsArray()
            .Where(item => (bool)item!["sceneItemEnabled"]!)
            .Select(item => CanvasCoverage.RectOf(JsonSerializer.SerializeToElement(item!["sceneItemTransform"])))
            .OfType<CanvasRect>()];
    }

    [Fact]
    public async Task The_visible_sources_of_pantalla_reach_the_camera_two_scenes_inside_and_skip_what_is_hidden()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        var queries = new ObsQueries(session.Client, session.State);

        Result<IReadOnlyList<SceneSource>> sources = await queries.VisibleSourcesAsync(CancellationToken.None);

        Assert.True(sources.Ok, sources.Failure?.Message);
        Assert.Contains(sources.Value!, source => source.Uuid == FakeObs.CameraInputUuid && source.InputKind == "dshow_input");
        Assert.Contains(sources.Value!, source => source.Name == "Captura de pantalla" && source.Width == 1920);
        Assert.DoesNotContain(sources.Value!, source => source.Name == "Logo marca de agua");     // "Marca de agua" está oculta en Pantalla
        Assert.Single(sources.Value!, source => source.Uuid == FakeObs.CameraInputUuid);
    }

    [Fact]
    public async Task A_group_is_walked_and_a_scene_inside_itself_does_not_hang()
    {
        await using ObsSession session = await ObsSession.StartAsync(before: obs =>
        {
            obs.AddGroup(obs.SceneUuid, "Grupo", "aaaaaaaa-0000-0000-0000-000000000000", [new JsonObject
            {
                ["sceneItemId"] = 1, ["sceneItemEnabled"] = true, ["sourceName"] = "Celu (scrcpy)", ["sourceUuid"] = FakeObs.CeluUuid,
                ["sourceType"] = "OBS_SOURCE_TYPE_INPUT", ["inputKind"] = "window_capture", ["isGroup"] = null,
                ["sceneItemTransform"] = new JsonObject { ["sourceWidth"] = 0.0, ["sourceHeight"] = 0.0 },
            }]);
            obs.ItemsOf("7e0319cb-a8d6-4b9b-a7ef-c68559e85d12").Add(new JsonObject
            {
                ["sceneItemId"] = 50,
                ["sceneItemEnabled"] = true,
                ["sourceName"] = "Pantalla",
                ["sourceUuid"] = obs.SceneUuid,
                ["sourceType"] = "OBS_SOURCE_TYPE_SCENE",
                ["inputKind"] = null,
                ["isGroup"] = null,
                ["sceneItemTransform"] = new JsonObject(),
            });
        });
        var queries = new ObsQueries(session.Client, session.State);

        Result<IReadOnlyList<SceneSource>> sources = await queries.VisibleSourcesAsync(CancellationToken.None);

        Assert.True(sources.Ok, sources.Failure?.Message);
        Assert.Contains(sources.Value!, source => source.Uuid == FakeObs.CeluUuid && source.Width == 0);
    }

    [Fact]
    public async Task Without_a_link_nothing_is_asked_and_it_is_obs_disconnected()
    {
        await using var offline = new OfflineObs();
        var queries = new ObsQueries(offline.Client, offline.State);

        Result<JsonElement> answer = await queries.AskAsync("GetVideoSettings", null, "el lienzo", CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsDisconnected, answer.Failure?.Code);
        Assert.Equal("Sin OBS: no se pudo leer el lienzo.", answer.Failure?.Message);
    }

    [Fact]
    public void A_flat_capture_has_no_deviation_and_a_gradient_has_a_lot()
    {
        byte[] flat = Bmp(160, 90, (_, _) => 2);
        byte[] gradient = Bmp(160, 90, (x, _) => (byte)(x * 255 / 159));

        Brightness covered = ImageStats.FromBmp(flat)!.Value;
        Brightness open = ImageStats.FromBmp(gradient)!.Value;

        Assert.Equal(2, covered.Mean, 3);
        Assert.Equal(0, covered.Deviation, 3);
        Assert.True(open.Deviation > 50, $"desvío {open.Deviation}");
    }

    [Theory]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("data:image/bmp;base64,no-es-base64")]
    [InlineData("data:image/bmp;base64,AAAA")]
    [InlineData(null)]
    public void What_is_not_a_bmp_of_obs_gives_nothing(string? imageData)
    {
        Assert.Null(ImageStats.FromImageData(imageData));
    }

    [Fact]
    public void The_luminance_weights_red_green_and_blue_like_relevar_obs()
    {
        byte[] red = Bmp(2, 1, (_, _) => 0, blue: 0, green: 0, red: 255);

        Assert.Equal(0.299 * 255, ImageStats.FromBmp(red)!.Value.Mean, 3);
    }

    [Fact]
    public void Vertical_solidworks_fills_less_than_a_third_of_the_horizontal_canvas()
    {
        // 03 §7 dice 31,7 %; con la fórmula de 03 §7 sobre los elementos medidos da 31,78 %.
        double covered = CanvasCoverage.Covered(RectsOf(FakeObs.VerticalSolidWorksUuid), 1920, 1080);

        Assert.Equal(0.3178, covered, 4);
    }

    [Fact]
    public void Pantalla_fills_the_canvas()
    {
        Assert.Equal(1, CanvasCoverage.Covered(RectsOf("f3637453-bc14-4e54-b4c5-1b76f4fb4a21"), 1920, 1080), 6);
    }

    [Fact]
    public void An_item_with_bounds_counts_its_bounds_but_a_source_of_zero_counts_nothing()
    {
        CanvasRect? stretched = CanvasCoverage.RectOf(Transform("""{ "sourceWidth": 1823, "sourceHeight": 850, "scaleX": 1, "scaleY": 1, "cropLeft": 133, "cropRight": 557, "cropTop": 106, "cropBottom": 107, "boundsType": "OBS_BOUNDS_STRETCH", "boundsWidth": 1920, "boundsHeight": 1080, "positionX": 0, "positionY": 0, "alignment": 5 }"""));
        CanvasRect? closed = CanvasCoverage.RectOf(Transform("""{ "sourceWidth": 0, "sourceHeight": 0, "boundsType": "OBS_BOUNDS_STRETCH", "boundsWidth": 1920, "boundsHeight": 1080, "alignment": 5 }"""));

        Assert.Equal(new CanvasRect(0, 0, 1920, 1080), stretched);
        Assert.Null(closed);
    }

    [Theory]
    [InlineData(5, 100, 100)]       // arriba a la izquierda
    [InlineData(0, 50, 75)]         // centrado
    [InlineData(10, 0, 50)]         // abajo a la derecha
    public void The_position_is_the_one_of_the_alignment_point(int alignment, double left, double top)
    {
        CanvasRect? rect = CanvasCoverage.RectOf(Transform($$"""{ "sourceWidth": 100, "sourceHeight": 50, "scaleX": 1, "scaleY": 1, "boundsType": "OBS_BOUNDS_NONE", "positionX": 100, "positionY": 100, "alignment": {{alignment}} }"""));

        Assert.Equal(new CanvasRect(left, top, left + 100, top + 50), rect);
    }

    [Fact]
    public void Overlaps_count_once_and_what_falls_outside_the_canvas_does_not_count()
    {
        double covered = CanvasCoverage.Covered([new CanvasRect(0, 0, 100, 100), new CanvasRect(50, 50, 150, 150), new CanvasRect(-50, 0, 0, 10)], 200, 200);

        Assert.Equal((10000 + 10000 - 2500) / 40000.0, covered, 6);
    }

    private static byte[] Bmp(int width, int height, Func<int, int, byte> level, byte? blue = null, byte? green = null, byte? red = null)
    {
        int stride = ((width * 3) + 3) / 4 * 4;
        var bytes = new byte[54 + (stride * height)];
        Encoding.ASCII.GetBytes("BM").CopyTo(bytes, 0);
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = 54 + (y * stride) + (x * 3);
                byte value = level(x, y);
                bytes[at] = blue ?? value;
                bytes[at + 1] = green ?? value;
                bytes[at + 2] = red ?? value;
            }
        }

        return bytes;
    }
}
