using PampaPad.Core;

namespace PampaPad.App;

/// <summary>La cara del programa: ícono, texto de ayuda y menú (05 §3). Pinta lo que calcula TraySummary; no decide nada.</summary>
internal sealed class Tray : ApplicationContext
{
    private const int MaxTooltip = 127;

    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _reconnect = new("Reconectar con OBS") { Visible = false };
    private readonly ToolStripMenuItem? _check;
    private readonly ToolStripMenuItem? _install;
    private TraySummary? _painted;

    /// <summary>Sin los dos manejadores, la bandeja no ofrece nada de actualización: es la copia sin instalar.</summary>
    public Tray(string title, System.Action? onCheckUpdate, System.Action? onInstallUpdate)
    {
        if (onCheckUpdate is not null && onInstallUpdate is not null)
        {
            _check = new ToolStripMenuItem("Buscar actualización", null, (_, _) => onCheckUpdate());
            _install = new ToolStripMenuItem("Instalar", null, (_, _) => onInstallUpdate()) { Visible = false };
            _menu.Items.Add(_check);
            _menu.Items.Add(_install);
            _menu.Items.Add(new ToolStripSeparator());
        }

        _menu.Items.Add("Salir", null, (_, _) => ExitThread());

        // Arriba de todo, y sólo visible en auth_failed y kicked (05 §3).
        _reconnect.Click += (_, _) => ReconnectRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Insert(0, _reconnect);

        _icon = new NotifyIcon
        {
            Icon = TrayIcons.For(TrayIcon.NoObs),
            Text = Clip(title),
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    public event EventHandler? ReconnectRequested;

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

    private static string Clip(string text) => text.Length <= MaxTooltip ? text : text[..(MaxTooltip - 1)] + "…";
}
