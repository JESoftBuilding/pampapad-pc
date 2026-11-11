using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La cola, la conexión y lo que ve OpenRGB (plan de la 1.6.0, decisiones 5, 6, 8 y 9), con el doble de IOpenRgb.</summary>
public class PcLightsTests
{
    private static readonly Rgb Black = Rgb.Black;

    [Fact]
    public async Task A_color_is_direct_with_the_zone_scaled_and_the_rest_of_the_board_black()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");

        rig.Lights.Apply(new LightCommand(2, 0, 0, 0, 128));
        rig.Lights.Apply(new LightCommand(1, 255, 100, 0, 0));
        await rig.UntilAsync(() => Last(rig).Leds is { } leds && leds[5] == new Rgb(128, 50, 0), "el color escalado");

        var half = new Rgb(128, 50, 0);
        AssertWrite("Direct", [Black, Black, Black, Black, Black, half, half], Last(rig));
        Assert.Contains("mode 3 Direct", rig.OpenRgb.Calls);
    }

    [Theory]
    [InlineData(6, "Breathing", true)]
    [InlineData(7, "Spectrum Cycle", false)]
    public async Task An_effect_is_the_mode_of_decision_5_found_by_name(byte preset, string mode, bool colors)
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");

        rig.Lights.Apply(new LightCommand(4, 0, 0, 0, preset));
        await rig.UntilAsync(() => rig.OpenRgb.Writes.Count == 1 && (!colors || Last(rig).Leds is not null), mode);

        var violet = new Rgb(109, 93, 246);
        Assert.Equal(mode, Last(rig).Mode);
        Assert.Equal(colors ? new[] { Black, Black, Black, Black, Black, violet, violet } : null, Last(rig).Leds);
    }

    [Fact]
    public async Task Off_is_the_off_mode_without_colors()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");

        rig.Lights.Apply(new LightCommand(5, 0, 0, 0, 0));
        await rig.UntilAsync(() => rig.OpenRgb.Writes.Count == 1, "apagado");

        AssertWrite("Off", null, Last(rig));
    }

    [Fact]
    public async Task Ten_detents_in_a_row_end_at_the_right_brightness_with_few_writes()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");
        rig.Lights.Apply(new LightCommand(1, 255, 255, 255, 0));
        rig.Lights.Apply(new LightCommand(2, 0, 0, 0, 0));
        await rig.UntilAsync(() => rig.OpenRgb.Writes.Count == 1 && Last(rig).Leds is not null, "la primera escritura");

        for (int detent = 0; detent < 10; detent++)
        {
            rig.Lights.Apply(Lights.AdjustBrightness(Dispatcher.BrightnessPerDetent));
        }

        await Eventually.AdvancingAsync(rig.Clock, PcLights.WriteEvery, () => Last(rig).Leds?[5] == new Rgb(160, 160, 160), "brillo 160");
        await PcLightsRig.SettleAsync();

        Assert.Equal(160, rig.Lights.Model.Brightness);
        Assert.InRange(rig.OpenRgb.Writes.Count, 2, 3);
    }

    [Fact]
    public async Task A_write_right_after_another_waits_its_50_ms()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");
        rig.Lights.Apply(new LightCommand(1, 255, 0, 0, 0));
        await rig.UntilAsync(() => rig.OpenRgb.Writes.Count == 1, "la primera escritura");

        // Como mucho 20 escrituras por segundo, para no inundar el SMBus (decisión 9): con el reloj quieto, la siguiente no sale.
        rig.Lights.Apply(new LightCommand(1, 0, 255, 0, 0));
        await PcLightsRig.SettleAsync();
        Assert.Single(rig.OpenRgb.Writes);

        await Eventually.AdvancingAsync(rig.Clock, PcLights.WriteEvery, () => rig.OpenRgb.Writes.Count == 2, "la segunda, a los 50 ms");
    }

    [Fact]
    public async Task A_clock_set_back_by_windows_does_not_hold_the_next_write()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");
        rig.Lights.Apply(new LightCommand(1, 255, 0, 0, 0));
        await rig.UntilAsync(() => rig.OpenRgb.Writes.Count == 1, "la primera escritura");

        // Windows corrige la hora una hora para atrás: los 50 ms entre escrituras no pueden volverse una hora (revisión de RGB-3).
        rig.WallClockBack = TimeSpan.FromHours(1);
        rig.Lights.Apply(new LightCommand(1, 0, 255, 0, 0));

        await Eventually.AdvancingAsync(rig.Clock, PcLights.WriteEvery, () => rig.OpenRgb.Writes.Count == 2, "la segunda escritura");
        Assert.Equal(new Rgb(0, 255, 0), Last(rig).Leds?[5]);
    }

    [Fact]
    public async Task At_start_the_saved_state_is_put_once_when_openrgb_answers_on_the_third_try()
    {
        string local = $$"""{ "pcLights": { "device": "{{FakeOpenRgb.Board}}", "zone": "{{FakeOpenRgb.Header}}", "last": { "on": true, "color": { "r": 0, "g": 0, "b": 255 }, "brightness": 255 } } }""";
        await using var rig = new PcLightsRig(local, failConnects: 2).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Unreachable, "sin OpenRGB");

        await Eventually.AdvancingAsync(rig.Clock, TimeSpan.FromSeconds(1), () => rig.OpenRgb.Writes.Count > 0, "la escritura");
        await Eventually.StepsAsync(rig.Clock, PcLights.RetryEvery, 3);

        Assert.Equal(3, rig.OpenRgb.Connects);
        var blue = new Rgb(0, 0, 255);
        AssertWrite("Direct", [Black, Black, Black, Black, Black, blue, blue], Assert.Single(rig.OpenRgb.Writes));
        Assert.Equal(PcLightsLinkState.Connected, rig.Link);
        Assert.Single(rig.Log.Recent(), e => e.Module == "pclights" && e.Level == LogLevel.Warning);
        Assert.Single(rig.Log.Recent(), e => e.Module == "pclights" && e.Level == LogLevel.Info);
    }

    [Fact]
    public async Task With_a_zone_and_nothing_saved_it_connects_and_writes_nothing()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();

        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");
        await Eventually.StepsAsync(rig.Clock, PcLights.SaveEvery, 3);

        Assert.Empty(rig.OpenRgb.Writes);
        Assert.Equal(new PcLightsLink(PcLightsLinkState.Connected, $"{FakeOpenRgb.Board} · {FakeOpenRgb.Header}"), rig.State.Current.PcLights);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "consolePort": "COM7" }""")]
    [InlineData("""{ "pcLights": { "device": null, "zone": null } }""")]
    public async Task Without_pcLights_not_a_single_call_reaches_openrgb(string? local)
    {
        await using var rig = new PcLightsRig(local).Start();

        rig.Lights.Apply(new LightCommand(1, 255, 0, 0, 0));
        rig.Lights.Apply(new LightCommand(5, 0, 0, 0, 2));
        await Eventually.StepsAsync(rig.Clock, PcLights.RetryEvery, 2);

        Assert.Empty(rig.OpenRgb.Calls);
        Assert.Equal(PcLightsLink.Off, rig.State.Current.PcLights);
        Assert.Equal(local, File.Exists(rig.Path) ? File.ReadAllText(rig.Path) : null);
    }

    [Fact]
    public async Task A_failed_write_retries_every_5_s_with_a_single_cycle_and_one_log_line()
    {
        await using var rig = new PcLightsRig(PcLightsRig.Chosen).Start();
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Connected, "conectado");
        rig.OpenRgb.FailWrites = true;
        rig.Lights.Apply(new LightCommand(1, 255, 0, 0, 0));
        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Unreachable, "se perdió");

        for (int command = 0; command < 5; command++)
        {
            rig.Lights.Apply(Lights.AdjustBrightness(-16));
            await PcLightsRig.SettleAsync();
        }

        // El reintento va a los 5 s y no antes (decisión 6): a los 4 s sigue el primer intento.
        rig.Clock.Advance(PcLights.RetryEvery - TimeSpan.FromSeconds(1));
        await PcLightsRig.SettleAsync();
        Assert.Equal(1, rig.OpenRgb.Connects);
        await Eventually.StepsAsync(rig.Clock, PcLights.RetryEvery, 2);
        Assert.Equal(3, rig.OpenRgb.Connects);

        rig.OpenRgb.FailWrites = false;
        await Eventually.AdvancingAsync(rig.Clock, PcLights.RetryEvery, () => rig.Link == PcLightsLinkState.Connected, "volvió");
        await rig.UntilAsync(() => rig.OpenRgb.Writes.Any(w => w.Leds is not null), "el último estado");
        Assert.Equal(new Rgb(175, 0, 0), Last(rig).Leds![5]);
        Assert.Single(rig.Log.Recent(), e => e.Module == "pclights" && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task The_zones_are_listed_through_the_same_queue_even_without_a_zone_chosen()
    {
        await using var rig = new PcLightsRig(null).Start();

        IReadOnlyList<PcLightZone>? zones = await rig.Lights.ZonesAsync(CancellationToken.None);

        Assert.Equal(
            ["Corsair · Fans", $"{FakeOpenRgb.Board} · Back I/O", $"{FakeOpenRgb.Board} · {FakeOpenRgb.Header}"],
            zones!.Select(zone => zone.Label));
        Assert.Equal(PcLightsLink.Off, rig.State.Current.PcLights);
    }

    [Fact]
    public async Task Without_openrgb_the_zones_are_null()
    {
        await using var rig = new PcLightsRig(null, failConnects: 1).Start();

        Assert.Null(await rig.Lights.ZonesAsync(CancellationToken.None));
        Assert.Equal(PcLightsLink.Off, rig.State.Current.PcLights);
    }

    [Fact]
    public async Task A_zone_that_does_not_exist_is_unreachable_with_the_reason_in_the_log()
    {
        await using var rig = new PcLightsRig($$"""{ "pcLights": { "device": "{{FakeOpenRgb.Board}}", "zone": "Fans" } }""").Start();

        await rig.UntilAsync(() => rig.Link == PcLightsLinkState.Unreachable, "sin la zona");

        Assert.Contains(rig.Log.Recent(), e => e.Module == "pclights" && e.Message.Contains("\"Fans\"", StringComparison.Ordinal));
    }

    private static (string Mode, Rgb[]? Leds) Last(PcLightsRig rig) => rig.OpenRgb.Writes.Count == 0 ? ("", null) : rig.OpenRgb.Writes[^1];

    // Assert.Equal compara los arreglos por contenido sólo fuera de una tupla.
    private static void AssertWrite(string mode, Rgb[]? leds, (string Mode, Rgb[]? Leds) write)
    {
        Assert.Equal(mode, write.Mode);
        Assert.Equal(leds, write.Leds);
    }
}
