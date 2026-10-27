using System.Text.Json;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public sealed class DispatcherDraftTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _rig.DisposeAsync();

    private static PadAction Draft(string json) => JsonSerializer.Deserialize<PadAction>(json, Json.Options)!;

    private static Invocation By(Guid? runId = null) => new(runId ?? Guid.NewGuid(), Trigger.Window, "window", null, null, null);

    private const string Short = """{ "id": "prueba", "name": "Prueba", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""";

    [Fact]
    public async Task A_draft_that_is_not_in_the_library_runs_its_steps_and_finishes_with_a_report_per_step()
    {
        Failure? started = _rig.Dispatcher.LaunchDraft(Draft(Short), By());
        await Poll.Until(() => _rig.Finished.Count == 1);

        ActionResult result = Assert.Single(_rig.Finished);
        Assert.Null(started);
        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("prueba", result.ActionId);
        Assert.Equal(Trigger.Window, result.RequestedBy);
        Assert.Equal(StepOutcome.Ok, Assert.Single(result.Steps).Outcome);
        Assert.Single(_rig.Marker.Runs);
        Assert.Null(_rig.Library.Current.FindAction("prueba"));
    }

    [Fact]
    public async Task A_draft_in_course_is_listed_as_running_and_can_be_cancelled_by_its_run_id()
    {
        System.Action release = _rig.Hold();
        Guid runId = Guid.NewGuid();

        _rig.Dispatcher.LaunchDraft(Draft(Short), By(runId));
        await Poll.Until(() => _rig.State.Current.Running.Count == 1);
        RunningAction running = Assert.Single(_rig.State.Current.Running);
        bool cancelled = _rig.Dispatcher.Cancel(runId);
        release();
        await Poll.Until(() => _rig.Finished.Count == 1);

        Assert.Equal((runId, "prueba", Trigger.Window), (running.RunId, running.ActionId, running.RequestedBy));
        Assert.True(cancelled);
        Assert.Equal(ErrorCodes.Cancelled, Assert.Single(_rig.Finished).Failure?.Code);
        Assert.Empty(_rig.State.Current.Running);
    }

    [Fact]
    public async Task A_draft_with_the_id_of_an_action_that_is_running_is_refused_and_the_one_in_course_goes_on()
    {
        System.Action release = _rig.Hold();
        Task<ActionResult> saved = _rig.Dispatcher.Launch("marca-short", By());
        await Poll.Until(() => _rig.State.Current.Running.Count == 1);

        Failure? refused = _rig.Dispatcher.LaunchDraft(Draft(Short.Replace("prueba", "marca-short", StringComparison.Ordinal)), By());
        release();
        ActionResult result = await saved;

        Assert.Equal(ErrorCodes.Cancelled, refused?.Code);
        Assert.Contains("marca-short", refused!.Message, StringComparison.Ordinal);
        Assert.True(result.Ok);
        Assert.Single(_rig.Marker.Runs);
    }

    [Fact]
    public async Task A_draft_without_an_id_runs_under_the_draft_key_and_a_second_one_is_refused_until_it_ends()
    {
        System.Action release = _rig.Hold();
        PadAction nameless = Draft("""{ "name": "Sin id", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""");

        Failure? first = _rig.Dispatcher.LaunchDraft(nameless, By());
        await Poll.Until(() => _rig.State.Current.Running.Count == 1);
        Failure? second = _rig.Dispatcher.LaunchDraft(nameless, By());
        string key = _rig.State.Current.Running[0].ActionId;
        release();
        await Poll.Until(() => _rig.Finished.Count == 1);

        Assert.Null(first);
        Assert.Equal(ErrorCodes.Cancelled, second?.Code);
        Assert.Equal(Dispatcher.DraftKey, key);
        Assert.Equal("", Assert.Single(_rig.Finished).ActionId);
    }

    [Fact]
    public async Task A_draft_with_a_problem_does_not_run_and_says_which_step()
    {
        _rig.Marker.Validation = _ => [new Problem("marker", ProblemCodes.ParameterInvalid, "No es un tipo de marca.", Severity.Warning)];

        _rig.Dispatcher.LaunchDraft(Draft(Short), By());
        await Poll.Until(() => _rig.Finished.Count == 1);

        ActionResult result = Assert.Single(_rig.Finished);
        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Equal("steps[0].marker", result.Failure?.Where);
        Assert.Equal(StepOutcome.NotRun, Assert.Single(result.Steps).Outcome);
        Assert.Empty(_rig.Marker.Runs);
    }
}
