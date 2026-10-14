namespace PampaPad.Core;

/// <summary>Los atajos globales de Windows. El adaptador real está en la cáscara; acá, las reglas.</summary>
public interface ISystemHotkeys
{
    event Action<int>? Pressed;

    /// <summary>False si Windows rechazó la combinación: otro programa la registró antes.</summary>
    bool TryRegister(int id, Hotkey hotkey);

    void Unregister(int id);
}

/// <summary>Lo que RegisterHotKey espera en fsModifiers. Vive en el núcleo para que tenga prueba.</summary>
public static class HotkeyNative
{
    /// <summary>MOD_NOREPEAT: mantener la combinación apretada no repite el aviso. Una pulsación es una marca.</summary>
    public const uint NoRepeat = 0x4000;

    public static uint Modifiers(Hotkey hotkey) => (uint)hotkey.Modifiers | NoRepeat;
}
