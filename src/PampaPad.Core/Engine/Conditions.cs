using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// El catálogo de condiciones (02 §5): cada una sirve para un onlyIf y para el chequeo previo (01 §6, "Una condición"). Reciben sus
/// dependencias al armarse, como los pasos (01 §3.1, Inferido). Las de OBS están en Conditions.Obs.cs y Conditions.Scene.cs.
/// </summary>
public static partial class Conditions
{
    public const string WindowExists = "window.exists";
    public const string WindowSizeIs = "window.sizeIs";
    public const string RecordingIs = "recording.is";

    private static readonly string[] RecordingWords = ["stopped", "recording", "paused"];

    /// <summary>Las de 02 §5 que ya existen, en el orden de su tabla.</summary>
    public static IReadOnlyList<ConditionType> Build(IWindows windows, StateStore state, ObsClient obs)
    {
        var queries = new ObsQueries(obs, state);
        return
        [
            Exists(windows),
            SizeIs(windows),
            Recording(state),
            Profile(queries),
            RecordFormat(queries),
            CameraAudio(queries),
            Mic(queries, state),
        ];
    }

    /// <summary>Hay una ventana con ese título (02 §5); process, opcional, la limita a la de ese programa (decisión 32).</summary>
    public static ConditionType Exists(IWindows windows) => new(
        WindowExists,
        "Hay una ventana",
        [new Field("title", "Título de la ventana", FieldKind.Window), new Field("process", "Programa", FieldKind.Text, Optional: true)],
        (p, _) =>
        {
            string title = ConditionRules.Text(p, "title") ?? "";
            string? process = ConditionRules.Text(p, "process");
            IReadOnlyList<WindowInfo> found = WindowMatch.Find(windows.List(), title, process);
            return Task.FromResult<Result<Verdict>>(new Verdict(found.Count > 0, WindowMatch.Describe(found, title, process, null, null)));
        })
    {
        Check = p => [.. CheckTitle(p), .. CheckProcess(p)],
    };

    /// <summary>La ventana existe y mide eso, comparado como en wait.window: exacto, en las unidades de SetWindowPos (02 §5).</summary>
    public static ConditionType SizeIs(IWindows windows) => new(
        WindowSizeIs,
        "Una ventana mide",
        [
            new Field("title", "Título de la ventana", FieldKind.Window),
            new Field("width", "Ancho", FieldKind.Number),
            new Field("height", "Alto", FieldKind.Number),
            new Field("process", "Programa", FieldKind.Text, Optional: true),
        ],
        (p, _) =>
        {
            string title = ConditionRules.Text(p, "title") ?? "";
            string? process = ConditionRules.Text(p, "process");
            int? width = ConditionRules.Integer(p, "width");
            int? height = ConditionRules.Integer(p, "height");
            IReadOnlyList<WindowInfo> found = WindowMatch.Find(windows.List(), title, process);
            bool met = found.Any(window => WindowMatch.HasSize(window, width, height));
            return Task.FromResult<Result<Verdict>>(new Verdict(met, WindowMatch.Describe(found, title, process, width, height)));
        })
    {
        Check = p => [.. CheckTitle(p), .. CheckProcess(p), .. CheckPositive(p, "width"), .. CheckPositive(p, "height")],
    };

    /// <summary>
    /// El estado de grabación que el programa ya conoce; no le pregunta a OBS (02 §5). Arrancando, grabando, en pausa y deteniendo
    /// cuentan como recording, como en la regla 1 de navegación (02, Incierto: Propuesto).
    /// </summary>
    public static ConditionType Recording(StateStore state) => new(
        RecordingIs,
        "Estado de la grabación",
        [new Field("state", "Estado", FieldKind.Option, Options: RecordingWords)],
        (p, _) =>
        {
            RecordingState known = state.Current.Obs.Recording;
            string wanted = ConditionRules.Text(p, "state") ?? "";
            bool met = wanted switch
            {
                "stopped" => known == RecordingState.Stopped,
                "paused" => known == RecordingState.Paused,
                _ => known != RecordingState.Stopped,
            };
            return Task.FromResult<Result<Verdict>>(new Verdict(met, $"OBS está {RecordRules.Describe(known)}; se pedía \"{wanted}\"."));
        })
    {
        Check = p => ConditionRules.Text(p, "state") is { } wanted && RecordingWords.Contains(wanted)
            ? []
            : [ConditionRules.Invalid("state", "state va stopped, recording o paused.")],
    };

    private static IEnumerable<Problem> CheckTitle(JsonElement p)
    {
        if (string.IsNullOrWhiteSpace(ConditionRules.Text(p, "title")))
        {
            yield return ConditionRules.Invalid("title", "Falta el título de la ventana, como texto.");
        }
    }

    // Un process que no es texto se leería como si no estuviera, y la condición contaría la ventana de cualquier programa: con el
    // negate de "ir al banco", una carpeta que se llame bancocam saltearía el script. Es un problema, como en un paso (StepType.Read).
    private static IEnumerable<Problem> CheckProcess(JsonElement p)
    {
        if (ObsMessages.Has(p, "process", out JsonElement value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            yield return ConditionRules.Invalid("process", "process va como texto: el programa sin .exe, por ejemplo scrcpy.");
        }
    }

    private static IEnumerable<Problem> CheckPositive(JsonElement p, string name)
    {
        if (ConditionRules.Integer(p, name) is not > 0)
        {
            yield return ConditionRules.Invalid(name, $"{name} va como un número entero de píxeles, mayor que 0.");
        }
    }
}
