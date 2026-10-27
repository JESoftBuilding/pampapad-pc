using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class HotkeySourceSuspendTests : IAsyncLifetime
{
    private const string Marks = """{ "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" }, { "position": 6, "label": "Inicio", "action": "marca-inicio", "hotkey": "Ctrl+Shift+Alt+F6" }""";
    private const string Others = """{ "position": 1, "label": "Otra", "action": "otra", "hotkey": "Ctrl+Alt+O" }""";
    private const string Stop = """{ "hotkey": "Ctrl+Shift+Alt+D", "action": "parar" }""";

    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly FakeSystemHotkeys _windows = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<InputEvent> _events = [];
    private Log _log = null!;
    private HotkeySource _source = null!;

    public async Task InitializeAsync()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _source = new HotkeySource(_windows, _state, _log, _clock);
        _source.Input += _events.Add;
        await _source.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    private static Page PageOf(string keys) =>
        JsonSerializer.Deserialize<Page>($$"""{ "id": "grabacion", "name": "Grabación", "keys": [ {{keys}} ] }""", Json.Options)!;

    private static IReadOnlyList<HotkeyBinding> General(string hotkeys) =>
        JsonSerializer.Deserialize<List<HotkeyBinding>>($"[ {hotkeys} ]", Json.Options)!;

    private string[] Registered() => [.. _windows.Registered.Values.Select(hotkey => hotkey.ToString()).Order()];

    [Fact]
    public void Suspending_lets_go_of_every_combination_and_resuming_takes_the_same_ones_again()
    {
        _source.Apply(PageOf(Marks), General(Stop));
        string[] before = Registered();

        _source.Suspend(true);

        Assert.Equal(3, before.Length);
        Assert.Empty(_windows.Registered);
        Assert.Equal(new HotkeysState(0, 0), _state.Current.Hotkeys);
        Assert.True(_source.Suspended);

        _source.Suspend(false);

        Assert.Equal(before, Registered());
        Assert.Equal(new HotkeysState(3, 0), _state.Current.Hotkeys);
        Assert.False(_source.Suspended);
    }

    [Fact]
    public void While_suspended_a_press_does_nothing_and_a_new_page_is_remembered_but_not_taken()
    {
        _source.Apply(PageOf(Marks), General(Stop));
        _source.Suspend(true);

        IReadOnlyList<Problem> applied = _source.Apply(PageOf(Others), General(Stop));

        Assert.Empty(applied);
        Assert.Empty(_windows.Registered);
        Assert.False(_windows.Press("Ctrl+Shift+Alt+F9"));

        _source.Suspend(false);

        Assert.Equal(new[] { "Ctrl+Alt+O", "Ctrl+Shift+Alt+D" }, Registered());
    }

    [Fact]
    public void A_combination_another_program_took_in_the_meantime_is_counted_when_resuming()
    {
        _source.Apply(PageOf(Marks), []);
        _source.Suspend(true);
        _windows.Taken.Add(Hotkey.Parse("Ctrl+Shift+Alt+F6")!.Value);

        _source.Suspend(false);

        Assert.Equal(new[] { "Ctrl+Shift+Alt+F9" }, Registered());
        Assert.Equal(new HotkeysState(1, 1), _state.Current.Hotkeys);
    }

    [Fact]
    public void Nobody_resuming_the_clock_does_it_after_the_limit_and_leaves_a_warning()
    {
        _source.Apply(PageOf(Marks), []);
        _source.Suspend(true);

        _clock.Advance(HotkeySource.SuspendLimit - TimeSpan.FromSeconds(1));
        Assert.True(_source.Suspended);
        Assert.Empty(_windows.Registered);

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(_source.Suspended);
        Assert.Equal(2, _windows.Registered.Count);
        Assert.Contains(_log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Module == "hotkeys" && entry.Message.Contains("30 s", StringComparison.Ordinal));
    }

    [Fact]
    public void Suspending_again_starts_the_limit_over()
    {
        _source.Apply(PageOf(Marks), []);
        _source.Suspend(true);
        _clock.Advance(TimeSpan.FromSeconds(20));

        _source.Suspend(true);
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True(_source.Suspended);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(_source.Suspended);
    }

    [Fact]
    public void Resuming_without_having_suspended_changes_nothing_and_a_late_clock_does_not_warn()
    {
        _source.Apply(PageOf(Marks), []);
        int[] ids = [.. _windows.Registered.Keys.Order()];
        _source.Suspend(true);
        _source.Suspend(false);
        int[] again = [.. _windows.Registered.Keys.Order()];

        _source.Suspend(false);
        _clock.Advance(HotkeySource.SuspendLimit * 2);

        Assert.Equal(2, ids.Length);
        Assert.Equal(2, again.Length);
        Assert.Equal(again, _windows.Registered.Keys.Order().ToArray());
        Assert.DoesNotContain(_log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Module == "hotkeys");
    }

    [Fact]
    public async Task Closing_the_source_while_suspended_drops_the_clock_and_takes_nothing()
    {
        _source.Apply(PageOf(Marks), []);
        _source.Suspend(true);

        await _source.DisposeAsync();
        _clock.Advance(HotkeySource.SuspendLimit * 2);

        Assert.Empty(_windows.Registered);
        _source.Suspend(false);
        Assert.Empty(_windows.Registered);
    }
}
