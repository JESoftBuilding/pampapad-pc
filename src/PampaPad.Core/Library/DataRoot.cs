using System.Text;

namespace PampaPad.Core;

/// <summary>
/// Dónde vive la biblioteca: &lt;raíz&gt;\&lt;ruta relativa fija&gt; (02 §1). La raíz sale de una variable de entorno de usuario
/// (Decidido 7) y la ruta relativa se escribe sólo acá (Decidido 20).
/// </summary>
public static class DataRoot
{
    public const string Variable = "CONOCIMIENTO";
    public const string DevLibraryVariable = "PAMPAPAD_LIBRARY";
    public const string RelativePath = @"Areas\marca-personal\pampapad";

    private static readonly UTF8Encoding Utf8 = new(false);

    public static string? Read(Func<string, string?> environment)
    {
        string? root = environment(Variable);
        return string.IsNullOrWhiteSpace(root) ? null : root.Trim();
    }

    /// <summary>
    /// La carpeta de la biblioteca, o <c>null</c> si no hay raíz. La copia de desarrollo usa PAMPAPAD_LIBRARY o,
    /// si falta, la biblioteca de ejemplo que viaja con el programa; la copia instalada no mira esa variable.
    /// </summary>
    public static string? LibraryFolder(string? root, bool dev, Func<string, string?> environment, string baseDirectory)
    {
        if (dev)
        {
            string? chosen = environment(DevLibraryVariable);
            return string.IsNullOrWhiteSpace(chosen) ? Path.Combine(baseDirectory, "examples", "library") : chosen.Trim();
        }

        return root is null ? null : Path.Combine(root, RelativePath);
    }

    /// <summary>Sólo se ofrece crear la carpeta cuando la raíz existe y la biblioteca no: nunca se crea la raíz.</summary>
    public static bool CanCreateLibrary(string? root, string? libraryFolder, Func<string, bool> directoryExists) =>
        root is not null && libraryFolder is not null && directoryExists(root) && !directoryExists(libraryFolder);

    /// <summary>Crea la biblioteca vacía: config.json con la versión, scripts/ y un .gitignore para el temporal de escritura. No copia ejemplos ni pisa lo que haya.</summary>
    public static void CreateLibrary(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        WriteIfMissing(Path.Combine(folder, Library.FileName), $"{{\n  \"version\": {Migrations.CurrentVersion}\n}}\n");
        WriteIfMissing(Path.Combine(folder, ".gitignore"), "config.json.tmp\n");
    }

    private static void WriteIfMissing(string file, string content)
    {
        if (!File.Exists(file))
        {
            File.WriteAllText(file, content, Utf8);
        }
    }
}
