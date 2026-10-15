using System.Runtime.InteropServices;

namespace PampaPad.App;

/// <summary>Instancia única: un mutex con nombre y un evento con nombre. La segunda copia señala a la residente y sale (05 §1).</summary>
internal static class SingleInstance
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _signal;
    private static RegisteredWaitHandle? _wait;

    public static bool TryAcquire(string name, System.Action onOpenRequested)
    {
        var mutex = new Mutex(true, $@"Local\{name}", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting($@"Local\{name}.open", out var existing))
            {
                // La residente va a traer su ventana al frente: sin este permiso, Windows sólo haría parpadear la barra de tareas
                // (05 §1; 01 §9). ASFW_ANY, porque esta copia no sabe el número de proceso de la residente.
                _ = AllowSetForegroundWindow(AnyProcess);
                existing.Set();
                existing.Dispose();
            }

            return false;
        }

        _mutex = mutex;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.open");
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => onOpenRequested(), null, Timeout.Infinite, false);
        return true;
    }

    public static void Release()
    {
        _wait?.Unregister(null);
        _signal?.Dispose();
        _mutex?.Dispose();
    }

    private const int AnyProcess = -1;   // ASFW_ANY

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
