using System.Globalization;

namespace PampaPad.Core;

/// <summary>
/// Lo que hizo un control (02 §6). Press, Release y Turn llegan de la consola; Tap, Hold y DoubleTap se reconocen en la PC.
/// Los atajos de teclado entregan Tap directo: Windows no avisa cuando la tecla se suelta.
/// </summary>
public enum Gesture
{
    Press,
    Release,
    Turn,
    Tap,
    Hold,

    /// <summary>Dos toques seguidos, sólo de un control que lo pide (Gestures, wantsDoubleTap).</summary>
    DoubleTap,
}

/// <summary>Lo que llega de una fuente de entrada. Source es el Id de la fuente; Value, los pasos de la perilla o el lugar del atajo en la lista general.</summary>
public sealed record InputEvent(string Source, string Control, Gesture Gesture, int Value = 0);

/// <summary>Los nombres de control de un InputEvent: key.1 a key.12, hotkey, y knob y moduleKey de la consola (02 §6).</summary>
public static class Controls
{
    public const string Hotkey = "hotkey";

    /// <summary>La perilla: Turn con los detentes con signo en Value, y Press/Release por su clic (PUSH).</summary>
    public const string Knob = "knob";

    /// <summary>La tecla del módulo (KO): Press/Release.</summary>
    public const string ModuleKey = "moduleKey";

    private const string KeyPrefix = "key.";

    public static string Key(int position) => KeyPrefix + position.ToString(CultureInfo.InvariantCulture);

    public static bool TryKey(string control, out int position)
    {
        position = 0;
        if (control.StartsWith(KeyPrefix, StringComparison.Ordinal)
            && int.TryParse(control.AsSpan(KeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number is >= 1 and <= Validator.KeyCount)
        {
            position = number;
            return true;
        }

        return false;
    }
}
