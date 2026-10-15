using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ValidatorTests
{
    private const string OnePage = """{ "version": 1, "pages": [ { "id": "a", "name": "A", "keys": [ __KEYS__ ] } ], "actions": [ { "id": "x", "name": "X", "steps": [] } ], "hotkeys": [ __HOTKEYS__ ] }""";

    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    private static IReadOnlyList<Problem> Validate(string json) =>
        Validator.Validate(JsonSerializer.Deserialize<Config>(json, Json.Options)!);

    // Los marcadores llevan guiones bajos: "KEYS" a secas está dentro de "HOTKEYS" y el primer Replace rompería el segundo.
    private static IReadOnlyList<Problem> ValidatePage(string keys, string hotkeys = "") =>
        Validate(OnePage.Replace("__KEYS__", keys, StringComparison.Ordinal).Replace("__HOTKEYS__", hotkeys, StringComparison.Ordinal));

    [Fact]
    public void A_mistyped_action_breaks_only_its_key()
    {
        Problem problem = Assert.Single(Validate(File.ReadAllText(Data("config.broken.json"))));

        Assert.Equal("pages[0].keys[1].action", problem.Where);
        Assert.Equal(ProblemCodes.ActionNotFound, problem.Code);
        Assert.Equal(Severity.Error, problem.Severity);
        Assert.Contains("marca-inicoi", problem.Message);
    }

    [Fact]
    public void The_minimal_file_has_no_problems()
    {
        Assert.Empty(Validate(File.ReadAllText(Data("config.minimal.json"))));
    }

    [Fact]
    public void The_example_of_the_spec_has_no_structural_problems()
    {
        Assert.Empty(Validate(File.ReadAllText(Data("config.v1.json"))));
    }

    [Theory]
    [InlineData("""{ "pages": [] }""", "version", "field_missing")]
    [InlineData("""{ "version": 1, "pages": [ { "name": "A" } ] }""", "pages[0].id", "field_missing")]
    [InlineData("""{ "version": 1, "pages": [ { "id": "a" } ] }""", "pages[0].name", "field_missing")]
    [InlineData("""{ "version": 1, "pages": [ { "id": "Grabacion", "name": "A" } ] }""", "pages[0].id", "id_invalid")]
    [InlineData("""{ "version": 1, "pages": [ { "id": "", "name": "A" } ] }""", "pages[0].id", "id_invalid")]
    [InlineData("""{ "version": 1, "pages": [ { "id": "main", "name": "A" } ] }""", "pages[0].id", "id_invalid")]
    [InlineData("""{ "version": 1, "pages": [ { "id": "a", "name": "A" }, { "id": "a", "name": "B" } ] }""", "pages[1].id", "id_repeated")]
    [InlineData("""{ "version": 1, "actions": [ { "id": "con espacio", "name": "X", "steps": [] } ] }""", "actions[0].id", "id_invalid")]
    [InlineData("""{ "version": 1, "actions": [ { "id": "x", "steps": [] } ] }""", "actions[0].name", "field_missing")]
    [InlineData("""{ "version": 1, "actions": [ { "id": "x", "name": "X" } ] }""", "actions[0].steps", "field_missing")]
    [InlineData("""{ "version": 1, "actions": [ { "id": "x", "name": "X", "steps": [ { "marker": "short" } ] } ] }""", "actions[0].steps[0].type", "field_missing")]
    [InlineData("""{ "version": 1, "mainPage": "otra", "pages": [ { "id": "a", "name": "A" } ] }""", "mainPage", "page_not_found")]
    [InlineData("""{ "version": 1, "moduleKey": { "layer": "capa" }, "pages": [ { "id": "a", "name": "A" } ] }""", "moduleKey.layer", "page_not_found")]
    [InlineData("""{ "version": 1, "preflight": [ { "condition": { "type": "obs.profileIs" } } ] }""", "preflight[0].message", "field_missing")]
    [InlineData("""{ "version": 1, "preflight": [ { "message": "x" } ] }""", "preflight[0].condition", "field_missing")]
    public void Finds_a_problem_of_the_file(string json, string where, string code)
    {
        Assert.Contains(Validate(json), problem => problem.Where == where && problem.Code == code && problem.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("""{ "position": 0, "label": "X" }""", "pages[0].keys[0].position", "position_invalid")]
    [InlineData("""{ "position": 13, "label": "X" }""", "pages[0].keys[0].position", "position_invalid")]
    [InlineData("""{ "position": 5, "label": "X" }, { "position": 5, "label": "Y" }""", "pages[0].keys[1].position", "position_repeated")]
    [InlineData("""{ "label": "X" }""", "pages[0].keys[0].position", "field_missing")]
    [InlineData("""{ "position": 1 }""", "pages[0].keys[0].label", "field_missing")]
    [InlineData("""{ "position": 1, "label": "X", "action": "nada" }""", "pages[0].keys[0].action", "action_not_found")]
    [InlineData("""{ "position": 1, "label": "X", "onHold": {} }""", "pages[0].keys[0].onHold.action", "field_missing")]
    [InlineData("""{ "position": 1, "label": "X", "onHold": { "action": "nada" } }""", "pages[0].keys[0].onHold.action", "action_not_found")]
    [InlineData("""{ "position": 1, "label": "X", "hotkey": "Win+F7" }""", "pages[0].keys[0].hotkey", "hotkey_invalid")]
    [InlineData("""{ "position": 1, "label": "X", "hotkey": "Ctrl+F5" }, { "position": 2, "label": "Y", "hotkey": "ctrl+f5" }""", "pages[0].keys[1].hotkey", "hotkey_repeated")]
    public void Finds_a_problem_of_a_key(string keys, string where, string code)
    {
        Assert.Contains(ValidatePage(keys), problem => problem.Where == where && problem.Code == code && problem.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("""{ "action": "x" }""", "hotkeys[0].hotkey", "field_missing")]
    [InlineData("""{ "hotkey": "Ctrl+F5" }""", "hotkeys[0].action", "field_missing")]
    [InlineData("""{ "hotkey": "Ctrl+F5", "action": "nada" }""", "hotkeys[0].action", "action_not_found")]
    [InlineData("""{ "hotkey": "F5", "action": "x" }""", "hotkeys[0].hotkey", "hotkey_invalid")]
    [InlineData("""{ "hotkey": "Ctrl+F5", "action": "x" }, { "hotkey": "Ctrl+F5", "action": "x" }""", "hotkeys[1].hotkey", "hotkey_repeated")]
    public void Finds_a_problem_of_a_general_hotkey(string hotkeys, string where, string code)
    {
        Assert.Contains(ValidatePage("", hotkeys), problem => problem.Where == where && problem.Code == code && problem.Severity == Severity.Error);
    }

    [Fact]
    public void A_key_cannot_repeat_a_general_hotkey()
    {
        IReadOnlyList<Problem> problems = ValidatePage(
            """{ "position": 6, "label": "Inicio", "hotkey": "Ctrl+Shift+Alt+F6" }""",
            """{ "hotkey": "ctrl+shift+alt+f6", "action": "x" }""");

        Problem problem = Assert.Single(problems);
        Assert.Equal("pages[0].keys[0].hotkey", problem.Where);
        Assert.Equal(ProblemCodes.HotkeyRepeated, problem.Code);
    }

    [Fact]
    public void The_same_hotkey_in_two_pages_is_not_a_problem()
    {
        string json = """{ "version": 1, "pages": [ { "id": "a", "name": "A", "keys": [ { "position": 1, "label": "X", "hotkey": "Ctrl+F5" } ] }, { "id": "b", "name": "B", "keys": [ { "position": 1, "label": "Y", "hotkey": "Ctrl+F5" } ] } ] }""";

        Assert.Empty(Validate(json));
    }

    [Fact]
    public void An_id_longer_than_forty_characters_is_invalid()
    {
        string id = new('a', 41);

        Assert.Contains(
            Validate($$"""{ "version": 1, "pages": [ { "id": "{{id}}", "name": "A" } ] }"""),
            problem => problem.Where == "pages[0].id" && problem.Code == ProblemCodes.IdInvalid);
    }

    [Theory]
    [InlineData("""[ "UNO", "DOS", "TRES" ]""")]
    [InlineData("""[ "DEMASIADO" ]""")]
    [InlineData("""[]""")]
    public void A_short_label_out_of_shape_is_a_warning(string shortLabel)
    {
        Problem problem = Assert.Single(ValidatePage($$"""{ "position": 1, "label": "X", "shortLabel": {{shortLabel}} }"""));

        Assert.Equal("pages[0].keys[0].shortLabel", problem.Where);
        Assert.Equal(ProblemCodes.ShortLabelInvalid, problem.Code);
        Assert.Equal(Severity.Warning, problem.Severity);
    }
}
