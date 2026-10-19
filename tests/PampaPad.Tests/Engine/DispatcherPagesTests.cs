using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La base que elige page.go y las reglas 1, 2, 5, 6 y 7 de navegación (02 §7), con la biblioteca de ejemplo: grabacion y escenas.</summary>
public class DispatcherPagesTests
{
    // La tecla 10 de grabacion: su atajo es de la tecla y vale sólo con grabacion de base (regla 6). Los de la lista general valen siempre.
    private const string Mic = "Ctrl+Shift+Alt+M";

    // Una de cada tipo de página: la de teclas sin type, la local, la de teclas con type y una reservada.
    private const string Types = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            { "id": "grabacion", "name": "Grabación" },
            { "id": "sin-pc", "name": "Sin la PC", "type": "local" },
            { "id": "escenas", "name": "Escenas", "type": "keys" },
            { "id": "chequeo", "name": "Chequeo previo", "type": "preflight" }
          ]
        }
        """;

    // La tecla 5 en las dos páginas, y en escenas con su atajo de tecla.
    private const string KeyHotkey = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            { "id": "grabacion", "name": "Grabación", "keys": [ { "position": 5, "label": "Short", "action": "marca-short" } ] },
            { "id": "escenas", "name": "Escenas", "keys": [ { "position": 5, "label": "Cámara", "action": "camara", "hotkey": "Ctrl+Shift+Alt+Z" } ] }
          ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "camara", "name": "Cámara", "steps": [ { "type": "obs.scene", "scene": "Cámara" } ] }
          ]
        }
        """;

    private static void Recording(DispatcherRig rig, RecordingState recording) =>
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = recording } });

    private static IEnumerable<string> Texts(IEnumerable<Hotkey> hotkeys) => hotkeys.Select(hotkey => hotkey.ToString()).Order();

    // Traba los avisos del estado hasta que se llame a lo que devuelve: lo que cambie mientras tanto no le llega al Dispatcher, así que la
    // regla 2 no se entera de que arrancó la grabación y la base sigue siendo la elegida. Traba con un cambio que no mira nadie (Streaming),
    // porque el Dispatcher se suscribió antes y del de la grabación ya se habría enterado.
    private static System.Action HoldStateNotices(DispatcherRig rig)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable held = rig.State.Subscribe((_, after) =>
        {
            if (after.Obs.Streaming)
            {
                release.Task.Wait();
            }
        });
        rig.State.Update(s => s with { Obs = s.Obs with { Streaming = true } });
        return () =>
        {
            release.TrySetResult();
            held.Dispose();
        };
    }

    [Fact]
    public async Task Going_to_another_page_changes_the_base_and_its_hotkeys()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        bool micBefore = rig.Windows.Press(Mic);

        Failure? failure = rig.Dispatcher.Go("escenas");

        Assert.Null(failure);
        Assert.Equal("escenas", rig.State.Current.BasePage);
        Assert.Equal("escenas", rig.State.Current.ActivePage);
        Assert.Equal("grabacion", rig.State.Current.MainPage);
        Assert.True(micBefore);

        // Regla 6: la página de escenas no tiene atajos de tecla; quedan sólo los de la lista general hotkeys.
        Assert.Equal(Texts(rig.Library.Current.Hotkeys!.Select(binding => Hotkey.Parse(binding.Hotkey)!.Value)), Texts(rig.Windows.Registered.Values));
        Assert.False(rig.Windows.Press(Mic));
    }

    [Theory]
    [InlineData("main", "grabacion")]
    [InlineData("next", "grabacion")]
    [InlineData("previous", "grabacion")]
    [InlineData("grabacion", "grabacion")]
    public async Task Main_next_previous_and_an_id_come_back(string target, string expected)
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Dispatcher.Go("escenas");

        Failure? failure = rig.Dispatcher.Go(target);

        Assert.Null(failure);
        Assert.Equal(expected, rig.State.Current.BasePage);
        Assert.True(rig.Windows.Press(Mic));
    }

    [Fact]
    public async Task Next_and_previous_skip_the_local_page_and_the_types_that_are_not_built()
    {
        // 02 §7, regla 7: la página local no está en el recorrido; 02 §4.2: un tipo reservado o que el programa no conoce se saltea.
        await using var rig = new DispatcherRig(Types);

        rig.Dispatcher.Go("next");
        string next = rig.State.Current.BasePage;
        rig.Dispatcher.Go("next");
        string around = rig.State.Current.BasePage;
        rig.Dispatcher.Go("previous");

        Assert.Equal("escenas", next);
        Assert.Equal("grabacion", around);
        Assert.Equal("escenas", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task A_page_that_is_not_of_keys_is_not_a_base_even_by_its_id()
    {
        // Lo mismo que el recorrido, por id: la local la muestra la consola sola (regla 7) y un tipo reservado se saltea (02 §4.2). Una
        // recarga que deja la elegida con otro tipo la suelta, como si no existiera.
        await using var rig = new DispatcherRig(Types);
        rig.Dispatcher.Go("escenas");

        Failure? local = rig.Dispatcher.Go("sin-pc");
        Failure? reserved = rig.Dispatcher.Go("chequeo");
        string before = rig.State.Current.BasePage;
        rig.Write(Types.Replace("\"type\": \"keys\"", "\"type\": \"local\"", StringComparison.Ordinal));
        rig.Library.Load();

        Assert.Equal(ErrorCodes.ConfigInvalid, local?.Code);
        Assert.Equal("La página \"sin-pc\" es de tipo local, que no es de teclas: no se cambió de página.", local?.Message);
        Assert.Equal(ErrorCodes.ConfigInvalid, reserved?.Code);
        Assert.Equal("escenas", before);
        Assert.Equal("grabacion", rig.State.Current.BasePage);
    }

    [Theory]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Stopping)]
    public async Task With_a_take_in_progress_going_to_a_page_is_navigation_blocked_and_the_base_stays(RecordingState recording)
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        Recording(rig, recording);

        Failure? failure = rig.Dispatcher.Go("escenas");

        Assert.Equal(ErrorCodes.NavigationBlocked, failure?.Code);
        Assert.Equal("grabacion", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task A_page_that_does_not_exist_is_config_invalid_and_nothing_changes()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Dispatcher.Go("escenas");

        Failure? failure = rig.Dispatcher.Go("sala");

        Assert.Equal(ErrorCodes.ConfigInvalid, failure?.Code);
        Assert.Equal("La página \"sala\" no existe: no se cambió de página.", failure?.Message);
        Assert.Equal("escenas", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task When_the_recording_starts_on_another_page_the_base_goes_back_to_main_and_stays()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Dispatcher.Go("escenas");

        Recording(rig, RecordingState.Starting);
        await Poll.Until(() => rig.State.Current.BasePage == "grabacion");
        await Poll.Until(() => rig.Windows.Press(Mic));
        Recording(rig, RecordingState.Stopped);
        rig.Library.Load();

        Assert.Equal("grabacion", rig.State.Current.BasePage);
        Assert.Contains(rig.Log.Recent(), entry => entry.Message.Contains("regla 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task While_recording_the_keys_are_those_of_main_even_before_the_base_changes()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Dispatcher.Go("escenas");
        System.Action release = HoldStateNotices(rig);
        string baseAtTheKey;
        try
        {
            // Arranca la grabación y la tecla 5 llega antes de que la regla 2 cambie la base: en grabacion es la marca short; en escenas,
            // Cámara (zoom).
            Recording(rig, RecordingState.Recording);
            rig.Dispatcher.Receive(new InputEvent("console", Controls.Key(5), Gesture.Press));
            await Poll.Until(() => rig.Finished.Count == 1);
            baseAtTheKey = rig.State.Current.BasePage;
        }
        finally
        {
            release();
        }

        Assert.Equal("escenas", baseAtTheKey);
        Assert.Single(rig.Marker.Runs);
        Assert.Empty(rig.Scene.Runs);
    }

    [Fact]
    public async Task A_key_hotkey_of_the_base_that_no_longer_rules_does_nothing()
    {
        // Grabando, hasta que la regla 2 registra los de la principal, siguen tomados los atajos de la elegida. HotkeySource manda sólo la
        // posición: sin esto, el de la tecla 5 de escenas dispararía la tecla 5 de grabacion, otra acción.
        await using var rig = new DispatcherRig(KeyHotkey);
        rig.Dispatcher.Go("escenas");
        System.Action release = HoldStateNotices(rig);
        bool taken;
        try
        {
            Recording(rig, RecordingState.Recording);
            taken = rig.Windows.Press("Ctrl+Shift+Alt+Z");
        }
        finally
        {
            release();
        }

        Assert.True(taken);
        Assert.Contains(rig.Log.Recent(), entry => entry.Message == "El atajo de la tecla 5 es de la página escenas, que ya no rige: se ignora.");
        Assert.Empty(rig.Marker.Runs);
        Assert.Empty(rig.Scene.Runs);
    }

    [Fact]
    public async Task A_reload_keeps_the_chosen_base_while_the_page_exists()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Dispatcher.Go("escenas");

        rig.Library.Load();
        string kept = rig.State.Current.BasePage;
        rig.Write(DispatcherRig.Example.Replace("\"id\": \"escenas\"", "\"id\": \"escenas-viejas\"", StringComparison.Ordinal));
        rig.Library.Load();

        Assert.Equal("escenas", kept);
        Assert.Equal("grabacion", rig.State.Current.BasePage);
    }
}
