using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que el catálogo real necesita para las cadenas, armado para una prueba: ventanas de mentira y page.go que no hace nada.</summary>
internal static class TestChains
{
    public static Chains For(Library library, Log log, IWindows? windows = null, Func<string, Failure?>? goToPage = null, TimeProvider? clock = null) =>
        new(library, windows ?? new FakeWindows(), new ScriptRunner(library, log, clock ?? TimeProvider.System, null), goToPage ?? (_ => null));

    /// <summary>Copia scripts/ de la biblioteca de ejemplo (llega a la salida de las pruebas como Data/example-library) a otra biblioteca.</summary>
    public static void CopyExampleScripts(string libraryFolder)
    {
        string from = Path.Combine(AppContext.BaseDirectory, "Data", "example-library", "scripts");
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(libraryFolder, "scripts", Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, overwrite: true);
        }
    }
}
