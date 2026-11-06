namespace PampaPad.Core;

public enum UpdatePhase
{
    Idle,
    Checking,
    Available,
    Downloading,
    Applying,
}

/// <summary>
/// En qué anda la actualización. Version es la que se ofrece; Detail, la última falla al bajarla o al dejarla instalada.
/// Que RestartPolicy no deje instalar no va acá: se calcula del estado cada vez (TraySummary.UpdateItems).
/// </summary>
public sealed record UpdateState(UpdatePhase Phase, string? Version = null, string? Detail = null)
{
    public static UpdateState Idle { get; } = new(UpdatePhase.Idle);
}

/// <summary>De qué carpeta es la biblioteca y qué pasó al cargarla. Folder está vacía si no hay carpeta elegida.</summary>
public sealed record LibraryState(LibraryStatus Status, string Folder);

/// <summary>La única foto de lo que el programa sabe (01 §3.1). Hasta ahora: la actualización, OBS y la biblioteca; las etapas siguientes suman el resto.</summary>
public sealed record AppState
{
    public static AppState Initial { get; } = new();

    /// <summary>Siempre creciente. Lo pone StateStore; quien arma un estado nuevo no lo toca.</summary>
    public long Seq { get; init; }

    public UpdateState Update { get; init; } = UpdateState.Idle;

    public ObsState Obs { get; init; } = ObsState.Initial;

    /// <summary>Qué pasó con la última carga de la biblioteca, y de qué carpeta.</summary>
    public LibraryState Library { get; init; } = new(LibraryStatus.NotLoaded, "");

    /// <summary>Todos los problemas vigentes: los de la carga, los de los pasos y los atajos que Windows rechazó.</summary>
    public IReadOnlyList<Problem> Problems { get; init; } = [];

    /// <summary>Cuántos atajos tomó el programa y cuántos rechazó Windows.</summary>
    public HotkeysState Hotkeys { get; init; } = new(0, 0);

    /// <summary>La página base, de la que salen los atajos de tecla. Vacía si no hay páginas.</summary>
    public string BasePage { get; init; } = "";

    /// <summary>La página principal (mainPage, o la primera): el texto de ayuda nombra la base cuando no es esta (05 §3).</summary>
    public string MainPage { get; init; } = "";

    /// <summary>La página que rige: la base o, con la consola, la capa de la tecla del módulo mientras se la mantiene, o la página encima.</summary>
    public string ActivePage { get; init; } = "";

    /// <summary>La página encima de page.overlay, o null (02 §7, regla 8). La capa mantenida rige sobre ella.</summary>
    public string? OverlayPage { get; init; }

    /// <summary>Las acciones en curso (01 §4.5).</summary>
    public IReadOnlyList<RunningAction> Running { get; init; } = [];

    /// <summary>El último chequeo previo, o null si todavía no corrió (01 §3.1: el chequeo previo vive en el estado).</summary>
    public PreflightReport? Preflight { get; init; }

    /// <summary>La última acción que falló. Se borra cuando la próxima termina bien o cuando se abre el menú de la bandeja.</summary>
    public ActionFailure? LastFailure { get; init; }

    /// <summary>La última acción que falló, con su hora: la consola la muestra 5 s en la línea de escena (04:147). Dismiss no la borra.</summary>
    public RecentFailure? RecentFailure { get; init; }

    /// <summary>El aviso grave a la vista, o null. Lo escribe sólo AlertWatch (plan de la 9b, E9b-3).</summary>
    public GraveAlert? Alert { get; init; }

    /// <summary>La primera falla del chequeo al empezar esta toma, en letras de la consola: el renglón chico (decisión 11). No es AppState.Preflight.</summary>
    public string? TakeCheckFailure { get; init; }

    /// <summary>El enlace con la consola por cable y el puerto que dice local.json. Sin puerto, apagada.</summary>
    public ConsoleLink Console { get; init; } = ConsoleLink.None;
}

public sealed record HotkeysState(int Registered, int Rejected);

public sealed record RunningAction(Guid RunId, string ActionId, Trigger RequestedBy);

/// <summary>NoticeId es el id del aviso (marker_failed o action_failed); Summary, "Falló acción: paso n (tipo)".</summary>
public sealed record ActionFailure(string NoticeId, string Summary, string Message);

/// <summary>
/// Lo que necesitan los avisos de la consola: el id del aviso, el nombre de la acción, el número del paso que falló, cuándo y el código del
/// error, del que sale el texto de marker_failed (plan de la 9b, E9b-2).
/// </summary>
public sealed record RecentFailure(string NoticeId, string ActionName, int? Step, DateTimeOffset At, string? Code = null);
