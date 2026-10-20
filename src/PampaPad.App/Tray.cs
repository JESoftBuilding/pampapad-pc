using System.Media;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>La cara del programa: ícono, texto de ayuda y menú (05 §3). Pinta lo que calcula TraySummary; no decide nada.</summary>
internal sealed class Tray : ApplicationContext
{
    private const int MaxTooltip = 127;

    // El recurso se llama como el espacio de nombres del proyecto más la ruta: PampaPad.App.Resources.failed.wav.
    private static readonly SoundPlayer? Failed = LoadSound("PampaPad.App.Resources.failed.wav");

    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _reconnect = new("Reconectar con OBS") { Visible = false };
    private readonly ToolStripMenuItem _preflight = new("Chequeo previo") { Visible = false };
    private readonly ToolStripMenuItem _cancelAll = new("Cancelar todo") { Visible = false };
    private readonly ToolStripMenuItem _console = new() { Visible = false };
    private readonly ToolStripMenuItem? _check;
    private readonly ToolStripMenuItem? _install;
    private TraySummary? _painted;

    /// <summary>Sin los dos manejadores, la bandeja no ofrece nada de actualización: es la copia sin instalar.</summary>
    public Tray(string title, System.Action? onCheckUpdate, System.Action? onInstallUpdate)
    {
        // El orden de la tabla de 05 §3: la ventana, "Chequeo previo", "Reconectar con OBS" (visible sólo en auth_failed y kicked),
        // "Cancelar todo" y después lo demás. Los tres del medio se ven según el estado (Paint).
        // Fuera del if de las actualizaciones: la copia de desarrollo también los necesita.
        _menu.Items.Add("Abrir PampaPad", null, (_, _) => OpenWindowRequested?.Invoke());
        _menu.Items.Add("Estado", null, (_, _) => StatusRequested?.Invoke());
        _menu.Items.Add(_preflight);
        _menu.Items.Add(_reconnect);
        _menu.Items.Add(_cancelAll);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Recargar biblioteca", null, (_, _) => ReloadRequested?.Invoke());
        _menu.Items.Add("Elegir carpeta de datos", null, (_, _) => ChooseFolderRequested?.Invoke());

        // Sólo aparece con un puerto en local.json: Paint lo muestra y le pone el texto (05 §3).
        _console.Click += (_, _) => ConsoleToggleRequested?.Invoke();
        _menu.Items.Add(_console);
        _menu.Items.Add(new ToolStripSeparator());

        if (onCheckUpdate is not null && onInstallUpdate is not null)
        {
            _check = new ToolStripMenuItem("Buscar actualización", null, (_, _) => onCheckUpdate());
            _install = new ToolStripMenuItem("Instalar", null, (_, _) => onInstallUpdate()) { Visible = false };
            _menu.Items.Add(_check);
            _menu.Items.Add(_install);
            _menu.Items.Add(new ToolStripSeparator());
        }

        _menu.Items.Add("Salir", null, (_, _) => ExitThread());

        _reconnect.Click += (_, _) => ReconnectRequested?.Invoke(this, EventArgs.Empty);
        _preflight.Click += (_, _) => PreflightRequested?.Invoke();
        _cancelAll.Click += (_, _) => CancelAllRequested?.Invoke();
        _menu.Opening += (_, _) => MenuOpened?.Invoke();

        _icon = new NotifyIcon
        {
            Icon = TrayIcons.For(TrayIcon.NoObs),
            Text = Clip(title),
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    public event EventHandler? ReconnectRequested;

    /// <summary>Se eligió "Abrir PampaPad" (05 §3).</summary>
    public event System.Action? OpenWindowRequested;

    /// <summary>Se eligió "Estado" (05 §3).</summary>
    public event System.Action? StatusRequested;

    public event System.Action? ReloadRequested;

    public event System.Action? ChooseFolderRequested;

    /// <summary>Se eligió "Soltar la consola" o "Tomar la consola": quien compone decide cuál de las dos según el enlace.</summary>
    public event System.Action? ConsoleToggleRequested;

    /// <summary>Se abrió el menú: es una de las dos cosas que sacan el ícono de "falló una acción" (05 §3).</summary>
    public event System.Action? MenuOpened;

    /// <summary>
    /// Se eligió "Chequeo previo": el resultado va al registro y a un globo (01 §8, fila 5). Que abra Estado con el resultado queda para
    /// la tarea de la pregunta 14 de la etapa 5, aunque la ventana ya exista.
    /// </summary>
    public event System.Action? PreflightRequested;

    /// <summary>Se eligió "Cancelar todo": cancela las acciones en curso (01 §4.5).</summary>
    public event System.Action? CancelAllRequested;

    /// <summary>El sonido "falló". Sólo desde el hilo de la interfaz. Si suena o no lo decidió el Notifier.</summary>
    public void PlayFailed() => Failed?.Play();

    /// <summary>
    /// El cartel de Windows. Sólo desde el hilo de la interfaz, y sólo con OBS detenido: lo decidió el Notifier. Un aviso que no es una
    /// advertencia, como un chequeo previo que dio bien, sale con el ícono de información.
    /// </summary>
    public void ShowBalloon(string title, string text, bool warning = true) =>
        _icon.ShowBalloonTip(5000, Shorten(title, 63), Shorten(text.Length == 0 ? title : text, 255), warning ? ToolTipIcon.Warning : ToolTipIcon.Info);

    /// <summary>Pinta lo que calculó TraySummary. Sólo desde el hilo de la interfaz. Lo ya pintado se saltea.</summary>
    public void Paint(TraySummary summary)
    {
        if (summary == _painted)
        {
            return;
        }

        _painted = summary;
        _icon.Icon = TrayIcons.For(summary.Icon);
        _icon.Text = Clip(summary.Tooltip);
        _reconnect.Visible = summary.CanReconnect;
        _preflight.Visible = summary.CanRunPreflight;
        _cancelAll.Visible = summary.CanCancel;
        _console.Visible = summary.ConsoleItem is not null;
        _console.Text = summary.ConsoleItem ?? string.Empty;
    }

    /// <summary>
    /// Pinta los dos ítems de actualización que calculó TraySummary.UpdateItems; sólo existen en la copia instalada.
    /// Sólo desde el hilo de la interfaz.
    /// </summary>
    public void PaintUpdate(UpdateMenu menu)
    {
        if (_check is null || _install is null)
        {
            return;
        }

        _check.Enabled = menu.CheckEnabled;
        _check.Text = menu.CheckText;
        _install.Visible = menu.InstallVisible;
        _install.Enabled = menu.InstallEnabled;
        _install.Text = menu.InstallText;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string Clip(string text) => Shorten(text, MaxTooltip);

    private static string Shorten(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";

    private static SoundPlayer? LoadSound(string resource)
    {
        Stream? stream = typeof(Tray).Assembly.GetManifestResourceStream(resource);
        return stream is null ? null : new SoundPlayer(stream);
    }
}
