using System.Text.Json;

namespace PampaPad.Core;

// Las dos reglas del validador que miran pasos de OBS (02 §10, problemas 10 y 12). Van aparte por el tope de 300 líneas (01 §1).
// Como el resto del validador, no usan el catálogo de pasos: miran el type y los parámetros tal como están en el archivo.
public static partial class Validator
{
    /// <summary>Los pedidos de obs.request que detienen o pausan la grabación (02 §6).</summary>
    public static readonly IReadOnlySet<string> StopOrPauseRequests =
        new HashSet<string>(StringComparer.Ordinal) { "StopRecord", "PauseRecord", "ToggleRecord", "ToggleRecordPause" };

    /// <summary>Si algún paso de la acción para o pausa la grabación. Lo usa también el Dispatcher: esa tecla no dispara con un toque.</summary>
    public static bool StopsOrPauses(Action? action) =>
        action is not null && action.Steps.Indexed().Any(step => StopsOrPauses(step.Item));

    private static bool StopsOrPauses(Step step) => step.Type switch
    {
        ObsRecord.TypeId => Parameter(step, "do") is "stop" or "pause",
        ObsRequestStep.TypeId => Parameter(step, "requestType") is { } type && StopOrPauseRequests.Contains(type),
        _ => false,
    };

    // Problema 10: una tecla cuya action, la del toque, para o pausa. Esos pasos van en onHold. No mira onHold ni la lista hotkeys (02 §6).
    private static void CheckHold(Config config, List<Problem> found)
    {
        foreach ((int pageIndex, Page page) in config.Pages.Indexed())
        {
            foreach ((int keyIndex, Key key) in page.Keys.Indexed())
            {
                if (StopsOrPauses(config.FindAction(key.Action)))
                {
                    found.Add(Error(
                        $"pages[{pageIndex}].keys[{keyIndex}].action",
                        ProblemCodes.HoldRequired,
                        $"La tecla {key.Position} de {page.Name ?? page.Id ?? "?"} para o pausa la grabación con un toque: parar y pausar van en onHold, que pide mantener."));
                }
            }
        }
    }

    // Problema 12: con OBS no se usan combinaciones de teclas (Decidido 10).
    private static void CheckRequests(Config config, List<Problem> found)
    {
        foreach ((int actionIndex, Action action) in config.Actions.Indexed())
        {
            foreach ((int stepIndex, Step step) in action.Steps.Indexed())
            {
                if (step.Type == ObsRequestStep.TypeId && Parameter(step, "requestType") is { } type && ObsRequestStep.Forbidden.Contains(type))
                {
                    found.Add(Error(
                        $"actions[{actionIndex}].steps[{stepIndex}].requestType",
                        ProblemCodes.RequestForbidden,
                        $"obs.request no admite {type}: con OBS no se usan combinaciones de teclas (decisión 10)."));
                }
            }
        }
    }

    private static string? Parameter(Step step, string name) =>
        step.Parameters.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
