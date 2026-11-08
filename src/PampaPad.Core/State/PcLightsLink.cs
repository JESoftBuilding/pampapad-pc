namespace PampaPad.Core;

/// <summary>Cómo están las luces de la PC (plan de la 1.6.0, decisión 8).</summary>
public enum PcLightsLinkState
{
    /// <summary>No hay zona elegida en local.json: PampaPad no toca OpenRGB.</summary>
    Off,

    /// <summary>OpenRGB respondió y tiene la zona elegida.</summary>
    Connected,

    /// <summary>OpenRGB no responde, o no tiene la zona: se reintenta cada 5 s.</summary>
    Unreachable,
}

/// <summary>El estado de las luces de la PC y la zona que se maneja, como "dispositivo · zona". Sin zona, Zone es null.</summary>
public sealed record PcLightsLink(PcLightsLinkState State, string? Zone)
{
    public static PcLightsLink Off { get; } = new(PcLightsLinkState.Off, null);
}
