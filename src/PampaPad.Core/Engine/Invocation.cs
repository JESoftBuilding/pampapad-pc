namespace PampaPad.Core;

/// <summary>Quién disparó una ejecución. Es lo que queda en el registro como requestedBy (05 §9).</summary>
public enum Trigger
{
    Key,
    Hotkey,
    Window,
    Mcp,
    Tray,
    Internal,
}

/// <summary>Una ejecución de una acción: su id y de dónde vino. Llega a cada paso, al resultado y al registro (01 §3.1).</summary>
public sealed record Invocation(Guid RunId, Trigger RequestedBy, string? Source, string? Control, Gesture? Gesture, int? Value);
