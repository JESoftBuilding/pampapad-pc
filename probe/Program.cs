// Prueba descartable de la etapa 0: una bandeja vacía que se actualiza con Velopack desde GitHub Releases.
// No es el programa. Vive sólo en la rama prueba/velopack.
using System.Reflection;
using Velopack;

namespace PampaPadProbe;

internal static class Program
{
    internal const string RepoUrl = "https://github.com/JESoftBuilding/pampapad-pc";

    // Ruta fija y fuera de la carpeta de instalación (%LOCALAPPDATA%\PampaPadProbe): el registro tiene que
    // sobrevivir a la actualización y a la desinstalación, y leerse sin saber qué versión está corriendo.
    internal static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PampaPadProbe-log", "probe.log");

    private static readonly object LogLock = new();

    [STAThread]
    private static int Main(string[] args)
    {
        // Primera línea, antes de todo (01 §4.3 y §9): Velopack corre este binario con argumentos propios
        // al instalar, actualizar y desinstalar, y lo hace salir. No aplica sola al arrancar una versión ya bajada.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(version => Log($"gancho OnAfterInstall {version}"))
            .OnBeforeUpdateFastCallback(version => Log($"gancho OnBeforeUpdate {version}"))
            .OnAfterUpdateFastCallback(version => Log($"gancho OnAfterUpdate {version}"))
            .OnBeforeUninstallFastCallback(version => Log($"gancho OnBeforeUninstall {version}"))
            .OnFirstRun(version => Log($"gancho OnFirstRun {version}"))
            .OnRestarted(version => Log($"gancho OnRestarted {version}"))
            .Run();

        if (args.Contains("--echo"))
        {
            // Para saber si el ejecutable de arranque reenvía la entrada, la salida y el código de salida.
            string? line = Console.In.ReadLine();
            Console.Out.WriteLine($"eco: {line}");
            Console.Out.Flush();
            return 7;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"error sin atrapar: {e.ExceptionObject}");
        Application.ThreadException += (_, e) => Log($"error en la ventana: {Describe(e.Exception)}");

        string binary = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "desconocida";
        Log($"arranque; binario={binary}; args=[{string.Join(' ', args)}]; exe={Environment.ProcessPath}");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 01 §9: NotifyIcon no es un control, así que el contexto de sincronización se instala a mano.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        // --auto: busca, baja y aplica reiniciando apenas arranca, sin que nadie toque el menú de la bandeja.
        Application.Run(new TrayContext(auto: args.Contains("--auto")));
        Log("salida normal");
        return 0;
    }

    internal static string Describe(Exception ex) =>
        ex.InnerException is null
            ? $"{ex.GetType().Name}: {ex.Message}"
            : $"{ex.GetType().Name}: {ex.Message} <- {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";

    // Nunca tira: los ganchos de Velopack corren en otro proceso al mismo tiempo, y un registro que falla
    // no puede voltear la instalación ni la actualización.
    internal static void Log(string text)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [pid {Environment.ProcessId}] {text}{Environment.NewLine}";
        lock (LogLock)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    File.AppendAllText(LogPath, line);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(40);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }
    }
}
