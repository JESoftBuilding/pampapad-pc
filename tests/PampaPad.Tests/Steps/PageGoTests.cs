using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class PageGoTests : IAsyncDisposable
{
    // La tecla 4 de grabacion va a escenas; la tecla 1 de escenas vuelve a la principal.
    private const string Config = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            { "id": "grabacion", "name": "Grabación", "keys": [
              { "position": 4, "label": "Escenas", "action": "ir-a-escenas" },
              { "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" }
            ] },
            { "id": "escenas", "name": "Escenas", "keys": [ { "position": 1, "label": "Volver", "action": "ir-a-principal" } ] }
          ],
          "actions": [
            { "id": "ir-a-escenas", "name": "Ir a escenas", "steps": [ { "type": "page.go", "target": "escenas" } ] },
            { "id": "ir-a-principal", "name": "Ir a la principal", "steps": [ { "type": "page.go", "target": "main" } ] },
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] }
          ]
        }
        """;

    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly Log _log;
    private readonly CoreLibrary _library;
    private readonly Dispatcher _dispatcher;

    public PageGoTests()
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), Config);
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        _library.Load();
        Dispatcher? dispatcher = null;
        var executor = new Executor([new FakeStep("obs.marker"), new PageGo(_library, target => dispatcher!.Go(target))], TimeProvider.System);
        dispatcher = new Dispatcher(_library, _state, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), _log);
        dispatcher.Finished += Finished.Enqueue;
        _dispatcher = dispatcher;
    }

    private ConcurrentQueue<ActionResult> Finished { get; } = new();

    public async ValueTask DisposeAsync()
    {
        _dispatcher.CancelAll();
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<ActionResult> PressAsync(int position)
    {
        int before = Finished.Count;
        _dispatcher.Receive(new InputEvent("console", Controls.Key(position), Gesture.Press));
        await Poll.Until(() => Finished.Count > before);
        return Finished.Last();
    }

    [Fact]
    public async Task A_key_with_page_go_changes_the_base_and_the_other_page_brings_it_back()
    {
        ActionResult there = await PressAsync(4);
        string baseThere = _state.Current.BasePage;
        ActionResult back = await PressAsync(1);

        Assert.True(there.Ok, there.Failure?.Message);
        Assert.Equal("escenas", baseThere);
        Assert.True(back.Ok, back.Failure?.Message);
        Assert.Equal("grabacion", _state.Current.BasePage);
    }

    [Fact]
    public async Task While_recording_page_go_is_navigation_blocked_the_chain_fails_and_the_base_stays()
    {
        _state.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        ActionResult result = await PressAsync(4);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.NavigationBlocked, result.Failure?.Code);
        Assert.Equal("page.go", result.StepType);
        Assert.Equal("grabacion", _state.Current.BasePage);
    }

    [Fact]
    public void A_target_that_names_no_page_is_problem_4_and_stops_the_save()
    {
        var step = new PageGo(_library, _ => null);

        Problem problem = Assert.Single(step.Validate(JsonSerializer.Deserialize<Step>("""{ "type": "page.go", "target": "sala" }""", Json.Options)!));

        Assert.Equal("target", problem.Where);
        Assert.Equal(ProblemCodes.PageNotFound, problem.Code);
        Assert.Equal(Severity.Error, problem.Severity);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("escenas")]
    public void The_reserved_targets_and_a_page_that_exists_are_fine(string target)
    {
        var step = new PageGo(_library, _ => null);

        Assert.Empty(step.Validate(JsonSerializer.Deserialize<Step>($$"""{ "type": "page.go", "target": "{{target}}" }""", Json.Options)!));
    }

    [Theory]
    [InlineData("""{ "type": "page.go" }""")]
    [InlineData("""{ "type": "page.go", "target": "" }""")]
    public void Without_a_target_it_is_a_problem(string json)
    {
        Problem problem = Assert.Single(new PageGo(_library, _ => null).Validate(JsonSerializer.Deserialize<Step>(json, Json.Options)!));

        Assert.Equal("target", problem.Where);
    }

    [Fact]
    public async Task A_page_go_whose_target_is_gone_on_disk_does_not_run_the_action()
    {
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), Config.Replace("\"id\": \"escenas\"", "\"id\": \"escenas-viejas\"", StringComparison.Ordinal));
        _library.Load();

        ActionResult result = await PressAsync(4);

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Equal(StepOutcome.NotRun, Assert.Single(result.Steps).Outcome);
        Assert.Contains(_state.Current.Problems, problem => problem.Code == ProblemCodes.PageNotFound && problem.Where == "actions[0].steps[0].target");
    }
}
