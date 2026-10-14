using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Qué página rige, qué problemas junta y a quién le avisa el despachador cuando la biblioteca cambia.</summary>
public class DispatcherRefreshTests
{
    private const string TwoPages = """
        {
          "version": 1,
          "mainPage": "b",
          "pages": [
            { "id": "a", "name": "A", "keys": [ { "position": 1, "label": "A1", "action": "marca-short", "hotkey": "Ctrl+Alt+A" } ] },
            { "id": "b", "name": "B", "keys": [
              { "position": 1, "label": "B1", "action": "marca-largo", "hotkey": "Ctrl+Alt+B" },
              { "position": 2, "label": "B2", "action": "marca-short", "hotkey": "Ctrl+Alt+C" }
            ] }
          ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-largo", "name": "Marca: largo", "steps": [ { "type": "obs.marker", "marker": "largo" } ] }
          ],
          "hotkeys": [ { "hotkey": "Ctrl+Alt+G", "action": "marca-short" } ]
        }
        """;

    [Fact]
    public async Task The_main_page_rules_even_when_it_is_not_the_first_one()
    {
        await using var rig = new DispatcherRig(TwoPages);

        Assert.Equal(("b", "b"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
        Assert.Equal(
            new[] { "Ctrl+Alt+B", "Ctrl+Alt+C", "Ctrl+Alt+G" },
            rig.Windows.Registered.Values.Select(hotkey => hotkey.ToString()).Order());
        Assert.False(rig.Windows.Press("Ctrl+Alt+A"));
        rig.Windows.Press("Ctrl+Alt+B");
        await Poll.Until(() => rig.Finished.Count == 1);
        Assert.Equal("marca-largo", Assert.Single(rig.Finished).ActionId);
    }

    [Fact]
    public async Task What_a_source_rejects_on_a_page_that_is_not_the_first_says_that_page()
    {
        await using var rig = new DispatcherRig(TwoPages, windows => windows.Taken.Add(Hotkey.Parse("Ctrl+Alt+C")!.Value));

        Problem problem = Assert.Single(rig.State.Current.Problems);

        Assert.Equal("pages[1].keys[1].hotkey", problem.Where);
        Assert.Equal(ErrorCodes.HotkeyTaken, problem.Code);
    }

    [Fact]
    public async Task Without_a_main_page_the_first_page_with_an_id_is_the_base()
    {
        const string withoutMain = """
            {
              "version": 1,
              "pages": [
                { "name": "sin id", "keys": [] },
                { "id": "x", "name": "X", "keys": [ { "position": 1, "label": "X1", "action": "marca-short", "hotkey": "Ctrl+Alt+X" } ] }
              ],
              "actions": [ { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] } ]
            }
            """;
        await using var rig = new DispatcherRig(withoutMain);

        Assert.Equal("x", rig.State.Current.BasePage);
        Assert.True(rig.Windows.Press("Ctrl+Alt+X"));
    }

    [Fact]
    public async Task A_main_page_that_does_not_exist_leaves_the_keys_without_hotkeys_and_keeps_the_general_ones()
    {
        await using var rig = new DispatcherRig(TwoPages.Replace("\"mainPage\": \"b\"", "\"mainPage\": \"zzz\"", StringComparison.Ordinal));

        Assert.Equal("zzz", rig.State.Current.BasePage);
        Assert.Equal(new[] { "Ctrl+Alt+G" }, rig.Windows.Registered.Values.Select(hotkey => hotkey.ToString()));
        Assert.Contains(rig.State.Current.Problems, problem => problem is { Where: "mainPage", Code: ProblemCodes.PageNotFound });
    }

    [Fact]
    public async Task Without_a_library_folder_there_is_no_page_and_no_hotkeys_and_the_state_says_so()
    {
        await using var rig = new DispatcherRig(null);

        Assert.Equal(new LibraryState(LibraryStatus.NoLibrary, ""), rig.State.Current.Library);
        Assert.Equal(("", ""), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
        Assert.Equal(ProblemCodes.LibraryMissing, Assert.Single(rig.State.Current.Problems).Code);
        Assert.Empty(rig.Windows.Registered);
        Assert.Equal(new HotkeysState(0, 0), rig.State.Current.Hotkeys);

        rig.Dispatcher.Receive(new InputEvent("console", "key.5", Gesture.Tap));
        Assert.Empty(rig.Finished);
    }

    [Fact]
    public async Task Building_the_dispatcher_publishes_the_library_even_before_any_source_is_attached()
    {
        // Es la composición de la prueba de humo: sin bandeja ni atajos, el estado igual tiene que saber de la biblioteca.
        await using var rig = new DispatcherRig(TwoPages, attach: false);

        Assert.Equal(new LibraryState(LibraryStatus.Loaded, rig.Library.Folder), rig.State.Current.Library);
        Assert.Equal(("b", "b"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
        Assert.Empty(rig.Windows.Registered);
    }

    [Fact]
    public async Task A_page_whose_id_is_empty_is_not_a_base_page_and_takes_no_hotkeys()
    {
        const string emptyId = """
            {
              "version": 1,
              "pages": [ { "id": "", "name": "Sin id", "keys": [ { "position": 1, "label": "Uno", "action": "marca-short", "hotkey": "Ctrl+Alt+U" } ] } ],
              "actions": [ { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] } ]
            }
            """;
        await using var rig = new DispatcherRig(emptyId);

        Assert.Equal("", rig.State.Current.BasePage);
        Assert.Empty(rig.Windows.Registered);
        Assert.Contains(rig.State.Current.Problems, problem => problem.Where == "pages[0].id" && problem.Code == ProblemCodes.IdInvalid);
    }

    [Fact]
    public async Task An_event_with_no_base_page_does_not_fall_on_a_page_whose_id_is_empty()
    {
        const string emptyId = """
            {
              "version": 1,
              "pages": [ { "id": "", "name": "Sin id", "keys": [ { "position": 1, "label": "Uno", "action": "marca-short" } ] } ],
              "actions": [ { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] } ]
            }
            """;
        await using var rig = new DispatcherRig(emptyId);
        rig.Hold();

        rig.Dispatcher.Receive(new InputEvent("console", "key.1", Gesture.Tap));

        Assert.Empty(rig.State.Current.Running);
        Assert.Empty(rig.Marker.Runs);
    }

    [Fact]
    public async Task A_step_without_type_is_reported_once_although_the_validator_and_the_executor_both_see_it()
    {
        const string withoutType = """
            {
              "version": 1,
              "pages": [ { "id": "p", "name": "P", "keys": [] } ],
              "actions": [ { "id": "vacia", "name": "Vacía", "steps": [ {} ] } ]
            }
            """;
        await using var rig = new DispatcherRig(withoutType);

        Problem problem = Assert.Single(rig.State.Current.Problems);

        Assert.Equal(("actions[0].steps[0].type", ProblemCodes.FieldMissing), (problem.Where, problem.Code));
    }

    [Fact]
    public async Task The_problems_of_the_load_the_steps_and_the_source_all_end_up_in_the_state_in_that_order()
    {
        const string both = """
            {
              "version": 1,
              "pages": [ { "id": "p", "name": "P", "keys": [ { "position": 1, "label": "Uno", "action": "no-existe", "hotkey": "Ctrl+Alt+U" } ] } ],
              "actions": [ { "id": "escena", "name": "Escena", "steps": [ { "type": "obs.scene", "scene": "Banco" } ] } ]
            }
            """;
        await using var rig = new DispatcherRig(both, windows => windows.Taken.Add(Hotkey.Parse("Ctrl+Alt+U")!.Value));

        Assert.Equal(
            new[] { ("pages[0].keys[0].action", ProblemCodes.ActionNotFound), ("actions[0].steps[0].type", ProblemCodes.StepTypeUnknown), ("pages[0].keys[0].hotkey", ErrorCodes.HotkeyTaken) },
            rig.State.Current.Problems.Select(problem => (problem.Where, problem.Code)));
    }

    [Fact]
    public async Task A_source_gets_the_base_page_and_the_general_hotkeys_when_attached_and_again_on_every_reload()
    {
        await using var rig = new DispatcherRig(TwoPages);
        var console = new FakeInputSource("console");

        rig.Dispatcher.Attach(console);
        rig.Library.Load();

        Assert.Equal(2, console.Applied.Count);
        Assert.All(console.Applied, applied => Assert.Equal("b", applied.Page.Id));
        Assert.All(console.Applied, applied => Assert.Equal("Ctrl+Alt+G", Assert.Single(applied.Hotkeys).Hotkey));
    }

    [Fact]
    public async Task The_problems_of_a_source_keep_their_place_in_the_file()
    {
        await using var rig = new DispatcherRig(TwoPages);
        var console = new FakeInputSource("console");
        console.Rejects.Add(new Problem("keys[2].hotkey", ErrorCodes.HotkeyTaken, "uno", Severity.Warning));
        console.Rejects.Add(new Problem("hotkeys[1].hotkey", ErrorCodes.HotkeyTaken, "dos", Severity.Warning));
        console.Rejects.Add(new Problem("", ErrorCodes.InternalError, "tres", Severity.Error));

        rig.Dispatcher.Attach(console);

        Assert.Equal(
            new[] { "pages[1].keys[2].hotkey", "hotkeys[1].hotkey", "" },
            rig.State.Current.Problems.Select(problem => problem.Where));
    }

    [Fact]
    public async Task An_event_from_an_attached_source_runs_the_action_of_the_key_on_the_base_page()
    {
        await using var rig = new DispatcherRig(TwoPages);
        var console = new FakeInputSource("console");
        rig.Dispatcher.Attach(console);

        console.Raise(new InputEvent("console", "key.2", Gesture.Tap));
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult result = Assert.Single(rig.Finished);
        Assert.Equal(("marca-short", Trigger.Key), (result.ActionId, result.RequestedBy));
    }

    [Fact]
    public async Task A_step_type_whose_validation_throws_is_a_problem_of_its_action_and_the_other_keys_still_work()
    {
        const string twoActions = """
            {
              "version": 1,
              "pages": [ { "id": "p", "name": "P", "keys": [
                { "position": 1, "label": "Mala", "action": "mala", "hotkey": "Ctrl+Alt+M" },
                { "position": 2, "label": "Buena", "action": "buena", "hotkey": "Ctrl+Alt+B" }
              ] } ],
              "actions": [
                { "id": "mala", "name": "Mala", "steps": [ { "type": "obs.marker", "marker": "short", "label": "mala" } ] },
                { "id": "buena", "name": "Buena", "steps": [ { "type": "obs.marker", "marker": "short" } ] }
              ]
            }
            """;

        // El paso se arma antes que el despachador: si Refresh dejara pasar la excepción, el constructor no terminaría.
        await using var rig = new DispatcherRig(twoActions, beforeDispatcher: marker => marker.Validation = step =>
            step.Parameters.ContainsKey("label") ? throw new InvalidOperationException("Validate roto a propósito") : []);

        Problem problem = Assert.Single(rig.State.Current.Problems);
        Assert.Equal(("actions[0]", ProblemCodes.ParameterInvalid, Severity.Error), (problem.Where, problem.Code, problem.Severity));
        Assert.Contains("Validate roto a propósito", problem.Message, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Recent(), entry => entry.Code == ErrorCodes.InternalError && entry.Message.Contains("Validate roto a propósito", StringComparison.Ordinal));
        Assert.Equal(2, rig.Windows.Registered.Count);

        rig.Windows.Press("Ctrl+Alt+B");
        rig.Windows.Press("Ctrl+Alt+M");
        await Poll.Until(() => rig.Finished.Count == 2);

        ActionResult[] results = [.. rig.Finished.OrderBy(result => result.ActionId, StringComparer.Ordinal)];
        Assert.Equal(("buena", true), (results[0].ActionId, results[0].Ok));
        Assert.Equal(("mala", ErrorCodes.InternalError), (results[1].ActionId, results[1].Failure?.Code));
        Assert.Empty(rig.State.Current.Running);
    }

    [Fact]
    public async Task A_source_that_throws_does_not_stop_the_others_and_shows_up_as_a_problem()
    {
        await using var rig = new DispatcherRig(TwoPages);
        var broken = new FakeInputSource("console") { ThrowOnApply = true };
        var healthy = new FakeInputSource("otra");

        rig.Dispatcher.Attach(broken);
        rig.Dispatcher.Attach(healthy);

        Problem problem = Assert.Single(rig.State.Current.Problems);
        Assert.Equal(("", ErrorCodes.InternalError, Severity.Error), (problem.Where, problem.Code, problem.Severity));
        Assert.Contains("console", problem.Message, StringComparison.Ordinal);
        Assert.Equal(3, rig.Windows.Registered.Count);
        Assert.Single(healthy.Applied);
        Assert.Contains(rig.Log.Recent(), entry => entry.Code == ErrorCodes.InternalError && entry.Message.Contains("Apply roto a propósito", StringComparison.Ordinal));
        Assert.Equal("b", rig.State.Current.BasePage);
    }
}
