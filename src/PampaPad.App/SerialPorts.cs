using System.IO.Ports;

namespace PampaPad.App;

/// <summary>Abre el puerto serie de la consola y le entrega el flujo al núcleo. Sin reglas (01 §3.2).</summary>
internal static class SerialPorts
{
    // El cable va a 115200 baudios, 8N1 (04 §9; monitor_speed del firmware).
    private const int BaudRate = 115200;

    // Depende de E0-5. Con las dos líneas sin activar, el circuito de reinicio automático de la placa no la reinicia.
    private const bool Dtr = false;
    private const bool Rts = false;

    /// <summary>La función que SerialConsole llama en cada intento. Si el puerto no abre, tira, y SerialConsole reintenta.</summary>
    public static Func<CancellationToken, Task<Stream>> Opener(string portName) =>
        ct => Task.Run(() => Open(portName), ct);

    private static Stream Open(string portName)
    {
        var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = Dtr,
            RtsEnable = Rts,
        };
        try
        {
            port.Open();

            // Cerrar este flujo cierra el puerto: es lo mismo que hace SerialPort.Close por dentro.
            return port.BaseStream;
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }
}
