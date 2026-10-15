using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class StatusRequestsTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly ConcurrentQueue<string> _events = new();
    private readonly Bridge _bridge;
    private IDisposable? _hooks;

    public StatusRequestsTests()
    {
        _bridge = new Bridge(_rig.State, _rig.Log);
        _hooks = Requests.RegisterAll(_bridge, new StatusSources(
            "9.9.9", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, [_rig.Marker], () => new SettingsView(true, "COM20", null)));
        _bridge.Subscribe(_events.Enqueue);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks?.Dispose();
        await _rig.DisposeAsync();
    }

    [Fact]
    public async Task The_snapshot_brings_version_schema_config_state_step_types_and_settings()
    {
        using JsonDocument reply = JsonDocument.Parse(await Ask("""{"id":1,"request":"app.snapshot","data":{}}"""));
        JsonElement data = reply.RootElement.GetProperty("data");

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("9.9.9", data.GetProperty("version").GetString());
        Assert.Equal(1, data.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("grabacion", data.GetProperty("config").GetProperty("mainPage").GetString());
        Assert.Equal(_rig.State.Current.Seq, data.GetProperty("state").GetProperty("seq").GetInt64());
        Assert.Equal("obs.marker", data.GetProperty("stepTypes")[0].GetProperty("id").GetString());
        Assert.Equal(0, data.GetProperty("conditionTypes").GetArrayLength());
        Assert.Equal("COM20", data.GetProperty("settings").GetProperty("consolePort").GetString());
    }

    [Fact]
    public async Task Snapshot_through_the_tray_is_refused()
    {
        Assert.Equal(ErrorCodes.NoPermission, Code(await Ask("""{"id":1,"request":"app.snapshot","data":{}}""", Caller.Tray)));
    }

    [Fact]
    public async Task Log_read_gives_the_last_entries_that_pass_the_filters()
    {
        // requestedBy mcp: ninguna otra línea del despachador de prueba lo lleva.
        _rig.Log.Write(LogLevel.Warning, "prueba", "uno", null, Trigger.Mcp);
        _rig.Log.Write(LogLevel.Info, "prueba", "dos", null, Trigger.Mcp);
        _rig.Log.Write(LogLevel.Error, "prueba", "tres", null, Trigger.Mcp);

        using JsonDocument reply = JsonDocument.Parse(await Ask("""{"id":2,"request":"log.read","data":{"count":2,"level":"warning","requestedBy":"mcp"}}"""));

        string?[] messages = [.. reply.RootElement.GetProperty("data").GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("message").GetString())];
        Assert.Equal(new[] { "uno", "tres" }, messages);
    }

    [Fact]
    public async Task Log_read_with_an_unknown_field_is_invalid_data()
    {
        Assert.Equal(ErrorCodes.InvalidData, Code(await Ask("""{"id":3,"request":"log.read","data":{"lines":3}}""")));
    }

    [Fact]
    public async Task Cancel_by_run_id_cancels_that_run()
    {
        System.Action release = _rig.Hold();
        var runId = Guid.NewGuid();
        Task<ActionResult> run = _rig.Dispatcher.Launch("marca-short", new Invocation(runId, Trigger.Key, null, null, null, null));

        string reply = await Ask(Envelope.Request(4, RequestNames.ActionCancel, new ActionCancelRequest(RunId: runId)));
        release();
        await run;

        Assert.Equal("""{"id":4,"ok":true,"data":{"cancelled":1}}""", reply);
    }

    [Fact]
    public async Task Cancel_all_says_how_many()
    {
        System.Action release = _rig.Hold();
        Task<ActionResult> first = _rig.Dispatcher.Launch("marca-short", Invocation());
        Task<ActionResult> second = _rig.Dispatcher.Launch("marca-inicio", Invocation());

        string reply = await Ask("""{"id":5,"request":"action.cancel","data":{"all":true}}""", Caller.Tray);
        release();
        await Task.WhenAll(first, second);

        Assert.Equal("""{"id":5,"ok":true,"data":{"cancelled":2}}""", reply);
        Assert.Contains(_rig.Log.Recent(), entry => entry.Module == "bridge" && entry.RequestedBy == Trigger.Tray);
    }

    [Fact]
    public async Task Cancel_of_a_run_that_already_ended_says_zero()
    {
        string reply = await Ask(Envelope.Request(6, RequestNames.ActionCancel, new ActionCancelRequest(RunId: Guid.NewGuid())));

        Assert.Equal("""{"id":6,"ok":true,"data":{"cancelled":0}}""", reply);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"all":true,"runId":"0f8fad5b-d9cb-469f-a165-70867728950e"}""")]
    public async Task Cancel_needs_a_run_id_or_all_but_not_both(string data)
    {
        Assert.Equal(ErrorCodes.InvalidData, Code(await Ask("""{"id":7,"request":"action.cancel","data":""" + data + "}")));
    }

    [Fact]
    public async Task A_state_change_goes_out_as_a_state_event_with_its_own_seq()
    {
        _rig.State.Update(s => s with { BasePage = "otra" });
        long seq = _rig.State.Current.Seq;

        string json = await Event(e => e.Contains("\"basePage\":\"otra\"", StringComparison.Ordinal));

        using JsonDocument evt = JsonDocument.Parse(json);
        Assert.Equal("state", evt.RootElement.GetProperty("event").GetString());
        Assert.Equal(seq, evt.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(seq, evt.RootElement.GetProperty("data").GetProperty("seq").GetInt64());
    }

    [Fact]
    public async Task A_log_line_goes_out_as_a_log_event()
    {
        _rig.Log.Info("prueba", "una línea para la ventana");

        string json = await Event(e => e.Contains("una línea para la ventana", StringComparison.Ordinal));

        Assert.StartsWith("""{"event":"log","seq":""", json);
    }

    [Fact]
    public async Task A_finished_action_goes_out_as_action_finished()
    {
        await _rig.Dispatcher.Launch("marca-short", Invocation());

        string json = await Event(e => e.StartsWith("""{"event":"action.finished""", StringComparison.Ordinal));

        Assert.Contains("\"actionId\":\"marca-short\",", json);
        Assert.Contains("\"ok\":true,", json);
    }

    [Fact]
    public async Task Reloading_the_library_goes_out_as_config_changed()
    {
        _rig.Library.Load();

        string json = await Event(e => e.StartsWith("""{"event":"config.changed""", StringComparison.Ordinal));

        Assert.Contains("\"mainPage\":\"grabacion\"", json);
    }

    [Fact]
    public void A_log_line_written_while_publishing_a_log_event_does_not_loop()
    {
        // Un suscriptor que escribe en el registro cada vez que ve un evento log: sin la guarda, esto no termina nunca.
        using IDisposable writer = _bridge.Subscribe(json =>
        {
            if (json.StartsWith("""{"event":"log""", StringComparison.Ordinal))
            {
                _rig.Log.Info("prueba", "escrita desde el suscriptor");
            }
        });

        _rig.Log.Info("prueba", "disparo");

        Assert.Single(_rig.Log.Recent(), entry => entry.Message == "escrita desde el suscriptor");
    }

    [Fact]
    public async Task A_log_line_written_under_a_lock_that_a_state_update_waits_for_does_not_deadlock()
    {
        // La cadena entera del Dispatcher: una línea escrita con su _gate tomado (Dispatcher.cs:126-131) sale como evento log en ese
        // mismo hilo, mientras PublishRunning espera ese _gate adentro de StateStore.Update (:194-201).
        var gate = new Lock();
        using var holding = new ManualResetEventSlim();
        using var updating = new ManualResetEventSlim();
        Task writer = Task.Run(() =>
        {
            lock (gate)
            {
                holding.Set();
                updating.Wait(TimeSpan.FromSeconds(10));
                _rig.Log.Info("prueba", "escrita con un lock tomado");
            }
        });
        Task updater = Task.Run(() =>
        {
            holding.Wait(TimeSpan.FromSeconds(10));
            _rig.State.Update(s =>
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

        Task both = Task.WhenAll(writer, updater);
        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(5))));
        await Event(e => e.Contains("escrita con un lock tomado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_unhooking_nothing_more_goes_out()
    {
        _hooks!.Dispose();
        _hooks = null;

        _rig.Log.Info("prueba", "después de soltar");
        _rig.State.Update(s => s with { BasePage = "después de soltar" });
        await Task.Delay(200);

        Assert.DoesNotContain(_events, e => e.Contains("después de soltar", StringComparison.Ordinal));
    }

    private Task<string> Ask(string json, Caller caller = Caller.Window) => _bridge.HandleAsync(json, caller, CancellationToken.None);

    private async Task<string> Event(Func<string, bool> match)
    {
        await Poll.Until(() => _events.Any(match));
        return _events.Last(match);
    }

    private static Invocation Invocation() => new(Guid.NewGuid(), Trigger.Tray, null, null, null, null);

    private static string? Code(string reply)
    {
        using JsonDocument document = JsonDocument.Parse(reply);
        return document.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetProperty("code").GetString() : null;
    }
}
