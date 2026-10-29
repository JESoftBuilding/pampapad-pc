using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Mcp;

/// <summary>Lo que devuelve cada herramienta: la respuesta del programa como texto JSON, recortada a MaxChars y diciendo que recortó (05 §7).</summary>
public static class ToolOutput
{
    /// <summary>Claude Code corta la salida en 25.000 tokens: con esto se recorta antes y con aviso (Propuesto, sin medir).</summary>
    public const int MaxChars = 60_000;

    public static string Clip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= MaxChars)
        {
            return text;
        }

        // No partir un carácter en dos mitades.
        int cut = char.IsHighSurrogate(text[MaxChars - 1]) ? MaxChars - 1 : MaxChars;
        return string.Concat(text.AsSpan(0, cut),
            $"\n[Recortado: la respuesta tenía {text.Length} caracteres y se muestran los primeros {cut}. Pedí menos: con un filtro, un id o una parte.]");
    }

    // Lo que Claude necesita aunque el recorte se lleve la cola va adelante: la huella para guardar y cómo terminó la prueba.
    private static readonly string[] First = ["end", "fingerprint"];

    public static string Clip(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node["data"] is JsonObject data)
        {
            foreach (string key in First)
            {
                if (data.TryGetPropertyValue(key, out JsonNode? value))
                {
                    data.Remove(key);
                    data.Insert(0, key, value);
                }
            }
        }

        return Clip(node.ToJsonString(Json.Compact));
    }

    /// <summary>La respuesta con su data reducida a las claves que pasan keep. Un error vuelve entero.</summary>
    public static JsonObject Trim(string reply, Func<string, bool> keep)
    {
        var node = JsonNode.Parse(reply)!.AsObject();
        if (node["data"] is JsonObject data)
        {
            foreach (var key in data.Select(property => property.Key).Where(key => !keep(key)).ToList())
            {
                data.Remove(key);
            }
        }

        return node;
    }

    public static bool IsOk(JsonNode reply) => reply["ok"] is JsonValue ok && ok.TryGetValue(out bool value) && value;
}
