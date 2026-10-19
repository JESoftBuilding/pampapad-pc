using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class ConditionsTests : IAsyncLifetime
{
    private static readonly WindowInfo Mirror = new("bancocam", "scrcpy", 1823, 850, Minimized: false);

    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly FakeWindows _windows = new();
    private Log _log = null!;
    private ObsClient _obs = null!;

    public Task InitializeAsync()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _obs = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, _state, _log, TimeProvider.System);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _obs.DisposeAsync();
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    private static Condition Parse(string json) => JsonSerializer.Deserialize<Condition>(json, Json.Options)!;

    private ConditionType Type(string id) => Conditions.Build(_windows, _state, _obs).Single(type => type.Id == id);

    private Task<ConditionResult> Evaluate(string json)
    {
        Condition condition = Parse(json);
        return ConditionRules.EvaluateAsync(Type(condition.Type), condition, CancellationToken.None);
    }

    [Fact]
    public void The_window_and_recording_conditions_are_in_the_catalog_once()
    {
        IReadOnlyList<ConditionType> all = Conditions.Build(_windows, _state, _obs);

        Assert.Contains(all, type => type.Id == "window.exists");
        Assert.Contains(all, type => type.Id == "window.sizeIs");
        Assert.Contains(all, type => type.Id == "recording.is");
        Assert.Equal(all.Count, all.Select(type => type.Id).Distinct().Count());
        Assert.All(all, type => Assert.False(string.IsNullOrWhiteSpace(type.Name)));
    }

    [Fact]
    public async Task Window_exists_compares_the_title_without_case()
    {
        _windows.Open(Mirror);

        ConditionResult found = await Evaluate("""{ "type": "window.exists", "title": "BancoCam" }""");
        ConditionResult missing = await Evaluate("""{ "type": "window.exists", "title": "celucam" }""");

        Assert.Equal(ConditionOutcome.Pass, found.Outcome);
        Assert.Equal(ConditionOutcome.Fail, missing.Outcome);
        Assert.Equal("No hay una ventana \"celucam\".", missing.Detail);
    }

    [Fact]
    public async Task Window_exists_with_negate_is_the_onlyIf_of_ir_al_banco()
    {
        ConditionResult closed = await Evaluate("""{ "type": "window.exists", "title": "bancocam", "negate": true }""");
        _windows.Open(Mirror);
        ConditionResult open = await Evaluate("""{ "type": "window.exists", "title": "bancocam", "negate": true }""");

        Assert.Equal(ConditionOutcome.Pass, closed.Outcome);
        Assert.Equal(ConditionOutcome.Fail, open.Outcome);
    }

    [Fact]
    public async Task With_a_process_a_folder_named_bancocam_does_not_count()
    {
        _windows.Open(new WindowInfo("bancocam", "explorer", 960, 600, false));

        ConditionResult any = await Evaluate("""{ "type": "window.exists", "title": "bancocam" }""");
        ConditionResult scrcpy = await Evaluate("""{ "type": "window.exists", "title": "bancocam", "process": "scrcpy" }""");

        Assert.Equal(ConditionOutcome.Pass, any.Outcome);
        Assert.Equal(ConditionOutcome.Fail, scrcpy.Outcome);
        Assert.Empty(ConditionRules.Validate(Type("window.exists"), Parse("""{ "type": "window.exists", "title": "bancocam", "process": "scrcpy" }""")));
    }

    [Fact]
    public async Task Window_sizeIs_says_the_size_it_measured_or_that_it_is_minimized()
    {
        _windows.Open(Mirror with { Width = 2279, Height = 1063 });
        ConditionResult wrong = await Evaluate("""{ "type": "window.sizeIs", "title": "bancocam", "width": 1823, "height": 850 }""");
        _windows.Close("bancocam");
        _windows.Open(Mirror with { Minimized = true });
        ConditionResult minimized = await Evaluate("""{ "type": "window.sizeIs", "title": "bancocam", "width": 1823, "height": 850 }""");
        _windows.Close("bancocam");
        _windows.Open(Mirror);
        ConditionResult right = await Evaluate("""{ "type": "window.sizeIs", "title": "bancocam", "width": 1823, "height": 850 }""");

        Assert.Equal(ConditionOutcome.Fail, wrong.Outcome);
        Assert.Equal("La ventana \"bancocam\" mide 2279 x 1063 y se esperaba 1823 x 850.", wrong.Detail);
        Assert.Equal(ConditionOutcome.Fail, minimized.Outcome);
        Assert.Contains("minimizada", minimized.Detail, StringComparison.Ordinal);
        Assert.Equal(ConditionOutcome.Pass, right.Outcome);
    }

    [Theory]
    [InlineData("""{ "type": "window.sizeIs", "title": "bancocam", "width": 0, "height": 850 }""", "width")]
    [InlineData("""{ "type": "window.sizeIs", "title": "bancocam", "width": 1823, "height": "850" }""", "height")]
    [InlineData("""{ "type": "window.sizeIs", "title": "", "width": 1823, "height": 850 }""", "title")]
    [InlineData("""{ "type": "window.exists", "title": 5 }""", "title")]
    [InlineData("""{ "type": "window.exists", "title": "bancocam", "process": 5 }""", "process")]
    [InlineData("""{ "type": "window.sizeIs", "title": "bancocam", "width": 1823, "height": 850, "process": ["scrcpy"] }""", "process")]
    [InlineData("""{ "type": "recording.is", "state": "grabando" }""", "state")]
    public void A_bad_parameter_is_a_problem_of_the_condition(string json, string where)
    {
        Condition condition = Parse(json);

        Problem problem = Assert.Single(ConditionRules.Validate(Type(condition.Type), condition));

        Assert.Equal(where, problem.Where);
    }

    [Theory]
    [InlineData(RecordingState.Stopped, "stopped", ConditionOutcome.Pass)]
    [InlineData(RecordingState.Stopped, "recording", ConditionOutcome.Fail)]
    [InlineData(RecordingState.Recording, "recording", ConditionOutcome.Pass)]
    [InlineData(RecordingState.Starting, "recording", ConditionOutcome.Pass)]
    [InlineData(RecordingState.Stopping, "recording", ConditionOutcome.Pass)]
    [InlineData(RecordingState.Paused, "recording", ConditionOutcome.Pass)]
    [InlineData(RecordingState.Paused, "paused", ConditionOutcome.Pass)]
    [InlineData(RecordingState.Recording, "paused", ConditionOutcome.Fail)]
    [InlineData(RecordingState.Starting, "stopped", ConditionOutcome.Fail)]
    public async Task Recording_is_reads_what_the_program_knows(RecordingState known, string wanted, ConditionOutcome expected)
    {
        _state.Update(s => s with { Obs = s.Obs with { Recording = known } });

        ConditionResult result = await Evaluate($$"""{ "type": "recording.is", "state": "{{wanted}}" }""");

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Recording_is_does_not_need_obs()
    {
        ConditionResult result = await Evaluate("""{ "type": "recording.is", "state": "stopped" }""");

        Assert.Equal(ConditionOutcome.Pass, result.Outcome);
        Assert.Equal("OBS está detenido; se pedía \"stopped\".", result.Detail);
    }
}
