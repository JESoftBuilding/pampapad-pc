using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class HotkeySourceTests : IAsyncLifetime
{
    private const string Marks = """{ "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" }, { "position": 6, "label": "Inicio", "action": "marca-inicio", "hotkey": "Ctrl+Shift+Alt+F6" }, { "position": 7, "label": "Sin atajo", "action": "marca-fin" }""";

    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly FakeSystemHotkeys _windows = new();
    private readonly List<InputEvent> _events = [];
    private Log _log = null!;
    private HotkeySource _source = null!;

    public async Task InitializeAsync()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _source = new HotkeySource(_windows, _state, _log);
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

    private IEnumerable<string> RegisteredTexts() => _windows.Registered.Values.Select(hotkey => hotkey.ToString()).Order();

    [Fact]
    public void Registers_the_hotkeys_of_the_base_page_and_the_general_ones()
    {
        IReadOnlyList<Problem> problems = _source.Apply(PageOf(Marks), General("""{ "hotkey": "Ctrl+Shift+Alt+F5", "action": "marca-largo" }"""));

        Assert.Empty(problems);
        Assert.Equal(new[] { "Ctrl+Shift+Alt+F5", "Ctrl+Shift+Alt+F6", "Ctrl+Shift+Alt+F9" }, RegisteredTexts());
        Assert.Equal(new HotkeysState(3, 0), _state.Current.Hotkeys);
    }

    [Fact]
    public void A_press_is_a_tap_of_its_key()
    {
        _source.Apply(PageOf(Marks), []);

        Assert.True(_windows.Press("Ctrl+Shift+Alt+F6"));

        Assert.Equal(new InputEvent("hotkeys", "key.6", Gesture.Tap), Assert.Single(_events));
    }

    [Fact]
    public void A_general_hotkey_is_a_tap_of_hotkey_with_its_place_in_the_list()
    {
        _source.Apply(PageOf(""), General("""{ "hotkey": "Ctrl+Alt+S", "action": "a" }, { "hotkey": "Ctrl+Alt+D", "action": "b" }"""));

        _windows.Press("Ctrl+Alt+D");

        Assert.Equal(new InputEvent("hotkeys", "hotkey", Gesture.Tap, 1), Assert.Single(_events));
    }

    [Fact]
    public void Two_presses_are_two_events()
    {
        _source.Apply(PageOf(Marks), []);

        _windows.Press("Ctrl+Shift+Alt+F9");
        _windows.Press("Ctrl+Shift+Alt+F9");

        Assert.Equal(2, _events.Count);
        Assert.All(_events, input => Assert.Equal("key.5", input.Control));
    }

    [Fact]
    public void Native_modifiers_always_carry_no_repeat()
    {
        // MOD_NOREPEAT (0x4000): mantener la combinación no repite el aviso. Ctrl es 0x2, Shift 0x4 y Alt 0x1.
        Assert.Equal(0x4007u, HotkeyNative.Modifiers(Hotkey.Parse("Ctrl+Shift+Alt+F9")!.Value));
        Assert.Equal(0x4001u, HotkeyNative.Modifiers(Hotkey.Parse("Alt+A")!.Value));
        Assert.Equal(0x4000u, HotkeyNative.NoRepeat);
    }

    [Fact]
    public void What_windows_rejects_comes_back_as_a_problem_of_its_key_and_the_rest_works()
    {
        _windows.Taken.Add(Hotkey.Parse("Ctrl+Shift+Alt+F6")!.Value);

        Problem problem = Assert.Single(_source.Apply(PageOf(Marks), []));

        Assert.Equal("keys[1].hotkey", problem.Where);
        Assert.Equal(ErrorCodes.HotkeyTaken, problem.Code);
        Assert.Equal(Severity.Warning, problem.Severity);
        Assert.Contains("Ctrl+Shift+Alt+F6", problem.Message);
        Assert.True(_windows.Press("Ctrl+Shift+Alt+F9"));
        Assert.False(_windows.Press("Ctrl+Shift+Alt+F6"));
        Assert.Equal(new HotkeysState(1, 1), _state.Current.Hotkeys);
    }

    [Fact]
    public void A_rejected_general_hotkey_names_its_place_in_the_list()
    {
        _windows.Taken.Add(Hotkey.Parse("Ctrl+Alt+D")!.Value);

        Problem problem = Assert.Single(_source.Apply(new Page(), General("""{ "hotkey": "Ctrl+Alt+S", "action": "a" }, { "hotkey": "Ctrl+Alt+D", "action": "b" }""")));

        Assert.Equal("hotkeys[1].hotkey", problem.Where);
    }

    [Fact]
    public void Applying_another_page_releases_the_previous_hotkeys()
    {
        _source.Apply(PageOf(Marks), []);

        _source.Apply(PageOf("""{ "position": 1, "label": "Otra", "action": "x", "hotkey": "Ctrl+Alt+B" }"""), []);

        Assert.Equal(new[] { "Ctrl+Alt+B" }, RegisteredTexts());
        Assert.False(_windows.Press("Ctrl+Shift+Alt+F9"));
    }

    [Fact]
    public void What_is_not_a_hotkey_or_is_repeated_is_not_registered()
    {
        IReadOnlyList<Problem> problems = _source.Apply(
            PageOf("""{ "position": 1, "label": "A", "hotkey": "Win+F7" }, { "position": 2, "label": "B", "hotkey": "Ctrl+F5" }, { "position": 3, "label": "C", "hotkey": "ctrl+f5" }"""),
            General("""{ "hotkey": "Ctrl+F5", "action": "x" }"""));

        Assert.Empty(problems);
        Assert.Single(_windows.Registered);
        _windows.Press("Ctrl+F5");
        Assert.Equal("key.2", Assert.Single(_events).Control);
    }

    [Fact]
    public void An_empty_page_releases_everything()
    {
        _source.Apply(PageOf(Marks), []);

        _source.Apply(new Page(), []);

        Assert.Empty(_windows.Registered);
        Assert.Equal(new HotkeysState(0, 0), _state.Current.Hotkeys);
    }

    [Fact]
    public async Task Disposing_releases_the_hotkeys()
    {
        _source.Apply(PageOf(Marks), []);

        await _source.DisposeAsync();

        Assert.Empty(_windows.Registered);
    }

    [Theory]
    [InlineData("key.1", true, 1)]
    [InlineData("key.12", true, 12)]
    [InlineData("key.13", false, 0)]
    [InlineData("key.0", false, 0)]
    [InlineData("hotkey", false, 0)]
    [InlineData("knob", false, 0)]
    public void A_control_says_which_key_it_is(string control, bool isKey, int position)
    {
        Assert.Equal(isKey, Controls.TryKey(control, out int found));
        Assert.Equal(position, found);
        Assert.Equal("key.7", Controls.Key(7));
    }

    [Fact]
    public void The_switch_of_this_pc_is_read_from_local_json()
    {
        Assert.Null(JsonSerializer.Deserialize<LocalData>("""{ "version": 1 }""", Json.Options)!.HotkeysEnabled);
        Assert.False(JsonSerializer.Deserialize<LocalData>("""{ "version": 1, "hotkeysEnabled": false }""", Json.Options)!.HotkeysEnabled);
    }
}
