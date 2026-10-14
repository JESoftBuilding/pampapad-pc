using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que comparten las pruebas de bordes de la fuente de atajos: el reloj, el estado, el registro y Windows de mentira.</summary>
public abstract class HotkeySourceFixture : IAsyncLifetime
{
    protected const string Marks = """{ "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" }, { "position": 6, "label": "Inicio", "action": "marca-inicio", "hotkey": "Ctrl+Shift+Alt+F6" }, { "position": 7, "label": "Sin atajo", "action": "marca-fin" }""";

    private readonly TempFolder _temp = new();

    protected StateStore State { get; } = new();

    protected FakeSystemHotkeys Windows { get; } = new();

    protected List<InputEvent> Events { get; } = [];

    protected Log Log { get; private set; } = null!;

    protected HotkeySource Source { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Source = new HotkeySource(Windows, State, Log);
        Source.Input += Events.Add;
        await Source.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Source.DisposeAsync();
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }

    protected static Page PageOf(string keys) =>
        JsonSerializer.Deserialize<Page>($$"""{ "id": "grabacion", "name": "Grabación", "keys": [ {{keys}} ] }""", Json.Options)!;

    protected static IReadOnlyList<HotkeyBinding> General(string hotkeys) =>
        JsonSerializer.Deserialize<List<HotkeyBinding>>($"[ {hotkeys} ]", Json.Options)!;

    protected static Hotkey Parse(string text) => Hotkey.Parse(text)!.Value;

    protected IEnumerable<string> RegisteredTexts() => Windows.Registered.Values.Select(hotkey => hotkey.ToString()).Order();
}
