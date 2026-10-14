using System.Drawing.Drawing2D;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// Los íconos de estado de la bandeja, dibujados una vez. Cada uno se distingue por la forma y no sólo por
/// el color (spec 05 §3). Sin lógica: qué ícono va lo decide TraySummary, en el núcleo y con prueba.
/// </summary>
internal static class TrayIcons
{
    private const int Size = 32;
    private static readonly Color Red = Color.FromArgb(0xD2, 0x1F, 0x3C);
    private static readonly Color Amber = Color.FromArgb(0xF5, 0xA6, 0x23);
    private static readonly Color Violet = Color.FromArgb(0x6D, 0x5D, 0xF6);
    private static readonly Color Gray = Color.FromArgb(0x9A, 0x9A, 0xA6);
    private static readonly Dictionary<TrayIcon, Icon> Cache = [];

    /// <summary>Sólo se llama desde el hilo de la interfaz.</summary>
    public static Icon For(TrayIcon icon)
    {
        if (!Cache.TryGetValue(icon, out var drawn))
        {
            drawn = Draw(icon);
            Cache[icon] = drawn;
        }

        return drawn;
    }

    private static Icon Draw(TrayIcon icon)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            switch (icon)
            {
                case TrayIcon.Recording:        // círculo lleno, rojo
                    using (var brush = new SolidBrush(Red))
                    {
                        g.FillEllipse(brush, 4, 4, 24, 24);
                    }

                    break;
                case TrayIcon.Paused:           // dos barras verticales, ámbar
                    using (var brush = new SolidBrush(Amber))
                    {
                        g.FillRectangle(brush, 7, 5, 7, 22);
                        g.FillRectangle(brush, 18, 5, 7, 22);
                    }

                    break;
                case TrayIcon.Streaming:        // punto con dos arcos de onda, violeta
                    using (var brush = new SolidBrush(Violet))
                    using (var pen = new Pen(Violet, 3))
                    {
                        g.FillEllipse(brush, 12, 12, 8, 8);
                        g.DrawArc(pen, 5, 5, 22, 22, -50, 100);
                        g.DrawArc(pen, 5, 5, 22, 22, 130, 100);
                    }

                    break;
                case TrayIcon.NoObs:            // X, gris
                    using (var pen = new Pen(Gray, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(pen, 7, 7, 25, 25);
                        g.DrawLine(pen, 25, 7, 7, 25);
                    }

                    break;
                case TrayIcon.UpdateReady:      // flecha hacia abajo, violeta
                    using (var pen = new Pen(Violet, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLine(pen, 16, 4, 16, 25);
                        g.DrawLines(pen, new[] { new Point(7, 17), new Point(16, 27), new Point(25, 17) });
                    }

                    break;
                default:                        // contorno de círculo, gris: detenido, sin novedades
                    using (var pen = new Pen(Gray, 3))
                    {
                        g.DrawEllipse(pen, 5, 5, 22, 22);
                    }

                    break;
            }
        }

        // La manija del ícono no se libera: son seis y viven lo que vive el proceso.
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
