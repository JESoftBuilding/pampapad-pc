using System.Text.Json;
using PampaPad.Core;
using static PampaPad.Tests.ProjectionCase;

namespace PampaPad.Tests;

/// <summary>
/// console.preview (plan de la 9b, decisión 13; E9b-6): el ConsoleFrame que saldría por el cable con el borrador y un estado de muestra,
/// armado en C# sin OBS. La página sólo lo dibuja.
/// </summary>
public sealed class ConsolePreviewTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private Bridge _bridge = null!;

    public Task InitializeAsync()
    {
        _bridge = new Bridge(_rig.State, _rig.Log);
        KeysRequests.Register(_bridge, new EditSources(_rig.Library, _rig.State, _rig.Log, _rig.Executor, [_rig.Marker], [], _rig.Dispatcher, new FakeWindows(), null));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _rig.DisposeAsync();

    [Fact]
    public void Recording_on_a_page_of_scenes_puts_its_first_scene_on_air_and_the_clock_at_the_sample_time()
    {
        ConsoleFrame frame = ConsolePreview.Frame(Sample, "toma", ConsolePreviewSample.Recording, ObsState.Initial);

        Assert.Equal(ConsoleRecording.Recording, frame.Screen.Recording);
        Assert.True(Bit(frame.Screen.ActiveMask, 1)); // grabando, la tecla de grabar está activa
        Assert.True(Bit(frame.Screen.ActiveMask, 3)); // Cámara, la primera escena de la página
        Assert.False(Bit(frame.Screen.ActiveMask, 4));
        Assert.Equal(2052, frame.Screen.RecordedSec);
        Assert.Equal("CAMARA", frame.SceneLine.Text);
    }

    [Fact]
    public void Each_sample_gives_its_band()
    {
        ConsoleFrame paused = ConsolePreview.Frame(Sample, "toma", ConsolePreviewSample.Paused, ObsState.Initial);
        ConsoleFrame stopped = ConsolePreview.Frame(Sample, "toma", ConsolePreviewSample.Stopped, ObsState.Initial);
        ConsoleFrame noObs = ConsolePreview.Frame(Sample, "toma", ConsolePreviewSample.NoObs, ObsState.Initial);

        Assert.Equal((ConsoleRecording.Paused, 130), (paused.Screen.Recording, paused.Screen.PausedSec));
        Assert.Equal(ConsoleRecording.Stopped, stopped.Screen.Recording);
        Assert.Equal((ConsoleRecording.NoObs, "NO ESTA ABIERTO", ""), (noObs.Screen.Recording, noObs.SmallLine.Text, noObs.SceneLine.Text));
        Assert.Null(noObs.Alert);
    }

    [Fact]
    public void Without_scene_keys_the_scene_line_is_the_one_obs_has_on_air()
    {
        ObsState live = ObsState.Initial with { Link = ObsLinkState.Ready, SceneName = "Pantalla" };

        ConsoleFrame frame = ConsolePreview.Frame(Sample, "sala", ConsolePreviewSample.Recording, live);

        Assert.Equal("PANTALLA", frame.SceneLine.Text);
        Assert.Equal(new KeyFace(KeyRole.Normal, "LUCES", ""), frame.Key(1));
    }

    [Fact]
    public void A_page_that_does_not_exist_gives_empty_keys_and_does_not_throw()
    {
        ConsoleFrame frame = ConsolePreview.Frame(Sample, "no-existe", ConsolePreviewSample.Recording, ObsState.Initial);

        Assert.All(Enumerable.Range(0, 12), index => Assert.Equal(KeyFace.Empty, frame.Key(index)));
    }

    [Fact]
    public async Task The_request_draws_the_draft_it_receives_not_the_saved_library()
    {
        using JsonDocument reply = await PreviewAsync("""{"config":{"version":1,"pages":[{"id":"p","name":"P","keys":[{"position":2,"label":"Cámara","shortLabel":["Nueva"],"action":"cam"}]}],"actions":[{"id":"cam","steps":[{"type":"obs.scene","scene":"Cámara"}]}]},"state":"recording","page":"p"}""");

        JsonElement data = reply.RootElement.GetProperty("data");
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Equal("NUEVA", data.GetProperty("keys")[1].GetProperty("line1").GetString());
        Assert.Equal("scene", data.GetProperty("keys")[1].GetProperty("role").GetString());
        Assert.Equal(2, data.GetProperty("screen").GetProperty("activeMask").GetInt32());
        Assert.Equal("recording", data.GetProperty("screen").GetProperty("recording").GetString());
        Assert.Equal(0, data.GetProperty("screen").GetProperty("flags").GetInt32()); // los bits van en número, como en el cable
    }

    [Fact]
    public async Task A_draft_that_cannot_be_read_is_config_invalid_and_the_mcp_has_no_door()
    {
        using JsonDocument missing = await PreviewAsync("""{"state":"stopped"}""");
        using JsonDocument nullPage = await PreviewAsync("""{"config":{"version":1,"pages":[null]},"state":"stopped"}""");
        using JsonDocument mcp = await PreviewAsync("""{"config":{"version":1},"state":"stopped"}""", Caller.Mcp);

        Assert.Equal(ErrorCodes.ConfigInvalid, missing.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.ConfigInvalid, nullPage.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.NoPermission, mcp.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<JsonDocument> PreviewAsync(string data, Caller caller = Caller.Window) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"console.preview","data":{{data}}}""", caller, CancellationToken.None));
}
