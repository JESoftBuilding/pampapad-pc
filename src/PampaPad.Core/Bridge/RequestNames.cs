namespace PampaPad.Core;

/// <summary>
/// Los nombres de los pedidos del puente (05 §11). Entran de a uno, con la pantalla que los usa (01 §6): la prueba de humo exige un
/// manejador por cada constante, y Contract.cs una muestra por cada uno que pide la ventana.
/// </summary>
public static class RequestNames
{
    public const string AppSnapshot = "app.snapshot";
    public const string LogRead = "log.read";
    public const string ActionCancel = "action.cancel";
    public const string WindowReady = "window.ready";
    public const string ConfigSave = "config.save";
    public const string LibraryReload = "library.reload";

    public static IReadOnlyList<string> All { get; } = [AppSnapshot, LogRead, ActionCancel, WindowReady, ConfigSave, LibraryReload];

    /// <summary>Los que atiende la cáscara (AppRequests, 01 §3.2): el núcleo los nombra pero no los registra.</summary>
    public static IReadOnlyList<string> Shell { get; } = [WindowReady];
}

/// <summary>Los eventos del puente (05, Términos). script.output entra con los scripts, en la etapa 5.</summary>
public static class EventNames
{
    public const string State = "state";
    public const string Log = "log";
    public const string ActionFinished = "action.finished";
    public const string ConfigChanged = "config.changed";

    public static IReadOnlyList<string> All { get; } = [State, Log, ActionFinished, ConfigChanged];
}
