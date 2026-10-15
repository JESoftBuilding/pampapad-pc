using Microsoft.Web.WebView2.Core;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// El puente, la ventana de edición y lo que piden los ítems del menú de la bandeja (01 §3.2, §4.3 y §5). Va aparte de Composition.cs
/// por el tope de 300 líneas por archivo (01 §1).
/// </summary>
internal sealed partial class Composition
{
    private SynchronizationContext? _ui;
    private string _localFolder = "";
    private Bridge? _bridge;
    private IDisposable? _bridgeEvents;
    private EditorWindow? _window;

    /// <summary>El puente con todos sus pedidos. La prueba de humo comprueba que cada nombre de RequestNames tenga manejador.</summary>
    public Bridge Bridge => _bridge ?? throw new InvalidOperationException("El puente lo arma Composition.Build.");

    /// <summary>Otra copia pidió abrir la ventana (05 §1). Llega en un hilo del grupo (SingleInstance): se pasa al de la interfaz.</summary>
    public void OpenRequested()
    {
        Log.Info("app", "Otra copia pidió abrir la ventana");
        _ui?.Post(_ => OpenWindow(EditorWindow.FirstScreen), null);
    }

    /// <summary>Abre la ventana en esa pantalla o, si ya está abierta, la lleva a esa pantalla y al frente. Sólo desde el hilo de la interfaz.</summary>
    public void OpenWindow(string screen)
    {
        if (_bridge is null || _ui is null)
        {
            return;
        }

        if (_window is { IsDisposed: false } open)
        {
            open.ShowScreen(screen);
            return;
        }

        Uri? devServer = DevServer();
        string uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        if (devServer is null && !File.Exists(Path.Combine(uiFolder, "index.html")))
        {
            Log.Error("window", $"Falta la página en {uiFolder}: se arma con pnpm.cmd -C ui build antes de compilar", ErrorCodes.InternalError);
            MessageBox.Show($"Falta la página de la ventana en {uiFolder}.", AppIdentity.Title);
            return;
        }

        var window = new EditorWindow(_bridge, Log, _ui, uiFolder, Path.Combine(_localFolder, "WebView2"), devServer);
        window.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        _window = window;
        _ = OpenAsync(window, screen);
    }

    // Los new del puente, en su lugar de 01 §4.3. Los pedidos de la cáscara (AppRequests) van acá también: el humo los cuenta.
    private void ComposeBridge(
        StateStore state, Library library, Dispatcher dispatcher, IReadOnlyList<IStepType> catalog, LocalSettings settings, SynchronizationContext ui, string localFolder)
    {
        var bridge = new Bridge(state, Log);
        bool hotkeysOn = _hotkeys is not null;
        _bridgeEvents = Requests.RegisterAll(bridge, new StatusSources(
            AppIdentity.Version,
            state,
            library,
            dispatcher,
            Log,
            catalog,
            () => new SettingsView(hotkeysOn, settings.Current.ConsolePort, settings.Current.SkippedVersion)));
        AppRequests.Register(bridge, onWindowReady: () => ui.Post(_ => _window?.PageReady(), null));
        _bridge = bridge;
        _ui = ui;
        _localFolder = localFolder;
    }

    // Primero la ventana, en el hilo de la interfaz y antes de cerrar lo demás; después, los eventos del puente.
    private void CloseWindow()
    {
        _window?.Close();
        _window = null;
        _bridgeEvents?.Dispose();
    }

    private async Task OpenAsync(EditorWindow window, string screen)
    {
        try
        {
            await window.OpenAsync(screen);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            Forget(window);
            Log.Error("window", "Falta WebView2 Runtime: " + ex, ErrorCodes.InternalError);
            MessageBox.Show(
                "Falta Microsoft Edge WebView2 Runtime, que pone el instalador de PampaPad. Sin él no hay ventana; la bandeja y las teclas siguen andando.",
                AppIdentity.Title);
        }
        catch (Exception ex)
        {
            Forget(window);
            Log.Error("window", "No se pudo abrir la ventana: " + ex, ErrorCodes.InternalError);
        }
    }

    private void Forget(EditorWindow window)
    {
        if (ReferenceEquals(_window, window))
        {
            _window = null;
        }

        window.Dispose();
    }

    // En Debug, con PAMPAPAD_UI_DEV definida, la página sale del servidor de Vite (01 §5, 05 §10). En Release, nunca.
    private static Uri? DevServer()
    {
#if DEBUG
        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PAMPAPAD_UI_DEV")) ? null : new Uri("http://localhost:5173/");
#else
        return null;
#endif
    }

    private void CheckUpdate() => _ = RunAsync("buscar una actualización", () => _updater.CheckAsync(_life.Token));

    private void InstallUpdate() => _ = RunAsync("instalar la actualización", async () =>
    {
        var result = await _updater.DownloadAndApplyAsync(_life.Token);
        if (result.Ok)
        {
            // Seguimos en el hilo de la interfaz. Velopack ya espera a que el programa salga para instalarlo y reabrirlo.
            Tray?.ExitThread();
        }

        return result;
    });

    /// <summary>Los manejadores del menú no pueden dejar escapar una excepción ni quedar sin anotar. La línea lleva la excepción entera.</summary>
    private async Task RunAsync(string what, Func<Task<Result>> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log.Error("app", $"Falló {what}: {ex}", ErrorCodes.InternalError);
        }
    }
}
