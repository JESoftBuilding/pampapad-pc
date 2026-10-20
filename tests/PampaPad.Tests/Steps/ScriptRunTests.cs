using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public class ScriptRunTests
{
    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static PadAction Chain(string steps) =>
        JsonSerializer.Deserialize<PadAction>($$"""{ "id": "ir-al-banco", "name": "Ir al banco", "steps": {{steps}} }""", Json.Options)!;

    [Fact]
    public async Task A_script_that_ends_well_gives_its_last_line_to_the_report_of_its_step()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("bancocam", "@echo off\necho bancocam corriendo fuera de pantalla (1823x850).\n");
        var executor = new Executor([new ScriptRun(rig.Runner, rig.Library)], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "script.run", "script": "bancocam" } ]"""), ScriptRig.Key, CancellationToken.None);

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("bancocam corriendo fuera de pantalla (1823x850).", result.Steps[0].Message);
    }

    [Fact]
    public async Task A_script_that_exits_with_1_stops_the_chain_at_its_step_with_its_last_line()
    {
        // Lo que hace bancocam con el celular desenchufado: escribe el motivo y sale con 1 (bancocam.ps1:17).
        await using var rig = new ScriptRig();
        rig.AddCmd("bancocam", "@echo off\necho scrcpy no abrio la ventana (celu conectado? depuracion USB? celu desbloqueado?)\nexit /b 1\n");
        var after = new FakeStep("obs.scene");
        var executor = new Executor([new ScriptRun(rig.Runner, rig.Library), after], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "script.run", "script": "bancocam" }, { "type": "obs.scene" } ]"""), ScriptRig.Key, CancellationToken.None);

        Assert.Equal(ErrorCodes.ScriptFailed, result.Failure?.Code);
        Assert.Equal(1, result.StepNumber);
        Assert.Equal("script.run", result.StepType);
        Assert.Equal("El script \"bancocam\" salió con 1: scrcpy no abrio la ventana (celu conectado? depuracion USB? celu desbloqueado?)", result.Failure?.Message);
        Assert.Empty(after.Runs);
    }

    [Fact]
    public async Task The_script_knows_which_action_ran_it()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("quien", "@echo off\necho %PAMPAPAD_ACTION% %PAMPAPAD_TRIGGER%\n");
        var executor = new Executor([new ScriptRun(rig.Runner, rig.Library)], TimeProvider.System);
        var tray = new Invocation(Guid.NewGuid(), Trigger.Tray, null, null, null, null);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "script.run", "script": "quien" } ]"""), tray, CancellationToken.None);

        Assert.Equal("ir-al-banco tray", result.Steps[0].Message);
    }

    [Fact]
    public async Task The_limit_of_the_step_wins_over_the_one_of_the_manifest()
    {
        var clock = new FakeTimeProvider();
        await using var rig = new ScriptRig(clock);
        string folder = rig.AddPs1(
            "lento",
            "Set-Content -Path (Join-Path $PSScriptRoot 'vivo.txt') -Value 1\nStart-Sleep -Seconds 60\n",
            """, "timeoutSec": 60""");
        var step = new ScriptRun(rig.Runner, rig.Library);

        Task<StepResult> running = step.RunAsync(Parse("""{ "type": "script.run", "script": "lento", "timeoutSec": 2 }"""), ScriptRig.Key, CancellationToken.None);
        await Poll.Until(() => File.Exists(Path.Combine(folder, "vivo.txt")), 20_000);
        clock.Advance(TimeSpan.FromSeconds(2));
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.ScriptTimeout, result.Failure?.Code);
        Assert.Contains("tope de 2 s", result.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_wait_of_the_step_wins_over_the_one_of_the_manifest()
    {
        // Esperándolo, saldría con 4 y el paso fallaría. Sin esperar termina bien enseguida y el 4 queda en el registro (02 §9, punto 11).
        await using var rig = new ScriptRig();
        rig.AddCmd("suelto", "@echo off\nping -n 2 127.0.0.1 >nul\nexit /b 4\n", """, "wait": true""");

        StepResult result = await new ScriptRun(rig.Runner, rig.Library).RunAsync(
            Parse("""{ "type": "script.run", "script": "suelto", "wait": false }"""), ScriptRig.Key, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Null(result.Message);
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Message == "suelto (sin esperar) salió con 4."), 15_000);
    }

    [Fact]
    public async Task Cancelling_the_chain_kills_the_script_of_the_step_and_what_it_launched()
    {
        // El Executor corta la cadena igual con su WaitAsync: esta mira que el token llegue al script y que no quede vivo lo que lanzó,
        // como el scrcpy de bancocam (01 §4.5).
        await using var rig = new ScriptRig();
        string folder = rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 60""");
        var executor = new Executor([new ScriptRun(rig.Runner, rig.Library)], TimeProvider.System);
        using var cancel = new CancellationTokenSource();

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "script.run", "script": "lento" } ]"""), ScriptRig.Key, cancel.Token);
        int child = await ScriptRig.ChildAsync(folder);
        await cancel.CancelAsync();
        ActionResult result = await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.True(ScriptRig.Gone(child));
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Message == "Se canceló el script \"lento\": se cortó, con lo que había lanzado."), 15_000);
    }

    [Fact]
    public async Task A_missing_script_is_a_problem_of_the_step_and_the_action_does_not_run_even_with_an_onlyIf()
    {
        // El espejo ya abierto, como en ir al banco: la ventana está y el onlyIf negado saltearía el paso. Igual no corre nada, ni se
        // pregunta la condición. Con NotMet el paso iba a correr y la prueba no miraba el salteo (revisión de E5-10).
        await using var rig = new ScriptRig();
        var open = FakeCondition.Met("window.exists");
        var executor = new Executor([new ScriptRun(rig.Runner, rig.Library)], [open.Type], TimeProvider.System);
        PadAction action = Chain("""[ { "type": "script.run", "script": "bancocam", "onlyIf": { "type": "window.exists", "negate": true } } ]""");

        Problem problem = Assert.Single(executor.Check(action));
        ActionResult result = await executor.RunAsync(action, ScriptRig.Key, CancellationToken.None);

        Assert.Equal("steps[0].script", problem.Where);
        Assert.Equal(ErrorCodes.ScriptNotFound, problem.Code);
        Assert.Equal(Severity.Warning, problem.Severity);
        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Equal(StepOutcome.NotRun, result.Steps[0].Outcome);
        Assert.Equal(0, open.Evaluations);
    }

    [Fact]
    public async Task A_folder_without_manifest_is_a_problem_with_its_own_code()
    {
        await using var rig = new ScriptRig();
        Directory.CreateDirectory(Path.Combine(rig.LibraryFolder, "scripts", "suelto"));

        Problem problem = Assert.Single(new ScriptRun(rig.Runner, rig.Library).Validate(Parse("""{ "type": "script.run", "script": "suelto" }""")));

        Assert.Equal(ErrorCodes.ScriptWithoutManifest, problem.Code);
    }

    [Theory]
    [InlineData("""{ "type": "script.run" }""", "script")]
    [InlineData("""{ "type": "script.run", "script": "" }""", "script")]
    [InlineData("""{ "type": "script.run", "script": "hola", "timeoutSec": 0 }""", "timeoutSec")]
    [InlineData("""{ "type": "script.run", "script": "hola", "timeoutSec": 60 }""", "timeoutSec")]
    [InlineData("""{ "type": "script.run", "script": "hola", "wait": "no" }""", "")]
    public async Task A_bad_parameter_is_a_problem_of_the_step(string json, string where)
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("hola", "@echo off\n");

        Problem problem = Assert.Single(new ScriptRun(rig.Runner, rig.Library).Validate(Parse(json)));

        Assert.Equal(where, problem.Where);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(59)]
    public async Task The_limit_of_the_step_goes_from_1_s_to_under_the_limit_of_the_action(int timeoutSec)
    {
        // 02 §5: de 1 a 59; 60 es el tope de la acción, que vence primero (decisión 35). La de arriba prueba sólo lo que se rechaza: un
        // borde corrido no la haría fallar (revisión de E5-10).
        await using var rig = new ScriptRig();
        rig.AddCmd("hola", "@echo off\n");

        Assert.Empty(new ScriptRun(rig.Runner, rig.Library).Validate(Parse($$"""{ "type": "script.run", "script": "hola", "timeoutSec": {{timeoutSec}} }""")));
    }

    [Fact]
    public async Task The_problem_at_the_limit_says_up_to_where_it_goes_and_why()
    {
        // Es lo que se lee en el editor con la tecla rota, y el número es el de la decisión 35 (revisión de E5-23).
        await using var rig = new ScriptRig();
        rig.AddCmd("hola", "@echo off\n");

        Problem problem = Assert.Single(new ScriptRun(rig.Runner, rig.Library).Validate(Parse("""{ "type": "script.run", "script": "hola", "timeoutSec": 60 }""")));

        Assert.Equal("timeoutSec va en segundos, de 1 a 59: con 60 vence antes el tope de la acción.", problem.Message);
    }
}
