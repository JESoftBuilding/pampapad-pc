using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Cómo está el enlace con la consola por cable.</summary>
public enum ConsoleLinkState
{
    /// <summary>No hay puerto en local.json: la consola no se usa.</summary>
    Off,

    /// <summary>Hay puerto y no está abierto: se reintenta.</summary>
    Waiting,

    /// <summary>El puerto abrió. Mientras la consola no mande su saludo, no quiere decir más que eso (05 §4.5).</summary>
    Connected,

    /// <summary>El programa soltó el puerto a pedido, para cargar firmware o abrir el monitor serie.</summary>
    Released,
}

/// <summary>
/// Lo que el programa sabe de la consola por cable: el enlace, el puerto que dice local.json y lo que dijo el último Hello de este
/// enlace (plan de la 9a, decisión 13). Sin Hello, todo lo del saludo queda en null: la 0.1.4 y la 0.1.5 lo mandan, el banco no.
/// </summary>
public sealed record ConsoleLink(ConsoleLinkState State, string? Port)
{
    public static ConsoleLink None { get; } = new(ConsoleLinkState.Off, null);

    /// <summary>La versión del firmware, por ejemplo "0.1.5".</summary>
    public string? Firmware { get; init; }

    /// <summary>El byte de capacidades del Hello: perilla, tecla del módulo, luz regulable, etc. (ConsoleWire.h:137-145).</summary>
    public int? Capabilities { get; init; }

    /// <summary>0 activa, 1 atenuada, 2 pantalla apagada.</summary>
    public int? Energy { get; init; }

    /// <summary>La huella de los últimos ajustes que aceptó la consola; 0 si nunca recibió.</summary>
    public int? SettingsFingerprint { get; init; }

    /// <summary>Si esa huella es la de los ajustes que manda la PC. Lo escribe el emisor, ConsoleFeed.</summary>
    public bool? SettingsInSync { get; init; }

    /// <summary>Lo que dijo el último ReceiverHello de este enlace; null por cable, sin receptor (plan de la 1.4.0, decisión 2).</summary>
    public ReceiverLink? Receiver { get; init; }

    /// <summary>
    /// "Consola sin señal": el puerto abierto es el receptor y no escucha a la consola. No es un estado del enlace sino una cuenta
    /// (decisión 3); es un método para que no viaje en el estado del puente, donde ya va Receiver.
    /// </summary>
    public bool WithoutSignal() => State == ConsoleLinkState.Connected && Receiver is { HearsConsole: false };

    /// <summary>
    /// "Automático" en local.json (plan de la 1.4.0, decisión 6). Port es entonces el puerto que se eligió en el último intento, o null
    /// si no había ninguna placa conocida: nunca "auto". En falso no viaja, como lo que falta del saludo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Auto { get; init; }

    /// <summary>El mismo enlace en otro estado y sin nada del saludo: un enlace nuevo tiene que volver a saludar.</summary>
    public ConsoleLink Moved(ConsoleLinkState state) => new(state, Port) { Auto = Auto };
}

/// <summary>El receptor por radio en el enlace: la versión de su firmware y si escucha a la consola (04 §7, ReceiverHello).</summary>
public sealed record ReceiverLink(string Firmware, bool HearsConsole);
