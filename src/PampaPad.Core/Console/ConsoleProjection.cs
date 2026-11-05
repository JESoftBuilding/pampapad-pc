using System.Globalization;

namespace PampaPad.Core;

/// <summary>
/// Del estado a lo que muestra la consola (plan de la 9a, E9-5): la banda, las 12 teclas de la página que rige, los dos textos y la tira.
/// Es pura: la hora es sólo la que le pasan, y una página o una acción que no existen dan teclas vacías, nunca una excepción.
/// </summary>
public static class ConsoleProjection
{
    public const int StripLeds = 8;
    public const int SceneLetters = 25;

    /// <summary>Lo que dura el aviso leve de una acción que falló en la línea de escena (04:147).</summary>
    public static readonly TimeSpan FailureShown = TimeSpan.FromSeconds(5);

    // Colores de LED, sin corrección (04 §7): el ámbar de la marca y el rojo puro, que en la tira se ve rojo.
    private static readonly LedColor Red = new(255, 0, 0);
    private static readonly LedColor Amber = new(245, 166, 35, LedEffect.Blink1Hz);

    public static ConsoleFrame Project(AppState state, Config config, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        ObsState obs = state.Obs;
        bool ready = obs.Link == ObsLinkState.Ready;
        ConsoleRecording recording = RecordingOf(obs);

        (int pageIndex, Page? page) = config.Pages.Indexed().FirstOrDefault(candidate => state.ActivePage.Length > 0 && candidate.Item.Id == state.ActivePage);
        var keys = new KeyFace[Validator.KeyCount];
        int active = 0, off = 0;
        for (int position = 1; position <= keys.Length; position++)
        {
            Key? key = page?.Keys.Indexed().Select(item => item.Item).FirstOrDefault(candidate => candidate.Position == position);
            (KeyKind kind, ObsRef? scene) = key is null ? (KeyKind.Empty, null) : KeyRoles.Of(key, config);
            int bit = 1 << (position - 1);
            keys[position - 1] = kind == KeyKind.Empty ? KeyFace.Empty : Face(key!, kind);
            off |= kind is KeyKind.Empty or KeyKind.Missing ? bit : 0;
            active |= ready && IsActive(kind, scene, obs) ? bit : 0;
        }

        var screen = new ScreenFrame(
            pageIndex,
            recording,
            Flags(obs, ready, state.Preflight),
            RecordedSec: ready ? (int)obs.Recorded.TotalSeconds : 0,
            PausedSec: recording == ConsoleRecording.Paused && obs.RecordingChangedOn is { } since ? Math.Max(0, (int)(now - since).TotalSeconds) : 0,
            ActiveMask: active,
            OffMask: off,
            Alert: state.Alert?.Number ?? 0,
            Flags2: Flags2(obs, state.Alert));

        // La tira (decisión 2): la de la grabación y, encima, el aviso titilando 4 veces por segundo en su color si su luz está prendida.
        LedColor led = (state.Alert, recording) switch
        {
            ({ Light: true } alert, _) => (alert.Tone == 2 ? Red : Amber) with { Effect = LedEffect.Blink4Hz },
            (_, ConsoleRecording.Recording) => Red,
            (_, ConsoleRecording.Paused) => Amber,
            _ => LedColor.Off,
        };

        return new ConsoleFrame(screen, keys, SceneLine(state, now), SmallLine(state, screen.Flags), [.. Enumerable.Repeat(led, StripLeds)], AlertOf(state.Alert));
    }

    // El byte 1 (04 §7): sin OBS listo es 0, salvo el cambio de colección, que tiene el suyo.
    private static ConsoleRecording RecordingOf(ObsState obs) => obs.Link switch
    {
        ObsLinkState.Switching => ConsoleRecording.Switching,
        ObsLinkState.Ready => obs.Recording switch
        {
            RecordingState.Starting => ConsoleRecording.Starting,
            RecordingState.Recording => ConsoleRecording.Recording,
            RecordingState.Paused => ConsoleRecording.Paused,
            RecordingState.Stopping => ConsoleRecording.Stopping,
            _ => ConsoleRecording.Stopped,
        },
        _ => ConsoleRecording.NoObs,
    };

    // LISTO y REVISAR (decisión 3): detenido, del último chequeo si es posterior al último cambio de la grabación. Así empezar a grabar
    // los borra, y no vuelven al detener hasta otro chequeo. REVISAR si hubo una falla o algo sin comprobar (04 §2).
    private static ScreenFlags Flags(ObsState obs, bool ready, PreflightReport? preflight)
    {
        ScreenFlags flags = ready && obs.MicMuted ? ScreenFlags.MicMuted : ScreenFlags.None;
        bool fresh = preflight is not null && (obs.RecordingChangedOn is not { } changed || preflight.At >= changed);
        if (ready && obs.Recording == RecordingState.Stopped && fresh)
        {
            flags |= ScreenFlags.PreflightRan;
            flags |= preflight!.Failures.Count > 0 || preflight.Unchecked.Count > 0 ? ScreenFlags.PreflightFailed : ScreenFlags.None;
        }

        return flags;
    }

    private static KeyFace Face(Key key, KeyKind kind)
    {
        IReadOnlyList<string> lines = ShortLabels.Of(key);
        return new KeyFace(KeyRoles.RoleOf(kind), lines[0], lines.Count > 1 ? lines[1] : "", Hold: key.OnHold?.Action is not null);
    }

    // Decisión 4: la palabra no cambia, el estado lo dicen el rol y esta máscara.
    private static bool IsActive(KeyKind kind, ObsRef? scene, ObsState obs) => kind switch
    {
        KeyKind.Record => obs.Recording is RecordingState.Recording or RecordingState.Paused,
        KeyKind.Pause => obs.Recording == RecordingState.Paused,
        KeyKind.Mic => obs.MicMuted,
        KeyKind.Scene => scene is { } wanted && ((wanted.Uuid is { Length: > 0 } uuid && uuid == obs.SceneUuid) || (wanted.Name.Length > 0 && wanted.Name == obs.SceneName)),
        _ => false,
    };

    // La escena al aire o, si una acción falló hace menos de 5 s, el aviso leve en ámbar (04:147). marker_failed no va acá: es grave y sale
    // en el cuerpo, con AppState.Alert (plan de la 9b, E9b-4).
    private static ConsoleText SceneLine(AppState state, DateTimeOffset now)
    {
        if (state.RecentFailure is { NoticeId: Notifier.ActionFailed } failure && now >= failure.At && now - failure.At < FailureShown)
        {
            string step = failure.Step is { } number ? string.Create(CultureInfo.InvariantCulture, $" P{number}") : "";
            return new ConsoleText(Letters($"FALLO{step}: {failure.ActionName}"), 1);
        }

        // Sin OBS listo la escena que se sabe puede ser vieja: la línea queda vacía.
        return new ConsoleText(state.Obs.Link == ObsLinkState.Ready ? Letters(state.Obs.SceneName ?? "") : "");
    }

    private static string Letters(string text) => ShortLabels.Cut(ShortLabels.Screen(text), SceneLetters).TrimEnd();

    // keepAwake con una toma en curso; alertSound si el canal de sonido del aviso está prendido, también grabando (decisión 9). alertLight no:
    // la consola no lo lee, y el vector del firmware no lo lleva.
    private static ScreenFlags2 Flags2(ObsState obs, GraveAlert? alert) =>
        (obs.Recording is RecordingState.Recording or RecordingState.Paused || obs.Streaming ? ScreenFlags2.KeepAwake : ScreenFlags2.None)
        | (alert is { Sound: true } ? ScreenFlags2.AlertSound : ScreenFlags2.None);

    // Los cuatro textos del aviso grave a la vista, con su número como referencia (plan de la 9b, decisión 10).
    private static ConsoleAlert? AlertOf(GraveAlert? alert) => alert is null ? null
        : new ConsoleAlert(alert.Number, new ConsoleText(alert.Title, alert.Tone), new ConsoleText(alert.Line1), new ConsoleText(alert.Line2), new ConsoleText(alert.Hint));

    // Con OBS listo, por prioridad (plan de la 9b, decisión 11): MIC MUDO; si no, la primera falla, la del chequeo al empezar la toma o la
    // del último chequeo mientras la banda dice REVISAR; si no, durante la toma, las marcas que entraron.
    private static string ReadyLine(AppState state, ScreenFlags flags)
    {
        ObsState obs = state.Obs;
        if (obs.MicMuted)
        {
            return "MIC MUDO";
        }

        if (obs.TakeInProgress)
        {
            return state.TakeCheckFailure ?? obs.TakeMarks switch
            {
                <= 0 => "",
                1 => "1 MARCA",
                int marks => string.Create(CultureInfo.InvariantCulture, $"{marks} MARCAS"),
            };
        }

        return flags.HasFlag(ScreenFlags.PreflightFailed) && state.Preflight?.Failures is [CheckResult first, ..] ? AlertTexts.Failure(first) : "";
    }

    // El renglón chico: sin OBS, el motivo por ObsLinkState (04 §2); con OBS, ReadyLine.
    private static ConsoleText SmallLine(AppState state, ScreenFlags flags) => new(state.Obs.Link switch
    {
        ObsLinkState.Ready => ReadyLine(state, flags),
        ObsLinkState.Closed => "NO ESTA ABIERTO",
        ObsLinkState.NoWebsocket => "OBS SIN CONEXION",
        ObsLinkState.AuthFailed => "CLAVE RECHAZADA",
        ObsLinkState.Loading => "CONECTANDO",
        ObsLinkState.Lost or ObsLinkState.Kicked => "SIN DATOS DE OBS",
        ObsLinkState.Exiting => "OBS SE CERRO",
        ObsLinkState.Switching => "CAMBIANDO",
        _ => "",
    });
}
