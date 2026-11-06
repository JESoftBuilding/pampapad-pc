using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>El validador de page.overlay: el target sigue las reglas de page.go (plan de la 1.3.0, decisión 5; problema 4 y Decidido 35).</summary>
public sealed class PageOverlayTests : IDisposable
{
    private const string Config = """
        {
          "version": 1,
          "pages": [
            { "id": "grabacion", "keys": [] },
            { "id": "luces", "show": false, "type": "keys" },
            { "id": "sin-pc", "type": "local" },
            { "id": "de-obs", "type": "scenes" }
          ]
        }
        """;

    private readonly TempFolder _temp = new();
    private readonly PageOverlay _step;

    public PageOverlayTests()
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), Config);
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        library.Load();
        _step = new PageOverlay(library, _ => null);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("ninguna")]
    [InlineData("main")]
    public void A_target_that_names_no_page_is_problem_4(string target)
    {
        Problem problem = Assert.Single(Validate(target));

        Assert.Equal(("target", ProblemCodes.PageNotFound, Severity.Error), (problem.Where, problem.Code, problem.Severity));
    }

    [Theory]
    [InlineData("sin-pc", "local")]
    [InlineData("de-obs", "scenes")]
    public void A_target_that_is_not_of_keys_is_problem_4(string target, string type)
    {
        Problem problem = Assert.Single(Validate(target));

        Assert.Equal(("target", ProblemCodes.PageNotFound, Severity.Error), (problem.Where, problem.Code, problem.Severity));
        Assert.Equal($"page.overlay nombra la página \"{target}\", de tipo {type}, que no es de teclas: no puede ir encima.", problem.Message);
    }

    [Theory]
    [InlineData("luces")]
    [InlineData("grabacion")]
    public void A_page_of_keys_is_fine_with_or_without_show(string target) => Assert.Empty(Validate(target));

    [Fact]
    public void Without_target_it_is_parameter_invalid() =>
        Assert.Equal(ProblemCodes.ParameterInvalid, Assert.Single(_step.Validate(JsonSerializer.Deserialize<Step>("""{ "type": "page.overlay" }""", Json.Options)!)).Code);

    private IReadOnlyList<Problem> Validate(string target) =>
        [.. _step.Validate(JsonSerializer.Deserialize<Step>($$"""{ "type": "page.overlay", "target": "{{target}}" }""", Json.Options)!)];
}
