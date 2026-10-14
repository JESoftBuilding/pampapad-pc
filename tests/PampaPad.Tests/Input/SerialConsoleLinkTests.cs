using PampaPad.Core;

namespace PampaPad.Tests;

// Bordes del enlace de SerialConsole: soltar, reintentar, cerrar y qué queda en el estado y en el registro.
public class SerialConsoleLinkTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";
    private const string AllUp = "#301110AAAAAAAAAAA=\n";

    [Fact]
    public async Task A_stream_that_ends_waits_the_retry_delay_before_it_reconnects()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextOpensStream(new EndedStream());
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Log.Recent().Any(e => e.Level == LogLevel.Warning && e.Message.Contains("cerró el puerto", StringComparison.Ordinal)));

        rig.Clock.Advance(SerialConsole.RetryDelay - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.Equal(1, rig.OpenCalls);                     // no gira en vacío: espera los 2 s

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await Poll.Until(() => rig.OpenCalls == 2);
        second.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Releasing_before_the_loop_starts_never_opens_the_port()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        rig.Source.Release(true);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        Assert.Equal(0, rig.OpenCalls);

        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        Assert.Equal(1, rig.OpenCalls);
        cable.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Releasing_twice_logs_once_and_taking_back_what_was_not_released_does_nothing()
    {
        await using var rig = new SerialConsoleRig();

        rig.Source.Release(false);
        rig.Source.Release(true);
        rig.Source.Release(true);

        var lines = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId).Select(e => e.Message).ToList();
        Assert.Equal(["Se soltó el puerto de la consola."], lines);
    }

    [Fact]
    public async Task A_port_that_finishes_opening_after_Release_is_closed_and_never_used()
    {
        await using var rig = new SerialConsoleRig();
        var seen = new List<ConsoleLinkState>();
        rig.Store.Subscribe((_, now) =>
        {
            lock (seen)
            {
                seen.Add(now.Console.State);
            }
        });
        var slow = rig.NextOpensLater();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.OpenCalls == 1);
        rig.Source.Release(true);
        var late = new MemoryPipe();

        slow.SetResult(late);

        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);
        Assert.True(late.Closed);
        lock (seen)
        {
            Assert.DoesNotContain(ConsoleLinkState.Connected, seen);
        }

        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        second.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(2, rig.OpenCalls);
    }

    [Fact]
    public async Task Releasing_the_port_forgets_the_pressed_keys_so_the_key_enters_as_new_when_it_is_taken_back()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        first.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());

        rig.Source.Release(true);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);
        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        second.Feed(AllUp + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(2, rig.EventCount);                    // ningún "soltó" de una tecla que ya se había olvidado
    }

    [Fact]
    public async Task The_link_keeps_the_port_name_through_every_change()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        Assert.Equal("COM7", rig.Store.Current.Console.Port);

        rig.Source.Release(true);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);
        Assert.Equal("COM7", rig.Store.Current.Console.Port);

        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        Assert.Equal("COM7", rig.Store.Current.Console.Port);
        Assert.True(first.Closed);
    }

    [Fact]
    public async Task The_same_failure_is_logged_once_and_a_different_one_is_logged_again()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextFails("uno");
        rig.NextFails("uno");
        rig.NextFails("uno");
        rig.NextFails("dos");
        rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        await rig.AdvanceUntil(() => rig.Link == ConsoleLinkState.Connected);

        Assert.Equal(5, rig.OpenCalls);
        var warnings = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId && e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.StartsWith("Sin consola: uno ", warnings[0], StringComparison.Ordinal);
        Assert.StartsWith("Sin consola: dos ", warnings[1], StringComparison.Ordinal);
        Assert.EndsWith("Se reintenta cada 2 s.", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_that_comes_back_after_a_good_connection_is_logged_again()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextOpensStream(new EndedStream());
        rig.NextOpensStream(new EndedStream());
        rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        await rig.AdvanceUntil(() => rig.OpenCalls == 3);

        var warnings = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId && e.Level == LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count);                    // conectó entre una y otra: el mismo motivo vale otra línea
    }

    [Fact]
    public async Task Stopping_the_program_is_not_logged_as_a_lost_console()
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
    public async Task Disposing_before_starting_and_disposing_twice_are_harmless()
    {
        await using var rig = new SerialConsoleRig();

        await rig.Source.DisposeAsync();
        await rig.Source.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_while_waiting_to_retry_returns_at_once()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextFails("Access to the path 'COM7' is denied.");
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Log.Recent().Any(e => e.Level == LogLevel.Warning));

        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disposing_while_released_returns_at_once()
    {
        await using var rig = new SerialConsoleRig();
        rig.Source.Release(true);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);

        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disposing_while_reading_closes_the_port()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cable.Closed);
    }

    [Fact]
    public async Task Cancelling_the_token_ends_the_loop_even_when_it_is_waiting_for_a_release()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextOpens();
        using var life = new CancellationTokenSource();
        rig.Source.Release(true);
        await rig.Source.StartAsync(life.Token);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);

        await life.CancelAsync();
        rig.Source.Release(false);
        await Task.Delay(100);

        Assert.Equal(0, rig.OpenCalls);                     // el bucle ya terminó: tomarla de nuevo no abre nada
    }

    [Fact]
    public async Task Sending_while_connected_still_says_console_disconnected_and_writes_nothing()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        var result = await rig.Source.SendAsync("#101013BQAAAAI=", CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
        Assert.Contains("no le manda nada", result.Failure?.Message, StringComparison.Ordinal);   // MemoryPipe.Write tira: si escribiera, la prueba fallaría
    }

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, $"key.{key}", Gesture.Press);

    /// <summary>Un flujo que ya no tiene nada para leer y devuelve 0, como un puerto que se cerró del otro lado.</summary>
    private sealed class EndedStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
