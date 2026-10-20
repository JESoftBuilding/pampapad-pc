using PampaPad.Core;

namespace PampaPad.App;

/// <summary>
/// --smoke-test: arma todo sin bandeja con la biblioteca de ejemplo y comprueba, en este orden, que cargue limpia (si no, sale con 2), que
/// cada nombre de pedido tenga manejador (3; 01 §3.2), que app.snapshot traiga los tipos de paso y de condición que corre el programa (5),
/// que la página esté al lado del ejecutable (4) y que el registro llegue al disco (1). Si todo está, sale con 0 (05 §9).
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
            IReadOnlyCollection<string> registered = composition.Bridge.Registered;

            // La foto por el puente del programa real, como la pide la ventana: Contract.cs arma la suya sin pasar por Composition, y un
            // ComposeBridge con [] en lugar de las condiciones compilaba con la suite entera en verde (revisión de E5-23a).
            string? snapshot = SnapshotCheck.ProblemAsync(
                composition.Bridge,
                composition.StepTypes.Select(type => type.Id),
                composition.ConditionTypes.Select(type => type.Id),
                CancellationToken.None).GetAwaiter().GetResult();
            composition.Log.Info("smoke", $"Biblioteca de ejemplo: {exampleFolder}. Estado: {library.Status}; problemas: {problems.Count}; pedidos: {registered.Count}");
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

            string[] missing = [.. RequestNames.All.Where(name => !registered.Contains(name))];
            if (missing.Length > 0)
            {
                Console.Error.WriteLine("Pedidos sin manejador: " + string.Join(", ", missing));
                return 3;
            }

            if (snapshot is not null)
            {
                Console.Error.WriteLine(snapshot);
                return 5;
            }

            string page = Path.Combine(AppContext.BaseDirectory, "ui", "index.html");
            if (!File.Exists(page))
            {
                Console.Error.WriteLine($"Falta la página de la ventana: {page}. Se arma con pnpm -C ui build antes de compilar o publicar.");
                return 4;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Hallazgo del backlog: una UnauthorizedAccessException al borrar la carpeta temporal hacía fallar el humo por otra cosa.
            Console.Error.WriteLine($"No se pudo borrar {folder}: {ex.Message}");
        }
    }
}
