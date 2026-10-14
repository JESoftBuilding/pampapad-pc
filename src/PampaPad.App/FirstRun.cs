using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// Primer arranque: pregunta dónde están los datos y guarda la variable de usuario (Decidido 7; 02 §1).
/// Si se cancela no escribe nada: el programa queda en la bandeja "sin biblioteca".
/// </summary>
internal static class FirstRun
{
    public static string? AskRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "¿Dónde están tus datos?",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return null;
        }

        string root = dialog.SelectedPath;

        // Lo que ya está abierto no ve la variable de usuario: se define también en este proceso, que es de donde la heredan los scripts.
        Environment.SetEnvironmentVariable(DataRoot.Variable, root);

        // Guardar la de usuario avisa a todas las ventanas, con un tope de 1 s cada una: va fuera del hilo de la interfaz (01 §9).
        _ = Task.Run(() => Environment.SetEnvironmentVariable(DataRoot.Variable, root, EnvironmentVariableTarget.User));
        return root;
    }

    /// <summary>Si la raíz existe y la carpeta de la biblioteca no, pide confirmación mostrando la ruta completa y la crea vacía.</summary>
    public static void OfferToCreate(string? root, string? libraryFolder)
    {
        if (!DataRoot.CanCreateLibrary(root, libraryFolder, Directory.Exists))
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            $"No existe la carpeta de la biblioteca:\n\n{libraryFolder}\n\n¿La creo, con un config.json vacío?",
            "PampaPad",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            DataRoot.CreateLibrary(libraryFolder!);
        }
    }
}
