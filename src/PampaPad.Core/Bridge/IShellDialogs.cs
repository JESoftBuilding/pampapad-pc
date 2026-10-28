namespace PampaPad.Core;

/// <summary>
/// Lo de Windows que piden los pedidos de Scripts y Ajustes: el diálogo para elegir archivos y el Explorador. Lo implementa la cáscara
/// (App/ShellDialogs.cs), en el hilo de la interfaz; las pruebas usan uno de mentira.
/// </summary>
public interface IShellDialogs
{
    /// <summary>Los archivos que eligió el usuario, en el orden del diálogo. Vacía si lo canceló. Vuelve cuando se cierra el diálogo.</summary>
    Task<IReadOnlyList<string>> PickFiles();

    /// <summary>Abre esa carpeta en el Explorador. No espera a que se abra.</summary>
    void Reveal(string path);
}
