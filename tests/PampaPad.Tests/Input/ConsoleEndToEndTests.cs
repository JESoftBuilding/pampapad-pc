using PampaPad.Core;

namespace PampaPad.Tests;

public class ConsoleEndToEndTests
{
    private const string Key5Down = "#301110EAAAAAAAAAA=\n";
    private const string Key12Down = "#301110AAgAAAAAAAA=\n";

    [Fact]
    public async Task A_line_from_the_board_runs_the_action_of_that_key()
    {
        await using var rig = new KeysRig();
        var cable = new MemoryPipe();
        await using var console = new SerialConsole(_ => Task.FromResult<Stream>(cable), rig.Store, rig.Log, rig.Clock);
        rig.Dispatcher.Attach(console);
        await console.StartAsync(CancellationToken.None);

        cable.Feed(Key5Down);

        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
    }

    [Fact]
    public async Task Unplugged_while_holding_a_key_with_on_hold_fires_nothing()
    {
        await using var rig = new KeysRig();
        var first = new MemoryPipe();
        var second = new MemoryPipe();
        var cables = new Queue<Stream>([first, second]);
        var seen = 0;
        await using var console = new SerialConsole(_ => Task.FromResult(cables.Dequeue()), rig.Store, rig.Log, rig.Clock);
        rig.Dispatcher.Attach(console);
        console.Input += _ => Interlocked.Increment(ref seen);
        await console.StartAsync(CancellationToken.None);

        first.Feed(Key12Down);                                  // la tecla con onHold, apretada
        await Poll.Until(() => Volatile.Read(ref seen) == 1);
        first.Break();                                          // y se desenchufa el cable
        await Poll.Until(() => rig.Store.Current.Console.State == ConsoleLinkState.Waiting);

        rig.Clock.Advance(Gestures.DefaultFreshness);           // 150 ms sin estado: se da por soltada
        rig.Dispatcher.Tick();
        rig.Clock.Advance(TimeSpan.FromSeconds(2));             // pasan el segundo de mantener y el reintento del puerto
        rig.Dispatcher.Tick();
        await Poll.Until(() => rig.Store.Current.Console.State == ConsoleLinkState.Connected);

        second.Feed(Key5Down);                                  // vuelve el cable y se aprieta otra tecla
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        Assert.Equal(1, rig.Runs);                              // ni "inicio" ni "fin": no hubo toque ni mantenido fantasma
    }

    [Fact]
    public async Task A_line_from_the_board_ends_as_a_chapter_in_obs()
    {
        await using var session = await ObsSession.StartAsync(RecordingState.Recording);
        using var temp = new TempFolder();
        var clock = TimeProvider.System;
        var executor = new Executor(Catalog.Build(session.Client, session.State, session.Log, clock), clock);
        var gestures = new Gestures(clock, Gestures.HoldFrom(null), Gestures.DefaultFreshness);
        var dispatcher = new Dispatcher(KeysRig.LoadLibrary(temp), session.State, executor, gestures, session.Log);
        var finished = new TaskCompletionSource<ActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Finished += result => finished.TrySetResult(result);
        var cable = new MemoryPipe();
        await using var console = new SerialConsole(_ => Task.FromResult<Stream>(cable), session.State, session.Log, clock);
        dispatcher.Attach(console);
        await console.StartAsync(CancellationToken.None);

        cable.Feed(Key5Down);                                   // la tecla 5 de la placa: "Short"

        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Ok);
        Assert.Equal("marca-short", result.ActionId);
        Assert.Equal(Trigger.Key, result.RequestedBy);
        Assert.Equal(new[] { "short" }, session.Obs.Chapters.ToArray());
    }
}
