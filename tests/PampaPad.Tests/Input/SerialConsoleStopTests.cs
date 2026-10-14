using PampaPad.Core;

namespace PampaPad.Tests;

// Cómo se cierra el puerto: al parar el programa, al soltarlo y al liberar la consola. El puerto real no respeta el token de una
// lectura pendiente (01 §9): lo único que la corta es cerrarlo. DeafStream es ese puerto; MemoryPipe, en cambio, sí respeta el token.
public class SerialConsoleStopTests
{
    [Fact]
    public async Task Stopping_the_program_closes_a_port_whose_read_ignores_the_token_and_logs_nothing_wrong()
    {
        await using var rig = new SerialConsoleRig();
        var port = new DeafStream();
        rig.NextOpensStream(port);
        using var life = new CancellationTokenSource();
        await rig.Source.StartAsync(life.Token);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        await life.CancelAsync();

        await Poll.Until(() => port.Closed);
        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(rig.Log.Recent(), e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Stopping_the_program_is_not_logged_as_a_lost_console_when_the_read_honors_the_token()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        using var life = new CancellationTokenSource();
        await rig.Source.StartAsync(life.Token);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        await life.CancelAsync();
        await Poll.Until(() => cable.Closed);
        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(rig.Log.Recent(), e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Releasing_closes_a_port_whose_read_ignores_the_token_and_taking_it_back_reopens_it()
    {
        await using var rig = new SerialConsoleRig();
        var port = new DeafStream();
        rig.NextOpensStream(port);
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        rig.Source.Release(true);

        await Poll.Until(() => port.Closed && rig.Link == ConsoleLinkState.Released);
        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        second.Feed("#301110BAAAAAAAAAA=\n");
        Assert.Equal(new InputEvent(SerialConsole.SourceId, "key.3", Gesture.Press), await rig.NextEvent());
        Assert.Equal(2, rig.OpenCalls);
    }

    [Fact]
    public async Task Disposing_closes_a_port_whose_read_ignores_the_token()
    {
        await using var rig = new SerialConsoleRig();
        var port = new DeafStream();
        rig.NextOpensStream(port);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(port.Closed);
    }
}
