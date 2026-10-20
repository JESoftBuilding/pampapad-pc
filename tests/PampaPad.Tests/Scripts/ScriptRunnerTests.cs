using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ScriptRunnerTests
{
    [Fact]
    public async Task A_cmd_saved_in_the_oem_page_says_its_last_line_with_its_accents()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("hola", "@echo off\necho primera\necho.\necho acción lista\n");
        var lines = new List<string>();

        Result<ScriptResult> result = await rig.RunAsync("hola", onLine: lines.Add);

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(0, result.Value!.ExitCode);
        Assert.Equal("acción lista", result.Value.Message);
        Assert.Equal(new[] { "primera", "acción lista" }, lines);
    }

    [Fact]
    public async Task A_ps1_with_bom_also_comes_out_in_the_oem_page()
    {
        await using var rig = new ScriptRig();
        rig.AddPs1("hola", "Write-Output 'primera'\nWrite-Host 'cámara lista'\n");

        Result<ScriptResult> result = await rig.RunAsync("hola");

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("cámara lista", result.Value!.Message);
    }

    [Fact]
    public async Task An_exit_code_that_is_not_zero_is_script_failed_with_the_last_line()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("bancocam", "@echo off\necho scrcpy no abrio la ventana (celu conectado?)\nexit /b 1\n");

        Result<ScriptResult> result = await rig.RunAsync("bancocam");

        Assert.Equal(ErrorCodes.ScriptFailed, result.Failure?.Code);
        Assert.Equal("El script \"bancocam\" salió con 1: scrcpy no abrio la ventana (celu conectado?)", result.Failure?.Message);
        Assert.Contains(rig.Log.Recent(), entry => entry.Code == ErrorCodes.ScriptFailed && entry.RunId == ScriptRig.Key.RunId);
    }

    [Fact]
    public async Task The_variables_of_the_contract_arrive_and_it_runs_in_the_scripts_folder()
    {
        await using var rig = new ScriptRig();
        string folder = rig.AddCmd("vars", "@echo off\necho %PAMPAPAD_ACTION%^|%PAMPAPAD_TRIGGER%^|%PAMPAPAD_SOURCE%^|%CONOCIMIENTO%\necho %PAMPAPAD_SCRIPT_DIR%\necho %PAMPAPAD_LIBRARY%\ncd\n");
        var lines = new List<string>();

        Result<ScriptResult> result = await rig.RunAsync("vars", onLine: lines.Add);

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal($"ir-al-banco|key|console|{ScriptRig.Root}", lines[0]);
        Assert.Equal(folder, lines[1], ignoreCase: true);
        Assert.Equal(rig.LibraryFolder, lines[2], ignoreCase: true);
        Assert.Equal(Path.Combine(rig.LibraryFolder, "scripts"), lines[3].TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public async Task Passing_the_limit_kills_the_script_and_what_it_launched()
    {
        var clock = new FakeTimeProvider();
        await using var rig = new ScriptRig(clock);
        string folder = rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 5""");

        Task<Result<ScriptResult>> running = rig.RunAsync("lento");
        int child = await ScriptRig.ChildAsync(folder);
        clock.Advance(TimeSpan.FromSeconds(5));
        Result<ScriptResult> result = await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.ScriptTimeout, result.Failure?.Code);
        Assert.Equal("El script \"lento\" pasó el tope de 5 s: se cortó, con lo que había lanzado.", result.Failure?.Message);
        Assert.True(ScriptRig.Gone(child));
    }

    [Fact]
    public async Task Cancelling_the_chain_kills_the_script_too()
    {
        await using var rig = new ScriptRig();
        string folder = rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 60""");
        using var cancel = new CancellationTokenSource();

        Task<Result<ScriptResult>> running = rig.RunAsync("lento", cancel.Token);
        int child = await ScriptRig.ChildAsync(folder);
        await cancel.CancelAsync();
        Result<ScriptResult> result = await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.True(ScriptRig.Gone(child));
    }

    [Fact]
    public async Task Without_waiting_it_ends_at_once_and_the_exit_code_goes_to_the_log()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("suelto", "@echo off\nping -n 2 127.0.0.1 >nul\nexit /b 4\n", """, "wait": false""");

        Result<ScriptResult> result = await rig.RunAsync("suelto");

        Assert.True(result.Ok);
        Assert.Null(result.Value!.ExitCode);
        Assert.Null(result.Value.Message);
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Message == "suelto (sin esperar) salió con 4."), 15_000);
    }

    [Fact]
    public async Task The_step_can_ask_to_wait_even_if_the_manifest_says_not_to()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("suelto", "@echo off\necho terminé\n", """, "wait": false""");

        Result<ScriptResult> result = await rig.RunAsync("suelto", wait: true);

        Assert.Equal(0, result.Value!.ExitCode);
        Assert.Equal("terminé", result.Value.Message);
    }

    [Fact]
    public async Task A_script_that_writes_a_lot_keeps_only_its_last_line()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("verborragico", "@echo off\nfor /l %%i in (1,1,5000) do echo renglon %%i\necho fin\n");
        var count = 0;

        Result<ScriptResult> result = await rig.RunAsync("verborragico", onLine: _ => count++);

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("fin", result.Value!.Message);
        Assert.Equal(5001, count);
    }

    [Fact]
    public async Task A_child_that_keeps_the_output_open_does_not_hold_the_step()
    {
        // El hijo escribe "tarde" unos 3 s después, con el paso ya terminado. Antes deja un archivo al lado del script, para avisar que ya
        // va: con la salida cerrada, el echo falla y cmd no sigue.
        await using var rig = new ScriptRig();
        string folder = rig.AddCmd("lanzador", "@echo off\nstart /b cmd /c \"ping -n 4 127.0.0.1 >nul & echo x> lanzador\\tarde.txt & echo tarde\"\necho listo\n");
        var lines = new ConcurrentQueue<string>();
        var watch = Stopwatch.StartNew();

        Result<ScriptResult> result = await rig.RunAsync("lanzador", onLine: lines.Enqueue);
        TimeSpan took = watch.Elapsed;
        await Poll.Until(() => File.Exists(Path.Combine(folder, "tarde.txt")), 15_000);
        await Task.Delay(500);

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("listo", result.Value!.Message);
        Assert.True(took < TimeSpan.FromSeconds(5), $"tardó {took}");
        Assert.Contains(rig.Log.Recent(), entry => entry.Message.StartsWith("La salida siguió abierta", StringComparison.Ordinal));

        // Se dejó de leer de verdad: lo que el hijo escribió después no le llegó a nadie, y la lectura se cortó y soltó su hilo.
        Assert.Equal(new[] { "listo" }, lines);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Message.Contains("no se cortó", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("noesta", ErrorCodes.ScriptNotFound)]
    [InlineData("sinficha", ErrorCodes.ScriptWithoutManifest)]
    public async Task A_script_that_cannot_be_found_fails_without_launching_anything(string id, string code)
    {
        await using var rig = new ScriptRig();
        Directory.CreateDirectory(Path.Combine(rig.LibraryFolder, "scripts", "sinficha"));

        Result<ScriptResult> result = await rig.RunAsync(id);

        Assert.Equal(code, result.Failure?.Code);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Message.Contains("arrancó", StringComparison.Ordinal));
    }
}
