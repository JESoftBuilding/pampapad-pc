using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>
/// script_test (05 §7): scripts.test y la espera de su salida por script.output hasta el end. Las líneas pueden llegar antes que la
/// respuesta con el runId, y por la misma conexión llega la salida de otras corridas del MCP: se guarda todo y se filtra por el runId.
/// </summary>
public sealed class ScriptTest(PipeClient pipe)
{
    private const string Text = """
        Prueba un script de la biblioteca: lo corre con su tope (timeoutSec de la ficha) y devuelve sus líneas en orden, cada una con su
        salida (stdout o stderr), y cómo terminó en "end": si anduvo, el código de salida, cuánto tardó y su mensaje. PampaPad corta la
        salida en 2.000 líneas, con un aviso; el script sigue hasta terminar. Espera el timeoutSec del script más 10 s.
        Es ejecutar código en la PC con los permisos de Emmanuel. Para probar un script recién escrito con script_write, pasá el mismo id.
        Mientras se graba, se está en pausa o se transmite, devuelve read_only_while_recording: grabando, Claude sólo lee.
        """;

    // El timeoutSec que vale si la ficha no lo trae (02 §9) y el margen sobre él (plan de la etapa MCP).
    private const int DefaultTimeoutSec = 15;
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(10);

    [McpServerTool(Name = "script_test")]
    [McpMeta(McpTools.RequiresUserInteraction, true)]
    [Description(Text)]
    public Task<string> TestAsync(
        [Description("El id del script.")] string id,
        CancellationToken cancellationToken = default) =>
        pipe.RunAsync(PipeClient.RequestTimeout, async (session, ct) =>
        {
            string read = await session.SendAsync(RequestNames.ScriptsRead, new { id }, null, ct).ConfigureAwait(false);
            JsonNode script = JsonNode.Parse(read)!;
            if (!ToolOutput.IsOk(script))
            {
                return ToolOutput.Clip(read);
            }

            var outputs = new List<JsonElement>();
            string started = await session.SendAsync(RequestNames.ScriptsTest, new { id }, outputs.Add, ct).ConfigureAwait(false);
            JsonNode test = JsonNode.Parse(started)!;
            if (!ToolOutput.IsOk(test))
            {
                return ToolOutput.Clip(started);
            }

            // Una ficha hecha a mano puede traer 0 o un negativo: CancelAfter tiraría con el script ya andando.
            int timeoutSec = script["data"]?["script"]?["timeoutSec"]?.GetValue<int>() is int seconds and > 0 ? seconds : DefaultTimeoutSec;
            session.WaitUpTo(TimeSpan.FromSeconds(timeoutSec) + Margin);
            string runId = test["data"]!["runId"]!.GetValue<string>();
            var lines = new JsonArray();
            JsonNode? end = null;
            for (int seen = 0; end is null; seen++)
            {
                JsonElement message = seen < outputs.Count ? outputs[seen] : await session.NextEventAsync(ct).ConfigureAwait(false);
                if (message.GetProperty("event").GetString() != EventNames.ScriptOutput)
                {
                    continue;
                }

                JsonElement data = message.GetProperty("data");
                if (!string.Equals(data.GetProperty("runId").GetString(), runId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (data.TryGetProperty("end", out JsonElement finished))
                {
                    end = JsonNode.Parse(finished.GetRawText());
                }
                else
                {
                    lines.Add(new JsonObject { ["line"] = data.GetProperty("line").GetString(), ["stream"] = data.GetProperty("stream").GetString() });
                }
            }

            return ToolOutput.Clip(new JsonObject
            {
                ["ok"] = true,
                ["data"] = new JsonObject { ["runId"] = runId, ["lines"] = lines, ["end"] = end },
            });
        }, cancellationToken);
}
