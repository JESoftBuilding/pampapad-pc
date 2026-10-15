namespace PampaPad.Core;

/// <summary>
/// El agregador del puente (01 §3.1): registra todos los pedidos del núcleo de forma explícita, un archivo por pantalla. Hoy, los de
/// Estado. Los que atiende la cáscara los registra AppRequests (RequestNames.Shell).
/// </summary>
public static class Requests
{
    public static IDisposable RegisterAll(Bridge bridge, StatusSources status) => StatusRequests.Register(bridge, status);
}
