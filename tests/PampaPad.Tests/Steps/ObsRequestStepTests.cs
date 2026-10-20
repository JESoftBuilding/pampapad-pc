using System.Text.Json;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsRequestStepTests
{
    private static readonly Invocation Window = new(Guid.NewGuid(), Trigger.Window, null, null, null, null);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static ObsRequestStep StepOf(ObsSession session) => new(session.Client, session.State);

    // Un pedido que saliera igual llega al falso después de que el paso vuelve (el socket manda y recién espera). El falso atiende cada
    // sesión en orden y cuenta antes de contestar: un GetVersion por el mismo socket espera a que haya contado todo lo anterior.
    private static async Task SettleAsync(ObsSession session) =>
        Assert.True((await session.Client.RequestAsync("GetVersion", null, CancellationToken.None)).Ok);

    [Fact]
    public async Task A_read_brings_back_the_data_of_the_answer()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.request", "requestType": "GetSceneList" }"""), Window, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal("Pantalla", result.Data!.Value.GetProperty("currentProgramSceneName").GetString());
        Assert.Equal(16, result.Data!.Value.GetProperty("scenes").GetArrayLength());
    }

    [Fact]
    public async Task The_request_data_goes_as_written()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.request", "requestType": "SetCurrentProgramScene", "requestData": { "sceneName": "Banco" } }"""), Window, CancellationToken.None);

        Assert.Null(result.Failure);
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == FakeObs.BancoUuid);
    }

    [Fact]
    public async Task A_code_that_is_not_100_is_obs_request_failed_with_the_code_and_the_comment()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.request", "requestType": "NoExiste" }"""), Window, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsRequestFailed, result.Failure?.Code);
        Assert.Equal("OBS rechazó el pedido NoExiste (código 204): Your request type is not valid.", result.Failure?.Message);
    }

    [Theory]
    [InlineData("TriggerHotkeyByName")]
    [InlineData("TriggerHotkeyByKeySequence")]
    public async Task The_two_hotkey_requests_are_never_sent(string requestType)
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.request", "requestType": "{{requestType}}" }"""), Window, CancellationToken.None);
        await SettleAsync(session);

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times(requestType));
    }

    [Fact]
    public async Task The_stream_key_never_comes_back_nor_reaches_the_log()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.request", "requestType": "GetStreamServiceSettings" }"""), Window, CancellationToken.None);
        await session.Log.FlushAsync();

        Assert.Null(result.Failure);
        Assert.Equal(ObsRequestStep.Hidden, result.Data!.Value.GetProperty("streamServiceSettings").GetProperty("key").GetString());
        Assert.Equal("Twitch", result.Data!.Value.GetProperty("streamServiceSettings").GetProperty("service").GetString());
        Assert.DoesNotContain(FakeObs.StreamKey, result.Data!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(session.Log.Recent(), entry => entry.Message.Contains(FakeObs.StreamKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Redact_leaves_an_answer_without_the_key_as_it_was()
    {
        using JsonDocument document = JsonDocument.Parse("""{"currentProgramSceneName":"Pantalla"}""");

        Assert.Equal(document.RootElement.GetRawText(), ObsRequestStep.Redact(document.RootElement).GetRawText());
    }

    // ObsConnection manda con su propio tope y no con el token de quien llama (hallazgo E2-3): sólo esta comprobación frena el pedido,
    // y aquí el pedido puede ser SetCurrentProgramScene o StopRecord.
    [Fact]
    public async Task A_request_cancelled_before_sending_sends_nothing()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        using var cut = new CancellationTokenSource();
        cut.Cancel();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.request", "requestType": "SetCurrentProgramScene", "requestData": { "sceneName": "Banco" } }"""), Window, cut.Token);
        await SettleAsync(session);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times("SetCurrentProgramScene"));
    }

    [Fact]
    public async Task Without_a_link_nothing_is_sent()
    {
        await using var offline = new OfflineObs();
        var request = new ObsRequestStep(offline.Client, offline.State);

        StepResult result = await request.RunAsync(Parse("""{ "type": "obs.request", "requestType": "GetSceneList" }"""), Window, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
    }

    [Theory]
    [InlineData("""{ "type": "obs.request" }""", "requestType")]
    [InlineData("""{ "type": "obs.request", "requestType": " " }""", "requestType")]
    [InlineData("""{ "type": "obs.request", "requestType": "SetInputMute", "requestData": 5 }""", "requestData")]
    public async Task A_request_without_type_or_with_data_that_is_not_an_object_is_a_problem(string json, string where)
    {
        await using var offline = new OfflineObs();
        var request = new ObsRequestStep(offline.Client, offline.State);

        Problem problem = Assert.Single(request.Validate(Parse(json)));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    // El Executor no corre una acción con cualquier problema, aunque sea Warning: un pedido bien escrito no puede dar ninguno.
    [Theory]
    [InlineData("""{ "type": "obs.request", "requestType": "GetSceneList" }""")]
    [InlineData("""{ "type": "obs.request", "requestType": "SetCurrentProgramScene", "requestData": { "sceneName": "Banco" } }""")]
    [InlineData("""{ "type": "obs.request", "requestType": "GetSceneList", "requestData": null }""")]
    public async Task A_well_written_request_has_no_problems(string json)
    {
        await using var offline = new OfflineObs();
        var request = new ObsRequestStep(offline.Client, offline.State);

        Assert.Empty(request.Validate(Parse(json)));
    }
}
