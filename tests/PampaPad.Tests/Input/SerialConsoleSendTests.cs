using PampaPad.Core;

namespace PampaPad.Tests;

// El envío por el cable (etapa 8b): una línea por paso, el candado y los tres casos de console_disconnected.
public class SerialConsoleSendTests
{
    private const string Toggle = "#101013BQAAAAI=";
    private const string Violet = "#101013AW1d9gA=";
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";

    [Fact]
    public async Task Sending_while_connected_leaves_exactly_the_line_and_its_newline_on_the_cable()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        var result = await rig.Source.SendAsync(Toggle, CancellationToken.None);

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(Toggle + "\n", cable.Written);
    }

    [Fact]
    public async Task Sending_without_a_connection_says_the_console_is_not_connected()
    {
        await using var rig = new SerialConsoleRig();

        var result = await rig.Source.SendAsync(Toggle, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
        Assert.Equal("La consola no está conectada.", result.Failure?.Message);
    }

    [Fact]
    public async Task Sending_with_the_port_released_says_it_is_released_and_writes_nothing()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        rig.Source.Release(true);

        var result = await rig.Source.SendAsync(Toggle, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
        Assert.Equal("La consola está soltada.", result.Failure?.Message);
        Assert.Equal("", cable.Written);
    }

    [Fact]
    public async Task A_write_that_fails_says_so_and_neither_holds_the_lock_nor_stops_the_reading()
    {
        await using var rig = new SerialConsoleRig();
        var cable = new MemoryPipe { FailsToWrite = true };
        rig.NextOpensStream(cable);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        var failed = await rig.Source.SendAsync(Toggle, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, failed.Failure?.Code);
        Assert.StartsWith("No se pudo escribir en la consola: ", failed.Failure?.Message, StringComparison.Ordinal);
        cable.Feed(Key3Down);
        Assert.Equal(new InputEvent(SerialConsole.SourceId, "key.3", Gesture.Press), await rig.NextEvent());

        // El candado quedó libre: la próxima escritura pasa, sin esperar a nadie.
        cable.FailsToWrite = false;
        var next = await rig.Source.SendAsync(Toggle, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(next.Ok, next.Failure?.Message);
        Assert.Equal(Toggle + "\n", cable.Written);
    }

    [Fact]
    public async Task After_unplugging_mid_write_the_reading_comes_back_on_its_own_and_keys_arrive()
    {
        await using var rig = new SerialConsoleRig();
        var first = new MemoryPipe { FailsToWrite = true };
        rig.NextOpensStream(first);
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        var failed = await rig.Source.SendAsync(Toggle, CancellationToken.None);
        first.Break();
        await rig.AdvanceUntil(() => rig.OpenCalls == 2 && rig.Link == ConsoleLinkState.Connected);
        second.Feed(Key3Down);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, failed.Failure?.Code);
        Assert.Equal(new InputEvent(SerialConsole.SourceId, "key.3", Gesture.Press), await rig.NextEvent());
        Assert.True((await rig.Source.SendAsync(Violet, CancellationToken.None)).Ok);
        Assert.Equal(Violet + "\n", second.Written);
    }

    // Revisión de la tanda 1 (must y should): el acceso denegado es el error 5 de Windows al desenchufar (SerialConsole.cs:229-233), y las
    // otras tres no tenían prueba. Ninguna puede salir de SendAsync: el Executor la volvería internal_error.
    [Theory]
    [InlineData("io")]
    [InlineData("denied")]
    [InlineData("disposed")]
    [InlineData("timeout")]
    [InlineData("aborted")]
    public async Task Every_error_a_serial_port_gives_while_writing_answers_console_disconnected(string kind)
    {
        await using var rig = new SerialConsoleRig();
        var cable = new MemoryPipe();
        rig.NextOpensStream(cable);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        cable.WriteError = kind switch
        {
            "io" => new IOException("El dispositivo no está conectado."),
            "denied" => new UnauthorizedAccessException("Acceso denegado."),
            "disposed" => new ObjectDisposedException("SerialStream"),
            "timeout" => new TimeoutException("Se agotó el tiempo de espera."),
            _ => new OperationCanceledException("La operación de E/S se anuló."),
        };

        var result = await rig.Source.SendAsync(Toggle, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
        Assert.StartsWith("No se pudo escribir en la consola: ", result.Failure?.Message, StringComparison.Ordinal);
    }

    // Revisión de la tanda 1 (should): el WriteAsync de SerialStream pone WriteTimeout en infinito, así que una escritura trabada colgaba el
    // paso con el candado tomado. El Write sincrónico sí respeta el tope.
    [Fact]
    public async Task A_cable_whose_async_write_never_ends_still_gets_the_line()
    {
        await using var rig = new SerialConsoleRig();
        var cable = new MemoryPipe { AsyncWriteNeverEnds = true };
        rig.NextOpensStream(cable);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        var result = await rig.Source.SendAsync(Toggle, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(Toggle + "\n", cable.Written);
    }

    [Fact]
    public async Task Two_sends_at_once_on_a_slow_cable_leave_two_whole_lines_one_after_the_other()
    {
        // Una tecla y Claude a la vez: sin candado, el cable lento intercala los bytes de las dos líneas.
        await using var rig = new SerialConsoleRig();
        var cable = new MemoryPipe { Slow = true };
        rig.NextOpensStream(cable);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        var results = await Task.WhenAll(
            Task.Run(() => rig.Source.SendAsync(Toggle, CancellationToken.None)),
            Task.Run(() => rig.Source.SendAsync(Violet, CancellationToken.None)));

        Assert.All(results, r => Assert.True(r.Ok, r.Failure?.Message));
        Assert.Contains(cable.Written, new[] { $"{Toggle}\n{Violet}\n", $"{Violet}\n{Toggle}\n" });
    }

    // Repaso de la 1.4.0: el receptor 0.1.0 dejaba de aceptar bytes con la lectura viva, y el programa escribió 11 minutos sin que el
    // registro dijera nada. Una escritura que vence se anota una vez por conexión; un desenchufe no, porque lo anota la lectura.
    [Fact]
    public async Task A_write_that_times_out_with_the_port_open_is_logged_once_per_connection()
    {
        await using var rig = new SerialConsoleRig();
        var cable = new MemoryPipe();
        rig.NextOpensStream(cable);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        cable.WriteError = new TimeoutException("Se agotó el tiempo de espera del semáforo.");

        Assert.False((await rig.Source.SendAsync(Toggle, CancellationToken.None)).Ok);
        Assert.False((await rig.Source.SendAsync(Violet, CancellationToken.None)).Ok);

        var stuck = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId && e.Level == LogLevel.Warning).ToList();
        var warning = Assert.Single(stuck);
        Assert.StartsWith("La consola no acepta lo que escribe la PC: ", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_write_that_fails_because_the_cable_left_is_not_logged_by_the_writer()
    {
        await using var rig = new SerialConsoleRig();
        var cable = new MemoryPipe();
        rig.NextOpensStream(cable);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        cable.WriteError = new IOException("El dispositivo no está conectado.");

        Assert.False((await rig.Source.SendAsync(Toggle, CancellationToken.None)).Ok);

        Assert.DoesNotContain(rig.Log.Recent(), e => e.Module == SerialConsole.SourceId && e.Message.StartsWith("La consola no acepta", StringComparison.Ordinal));
    }
}
