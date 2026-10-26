using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// La ventana de edición: un Form con WebView2 que se crea al abrir y se destruye al cerrar (Decidido 6). No decide nada: conecta
/// WebMessageReceived con Bridge.HandleAsync y le pasa a la página los eventos del puente (01 §3.2 y §5). Todo en el hilo de la interfaz.
/// </summary>
internal sealed partial class EditorWindow : Form
{
    /// <summary>El host virtual de la página (01 §5). Termina en .example porque .local demora las navegaciones.</summary>
    public const string Host = "pampapad.example";

    public const string StatusScreen = "status";

    public const string KeysScreen = "keys";

    /// <summary>La de "Abrir PampaPad" y la de la segunda copia: Teclas (05 §3), desde que existe (etapa 7).</summary>
    public const string FirstScreen = KeysScreen;

    // Si la página no avisa, se muestra igual a los 3 s (01 §5; Propuesto, sin medir). E0-3 midió el aviso entre 563 y 1476 ms.
    private static readonly TimeSpan ShowLimit = TimeSpan.FromSeconds(3);

    // El fondo de la página (ui/src/index.css, --color-background): igual acá, para que no haya destello al mostrarla (01 §9).
    private static readonly Color Background = Color.FromArgb(0x0E, 0x0D, 0x17);

    // Los lentes, como el ejecutable (ApplicationIcon): un Form no toma solo el ícono del .exe y sin esto lleva el de WinForms. Va como
    // recurso, con sus siete tamaños; Icon.ExtractAssociatedIcon da sólo el de 32 px. Uno para todas las ventanas: el Form no lo libera.
    private static readonly Icon? WindowIcon = LoadIcon("PampaPad.App.Resources.pampapad.ico");

    private readonly Bridge _bridge;
    private readonly Log _log;
    private readonly SynchronizationContext _ui;
    private readonly string _uiFolder;
    private readonly string _dataFolder;
    private readonly Uri _origin;
    private readonly bool _fromDevServer;
    private readonly CancellationTokenSource _closing = new();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    private IDisposable? _events;
    private bool _shown;

    public EditorWindow(Bridge bridge, Log log, SynchronizationContext ui, string uiFolder, string dataFolder, Uri? devServer)
    {
        _bridge = bridge;
        _log = log;
        _ui = ui;
        _uiFolder = uiFolder;
        _dataFolder = dataFolder;
        _fromDevServer = devServer is not null;
        _origin = devServer ?? new Uri($"https://{Host}/");
        Text = AppIdentity.Title;
        if (WindowIcon is not null)
        {
            Icon = WindowIcon;
        }
        else
        {
            _log.Warning("window", "Falta el recurso del ícono: la ventana lleva el de WinForms");
        }

        BackColor = Background;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
    }

    /// <summary>La crea oculta, arma un entorno nuevo, navega a la pantalla y la muestra con window.ready o a los 3 s.</summary>
    public async Task OpenAsync(string screen)
    {
        // La ventana y el control existen sin mostrarse: la página carga oculta y avisa (E0-3).
        _ = Handle;
        _ = _web.Handle;

        // Un entorno nuevo en cada apertura, con la carpeta de datos fuera de current, que Velopack reemplaza entera (01 §9).
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, _dataFolder);
        await _web.EnsureCoreWebView2Async(environment);
        CoreWebView2 core = _web.CoreWebView2;
        Restrict(core.Settings);
        if (!_fromDevServer)
        {
            core.SetVirtualHostNameToFolderMapping(Host, _uiFolder, CoreWebView2HostResourceAccessKind.Allow);
        }

        core.WebMessageReceived += OnMessage;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;

        // Los eventos llegan en cualquier hilo: se encolan al de la interfaz (01 §4.4).
        _events = _bridge.Subscribe(json => _ui.Post(_ => Send(json), null));
        core.Navigate(new Uri(_origin, "index.html#/" + screen).ToString());
        _log.Info("window", $"Ventana creada oculta, runtime {environment.BrowserVersionString}, página de {(_fromDevServer ? _origin.ToString() : _uiFolder)}");

        try
        {
            await Task.Delay(ShowLimit, TimeProvider.System, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_shown)
        {
            _log.Warning("window", "La página no avisó en 3 s: la ventana se muestra igual");
            Reveal();
        }
    }

    /// <summary>Le manda a la página una respuesta o un evento. Sólo desde el hilo de la interfaz y con el control vivo (01 §9).</summary>
    public void Send(string json)
    {
        if (IsDisposed || _web.IsDisposed || _web.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or COMException)
        {
            _log.Warning("window", "No se pudo mandar a la página: " + e.Message);
        }
    }

    /// <summary>
    /// window.ready: la página tiene su foto y se puede mostrar. Corre en el hilo de la interfaz: el manejador de AppRequests se invoca
    /// desde OnMessage, y Bridge.HandleAsync lo corre de forma síncrona hasta su primer await.
    /// </summary>
    public void PageReady()
    {
        if (_shown)
        {
            return;
        }

        _log.Info("window", "La página avisó que está lista (window.ready)");
        Reveal();
    }

    /// <summary>Ya abierta: la lleva a otra pantalla, sin recargar, y la trae al frente.</summary>
    public void ShowScreen(string screen)
    {
        if (_web.CoreWebView2 is { } core)
        {
            _ = GoAsync(core, screen);
        }

        if (_shown)
        {
            Reveal();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Se cancelan los pedidos de esta ventana que siguen en curso (01 §5). El CancellationTokenSource no se libera: un manejador puede
        // tener todavía su token, como en Dispatcher.
        _closing.Cancel();
        _events?.Dispose();

        // Suelta el entorno: sus msedgewebview2.exe se van (E0-3) y la próxima apertura arma uno nuevo (01 §9).
        _web.Dispose();
        _log.Info("window", "Ventana cerrada");
        base.OnFormClosed(e);
    }

    private static void Restrict(CoreWebView2Settings settings)
    {
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;   // F5, Ctrl+R y las demás del navegador
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDevToolsEnabled = AppIdentity.Dev;       // sólo en Debug (01 §5)
    }

    // async void porque es un evento. HandleAsync nunca tira (01 §4.1) y Send atrapa lo suyo; nada se espera bloqueando.
    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsOwnOrigin(e.Source))
        {
            _log.Warning("window", "Mensaje de un origen que no es el de la página, ignorado: " + e.Source);
            return;
        }

        string reply = await _bridge.HandleAsync(e.WebMessageAsJson, Caller.Window, _closing.Token);
        Send(reply);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsOwnOrigin(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        OpenOutside(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;   // ninguna ventana nueva
        OpenOutside(e.Uri);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _log.Error("window", $"Falló un proceso de WebView2: {e.ProcessFailedKind}", ErrorCodes.InternalError);
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
        {
            // La página no se recupera sola: se cierra, y la próxima apertura arma un entorno nuevo (01 §9). Sin preguntar por cambios sin
            // guardar: ya no hay página que los guarde.
            Dirty = false;
            BeginInvoke(new MethodInvoker(Close));
        }
    }

    // El nombre es el espacio de nombres del proyecto más la ruta, como el de failed.wav en Tray. Icon copia el contenido: el flujo se cierra.
    private static Icon? LoadIcon(string resource)
    {
        using Stream? stream = typeof(EditorWindow).Assembly.GetManifestResourceStream(resource);
        return stream is null ? null : new Icon(stream);
    }

    private bool IsOwnOrigin(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == _origin.Scheme
        && string.Equals(uri.Host, _origin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == _origin.Port;

    private void OpenOutside(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            _log.Info("window", "Enlace externo, al navegador del sistema: " + uri);
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _log.Warning("window", "No se pudo abrir el navegador: " + ex.Message);
            }

            return;
        }

        _log.Warning("window", "Navegación fuera de la página, cancelada: " + address);
    }

    private void Reveal()
    {
        _shown = true;
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();

        // Show y Activate no siempre la traen al frente (E0-3, 2 de 3). Lo pide la bandeja, o la segunda copia, que cede el permiso (05 §1).
        bool front = GetForegroundWindow() == Handle || SetForegroundWindow(Handle);
        _log.Info("window", front ? "Ventana mostrada al frente" : "Ventana mostrada, pero Windows no la dejó al frente");
    }

    private async Task GoAsync(CoreWebView2 core, string screen)
    {
        try
        {
            await core.ExecuteScriptAsync($"location.hash = '#/{screen}'");
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or COMException)
        {
            _log.Warning("window", "No se pudo cambiar de pantalla: " + e.Message);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
