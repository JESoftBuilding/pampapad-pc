using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// El doble de IOpenRgb: la placa de esta PC tal como la leyó RGB-1 (2026-11-07), con "Back I/O" en los LEDs 0 a 4 y "RGB Header" en el
/// 5 y el 6, y los diez modos en su orden. Anota cada llamada; Connect falla las veces que diga FailConnects. Ninguna prueba toca OpenRGB.
/// </summary>
internal sealed class FakeOpenRgb : IOpenRgb
{
    public const string Board = "ASUS ROG STRIX B450-F GAMING II";
    public const string Header = "RGB Header";

    public static readonly string[] Modes =
        ["Direct", "Off", "Static", "Breathing", "Flashing", "Spectrum Cycle", "Rainbow", "Chase Fade", "Chase", "Random Flicker"];

    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];
    private readonly List<(string Mode, Rgb[]? Leds)> _writes = [];
    private string? _pendingMode;

    // El índice 3, y no el 0, para que una prueba vea que se usa el de OpenRGB y no la posición en la lista.
    public IReadOnlyList<OpenRgbDevice> Board3 { get; set; } =
    [
        new(0, "Corsair", [new OpenRgbZone("Fans", 4)], ["Direct"]),
        new(3, Board, [new OpenRgbZone("Back I/O", 5), new OpenRgbZone(Header, 2)], Modes),
    ];

    /// <summary>Cuántos Connect fallan antes del primero que anda.</summary>
    public int FailConnects { get; set; }

    /// <summary>Si las escrituras fallan, como cuando OpenRGB se cierra con la conexión abierta.</summary>
    public bool FailWrites { get; set; }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public int Connects => Calls.Count(call => call == "connect");

    /// <summary>Lo que se escribió: el modo puesto y, si después vinieron colores, los siete LEDs.</summary>
    public IReadOnlyList<(string Mode, Rgb[]? Leds)> Writes
    {
        get
        {
            lock (_gate)
            {
                return [.. _writes];
            }
        }
    }

    public void Connect()
    {
        lock (_gate)
        {
            _calls.Add("connect");
            if (FailConnects > 0)
            {
                FailConnects--;
                throw new IOException("No se pudo conectar a 127.0.0.1:6742");
            }
        }
    }

    public IReadOnlyList<OpenRgbDevice> Devices()
    {
        lock (_gate)
        {
            _calls.Add("devices");
            return Board3;
        }
    }

    public void SetMode(int device, int mode)
    {
        lock (_gate)
        {
            _calls.Add($"mode {device} {Modes[mode]}");
            Throw();
            _pendingMode = Modes[mode];
            _writes.Add((_pendingMode, null));
        }
    }

    public void SetLeds(int device, IReadOnlyList<Rgb> colors)
    {
        lock (_gate)
        {
            _calls.Add($"leds {device}");
            Throw();
            _writes[^1] = (_pendingMode!, [.. colors]);
        }
    }

    public void Dispose()
    {
    }

    private void Throw()
    {
        if (FailWrites)
        {
            throw new IOException("Se cerró la conexión");
        }
    }
}
