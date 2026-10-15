using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class BridgeTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly Log _log;
    private readonly Bridge _bridge;

    public BridgeTests()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _bridge = new Bridge(_state, _log);

        // Un pedido de prueba: devuelve en "cancelled" el count que le mandan.
        _bridge.Register<LogReadRequest, ActionCancelResponse>("test.count", RequestKind.Read, Doors.WindowAndMcp,
            (request, _, _) => Task.FromResult(Result<ActionCancelResponse>.Success(new ActionCancelResponse(request.Count ?? 0))));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task A_request_with_a_handler_answers_ok_with_its_data()
    {
        string reply = await Ask("""{"id":1,"request":"test.count","data":{"count":3}}""");

        Assert.Equal("""{"id":1,"ok":true,"data":{"cancelled":3}}""", reply);
    }

    [Fact]
    public async Task Unknown_request_answers_unknown_request()
    {
        string reply = await Ask("""{"id":7,"request":"config.sav","data":{}}""");

        Assert.Equal("""{"id":7,"ok":false,"error":{"code":"unknown_request","message":"No existe el pedido config.sav.","where":"request"}}""", reply);
        Assert.Contains(_log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Code == ErrorCodes.UnknownRequest);
    }

    [Fact]
    public async Task Broken_json_answers_invalid_data_with_id_zero()
    {
        string reply = await Ask("{ esto no es json");

        Assert.StartsWith("""{"id":0,"ok":false,"error":{"code":"invalid_data",""", reply);
    }

    [Fact]
    public async Task Unknown_field_in_the_data_is_invalid_data()
    {
        string reply = await Ask("""{"id":2,"request":"test.count","data":{"count":3,"lines":1}}""");

        Assert.Equal(2, Id(reply));
        Assert.Equal(ErrorCodes.InvalidData, Code(reply));
    }

    [Fact]
    public async Task A_handler_failure_travels_as_its_error()
    {
        _bridge.Register<NoData, NoData>("test.fail", RequestKind.Read, Doors.Window,
            (_, _, _) => Task.FromResult(Result<NoData>.Fail(ErrorCodes.ConfigInvalid, "mal", "pages[0]")));

        string reply = await Ask("""{"id":3,"request":"test.fail","data":{}}""");

        Assert.Equal("""{"id":3,"ok":false,"error":{"code":"config_invalid","message":"mal","where":"pages[0]"}}""", reply);
    }

    [Fact]
    public async Task A_handler_that_throws_answers_internal_error_and_logs_the_exception()
    {
        _bridge.Register<NoData, NoData>("test.throw", RequestKind.Read, Doors.Window,
            (_, _, _) => throw new InvalidOperationException("se rompió"));

        string reply = await Ask("""{"id":4,"request":"test.throw","data":{}}""");

        Assert.Equal(4, Id(reply));
        Assert.Equal(ErrorCodes.InternalError, Code(reply));
        LogEntry line = Assert.Single(_log.Recent(), entry => entry.Level == LogLevel.Error && entry.Module == "bridge");
        Assert.Contains("System.InvalidOperationException: se rompió", line.Message);
    }

    [Fact]
    public async Task A_handler_that_throws_after_an_await_is_caught_too()
    {
        _bridge.Register<NoData, NoData>("test.later", RequestKind.Read, Doors.Window, async (_, _, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("más tarde");
        });

        Assert.Equal(ErrorCodes.InternalError, Code(await Ask("""{"id":5,"request":"test.later","data":{}}""")));
    }

    [Fact]
    public async Task The_door_is_checked_before_the_handler_runs()
    {
        int runs = 0;
        _bridge.Register<NoData, NoData>("test.window", RequestKind.Read, Doors.Window, (_, _, _) =>
        {
            runs++;
            return Task.FromResult(Result<NoData>.Success(new NoData()));
        });

        string reply = await Ask("""{"id":6,"request":"test.window","data":{}}""", Caller.Tray);

        Assert.Equal(ErrorCodes.NoPermission, Code(reply));
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task The_mcp_is_refused_while_its_switch_does_not_exist()
    {
        string reply = await Ask("""{"id":8,"request":"test.count","data":{}}""", Caller.Mcp);

        Assert.Equal(ErrorCodes.NoPermission, Code(reply));
        Assert.Contains("El MCP está apagado en Ajustes", reply);
    }

    [Fact]
    public async Task With_the_mcp_allowed_a_read_passes_and_a_write_while_recording_does_not()
    {
        var bridge = new Bridge(_state, _log) { McpAllowed = () => true };
        bridge.Register<NoData, NoData>("test.read", RequestKind.Read, Doors.WindowAndMcp, (_, _, _) => Task.FromResult(Result<NoData>.Success(new NoData())));
        bridge.Register<NoData, NoData>("test.write", RequestKind.Write, Doors.WindowAndMcp, (_, _, _) => Task.FromResult(Result<NoData>.Success(new NoData())));
        _state.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        string read = await bridge.HandleAsync("""{"id":1,"request":"test.read","data":{}}""", Caller.Mcp, CancellationToken.None);
        string write = await bridge.HandleAsync("""{"id":2,"request":"test.write","data":{}}""", Caller.Mcp, CancellationToken.None);

        Assert.Equal("""{"id":1,"ok":true,"data":{}}""", read);
        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, Code(write));
    }

    [Fact]
    public void Registered_lists_every_name_and_RegisteredFor_only_its_doors()
    {
        _bridge.Register<NoData, NoData>("test.window", RequestKind.WindowOnly, Doors.Window, (_, _, _) => Task.FromResult(Result<NoData>.Success(new NoData())));
        _bridge.Register<NoData, NoData>("test.tray", RequestKind.Execute, Doors.WindowAndTray, (_, _, _) => Task.FromResult(Result<NoData>.Success(new NoData())));

        Assert.Equal(new[] { "test.count", "test.tray", "test.window" }, _bridge.Registered.Order());
        Assert.Equal(new[] { "test.count", "test.tray", "test.window" }, _bridge.RegisteredFor(Caller.Window).Order());
        Assert.Equal(new[] { "test.tray" }, _bridge.RegisteredFor(Caller.Tray));
        Assert.Equal(new[] { "test.count" }, _bridge.RegisteredFor(Caller.Mcp));
        Assert.Equal(3, _bridge.RegisteredFor(Caller.Internal).Count);
    }

    [Fact]
    public void A_name_registers_only_once()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _bridge.Register<NoData, NoData>("test.count", RequestKind.Read, Doors.Window, (_, _, _) => Task.FromResult(Result<NoData>.Success(new NoData()))));
    }

    [Fact]
    public async Task Cancelling_the_caller_answers_cancelled()
    {
        _bridge.Register<NoData, NoData>("test.wait", RequestKind.Read, Doors.Window, async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Result<NoData>.Success(new NoData());
        });
        using var closing = new CancellationTokenSource();

        Task<string> reply = _bridge.HandleAsync("""{"id":9,"request":"test.wait","data":{}}""", Caller.Window, closing.Token);
        await closing.CancelAsync();

        Assert.Equal(ErrorCodes.Cancelled, Code(await reply));
    }

    [Fact]
    public void Publish_reaches_every_subscriber_with_the_seq_of_the_state()
    {
        _state.Update(s => s with { BasePage = "a" });
        _state.Update(s => s with { BasePage = "b" });
        var first = new ConcurrentQueue<string>();
        var second = new ConcurrentQueue<string>();
        using IDisposable one = _bridge.Subscribe(first.Enqueue);
        using IDisposable two = _bridge.Subscribe(second.Enqueue);

        _bridge.Publish(EventNames.Log, new NoData());

        Assert.Equal(new[] { """{"event":"log","seq":2,"data":{}}""" }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Publish_with_its_own_seq_keeps_it()
    {
        var events = new ConcurrentQueue<string>();
        using IDisposable subscription = _bridge.Subscribe(events.Enqueue);

        _bridge.Publish(EventNames.State, 41, new NoData());

        Assert.Equal(new[] { """{"event":"state","seq":41,"data":{}}""" }, events);
    }

    [Fact]
    public void A_disposed_subscription_gets_nothing_more()
    {
        var events = new ConcurrentQueue<string>();
        IDisposable subscription = _bridge.Subscribe(events.Enqueue);
        _bridge.Publish(EventNames.Log, new NoData());

        subscription.Dispose();
        _bridge.Publish(EventNames.Log, new NoData());

        Assert.Single(events);
    }

    [Fact]
    public void A_subscriber_that_throws_is_dropped_and_the_others_still_get_the_event()
    {
        int thrown = 0;
        var events = new ConcurrentQueue<string>();
        using IDisposable broken = _bridge.Subscribe(_ =>
        {
            thrown++;
            throw new InvalidOperationException("roto");
        });
        using IDisposable good = _bridge.Subscribe(events.Enqueue);

        _bridge.Publish(EventNames.Log, new NoData());
        _bridge.Publish(EventNames.Log, new NoData());

        Assert.Equal(1, thrown);
        Assert.Equal(2, events.Count);
        Assert.Contains(_log.Recent(), entry => entry.Level == LogLevel.Error && entry.Message.StartsWith("Un suscriptor del puente tiró", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publishing_while_holding_a_lock_that_a_state_update_waits_for_does_not_deadlock()
    {
        // Lo que hace el Dispatcher: escribe en el registro con su _gate tomado (Dispatcher.cs:126-131), y PublishRunning pide ese _gate
        // adentro de StateStore.Update (:194-201). Con el evento log, esa escritura publica: Publish no puede esperar el lock del estado.
        var gate = new Lock();
        using var holding = new ManualResetEventSlim();
        using var updating = new ManualResetEventSlim();
        Task publisher = Task.Run(() =>
        {
            lock (gate)
            {
                holding.Set();
                updating.Wait(TimeSpan.FromSeconds(10));
                _bridge.Publish(EventNames.Log, new NoData());
            }
        });
        Task updater = Task.Run(() =>
        {
            holding.Wait(TimeSpan.FromSeconds(10));
            _state.Update(s =>
            {
                updating.Set();

                // Con tope: si se traban, este lado se suelta a los 10 s y la prueba da rojo en vez de colgar el proceso de pruebas.
                if (gate.TryEnter(TimeSpan.FromSeconds(10)))
                {
                    gate.Exit();
                }

                return s with { BasePage = "cruzada" };
            });
        });

        Task both = Task.WhenAll(publisher, updater);
        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    private Task<string> Ask(string json, Caller caller = Caller.Window) => _bridge.HandleAsync(json, caller, CancellationToken.None);

    private static long Id(string reply)
    {
        using JsonDocument document = JsonDocument.Parse(reply);
        return document.RootElement.GetProperty("id").GetInt64();
    }

    private static string? Code(string reply)
    {
        using JsonDocument document = JsonDocument.Parse(reply);
        return document.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetProperty("code").GetString() : null;
    }
}
