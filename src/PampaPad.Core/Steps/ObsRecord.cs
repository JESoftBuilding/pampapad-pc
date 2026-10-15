namespace PampaPad.Core;

public sealed record RecordParameters(string? Do);

/// <summary>
/// obs.record: graba, detiene, pausa o sigue (02 §5; 03 §3, función 2). Antes de mandar mira el estado que conoce, con la tabla de
/// RecordRules: si OBS ya está ahí no manda nada, termina bien y deja una línea en el registro (03 §3). Arrancando o deteniendo, espera el
/// evento que cierra la transición hasta el tope de un pedido y decide con lo que quedó. La indicación sale del evento, nunca de la
/// respuesta (03 §5): el paso termina con el 100 y la bandeja cambia cuando llega el evento.
/// </summary>
public sealed class ObsRecord(ObsClient obs, StateStore state, Log log, TimeProvider clock) : StepType<RecordParameters>
{
    public const string TypeId = "obs.record";

    public override string Id => TypeId;

    public override string Name => "Grabar, parar o pausar";

    public override string Group => "OBS";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("do", "Qué hace", FieldKind.Option, Options: [.. RecordRules.Words]),
    ];

    protected override IEnumerable<Problem> Check(RecordParameters p)
    {
        if (!RecordRules.TryParse(p.Do, out _))
        {
            yield return Invalid("do", $"\"{p.Do}\" no es una orden de grabación: van start, stop, pause o resume.");
        }
    }

    protected override async Task<StepResult> RunAsync(RecordParameters p, Invocation invocation, CancellationToken ct)
    {
        if (!RecordRules.TryParse(p.Do, out RecordCommand command))
        {
            return new StepResult(new Failure(ErrorCodes.ConfigInvalid, $"\"{p.Do}\" no es una orden de grabación."));
        }

        string what = $"el pedido de {Verb(command)}";
        if (ObsStep.Unready(state.Current.Obs, what) is { } offline)
        {
            return new StepResult(offline);
        }

        RecordingState known = state.Current.Obs.Recording;
        if (RecordRules.Decide(command, known) == RecordMove.Wait)
        {
            if (await SettledAsync(ct).ConfigureAwait(false) is not { } settled)
            {
                string transition = known == RecordingState.Starting ? "arrancar" : "detener";
                return new StepResult(new Failure(ErrorCodes.ObsTimeout,
                    $"OBS no terminó de {transition} la grabación en {ObsClient.RequestTimeout.TotalSeconds:0} s: no se mandó {what}."));
            }

            known = settled;
        }

        switch (RecordRules.Decide(command, known))
        {
            case RecordMove.AlreadyThere:
                Note(invocation, $"{Verb(command)}: OBS ya está {RecordRules.Describe(known)}, no se manda nada.");
                return new StepResult(null);
            case RecordMove.NotRecording:
                return new StepResult(new Failure(ErrorCodes.NotRecording, $"No estás grabando: no hay grabación para {Verb(command)}."));
        }

        if (ObsStep.CancelledBefore(ct, what) is { } cancelled)
        {
            return new StepResult(cancelled);
        }

        ObsResponse response = await obs.RequestAsync(RecordRules.RequestType(command), null, ct).ConfigureAwait(false);
        if (response.Ok)
        {
            return new StepResult(null);
        }

        if (response.Code == RecordRules.RaceCode(command))
        {
            if (!RecordRules.RaceEndsWell(command, state.Current.Obs.Recording))
            {
                return new StepResult(new Failure(ErrorCodes.NotRecording, $"No estás grabando: OBS no tenía grabación para {Verb(command)}."));
            }

            Note(invocation, $"{Verb(command)}: OBS contestó {response.Code}, ya estaba así: cuenta como hecho.");
            return new StepResult(null);
        }

        return new StepResult(ObsStep.Rejected(response, what));
    }

    private static bool InTransition(RecordingState recording) => recording is RecordingState.Starting or RecordingState.Stopping;

    private static string Verb(RecordCommand command) => command switch
    {
        RecordCommand.Start => "grabar",
        RecordCommand.Stop => "parar",
        RecordCommand.Pause => "pausar",
        _ => "seguir",
    };

    // Espera a que la grabación salga de starting o stopping, hasta el tope de un pedido (03 §3). Null si no salió a tiempo. En cada aviso
    // mira Current y no el estado que trae el aviso, que puede ser uno viejo encolado antes de suscribirse. No toma ningún lock propio.
    private async Task<RecordingState?> SettledAsync(CancellationToken ct)
    {
        var settled = new TaskCompletionSource<RecordingState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = state.Subscribe((_, _) =>
        {
            RecordingState now = state.Current.Obs.Recording;
            if (!InTransition(now))
            {
                settled.TrySetResult(now);
            }
        });

        // Pudo haber salido entre la lectura y la suscripción.
        RecordingState current = state.Current.Obs.Recording;
        if (!InTransition(current))
        {
            return current;
        }

        try
        {
            return await settled.Task.WaitAsync(ObsClient.RequestTimeout, clock, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private void Note(Invocation invocation, string message) =>
        log.Write(LogLevel.Info, "obs", message, null, invocation.RequestedBy, invocation.RunId);
}
