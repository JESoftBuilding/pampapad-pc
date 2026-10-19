namespace PampaPad.Core;

public sealed record RecordParameters(string? Do);

/// <summary>
/// obs.record: graba, detiene, pausa o sigue (02 §5; 03 §3, función 2). Antes de mandar mira el estado que conoce, con la tabla de
/// RecordRules: si OBS ya está ahí no manda nada, termina bien y deja una línea en el registro (03 §3). Arrancando o deteniendo, espera el
/// evento que cierra la transición hasta el tope de un pedido y decide con lo que quedó. La indicación sale del evento, nunca de la
/// respuesta (03 §5). La guarda del hallazgo E4-11: no manda nada hasta que pasó Settle desde el último cambio que avisó OBS, y un
/// grabar que OBS contesta con 100 se confirma con el evento de arranque.
/// </summary>
public sealed class ObsRecord(ObsClient obs, StateStore state, Log log, TimeProvider clock) : StepType<RecordParameters>
{
    public const string TypeId = "obs.record";

    /// <summary>
    /// Cuánto se espera después de un cambio de la grabación antes de mandar otro pedido (Propuesto, sin medir: plan de la etapa 5). Medido
    /// el 2026-10-15, una vez cada uno: parar ~30 ms después de arrancar colgó la salida y grabar ~30 ms después de parar no arrancó; parar
    /// ~312 ms después de seguir y grabar 5 s después de parar anduvieron (E4-11).
    /// </summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

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
        long? waitedFor = null;
        while (true)
        {
            switch (RecordRules.Decide(command, known))
            {
                case RecordMove.Wait:
                    Result<RecordingState> settled = await SettledAsync(ct).ConfigureAwait(false);
                    if (!settled.Ok)
                    {
                        string transition = known == RecordingState.Starting ? "arrancar" : "detener";
                        return new StepResult(settled.Failure.Code == ErrorCodes.ObsTimeout
                            ? new Failure(ErrorCodes.ObsTimeout, $"OBS no terminó de {transition} la grabación en {ObsClient.RequestTimeout.TotalSeconds:0} s: no se mandó {what}.")
                            : settled.Failure);
                    }

                    known = settled.Value;
                    continue;
                case RecordMove.AlreadyThere:
                    Note(invocation, $"{Verb(command)}: OBS ya está {RecordRules.Describe(known)}, no se manda nada.");
                    return new StepResult(null);
                case RecordMove.NotRecording:
                    return new StepResult(new Failure(ErrorCodes.NotRecording, $"No estás grabando: no hay grabación para {Verb(command)}."));
            }

            // Una sola espera por aviso: si en la espera no llegó otro cambio, se manda.
            long? changedAt = state.Current.Obs.RecordingChangedAt;
            TimeSpan early = changedAt == waitedFor ? TimeSpan.Zero : Early(changedAt);
            if (early <= TimeSpan.Zero)
            {
                break;
            }

            Note(invocation, $"{Verb(command)}: OBS avisó un cambio de la grabación hace menos de {Settle.TotalSeconds:0} s; se espera {early.TotalMilliseconds:0} ms antes de mandar (E4-11).");
            await Task.Delay(early, clock, ct).ConfigureAwait(false);
            waitedFor = changedAt;
            known = state.Current.Obs.Recording;
        }

        if (ObsStep.CancelledBefore(ct, what) is { } cancelled)
        {
            return new StepResult(cancelled);
        }

        ObsResponse response = await obs.RequestAsync(RecordRules.RequestType(command), null, ct).ConfigureAwait(false);
        if (response.Ok)
        {
            return command == RecordCommand.Start ? await ConfirmStartAsync(ct).ConfigureAwait(false) : new StepResult(null);
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

    // Cuánto falta para que pase Settle desde el último cambio que avisó OBS, con el reloj monótono del TimeProvider: no salta si Windows
    // corrige la hora. Sin aviso desde que arrancó el programa, nada. Nunca más que Settle: una marca de otro reloj o que quedó adelante
    // no hace esperar de más.
    private TimeSpan Early(long? changedAt)
    {
        if (changedAt is not long at)
        {
            return TimeSpan.Zero;
        }

        TimeSpan since = clock.GetElapsedTime(at);
        return since < TimeSpan.Zero ? Settle : since >= Settle ? TimeSpan.Zero : Settle - since;
    }

    private static string Verb(RecordCommand command) => command switch
    {
        RecordCommand.Start => "grabar",
        RecordCommand.Stop => "parar",
        RecordCommand.Pause => "pausar",
        _ => "seguir",
    };

    // Un StartRecord que OBS contesta con 100 y no arranca no deja evento (E4-11): sin ..._STARTING en el tope de un pedido, falla. Así la
    // cadena no sigue con una marca que no va a entrar.
    private async Task<StepResult> ConfirmStartAsync(CancellationToken ct)
    {
        Result<RecordingState> started = await WaitForAsync(now => now != RecordingState.Stopped, ct).ConfigureAwait(false);
        return started.Ok || started.Failure.Code != ErrorCodes.ObsTimeout
            ? new StepResult(started.Ok ? null : started.Failure)
            : new StepResult(new Failure(ErrorCodes.ObsTimeout,
                $"OBS aceptó el pedido de grabar pero no avisó que arrancaba en {ObsClient.RequestTimeout.TotalSeconds:0} s: puede que no esté grabando."));
    }

    // Espera a que la grabación salga de starting o stopping, hasta el tope de un pedido (03 §3).
    private Task<Result<RecordingState>> SettledAsync(CancellationToken ct) => WaitForAsync(now => !InTransition(now), ct);

    // Espera a que la grabación cumpla algo, hasta el tope de un pedido. En cada aviso mira Current y no el estado que trae el aviso, que
    // puede ser uno viejo encolado antes de suscribirse. Si se corta el enlace es obs_disconnected y no obs_timeout (hallazgo E4-6). No
    // toma ningún lock propio.
    private async Task<Result<RecordingState>> WaitForAsync(Func<RecordingState, bool> done, CancellationToken ct)
    {
        var result = new TaskCompletionSource<Result<RecordingState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Look()
        {
            ObsState now = state.Current.Obs;
            if (now.Link != ObsLinkState.Ready)
            {
                result.TrySetResult(new Failure(ErrorCodes.ObsDisconnected, "Se cortó OBS mientras se esperaba el aviso de la grabación."));
            }
            else if (done(now.Recording))
            {
                result.TrySetResult(now.Recording);
            }
        }

        using IDisposable subscription = state.Subscribe((_, _) => Look());

        // Pudo haber pasado entre la lectura y la suscripción.
        Look();
        try
        {
            return await result.Task.WaitAsync(ObsClient.RequestTimeout, clock, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new Failure(ErrorCodes.ObsTimeout, "OBS no avisó a tiempo.");
        }
    }

    private void Note(Invocation invocation, string message) =>
        log.Write(LogLevel.Info, "obs", message, null, invocation.RequestedBy, invocation.RunId);
}
