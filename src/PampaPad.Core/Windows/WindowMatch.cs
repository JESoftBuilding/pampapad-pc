using System.Globalization;

namespace PampaPad.Core;

/// <summary>
/// Cómo se busca una ventana por título y cómo se dice lo que se encontró (02 §5: window.exists, window.sizeIs y wait.window). Es pura:
/// la lista la da IWindows. El título se compara entero y sin distinguir mayúsculas, como bancocam.ps1:16. El proceso es opcional
/// (decisión 32): sin él, una carpeta del Explorador que se llame "bancocam" también cuenta.
/// </summary>
public static class WindowMatch
{
    public static IReadOnlyList<WindowInfo> Find(IReadOnlyList<WindowInfo> windows, string title, string? process)
    {
        ArgumentNullException.ThrowIfNull(windows);
        string? wanted = Bare(process);
        return [.. windows.Where(window =>
            string.Equals(window.Title, title, StringComparison.OrdinalIgnoreCase)
            && (wanted is null || string.Equals(Bare(window.Process), wanted, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>
    /// Tamaño exacto, con tolerancia de 0 px (02 §5). Sin ancho ni alto pedidos sirve cualquier ventana, también una minimizada:
    /// wait.window exige el tamaño sólo "si se piden" (02 §5). Con un tamaño pedido, una minimizada nunca lo tiene.
    /// </summary>
    public static bool HasSize(WindowInfo window, int? width, int? height)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (width is null && height is null)
        {
            return true;
        }

        return !window.Minimized && (width is null || window.Width == width) && (height is null || window.Height == height);
    }

    /// <summary>Lo que se midió y lo que se esperaba: el detalle de las dos condiciones y la falla de la espera.</summary>
    public static string Describe(IReadOnlyList<WindowInfo> found, string title, string? process, int? width, int? height)
    {
        ArgumentNullException.ThrowIfNull(found);
        string name = Bare(process) is { } owner ? $"\"{title}\" de {owner}" : $"\"{title}\"";
        if (found.Count == 0)
        {
            return $"No hay una ventana {name}.";
        }

        // Entre las que sirven, primero una que se ve: de una minimizada sólo se dice eso, porque mide otra cosa (160 x 28, E5-22).
        if (found.Where(window => HasSize(window, width, height)).OrderBy(window => window.Minimized).FirstOrDefault() is { } good)
        {
            return good.Minimized ? $"La ventana {name} está minimizada." : $"La ventana {name} mide {good.Width} x {good.Height}.";
        }

        string expected = $" y se esperaba {Number(width, "cualquier ancho")} x {Number(height, "cualquier alto")}";
        WindowInfo? shown = found.FirstOrDefault(window => !window.Minimized);
        return shown is null
            ? $"La ventana {name} está minimizada{expected}."
            : $"La ventana {name} mide {shown.Width} x {shown.Height}{expected}.";
    }

    private static string Number(int? value, string none) => value?.ToString(CultureInfo.InvariantCulture) ?? none;

    private static string? Bare(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
        {
            return null;
        }

        string trimmed = process.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }
}
