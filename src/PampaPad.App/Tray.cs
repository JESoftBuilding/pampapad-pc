using System.Drawing.Drawing2D;

namespace PampaPad.App;

/// <summary>La cara del programa. En esta tarea sólo tiene el ícono y Salir; no decide nada (01 §3.2).</summary>
internal sealed class Tray : ApplicationContext
{
    private const int MaxTooltip = 127;

    // Se dibuja una vez y vive lo que vive el proceso: el asa del ícono no se libera.
    private static readonly Icon Idle = Circle(Color.FromArgb(0x9A, 0x9A, 0xA8));

    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;

    public Tray(string title)
    {
        _menu.Items.Add("Salir", null, (_, _) => ExitThread());
        _icon = new NotifyIcon
        {
            Icon = Idle,
            Text = Clip(title),
            ContextMenuStrip = _menu,
            Visible = true,
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
}
