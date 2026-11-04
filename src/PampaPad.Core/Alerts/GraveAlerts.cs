namespace PampaPad.Core;

/// <summary>
/// Qué aviso grave vale ahora (04 §5; plan de la 9b, decisiones 6 a 9). Puro: la hora es la que le pasan y mirar dos veces el mismo estado
/// da lo mismo, porque lo que salta por un cambio queda anotado en la memoria. Cada aviso sale por su condición o por el clic.
/// </summary>
public static class GraveAlerts
{
    /// <summary>Lo que dura marker_failed a la vista.</summary>
    public static readonly TimeSpan MarkerShown = TimeSpan.FromSeconds(2);

    /// <summary>Un silencio del micrófono es propio si PampaPad lo pidió en este lapso (decisión 7; sin medir).</summary>
    public static readonly TimeSpan OwnMute = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan LongPauseAfter = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan StillRecordingAfter = TimeSpan.FromHours(3);
    public static readonly TimeSpan StillRecordingQuiet = TimeSpan.FromMinutes(30);

    public static AlertMemory Step(AlertMemory memory, AppState state, Config config, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        ObsState obs = state.Obs;
        bool ready = obs.Link == ObsLinkState.Ready;
        AlertMemory m = (ready ? Seen(memory, obs, now) : Unseen(memory, obs)) with { WasReady = ready };

        GraveAlert? candidate = AlertTexts.Priority.Select(id => Candidate(id, m, state, now)).FirstOrDefault(alert => alert is not null);
        return m with { Shown = candidate is null ? null : Channels(candidate, memory.Shown, config) };
    }

    /// <summary>El clic: atiende el aviso a la vista. El que espera sale en la mirada siguiente, si sigue valiendo.</summary>
    public static AlertMemory Attend(AlertMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return memory.Shown switch
        {
            null => memory,
            { Id: AlertTexts.ObsLost } => memory with { Shown = null, Lost = null },
            { Id: AlertTexts.StillRecording } => memory with { Shown = null, StillQuietUntil = memory.ReadyRecorded + StillRecordingQuiet },
            { Id: AlertTexts.MarkerFailed } shown => memory with { Shown = null, MarkerAttended = shown.Since },
            { } shown => memory with { Shown = null, Attended = new HashSet<string>(memory.Attended) { shown.Id } },
        };
    }

    // Con el enlace listo: la toma nueva o terminada, el micrófono, la pausa y OBS que vuelve.
    private static AlertMemory Seen(AlertMemory m, ObsState obs, DateTimeOffset now)
    {
        bool wasTake = m.ReadyRecording is RecordingState.Recording or RecordingState.Paused;
        if (obs.Recording == RecordingState.Recording && !wasTake)
        {
            // Una toma nueva: lo atendido vuelve a valer. Arrancar con el micrófono silenciado avisa aunque lo haya silenciado el pad (04 §5).
            m = Fresh(m) with { Take = m.Take + 1, Lost = null, MicLatched = obs.MicMuted };
        }
        else if (obs.Recording == RecordingState.Stopped)
        {
            // obs_lost queda: OBS pudo volver detenido, y la toma que se cortó se ve hasta el clic.
            m = Fresh(m);
        }
        else if (obs.TakeInProgress)
        {
            m = m with { Lost = null };
        }

        bool own = obs.OwnMuteAt is { } at && now >= at && now - at <= OwnMute;
        bool latched = obs.MicMuted && (m.MicLatched || (obs.TakeInProgress && !m.MicWasMuted && !own));
        DateTimeOffset? pause = obs.Recording == RecordingState.Paused ? m.PauseSince ?? obs.RecordingChangedOn ?? now : null;
        return m with { MicLatched = latched, PauseSince = pause, ReadyRecording = obs.Recording, ReadyRecorded = obs.Recorded, MicWasMuted = obs.MicMuted };
    }

    // Sin enlace: si recién se cayó con una toma en curso, obs_lost. "Se cerró" sólo si llegó ExitStarted (04 §5), que deja Exiting.
    private static AlertMemory Unseen(AlertMemory m, ObsState obs)
    {
        if (m.WasReady && m.ReadyRecording is RecordingState.Recording or RecordingState.Paused)
        {
            m = m with { Lost = new LostTake(m.ReadyRecorded, Exited: false) };
        }

        return obs.Link == ObsLinkState.Exiting && m.Lost is { Exited: false } lost ? m with { Lost = lost with { Exited = true } } : m;
    }

    private static AlertMemory Fresh(AlertMemory m) => m with
    {
        Attended = new HashSet<string>(),
        MicLatched = false,
        PauseSince = null,
        StillQuietUntil = null,
        StartFailure = null,
    };

    private static GraveAlert? Candidate(string id, AlertMemory m, AppState state, DateTimeOffset now)
    {
        ObsState obs = state.Obs;
        bool inTake = obs.Link == ObsLinkState.Ready && obs.TakeInProgress;
        bool take = inTake && !m.Attended.Contains(id);
        return id switch
        {
            AlertTexts.ObsLost when m.Lost is { } lost =>
                Make(id, lost.Exited ? "OBS SE CERRO" : "SIN DATOS DE OBS", $"{AlertTexts.Clock(lost.Recorded)} GRABADOS", "CLICK: YA SE", now),
            AlertTexts.MicMuted when take && m.MicLatched => Make(id, "ESTAS GRABANDO", "", "MIC: ABRIR   CLICK: YA SE", now),
            AlertTexts.StillRecording when inTake && obs.Recorded >= StillRecordingAfter && obs.Recorded >= (m.StillQuietUntil ?? TimeSpan.Zero) =>
                Make(id, $"HACE {AlertTexts.Clock(obs.Recorded)}", "SEGUIS GRABANDO?", "CLICK: SIGO   REC: CORTAR", now),
            AlertTexts.RecordingWithFailure when take && m.StartFailure is { } failure => Make(id, "GRABANDO CON:", failure, "CLICK: YA SE", now),
            AlertTexts.LongPause when take && m.PauseSince is { } since && now - since >= LongPauseAfter =>
                Make(id, $"HACE {AlertTexts.Clock(now - since, minutes: true)}", "", "NO SE GRABA NADA", now),
            AlertTexts.MarkerFailed when state.RecentFailure is { NoticeId: AlertTexts.MarkerFailed } failed
                && now >= failed.At && now - failed.At < MarkerShown && failed.At != m.MarkerAttended => Marker(failed),
            _ => null,
        };
    }

    private static GraveAlert Marker(RecentFailure failed)
    {
        (string line1, string line2, string hint) = AlertTexts.Marker(failed.Code);
        return Make(AlertTexts.MarkerFailed, line1, line2, hint, failed.At);
    }

    private static GraveAlert Make(string id, string line1, string line2, string hint, DateTimeOffset since) => new(
        id,
        AlertTexts.Number(id),
        AlertTexts.Tone(id),
        AlertTexts.Title(id),
        AlertTexts.Letters(line1, AlertTexts.LineLetters),
        AlertTexts.Letters(line2, AlertTexts.LineLetters),
        AlertTexts.Letters(hint, AlertTexts.HintLetters),
        since,
        Light: true,
        Sound: true);

    // Los canales salen de console.alerts al aparecer y no cambian mientras dura (decisión 9). Por defecto, luz y sonido. Una falla de marca
    // nueva es otro aviso aunque tenga el mismo id: su hora es la de la falla.
    private static GraveAlert Channels(GraveAlert candidate, GraveAlert? previous, Config config)
    {
        if (previous is not null && previous.Id == candidate.Id && (candidate.Id != AlertTexts.MarkerFailed || previous.Since == candidate.Since))
        {
            return candidate with { Since = previous.Since, Light = previous.Light, Sound = previous.Sound };
        }

        AlertChannels? channels = config.Console?.Alerts?.GetValueOrDefault(candidate.Id);
        return candidate with { Light = channels?.Light ?? true, Sound = channels?.Sound ?? true };
    }
}
