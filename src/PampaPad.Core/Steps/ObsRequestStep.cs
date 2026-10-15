using System.Text.Json;
using System.Text.Json.Nodes;

namespace PampaPad.Core;

public sealed record RequestParameters(string? RequestType, JsonElement? RequestData);

/// <summary>
/// obs.request: el pedido libre (02 §5; Decidido 9). Manda requestType y requestData tal cual, con los nombres del protocolo de
/// obs-websocket, y devuelve en StepResult.Data los datos de la respuesta. Los dos pedidos de atajos no se mandan nunca (Decidido 10):
/// el validador los marca al cargar (02 §10, problema 12) y el paso, además, se niega. La clave de transmisión no sale del paso: se
/// tapa antes de devolver (03 §2; Propuesto, porque 03 Incierto dice que no se habló).
/// </summary>
public sealed class ObsRequestStep(ObsClient obs, StateStore state) : StepType<RequestParameters>
{
    public const string TypeId = "obs.request";

    /// <summary>Lo que queda en lugar de la clave, como en relevar_obs.py.</summary>
    public const string Hidden = "<oculto>";

    public static readonly IReadOnlySet<string> Forbidden =
        new HashSet<string>(StringComparer.Ordinal) { "TriggerHotkeyByName", "TriggerHotkeyByKeySequence" };

    public override string Id => TypeId;

    public override string Name => "Pedido a OBS";

    public override string Group => "OBS";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("requestType", "Pedido", FieldKind.Text),
        new Field("requestData", "Datos del pedido", FieldKind.Json, Optional: true),
    ];

    /// <summary>La respuesta con streamServiceSettings.key tapada, si la trae. Si no, la misma.</summary>
    public static JsonElement Redact(JsonElement data)
    {
        if (!ObsMessages.Has(data, "streamServiceSettings", out JsonElement settings) || !ObsMessages.Has(settings, "key", out _))
        {
            return data;
        }

        JsonNode node = JsonNode.Parse(data.GetRawText())!;
        node["streamServiceSettings"]!["key"] = Hidden;
        return JsonSerializer.SerializeToElement(node, Json.Compact);
    }

    protected override IEnumerable<Problem> Check(RequestParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.RequestType))
        {
            yield return Invalid("requestType", "Falta el pedido: va el nombre del protocolo de obs-websocket, como GetSceneList.");
        }

        if (p.RequestData is { } data && data.ValueKind != JsonValueKind.Object)
        {
            yield return Invalid("requestData", "requestData tiene que ser un objeto, entre llaves.");
        }
    }

    protected override async Task<StepResult> RunAsync(RequestParameters p, Invocation invocation, CancellationToken ct)
    {
        string type = p.RequestType ?? "";
        if (Forbidden.Contains(type))
        {
            return new StepResult(new Failure(ErrorCodes.ConfigInvalid,
                $"obs.request no manda {type}: con OBS no se usan combinaciones de teclas (decisión 10). No se mandó nada."));
        }

        string what = $"el pedido {type}";
        if (ObsStep.Unready(state.Current.Obs, what) is { } offline)
        {
            return new StepResult(offline);
        }

        if (ObsStep.CancelledBefore(ct, what) is { } cancelled)
        {
            return new StepResult(cancelled);
        }

        object? data = p.RequestData is { } element ? element : null;
        ObsResponse response = await obs.RequestAsync(type, data, ct).ConfigureAwait(false);
        if (!response.Ok)
        {
            return new StepResult(ObsStep.Rejected(response, what));
        }

        JsonElement? answer = response.Data.ValueKind == JsonValueKind.Undefined ? null : Redact(response.Data);
        return new StepResult(null, null, answer);
    }
}
