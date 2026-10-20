using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Windows de mentira para los atajos: anota qué se registró, rechaza lo que "ya tiene otro programa" y deja simular una pulsación.</summary>
public sealed class FakeSystemHotkeys : ISystemHotkeys
{
    // La fuente registra en su hilo y la prueba aprieta en el suyo: el diccionario va con lock.
    private readonly object _gate = new();

    public event Action<int>? Pressed;

    public Dictionary<int, Hotkey> Registered { get; } = [];

    /// <summary>Las combinaciones que Windows va a rechazar.</summary>
    public HashSet<Hotkey> Taken { get; } = [];

    public bool TryRegister(int id, Hotkey hotkey)
    {
        lock (_gate)
        {
            if (Taken.Contains(hotkey) || Registered.ContainsValue(hotkey))
            {
                return false;
            }

            Registered[id] = hotkey;
            return true;
        }
    }

    public void Unregister(int id)
    {
        lock (_gate)
        {
            Registered.Remove(id);
        }
    }

    /// <summary>Una pulsación: con MOD_NOREPEAT, mantener la combinación apretada es una sola. Devuelve false si nadie la tiene registrada.</summary>
    public bool Press(Hotkey hotkey)
    {
        int? found = null;
        lock (_gate)
        {
            foreach ((int id, Hotkey registered) in Registered)
            {
                if (registered == hotkey)
                {
                    found = id;
                    break;
                }
            }
        }

        if (found is not { } pressed)
        {
            return false;
        }

        Pressed?.Invoke(pressed);
        return true;
    }

    public bool Press(string text) => Press(Parse(text));

    /// <summary>
    /// Espera a que la fuente registre la combinación y la aprieta. HotkeySource registra en su hilo después de Attach: apretar enseguida
    /// puede llegar antes (falló 1 de unas 15 corridas de la suite el 2026-10-20) y devolver false.
    /// </summary>
    public async Task<bool> PressWhenRegisteredAsync(string text)
    {
        Hotkey hotkey = Parse(text);
        await Poll.Until(() =>
        {
            lock (_gate)
            {
                return Registered.ContainsValue(hotkey);
            }
        });
        return Press(hotkey);
    }

    private static Hotkey Parse(string text) => Hotkey.Parse(text) ?? throw new ArgumentException($"\"{text}\" no es un atajo.", nameof(text));

    /// <summary>Un aviso con un id cualquiera, como el que quedó en la cola de Windows de una combinación que ya se soltó.</summary>
    public void PressId(int id) => Pressed?.Invoke(id);

    /// <summary>Cuántos oyentes tiene Pressed ahora: para comprobar que la fuente se engancha una vez y se suelta al cerrar.</summary>
    public int Listeners => Pressed?.GetInvocationList().Length ?? 0;
}
