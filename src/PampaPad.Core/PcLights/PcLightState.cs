namespace PampaPad.Core;

/// <summary>Cómo tiene que verse la placa: el modo por nombre y el color de la zona, o null si el modo no lleva colores.</summary>
public sealed record PcLightsFrame(string Mode, Rgb? ZoneColor);

/// <summary>
/// Las luces de la PC: encendido, color, brillo y efecto, con el mismo estado inicial que la tira de la sala (luces-rgb,
/// LightController.cpp:8). Es lo que se guarda en local.json, en pcLights.last (plan de la 1.6.0, decisión 6). Effect es el preset de
/// efecto, de 6 a 9, o null.
/// </summary>
public sealed record PcLightState(bool On, Rgb Color, byte Brightness, byte? Effect = null)
{
    // Los presets de luces-rgb (lib/core/Presets.h:31-41): 0 a 5 son colores; 6 a 9, efectos, sin color propio en el modelo.
    private static readonly Rgb?[] Presets =
        [new(109, 93, 246), new(245, 166, 35), new(255, 255, 255), new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), null, null, null, null];

    private const byte Breathe = 6;
    private const byte Cycle = 7;
    private const byte Candle = 8;
    private const byte Sunrise = 9;

    public static PcLightState Initial { get; } = new(false, Presets[0]!.Value, 255);

    /// <summary>
    /// Lo mismo que LightController::apply (LightController.cpp:17-61): color y preset la prenden, un preset de color saca el efecto,
    /// brillo y ajuste no la prenden y el ajuste queda entre 0 y 255. Un comando que allá no vale deja el estado como estaba.
    /// </summary>
    public PcLightState Apply(LightCommand command) => command.Cmd switch
    {
        1 => this with { Color = new Rgb(command.R, command.G, command.B), Effect = null, On = true },
        2 => this with { Brightness = command.Value },
        3 => this with { Brightness = (byte)Math.Clamp(Brightness + unchecked((sbyte)command.Value), 0, 255) },
        4 when command.Value < Presets.Length => Presets[command.Value] is { } color
            ? this with { Color = color, Effect = null, On = true }
            : this with { Effect = command.Value, On = true },
        5 => command.Value switch
        {
            0 => this with { On = false },
            1 => this with { On = true },
            2 => this with { On = !On },
            _ => this,
        },
        _ => this,
    };

    /// <summary>
    /// Cómo se ve en la placa (decisión 5). Ningún modo del chip tiene brillo propio, así que el brillo escala el color. La vela y el
    /// amanecer son colores fijos: Random Flicker no deja elegir el color y la subida del amanecer queda sólo en la sala (RGB-1).
    /// </summary>
    public PcLightsFrame Frame() => (On, Effect) switch
    {
        (false, _) => new PcLightsFrame("Off", null),
        (_, Breathe) => new PcLightsFrame("Breathing", Scaled(Color)),
        (_, Cycle) => new PcLightsFrame("Spectrum Cycle", null),
        (_, Candle) => new PcLightsFrame("Direct", Scaled(new Rgb(255, 110, 20))),
        (_, Sunrise) => new PcLightsFrame("Direct", Scaled(new Rgb(255, 120, 25))),
        _ => new PcLightsFrame("Direct", Scaled(Color)),
    };

    private Rgb Scaled(Rgb color) => new(Scale(color.R), Scale(color.G), Scale(color.B));

    private byte Scale(byte channel) => (byte)((channel * Brightness + 127) / 255);
}
