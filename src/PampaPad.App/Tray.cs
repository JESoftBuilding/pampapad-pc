using System.Drawing.Drawing2D;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// La cara del programa: ícono, texto de ayuda y menú (05 §3). Hasta E2-6 pinta directo desde AppState;
/// después pinta lo que calcule TraySummary.
/// </summary>
internal sealed class Tray : ApplicationContext
{
    private const int MaxTooltip = 127;

    // Se dibujan una vez y viven lo que vive el proceso: sus asas no se liberan.
    private static readonly Icon Idle = Circle(Color.FromArgb(0x9A, 0x9A, 0xA8));
    private static readonly Icon UpdateReady = Arrow(Color.FromArgb(0x6D, 0x5D, 0xF6));

    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem? _check;
    private readonly ToolStripMenuItem? _install;
    private readonly string _title;

    /// <summary>Sin los dos manejadores, la bandeja no ofrece nada de actualización: es la copia sin instalar.</summary>
    public Tray(string title, System.Action? onCheckUpdate, System.Action? onInstallUpdate)
    {
        _title = title;
        if (onCheckUpdate is not null && onInstallUpdate is not null)
        {
            _check = new ToolStripMenuItem("Buscar actualización", null, (_, _) => onCheckUpdate());
            _install = new ToolStripMenuItem("Instalar", null, (_, _) => onInstallUpdate()) { Visible = false };
            _menu.Items.Add(_check);
            _menu.Items.Add(_install);
            _menu.Items.Add(new ToolStripSeparator());
        }

        _menu.Items.Add("Salir", null, (_, _) => ExitThread());
        _icon = new NotifyIcon
        {
            Icon = Idle,
            Text = Clip(title),
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    /// <summary>Se llama en el hilo de la interfaz, siempre con el último estado.</summary>
    public void Paint(AppState state)
    {
        var update = state.Update;
        var offered = update.Version is not null && update.Phase != UpdatePhase.Idle;
        _icon.Icon = offered ? UpdateReady : Idle;
        _icon.Text = Clip(Tooltip(update));
        if (_check is null || _install is null)
        {
            return;
        }

        _check.Enabled = update.Phase is UpdatePhase.Idle or UpdatePhase.Available;
        _check.Text = update.Phase == UpdatePhase.Checking ? "Buscando actualización…" : "Buscar actualización";
        _install.Visible = offered;
        _install.Enabled = update.Phase == UpdatePhase.Available;
        _install.Text = update.Phase switch
        {
            UpdatePhase.Downloading => $"Bajando la versión {update.Version}…",
            UpdatePhase.Applying => $"Instalando la versión {update.Version}…",
            _ => $"Instalar versión {update.Version}",
        };
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

    private static Icon Circle(Color color) => Draw(graphics =>
    {
        using var pen = new Pen(color, 4f);
        graphics.DrawEllipse(pen, 5, 5, 22, 22);
    });

    private static Icon Arrow(Color color) => Draw(graphics =>
    {
        using var pen = new Pen(color, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLine(pen, 16, 4, 16, 25);
        graphics.DrawLines(pen, new[] { new Point(7, 17), new Point(16, 27), new Point(25, 17) });
    });

    private static Icon Draw(Action<Graphics> paint)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            paint(graphics);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private string Tooltip(UpdateState update) => update.Phase switch
    {
        UpdatePhase.Available when update.Detail is null => $"Versión {update.Version} lista para instalar",
        UpdatePhase.Available => $"Versión {update.Version} lista para instalar. {update.Detail}",
        UpdatePhase.Downloading => $"Bajando la versión {update.Version}",
        UpdatePhase.Applying => $"Instalando la versión {update.Version}",
        _ => _title,
    };
}
