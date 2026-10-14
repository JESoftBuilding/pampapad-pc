namespace PampaPad.App;

/// <summary>--smoke-test: arma todo sin bandeja, comprueba que el registro llega al disco y sale con 0 (05 §9).</summary>
internal static class SmokeTest
{
    public static int Run(string exampleFolder)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PampaPad-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var composition = Composition.Build(null, new SynchronizationContext(), folder, headless: true);
            composition.StartAsync().GetAwaiter().GetResult();
            composition.Log.Info("smoke", $"Biblioteca de ejemplo: {exampleFolder}. Se carga desde la etapa 3");
            composition.DisposeAsync().AsTask().GetAwaiter().GetResult();

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
