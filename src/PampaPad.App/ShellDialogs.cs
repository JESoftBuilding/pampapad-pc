using System.Diagnostics;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// El diálogo de Windows para elegir archivos y el Explorador, para los pedidos de Scripts y Ajustes (IShellDialogs). El pedido llega en
/// otro hilo: el diálogo se abre en el de la interfaz (ui.Post), encima de la ventana si está abierta. Se arma también sin ventana, por
/// la prueba de humo, que no lo abre.
/// </summary>
internal sealed class ShellDialogs(SynchronizationContext ui, Func<IWin32Window?> owner) : IShellDialogs
{
    public Task<IReadOnlyList<string>> PickFiles()
    {
        var picked = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(
            _ =>
            {
                try
                {
                    using var dialog = new OpenFileDialog
                    {
                        Title = "Importar un script",
                        Multiselect = true,
                        Filter = "Scripts (*.cmd;*.ps1;*.py;*.exe)|*.cmd;*.ps1;*.py;*.exe|Todos los archivos (*.*)|*.*",
                    };
                    DialogResult result = owner() is { } window ? dialog.ShowDialog(window) : dialog.ShowDialog();
                    picked.TrySetResult(result == DialogResult.OK ? dialog.FileNames : []);
                }
                catch (Exception error)
                {
                    picked.TrySetException(error);
                }
            },
            null);
        return picked.Task;
    }

    // Como argumento y no armado a mano: una ruta con espacios o comas llega entera al Explorador.
    public void Reveal(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
}
