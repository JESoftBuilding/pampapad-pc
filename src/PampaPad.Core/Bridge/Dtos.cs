using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

// Los datos del puente (01 §5): uno por pedido, respuesta y evento. Los de los pedidos prohíben los campos desconocidos, porque la
// página es entrada no confiable (Decidido 9, en bloque); los que salen no, porque los escribe el núcleo. El espejo en TypeScript es
// ui/src/bridge/contract.ts, y Contract.cs no deja que se separen.

/// <summary>Un pedido o una respuesta sin datos: viaja como {}.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NoData;

/// <summary>log.read (05 §11): cuántas entradas y los filtros. Level es el mínimo: warning trae warning y error.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LogReadRequest(int? Count = null, LogLevel? Level = null, Trigger? RequestedBy = null, Guid? RunId = null);

public sealed record LogReadResponse(IReadOnlyList<LogEntry> Entries);

/// <summary>action.cancel (05 §11): un runId, o All en true para todas. Una de las dos.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionCancelRequest(Guid? RunId = null, bool All = false);

public sealed record ActionCancelResponse(int Cancelled);

/// <summary>
/// app.snapshot (01 §5 y 05 §11): versión, versión del esquema, configuración, estado con su seq, tipos de paso y de condición, y
/// ajustes. Sin huella hasta la etapa 7. Los problemas van dentro del estado, que es donde los junta el Dispatcher.
/// </summary>
public sealed record AppSnapshot(
    string Version,
    int SchemaVersion,
    Config Config,
    StateView State,
    IReadOnlyList<StepTypeView> StepTypes,
    IReadOnlyList<ConditionTypeView> ConditionTypes,
    SettingsView Settings);

/// <summary>Un tipo de paso con sus campos, de donde sale el formulario de la pantalla Acciones (01 §6).</summary>
public sealed record StepTypeView(string Id, string Name, string Group, IReadOnlyList<Field> Fields)
{
    public static StepTypeView From(IStepType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new StepTypeView(type.Id, type.Name, type.Group, type.Fields);
    }
}

/// <summary>Un tipo de condición con sus campos (02 §5): de acá sale el formulario de un "sólo si" y de una comprobación del chequeo.</summary>
public sealed record ConditionTypeView(string Id, string Name, IReadOnlyList<Field> Fields)
{
    public static ConditionTypeView From(ConditionType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ConditionTypeView(type.Id, type.Name, type.Fields);
    }
}

/// <summary>Los ajustes de esta PC que la página muestra. HotkeysEnabled dice si esta copia tomó los atajos (05 §10).</summary>
public sealed record SettingsView(bool HotkeysEnabled, string? ConsolePort, string? SkippedVersion);

/// <summary>El estado tal como viaja: el de AppState, con el tiempo grabado en milisegundos y el enlace con OBS con guion bajo.</summary>
public sealed record StateView(
    long Seq,
    UpdateState Update,
    ObsView Obs,
    LibraryState Library,
    IReadOnlyList<Problem> Problems,
    HotkeysState Hotkeys,
    string BasePage,
    string ActivePage,
    IReadOnlyList<RunningAction> Running,
    ActionFailure? LastFailure,
    ConsoleLink Console)
{
    public static StateView From(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new StateView(
            state.Seq,
            state.Update,
            ObsView.From(state.Obs),
            state.Library,
            state.Problems,
            state.Hotkeys,
            state.BasePage,
            state.ActivePage,
            state.Running,
            state.LastFailure,
            state.Console);
    }
}

public sealed record ObsView(
    string Link,
    RecordingState Recording,
    bool Streaming,
    long RecordedMs,
    string? SceneName,
    string? MicName,
    bool MicMuted,
    bool SupportsMarkers,
    bool TakeInProgress)
{
    public static ObsView From(ObsState obs)
    {
        ArgumentNullException.ThrowIfNull(obs);
        return new ObsView(
            LinkName(obs.Link),
            obs.Recording,
            obs.Streaming,
            (long)obs.Recorded.TotalMilliseconds,
            obs.SceneName,
            obs.MicName,
            obs.MicMuted,
            obs.SupportsMarkers,
            obs.TakeInProgress);
    }

    /// <summary>
    /// Con guion bajo, como lo escriben 03 §2 y el glosario (no_websocket, auth_failed): pregunta abierta 59, escrito (b), "convertir
    /// ese enum a mano". Los demás enums viajan en camelCase, por Json.cs.
    /// </summary>
    public static string LinkName(ObsLinkState link) => JsonNamingPolicy.SnakeCaseLower.ConvertName(link.ToString());
}

/// <summary>action.finished: el ActionResult de la acción que terminó, con la duración en milisegundos.</summary>
public sealed record ActionFinishedView(
    string ActionId,
    Guid RunId,
    Trigger RequestedBy,
    bool Ok,
    int? StepNumber,
    string? StepType,
    Failure? Failure,
    long DurationMs,
    IReadOnlyList<StepReport> Steps)
{
    public static ActionFinishedView From(ActionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ActionFinishedView(
            result.ActionId,
            result.RunId,
            result.RequestedBy,
            result.Ok,
            result.StepNumber,
            result.StepType,
            result.Failure,
            (long)result.Duration.TotalMilliseconds,
            result.Steps);
    }
}

/// <summary>config.changed: la configuración que quedó después de cargar. Los problemas llegan con el estado.</summary>
public sealed record ConfigChangedView(Config Config);
