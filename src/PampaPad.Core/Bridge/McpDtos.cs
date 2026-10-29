using System.Text.Json.Serialization;

namespace PampaPad.Core;

// Los datos de los pedidos que sólo usa el MCP (plan de la etapa MCP, tanda 2), aparte de Dtos.cs por el tope de 300 líneas. Tienen la
// puerta Doors.Mcp: la página no los manda y no llevan muestra en contract.samples.json (Contract.cs).

/// <summary>action.run: el id de una acción guardada. Contesta al terminar, con el ActionFinishedView (decisión 9).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionRunRequest(string Id);
