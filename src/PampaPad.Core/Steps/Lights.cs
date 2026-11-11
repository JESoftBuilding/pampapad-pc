using System.Globalization;
using System.Text.Json;

namespace PampaPad.Core;

public sealed record LightsParameters(string? Command, JsonElement? Value, string? Color);

/// <summary>
/// lights: manda un comando a la tira de luces por la consola (02 §5; 04 §7). Una línea por paso, y termina bien al escribirla: las luces
/// no acusan. Value es texto con power y un entero con los demás; se lee como JsonElement para que "50" no pase por 50 ni al revés
/// (plan de la 8b, decisión 9). brightness no prende la tira: lo hace luces-rgb y no se corrige acá. copyToPc es PcLights.Apply: el mismo
/// comando va a las luces de la PC, también sin consola, sin esperar y sin cambiar el resultado del paso (plan de la 1.6.0, decisión 2).
/// </summary>
public sealed class Lights(Func<string, CancellationToken, Task<Result>>? send, Action<LightCommand>? copyToPc = null) : StepType<LightsParameters>
{
    public const string TypeId = "lights";

    /// <summary>El tope de adjustBrightness, para los dos lados: viaja con signo en un byte (04 §7).</summary>
    public const int MaxAdjust = 127;

    // Los seis presets de luces-rgb, en su orden: violeta, ámbar, blanco, rojo, verde y azul (lib/core/Presets.h:88-97). Allá se agregan al
    // final y no se reordenan; si suman uno, este tope sube con ellos.
    // 0 a 5 son los colores y 6 a 9 los efectos de la placa de las luces (luces-rgb ffe4612 y 7cb0a20; plan de la 1.3.0, decisión 9).
    private const int LastPreset = 9;

    private const byte AdjustCmd = 3;

    private static readonly string[] Commands = ["power", "color", "brightness", "adjustBrightness", "preset"];

    public override string Id => TypeId;

    public override string Name => "Luces de la sala";

    public override string Group => "Cadenas";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("command", "Comando", FieldKind.Option, Options: Commands),
        new Field("value", "Valor", FieldKind.Text, Optional: true),
        new Field("color", "Color", FieldKind.Color, Optional: true),
    ];

    protected override IEnumerable<Problem> Check(LightsParameters p)
    {
        if (Build(p, out _) is { } problem)
        {
            yield return problem;
        }
    }

    protected override async Task<StepResult> RunAsync(LightsParameters p, Invocation invocation, CancellationToken ct)
    {
        if (Build(p, out LightCommand command) is { } problem)
        {
            return new StepResult(new Failure(ErrorCodes.ConfigInvalid, problem.Message));
        }

        CopyToPc(copyToPc, command);
        if (send is null)
        {
            return new StepResult(new Failure(ErrorCodes.ConsoleDisconnected, "No hay consola en este programa."));
        }

        Result sent = await send(ConsoleProtocol.LightLine(command), ct).ConfigureAwait(false);
        return new StepResult(sent.Failure);
    }

    /// <summary>Los 5 bytes de 04 §7, o el problema del campo que no calza. Check y RunAsync leen lo mismo.</summary>
    private static Problem? Build(LightsParameters p, out LightCommand command)
    {
        command = new LightCommand(0, 0, 0, 0, 0);
        if (p.Command == "color")
        {
            if (!TryColor(p.Color, out byte r, out byte g, out byte b))
            {
                return Invalid("color", "color va como #RRGGBB, por ejemplo #6D5DF6.");
            }

            command = new LightCommand(1, r, g, b, 0);
            return null;
        }

        if (p.Command == "power")
        {
            string? word = p.Value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
            byte? power = word switch { "off" => 0, "on" => 1, "toggle" => 2, _ => null };
            if (power is null)
            {
                return Invalid("value", "Con power, value va como texto: on, off o toggle.");
            }

            command = new LightCommand(5, 0, 0, 0, power.Value);
            return null;
        }

        (byte cmd, int min, int max, string rule) = p.Command switch
        {
            "brightness" => ((byte)2, 0, 255, "Con brightness, value es un entero de 0 a 255."),
            "adjustBrightness" => (AdjustCmd, -MaxAdjust, MaxAdjust, "Con adjustBrightness, value es un entero de -127 a 127, distinto de 0."),
            "preset" => ((byte)4, 0, LastPreset, $"Con preset, value es el número del preset, de 0 a {LastPreset}."),
            _ => ((byte)0, 0, 0, ""),
        };
        if (cmd == 0)
        {
            return Invalid("command", $"\"{p.Command}\" no es un comando de luces: van {string.Join(", ", Commands)}.");
        }

        // Sólo un número de JSON: "50" entre comillas es texto y no vale, como 1.5.
        if (p.Value is not { ValueKind: JsonValueKind.Number } number || !number.TryGetInt32(out int value)
            || value < min || value > max || (cmd == AdjustCmd && value == 0))
        {
            return Invalid("value", rule);
        }

        // adjustBrightness viaja con signo en un byte (04 §7): -1 es 0xFF.
        command = new LightCommand(cmd, 0, 0, 0, unchecked((byte)value));
        return null;
    }

    /// <summary>La línea de un adjustBrightness, de -127 a 127: la que manda el giro de la perilla en brightness (plan de la 1.3.0, decisión 7).</summary>
    public static string AdjustBrightnessLine(int value) => ConsoleProtocol.LightLine(AdjustBrightness(value));

    /// <summary>El adjustBrightness de -127 a 127 que arma el giro de la perilla: va a la consola y a las luces de la PC.</summary>
    public static LightCommand AdjustBrightness(int value) => new(AdjustCmd, 0, 0, 0, unchecked((byte)value));

    /// <summary>La copia a las luces de la PC no puede cambiar ni frenar lo de la sala: si tira, se ignora (decisión 2).</summary>
    public static void CopyToPc(Action<LightCommand>? copy, LightCommand command)
    {
        try
        {
            copy?.Invoke(command);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryColor(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        return text is { Length: 7 } && text[0] == '#'
            && TryHex(text.AsSpan(1, 2), out r) && TryHex(text.AsSpan(3, 2), out g) && TryHex(text.AsSpan(5, 2), out b);
    }

    private static bool TryHex(ReadOnlySpan<char> digits, out byte value) =>
        byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
