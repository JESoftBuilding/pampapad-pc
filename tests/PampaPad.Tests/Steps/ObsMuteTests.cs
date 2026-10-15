using System.Text.Json;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsMuteTests
{
    private const string GhostUuid = "7c1e5a52-3f0b-4d0e-9a58-0b6f3f6a2d11";

    private static readonly Invocation Press = new(Guid.NewGuid(), Trigger.Key, "console", "key.10", Gesture.Press, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static ObsMute StepOf(ObsSession session) => new(session.Client, session.State);

    // Un pedido que saliera igual llega al falso después de que el paso vuelve (el socket manda y recién espera). El falso atiende cada
    // sesión en orden y cuenta antes de contestar: un GetVersion por el mismo socket espera a que haya contado todo lo anterior.
    private static async Task SettleAsync(ObsSession session) =>
        Assert.True((await session.Client.RequestAsync("GetVersion", null, CancellationToken.None)).Ok);

    [Fact]
    public async Task Without_input_it_toggles_the_mic_with_what_the_state_knows()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        int asked = session.Obs.Times("GetInputMute");                 // la lectura inicial ya preguntó una vez

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.mute" }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times(ObsMute.RequestType));
        Assert.Equal(asked, session.Obs.Times("GetInputMute"));        // el del micrófono no se pregunta: lo refleja el estado
        await Poll.Until(() => session.State.Current.Obs.MicMuted);
        Assert.True(session.Obs.MicMuted);
    }

    [Fact]
    public async Task Two_toggles_leave_it_as_it_was()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        ObsMute mute = StepOf(session);

        await mute.RunAsync(Parse("""{ "type": "obs.mute" }"""), Press, CancellationToken.None);
        await Poll.Until(() => session.State.Current.Obs.MicMuted);
        await mute.RunAsync(Parse("""{ "type": "obs.mute" }"""), Press, CancellationToken.None);
        await Poll.Until(() => !session.State.Current.Obs.MicMuted);

        Assert.False(session.Obs.MicMuted);
    }

    [Fact]
    public async Task Mute_is_sent_even_if_it_already_is()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await session.Obs.SetMicMutedAsync(true);
        await Poll.Until(() => session.State.Current.Obs.MicMuted);

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.mute", "do": "mute" }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times(ObsMute.RequestType));
        Assert.True(session.Obs.MicMuted);
    }

    [Fact]
    public async Task Another_input_is_asked_before_toggling_it()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        int asked = session.Obs.Times("GetInputMute");

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.mute", "input": { "uuid": "{{FakeObs.StingUuid}}", "name": "Sting triste" } }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(asked + 1, session.Obs.Times("GetInputMute"));
        Assert.True(session.Obs.IsMuted(FakeObs.StingUuid));
        Assert.False(session.Obs.MicMuted);
    }

    [Fact]
    public async Task An_input_without_audio_is_rejected_by_obs_with_its_comment()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.mute", "input": "Captura de pantalla" }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsRequestFailed, result.Failure?.Code);
        Assert.Contains("(código 604): The specified input does not support audio.", result.Failure?.Message, StringComparison.Ordinal);
        Assert.Equal(0, session.Obs.Times(ObsMute.RequestType));     // falló al leer el silencio: no se mandó nada
    }

    [Fact]
    public async Task Without_mic1_and_without_input_nothing_is_sent()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Stopped, obs => obs.HasMic = false);

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.mute" }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsRefNotFound, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times(ObsMute.RequestType));
    }

    [Fact]
    public async Task An_input_removed_in_obs_fails_without_sending_anything()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await session.Obs.RemoveInputAsync(FakeObs.StingUuid);
        await Poll.Until(() => session.State.Current.Obs.Inputs.All(input => input.Uuid != FakeObs.StingUuid));

        StepResult result = await StepOf(session).RunAsync(Parse("""{ "type": "obs.mute", "input": "Sting triste", "do": "mute" }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsRefNotFound, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times(ObsMute.RequestType));
    }

    // OBS la perdió y el programa todavía la tiene en la lista: el 600 llega en la lectura (toggle) o en el pedido (mute).
    [Theory]
    [InlineData("mute", 0, 1)]
    [InlineData("toggle", 1, 0)]
    public async Task An_input_that_vanished_without_notice_is_not_found_by_obs_either(string what, int reads, int sets)
    {
        await using ObsSession session = await ObsSession.StartAsync();
        int asked = session.Obs.Times("GetInputMute");
        session.State.Update(s => s with { Obs = s.Obs with { Inputs = [.. s.Obs.Inputs, new ObsItem(GhostUuid, "Fantasma")] } });

        StepResult result = await StepOf(session).RunAsync(Parse($$"""{ "type": "obs.mute", "input": "Fantasma", "do": "{{what}}" }"""), Press, CancellationToken.None);
        await SettleAsync(session);

        Assert.Equal(ErrorCodes.ObsRefNotFound, result.Failure?.Code);
        Assert.Equal(asked + reads, session.Obs.Times("GetInputMute"));
        Assert.Equal(sets, session.Obs.Times(ObsMute.RequestType));
    }

    // ObsConnection manda con su propio tope y no con el token de quien llama (hallazgo E2-3): sólo estas comprobaciones frenan el pedido.
    [Theory]
    [InlineData("""{ "type": "obs.mute" }""")]
    [InlineData("""{ "type": "obs.mute", "input": "Sting triste" }""")]
    public async Task A_step_cancelled_before_sending_sends_nothing(string json)
    {
        await using ObsSession session = await ObsSession.StartAsync();
        int asked = session.Obs.Times("GetInputMute");
        using var cut = new CancellationTokenSource();
        cut.Cancel();

        StepResult result = await StepOf(session).RunAsync(Parse(json), Press, cut.Token);
        await SettleAsync(session);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(asked, session.Obs.Times("GetInputMute"));
        Assert.Equal(0, session.Obs.Times(ObsMute.RequestType));
    }

    [Fact]
    public async Task Without_a_link_nothing_is_sent()
    {
        await using var offline = new OfflineObs();
        var mute = new ObsMute(offline.Client, offline.State);

        StepResult result = await mute.RunAsync(Parse("""{ "type": "obs.mute" }"""), Press, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
    }

    [Theory]
    [InlineData("""{ "type": "obs.mute", "do": "mutear" }""", "do")]
    [InlineData("""{ "type": "obs.mute", "input": { "uuid": "506b0448-d6b6-443e-83d9-5dd76fbf1d60" } }""", "input")]
    [InlineData("""{ "type": "obs.mute", "input": 3 }""", "")]
    public async Task What_is_not_toggle_mute_or_unmute_or_an_input_without_name_is_a_problem(string json, string where)
    {
        await using var offline = new OfflineObs();
        var mute = new ObsMute(offline.Client, offline.State);

        Problem problem = Assert.Single(mute.Validate(Parse(json)));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    [Theory]
    [InlineData("""{ "type": "obs.mute" }""")]
    [InlineData("""{ "type": "obs.mute", "do": "unmute", "input": { "uuid": "506b0448-d6b6-443e-83d9-5dd76fbf1d60", "name": "Mic/Aux" } }""")]
    public async Task The_mic_alone_and_a_full_reference_have_no_problems(string json)
    {
        await using var offline = new OfflineObs();
        var mute = new ObsMute(offline.Client, offline.State);

        Assert.Empty(mute.Validate(Parse(json)));
    }
}
