using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La tubería del MCP con una tubería real (E-MCP-2). Cada prueba usa su propio nombre, PampaPad-test-guid: nunca PampaPad ni
/// PampaPad-dev, que pueden estar abiertas en esta PC.
/// </summary>
public sealed class PipeListenerTests : IAsyncLifetime
{
    private readonly ScriptRig _rig = new();
    private readonly StateStore _state = new();
    private readonly Bridge _bridge;
    private readonly Dispatcher _dispatcher;
    private readonly IDisposable _hooks;
    private readonly string _name = "PampaPad-test-" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _life = new();
    private Task _running = Task.CompletedTask;
    private int _reached;
    private volatile bool _mcpAllowed = true;

    public PipeListenerTests()
    {
        var executor = new Executor([], [], TimeProvider.System);
        _dispatcher = new Dispatcher(_rig.Library, _state, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), _rig.Log);
        _bridge = new Bridge(_state, _rig.Log) { McpAllowed = () => _mcpAllowed };
        _hooks = Requests.RegisterAll(_bridge, new StatusSources("9.9.9", _state, _rig.Library, _dispatcher, _rig.Log, [], [], () => new SettingsView(false, null, null)));

        // Contesta quién pidió: el que llega al manejador, no el que diga el mensaje.
        _bridge.Register<NoData, WhoAsked>("test.who", RequestKind.Read, Doors.WindowAndMcp, (_, caller, _) =>
        {
            Interlocked.Increment(ref _reached);
            return Task.FromResult(Result<WhoAsked>.Success(new WhoAsked(caller.ToString())));
        });
        _bridge.Register<NoData, NoData>("test.window", RequestKind.Read, Doors.Window, (_, _, _) =>
        {
            Interlocked.Increment(ref _reached);
            return Task.FromResult(Result<NoData>.Success(new NoData()));
        });
    }

    public Task InitializeAsync()
    {
        Start(TimeSpan.FromMilliseconds(300));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _life.CancelAsync();
        await _running;
        _hooks.Dispose();
        await _state.DisposeAsync();
        await _rig.DisposeAsync();
        _life.Dispose();
    }

    [Fact]
    public async Task The_hello_is_answered_and_a_snapshot_goes_and_comes_back()
    {
        await using var client = await ConnectAsync();

        string hello = await client.HelloAsync();
        string reply = await client.AskAsync("""{"id":7,"request":"app.snapshot","data":{}}""");

        Assert.Equal("""{"hello":{"protocol":1,"version":"9.9.9"}}""", hello);
        using JsonDocument snapshot = JsonDocument.Parse(reply);
        Assert.Equal(7, snapshot.RootElement.GetProperty("id").GetInt64());
        Assert.True(snapshot.RootElement.GetProperty("ok").GetBoolean(), reply);
        Assert.Equal("9.9.9", snapshot.RootElement.GetProperty("data").GetProperty("version").GetString());
    }

    [Fact]
    public async Task The_caller_is_always_the_mcp_whatever_the_message_says()
    {
        await using var client = await ConnectAsync();
        await client.HelloAsync();

        string reply = await client.AskAsync("""{"id":1,"request":"test.who","caller":"Window","data":{}}""");
        string windowOnly = await client.AskAsync("""{"id":2,"request":"test.window","caller":"Window","data":{}}""");

        Assert.Equal("""{"id":1,"ok":true,"data":{"caller":"Mcp"}}""", reply);
        Assert.Contains(ErrorCodes.NoPermission, windowOnly);
        Assert.Equal(1, Volatile.Read(ref _reached));
    }

    [Fact]
    public async Task Another_protocol_gets_an_error_and_the_connection_closes()
    {
        await using var client = await ConnectAsync();

        await client.SendAsync("""{"hello":{"protocol":2}}""");
        string reply = (await client.ReadAsync())!;

        Assert.StartsWith("""{"id":0,"ok":false,"error":{"code":"invalid_data",""", reply);
        Assert.Null(await client.ReadAsync());
    }

    [Fact]
    public async Task Without_a_hello_in_time_the_connection_closes()
    {
        await using var client = await ConnectAsync();

        Assert.Null(await client.ReadAsync());
        Assert.Contains(_rig.Log.Recent(), entry => entry.Module == "mcp" && entry.Message.Contains("no saludó", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_line_over_4_mb_closes_the_connection_without_reaching_the_bridge()
    {
        await using var client = await ConnectAsync();
        await client.HelloAsync();
        string big = $$"""{"id":1,"request":"test.who","data":{},"x":"{{new string('a', PipeProtocol.MaxLine)}}"}""";

        try
        {
            await client.SendAsync(big);
        }
        catch (IOException)
        {
            // El programa cortó a mitad de la escritura: es lo esperado.
        }

        Assert.Null(await client.ReadAsync());
        Assert.Equal(0, Volatile.Read(ref _reached));
    }

    [Fact]
    public async Task Two_connections_at_once_each_get_their_own_answer_and_a_broken_one_does_not_stop_them()
    {
        await using var first = await ConnectAsync();
        await using var second = await ConnectAsync();
        await using (var broken = await ConnectAsync())
        {
            await broken.SendAsync("""{"hello":{"proto""");
        }

        await first.HelloAsync();
        await second.HelloAsync();
        Task<string> one = first.AskAsync("""{"id":1,"request":"test.who","data":{}}""");
        Task<string> two = second.AskAsync("""{"id":2,"request":"test.who","data":{}}""");

        Assert.StartsWith("""{"id":1,"ok":true""", await one);
        Assert.StartsWith("""{"id":2,"ok":true""", await two);
        await using var third = await ConnectAsync();
        Assert.StartsWith("""{"hello":""", await third.HelloAsync());
    }

    [Fact]
    public async Task An_action_finished_reaches_the_open_connection_not_a_closed_one_and_a_state_reaches_none()
    {
        await using var open = await ConnectAsync();
        await open.HelloAsync();
        await using (var closed = await ConnectAsync())
        {
            await closed.HelloAsync();
        }

        await Poll.Until(() => Subscribers() == 1);
        _bridge.Publish(EventNames.State, new NoData());
        _bridge.Publish(EventNames.ActionFinished, new WhoAsked("fin"));

        string received = (await open.ReadAsync())!;

        Assert.StartsWith("""{"event":"action.finished",""", received);
    }

    // Revisión de la tanda 1 (must): con el interruptor apagado la tubería saluda y niega cada pedido, pero no reenvía eventos.
    [Fact]
    public async Task Switched_off_no_event_goes_down_the_pipe()
    {
        _mcpAllowed = false;
        await using var client = await ConnectAsync();
        await client.HelloAsync();
        await Poll.Until(() => Subscribers() == 1);

        _bridge.Publish(EventNames.ScriptOutput, new WhoAsked("secreto"));
        _bridge.Publish(EventNames.ActionFinished, new WhoAsked("fin"));
        string first = await client.AskAsync("""{"id":7,"request":"test.who","data":{}}""");

        Assert.StartsWith("""{"id":7,"ok":false""", first);
    }

    [Fact]
    public async Task Each_mcp_request_leaves_a_line_with_requested_by_mcp()
    {
        await using var client = await ConnectAsync();
        await client.HelloAsync();

        await client.AskAsync("""{"id":1,"request":"test.who","data":{}}""");
        await client.AskAsync("""{"id":2,"request":"no.existe","data":{}}""");

        LogEntry[] lines = [.. _rig.Log.Recent().Where(entry => entry.RequestedBy == Trigger.Mcp && entry.Module == "mcp")];
        Assert.Equal(["test.who: ok", $"no.existe: {ErrorCodes.UnknownRequest}"], lines.Select(entry => entry.Message));
    }

    [Fact]
    public async Task Closing_the_program_ends_the_listener_without_an_exception_even_with_a_connection_open()
    {
        await using var client = await ConnectAsync();
        await client.HelloAsync();

        await _life.CancelAsync();

        await _running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_running.IsCompletedSuccessfully);
    }

    // Revisión de la tanda 1 (should): una falla al crear la tubería terminaba el listener para siempre, con el programa abierto y Claude
    // viendo app_not_running. Una tubería de una sola instancia con el mismo nombre hace fallar la creación.
    [Fact]
    public async Task A_pipe_that_cannot_be_created_is_tried_again_and_answers_once_it_can()
    {
        string name = "PampaPad-test-" + Guid.NewGuid().ToString("N");
        var holder = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task running = new PipeListener(_bridge, _rig.Log, name, "9.9.9").RunAsync(_life.Token);
        await Task.Delay(300);
        Assert.False(running.IsCompleted);
        await holder.DisposeAsync();

        var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(5000);
        await using (var client = new PipeClient(stream))
        {
            Assert.StartsWith("""{"hello":""", await client.HelloAsync());
        }

        Assert.Single(_rig.Log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Module == "mcp");
        await _life.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_burst_of_300_clients_that_connect_and_leave_does_not_stop_the_listener()
    {
        for (int i = 0; i < 300; i++)
        {
            var stream = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stream.ConnectAsync(5000);
            await stream.DisposeAsync();
        }

        await using var client = await ConnectAsync();
        Assert.StartsWith("""{"hello":""", await client.HelloAsync());
    }

    private void Start(TimeSpan helloTimeout) =>
        _running = new PipeListener(_bridge, _rig.Log, _name, "9.9.9", helloTimeout).RunAsync(_life.Token);

    // Los suscriptores del puente: cada conexión saludada es uno. Por reflexión: el puente no los cuenta para afuera.
    private int Subscribers() =>
        ((Action<string>[])typeof(Bridge).GetField("_sinks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_bridge)!).Length;

    private async Task<PipeClient> ConnectAsync()
    {
        var stream = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(5000);
        return new PipeClient(stream);
    }

    private sealed record WhoAsked(string Caller);

    private sealed class PipeClient(NamedPipeClientStream stream) : IAsyncDisposable
    {
        private readonly StreamReader _reader = new(stream, new UTF8Encoding(false));

        public async Task<string> HelloAsync()
        {
            await SendAsync(PipeProtocol.ClientHello());
            return (await ReadAsync())!;
        }

        public async Task<string> AskAsync(string request)
        {
            await SendAsync(request);
            return (await ReadAsync())!;
        }

        public async Task SendAsync(string line)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
            await stream.FlushAsync();
        }

        public async Task<string?> ReadAsync() => await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            await stream.DisposeAsync();
        }
    }
}
