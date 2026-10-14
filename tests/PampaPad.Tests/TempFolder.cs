namespace PampaPad.Tests;

/// <summary>Una carpeta temporal por prueba, que se borra al terminar.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Root);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "pampapad-tests", Guid.NewGuid().ToString("N"));

    public string Combine(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
