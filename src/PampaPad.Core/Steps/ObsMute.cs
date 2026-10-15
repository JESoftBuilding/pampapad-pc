namespace PampaPad.Core;

public sealed record MuteParameters(ObsRef? Input, string? Do);

public enum MuteCommand
{
    Toggle,
    Mute,
    Unmute,
}

/// <summary>
/// obs.mute: silencia, abre o alterna una entrada de audio con SetInputMute por inputUuid (02 §5; 03 §3, función 4). Sin input es el
/// micrófono, el mic1 de GetSpecialInputs que refleja el estado. Alternar manda el contrario del silencio conocido: el del micrófono está
/// en el estado; el de otra entrada se pregunta antes con GetInputMute, porque el estado no lo refleja. Nunca ToggleInputMute: por uuid no
/// se probó (03, Incierto).
/// </summary>
public sealed class ObsMute(ObsClient obs, StateStore state) : StepType<MuteParameters>
{
    public const string TypeId = "obs.mute";
    public const string RequestType = "SetInputMute";

    private static readonly string[] Words = ["toggle", "mute", "unmute"];

    public override string Id => TypeId;

    public override string Name => "Silenciar o abrir el audio";

    public override string Group => "OBS";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("input", "Entrada de audio", FieldKind.ObsInput, Optional: true),
        new Field("do", "Qué hace", FieldKind.Option, Optional: true, Options: [.. Words]),
    ];

    /// <summary>Sin do es toggle, su valor por defecto (02 §5).</summary>
    public static bool TryParse(string? text, out MuteCommand command)
    {
        (bool ok, command) = text switch
        {
            null or "toggle" => (true, MuteCommand.Toggle),
            "mute" => (true, MuteCommand.Mute),
            "unmute" => (true, MuteCommand.Unmute),
            _ => (false, MuteCommand.Toggle),
        };
        return ok;
    }

    protected override IEnumerable<Problem> Check(MuteParameters p)
    {
        if (!TryParse(p.Do, out _))
        {
            yield return Invalid("do", $"\"{p.Do}\" no sirve: van toggle, mute o unmute.");
        }

        if (p.Input is { } input && string.IsNullOrWhiteSpace(input.Name))
        {
            yield return Invalid("input", "Falta el nombre de la entrada: se escribe \"input\": \"Mic/Aux\" o { \"uuid\": \"...\", \"name\": \"Mic/Aux\" }.");
        }
    }

    protected override async Task<StepResult> RunAsync(MuteParameters p, Invocation invocation, CancellationToken ct)
    {
        if (!TryParse(p.Do, out MuteCommand command))
        {
            return new StepResult(new Failure(ErrorCodes.ConfigInvalid, $"\"{p.Do}\" no sirve: van toggle, mute o unmute."));
        }

        ObsState known = state.Current.Obs;
        if (ObsStep.Unready(known, "el silencio") is { } offline)
        {
            return new StepResult(offline);
        }

        ObsItem? target = p.Input is { } wanted ? ObsItems.Resolve(wanted, known.Inputs) : Mic(known);
        if (target is null)
        {
            return new StepResult(new Failure(ErrorCodes.ObsRefNotFound, p.Input is { } missing
                ? $"OBS no tiene la entrada \"{missing.Name}\": no se mandó nada."
                : "OBS no informa micrófono (mic1 vacío en GetSpecialInputs): no se mandó nada."));
        }

        string what = $"el silencio de \"{target.Name}\"";
        bool muted = command == MuteCommand.Mute;
        if (command == MuteCommand.Toggle)
        {
            Result<bool> current = await CurrentAsync(known, target, ct).ConfigureAwait(false);
            if (!current.Ok)
            {
                return new StepResult(current.Failure);
            }

            muted = !current.Value;
        }

        if (ObsStep.CancelledBefore(ct, what) is { } cancelled)
        {
            return new StepResult(cancelled);
        }

        // Por uuid (Medido: E0-1). Si GetInputList no trajo el uuid del micrófono, va por nombre.
        object data = target.Uuid.Length > 0
            ? new { inputUuid = target.Uuid, inputMuted = muted }
            : new { inputName = target.Name, inputMuted = muted };
        ObsResponse response = await obs.RequestAsync(RequestType, data, ct).ConfigureAwait(false);
        return response.Ok ? new StepResult(null) : new StepResult(Missing(response, target) ?? ObsStep.Rejected(response, what));
    }

    private static ObsItem? Mic(ObsState known) => known.MicName is { } name ? new ObsItem(known.MicUuid ?? "", name) : null;

    // 600: la entrada se borró entre que se la buscó en la lista y que llegó el pedido.
    private static Failure? Missing(ObsResponse response, ObsItem target) => response.Code == ObsStep.ResourceNotFound
        ? new Failure(ErrorCodes.ObsRefNotFound, $"OBS ya no tiene la entrada \"{target.Name}\": {response.Comment}")
        : null;

    // El silencio conocido (03 §3): el del micrófono está en el estado; el de otra entrada se pregunta.
    private async Task<Result<bool>> CurrentAsync(ObsState known, ObsItem target, CancellationToken ct)
    {
        if (target.Uuid.Length == 0 || target.Uuid == known.MicUuid)
        {
            return known.MicMuted;
        }

        if (ObsStep.CancelledBefore(ct, $"la lectura del silencio de \"{target.Name}\"") is { } cancelled)
        {
            return cancelled;
        }

        ObsResponse reply = await obs.RequestAsync("GetInputMute", new { inputUuid = target.Uuid }, ct).ConfigureAwait(false);
        if (reply.Ok)
        {
            return ObsMessages.Flag(reply.Data, "inputMuted");
        }

        return Missing(reply, target) ?? ObsStep.Rejected(reply, $"la lectura del silencio de \"{target.Name}\"");
    }
}
