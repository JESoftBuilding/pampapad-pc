using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsMarkerTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.6", Gesture.Tap, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    [Fact]
    public async Task While_recording_the_marker_goes_in_with_its_type_and_its_label()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await marker.RunAsync(Parse("""{ "type": "obs.marker", "marker": "inicio", "label": "demo motores" }"""), Tap, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "inicio demo motores" }, session.Obs.Chapters);
    }

    [Theory]
    [InlineData(RecordingState.Stopped, "marker_not_recording", "No estás grabando: la marca no entró.")]
    [InlineData(RecordingState.Paused, "marker_paused", "Estás en pausa: la marca no entró.")]
    public async Task Without_a_running_take_obs_rejects_it_and_the_text_says_why(RecordingState recording, string code, string message)
    {
        await using ObsSession session = await ObsSession.StartAsync(recording);
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await marker.RunAsync(Parse("""{ "type": "obs.marker", "marker": "short" }"""), Tap, CancellationToken.None);

        Assert.NotNull(result.Failure);
        Assert.Equal(code, result.Failure.Code);
        Assert.Equal(message, result.Failure.Message);
        Assert.Empty(session.Obs.Chapters);
    }

    [Fact]
    public async Task A_recording_format_without_chapters_is_a_rejected_marker()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        session.Obs.ChaptersUnsupported = true;
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await marker.RunAsync(Parse("""{ "type": "obs.marker", "marker": "short" }"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarkerRejected, result.Failure?.Code);
    }

    [Fact]
    public async Task An_obs_without_the_request_is_not_even_asked()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording, obs => obs.AvailableRequests.Remove(Markers.RequestType));
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await marker.RunAsync(Parse("""{ "type": "obs.marker", "marker": "short" }"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarkerUnsupported, result.Failure?.Code);
        Assert.Equal("Este OBS no admite marcas.", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times(Markers.RequestType));
    }

    [Fact]
    public async Task Without_a_link_nothing_is_sent()
    {
        using var temp = new TempFolder();
        await using var log = new Log(temp.Combine("log"), TimeProvider.System);
        await using var state = new StateStore();
        await using var client = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, state, log, TimeProvider.System);
        var marker = new ObsMarker(client, state);

        StepResult result = await marker.RunAsync(Parse("""{ "type": "obs.marker", "marker": "short" }"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal("Sin OBS: la marca no entró.", result.Failure?.Message);
    }

    [Theory]
    [InlineData("""{ "type": "obs.marker", "marker": "shrot" }""", "marker")]
    [InlineData("""{ "type": "obs.marker", "marker": "Short" }""", "marker")]
    [InlineData("""{ "type": "obs.marker" }""", "marker")]
    [InlineData("""{ "type": "obs.marker", "marker": 5 }""", "")]
    public async Task A_marker_that_is_not_one_of_the_five_is_a_problem_of_the_step(string json, string where)
    {
        using var temp = new TempFolder();
        await using var log = new Log(temp.Combine("log"), TimeProvider.System);
        await using var state = new StateStore();
        await using var client = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, state, log, TimeProvider.System);
        var marker = new ObsMarker(client, state);

        Problem problem = Assert.Single(marker.Validate(Parse(json)));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
        Assert.Equal(Severity.Warning, problem.Severity);
    }

    [Fact]
    public async Task A_good_step_has_no_problems_and_tolerates_what_it_does_not_know()
    {
        using var temp = new TempFolder();
        await using var log = new Log(temp.Combine("log"), TimeProvider.System);
        await using var state = new StateStore();
        await using var client = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, state, log, TimeProvider.System);
        var marker = new ObsMarker(client, state);

        Assert.Empty(marker.Validate(Parse("""{ "type": "obs.marker", "marker": "largo", "label": "capítulo 2", "inventado": true }""")));
    }
}
