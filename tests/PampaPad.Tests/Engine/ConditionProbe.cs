using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Evalúa una condición del catálogo real, escrita como en config.json, contra un OBS (falso o sin enlace).</summary>
internal static class ConditionProbe
{
    public static Task<ConditionResult> EvaluateAsync(ObsClient obs, StateStore state, string json, IWindows? windows = null, CancellationToken ct = default)
    {
        Condition condition = JsonSerializer.Deserialize<Condition>(json, Json.Options)!;
        ConditionType type = Conditions.Build(windows ?? new FakeWindows(), state, obs).Single(candidate => candidate.Id == condition.Type);
        return ConditionRules.EvaluateAsync(type, condition, ct);
    }

    public static Task<ConditionResult> EvaluateAsync(ObsSession session, string json, CancellationToken ct = default) =>
        EvaluateAsync(session.Client, session.State, json, ct: ct);

    /// <summary>Pone una escena al aire en el OBS falso, como desde su ventana, y espera a que el estado lo sepa.</summary>
    public static async Task OnAirAsync(ObsSession session, string name, string uuid)
    {
        await session.Obs.SetSceneAsync(name, uuid);
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == uuid);
    }
}
