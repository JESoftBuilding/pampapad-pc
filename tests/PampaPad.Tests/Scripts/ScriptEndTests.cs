using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La línea de cierre de cada corrida, OnFinished y ScriptRunner.Running (plan de la etapa 8, E8-2).</summary>
public class ScriptEndTests
{
    [Fact]
    public async Task A_run_that_ends_well_leaves_one_closing_line_and_tells_on_finished()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("hola", "@echo off\necho primera\necho lista\n");
        var finished = new ConcurrentQueue<ScriptResult>();

        Result<ScriptResult> result = await Run(rig, "hola", finished);

        Assert.True(result.Ok, result.Failure?.Message);
        ScriptEnd end = SingleEnd(rig, "hola");
        Assert.True(end.Ok);
        Assert.Equal(0, end.ExitCode);
        Assert.Equal("lista", end.Message);
        Assert.Equal(0, Assert.Single(finished).ExitCode);
        Assert.All(rig.Log.Recent().Where(entry => entry.Module == "scripts"), entry => Assert.Equal("hola", entry.Script));
    }

    [Fact]
    public async Task An_exit_code_that_is_not_zero_closes_with_its_code_and_last_line()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("bancocam", "@echo off\necho sin celu\nexit /b 3\n");
        var finished = new ConcurrentQueue<ScriptResult>();

        Result<ScriptResult> result = await Run(rig, "bancocam", finished);

        Assert.Equal(ErrorCodes.ScriptFailed, result.Failure?.Code);
        ScriptEnd end = SingleEnd(rig, "bancocam");
        Assert.False(end.Ok);
        Assert.Equal(3, end.ExitCode);
        Assert.Equal("sin celu", end.Message);
        Assert.Equal(3, Assert.Single(finished).ExitCode);
    }

    [Fact]
    public async Task Passing_the_limit_closes_once_without_exit_code()
    {
        var clock = new FakeTimeProvider();
        await using var rig = new ScriptRig(clock);
        string folder = rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 5""");
        var finished = new ConcurrentQueue<ScriptResult>();

        Task<Result<ScriptResult>> running = Run(rig, "lento", finished);
        await ScriptRig.ChildAsync(folder);
        Assert.Contains("lento", rig.Runner.Running);
        clock.Advance(TimeSpan.FromSeconds(5));
        Result<ScriptResult> result = await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.ScriptTimeout, result.Failure?.Code);
        ScriptEnd end = SingleEnd(rig, "lento");
        Assert.False(end.Ok);
        Assert.Null(end.ExitCode);
        Assert.Single(finished);
        Assert.DoesNotContain("lento", rig.Runner.Running);
    }

    [Fact]
    public async Task A_cancelled_run_closes_once()
    {
        await using var rig = new ScriptRig();
        string folder = rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 60""");
        using var cancel = new CancellationTokenSource();
        var finished = new ConcurrentQueue<ScriptResult>();

        Task<Result<ScriptResult>> running = Run(rig, "lento", finished, cancel.Token);
        await ScriptRig.ChildAsync(folder);
        await cancel.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(SingleEnd(rig, "lento").Ok);
        Assert.Single(finished);
        Assert.DoesNotContain("lento", rig.Runner.Running);
    }

    [Fact]
    public async Task Without_waiting_it_closes_when_the_script_really_exits()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("suelto", "@echo off\nping -n 2 127.0.0.1 >nul\nexit /b 4\n", """, "wait": false""");
        var finished = new ConcurrentQueue<ScriptResult>();

        Result<ScriptResult> result = await Run(rig, "suelto", finished);
        Assert.True(result.Ok);
        await Poll.Until(() => !finished.IsEmpty, 15_000);

        ScriptEnd end = SingleEnd(rig, "suelto");
        Assert.False(end.Ok);
        Assert.Equal(4, end.ExitCode);
        Assert.Equal(4, Assert.Single(finished).ExitCode);
        Assert.DoesNotContain("suelto", rig.Runner.Running);
    }

    [Fact]
    public async Task A_script_that_does_not_start_closes_once_too()
    {
        await using var rig = new ScriptRig();
        rig.Add("roto", "roto.exe", "no soy un programa"u8.ToArray());
        var finished = new ConcurrentQueue<ScriptResult>();

        Result<ScriptResult> result = await Run(rig, "roto", finished);

        Assert.Equal(ErrorCodes.ScriptFailed, result.Failure?.Code);
        Assert.False(SingleEnd(rig, "roto").Ok);
        Assert.Single(finished);
        Assert.DoesNotContain("roto", rig.Runner.Running);
    }

    [Fact]
    public async Task A_chain_already_cancelled_and_a_missing_script_close_once()
    {
        await using var rig = new ScriptRig();
        rig.AddCmd("hola", "@echo off\necho hola\n");
        var finished = new ConcurrentQueue<ScriptResult>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Run(rig, "hola", finished, cancelled.Token);
        await Run(rig, "noesta", finished);

        Assert.False(SingleEnd(rig, "hola").Ok);
        Assert.False(SingleEnd(rig, "noesta").Ok);
        Assert.Equal(2, finished.Count);
    }

    private static Task<Result<ScriptResult>> Run(ScriptRig rig, string id, ConcurrentQueue<ScriptResult> finished, CancellationToken ct = default) =>
        rig.Runner.RunAsync(new ScriptRequest(id, null, null, ScriptRig.Key, OnFinished: finished.Enqueue), ct);

    private static ScriptEnd SingleEnd(ScriptRig rig, string id) =>
        Assert.Single(rig.Log.Recent(), entry => entry.Script == id && entry.End is not null).End!;
}
