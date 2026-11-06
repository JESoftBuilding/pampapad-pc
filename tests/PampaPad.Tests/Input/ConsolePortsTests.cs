using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

// "Automático" en el puerto de la consola: la elección y el abridor por intento (plan de la 1.4.0, R-3, decisión 6).
public class ConsolePortsTests
{
    private static readonly ConsolePortView Receiver = new("COM21", "Dispositivo serie USB", ConsolePorts.ReceiverUsbId);
    private static readonly ConsolePortView Cable = new("COM20", "USB-Enhanced-SERIAL CH343", ConsolePorts.CableUsbId);
    private static readonly ConsolePortView Ch9102 = new("COM8", "USB-Enhanced-SERIAL CH9102", "1A86:55D4");
    private static readonly ConsolePortView Com1 = new("COM1", "Puerto de comunicaciones", null);

    [Fact]
    public void The_receiver_wins_over_the_console_by_cable()
    {
        Assert.Equal(Receiver, ConsolePorts.Choose([Com1, Cable, Receiver]));
    }

    [Fact]
    public void Without_a_receiver_the_console_by_cable()
    {
        Assert.Equal(Cable, ConsolePorts.Choose([Com1, Ch9102, Cable]));
    }

    [Fact]
    public void Other_ids_do_not_count_and_without_any_known_board_there_is_none()
    {
        Assert.Null(ConsolePorts.Choose([Com1, Ch9102]));
        Assert.Null(ConsolePorts.Choose([]));
    }

    [Fact]
    public void Two_of_the_same_kind_take_the_first_by_port_name_whatever_the_order()
    {
        var com5 = Receiver with { Port = "COM5" };
        var com10 = Receiver with { Port = "COM10" };

        Assert.Equal(com5, ConsolePorts.Choose([com10, com5]));
        Assert.Equal(com5, ConsolePorts.Choose([com5, com10]));
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("AUTO", false)]
    [InlineData("COM7", false)]
    [InlineData(null, false)]
    public void Auto_is_the_value_of_local_json(string? port, bool auto)
    {
        Assert.Equal(auto, ConsolePorts.IsAuto(port));
    }

    [Fact]
    public async Task With_nothing_plugged_it_waits_and_logs_one_line_and_opens_the_receiver_when_it_shows_up()
    {
        await using var rig = new AutoRig();
        await rig.Source.StartAsync(CancellationToken.None);

        await rig.AdvanceUntil(() => rig.Lists >= 4);
        Assert.Equal(ConsoleLinkState.Waiting, rig.Link.State);
        Assert.True(rig.Link.Auto);
        Assert.Null(rig.Link.Port);
        Assert.Empty(rig.Opened);

        rig.Ports = [Cable, Receiver];
        await rig.AdvanceUntil(() => rig.Link.State == ConsoleLinkState.Connected);

        Assert.Equal(["COM21"], rig.Opened);
        Assert.Equal("COM21", rig.Link.Port);
        Assert.True(rig.Link.Auto);
        var warning = Assert.Single(rig.Warnings());
        Assert.Equal($"Sin consola: {ConsolePorts.NoneFound} Se reintenta cada 2 s.", warning);
    }

    [Fact]
    public async Task With_the_link_open_it_does_not_choose_again()
    {
        await using var rig = new AutoRig { Ports = [Cable] };
        await rig.Source.StartAsync(CancellationToken.None);
        await rig.AdvanceUntil(() => rig.Link.State == ConsoleLinkState.Connected);
        int lists = rig.Lists;

        rig.Ports = [Cable, Receiver];
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50);

        Assert.Equal(lists, rig.Lists);
        Assert.Equal("COM20", rig.Link.Port);
        Assert.Equal(ConsoleLinkState.Connected, rig.Link.State);

        // Al cerrarse el puerto vuelve a elegir, y ahora gana el receptor.
        rig.Pipes["COM20"].Break();
        await rig.AdvanceUntil(() => rig.Link is { State: ConsoleLinkState.Connected, Port: "COM21" });
        Assert.Equal(["COM20", "COM21"], rig.Opened);
    }

    [Fact]
    public async Task Released_it_keeps_the_port_in_use_and_taking_it_chooses_again()
    {
        await using var rig = new AutoRig { Ports = [Cable] };
        await rig.Source.StartAsync(CancellationToken.None);
        await rig.AdvanceUntil(() => rig.Link.State == ConsoleLinkState.Connected);

        rig.Source.Release(true);
        await Poll.Until(() => rig.Link.State == ConsoleLinkState.Released);
        Assert.Equal("COM20", rig.Link.Port);
        Assert.True(rig.Link.Auto);

        rig.Ports = [Receiver];
        rig.Source.Release(false);
        await rig.AdvanceUntil(() => rig.Link is { State: ConsoleLinkState.Connected, Port: "COM21" });
    }

    /// <summary>Una SerialConsole con el abridor de "Automático", una lista de puertos que la prueba cambia y cables de memoria.</summary>
    private sealed class AutoRig : IAsyncDisposable
    {
        private readonly TempFolder _temp = new();
        private int _lists;

        public AutoRig()
        {
            Log = new Log(_temp.Combine("log"), Clock);
            Store.Update(s => s with { Console = new ConsoleLink(ConsoleLinkState.Waiting, null) { Auto = true } });
            Source = new SerialConsole(ConsolePorts.Opener(List, Open, Store), Store, Log, Clock);
        }

        public FakeTimeProvider Clock { get; } = new();

        public StateStore Store { get; } = new();

        public Log Log { get; }

        public SerialConsole Source { get; }

        public IReadOnlyList<ConsolePortView> Ports { get; set; } = [];

        public List<string> Opened { get; } = [];

        public Dictionary<string, MemoryPipe> Pipes { get; } = [];

        public int Lists => Volatile.Read(ref _lists);

        public ConsoleLink Link => Store.Current.Console;

        public List<string> Warnings() => [.. Log.Recent().Where(e => e.Module == SerialConsole.SourceId && e.Level == LogLevel.Warning).Select(e => e.Message)];

        public Task AdvanceUntil(Func<bool> done) => Poll.Until(() =>
        {
            if (done())
            {
                return true;
            }

            Clock.Advance(TimeSpan.FromMilliseconds(100));
            return false;
        });

        public async ValueTask DisposeAsync()
        {
            await Source.DisposeAsync();
            await Store.DisposeAsync();
            await Log.DisposeAsync();
            _temp.Dispose();
        }

        private IReadOnlyList<ConsolePortView> List()
        {
            Interlocked.Increment(ref _lists);
            return Ports;
        }

        private Task<Stream> Open(string port, CancellationToken ct)
        {
            var pipe = new MemoryPipe();
            lock (Opened)
            {
                Opened.Add(port);
                Pipes[port] = pipe;
            }

            return Task.FromResult<Stream>(pipe);
        }
    }
}
