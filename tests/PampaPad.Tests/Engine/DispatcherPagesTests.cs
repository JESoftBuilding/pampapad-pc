using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La base que elige page.go y las reglas 1, 2, 5, 6 y 7 de navegación (02 §7), con la biblioteca de ejemplo: grabacion y escenas.</summary>
public class DispatcherPagesTests
{
    // La tecla 10 de grabacion: su atajo es de la tecla y vale sólo con grabacion de base (regla 6). Los de la lista general valen siempre.
    private const string Mic = "Ctrl+Shift+Alt+M";

    private static void Recording(DispatcherRig rig, RecordingState recording) =>
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = recording } });

    private static IEnumerable<string> Texts(IEnumerable<Hotkey> hotkeys) => hotkeys.Select(hotkey => hotkey.ToString()).Order();

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
        await using var rig = new DispatcherRig("""
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
            """);

        rig.Dispatcher.Go("next");
        string next = rig.State.Current.BasePage;
        rig.Dispatcher.Go("next");
        string around = rig.State.Current.BasePage;
        rig.Dispatcher.Go("previous");

        Assert.Equal("escenas", next);
        Assert.Equal("grabacion", around);
        Assert.Equal("escenas", rig.State.Current.BasePage);
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

        // Arranca la grabación y en el mismo instante llega la tecla 5: en grabacion es la marca short; en escenas, Cámara (zoom).
        Recording(rig, RecordingState.Recording);
        rig.Dispatcher.Receive(new InputEvent("console", Controls.Key(5), Gesture.Press));
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Single(rig.Marker.Runs);
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
