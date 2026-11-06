namespace PampaPad.Core;

/// <summary>
/// "Automático" en el puerto de la consola (05 §4.5; plan de la 1.4.0, decisión 6): elige entre las placas conocidas, nunca entre todos
/// los puertos, porque abrir uno puede reiniciar la placa del otro lado. La lista va fija en el programa (pregunta 2, la (a)).
/// </summary>
public static class ConsolePorts
{
    /// <summary>El valor de consolePort en local.json que pide "Automático".</summary>
    public const string Auto = "auto";

    /// <summary>El receptor: el USB nativo de la ESP32-S3-Zero. Es el de cualquier ESP32-S3 así enchufada (plan de la 1.4.0, Incierto).</summary>
    public const string ReceiverUsbId = "303A:1001";

    /// <summary>La consola por cable: la CH343 de la DevKitC.</summary>
    public const string CableUsbId = "1A86:55D3";

    /// <summary>Lo que dice el registro cuando no hay ninguna de las dos. SerialConsole lo anota una vez mientras no cambie.</summary>
    public const string NoneFound = "Automático: no hay un receptor ni una consola enchufados.";

    public static bool IsAuto(string? consolePort) => string.Equals(consolePort, Auto, StringComparison.Ordinal);

    /// <summary>
    /// El puerto a abrir: primero el receptor y después la consola por cable (pregunta 1, la (a)); null si no hay ninguno. Con dos del
    /// mismo tipo, el primero por nombre (COM5 antes que COM10), para que el elegido no dependa del orden en que Windows los lista.
    /// </summary>
    public static ConsolePortView? Choose(IReadOnlyList<ConsolePortView> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return First(ports, ReceiverUsbId) ?? First(ports, CableUsbId);
    }

    /// <summary>
    /// El abridor de "Automático" para SerialConsole: en cada intento lista, elige y abre. Deja en el enlace el puerto elegido, o ninguno,
    /// para que la ventana diga "Automático · COM5" o que espera. Sólo corre cuando hay que abrir: con el enlace abierto no cambia de puerto.
    /// </summary>
    public static Func<CancellationToken, Task<Stream>> Opener(
        Func<IReadOnlyList<ConsolePortView>> list,
        Func<string, CancellationToken, Task<Stream>> open,
        StateStore state)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(state);
        return ct =>
        {
            string? port = Choose(list())?.Port;
            state.Update(s => s.Console.Port == port ? s : s with { Console = s.Console with { Port = port } });
            return port is null ? throw new IOException(NoneFound) : open(port, ct);
        };
    }

    private static ConsolePortView? First(IReadOnlyList<ConsolePortView> ports, string usbId) => ports
        .Where(p => string.Equals(p.UsbId, usbId, StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p.Port.Length)
        .ThenBy(p => p.Port, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
}
