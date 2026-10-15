using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// Adaptador de RegisterHotKey. Sin reglas: qué se registra lo decide HotkeySource, en el núcleo.
/// Windows asocia cada combinación al hilo que la registra y le manda ahí WM_HOTKEY, así que todo pasa por un hilo propio
/// con su bucle de mensajes: una marca no espera a que la interfaz esté libre (01 §9).
/// </summary>
internal sealed class SystemHotkeys : ISystemHotkeys, IDisposable
{
    private const uint WmQuit = 0x0012;
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<System.Action> _work = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private uint _threadId;
    private volatile bool _disposed;

    public SystemHotkeys()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "PampaPad: atajos" };
        _thread.Start();
        _ready.Wait();
    }

    public event Action<int>? Pressed;

    public bool TryRegister(int id, Hotkey hotkey) =>
        OnThread(() => RegisterHotKey(IntPtr.Zero, id, HotkeyNative.Modifiers(hotkey), (uint)hotkey.VirtualKey));

    public void Unregister(int id) => OnThread(() => UnregisterHotKey(IntPtr.Zero, id));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Cerrado, el número de hilo ya no es nuestro: Windows lo puede reusar. Desde acá nadie le manda nada.
        _disposed = true;

        // Al terminar el hilo, Windows suelta las combinaciones que tenía.
        PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
    }

    /// <summary>Corre la llamada en el hilo de los atajos y espera el resultado: registrar y soltar tienen que salir del mismo hilo.</summary>
    private bool OnThread(Func<bool> call)
    {
        if (_disposed)
        {
            return false;
        }

        if (GetCurrentThreadId() == _threadId)
        {
            return call();
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() => done.SetResult(call()));
        if (!PostThreadMessage(_threadId, WmApp, IntPtr.Zero, IntPtr.Zero))
        {
            return false;
        }

        // Registrar tarda microsegundos: si pasan segundos, el hilo se cerró en el medio y no hay quien conteste.
        return done.Task.Wait(CallTimeout) && done.Task.Result;
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();

        // Pedir un mensaje crea la cola del hilo: sin esto, un PostThreadMessage temprano se pierde.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _ready.Set();
        while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Id == WmHotkey)
            {
                // Quien escucha sólo encola: el Dispatcher lanza la acción en otra tarea.
                Pressed?.Invoke((int)message.WParam);
            }
            else if (message.Id == WmApp)
            {
                while (_work.TryDequeue(out System.Action? work))
                {
                    work();
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint first, uint last, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
