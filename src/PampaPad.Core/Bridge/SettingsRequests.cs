namespace PampaPad.Core;

/// <summary>
/// Lo que necesitan los pedidos de Ajustes que no son de la biblioteca: local.json, el arranque con Windows, buscar y saltear una
/// actualización y cómo se arma el SettingsView de la foto. Las dos de la actualización son null en una copia sin instalar. Lo arma
/// Composition, también sin ventana: la prueba de humo cuenta los manejadores.
/// </summary>
public sealed record SettingsSources(
    LocalSettings Local, IStartup Startup, Func<CancellationToken, Task<Result>>? CheckUpdate, Func<CancellationToken, Task<Result>>? SkipUpdate, Func<SettingsView> View);

/// <summary>
/// Los pedidos de Ajustes de esta PC (05 §4.5 y §11). settings.save escribe sólo lo que cambió y contesta los ajustes como quedaron (la
/// tarjeta los muestra sin otra foto); update.check y update.skip llaman al ciclo de actualización, y la fase llega por state. Ninguno toca
/// las teclas: pasan con una toma en curso. update.install y app.restart no entran: la bandeja ya instala.
/// </summary>
public static class SettingsRequests
{
    public const string NotInstalled = "no disponible en la copia de desarrollo";

    public static void Register(Bridge bridge, SettingsSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<SettingsSaveRequest, SettingsView>(RequestNames.SettingsSave, RequestKind.WindowOnly, Doors.WindowAndTray,
            (request, _, ct) => SaveAsync(sources, request, ct));
        bridge.Register<NoData, NoData>(RequestNames.UpdateCheck, RequestKind.WindowOnly, Doors.WindowAndTray,
            (_, _, ct) => CallAsync(sources.CheckUpdate, "Buscar una actualización", ct));
        bridge.Register<NoData, NoData>(RequestNames.UpdateSkip, RequestKind.WindowOnly, Doors.WindowAndTray,
            (_, _, ct) => CallAsync(sources.SkipUpdate, "Saltear una versión", ct));
    }

    private static async Task<Result<SettingsView>> SaveAsync(SettingsSources sources, SettingsSaveRequest request, CancellationToken ct)
    {
        if (request.StartWithWindows is bool on)
        {
            if (sources.Startup.IsEnabled is null)
            {
                return Result<SettingsView>.Fail(ErrorCodes.InvalidData, $"El arranque con Windows está {NotInstalled}.", "startWithWindows");
            }

            if (sources.Startup.Set(on) is { Ok: false, Failure: { } failed })
            {
                return failed;
            }
        }

        if (request.SkippedVersion is string skipped)
        {
            string? version = skipped.Length == 0 ? null : skipped;
            if (await sources.Local.SaveAsync(data => data with { SkippedVersion = version }, ct).ConfigureAwait(false) is { Ok: false, Failure: { } failed })
            {
                return failed;
            }
        }

        return sources.View();
    }

    private static async Task<Result<NoData>> CallAsync(Func<CancellationToken, Task<Result>>? call, string what, CancellationToken ct)
    {
        if (call is null)
        {
            return Result<NoData>.Fail(ErrorCodes.InvalidData, $"{what} está {NotInstalled}: la actualización es de la copia instalada.");
        }

        Result result = await call(ct).ConfigureAwait(false);
        return result.Ok ? new NoData() : result.Failure!;
    }
}
