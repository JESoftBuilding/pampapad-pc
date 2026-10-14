using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Lo que las pruebas del plan no fijaban y una mutación a mano mostró (E3-2): los bordes de los ids y de shortLabel,
/// los ids reservados, el $ de .NET, las condiciones sin tipo, lo que pasa con huecos en las listas y el orden de lo que se informa.
/// </summary>
public class ValidatorEdgeTests
{
    private const string OnePage = """{ "version": 1, "pages": [ { "id": "a", "name": "A", "keys": [ __KEYS__ ] } ], "actions": [ { "id": "x", "name": "X", "steps": [] } ] }""";

    private static IReadOnlyList<Problem> Validate(string json) =>
        Validator.Validate(JsonSerializer.Deserialize<Config>(json, Json.Options)!);

    private static IReadOnlyList<Problem> ValidatePage(string keys) =>
        Validate(OnePage.Replace("__KEYS__", keys, StringComparison.Ordinal));

    private static string PageAndAction(string id) =>
        $$"""{ "version": 1, "pages": [ { "id": "{{id}}", "name": "A" } ], "actions": [ { "id": "{{id}}", "name": "X", "steps": [] } ] }""";

    [Fact]
    public void An_id_of_exactly_forty_characters_is_valid()
    {
        Assert.Empty(Validate(PageAndAction(new string('a', Validator.MaxIdLength))));
    }

    [Theory]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("main")]
    public void A_reserved_id_is_invalid_in_pages_and_in_actions(string id)
    {
        IReadOnlyList<Problem> problems = Validate(PageAndAction(id));

        Assert.Contains(problems, problem => problem.Where == "pages[0].id" && problem.Code == ProblemCodes.IdInvalid);
        Assert.Contains(problems, problem => problem.Where == "actions[0].id" && problem.Code == ProblemCodes.IdInvalid);
    }

    [Theory]
    [InlineData("a-")]
    [InlineData("-a")]
    [InlineData("a--b")]
    [InlineData("A")]
    [InlineData("a_b")]
    [InlineData("a b")]
    [InlineData("ñandú")]
    [InlineData("a\\n")]
    public void An_id_out_of_shape_is_invalid(string id)
    {
        // "a\\n" llega al JSON como el escape \n: el id que se lee es "a" y un salto de línea. El $ de .NET lo dejaría pasar.
        IReadOnlyList<Problem> problems = Validate(PageAndAction(id));

        Assert.Contains(problems, problem => problem.Where == "pages[0].id" && problem.Code == ProblemCodes.IdInvalid);
        Assert.Contains(problems, problem => problem.Where == "actions[0].id" && problem.Code == ProblemCodes.IdInvalid);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData("a-b")]
    [InlineData("marca-short")]
    [InlineData("x-1-y2")]
    public void An_id_in_shape_is_valid(string id)
    {
        Assert.Empty(Validate(PageAndAction(id)));
    }

    [Fact]
    public void A_repeated_action_id_is_found_at_the_second_one()
    {
        Problem problem = Assert.Single(Validate("""{ "version": 1, "actions": [ { "id": "x", "name": "X", "steps": [] }, { "id": "x", "name": "Y", "steps": [] } ] }"""));

        Assert.Equal("actions[1].id", problem.Where);
        Assert.Equal(ProblemCodes.IdRepeated, problem.Code);
    }

    [Theory]
    [InlineData("""{ "version": 1, "actions": [ { "id": "x", "name": "X", "steps": [ { "type": "obs.marker", "onlyIf": { "negate": true } } ] } ] }""", "actions[0].steps[0].onlyIf.type")]
    [InlineData("""{ "version": 1, "preflight": [ { "condition": {}, "message": "x" } ] }""", "preflight[0].condition.type")]
    public void A_condition_without_type_is_a_problem(string json, string where)
    {
        Problem problem = Assert.Single(Validate(json));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.FieldMissing, problem.Code);
        Assert.Equal(Severity.Error, problem.Severity);
    }

    [Theory]
    [InlineData("""[ "UNO" ]""")]
    [InlineData("""[ "UNO", "DOS" ]""")]
    [InlineData("""[ "ABCDEF", "ABCDEF" ]""")]
    public void A_short_label_in_shape_has_no_problem(string shortLabel)
    {
        Assert.Empty(ValidatePage($$"""{ "position": 1, "label": "X", "shortLabel": {{shortLabel}} }"""));
    }

    [Theory]
    [InlineData("""[ "ABCDEFG" ]""")]
    [InlineData("""[ "UNO", "ABCDEFG" ]""")]
    [InlineData("""[ null ]""")]
    [InlineData("""[ "UNO", null ]""")]
    public void A_short_label_with_a_bad_line_is_a_warning(string shortLabel)
    {
        Problem problem = Assert.Single(ValidatePage($$"""{ "position": 1, "label": "X", "shortLabel": {{shortLabel}} }"""));

        Assert.Equal(ProblemCodes.ShortLabelInvalid, problem.Code);
        Assert.Equal(Severity.Warning, problem.Severity);
    }

    [Fact]
    public void The_modifiers_in_another_order_are_the_same_hotkey()
    {
        Problem problem = Assert.Single(ValidatePage(
            """{ "position": 1, "label": "X", "hotkey": "Shift+Ctrl+F5" }, { "position": 2, "label": "Y", "hotkey": "Ctrl+Shift+F5" }"""));

        Assert.Equal("pages[0].keys[1].hotkey", problem.Where);
        Assert.Equal(ProblemCodes.HotkeyRepeated, problem.Code);
    }

    [Fact]
    public void An_empty_hotkey_text_is_unreadable_and_not_the_same_as_no_hotkey()
    {
        Problem problem = Assert.Single(ValidatePage("""{ "position": 1, "label": "X", "hotkey": "" }"""));

        Assert.Equal("pages[0].keys[0].hotkey", problem.Where);
        Assert.Equal(ProblemCodes.HotkeyInvalid, problem.Code);
    }

    [Fact]
    public void An_empty_file_only_lacks_its_version()
    {
        Problem problem = Assert.Single(Validate("{}"));

        Assert.Equal("version", problem.Where);
        Assert.Equal(ProblemCodes.FieldMissing, problem.Code);
    }

    [Theory]
    [InlineData("""{ "pages": [ null ], "actions": [ null ], "hotkeys": [ null ], "preflight": [ null ] }""")]
    [InlineData("""{ "pages": [ { "keys": [ null ] } ], "actions": [ { "steps": [ null ] } ] }""")]
    [InlineData("""{ "pages": [ { "id": "a", "name": "A", "keys": [ { "onHold": {}, "shortLabel": [ null ] } ] } ] }""")]
    [InlineData("""{ "mainPage": "a", "moduleKey": {}, "console": {} }""")]
    public void A_file_full_of_holes_is_reported_and_does_not_throw(string json)
    {
        Exception? thrown = Record.Exception(() => Validate(json));

        Assert.Null(thrown);
    }

    [Fact]
    public void Every_problem_is_reported_in_file_order_with_the_index_it_has_in_the_file()
    {
        string json = """
            {
              "version": 1,
              "pages": [
                null,
                { "id": "a", "name": "A", "keys": [ { "position": 13, "label": "X" }, null, { "position": 2, "label": "Y", "action": "nada" } ] }
              ],
              "actions": [ { "id": "x", "name": "X", "steps": [] } ]
            }
            """;
        string[] expected = ["pages[1].keys[0].position", "pages[1].keys[2].action"];

        Assert.Equal(expected, Validate(json).Select(problem => problem.Where));
    }

    [Fact]
    public void A_reference_to_an_invalid_id_reports_only_the_id()
    {
        Problem problem = Assert.Single(Validate("""{ "version": 1, "mainPage": "Grabacion", "pages": [ { "id": "Grabacion", "name": "A" } ] }"""));

        Assert.Equal("pages[0].id", problem.Where);
        Assert.Equal(ProblemCodes.IdInvalid, problem.Code);
    }
}
