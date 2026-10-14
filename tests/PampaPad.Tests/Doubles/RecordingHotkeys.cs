using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Windows de mentira con hilo seguro: anota cada id que le piden y deja que la prueba actúe en medio de un Unregister.
/// Tira si le piden un id que ya tiene vivo, como haría RegisterHotKey con una combinación que no es la misma.
/// </summary>
internal sealed class RecordingHotkeys : ISystemHotkeys
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Hotkey> _live = [];

    public event Action<int>? Pressed;

    public List<int> Requested { get; } = [];

    public System.Action<int>? OnUnregister { get; set; }

    public int Live
    {
        get
        {
            lock (_gate)
            {
                return _live.Count;
            }
        }
    }

    public bool TryRegister(int id, Hotkey hotkey)
    {
        lock (_gate)
        {
            Requested.Add(id);
            if (_live.ContainsKey(id))
            {
                throw new InvalidOperationException($"El id {id} ya está vivo.");
            }

            if (_live.ContainsValue(hotkey))
            {
                return false;
            }

            _live[id] = hotkey;
            return true;
        }
    }

    public void Unregister(int id)
    {
        lock (_gate)
        {
            _live.Remove(id);
        }

        OnUnregister?.Invoke(id);
    }

    public void Raise(int id) => Pressed?.Invoke(id);
}
