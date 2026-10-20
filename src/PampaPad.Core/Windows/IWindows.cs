namespace PampaPad.Core;

/// <summary>
/// Una ventana de primer nivel visible de Windows (01 §3.1). Process es el nombre del ejecutable sin ".exe". Width y Height son los de
/// GetWindowRect, la misma medida con que bancocam.ps1:20 la acomoda con SetWindowPos (01 §9). Una minimizada da otra medida: por eso
/// Minimized va aparte y nadie compara su tamaño. Elevated, que firma 01 §3.1, entra con keys.send: en la etapa 5 no lo usa nadie.
/// </summary>
public sealed record WindowInfo(string Title, string Process, int Width, int Height, bool Minimized);

/// <summary>Lo que el programa le pregunta a Windows de las ventanas y los procesos. La cumple Win32Windows; en las pruebas, FakeWindows.</summary>
public interface IWindows
{
    IReadOnlyList<WindowInfo> List();

    bool IsProcessRunning(string name);
}
