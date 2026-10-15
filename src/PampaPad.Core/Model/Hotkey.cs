using System.Globalization;
using System.Text;

namespace PampaPad.Core;

/// <summary>Los valores coinciden con MOD_ALT, MOD_CONTROL y MOD_SHIFT de RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
}

/// <summary>
/// Un atajo de teclado: modificadores y tecla virtual de Windows. Se escribe como en 02 §4.3:
/// de uno a tres entre Ctrl, Shift y Alt y una tecla entre A a Z, 0 a 9 y F1 a F24, unidos con +.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private const int F1 = 0x70;
    private const int F24 = 0x87;

    /// <summary>Lee sin distinguir mayúsculas. Devuelve <c>null</c> si el texto no es un atajo: Win no se admite y una tecla sola tampoco.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split('+');
        if (parts.Length < 2)
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            HotkeyModifiers one = parts[i].Trim().ToUpperInvariant() switch
            {
                "CTRL" => HotkeyModifiers.Ctrl,
                "SHIFT" => HotkeyModifiers.Shift,
                "ALT" => HotkeyModifiers.Alt,
                _ => HotkeyModifiers.None,
            };
            if (one == HotkeyModifiers.None || modifiers.HasFlag(one))
            {
                return null;
            }

            modifiers |= one;
        }

        int? key = ParseKey(parts[^1].Trim().ToUpperInvariant());
        return key is null ? null : new Hotkey(modifiers, key.Value);
    }

    /// <summary>Escribe siempre en el mismo orden: Ctrl, Shift, Alt y la tecla.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            text.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            text.Append("Shift+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            text.Append("Alt+");
        }

        if (VirtualKey is >= F1 and <= F24)
        {
            text.Append('F').Append((VirtualKey - F1 + 1).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            text.Append((char)VirtualKey);
        }

        return text.ToString();
    }

    private static int? ParseKey(string key)
    {
        // Las teclas virtuales de A a Z y de 0 a 9 valen lo mismo que su carácter.
        if (key.Length == 1 && key[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            return key[0];
        }

        if (key.Length is 2 or 3 && key[0] == 'F' && key[1] != '0'
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number is >= 1 and <= 24)
        {
            return F1 + number - 1;
        }

        return null;
    }
}
