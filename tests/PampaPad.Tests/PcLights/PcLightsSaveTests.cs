using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>pcLights en local.json (plan de la 1.6.0, decisiones 6 y 7): se conserva, y guardar last no pisa lo demás.</summary>
public class PcLightsSaveTests
{
    private const string Local = $$"""
        {
          "version": 1,
          "consolePort": "COM7",
          "futuro": { "a": 1 },
          "pcLights": { "device": "{{FakeOpenRgb.Board}}", "zone": "{{FakeOpenRgb.Header}}", "otro": true }
        }
        """;

    [Fact]
    public async Task Local_json_keeps_pcLights_and_what_it_does_not_know_when_something_else_is_saved()
    {
        string local = Local.Replace("\"otro\": true", "\"otro\": true, \"last\": { \"on\": true, \"color\": { \"r\": 1, \"g\": 2, \"b\": 3 }, \"brightness\": 9, \"effect\": 7 }", StringComparison.Ordinal);
        await using var rig = new PcLightsRig(local);

        await rig.Settings.SaveAsync(data => data with { SkippedVersion = "1.6.1" }, CancellationToken.None);

        JsonNode file = Read(rig);
        Assert.Equal("COM7", (string?)file["consolePort"]);
        Assert.Equal(1, (int?)file["futuro"]!["a"]);
        Assert.True((bool?)file["pcLights"]!["otro"]);
        Assert.Equal(new PcLightState(true, new Rgb(1, 2, 3), 9, 7), rig.Settings.Current.PcLights!.Last);
        Assert.Equal(7, (int?)file["pcLights"]!["last"]!["effect"]);
    }

    [Fact]
    public async Task Last_is_saved_at_most_every_2_s_only_if_it_changed_and_keeps_device_zone_and_the_rest()
    {
        await using var rig = new PcLightsRig(Local).Start();
        rig.Lights.Apply(new LightCommand(1, 255, 0, 0, 0));

        await Eventually.AdvancingAsync(rig.Clock, PcLights.SaveEvery, () => rig.Settings.Current.PcLights?.Last is not null, "guardado");
        DateTime written = File.GetLastWriteTimeUtc(rig.Path);
        await Eventually.StepsAsync(rig.Clock, PcLights.SaveEvery, 3);

        Assert.Equal(written, File.GetLastWriteTimeUtc(rig.Path));
        JsonNode file = Read(rig);
        Assert.Equal(FakeOpenRgb.Board, (string?)file["pcLights"]!["device"]);
        Assert.Equal(FakeOpenRgb.Header, (string?)file["pcLights"]!["zone"]);
        Assert.True((bool?)file["pcLights"]!["otro"]);
        Assert.Equal("COM7", (string?)file["consolePort"]);
        Assert.Equal(1, (int?)file["futuro"]!["a"]);
        Assert.Equal(new PcLightState(true, new Rgb(255, 0, 0), 255), rig.Settings.Current.PcLights!.Last);
    }

    [Fact]
    public async Task Saving_at_exit_keeps_a_zone_changed_meanwhile_in_settings()
    {
        await using var rig = new PcLightsRig(Local);
        await rig.Settings.SaveAsync(data => data with { PcLights = data.PcLights! with { Zone = "Back I/O" } }, CancellationToken.None);
        rig.Lights.Apply(new LightCommand(4, 0, 0, 0, 1));

        await rig.Lights.SaveAsync();

        PcLightsSettings saved = rig.Settings.Current.PcLights!;
        Assert.Equal((FakeOpenRgb.Board, "Back I/O"), (saved.Device, saved.Zone));
        Assert.Equal(new PcLightState(true, new Rgb(245, 166, 35), 255), saved.Last);
        Assert.Equal("Back I/O", (string?)Read(rig)["pcLights"]!["zone"]);
    }

    [Fact]
    public async Task Without_changes_nothing_is_saved_at_exit()
    {
        await using var rig = new PcLightsRig(Local);

        await rig.Lights.SaveAsync();

        Assert.Equal(Local, File.ReadAllText(rig.Path));
    }

    private static JsonNode Read(PcLightsRig rig) => JsonNode.Parse(File.ReadAllText(rig.Path))!;
}
