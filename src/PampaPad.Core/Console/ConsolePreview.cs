namespace PampaPad.Core;

/// <summary>El estado de muestra de la vista previa de Teclas (05 §4.1): Grabando, En pausa, Detenida y Sin OBS.</summary>
public enum ConsolePreviewSample
{
    Recording,
    Paused,
    Stopped,
    NoObs,
}

/// <summary>
/// La vista previa de Teclas (plan de la 9b, decisión 13): el ConsoleFrame que saldría por el cable con el borrador y un AppState de
/// muestra, sin OBS. Las reglas de roles, etiquetas y máscaras siguen siendo las de ConsoleProjection; la página sólo dibuja.
/// </summary>
public static class ConsolePreview
{
    // Un instante fijo: los relojes de la muestra quedan quietos (decisión 14). 2052 s es el vector del firmware (0:34:12).
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Recorded = TimeSpan.FromSeconds(2052);
    private static readonly TimeSpan Paused = TimeSpan.FromSeconds(130);
    private const int Marks = 3;

    /// <summary>page es la página que se mira en Teclas; null o una que no existe dan las teclas vacías, como en la proyección.</summary>
    public static ConsoleFrame Frame(Config draft, string? page, ConsolePreviewSample sample, ObsState live)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(live);
        return ConsoleProjection.Project(State(draft, page ?? "", sample, live), draft, At);
    }

    private static AppState State(Config draft, string page, ConsolePreviewSample sample, ObsState live)
    {
        // Al aire, la primera escena de la página, para que se vea una tecla de escena activa; sin ninguna, la que OBS tiene ahora.
        ObsRef? scene = draft.Pages.Indexed().Select(item => item.Item).FirstOrDefault(candidate => candidate.Id == page)?.Keys.Indexed()
            .OrderBy(item => item.Item.Position)
            .Select(item => KeyRoles.Of(item.Item, draft))
            .FirstOrDefault(role => role.Kind == KeyKind.Scene).Scene;
        ObsState ready = ObsState.Initial with
        {
            Link = ObsLinkState.Ready,
            SceneName = scene?.Name ?? (live.Link == ObsLinkState.Ready ? live.SceneName : null),
            SceneUuid = scene?.Uuid,
        };

        ObsState obs = sample switch
        {
            ConsolePreviewSample.Recording => ready with { Recording = RecordingState.Recording, Recorded = Recorded, RecordingChangedOn = At - Recorded, TakeMarks = Marks },
            ConsolePreviewSample.Paused => ready with { Recording = RecordingState.Paused, Recorded = Recorded, RecordingChangedOn = At - Paused, TakeMarks = Marks },
            ConsolePreviewSample.Stopped => ready,
            _ => ObsState.Initial,
        };

        return AppState.Initial with { Obs = obs, ActivePage = page, BasePage = page };
    }
}
