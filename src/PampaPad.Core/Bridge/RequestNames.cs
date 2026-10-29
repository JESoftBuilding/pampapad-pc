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
    public const string HotkeysSuspend = "hotkeys.suspend";
    public const string ActionTest = "action.test";
    public const string OptionsList = "options.list";
    public const string WindowDirty = "window.dirty";
    public const string ScriptsList = "scripts.list";
    public const string ScriptsRead = "scripts.read";
    public const string ScriptsWrite = "scripts.write";
    public const string ScriptsImport = "scripts.import";
    public const string ScriptsDelete = "scripts.delete";
    public const string ScriptsReveal = "scripts.reveal";
    public const string LibraryReveal = "library.reveal";
    public const string ScriptsTest = "scripts.test";
    public const string SettingsSave = "settings.save";
    public const string UpdateCheck = "update.check";
    public const string UpdateSkip = "update.skip";
    public const string ActionRun = "action.run";
    public const string PreflightRun = "preflight.run";
    public const string ActionSave = "action.save";
    public const string KeyAssign = "key.assign";

    public static IReadOnlyList<string> All { get; } =
        [AppSnapshot, LogRead, ActionCancel, WindowReady, ConfigSave, LibraryReload, HotkeysSuspend, ActionTest, OptionsList, WindowDirty, ScriptsList, ScriptsRead, ScriptsWrite, ScriptsImport,
        ScriptsDelete, ScriptsReveal, LibraryReveal, ScriptsTest, SettingsSave, UpdateCheck,
        UpdateSkip, ActionRun, PreflightRun, ActionSave, KeyAssign];

    /// <summary>Los que atiende la cáscara (AppRequests, 01 §3.2): el núcleo los nombra pero no los registra.</summary>
    public static IReadOnlyList<string> Shell { get; } = [WindowReady, WindowDirty];
}

/// <summary>Los eventos del puente (05, Términos). script.output, la salida en vivo de una prueba, entra con la pantalla Scripts (etapa 8).</summary>
public static class EventNames
{
    public const string State = "state";
    public const string Log = "log";
    public const string ActionFinished = "action.finished";
    public const string ConfigChanged = "config.changed";
    public const string ScriptOutput = "script.output";

    public static IReadOnlyList<string> All { get; } = [State, Log, ActionFinished, ConfigChanged, ScriptOutput];
}
