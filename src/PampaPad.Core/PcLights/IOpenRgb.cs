namespace PampaPad.Core;

/// <summary>Un color de 8 bits por canal, como lo llevan LightCommand y OpenRGB.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Black { get; } = new(0, 0, 0);
}

/// <summary>Una zona de un dispositivo de OpenRGB: su nombre y cuántos LEDs tiene. Los LEDs del dispositivo van zona tras zona, en orden.</summary>
public sealed record OpenRgbZone(string Name, int LedCount);

/// <summary>Un dispositivo de OpenRGB. Index es el de esta conexión: puede cambiar entre sesiones de OpenRGB, por eso se busca por nombre.</summary>
public sealed record OpenRgbDevice(int Index, string Name, IReadOnlyList<OpenRgbZone> Zones, IReadOnlyList<string> Modes);

/// <summary>
/// Lo poco que PampaPad le pide al servidor SDK de OpenRGB (plan de la 1.6.0, decisión 3). Lo implementa la cáscara con OpenRGB.NET, sin
/// reglas: las tiene PcLights. Cada método tira si OpenRGB no responde, y PcLights lo llama de a uno por vez. No hay "guardar en el
/// dispositivo": nada se graba en la memoria de la placa (decisión 6).
/// </summary>
public interface IOpenRgb : IDisposable
{
    /// <summary>Conecta, o vuelve a conectar soltando la conexión anterior, con el nombre "PampaPad".</summary>
    void Connect();

    /// <summary>Los dispositivos con sus zonas y sus modos, en el orden de OpenRGB.</summary>
    IReadOnlyList<OpenRgbDevice> Devices();

    /// <summary>Pone el modo del dispositivo entero: el modo no es de la zona.</summary>
    void SetMode(int device, int mode);

    /// <summary>Los colores de todos los LEDs del dispositivo, en orden.</summary>
    void SetLeds(int device, IReadOnlyList<Rgb> colors);
}
