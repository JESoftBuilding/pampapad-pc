using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>ScriptFolder.Inspect y ScriptCatalog: cada fila de scripts/, su estado, sus problemas y quién la usa (plan de la etapa 8, E8-3).</summary>
public class ScriptCatalogTests
{
    [Fact]
    public async Task Each_thing_in_scripts_is_a_row_with_its_state_and_problems()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("sano", "@echo off\necho hola\n", """, "name": "Sano", "tags": ["banco"]""");
        Directory.CreateDirectory(Path.Combine(Scripts(rig), "sinficha"));
        File.WriteAllText(Path.Combine(Scripts(rig), "sinficha", "a.cmd"), "@echo off");
        Directory.CreateDirectory(Path.Combine(Scripts(rig), "rota"));
        File.WriteAllText(Path.Combine(Scripts(rig), "rota", "script.json"), "{ no es json");
        File.Delete(Path.Combine(rig.AddCmd("sinentrada", "@echo off\n"), "sinentrada.cmd"));
        rig.AddCmd("nuevo", "@echo off\n", """, "contract": 2""");
        rig.Add("texto", "leeme.txt", "hola"u8.ToArray());
        File.WriteAllText(Path.Combine(Scripts(rig), "suelto.cmd"), "@echo off");
        using var runs = Runs(rig);

        Dictionary<string, ScriptView> rows = ScriptCatalog.List(new ScriptsSources(rig.Library, rig.Runner, runs, new FakeShellDialogs(), Path.Combine(rig.LibraryFolder, "respaldo"), TimeProvider.System)).ToDictionary(row => row.Id);

        Assert.Equal(["nuevo", "rota", "sano", "sinentrada", "sinficha", "suelto.cmd", "texto"], rows.Keys.Order(StringComparer.Ordinal));
        ScriptView sano = rows["sano"];
        Assert.Equal(ScriptStatus.Ready, sano.Status);
        Assert.Empty(sano.Problems);
        Assert.Equal("Sano", sano.Name);
        Assert.Equal("sano.cmd", sano.Entry);
        Assert.True(sano.Wait);
        Assert.Equal(15, sano.TimeoutSec);
        Assert.Equal(["banco"], sano.Tags);
        Assert.Null(sano.LastRun);
        AssertRow(rows["sinficha"], ScriptStatus.NoManifest, ProblemCodes.ManifestMissing);
        AssertRow(rows["suelto.cmd"], ScriptStatus.NoManifest, ProblemCodes.ManifestMissing);
        AssertRow(rows["rota"], ScriptStatus.Problem, ProblemCodes.ManifestUnreadable);
        AssertRow(rows["sinentrada"], ScriptStatus.Problem, ProblemCodes.EntryMissing);
        AssertRow(rows["nuevo"], ScriptStatus.Problem, ProblemCodes.ContractNewer);
        AssertRow(rows["texto"], ScriptStatus.Problem, ProblemCodes.NoInterpreter);
        Assert.Equal("rota", rows["rota"].Name);
    }

    [Fact]
    public async Task Who_uses_it_comes_from_the_actions_that_run_it_and_where_each_one_is()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("bancocam", "@echo off\n");
        File.WriteAllText(Path.Combine(rig.LibraryFolder, "config.json"), """
            {
              "version": 1,
              "pages": [ { "id": "banco", "name": "Banco", "keys": [
                { "position": 1, "label": "Cámara", "action": "ir-al-banco" },
                { "position": 2, "label": "Otra", "action": "nada", "onHold": { "action": "ir-al-banco" } }
              ] } ],
              "actions": [
                { "id": "nada", "name": "Nada", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
                { "id": "ir-al-banco", "name": "Ir al banco", "steps": [ { "type": "obs.marker", "marker": "inicio" }, { "type": "script.run", "script": "bancocam" } ] }
              ],
              "hotkeys": [ { "hotkey": "Ctrl+Shift+Alt+F2", "action": "ir-al-banco" } ]
            }
            """);
        rig.Library.Load();
        using var runs = Runs(rig);

        ScriptView row = Assert.Single(ScriptCatalog.List(new ScriptsSources(rig.Library, rig.Runner, runs, new FakeShellDialogs(), Path.Combine(rig.LibraryFolder, "respaldo"), TimeProvider.System)));

        ScriptUse use = Assert.Single(row.UsedBy);
        Assert.Equal("ir-al-banco", use.Action);
        Assert.Equal("Ir al banco", use.ActionName);
        Assert.Equal("actions[1].steps[1].script", use.Where);
        Assert.Equal(
            [(PlaceKind.Key, "pages[0].keys[0].action"), (PlaceKind.Hold, "pages[0].keys[1].onHold.action"), (PlaceKind.Hotkey, "hotkeys[0].action")],
            use.Places.Select(place => (place.Kind, place.Where)));
        Assert.Equal(2, use.Places[1].Position);
        Assert.Equal("Ctrl+Shift+Alt+F2", use.Places[2].Hotkey);
    }

    private static void AssertRow(ScriptView row, ScriptStatus status, string code)
    {
        Assert.Equal(status, row.Status);
        Assert.Contains(row.Problems, problem => problem.Code == code);
    }

    private static string Scripts(ScriptRig rig) => Path.Combine(rig.LibraryFolder, "scripts");

    private static ScriptRuns Runs(ScriptRig rig) => new(rig.Log, Path.Combine(rig.LibraryFolder, "no-hay-registro"), TimeProvider.System);
}
