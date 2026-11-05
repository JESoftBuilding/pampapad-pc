using System.Text.Json;

namespace PampaPad.Core;

// El bloque console (02 §4.10; plan de la 9b, decisión 15). Todo sale como warning: el cable recorta los valores y la consola ignora
// los avisos que no conoce, así que nada de esto rompe una tecla. Va aparte por el tope de 300 líneas (01 §1).
public static partial class Validator
{
    // Los segundos y holdMs viajan en una palabra de 16 bits en Settings 0x44 (ConsoleSettingsWire.Word).
    private const int WordMax = ushort.MaxValue;

    private static void CheckConsole(Config config, List<Problem> found)
    {
        if (config.Console is not { } console)
        {
            return;
        }

        Range("console.screen.brightness", console.Screen?.Brightness, 0, 100, found);
        Range("console.screen.dimBrightness", console.Screen?.DimBrightness, 0, 100, found);
        Range("console.screen.dimAfterSec", console.Screen?.DimAfterSec, 0, WordMax, found);
        Range("console.screen.offAfterSec", console.Screen?.OffAfterSec, 0, WordMax, found);
        Range("console.sleepAfterSec", console.SleepAfterSec, 0, WordMax, found);

        // 0 o menos no es "nunca": Gestures.HoldFrom lo toma como el segundo de fábrica.
        Range("console.holdMs", console.HoldMs, 1, WordMax, found);
        CheckStrip(console, found);

        foreach (string id in (console.Alerts?.Keys ?? []).Where(id => id != AlertTexts.NoPc && !AlertTexts.Priority.Contains(id)))
        {
            found.Add(Warning($"console.alerts.{id}", ProblemCodes.AlertUnknown, $"console.alerts nombra el aviso \"{id}\", que no existe: van los graves y no_pc."));
        }
    }

    // strip no tiene record propio y cae en Extra (ConsoleSettings.cs): se mira el JSON tal como está.
    private static void CheckStrip(ConsoleSettings console, List<Problem> found)
    {
        if (!console.Extra.TryGetValue("strip", out JsonElement strip))
        {
            return;
        }

        if (strip.ValueKind != JsonValueKind.Object)
        {
            found.Add(Warning("console.strip", ProblemCodes.ConsoleOutOfRange, "console.strip va con brightness adentro, de 0 a 100."));
            return;
        }

        if (!strip.TryGetProperty("brightness", out JsonElement brightness))
        {
            return;
        }

        if (brightness.ValueKind != JsonValueKind.Number || !brightness.TryGetInt32(out int value))
        {
            found.Add(Warning("console.strip.brightness", ProblemCodes.ConsoleOutOfRange, "console.strip.brightness va como un número entero de 0 a 100."));
            return;
        }

        Range("console.strip.brightness", value, 0, 100, found);
    }

    private static void Range(string where, int? value, int min, int max, List<Problem> found)
    {
        if (value is { } number && (number < min || number > max))
        {
            found.Add(Warning(where, ProblemCodes.ConsoleOutOfRange, $"{where} vale {number} y va de {min} a {max}: se manda recortado."));
        }
    }

    private static Problem Warning(string where, string code, string message) => new(where, code, message, Severity.Warning);
}
