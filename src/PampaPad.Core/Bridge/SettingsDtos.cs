using System.Text.Json.Serialization;

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
/// PcLights, en la 1.6.0: la zona de OpenRGB elegida, o null con "No manejar".
/// </summary>
public sealed record SettingsView(
    bool HotkeysEnabled,
    string? ConsolePort,
    string? SkippedVersion,
    StartupStatus StartWithWindows = StartupStatus.Unavailable,
    ObsSettingsView? Obs = null,
    bool McpEnabled = true,
    string? McpCommand = null,
    PcLightsChoice? PcLights = null)
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
/// "" es "Sin consola" y borra la clave; el puerto nuevo se usa al volver a abrir PampaPad (plan de la 8b, decisión 3). pcLights, en la
/// 1.6.0: con dispositivo y zona elige esa zona, y con los dos vacíos es "No manejar" y saca pcLights entero; vale en el acto.
/// </summary>
public sealed record SettingsSaveRequest(
    bool? StartWithWindows = null, string? SkippedVersion = null, bool? McpEnabled = null, string? ConsolePort = null, PcLightsChoice? PcLights = null);

/// <summary>La zona de OpenRGB de la tarjeta Luces de la PC, por nombre (plan de la 1.6.0, decisión 7). Sin Last: ése lo escribe sólo PcLights.</summary>
public sealed record PcLightsChoice(string? Device, string? Zone)
{
    /// <summary>Si es "No manejar": los dos vacíos.</summary>
    [JsonIgnore]
    public bool None => string.IsNullOrEmpty(Device) && string.IsNullOrEmpty(Zone);

    /// <summary>Lo que muestra la tarjeta de lo que hay en local.json: null si no hay zona elegida.</summary>
    public static PcLightsChoice? Of(PcLightsSettings? settings) => settings is { Chosen: true } ? new(settings.Device, settings.Zone) : null;
}

/// <summary>Un puerto serie que está, con la descripción de Windows y, si es USB, su VID:PID en hexadecimal (05 §11, console.ports).</summary>
public sealed record ConsolePortView(string Port, string Description, string? UsbId);

/// <summary>console.ports: los puertos serie que están ahora. Sin puertos, la lista vacía.</summary>
public sealed record ConsolePortsResponse(IReadOnlyList<ConsolePortView> Ports);
