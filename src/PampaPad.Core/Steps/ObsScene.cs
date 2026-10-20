namespace PampaPad.Core;

public sealed record SceneParameters(ObsRef? Scene);

/// <summary>
/// obs.scene: pone una escena al aire con SetCurrentProgramScene por sceneUuid (02 §5; 03 §3, función 1). La escena se busca en la lista
/// que refleja el estado, por uuid y, si no está, por nombre (02 §4.8): si no está, falla sin mandar nada (03 §1). Termina bien cuando OBS
/// acepta, sin esperar el evento de "al aire": con la escena ya al aire no llega (Medido: E0-1), y "al aire" lo dibuja la consola.
/// </summary>
public sealed class ObsScene(ObsClient obs, StateStore state, Log log) : StepType<SceneParameters>
{
    public const string TypeId = "obs.scene";
    public const string RequestType = "SetCurrentProgramScene";

    private const string What = "el cambio de escena";

    public override string Id => TypeId;

    public override string Name => "Cambiar de escena";

    public override string Group => "OBS";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("scene", "Escena", FieldKind.ObsScene),
    ];

    protected override IEnumerable<Problem> Check(SceneParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Scene?.Name))
        {
            yield return Invalid("scene", "Falta el nombre de la escena: se escribe \"scene\": \"Banco\" o { \"uuid\": \"...\", \"name\": \"Banco\" }.");
        }
    }

    protected override async Task<StepResult> RunAsync(SceneParameters p, Invocation invocation, CancellationToken ct)
    {
        if (p.Scene is not { } wanted)
        {
            return new StepResult(new Failure(ErrorCodes.ConfigInvalid, "Falta la escena."));
        }

        ObsState known = state.Current.Obs;
        if (ObsStep.Unready(known, What) is { } offline)
        {
            return new StepResult(offline);
        }

        if (ObsItems.Resolve(wanted, known.Scenes) is not { } scene)
        {
            return new StepResult(new Failure(ErrorCodes.ObsRefNotFound, $"OBS no tiene la escena \"{wanted.Name}\": no se mandó nada."));
        }

        Note(wanted, scene, invocation);
        if (ObsStep.CancelledBefore(ct, What) is { } cancelled)
        {
            return new StepResult(cancelled);
        }

        ObsResponse response = await obs.RequestAsync(RequestType, new { sceneUuid = scene.Uuid }, ct).ConfigureAwait(false);
        if (response.Ok)
        {
            return new StepResult(null);
        }

        // 600: se borró entre que se la buscó en la lista y que llegó el pedido.
        return new StepResult(response.Code == ObsStep.ResourceNotFound
            ? new Failure(ErrorCodes.ObsRefNotFound, $"OBS ya no tiene la escena \"{scene.Name}\": {response.Comment}")
            : ObsStep.Rejected(response, What));
    }

    // La corrección en memoria de 03 §1: se encontró, pero no como dice config.json. Se escribe recién con un guardado de la ventana.
    private void Note(ObsRef wanted, ObsItem scene, Invocation invocation)
    {
        string? message = wanted.Uuid is { } saved && saved != scene.Uuid
            ? $"La escena \"{scene.Name}\" se encontró por nombre: el uuid de config.json ya no está en OBS."
            : wanted.Name != scene.Name
                ? $"La escena \"{wanted.Name}\" ahora se llama \"{scene.Name}\" en OBS."
                : null;
        if (message is not null)
        {
            log.Write(LogLevel.Info, "obs", message, null, invocation.RequestedBy, invocation.RunId);
        }
    }
}
