using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La página encima (page.overlay) y la perilla por página (plan de la 1.3.0, decisiones 5 a 7; 02 §7, regla 8), con Gestures, reloj
/// falso y un puerto falso que anota las líneas que llegarían a la consola.
/// </summary>
public class DispatcherOverlayTests
{
    // toma no dice knob y sala dice pages: las dos recorren. tenue es una base de brillo. luces y capa, fuera del recorrido, son de brillo;
    // nota está fuera del recorrido y sin knob. sin-pc no es de teclas.
    private const string Config = """
        {
          "version": 1,
          "mainPage": "toma",
          "moduleKey": { "layer": "capa" },
          "pages": [
            { "id": "toma", "keys": [ { "position": 3, "action": "en-toma" } ] },
            { "id": "sala", "knob": "pages", "keys": [] },
            { "id": "tenue", "knob": "brightness", "keys": [] },
            { "id": "luces", "show": false, "knob": "brightness", "keys": [ { "position": 3, "action": "en-luces" } ] },
            { "id": "capa", "show": false, "knob": "brightness", "keys": [] },
            { "id": "nota", "show": false, "keys": [] },
            { "id": "sin-pc", "type": "local" }
          ],
          "actions": [
            { "id": "en-toma", "steps": [ { "type": "obs.marker", "marker": "toma" } ] },
            { "id": "en-luces", "steps": [ { "type": "obs.marker", "marker": "luces" } ] },
            { "id": "abrir-luces", "steps": [ { "type": "page.overlay", "target": "luces" } ] }
          ]
        }
        """;

    private readonly FakeTimeProvider _clock = new();
    private readonly ConcurrentQueue<string> _sent = new();
    private Result _reply = Result.Success;

    [Fact]
    public async Task It_is_put_taken_away_and_put_again_without_touching_the_base()
    {
        await using var rig = Rig();

        Assert.Null(rig.Dispatcher.ToggleOverlay("luces"));
        Assert.Equal(("toma", "luces", "luces"), Pages(rig));

        Assert.Null(rig.Dispatcher.ToggleOverlay("luces"));
        Assert.Equal(("toma", "toma", (string?)null), Pages(rig));

        Assert.Null(rig.Dispatcher.ToggleOverlay("luces"));
        Assert.Equal(("toma", "luces", "luces"), Pages(rig));
    }

    [Fact]
    public async Task Its_keys_are_the_ones_that_run_from_the_console()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("luces");

        Send(rig, "key.3", Gesture.Press);
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal("en-luces", Assert.Single(rig.Finished).ActionId);
    }

    [Fact]
    public async Task The_page_overlay_step_of_an_action_puts_it()
    {
        await using var rig = Rig();

        ActionResult result = await rig.Dispatcher.Launch("abrir-luces", new Invocation(Guid.NewGuid(), Trigger.Key, "console", "moduleKey", Gesture.DoubleTap, 0));

        Assert.True(result.Ok);
        Assert.Equal(("toma", "luces", "luces"), Pages(rig));
    }

    [Theory]
    [InlineData("ninguna")]
    [InlineData("sin-pc")]
    public async Task A_page_that_does_not_exist_or_is_not_of_keys_fails_and_changes_nothing(string target)
    {
        await using var rig = Rig();

        Assert.Equal(ErrorCodes.ConfigInvalid, rig.Dispatcher.ToggleOverlay(target)?.Code);
        Assert.Equal(("toma", "toma", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task While_recording_it_is_put_and_the_base_stays_on_the_main_page()
    {
        await using var rig = Rig();
        Record(rig);

        Assert.Null(rig.Dispatcher.ToggleOverlay("luces"));
        Assert.Equal(ErrorCodes.NavigationBlocked, rig.Dispatcher.Go("sala")?.Code);

        Assert.Equal(("toma", "luces", "luces"), Pages(rig));
    }

    [Fact]
    public async Task Starting_a_take_closes_it()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("luces");

        Record(rig);
        await Poll.Until(() => rig.State.Current.OverlayPage is null);

        Assert.Equal(("toma", "toma", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task Page_go_closes_it()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("nota");

        Assert.Null(rig.Dispatcher.Go("sala"));

        Assert.Equal(("sala", "sala", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task A_turn_of_the_knob_that_moves_pages_closes_it()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("nota");

        Turn(rig, +1);

        Assert.Equal(("sala", "sala", (string?)null), Pages(rig));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task A_tap_of_the_module_key_closes_it_even_on_the_main_page()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("luces");

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, Controls.ModuleKey, Gesture.Release);

        Assert.Equal(("toma", "toma", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task The_held_layer_wins_and_on_release_the_overlay_comes_back()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("luces");

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Assert.Equal(("toma", "capa", "luces"), Pages(rig));

        HoldPast(rig);
        Send(rig, Controls.ModuleKey, Gesture.Release);
        Assert.Equal(("toma", "luces", "luces"), Pages(rig));
    }

    [Fact]
    public async Task A_reload_without_the_page_takes_it_away()
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("luces");

        rig.Write(Config.Replace("\"luces\"", "\"otra\"", StringComparison.Ordinal));
        rig.Library.Load();

        Assert.Equal(("toma", "toma", (string?)null), Pages(rig));
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(-2, -32)]
    [InlineData(7, 112)]
    [InlineData(8, 127)]
    [InlineData(-9, -127)]
    public async Task On_the_overlay_with_brightness_a_turn_sends_16_per_detent_clipped_at_127(int detents, int value)
    {
        await using var rig = Rig();
        rig.Dispatcher.ToggleOverlay("luces");

        Turn(rig, detents);

        Assert.Equal(Lights.AdjustBrightnessLine(value), Assert.Single(_sent));
        Assert.Equal(("toma", "luces", "luces"), Pages(rig));
        AssertNoRun(rig);
    }

    [Fact]
    public async Task On_the_layer_with_brightness_the_layer_rules_over_the_base()
    {
        await using var rig = Rig();

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Turn(rig, -1);

        Assert.Equal(Lights.AdjustBrightnessLine(-16), Assert.Single(_sent));
        Assert.Equal("toma", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task On_a_base_with_brightness_a_turn_does_not_move_pages()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("tenue");

        Turn(rig, +2);

        Assert.Equal(Lights.AdjustBrightnessLine(32), Assert.Single(_sent));
        Assert.Equal(("tenue", "tenue", (string?)null), Pages(rig));
    }

    [Fact]
    public async Task While_recording_a_turn_with_brightness_still_sends()
    {
        await using var rig = Rig();
        Record(rig);
        rig.Dispatcher.ToggleOverlay("luces");

        Turn(rig, +1);

        Assert.Equal(Lights.AdjustBrightnessLine(16), Assert.Single(_sent));
        AssertNoRun(rig);
    }

    [Theory]
    [InlineData("toma", "sala")]
    [InlineData("sala", "tenue")]
    public async Task With_pages_or_without_the_field_a_turn_moves_pages_as_before(string from, string to)
    {
        await using var rig = Rig();
        rig.Dispatcher.Go(from);

        Turn(rig, +1);

        Assert.Equal(to, rig.State.Current.BasePage);
        Assert.Empty(_sent);
    }

    // Con consola que no contesta (sin placa) y sin consola en el programa.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failure_of_the_lights_stays_in_the_log_as_a_warning_and_does_not_sound(bool console)
    {
        _reply = Result.Fail(ErrorCodes.ConsoleDisconnected, "La consola no está conectada.");
        await using var rig = console ? Rig() : new DispatcherRig(Config, clock: _clock);
        rig.Dispatcher.ToggleOverlay("luces");

        Turn(rig, +1);
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Code == ErrorCodes.ConsoleDisconnected));

        LogEntry entry = rig.Log.Recent().Single(entry => entry.Code == ErrorCodes.ConsoleDisconnected);
        Assert.Equal((LogLevel.Warning, "dispatcher"), (entry.Level, entry.Module));
        AssertNoRun(rig);
    }

    private DispatcherRig Rig() => new(Config, clock: _clock, send: (line, _) =>
    {
        _sent.Enqueue(line);
        return Task.FromResult(_reply);
    });

    private static (string Base, string Active, string? Overlay) Pages(DispatcherRig rig) =>
        (rig.State.Current.BasePage, rig.State.Current.ActivePage, rig.State.Current.OverlayPage);

    private static void Record(DispatcherRig rig) => rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

    // Un giro no corre acciones: ni corridas terminadas, ni en curso, ni líneas del registro con runId (que no suena lo da Finished vacío).
    private static void AssertNoRun(DispatcherRig rig)
    {
        Assert.Empty(rig.Finished);
        Assert.Empty(rig.State.Current.Running);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.RunId is not null);
    }

    private static void Send(DispatcherRig rig, string control, Gesture gesture, int value = 0) =>
        rig.Dispatcher.Receive(new InputEvent("console", control, gesture, value));

    private static void Turn(DispatcherRig rig, int detents) => Send(rig, Controls.Knob, Gesture.Turn, detents);

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
