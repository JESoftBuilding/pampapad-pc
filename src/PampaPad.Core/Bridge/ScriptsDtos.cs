namespace PampaPad.Core;

// Los datos de los pedidos de la pantalla Scripts (05 §4.3 y §11), aparte de Dtos.cs por el tope de 300 líneas. Su par en la página es
// ui/src/bridge/contract.scripts.ts.

/// <summary>Cómo está un script en la lista: listo, corriendo, con un problema o sin ficha (05 §4.3).</summary>
public enum ScriptStatus
{
    Ready,
    Running,
    Problem,
    NoManifest,
}

/// <summary>Desde dónde se dispara una acción: una tecla, el mantenido de una tecla o un atajo general.</summary>
public enum PlaceKind
{
    Key,
    Hold,
    Hotkey,
}

/// <summary>Un lugar que dispara una acción que corre el script. Where es la ubicación en config.json, como la de un problema.</summary>
public sealed record ScriptPlace(PlaceKind Kind, string Where, string? Page = null, string? PageName = null, int? Position = null, string? Label = null, string? Hotkey = null);

/// <summary>Una acción que corre el script: Where es su primer paso que lo corre, y Places, desde dónde se dispara. Se calcula al listar.</summary>
public sealed record ScriptUse(string Action, string? ActionName, string Where, IReadOnlyList<ScriptPlace> Places);

/// <summary>Una corrida, de su línea de cierre del registro.</summary>
public sealed record ScriptRunView(DateTimeOffset At, Trigger? RequestedBy, ScriptEnd End);

/// <summary>
/// Una fila de la pantalla Scripts. Name es el id si la ficha no lo trae; Wait y TimeoutSec, los que valen al correr (02 §9: verdadero
/// y 15). LastRun falta si no corrió en los días que guarda el registro.
/// </summary>
public sealed record ScriptView(
    string Id,
    string Name,
    string? Description,
    string? Entry,
    bool Wait,
    int TimeoutSec,
    int? Contract,
    IReadOnlyList<string> Tags,
    ScriptStatus Status,
    IReadOnlyList<ScriptProblem> Problems,
    IReadOnlyList<ScriptUse> UsedBy,
    ScriptRunView? LastRun = null);

/// <summary>Un archivo de la carpeta de un script. Text, sólo con scripts.read y content: el de un .cmd, .ps1 o .py de hasta 256 KB.</summary>
public sealed record ScriptFile(string Name, long Size, DateTimeOffset Modified, string? Text = null);

/// <summary>Un archivo de texto que scripts.write escribe en la carpeta del script: el nombre, sin carpetas, y su texto.</summary>
public sealed record ScriptText(string Name, string Text);

public sealed record ScriptsListResponse(IReadOnlyList<ScriptView> Scripts);

/// <summary>scripts.read: el id y, con Content, el texto de los archivos de texto (el MCP; la ventana no lo pide).</summary>
public sealed record ScriptsReadRequest(string Id, bool Content = false);

/// <summary>
/// scripts.read con el id: la fila, la ficha cruda con lo que el programa no conoce, los archivos, la huella de la carpeta
/// (ScriptFingerprint) y las cinco últimas corridas, primero la más nueva. El texto de los archivos viaja sólo con content: la ventana no edita código.
/// </summary>
public sealed record ScriptsReadResponse(ScriptView Script, ScriptManifest? Manifest, IReadOnlyList<ScriptFile> Files, string Fingerprint, IReadOnlyList<ScriptRunView> Runs);

/// <summary>
/// scripts.write: la ficha y, si vienen, archivos de texto (Files; la ventana no los manda), con la huella de la carpeta que se leyó
/// (ScriptFingerprint; vacía para crear). Contesta la huella nueva.
/// </summary>
public sealed record ScriptsWriteRequest(string Id, ScriptManifest Manifest, string ExpectedFingerprint, IReadOnlyList<ScriptText>? Files = null);

public sealed record ScriptsWriteResponse(string Fingerprint);

/// <summary>scripts.import: el id del script creado, o vacío si se canceló el diálogo.</summary>
public sealed record ScriptsImportResponse(string Id);

public sealed record ScriptsDeleteRequest(string Id);

/// <summary>La carpeta del respaldo local donde quedó el script quitado.</summary>
public sealed record ScriptsDeleteResponse(string BackupPath);

/// <summary>scripts.reveal: con el id abre la carpeta del script; sin id, scripts/.</summary>
public sealed record ScriptsRevealRequest(string? Id = null);

/// <summary>scripts.test: el id del script. Contesta el runId en el acto, como action.test (ActionTestResponse).</summary>
public sealed record ScriptsTestRequest(string Id);
