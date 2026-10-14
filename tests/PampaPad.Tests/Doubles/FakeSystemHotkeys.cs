using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Windows de mentira para los atajos: anota qué se registró, rechaza lo que "ya tiene otro programa" y deja simular una pulsación.</summary>
public sealed class FakeSystemHotkeys : ISystemHotkeys
{
    public event Action<int>? Pressed;

    public Dictionary<int, Hotkey> Registered { get; } = [];

    /// <summary>Las combinaciones que Windows va a rechazar.</summary>
    public HashSet<Hotkey> Taken { get; } = [];

    public bool TryRegister(int id, Hotkey hotkey)
    {
        if (Taken.Contains(hotkey) || Registered.ContainsValue(hotkey))
        {
            return false;
        }

        Registered[id] = hotkey;
        return true;
    }

    public void Unregister(int id) => Registered.Remove(id);

    /// <summary>Una pulsación: con MOD_NOREPEAT, mantener la combinación apretada es una sola. Devuelve false si nadie la tiene registrada.</summary>
    public bool Press(Hotkey hotkey)
    {
        foreach ((int id, Hotkey registered) in Registered)
        {
            if (registered == hotkey)
            {
                Pressed?.Invoke(id);
                return true;
            }
        }

        return false;
    }

    public bool Press(string text) => Press(Hotkey.Parse(text) ?? throw new ArgumentException($"\"{text}\" no es un atajo.", nameof(text)));

    /// <summary>Un aviso con un id cualquiera, como el que quedó en la cola de Windows de una combinación que ya se soltó.</summary>
    public void PressId(int id) => Pressed?.Invoke(id);

    /// <summary>Cuántos oyentes tiene Pressed ahora: para comprobar que la fuente se engancha una vez y se suelta al cerrar.</summary>
    public int Listeners => Pressed?.GetInvocationList().Length ?? 0;
}
