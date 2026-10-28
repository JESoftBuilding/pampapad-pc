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

/// <summary>Un archivo de la carpeta de un script.</summary>
public sealed record ScriptFile(string Name, long Size, DateTimeOffset Modified);

public sealed record ScriptsListResponse(IReadOnlyList<ScriptView> Scripts);

public sealed record ScriptsReadRequest(string Id);

/// <summary>
/// scripts.read con el id: la fila, la ficha cruda con lo que el programa no conoce, los archivos, la huella de script.json (vacía sin
/// ficha) y las cinco últimas corridas, primero la más nueva. El contenido de los archivos no viaja: la ventana no edita código.
/// </summary>
public sealed record ScriptsReadResponse(ScriptView Script, ScriptManifest? Manifest, IReadOnlyList<ScriptFile> Files, string Fingerprint, IReadOnlyList<ScriptRunView> Runs);

/// <summary>scripts.write: sólo la ficha, con la huella de script.json que se leyó (vacía si no había). Contesta la huella nueva.</summary>
public sealed record ScriptsWriteRequest(string Id, ScriptManifest Manifest, string ExpectedFingerprint);

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
