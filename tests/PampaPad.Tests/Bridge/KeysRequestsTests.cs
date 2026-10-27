using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class KeysRequestsTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly RealCatalog _catalog;
    private Bridge _bridge = null!;
    private IDisposable? _hooks;

    public KeysRequestsTests()
    {
        _catalog = new RealCatalog(_rig.Library);
        Bind(_rig.Hotkeys);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks?.Dispose();
        await _catalog.DisposeAsync();
        await _rig.DisposeAsync();
    }

    // Una fuente de atajos, o null: la copia de desarrollo no los toma (05 §10).
    private void Bind(HotkeySource? hotkeys)
    {
        _hooks?.Dispose();
        _bridge = new Bridge(_rig.State, _rig.Log);
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, [_rig.Marker], [], () => new SettingsView(true, null, null)),
            new EditSources(_rig.Library, _rig.State, _rig.Log, _catalog.Executor, _catalog.Steps, _catalog.Conditions, _rig.Dispatcher, new FakeWindows(), hotkeys));
    }

    private async Task<JsonDocument> SuspendAsync(string data, Caller caller = Caller.Window) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"hotkeys.suspend","data":{{data}}}""", caller, CancellationToken.None));

    [Fact]
    public async Task Suspending_through_the_window_lets_go_of_the_combinations_and_resuming_takes_them_again()
    {
        await Poll.Until(() => _rig.Windows.Registered.Count > 0);
        int taken = _rig.Windows.Registered.Count;

        using JsonDocument suspended = await SuspendAsync("""{"suspended":true}""");
        int whileSuspended = _rig.Windows.Registered.Count;
        using JsonDocument resumed = await SuspendAsync("""{"suspended":false}""");

        Assert.True(suspended.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, whileSuspended);
        Assert.True(resumed.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(taken, _rig.Windows.Registered.Count);
    }

    [Fact]
    public async Task The_mcp_cannot_suspend_the_hotkeys_and_a_field_that_is_not_known_is_invalid_data()
    {
        using JsonDocument mcp = await SuspendAsync("""{"suspended":true}""", Caller.Mcp);
        using JsonDocument unknown = await SuspendAsync("""{"suspended":true,"on":1}""");

        Assert.Equal(ErrorCodes.NoPermission, mcp.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.InvalidData, unknown.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(_rig.Hotkeys.Suspended);
    }

    [Theory]
    [InlineData(RecordingState.Recording, false)]
    [InlineData(RecordingState.Paused, false)]
    [InlineData(RecordingState.Stopped, true)]
    public async Task With_a_take_in_progress_a_combination_is_not_recorded_but_resuming_is_always_allowed(RecordingState recording, bool streaming)
    {
        _rig.State.Update(s => s with { Obs = s.Obs with { Recording = recording, Streaming = streaming } });

        using JsonDocument suspend = await SuspendAsync("""{"suspended":true}""");
        using JsonDocument resume = await SuspendAsync("""{"suspended":false}""");

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, suspend.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(_rig.Hotkeys.Suspended);
        Assert.True(resume.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task A_resume_that_arrives_with_a_take_in_progress_lifts_a_suspension_that_was_already_there()
    {
        _rig.Hotkeys.Suspend(true);
        _rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        using JsonDocument resume = await SuspendAsync("""{"suspended":false}""");

        Assert.True(resume.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(_rig.Hotkeys.Suspended);
    }

    [Fact]
    public async Task Without_a_hotkey_source_it_answers_fine_and_does_nothing()
    {
        Bind(null);

        using JsonDocument reply = await SuspendAsync("""{"suspended":true}""");

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(_rig.Hotkeys.Suspended);
    }
}
