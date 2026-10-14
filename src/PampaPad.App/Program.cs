using Velopack;

namespace PampaPad.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Primera línea, antes de la instancia única: Velopack ejecuta este binario con argumentos propios al instalar,
        // actualizar y desinstalar (05 §8). Y nunca aplica solo, al arrancar, una actualización ya bajada (01 §9).
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => StartWithWindows.Enable())
            .OnBeforeUninstallFastCallback(_ => StartWithWindows.Disable())
            .Run();

        if (args.Contains("--smoke-test"))
        {
            return SmokeTest.Run(Path.Combine(AppContext.BaseDirectory, "examples", "library"));
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // NotifyIcon no es un control: sin esto no habría contexto de sincronización hasta crear el primero (01 §9).
        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        Composition? composition = null;
        if (!SingleInstance.TryAcquire(AppIdentity.Name, () => composition?.OpenRequested()))
        {
            return 0;
        }

        composition = Composition.Build(null, ui);
        _ = composition.StartAsync();
        Application.Run(composition.Tray!);
        composition.DisposeAsync().AsTask().GetAwaiter().GetResult();
        SingleInstance.Release();
        return 0;
    }
}
