using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Un rectángulo del lienzo, en píxeles del lienzo base.</summary>
public readonly record struct CanvasRect(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Cuánto del lienzo llena una escena (03 §7, obs.canvasFilled): la unión, no el rectángulo envolvente, del área real de cada elemento
/// visible. Área real (Verificado: obs-websocket utils/Obs_ObjectHelper.cpp:47-89): con boundsType distinto de OBS_BOUNDS_NONE, bounds;
/// si no, el tamaño de la fuente menos el recorte, por la escala. Un elemento cuya fuente mide 0 no llena nada y se descarta (Propuesto:
/// el espejo cerrado tiene bounds y no tiene imagen). Un elemento volteado cuenta igual. La rotación no se cuenta (Incierto: plan de la
/// etapa 5). Es pura.
/// </summary>
public static class CanvasCoverage
{
    // Los bits de la alineación de OBS (obs-defs.h): izquierda 1, derecha 2, arriba 4, abajo 8; sin bits, centrado.
    private const int AlignLeft = 1;
    private const int AlignRight = 2;
    private const int AlignTop = 4;
    private const int AlignBottom = 8;

    /// <summary>El rectángulo de un elemento a partir de su sceneItemTransform, o null si no ocupa nada.</summary>
    public static CanvasRect? RectOf(JsonElement transform)
    {
        double sourceWidth = ObsMessages.Number(transform, "sourceWidth");
        double sourceHeight = ObsMessages.Number(transform, "sourceHeight");
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        double width;
        double height;
        int alignment = (int)ObsMessages.Number(transform, "alignment");
        if (ObsMessages.Text(transform, "boundsType") is { } bounds && bounds != "OBS_BOUNDS_NONE")
        {
            width = ObsMessages.Number(transform, "boundsWidth");
            height = ObsMessages.Number(transform, "boundsHeight");
        }
        else
        {
            // Voltear en OBS deja la escala negativa: mide lo mismo y crece hacia el otro lado del punto de alineación (libobs, obs-scene.c,
            // update_item_transform; revisión de E5-14). Volteado y con bounds no se miró (Incierto: plan de la etapa 5).
            double scaleX = ObsMessages.Number(transform, "scaleX");
            double scaleY = ObsMessages.Number(transform, "scaleY");
            width = (sourceWidth - ObsMessages.Number(transform, "cropLeft") - ObsMessages.Number(transform, "cropRight")) * Math.Abs(scaleX);
            height = (sourceHeight - ObsMessages.Number(transform, "cropTop") - ObsMessages.Number(transform, "cropBottom")) * Math.Abs(scaleY);
            alignment = Mirrored(alignment, scaleX < 0, AlignLeft | AlignRight);
            alignment = Mirrored(alignment, scaleY < 0, AlignTop | AlignBottom);
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        // La posición es la del punto de alineación del elemento.
        double x = ObsMessages.Number(transform, "positionX");
        double y = ObsMessages.Number(transform, "positionY");
        double left = (alignment & AlignLeft) != 0 ? x : (alignment & AlignRight) != 0 ? x - width : x - (width / 2);
        double top = (alignment & AlignTop) != 0 ? y : (alignment & AlignBottom) != 0 ? y - height : y - (height / 2);
        return new CanvasRect(left, top, left + width, top + height);
    }

    /// <summary>La parte del lienzo que cubren, de 0 a 1. Lo que cae fuera del lienzo no cuenta.</summary>
    public static double Covered(IReadOnlyList<CanvasRect> rects, double canvasWidth, double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(rects);
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return 0;
        }

        var clipped = rects
            .Select(rect => new CanvasRect(Math.Max(0, rect.Left), Math.Max(0, rect.Top), Math.Min(canvasWidth, rect.Right), Math.Min(canvasHeight, rect.Bottom)))
            .Where(rect => rect.Right > rect.Left && rect.Bottom > rect.Top)
            .ToList();
        double[] xs = [.. clipped.SelectMany(rect => new[] { rect.Left, rect.Right }).Distinct().Order()];
        double[] ys = [.. clipped.SelectMany(rect => new[] { rect.Top, rect.Bottom }).Distinct().Order()];
        double area = 0;
        for (int i = 0; i + 1 < xs.Length; i++)
        {
            for (int j = 0; j + 1 < ys.Length; j++)
            {
                double midX = (xs[i] + xs[i + 1]) / 2;
                double midY = (ys[j] + ys[j + 1]) / 2;
                if (clipped.Any(rect => rect.Left <= midX && midX < rect.Right && rect.Top <= midY && midY < rect.Bottom))
                {
                    area += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]);
                }
            }
        }

        return area / (canvasWidth * canvasHeight);
    }

    // Volteado sobre un eje, izquierda pasa a derecha (o arriba a abajo) y al revés; centrado queda igual.
    private static int Mirrored(int alignment, bool flipped, int axis) => flipped && (alignment & axis) != 0 ? alignment ^ axis : alignment;
}
