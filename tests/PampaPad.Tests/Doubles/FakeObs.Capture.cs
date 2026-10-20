using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

/// <summary>
/// Cómo mide el OBS falso las capturas en las listas de elementos: el espejo del celular como esté, y la captura de pantalla que se
/// reinicia cuando se le pide la lista de monitor_id. Eso lo midió una sonda de sólo lectura en el repaso de E5-22 (2026-10-19): el
/// GetSceneItemList siguiente da la captura en 0 x 0 (a los 3 a 6 ms) y vuelve a 1920 x 1080 antes de 150 ms, tres de tres; sin ese
/// pedido, cinco de cinco en 1920 x 1080. El doble lo imita siempre, en la primera lectura que trae esa fuente y en ninguna más: en OBS es
/// tiempo, no lecturas, así que dos chequeos seguidos lo ven en el segundo aunque en OBS haría falta que llegue antes de 150 ms.
/// </summary>
public sealed partial class FakeObs
{
    // Las capturas a las que se les pidió la lista de monitores y que todavía no salieron en una lectura.
    private readonly HashSet<string> _restarting = [];

    // Una copia de los elementos, con el espejo del celular medido como esté y la captura recién reiniciada en 0 x 0.
    private JsonArray Answered(JsonArray items)
    {
        var copy = items.DeepClone().AsArray();
        var restarted = new HashSet<string>();
        foreach (JsonNode? item in copy)
        {
            string? uuid = (string?)item?["sourceUuid"];
            if (uuid == CeluUuid && MirrorOpen)
            {
                Size(item!, 1823.0, 850.0);
            }
            else if (uuid is not null && _restarting.Contains(uuid))
            {
                Size(item!, 0.0, 0.0);
                restarted.Add(uuid);
            }
        }

        _restarting.ExceptWith(restarted);
        return copy;
    }

    private static void Size(JsonNode item, double width, double height)
    {
        item["sceneItemTransform"]!["sourceWidth"] = width;
        item["sceneItemTransform"]!["sourceHeight"] = height;
    }
}
