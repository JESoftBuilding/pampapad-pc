using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class DtosTests
{
    [Theory]
    [InlineData(ObsLinkState.Closed, "closed")]
    [InlineData(ObsLinkState.NoWebsocket, "no_websocket")]
    [InlineData(ObsLinkState.AuthFailed, "auth_failed")]
    [InlineData(ObsLinkState.Loading, "loading")]
    [InlineData(ObsLinkState.Ready, "ready")]
    [InlineData(ObsLinkState.Switching, "switching")]
    [InlineData(ObsLinkState.Lost, "lost")]
    [InlineData(ObsLinkState.Kicked, "kicked")]
    [InlineData(ObsLinkState.Exiting, "exiting")]
    public void The_obs_link_travels_with_underscore(ObsLinkState link, string name)
    {
        Assert.Equal(name, ObsView.LinkName(link));
    }

    [Fact]
    public void Every_obs_link_is_one_of_the_glossary()
    {
        // Los nueve de 03 §2 y del glosario de la spec. Un valor nuevo del enum se suma también en ui/src/bridge/contract.ts.
        string[] glossary = ["closed", "no_websocket", "auth_failed", "loading", "ready", "switching", "lost", "kicked", "exiting"];

        Assert.Equal(glossary, Enum.GetValues<ObsLinkState>().Select(ObsView.LinkName));
    }

    [Fact]
    public void The_state_view_keeps_the_seq_and_turns_the_recorded_time_into_milliseconds()
    {
        AppState state = AppState.Initial with
        {
            Seq = 7,
            Obs = ObsState.Initial with { Link = ObsLinkState.Ready, Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(754.5) },
        };

        StateView view = StateView.From(state);

        Assert.Equal(7, view.Seq);
        Assert.Equal("ready", view.Obs.Link);
        Assert.Equal(754_500, view.Obs.RecordedMs);
        Assert.True(view.Obs.TakeInProgress);
    }

    [Fact]
    public void The_initial_state_travels_in_camel_case_and_without_nulls()
    {
        string json = JsonSerializer.Serialize(StateView.From(AppState.Initial), Json.Compact);

        Assert.StartsWith("""{"seq":0,"update":{"phase":"idle"},"obs":{"link":"closed","recording":"stopped","streaming":false,"recordedMs":0,""", json);
        Assert.Contains("\"library\":{\"status\":\"notLoaded\",\"folder\":\"\"}", json);
        Assert.Contains("\"console\":{\"state\":\"off\"}", json);
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("lastFailure", json);
    }

    [Theory]
    [InlineData("""{ "count": 5, "lines": 3 }""")]
    [InlineData("""{ "count": "cinco" }""")]
    [InlineData("""{ "level": "debug" }""")]
    public void Log_read_rejects_unknown_fields_and_wrong_types(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LogReadRequest>(json, Json.Compact));
    }

    [Fact]
    public void Log_read_takes_its_filters_in_camel_case()
    {
        var runId = Guid.NewGuid();

        LogReadRequest? request = JsonSerializer.Deserialize<LogReadRequest>(
            $$"""{ "count": 20, "level": "warning", "requestedBy": "hotkey", "runId": "{{runId}}" }""", Json.Compact);

        Assert.Equal(new LogReadRequest(20, LogLevel.Warning, Trigger.Hotkey, runId), request);
    }

    [Fact]
    public void No_data_is_an_empty_object_and_nothing_else()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new NoData(), Json.Compact));
        Assert.NotNull(JsonSerializer.Deserialize<NoData>("{}", Json.Compact));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NoData>("""{ "x": 1 }""", Json.Compact));
    }

    [Fact]
    public void Cancel_takes_a_run_id_or_all()
    {
        var runId = Guid.NewGuid();

        Assert.Equal(new ActionCancelRequest(runId, false), JsonSerializer.Deserialize<ActionCancelRequest>($$"""{ "runId": "{{runId}}" }""", Json.Compact));
        Assert.Equal(new ActionCancelRequest(null, true), JsonSerializer.Deserialize<ActionCancelRequest>("""{ "all": true }""", Json.Compact));
    }

    [Fact]
    public void A_finished_action_travels_with_its_duration_in_milliseconds()
    {
        var result = new ActionResult("marca-short", Guid.NewGuid(), Trigger.Key, true, null, null, null, TimeSpan.FromMilliseconds(18.7), [new StepReport(StepOutcome.Ok)]);

        ActionFinishedView view = ActionFinishedView.From(result);

        Assert.Equal(18, view.DurationMs);
        Assert.Equal(result.Steps, view.Steps);
    }
}
