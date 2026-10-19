namespace PampaPad.Core;

/// <summary>Un aviso para la PC. Id es el del aviso (04 §5); Title y Text, lo que se muestra. Warning en falso es un aviso de que salió bien.</summary>
public sealed record Notice(string Id, string Title, string Text, bool Warning = true);

/// <summary>Por qué canales de la PC sale un aviso, además del ícono, que va siempre.</summary>
public readonly record struct NoticeChannels(bool Sound, bool Balloon);

/// <summary>
/// Reparte el aviso de una acción que falló entre los canales de la PC, con la matriz de 05 §5.
/// Con una toma en curso nunca hay cartel: la escena Pantalla graba el monitor entero y saldría en el video.
/// Sólo encola en cada canal: sonar y mostrar el cartel es de la bandeja.
/// </summary>
public sealed class Notifier(StateStore state, Library library, Log log, Action<Notice> playSound, Action<Notice> showBalloon)
{
    public const string MarkerFailed = "marker_failed";
    public const string ActionFailed = "action_failed";

    /// <summary>El globo del chequeo previo de la bandeja, entre las etapas 5 y 6 (01 §8). 04 §5 no lo nombra: el id es Propuesto.</summary>
    public const string PreflightResult = "preflight_result";

    /// <summary>La regla, pura. "Una toma en curso" es grabando, en pausa, arrancando, deteniendo o transmitiendo (03 §5).</summary>
    public static NoticeChannels Decide(RecordingState recording, bool streaming, bool muteSoundWhileRecording)
    {
        bool take = recording != RecordingState.Stopped || streaming;
        return new NoticeChannels(Sound: !(take && muteSoundWhileRecording), Balloon: !take);
    }

    /// <summary>Se engancha a Dispatcher.Finished.</summary>
    public void OnFinished(ActionResult result)
    {
        if (result.Ok)
        {
            Dismiss();
            return;
        }

        Config config = library.Current;

        // Una falla da un solo aviso: si el paso que falló es una marca, es marker_failed y no action_failed (04 §5).
        string id = result.StepType == ObsMarker.TypeId ? MarkerFailed : ActionFailed;
        string at = result.StepNumber is { } step ? $": paso {step}{(string.IsNullOrEmpty(result.StepType) ? "" : $" ({result.StepType})")}" : "";
        string summary = $"Falló {NameOf(config, result)}{at}";
        string message = result.Failure?.Message ?? "";

        var obs = state.Current.Obs;
        bool mute = config.Console?.MuteSoundWhileRecording ?? ConsoleSettings.DefaultMuteSoundWhileRecording;
        NoticeChannels channels = Decide(obs.Recording, obs.Streaming, mute);

        state.Update(s => s with { LastFailure = new ActionFailure(id, summary, message) });
        var notice = new Notice(id, summary, message);
        if (channels.Sound)
        {
            Deliver("sonido", playSound, notice, result);
        }

        if (channels.Balloon)
        {
            Deliver("cartel", showBalloon, notice, result);
        }

        log.Write(
            LogLevel.Warning,
            "notifier",
            $"Aviso {id}: {summary}. {message} Sonido: {(channels.Sound ? "sí" : "no")}; cartel: {(channels.Balloon ? "sí" : "no")}.",
            result.Failure?.Code,
            result.RequestedBy,
            result.RunId);
    }

    /// <summary>
    /// El resultado del chequeo previo que pidió la bandeja (01 §8, fila 5): un globo con el resumen y la primera falla, con la misma regla
    /// que los demás: con una toma en curso no sale y queda el registro, que ya tiene una línea por comprobación. No suena (decisión 32).
    /// </summary>
    public void OnPreflight(PreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Notice notice = Summarize(report);
        ObsState obs = state.Current.Obs;
        bool mute = library.Current.Console?.MuteSoundWhileRecording ?? ConsoleSettings.DefaultMuteSoundWhileRecording;
        bool balloon = Decide(obs.Recording, obs.Streaming, mute).Balloon;
        if (balloon)
        {
            try
            {
                showBalloon(notice);
            }
            catch (Exception error)
            {
                log.Write(LogLevel.Error, "notifier", $"No se pudo avisar por el cartel: {error.Message}", ErrorCodes.InternalError, report.RequestedBy, report.RunId);
            }
        }

        log.Write(LogLevel.Info, "notifier", $"Aviso {PreflightResult}: {notice.Title}. {notice.Text} Cartel: {(balloon ? "sí" : "no")}.", null, report.RequestedBy, report.RunId);
    }

    /// <summary>El resumen del globo: cuántas fallas y la primera; si no hay, lo que no se pudo comprobar; si no, que dio todo bien.</summary>
    public static Notice Summarize(PreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        IReadOnlyList<CheckResult> failures = report.Failures;
        IReadOnlyList<CheckResult> skipped = report.Unchecked;
        if (failures.Count > 0)
        {
            string more = failures.Count > 1 ? $" (y {failures.Count - 1} más en el registro)" : "";
            return new Notice(PreflightResult, $"Chequeo previo: {failures.Count} {(failures.Count == 1 ? "falla" : "fallas")}", $"{failures[0].Message}. {failures[0].Detail}{more}");
        }

        if (skipped.Count > 0)
        {
            return new Notice(PreflightResult, $"Chequeo previo: {skipped.Count} sin comprobar", skipped[0].Detail);
        }

        return report.Checks.Count == 0
            ? new Notice(PreflightResult, "Chequeo previo: no hay comprobaciones", "La lista preflight de config.json está vacía.", Warning: false)
            : new Notice(PreflightResult, "Chequeo previo: todo bien", $"Las {report.Checks.Count} comprobaciones dieron bien.", Warning: false);
    }

    /// <summary>Saca el ícono de falla: lo llaman la próxima acción que termina bien y la bandeja al abrir el menú.</summary>
    public void Dismiss()
    {
        if (state.Current.LastFailure is not null)
        {
            state.Update(s => s with { LastFailure = null });
        }
    }

    // El nombre que el usuario le puso a la acción; si no tiene, su id; si tampoco, que se entienda igual.
    private static string NameOf(Config config, ActionResult result) =>
        config.FindAction(result.ActionId)?.Name is { } name && !string.IsNullOrWhiteSpace(name) ? name
        : string.IsNullOrWhiteSpace(result.ActionId) ? "una acción" : result.ActionId;

    // Un canal que tira (la bandeja ya cerrada, un sonido que no carga) no puede dejar sin el otro canal ni sin la línea del registro.
    private void Deliver(string channel, Action<Notice> send, Notice notice, ActionResult result)
    {
        try
        {
            send(notice);
        }
        catch (Exception error)
        {
            log.Write(LogLevel.Error, "notifier", $"No se pudo avisar por el {channel}: {error.Message}", ErrorCodes.InternalError, result.RequestedBy, result.RunId);
        }
    }
}
