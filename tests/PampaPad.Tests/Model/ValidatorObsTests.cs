using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ValidatorObsTests
{
    private const string Template = """
        {
          "version": 1,
          "pages": [ { "id": "grabacion", "name": "Grabación", "keys": [ __KEYS__ ] } ],
          "actions": [
            { "id": "grabar", "name": "Grabar", "steps": [ { "type": "obs.record", "do": "start" } ] },
            { "id": "parar", "name": "Parar", "steps": [ { "type": "obs.record", "do": "stop" } ] },
            { "id": "pausar", "name": "Pausar", "steps": [ { "type": "obs.record", "do": "pause" } ] },
            { "id": "seguir", "name": "Seguir", "steps": [ { "type": "obs.record", "do": "resume" } ] },
            { "id": "pedido", "name": "Pedido", "steps": [ { "type": "obs.marker", "marker": "short" }, { "type": "obs.request", "requestType": "__REQUEST__" } ] }
          ],
          "hotkeys": [ { "hotkey": "Ctrl+Shift+Alt+D", "action": "parar" } ]
        }
        """;

    private static IReadOnlyList<Problem> Validate(string keys, string request = "GetSceneList") =>
        Validator.Validate(JsonSerializer.Deserialize<Config>(
            Template.Replace("__KEYS__", keys, StringComparison.Ordinal).Replace("__REQUEST__", request, StringComparison.Ordinal), Json.Options)!);

    [Theory]
    [InlineData("parar")]
    [InlineData("pausar")]
    public void A_key_that_stops_or_pauses_with_a_tap_is_an_error(string action)
    {
        Problem problem = Assert.Single(Validate($$"""{ "position": 12, "label": "Mal", "action": "{{action}}" }"""));

        Assert.Equal(("pages[0].keys[0].action", ProblemCodes.HoldRequired, Severity.Error), (problem.Where, problem.Code, problem.Severity));
        Assert.Contains("La tecla 12 de Grabación", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("StopRecord")]
    [InlineData("PauseRecord")]
    [InlineData("ToggleRecord")]
    [InlineData("ToggleRecordPause")]
    public void A_key_whose_tap_asks_obs_to_stop_or_pause_is_also_an_error(string request)
    {
        Problem problem = Assert.Single(Validate("""{ "position": 1, "label": "Pedido", "action": "pedido" }""", request));

        Assert.Equal(("pages[0].keys[0].action", ProblemCodes.HoldRequired), (problem.Where, problem.Code));
    }

    [Fact]
    public void With_stop_and_pause_on_hold_and_start_and_resume_on_tap_the_page_has_no_problems()
    {
        Assert.Empty(Validate("""
            { "position": 11, "label": "Pausa", "action": "seguir", "onHold": { "action": "pausar" } },
            { "position": 12, "label": "Grabar", "action": "grabar", "onHold": { "action": "parar" } }
            """));
    }

    [Fact]
    public void The_general_hotkey_that_stops_is_not_a_problem()
    {
        Assert.Empty(Validate(""));                                  // la lista hotkeys trae "parar" con un atajo (02 §6)
    }

    [Theory]
    [InlineData("TriggerHotkeyByName")]
    [InlineData("TriggerHotkeyByKeySequence")]
    public void The_two_hotkey_requests_are_errors(string request)
    {
        Problem problem = Assert.Single(Validate("", request));

        Assert.Equal(("actions[4].steps[1].requestType", ProblemCodes.RequestForbidden, Severity.Error), (problem.Where, problem.Code, problem.Severity));
    }

    [Fact]
    public void An_action_that_is_missing_or_empty_neither_stops_nor_pauses()
    {
        Assert.False(Validator.StopsOrPauses(null));
        Assert.False(Validator.StopsOrPauses(new PampaPad.Core.Action { Id = "x", Name = "X", Steps = [] }));
    }
}
