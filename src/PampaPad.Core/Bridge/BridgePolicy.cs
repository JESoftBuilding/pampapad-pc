namespace PampaPad.Core;

/// <summary>Quién entra por el puente (01 §3.1). No es Trigger: Trigger es quién disparó una acción, y es lo que va al registro.</summary>
public enum Caller
{
    Window,
    Mcp,
    Tray,
    Internal,
}

/// <summary>La clase de un pedido (01 §5; la de cada uno, en 05 §11). WindowOnly quiere decir vedado al MCP.</summary>
public enum RequestKind
{
    Read,
    Write,
    Execute,
    WindowOnly,
}

/// <summary>Lo que la política necesita del momento: si el MCP tiene permiso y si hay una toma en curso.</summary>
public readonly record struct PolicyContext(bool McpAllowed, bool TakeInProgress)
{
    /// <summary>"Se graba, se está en pausa o se transmite" (03 §5): RecordingState distinto de stopped, o streaming.</summary>
    public static PolicyContext From(AppState state, bool mcpAllowed)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new PolicyContext(mcpAllowed, state.Obs.TakeInProgress || state.Obs.Streaming);
    }
}

/// <summary>Las puertas de los pedidos de esta etapa (05 §11). La puerta es la lista de Caller que pueden pedirlo; internal puede siempre.</summary>
public static class Doors
{
    public static IReadOnlySet<Caller> Window { get; } = new HashSet<Caller> { Caller.Window };

    public static IReadOnlySet<Caller> WindowAndMcp { get; } = new HashSet<Caller> { Caller.Window, Caller.Mcp };

    public static IReadOnlySet<Caller> WindowAndTray { get; } = new HashSet<Caller> { Caller.Window, Caller.Tray };

    /// <summary>Los pedidos que la ventana no usa (plan de la etapa MCP).</summary>
    public static IReadOnlySet<Caller> Mcp { get; } = new HashSet<Caller> { Caller.Mcp };
}

/// <summary>
/// Decide si un pedido pasa, antes de despacharlo (01 §5). Función pura: la tabla de 01 §5 menos su última fila, la huella vieja,
/// que la decide Library.SaveAsync. Devuelve null si pasa.
/// </summary>
public static class BridgePolicy
{
    public const string McpOff = "El MCP está apagado en Ajustes";

    // Los pedidos que no corren con una toma en curso, vengan de quien vengan: guardar cambiaría las teclas en plena toma (04 §3, regla de
    // grabación: las doce teclas no cambian de función) y probar corre pasos de verdad (action.test y scripts.test).
    // scripts.write, scripts.import y scripts.delete se suman en la etapa 8 (decisión 37: con una toma no se guarda).
    private static readonly HashSet<string> NotWhileTake =
        [RequestNames.ConfigSave, RequestNames.ActionTest, RequestNames.ScriptsTest, RequestNames.ScriptsWrite, RequestNames.ScriptsImport, RequestNames.ScriptsDelete];

    public static Failure? Check(string request, RequestKind kind, IReadOnlySet<Caller> callers, Caller caller, PolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(callers);

        // El interruptor va primero: con el permiso apagado, Claude lee siempre el mismo motivo (pregunta abierta 57, escrito (a)).
        if (caller == Caller.Mcp && !context.McpAllowed)
        {
            return new Failure(ErrorCodes.NoPermission, McpOff);
        }

        if (kind == RequestKind.WindowOnly && caller == Caller.Mcp)
        {
            return new Failure(ErrorCodes.NoPermission, $"{request} no se puede pedir por el MCP.");
        }

        if (caller != Caller.Internal && !callers.Contains(caller))
        {
            return new Failure(ErrorCodes.NoPermission, $"{request} no se puede pedir desde {Name(caller)}.");
        }

        if (caller == Caller.Mcp && (kind is RequestKind.Write or RequestKind.Execute) && context.TakeInProgress)
        {
            return ReadOnly(request);
        }

        if (NotWhileTake.Contains(request) && context.TakeInProgress)
        {
            return ReadOnly(request);
        }

        return null;
    }

    /// <summary>Quién queda en el registro como requestedBy cuando un pedido dispara algo (05 §9).</summary>
    public static Trigger AsTrigger(Caller caller) => caller switch
    {
        Caller.Window => Trigger.Window,
        Caller.Mcp => Trigger.Mcp,
        Caller.Tray => Trigger.Tray,
        _ => Trigger.Internal,
    };

    private static Failure ReadOnly(string request) =>
        new(ErrorCodes.ReadOnlyWhileRecording, $"{request} no se hace mientras se graba, se está en pausa o se transmite.");

    private static string Name(Caller caller) => caller switch
    {
        Caller.Window => "la ventana",
        Caller.Mcp => "el MCP",
        Caller.Tray => "la bandeja",
        _ => "el programa",
    };
}
