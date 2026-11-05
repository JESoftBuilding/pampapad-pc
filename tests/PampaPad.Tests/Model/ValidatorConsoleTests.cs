using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

// El bloque console en el validador (plan de la 9b, decisión 15): un aviso que no existe y los valores fuera de rango de 02 §4.10
// son problemas que no frenan el guardado, porque el cable ya recorta.
public class ValidatorConsoleTests
{
    private static IReadOnlyList<Problem> Validate(string console) =>
        Validator.Validate(JsonSerializer.Deserialize<Config>(
            $$"""{ "version": 1, "pages": [ { "id": "a", "name": "A" } ], "console": {{console}} }""", Json.Options)!);

    [Fact]
    public void The_values_of_the_factory_and_the_known_alerts_give_no_problem()
    {
        Assert.Empty(Validate("""
            {
              "screen": { "brightness": 100, "dimAfterSec": 0, "dimBrightness": 0, "offAfterSec": 65535 },
              "sleepAfterSec": 0, "holdMs": 500, "strip": { "brightness": 0 }, "muteSoundWhileRecording": false,
              "alerts": {
                "marker_failed": { "light": false }, "mic_muted": { "sound": false }, "long_pause": {}, "still_recording": {},
                "obs_lost": {}, "recording_with_failure": {}, "no_pc": { "sound": true }
              }
            }
            """));
    }

    [Fact]
    public void An_alert_that_does_not_exist_is_a_warning_at_its_place()
    {
        Problem problem = Assert.Single(Validate("""{ "alerts": { "scene_changed": { "light": true } } }"""));

        Assert.Equal(("console.alerts.scene_changed", ProblemCodes.AlertUnknown, Severity.Warning), (problem.Where, problem.Code, problem.Severity));
    }

    [Theory]
    [InlineData("""{ "screen": { "brightness": 101 } }""", "console.screen.brightness")]
    [InlineData("""{ "screen": { "brightness": -1 } }""", "console.screen.brightness")]
    [InlineData("""{ "screen": { "dimBrightness": 150 } }""", "console.screen.dimBrightness")]
    [InlineData("""{ "screen": { "dimAfterSec": -5 } }""", "console.screen.dimAfterSec")]
    [InlineData("""{ "screen": { "offAfterSec": 70000 } }""", "console.screen.offAfterSec")]
    [InlineData("""{ "sleepAfterSec": -1 }""", "console.sleepAfterSec")]
    [InlineData("""{ "holdMs": 0 }""", "console.holdMs")]
    [InlineData("""{ "holdMs": 70000 }""", "console.holdMs")]
    [InlineData("""{ "strip": { "brightness": 255 } }""", "console.strip.brightness")]
    [InlineData("""{ "strip": { "brightness": "mucho" } }""", "console.strip.brightness")]
    [InlineData("""{ "strip": 3 }""", "console.strip")]
    public void A_value_out_of_its_range_is_a_warning_at_its_place(string console, string where)
    {
        Problem problem = Assert.Single(Validate(console));

        Assert.Equal((where, ProblemCodes.ConsoleOutOfRange, Severity.Warning), (problem.Where, problem.Code, problem.Severity));
    }
}
