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

/// <summary>Lo que el programa sabe de la consola por cable: el enlace y el puerto que dice local.json.</summary>
public sealed record ConsoleLink(ConsoleLinkState State, string? Port)
{
    public static ConsoleLink None { get; } = new(ConsoleLinkState.Off, null);
}
