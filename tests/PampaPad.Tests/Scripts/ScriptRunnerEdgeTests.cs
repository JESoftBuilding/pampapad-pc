using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que las pruebas del plan no fijaban de ScriptRunner: los bordes que encontró la revisión de E5-6 (2026-10-19).</summary>
public class ScriptRunnerEdgeTests
{
    [Fact]
    public async Task Without_waiting_the_limit_still_kills_the_script_and_what_it_launched()
    {
        // 02 §9, punto 11: sin esperar, el paso termina enseguida pero el tope sigue corriendo.
        var clock = new FakeTimeProvider();
        await using var rig = new ScriptRig(clock);
        string folder = rig.AddPs1("suelto", ScriptRig.LaunchesPing, """, "wait": false, "timeoutSec": 5""");

        Result<ScriptResult> result = await rig.RunAsync("suelto");
        int child = await ScriptRig.ChildAsync(folder);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.True(result.Ok, result.Failure?.Message);
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Code == ErrorCodes.ScriptTimeout && entry.Message == "suelto (sin esperar) pasó el tope de 5 s: se cortó."), 15_000);
        Assert.True(ScriptRig.Gone(child));
    }

    [Fact]
    public async Task A_script_that_asks_for_input_gets_nothing_and_goes_on()
    {
        // 02 §9, punto 7: la entrada se cierra al arrancar. Abierta, set /p esperaría hasta el tope.
        await using var rig = new ScriptRig();
        rig.AddCmd("pregunta", "@echo off\nset /p respuesta=\necho sin entrada\n", """, "timeoutSec": 10""");

        Result<ScriptResult> result = await rig.RunAsync("pregunta");

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("sin entrada", result.Value!.Message);
    }

    [Fact]
    public async Task A_chain_already_cancelled_does_not_launch_the_script()
    {
        await using var rig = new ScriptRig();
        string folder = rig.AddCmd("efecto", "@echo off\necho x> efecto\\corrio.txt\n");
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        Result<ScriptResult> result = await rig.RunAsync("efecto", cancel.Token);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal("Se canceló el script \"efecto\" antes de arrancar.", result.Failure?.Message);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Message.Contains("arrancó", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(folder, "corrio.txt")));
    }

    [Fact]
    public async Task A_limit_longer_than_the_clock_takes_is_cut_and_the_script_runs()
    {
        // Un CancellationTokenSource no acepta más de unos 49,7 días: sin acotarlo, tiraba con el script ya lanzado y sin tope.
        await using var rig = new ScriptRig();
        rig.AddCmd("largo", "@echo off\necho listo\n", """, "timeoutSec": 2147483647""");

        Result<ScriptResult> result = await rig.RunAsync("largo");

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("listo", result.Value!.Message);
        Assert.Contains(rig.Log.Recent(), entry => entry.Message == "largo: arrancó (largo.cmd, tope 4294967 s).");
    }

    [Fact]
    public async Task A_listener_that_throws_is_dropped_and_the_output_is_still_read()
    {
        // Si se dejara de leer, el script se trabaría con la salida llena (unos 4 KB) y terminaría en un tope que no pasó.
        await using var rig = new ScriptRig();
        rig.AddCmd("verborragico", "@echo off\nfor /l %%i in (1,1,5000) do echo renglon %%i\necho fin\n", """, "timeoutSec": 10""");
        var calls = 0;

        Result<ScriptResult> result = await rig.RunAsync("verborragico", onLine: _ =>
        {
            calls++;
            throw new InvalidOperationException("roto a propósito");
        });

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("fin", result.Value!.Message);
        Assert.Equal(1, calls);
        Assert.Contains(rig.Log.Recent(), entry => entry.Code == ErrorCodes.InternalError && entry.Message.Contains("roto a propósito", StringComparison.Ordinal));
    }
}
