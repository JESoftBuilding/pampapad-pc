namespace PampaPad.Core;

/// <summary>El arranque con Windows como lo muestra la tarjeta Programa: no disponible en una copia sin instalar.</summary>
public enum StartupStatus
{
    Unavailable,
    On,
    Off,
}

/// <summary>
/// Cómo se conecta con OBS (03 §2): la dirección, 127.0.0.1 con el puerto del archivo de OBS, y de dónde sale la clave. La clave nunca
/// viaja: el programa la lee del archivo de OBS en cada intento.
/// </summary>
public sealed record ObsSettingsView(string Address, string Password = ObsSettingsView.SavedInObs)
{
    public const string SavedInObs = "la guardada en OBS";
}

/// <summary>
/// Los ajustes de esta PC que la página muestra. HotkeysEnabled dice si esta copia tomó los atajos (05 §10). StartWithWindows sale de
/// Windows al armar la foto, no de local.json (02 §2). StartWithWindows y Obs, con su valor por defecto, entran en la etapa 8; McpEnabled y
/// McpCommand, en la etapa MCP: el interruptor del MCP y el comando para registrarlo en Claude Code, null en una copia sin instalar (05 §7).
/// </summary>
public sealed record SettingsView(
    bool HotkeysEnabled,
    string? ConsolePort,
    string? SkippedVersion,
    StartupStatus StartWithWindows = StartupStatus.Unavailable,
    ObsSettingsView? Obs = null,
    bool McpEnabled = true,
    string? McpCommand = null)
{
    public static StartupStatus Startup(bool? enabled) => enabled switch
    {
        true => StartupStatus.On,
        false => StartupStatus.Off,
        null => StartupStatus.Unavailable,
    };
}

/// <summary>
/// settings.save: sólo los campos que cambiaron. skippedVersion "" borra la versión salteada. Contesta el SettingsView nuevo. mcpEnabled
/// entra en la etapa MCP: el pedido sigue siendo sólo de la ventana, así el MCP no se puede prender a sí mismo. consolePort, en la 8b:
/// "" es "Sin consola" y borra la clave; el puerto nuevo se usa al volver a abrir PampaPad (plan de la 8b, decisión 3).
/// </summary>
public sealed record SettingsSaveRequest(bool? StartWithWindows = null, string? SkippedVersion = null, bool? McpEnabled = null, string? ConsolePort = null);

/// <summary>Un puerto serie que está, con la descripción de Windows y, si es USB, su VID:PID en hexadecimal (05 §11, console.ports).</summary>
public sealed record ConsolePortView(string Port, string Description, string? UsbId);

/// <summary>console.ports: los puertos serie que están ahora. Sin puertos, la lista vacía.</summary>
public sealed record ConsolePortsResponse(IReadOnlyList<ConsolePortView> Ports);
