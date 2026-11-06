using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La principal sigue a OBS (02 §7, regla 9; plan de la 1.5.0, decisiones 3 a 5): con OBS abierto rige grabacion y cerrado, escritorio. OBS
/// se simula cambiando Obs.Link en el estado.
/// </summary>
public class DispatcherFollowsObsTests
{
    // El atajo de la tecla 1 de cada página: vale sólo con esa página de base (regla 6).
    private const string OnGrabacion = "Ctrl+Shift+Alt+Z";
    private const string OnEscritorio = "Ctrl+Shift+Alt+X";

    private const string Config = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "mainPageWithoutObs": "escritorio",
          "moduleKey": { "layer": "capa" },
          "pages": [
            { "id": "grabacion", "keys": [ { "position": 1, "action": "a", "hotkey": "Ctrl+Shift+Alt+Z" } ] },
            { "id": "escenas", "keys": [] },
            { "id": "escritorio", "keys": [ { "position": 1, "action": "b", "hotkey": "Ctrl+Shift+Alt+X" } ] },
            { "id": "luces", "show": false, "keys": [] },
            { "id": "capa", "show": false, "keys": [] }
          ],
          "actions": [
            { "id": "a", "steps": [ { "type": "obs.marker", "marker": "a" } ] },
            { "id": "b", "steps": [ { "type": "obs.marker", "marker": "b" } ] }
          ]
        }
        """;

    private static readonly string WithoutField = Config.Replace("\"mainPageWithoutObs\": \"escritorio\",", "", StringComparison.Ordinal);

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public async Task On_the_main_page_closing_obs_goes_to_escritorio_and_opening_it_back_to_grabacion_with_their_hotkeys()
    {
        await using var rig = await Open(Config);

        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.BasePage == "escritorio");
        (bool desk, bool rec) closed = (rig.Windows.Press(OnEscritorio), rig.Windows.Press(OnGrabacion));
        Link(rig, ObsLinkState.Ready);
        await Poll.Until(() => rig.State.Current.BasePage == "grabacion");

        Assert.Equal((true, false), closed);
        Assert.Equal(("grabacion", "grabacion", (string?)null), Pages(rig));
        Assert.True(rig.Windows.Press(OnGrabacion));
        Assert.False(rig.Windows.Press(OnEscritorio));
    }

    [Fact]
    public async Task A_chosen_page_stays_when_obs_opens_or_closes()
    {
        await using var rig = await Open(Config);
        rig.Dispatcher.Go("escenas");

        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.MainPage == "escritorio");
        string closed = rig.State.Current.BasePage;
        Link(rig, ObsLinkState.Ready);
        await Poll.Until(() => rig.State.Current.MainPage == "grabacion");

        Assert.Equal("escenas", closed);
        Assert.Equal("escenas", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task The_overlay_stays_on_top_the_base_changes_below_and_a_tap_goes_to_the_main_page_of_now()
    {
        await using var rig = await Open(Config);
        rig.Dispatcher.ToggleOverlay("luces");

        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.BasePage == "escritorio");
        var below = Pages(rig);
        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, Controls.ModuleKey, Gesture.Release);

        Assert.Equal(("escritorio", "luces", "luces"), below);
        Assert.Equal(("escritorio", "escritorio", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task The_held_layer_rules_and_on_release_the_new_base_does()
    {
        await using var rig = await Open(Config);
        Send(rig, Controls.ModuleKey, Gesture.Press);

        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.BasePage == "escritorio");
        string held = rig.State.Current.ActivePage;
        HoldPast(rig);
        Send(rig, Controls.ModuleKey, Gesture.Release);

        Assert.Equal("capa", held);
        Assert.Equal(("escritorio", "escritorio", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task Escritorio_chosen_with_obs_open_becomes_the_main_page_when_obs_closes()
    {
        await using var rig = await Open(Config);
        rig.Dispatcher.Go("escritorio");

        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.MainPage == "escritorio");
        Link(rig, ObsLinkState.Ready);
        await Poll.Until(() => rig.State.Current.MainPage == "grabacion");

        Assert.Equal("grabacion", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task Without_the_field_opening_and_closing_obs_changes_nothing_and_writes_nothing()
    {
        await using var rig = await Open(WithoutField);

        Link(rig, ObsLinkState.Closed);
        Link(rig, ObsLinkState.Ready);
        Link(rig, ObsLinkState.Closed);
        await Delivered(rig);

        Assert.Equal(("grabacion", "grabacion", (string?)null), Pages(rig));
        Assert.Empty(ObsLines(rig));
    }

    [Fact]
    public async Task At_start_closed_then_ready_the_base_ends_on_mainPage_with_its_hotkeys()
    {
        // El estado arranca en Closed (ObsState.cs): la primera página que se aplica es escritorio, hasta el primer intento con OBS.
        await using var rig = new DispatcherRig(Config, clock: _clock);
        string first = rig.State.Current.BasePage;

        Link(rig, ObsLinkState.Loading);
        Link(rig, ObsLinkState.Ready);
        await Poll.Until(() => rig.State.Current.BasePage == "grabacion");

        Assert.Equal("escritorio", first);
        Assert.True(rig.Windows.Press(OnGrabacion));
        Assert.False(rig.Windows.Press(OnEscritorio));
    }

    [Fact]
    public async Task Recording_with_the_link_lost_stays_on_mainPage_and_closed_stops_and_follows_escritorio()
    {
        await using var rig = await Open(Config);
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        Link(rig, ObsLinkState.Lost);
        await Delivered(rig);
        string lost = rig.State.Current.BasePage;
        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.BasePage == "escritorio");

        Assert.Equal("grabacion", lost);
        Assert.Equal(RecordingState.Stopped, rig.State.Current.Obs.Recording);
    }

    [Fact]
    public async Task Two_changes_in_a_row_leave_the_base_of_the_last_one()
    {
        await using var rig = await Open(Config);

        Link(rig, ObsLinkState.Closed);
        Link(rig, ObsLinkState.Ready);
        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.BasePage == "escritorio");
        await Delivered(rig);

        Assert.Equal("escritorio", rig.State.Current.BasePage);
        Assert.True(rig.Windows.Press(OnEscritorio));
    }

    [Fact]
    public async Task With_obs_closed_a_reload_without_the_field_goes_back_to_mainPage_and_with_it_to_escritorio()
    {
        await using var rig = new DispatcherRig(Config, clock: _clock);

        rig.Write(WithoutField);
        rig.Library.Load();
        string without = rig.State.Current.BasePage;
        rig.Write(Config);
        rig.Library.Load();

        Assert.Equal("grabacion", without);
        Assert.Equal("escritorio", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task One_line_in_the_log_for_each_change_of_the_main_page()
    {
        await using var rig = await Open(Config);

        // Lost y Kicked siguen siendo OBS abierto: no cambian la principal.
        Link(rig, ObsLinkState.Lost);
        Link(rig, ObsLinkState.Kicked);
        Link(rig, ObsLinkState.Closed);
        await Poll.Until(() => rig.State.Current.MainPage == "escritorio");
        Link(rig, ObsLinkState.NoWebsocket);
        await Poll.Until(() => rig.State.Current.MainPage == "grabacion");
        Link(rig, ObsLinkState.Ready);
        await Delivered(rig);

        Assert.Equal(["OBS se abrió: la principal es grabacion.", "OBS se cerró: la principal es escritorio.", "OBS se abrió: la principal es grabacion."], ObsLines(rig));
    }

    // El rig con OBS listo y la base ya en grabacion: el estado arranca en Closed.
    private async Task<DispatcherRig> Open(string config)
    {
        var rig = new DispatcherRig(config, clock: _clock);
        Link(rig, ObsLinkState.Ready);
        await Poll.Until(() => rig.State.Current.MainPage == "grabacion");
        await Delivered(rig);
        return rig;
    }

    private static void Link(DispatcherRig rig, ObsLinkState link) => rig.State.Update(s => s with { Obs = s.Obs.WithLink(link) });

    // Espera a que el despachador haya visto los avisos del estado de hasta ahora: un solo hilo los entrega en orden, y él se suscribió
    // antes. Lo que encoló con ThreadPool puede seguir pendiente: sirve para ver que no se encoló nada.
    private static async Task Delivered(DispatcherRig rig)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long start = rig.State.Current.Seq;
        using IDisposable subscription = rig.State.Subscribe((_, after) =>
        {
            if (after.Seq > start)
            {
                seen.TrySetResult();
            }
        });
        rig.State.Update(s => s with { Obs = s.Obs with { Streaming = !s.Obs.Streaming } });
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static string[] ObsLines(DispatcherRig rig) =>
        [.. rig.Log.Recent().Where(entry => entry.Message.StartsWith("OBS se ", StringComparison.Ordinal)).Select(entry => entry.Message)];

    private static (string Base, string Active, string? Overlay) Pages(DispatcherRig rig) =>
        (rig.State.Current.BasePage, rig.State.Current.ActivePage, rig.State.Current.OverlayPage);

    private static void Send(DispatcherRig rig, string control, Gesture gesture) => rig.Dispatcher.Receive(new InputEvent("console", control, gesture));

    // Mantiene la tecla del módulo más allá de holdMs, refrescándola como la consola cada 50 ms.
    private void HoldPast(DispatcherRig rig)
    {
        for (int i = 0; i < 25; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(50));
            Send(rig, Controls.ModuleKey, Gesture.Press);
        }
    }
}
