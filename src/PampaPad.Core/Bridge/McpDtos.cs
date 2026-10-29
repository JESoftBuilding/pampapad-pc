using System.Text.Json.Serialization;

namespace PampaPad.Core;

// Los datos de los pedidos que sólo usa el MCP (plan de la etapa MCP, tanda 2), aparte de Dtos.cs por el tope de 300 líneas. Tienen la
// puerta Doors.Mcp: la página no los manda y no llevan muestra en contract.samples.json (Contract.cs).

/// <summary>action.run: el id de una acción guardada. Contesta al terminar, con el ActionFinishedView (decisión 9).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionRunRequest(string Id);

/// <summary>action.save: una acción entera, que reemplaza a la del mismo id o se agrega al final, con la huella de config.json que se leyó.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionSaveRequest(Action Action, string ExpectedFingerprint);

/// <summary>
/// key.assign: la tecla Position (1 a 12) de la página Page queda con estos campos; los que no vienen quedan vacíos. OnHold es el id de
/// la acción al mantener. Con Clear, la tecla se vacía y los demás campos no cuentan. Contesta como config.save.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record KeyAssignRequest(
    string Page,
    int? Position,
    string ExpectedFingerprint,
    string? Label = null,
    IReadOnlyList<string>? ShortLabel = null,
    string? Action = null,
    string? OnHold = null,
    string? Hotkey = null,
    bool Clear = false);
