namespace PampaPad.Core;

/// <summary>
/// Lo que necesitan los pedidos que editan la configuración (Teclas y Acciones): la biblioteca, el estado, el catálogo con que se valida
/// y se escribe cada paso, el despachador para probar una acción, las ventanas abiertas para las listas de los campos y la fuente de
/// atajos, que es null cuando esta copia no toma los atajos (05 §10). Lo arma Composition.
/// </summary>
public sealed record EditSources(
    Library Library,
    StateStore State,
    Log Log,
    Executor Executor,
    IReadOnlyList<IStepType> Steps,
    IReadOnlyList<ConditionType> Conditions,
    Dispatcher Dispatcher,
    IWindows Windows,
    HotkeySource? Hotkeys);

/// <summary>
/// El agregador del puente (01 §3.1): registra todos los pedidos del núcleo de forma explícita, un archivo por pantalla. Los de Estado
/// (StatusRequests) y los que editan (ConfigRequests, y con las etapas siguientes KeysRequests y ActionsRequests). Los que atiende la
/// cáscara los registra AppRequests (RequestNames.Shell).
/// </summary>
public static class Requests
{
    /// <summary>Sólo los de Estado: las pruebas de esa pantalla no necesitan lo demás.</summary>
    public static IDisposable RegisterAll(Bridge bridge, StatusSources status) => StatusRequests.Register(bridge, status);

    public static IDisposable RegisterAll(Bridge bridge, StatusSources status, EditSources edit)
    {
        IDisposable hooks = StatusRequests.Register(bridge, status);
        ConfigRequests.Register(bridge, edit);
        KeysRequests.Register(bridge, edit);
        ActionsRequests.Register(bridge, edit);
        return hooks;
    }
}
