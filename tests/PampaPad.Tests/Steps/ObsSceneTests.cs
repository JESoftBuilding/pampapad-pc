using System.Text.Json;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsSceneTests
{
    private const string PantallaUuid = "f3637453-bc14-4e54-b4c5-1b76f4fb4a21";

    private static readonly Invocation Press = new(Guid.NewGuid(), Trigger.Key, "console", "key.1", Gesture.Press, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static ObsScene StepOf(ObsSession session) => new(session.Client, session.State, session.Log);

    [Fact]
    public async Task A_scene_goes_on_air_by_its_uuid()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.scene", "scene": { "uuid": "{{FakeObs.CameraUuid}}", "name": "Cámara" } }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times(ObsScene.RequestType));
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == FakeObs.CameraUuid);
        Assert.Equal("Cámara", session.State.Current.Obs.SceneName);
    }

    [Fact]
    public async Task Written_by_hand_with_the_name_alone_it_is_found_by_name()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.scene", "scene": "Banco" }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == FakeObs.BancoUuid);
    }

    [Fact]
    public async Task A_uuid_that_is_gone_falls_back_to_the_name_and_says_so_in_the_log()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Invocation press = Press with { RunId = Guid.NewGuid() };

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.scene", "scene": { "uuid": "00000000-0000-0000-0000-000000000000", "name": "Banco" } }"""), press, CancellationToken.None);

        Assert.Null(result.Failure);
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == FakeObs.BancoUuid);
        Assert.Contains(session.Log.Recent(), entry => entry.RunId == press.RunId && entry.Message.Contains("se encontró por nombre", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_scene_renamed_in_obs_is_still_found_by_uuid()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await session.Obs.RenameSceneAsync(FakeObs.BancoUuid, "Banco de trabajo");
        await Poll.Until(() => session.State.Current.Obs.Scenes.Contains(new ObsItem(FakeObs.BancoUuid, "Banco de trabajo")));

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.scene", "scene": { "uuid": "{{FakeObs.BancoUuid}}", "name": "Banco" } }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        await Poll.Until(() => session.State.Current.Obs.SceneName == "Banco de trabajo");
    }

    [Fact]
    public async Task A_scene_removed_in_obs_fails_without_sending_anything()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await session.Obs.RemoveSceneAsync(FakeObs.BancoUuid);
        await Poll.Until(() => session.State.Current.Obs.Scenes.All(scene => scene.Uuid != FakeObs.BancoUuid));

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.scene", "scene": { "uuid": "{{FakeObs.BancoUuid}}", "name": "Banco" } }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsRefNotFound, result.Failure?.Code);
        Assert.Equal("OBS no tiene la escena \"Banco\": no se mandó nada.", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times(ObsScene.RequestType));
    }

    [Fact]
    public async Task A_scene_that_vanished_without_notice_is_not_found_by_obs_either()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        session.Obs.ForgetScene(FakeObs.BancoUuid);         // OBS la perdió y el programa todavía la tiene en la lista

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.scene", "scene": "Banco" }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsRefNotFound, result.Failure?.Code);
        Assert.Equal(1, session.Obs.Times(ObsScene.RequestType));
    }

    [Fact]
    public async Task The_scene_already_on_air_is_asked_anyway_and_ends_well()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.scene", "scene": { "uuid": "{{PantallaUuid}}", "name": "Pantalla" } }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times(ObsScene.RequestType));
        Assert.Equal(PantallaUuid, session.State.Current.Obs.SceneUuid);
    }

    [Fact]
    public async Task A_step_cancelled_before_sending_sends_nothing()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        using var cut = new CancellationTokenSource();
        cut.Cancel();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.scene", "scene": "Banco" }"""), Press, cut.Token);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times(ObsScene.RequestType));
    }

    [Fact]
    public async Task Without_a_link_nothing_is_sent()
    {
        await using var offline = new OfflineObs();
        var scene = new ObsScene(offline.Client, offline.State, offline.Log);

        StepResult result = await scene.RunAsync(Parse("""{ "type": "obs.scene", "scene": "Banco" }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal("Sin OBS: no se mandó el cambio de escena.", result.Failure?.Message);
    }

    [Theory]
    [InlineData("""{ "type": "obs.scene" }""", "scene")]
    [InlineData("""{ "type": "obs.scene", "scene": { "uuid": "f3637453-bc14-4e54-b4c5-1b76f4fb4a21" } }""", "scene")]
    [InlineData("""{ "type": "obs.scene", "scene": "" }""", "scene")]
    [InlineData("""{ "type": "obs.scene", "scene": 5 }""", "")]
    public async Task A_scene_without_a_name_is_a_problem_of_the_step(string json, string where)
    {
        await using var offline = new OfflineObs();
        var scene = new ObsScene(offline.Client, offline.State, offline.Log);

        Problem problem = Assert.Single(scene.Validate(Parse(json)));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    [Fact]
    public async Task A_good_step_has_no_problems_and_tolerates_what_it_does_not_know()
    {
        await using var offline = new OfflineObs();
        var scene = new ObsScene(offline.Client, offline.State, offline.Log);

        Assert.Empty(scene.Validate(Parse($$"""{ "type": "obs.scene", "scene": { "uuid": "{{PantallaUuid}}", "name": "Pantalla", "inventado": 1 } }""")));
    }
}
