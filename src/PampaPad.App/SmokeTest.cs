using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// --smoke-test: arma todo sin bandeja con la biblioteca de ejemplo, comprueba que cargue limpia y que el registro
/// llegue al disco, y sale con 0 (05 §9). Sale con 2 si la biblioteca de ejemplo no carga limpia o alguna de sus acciones tiene un problema.
/// </summary>
internal static class SmokeTest
{
    public static int Run(string exampleFolder)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PampaPad-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var composition = Composition.Build(exampleFolder, new SynchronizationContext(), folder, headless: true);
            composition.StartAsync().GetAwaiter().GetResult();
            Library library = composition.Library;

            // Los problemas del estado son los de la carga más los de los pasos (los junta el Dispatcher): con un catálogo sin obs.marker
            // la biblioteca de ejemplo cargaría limpia y ninguna de sus acciones correría.
            IReadOnlyList<Problem> problems = composition.Snapshot.Problems;
            composition.Log.Info("smoke", $"Biblioteca de ejemplo: {exampleFolder}. Estado: {library.Status}; problemas: {problems.Count}");
            composition.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (library.Status != LibraryStatus.Loaded || library.Problems.Count > 0 || problems.Count > 0)
            {
                foreach (Problem problem in problems)
                {
                    Console.Error.WriteLine($"La biblioteca de ejemplo tiene un problema en \"{problem.Where}\": {problem.Message}");
                }

                Console.Error.WriteLine($"La biblioteca de ejemplo no cargó limpia: {library.Status}");
                return 2;
            }

            var logFolder = Path.Combine(folder, "log");
            if (!Directory.Exists(logFolder) || !Directory.EnumerateFiles(logFolder, "*.jsonl").Any())
            {
                Console.Error.WriteLine("La prueba de humo no dejó registro en disco");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            TryDelete(folder);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"No se pudo borrar {folder}: {ex.Message}");
        }
    }
}
