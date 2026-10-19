namespace PampaPad.Core;

/// <summary>El brillo medio y el desvío de una captura, como los mide relevar_obs.py:146-165 para que valgan sus números (03 §7).</summary>
public readonly record struct Brightness(double Mean, double Deviation);

/// <summary>
/// Lee la captura en bmp que devuelve GetSourceScreenshot sin librería de imágenes (03 §7: 43.254 bytes en 9,9 ms) y calcula el brillo
/// de cada pixel como 0,299 R + 0,587 G + 0,114 B. Acepta 24 y 32 bits por pixel; lo demás no es una captura de OBS y da null.
/// </summary>
public static class ImageStats
{
    public const string BmpPrefix = "data:image/bmp;base64,";

    /// <summary>Del imageData de OBS, con su prefijo, al brillo. Null si no es un bmp que se entienda.</summary>
    public static Brightness? FromImageData(string? imageData)
    {
        if (imageData is null || !imageData.StartsWith(BmpPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return FromBmp(Convert.FromBase64String(imageData[BmpPrefix.Length..]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static Brightness? FromBmp(ReadOnlySpan<byte> bmp)
    {
        if (bmp.Length < 54 || bmp[0] != 'B' || bmp[1] != 'M')
        {
            return null;
        }

        int offset = BitConverter.ToInt32(bmp[10..14]);
        int width = BitConverter.ToInt32(bmp[18..22]);
        int rows = BitConverter.ToInt32(bmp[22..26]);      // negativo si las filas van de arriba abajo
        int bits = BitConverter.ToInt16(bmp[28..30]);
        if (width <= 0 || rows is 0 or int.MinValue || bits is not (24 or 32))
        {
            return null;
        }

        // En long: con una cabecera rota, el ancho por los bytes de un pixel no entra en un int y daría una fila negativa (revisión de E5-14).
        int height = Math.Abs(rows);
        int pixel = bits / 8;
        long rowBytes = (long)width * pixel;
        long stride = (rowBytes + 3) / 4 * 4;
        if (offset < 54 || stride > bmp.Length || offset + (stride * height) > bmp.Length)
        {
            return null;
        }

        double sum = 0;
        double squares = 0;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = bmp.Slice(offset + (y * (int)stride), (int)rowBytes);
            for (int x = 0; x < width; x++)
            {
                // En un bmp los colores van al revés: azul, verde, rojo.
                double level = (0.299 * row[(x * pixel) + 2]) + (0.587 * row[(x * pixel) + 1]) + (0.114 * row[x * pixel]);
                sum += level;
                squares += level * level;
            }
        }

        double count = (double)width * height;
        double mean = sum / count;
        return new Brightness(mean, Math.Sqrt(Math.Max(0, (squares / count) - (mean * mean))));
    }
}
