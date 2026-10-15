using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsMirrorListsTests
{
    private const string MicUuid = "506b0448-d6b6-443e-83d9-5dd76fbf1d60";
    private const string BancoUuid = "900ebc25-03ef-443e-80cc-2ad8991268ff";
    private const string PantallaUuid = "f3637453-bc14-4e54-b4c5-1b76f4fb4a21";

    private static readonly ObsState Ready = ObsState.Initial with
    {
        Link = ObsLinkState.Ready,
        SceneName = "Pantalla",
        SceneUuid = PantallaUuid,
        MicName = "Mic/Aux",
        MicUuid = MicUuid,
        Scenes = [new(PantallaUuid, "Pantalla"), new(BancoUuid, "Banco")],
        Inputs = [new(MicUuid, "Mic/Aux")],
    };

    private static JsonElement Data(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static ObsResponse Ok(string data) => new(true, 100, null, Data(data));

    private static ObsEvent Event(string type, int intent, string data) => new(type, intent, Data(data));

    [Fact]
    public void The_initial_read_keeps_every_scene_and_every_input()
    {
        ObsResponse record = Ok("""{"outputActive":false,"outputPaused":false,"outputDuration":0}""");
        ObsResponse stream = Ok("""{"outputActive":false,"outputReconnecting":false}""");
        ObsResponse scenes = Ok($$"""{"currentProgramSceneName":"Pantalla","currentProgramSceneUuid":"{{PantallaUuid}}","scenes":[{"sceneIndex":0,"sceneName":"Banco","sceneUuid":"{{BancoUuid}}"},{"sceneIndex":1,"sceneName":"Pantalla","sceneUuid":"{{PantallaUuid}}"}]}""");
        ObsResponse special = Ok("""{"mic1":"Mic/Aux","mic2":null}""");
        ObsResponse inputs = Ok($$"""{"inputs":[{"inputKind":"wasapi_input_capture","inputName":"Mic/Aux","inputUuid":"{{MicUuid}}"},{"inputKind":"monitor_capture","inputName":"Captura de pantalla","inputUuid":"2f658171-ce61-4501-aacd-8d405ede9f53"}]}""");

        ObsState obs = ObsMirror.ApplyInitial(ObsState.Initial, [record, stream, scenes, special, inputs]);

        Assert.Equal(new[] { "Banco", "Pantalla" }, obs.Scenes.Select(scene => scene.Name));
        Assert.Equal(new[] { BancoUuid, PantallaUuid }, obs.Scenes.Select(scene => scene.Uuid));
        Assert.Equal(new[] { "Mic/Aux", "Captura de pantalla" }, obs.Inputs.Select(input => input.Name));
        Assert.Equal(MicUuid, obs.MicUuid);
    }

    [Fact]
    public void A_failed_list_in_the_initial_read_leaves_the_one_there_was()
    {
        ObsResponse failed = new(false, 600, "No source was found", default);
        ObsResponse record = Ok("""{"outputActive":false,"outputPaused":false,"outputDuration":0}""");
        ObsResponse stream = Ok("""{"outputActive":false,"outputReconnecting":false}""");
        ObsResponse special = Ok("""{"mic1":"Mic/Aux"}""");

        ObsState obs = ObsMirror.ApplyInitial(Ready, [record, stream, failed, special, failed]);

        Assert.Same(Ready.Scenes, obs.Scenes);
        Assert.Same(Ready.Inputs, obs.Inputs);
    }

    [Fact]
    public void A_created_scene_joins_the_list_once_and_a_group_does_not()
    {
        ObsEvent created = Event("SceneCreated", 4, """{"isGroup":false,"sceneName":"Nueva","sceneUuid":"11111111-1111-1111-1111-111111111111"}""");
        ObsEvent group = Event("SceneCreated", 4, """{"isGroup":true,"sceneName":"Grupo","sceneUuid":"22222222-2222-2222-2222-222222222222"}""");

        ObsState obs = ObsMirror.Apply(ObsMirror.Apply(ObsMirror.Apply(Ready, created), created), group);

        Assert.Equal(new[] { "Pantalla", "Banco", "Nueva" }, obs.Scenes.Select(scene => scene.Name));
    }

    [Fact]
    public void A_removed_scene_leaves_the_list()
    {
        ObsState obs = ObsMirror.Apply(Ready, Event("SceneRemoved", 4, $$"""{"isGroup":false,"sceneName":"Banco","sceneUuid":"{{BancoUuid}}"}"""));

        Assert.Equal(new[] { "Pantalla" }, obs.Scenes.Select(scene => scene.Name));
    }

    [Fact]
    public void A_renamed_scene_keeps_its_uuid_and_the_one_on_air_takes_the_new_name()
    {
        ObsState banco = ObsMirror.Apply(Ready, Event("SceneNameChanged", 4, $$"""{"oldSceneName":"Banco","sceneName":"Banco de trabajo","sceneUuid":"{{BancoUuid}}"}"""));
        ObsState onAir = ObsMirror.Apply(Ready, Event("SceneNameChanged", 4, $$"""{"oldSceneName":"Pantalla","sceneName":"Monitor","sceneUuid":"{{PantallaUuid}}"}"""));

        Assert.Contains(new ObsItem(BancoUuid, "Banco de trabajo"), banco.Scenes);
        Assert.Equal("Pantalla", banco.SceneName);
        Assert.Equal("Monitor", onAir.SceneName);
        Assert.Equal(PantallaUuid, onAir.SceneUuid);
    }

    [Fact]
    public void A_created_input_joins_the_list()
    {
        ObsState obs = ObsMirror.Apply(Ready, Event("InputCreated", 8, """{"inputKind":"ffmpeg_source","inputName":"Sting nuevo","inputUuid":"33333333-3333-3333-3333-333333333333"}"""));

        Assert.Contains(new ObsItem("33333333-3333-3333-3333-333333333333", "Sting nuevo"), obs.Inputs);
    }

    [Fact]
    public void A_renamed_mic_is_still_the_mic()
    {
        ObsState obs = ObsMirror.Apply(Ready, Event("InputNameChanged", 8, $$"""{"oldInputName":"Mic/Aux","inputName":"Micrófono","inputUuid":"{{MicUuid}}"}"""));
        ObsState muted = ObsMirror.Apply(obs, Event("InputMuteStateChanged", 8, $$"""{"inputMuted":true,"inputName":"Micrófono","inputUuid":"{{MicUuid}}"}"""));

        Assert.Equal("Micrófono", obs.MicName);
        Assert.Contains(new ObsItem(MicUuid, "Micrófono"), obs.Inputs);
        Assert.True(muted.MicMuted);
    }

    [Fact]
    public void A_removed_mic_leaves_no_mic()
    {
        ObsState obs = ObsMirror.Apply(Ready with { MicMuted = true }, Event("InputRemoved", 8, $$"""{"inputName":"Mic/Aux","inputUuid":"{{MicUuid}}"}"""));

        Assert.Empty(obs.Inputs);
        Assert.Null(obs.MicName);
        Assert.Null(obs.MicUuid);
        Assert.False(obs.MicMuted);
    }

    [Fact]
    public void An_event_about_something_that_is_not_in_the_list_changes_nothing()
    {
        ObsState obs = ObsMirror.Apply(Ready, Event("SceneRemoved", 4, """{"isGroup":false,"sceneName":"Otra","sceneUuid":"44444444-4444-4444-4444-444444444444"}"""));

        Assert.Equal(Ready, obs);
    }
}
