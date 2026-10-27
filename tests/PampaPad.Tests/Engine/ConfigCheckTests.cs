using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public sealed class ConfigCheckTests : IAsyncLifetime
{
    // La biblioteca cargada tiene grabacion y escenas, y una acción que va a escenas.
    private const string Loaded = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            { "id": "grabacion", "name": "Grabación", "keys": [ { "position": 1, "label": "Escenas", "action": "ir-a-escenas" } ] },
            { "id": "escenas", "name": "Escenas", "keys": [] }
          ],
          "actions": [
            { "id": "ir-a-escenas", "name": "Ir a escenas", "steps": [ { "type": "page.go", "target": "escenas" } ] }
          ]
        }
        """;

    private readonly TempFolder _temp = new();
    private RealCatalog _catalog = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), Loaded);
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        library.Load();
        _catalog = new RealCatalog(library);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _catalog.DisposeAsync();
        _temp.Dispose();
    }

    private static Config Read(string json) => JsonSerializer.Deserialize<Config>(json, Json.Options)!;

    private IReadOnlyList<Problem> All(Config config) => ConfigCheck.All(config, _catalog.Executor, _catalog.Log);

    [Fact]
    public void A_page_go_to_a_page_only_the_candidate_has_is_fine_and_the_loaded_configuration_does_not_see_it()
    {
        // Hallazgo E5-11: el guardado agrega la página sala y un page.go a sala a la vez.
        Config candidate = Read(Loaded.Replace("{ \"id\": \"escenas\", \"name\": \"Escenas\", \"keys\": [] }", "{ \"id\": \"escenas\", \"name\": \"Escenas\", \"keys\": [] }, { \"id\": \"sala\", \"name\": \"Sala\", \"keys\": [] }", StringComparison.Ordinal)
            .Replace("\"target\": \"escenas\"", "\"target\": \"sala\"", StringComparison.Ordinal));
        PadAction goes = candidate.Actions![0];

        Assert.Empty(All(candidate));
        Assert.Equal(ProblemCodes.PageNotFound, Assert.Single(_catalog.Executor.Check(goes)).Code);
        Assert.Empty(_catalog.Executor.Check(goes, candidate));
    }

    [Fact]
    public void A_page_go_to_a_page_the_candidate_removed_is_an_error_even_though_the_loaded_one_still_has_it()
    {
        Config candidate = _catalog.Library.Current with { Pages = [_catalog.Library.Current.Pages![0]] };

        Problem problem = Assert.Single(All(candidate));

        Assert.Equal(("actions[0].steps[0].target", ProblemCodes.PageNotFound, Severity.Error), (problem.Where, problem.Code, problem.Severity));
        Assert.Empty(_catalog.Executor.Check(_catalog.Library.Current.Actions![0]));
    }

    [Fact]
    public void The_problems_of_the_file_and_of_the_steps_come_together_with_their_place_and_without_repeats()
    {
        // Un paso sin "type" lo informan los dos, el validador y el ejecutor: queda una vez. El del chequeo previo lleva su lugar.
        Config candidate = Read("""
            {
              "version": 1,
              "actions": [ { "id": "a", "name": "A", "steps": [ { "marker": "short" }, { "type": "obs.marker", "marker": "inventado" } ] } ],
              "preflight": [ { "condition": { "type": "obs.profileIs" }, "message": "Perfil" } ]
            }
            """);

        IReadOnlyList<Problem> problems = All(candidate);

        Assert.Single(problems, problem => problem.Where == "actions[0].steps[0].type");
        Assert.Contains(problems, problem => problem.Where == "actions[0].steps[1].marker" && problem.Code == ProblemCodes.ParameterInvalid && problem.Severity == Severity.Warning);
        Assert.Contains(problems, problem => problem.Where.StartsWith("preflight[0].condition", StringComparison.Ordinal) && problem.Code == ProblemCodes.ParameterInvalid);
        Assert.Equal(problems.Count, problems.DistinctBy(problem => (problem.Where, problem.Code)).Count());
    }

    [Fact]
    public void A_step_type_that_throws_while_validating_is_a_problem_of_its_action_and_a_line_in_the_log()
    {
        var broken = new FakeStep("obs.marker") { Validation = _ => throw new InvalidOperationException("Validate roto a propósito") };
        var executor = new Executor([broken], TimeProvider.System);
        Config candidate = Read("""{ "version": 1, "actions": [ { "id": "a", "name": "A", "steps": [ { "type": "obs.marker" } ] } ] }""");

        Problem problem = Assert.Single(ConfigCheck.Steps(candidate, executor, _catalog.Log));

        Assert.Equal(("actions[0]", ProblemCodes.ParameterInvalid, Severity.Error), (problem.Where, problem.Code, problem.Severity));
        Assert.Contains("Validate roto a propósito", problem.Message, StringComparison.Ordinal);
        Assert.Contains(_catalog.Log.Recent(), entry => entry.Module == "config" && entry.Code == ErrorCodes.InternalError);
    }

    [Fact]
    public async Task The_example_library_has_no_problem_with_the_real_catalog()
    {
        // Con sus scripts: una carpeta sin scripts da el aviso de script_not_found.
        Directory.CreateDirectory(_temp.Combine("ejemplo"));
        File.WriteAllText(_temp.Combine("ejemplo", "config.json"), DispatcherRig.Example);
        TestChains.CopyExampleScripts(_temp.Combine("ejemplo"));
        var library = new CoreLibrary(_temp.Combine("ejemplo"), _temp.Combine("backup-ejemplo"));
        library.Load();
        await using var catalog = new RealCatalog(library);

        Assert.Empty(ConfigCheck.All(library.Current, catalog.Executor, catalog.Log));
    }

    [Fact]
    public void A_step_type_without_a_rule_of_its_own_validates_the_same_against_a_candidate()
    {
        var plain = new FakeStep("obs.marker") { Validation = _ => [new Problem("marker", ProblemCodes.ParameterInvalid, "No.", Severity.Warning)] };
        var executor = new Executor([plain], TimeProvider.System);
        Config candidate = Read("""{ "version": 1, "actions": [ { "id": "a", "name": "A", "steps": [ { "type": "obs.marker" } ] } ] }""");
        PadAction action = candidate.Actions![0];

        Assert.Equal(executor.Check(action), executor.Check(action, candidate));
    }
}
