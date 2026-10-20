using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PampaPad.Core;

/// <summary>
/// La lista real de ventanas, por P/Invoke a user32 (01 §1: el núcleo puede llamar a user32; no crea ventanas). Sin lógica: queda sin
/// prueba automática y se repasa a mano (01 §7). Mide con GetWindowRect, en la conciencia de DPI del proceso, que Program.cs:24 fija
/// por monitor v2: a escala 100 % bancocam mide 1823 x 850 en las cuatro conciencias (Medido: E0-4 g). Con otra escala, sin medir.
/// </summary>
public sealed partial class Win32Windows : IWindows
{
    public IReadOnlyList<WindowInfo> List()
    {
        var found = new List<WindowInfo>();
        var names = new Dictionary<uint, string>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            int length = GetWindowTextLength(handle);
            if (length == 0)
            {
                return true;
            }

            // Una ventana que se cierra mientras se la lee hace fallar estas llamadas: no se lista, en vez de entrar sin título, de 0 x 0 o
            // con el proceso 0 (revisión de E5-3).
            var title = new StringBuilder(length + 1);
            if (GetWindowText(handle, title, title.Capacity) == 0 || !GetWindowRect(handle, out Rect rect) || GetWindowThreadProcessId(handle, out uint id) == 0)
            {
                return true;
            }

            found.Add(new WindowInfo(title.ToString(), NameOf(id, names), rect.Right - rect.Left, rect.Bottom - rect.Top, IsIconic(handle)));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public bool IsProcessRunning(string name)
    {
        Process[] running = Process.GetProcessesByName(name);
        foreach (Process process in running)
        {
            process.Dispose();
        }

        return running.Length > 0;
    }

    // Un proceso que terminó entre GetWindowThreadProcessId y esta lectura queda sin nombre: la ventana se lista igual.
    private static string NameOf(uint id, Dictionary<uint, string> names)
    {
        if (names.TryGetValue(id, out string? known))
        {
            return known;
        }

        string name;
        try
        {
            using Process process = Process.GetProcessById((int)id);
            name = process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            name = "";
        }

        names[id] = name;
        return name;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
}
