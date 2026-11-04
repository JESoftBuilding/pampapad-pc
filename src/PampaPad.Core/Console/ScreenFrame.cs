namespace PampaPad.Core;

/// <summary>El byte 1 de ScreenState, el estado de grabación que dibuja la banda (WireMessages.h:14).</summary>
public enum ConsoleRecording : byte
{
    NoObs = 0,
    Stopped,
    Starting,
    Recording,
    Paused,
    Stopping,
    Switching,
}

/// <summary>El byte 2 de ScreenState (WireMessages.h:17-22).</summary>
[Flags]
public enum ScreenFlags : byte
{
    None = 0,
    MicMuted = 1 << 0,
    CanvasVertical = 1 << 1,
    PreflightFailed = 1 << 2,
    SceneWithoutKey = 1 << 3,
    SpanOpen = 1 << 4,
    PreflightRan = 1 << 5,
}

/// <summary>El byte 17 de ScreenState (WireMessages.h:24-26). La consola no lee AlertLight: la tira la pinta la PC.</summary>
[Flags]
public enum ScreenFlags2 : byte
{
    None = 0,
    KeepAwake = 1 << 0,
    AlertLight = 1 << 1,
    AlertSound = 1 << 2,
}

/// <summary>
/// Lo que lleva un ScreenState 0x40, campo por campo (04 §7). Los segundos que no entran quedan en el tope; el byte 10 va en 0.
/// SpanKey es 1 a 12 o 0; Canvas, la cobertura en % con el bit 7 "no llena"; las máscaras, un bit por tecla desde la 1.
/// </summary>
public sealed record ScreenFrame(
    int Page,
    ConsoleRecording Recording,
    ScreenFlags Flags = ScreenFlags.None,
    int RecordedSec = 0,
    int PausedSec = 0,
    int SpanKey = 0,
    int Canvas = 100,
    int ActiveMask = 0,
    int OffMask = 0,
    int Alert = 0,
    int LabelsVersion = 1,
    ScreenFlags2 Flags2 = ScreenFlags2.None,
    int SpanSec = 0);

/// <summary>El rol de una tecla, bits 0 a 3 de su estilo (WireMessages.h:76). No se renumera: el desconocido se dibuja Normal.</summary>
public enum KeyRole : byte
{
    None = 0,
    Normal,
    Scene,
    Marker,
    Danger,
    Disabled,
    Caution,
}

/// <summary>Lo que muestra una tecla: el rol, dos líneas de hasta 6 letras, la marca de mantener y la letra grande de una línea.</summary>
public sealed record KeyFace(KeyRole Role, string Line1, string Line2, bool Hold = false, bool Big = false)
{
    /// <summary>Una celda sin tecla: rol 0 y sin letras (plan de la 9a, "Lo que puede morder").</summary>
    public static KeyFace Empty { get; } = new(KeyRole.None, "", "");
}

/// <summary>El efecto de un LED de la tira (WireMessages.h:166). Otro valor la consola lo toma como fijo.</summary>
public enum LedEffect : byte
{
    Steady = 0,
    Blink1Hz = 1,
    Blink4Hz = 2,
}

/// <summary>Un LED de la tira en colores de LED, sin corrección: el rojo es 255, 0, 0 (04 §7).</summary>
public sealed record LedColor(byte R, byte G, byte B, LedEffect Effect = LedEffect.Steady)
{
    public static LedColor Off { get; } = new(0, 0, 0);
}
