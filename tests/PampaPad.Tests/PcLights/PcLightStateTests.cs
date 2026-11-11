using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El modelo de las luces de la PC contra LightController::apply de luces-rgb (LightController.cpp:17-61; plan de la 1.6.0, decisión 4).</summary>
public class PcLightStateTests
{
    private static readonly Rgb Violet = new(109, 93, 246);

    private static PcLightState Off => PcLightState.Initial;

    [Fact]
    public void It_starts_violet_at_full_brightness_off_and_without_effect()
    {
        Assert.Equal(new PcLightState(false, Violet, 255, null), PcLightState.Initial);
    }

    [Fact]
    public void A_color_turns_it_on_and_takes_the_effect_away()
    {
        var breathing = Off with { Effect = 6 };

        Assert.Equal(new PcLightState(true, new Rgb(1, 2, 3), 255, null), breathing.Apply(new LightCommand(1, 1, 2, 3, 0)));
    }

    [Theory]
    [InlineData(0, 109, 93, 246)]
    [InlineData(1, 245, 166, 35)]
    [InlineData(2, 255, 255, 255)]
    [InlineData(3, 255, 0, 0)]
    [InlineData(4, 0, 255, 0)]
    [InlineData(5, 0, 0, 255)]
    public void A_color_preset_turns_it_on_with_its_color_and_takes_the_effect_away(byte preset, byte r, byte g, byte b)
    {
        var cycling = Off with { Color = new Rgb(9, 9, 9), Effect = 7 };

        Assert.Equal(new PcLightState(true, new Rgb(r, g, b), 255, null), cycling.Apply(new LightCommand(4, 0, 0, 0, preset)));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void An_effect_preset_turns_it_on_and_keeps_the_last_color(byte preset)
    {
        var red = Off with { Color = new Rgb(255, 0, 0) };

        Assert.Equal(new PcLightState(true, new Rgb(255, 0, 0), 255, preset), red.Apply(new LightCommand(4, 0, 0, 0, preset)));
    }

    [Fact]
    public void A_preset_out_of_range_changes_nothing()
    {
        Assert.Equal(Off, Off.Apply(new LightCommand(4, 0, 0, 0, 10)));
    }

    [Fact]
    public void Brightness_does_not_turn_it_on()
    {
        Assert.Equal(Off with { Brightness = 40 }, Off.Apply(new LightCommand(2, 0, 0, 0, 40)));
    }

    [Theory]
    [InlineData(100, 16, 116)]
    [InlineData(100, -16, 84)]
    [InlineData(250, 16, 255)]
    [InlineData(10, -127, 0)]
    public void Adjusting_stays_between_0_and_255_and_does_not_turn_it_on(byte from, int step, byte to)
    {
        var start = Off with { Brightness = from };

        Assert.Equal(Off with { Brightness = to }, start.Apply(Lights.AdjustBrightness(step)));
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, false)]
    [InlineData(true, 3, true)]
    public void Power_is_off_on_and_toggle_as_the_step_sends_it(bool on, byte value, bool expected)
    {
        Assert.Equal(expected, (Off with { On = on }).Apply(new LightCommand(5, 0, 0, 0, value)).On);
    }

    [Fact]
    public void Off_is_the_off_mode_without_colors()
    {
        Assert.Equal(new PcLightsFrame("Off", null), (Off with { Effect = 6 }).Frame());
    }

    [Fact]
    public void A_color_is_direct_scaled_by_the_brightness()
    {
        var half = new PcLightState(true, new Rgb(255, 100, 0), 128);

        Assert.Equal(new PcLightsFrame("Direct", new Rgb(128, 50, 0)), half.Frame());
    }

    [Theory]
    [InlineData(6, "Breathing", 255, 0, 0)]
    [InlineData(7, "Spectrum Cycle", null, null, null)]
    [InlineData(8, "Direct", 255, 110, 20)]
    [InlineData(9, "Direct", 255, 120, 25)]
    public void Each_effect_has_the_mode_of_decision_5(byte effect, string mode, int? r, int? g, int? b)
    {
        var red = new PcLightState(true, new Rgb(255, 0, 0), 255, effect);

        Rgb? color = r is null ? null : new Rgb((byte)r, (byte)g!, (byte)b!);
        Assert.Equal(new PcLightsFrame(mode, color), red.Frame());
    }
}
