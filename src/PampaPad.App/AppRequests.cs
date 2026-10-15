using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// Los pedidos que sólo la cáscara puede atender, como windowOnly (01 §3.2). En la etapa 6, uno: window.ready, el aviso de la página de
/// que está lista para mostrarse (05 §11). Sus nombres están en RequestNames.Shell, que Contract.cs y la prueba de humo controlan.
/// </summary>
internal static class AppRequests
{
    public static void Register(Bridge bridge, System.Action onWindowReady)
    {
        bridge.Register<NoData, NoData>(RequestNames.WindowReady, RequestKind.WindowOnly, Doors.Window, (_, _, _) =>
        {
            onWindowReady();
            return Task.FromResult(Result<NoData>.Success(new NoData()));
        });
    }
}
