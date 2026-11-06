namespace PampaPad.Core;

// El ítem de la consola en la bandeja, aparte por el tope de 300 líneas (01 §1). Con el receptor y "Automático" en la 1.4.0.
public sealed partial record TraySummary
{
    /// <summary>Dice qué hace el clic y cómo está el enlace: hasta la pantalla Estado, es la única señal de si el puerto abrió.</summary>
    public static string? ConsoleText(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string port = PortLabel(state.Console);
        return state.Console.State switch
        {
            ConsoleLinkState.Off => null,
            ConsoleLinkState.Released => $"Tomar la consola ({port})",
            ConsoleLinkState.Waiting => $"Soltar la consola ({port}, sin conexión)",
            _ => $"Soltar la consola ({port} · {LinkText(state.Console)})",
        };
    }

    /// <summary>"Automático · COM5", "Automático" mientras no encuentra placa, o el puerto a mano (portLabel en status.ts).</summary>
    private static string PortLabel(ConsoleLink link) => link.Auto
        ? link.Port is { } port ? $"Automático · {port}" : "Automático"
        : link.Port ?? "";

    // Los textos de la decisión 7 del plan de la 1.4.0; por cable, el saludo como antes. Igual que consoleText en status.ts.
    private static string LinkText(ConsoleLink link) => link.Receiver switch
    {
        _ when link.WithoutSignal() => "Receptor conectado · consola sin señal",
        not null => $"Consola conectada por radio · {Greeting(link)}",
        null => Greeting(link),
    };

    // El mismo saludo que la fila de Estado (greetingText en status.ts; plan de la 9a, decisión 13).
    private static string Greeting(ConsoleLink link) => link.Firmware is { } firmware
        ? $"firmware {firmware} · {(link.SettingsInSync == true ? "ajustes al día" : "ajustes sin confirmar")}"
        : "sin saludo todavía";
}
