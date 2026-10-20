using System.Reflection;

namespace PampaPad.Tests;

/// <summary>La barrera de dependencias de 01 §1 y el tope de 300 líneas por archivo.</summary>
public class Architecture
{
    private const int MaxLines = 300;

    private static readonly string[] ForbiddenInCore = ["System.Windows.Forms", "Microsoft.Web.WebView2", "Velopack"];

    [Fact]
    public void Core_references_no_windows_ui_and_no_velopack()
    {
        var core = Assembly.Load("PampaPad.Core");

        var offenders = core.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbiddenInCore)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("System.Windows.Forms", true)]
    [InlineData("Microsoft.Web.WebView2.Core", true)]
    [InlineData("Microsoft.Web.WebView2.WinForms", true)]
    [InlineData("Velopack", true)]
    [InlineData("System.Text.Json", false)]
    [InlineData("VelopackExtras", false)]
    public void Forbidden_assemblies_are_recognized_by_name_or_prefix(string name, bool forbidden)
    {
        Assert.Equal(forbidden, IsForbiddenInCore(name));
    }

    [Fact]
    public void Source_files_stay_within_300_lines()
    {
        var root = RepoRoot();

        var tooLong = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => (Name: Path.GetRelativePath(root, file), Lines: File.ReadLines(file).Count()))
            .Where(entry => entry.Lines > MaxLines)
            .Select(entry => $"{entry.Name}: {entry.Lines}")
            .ToArray();

        Assert.Empty(tooLong);
    }

    [Fact]
    public void Page_files_stay_within_300_lines()
    {
        var root = RepoRoot();
        var ui = Path.Combine(root, "ui");
        Assert.True(Directory.Exists(Path.Combine(ui, "src")), "Falta ui/src");

        string[] extensions = [".ts", ".tsx", ".mjs"];
        var tooLong = new[] { "src", "tests", "scripts" }
            .Select(folder => Path.Combine(ui, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.Ordinal))
            .Select(file => (Name: Path.GetRelativePath(root, file), Lines: File.ReadLines(file).Count()))
            .Where(entry => entry.Lines > MaxLines)
            .Select(entry => $"{entry.Name}: {entry.Lines}")
            .ToArray();

        Assert.Empty(tooLong);
    }

    private static bool IsForbiddenInCore(string name) =>
        ForbiddenInCore.Any(forbidden =>
            name.Equals(forbidden, StringComparison.Ordinal) ||
            name.StartsWith(forbidden + ".", StringComparison.Ordinal));

    private static bool IsBuildOutput(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }

    /// <summary>La raíz del repo, subiendo hasta PampaPad.slnx. La usan también las pruebas que leen archivos del repo (E1-6).</summary>
    internal static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "PampaPad.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName
            ?? throw new InvalidOperationException("No se encontró PampaPad.slnx subiendo desde " + AppContext.BaseDirectory);
    }
}
