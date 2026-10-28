namespace PampaPad.Core;

/// <summary>
/// La carpeta que deja el instalador de Velopack (Medido en E0-6): el programa corre desde &lt;raíz&gt;\current\, y en la
/// raíz están Update.exe y el ejecutable de arranque, que se llama como el --packTitle de build/publish.ps1 más ".exe".
/// </summary>
public static class InstallLayout
{
    /// <summary>El --packTitle de build/publish.ps1 más ".exe". Si cambia uno solo, InstallLayoutTests frena.</summary>
    public const string LauncherName = "PampaPad.exe";

    /// <summary>El ejecutable del MCP, que la publicación deja en current junto al programa (05 §7).</summary>
    public const string McpName = "PampaPad.Mcp.exe";

    private const string ProgramFolder = "current";

    /// <summary>La ruta del ejecutable de arranque, o null si el programa no corre desde una carpeta current.</summary>
    public static string? LauncherPath(string appBaseDirectory)
    {
        var folder = appBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(folder), ProgramFolder, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = Path.GetDirectoryName(folder);
        return string.IsNullOrEmpty(root) ? null : Path.Combine(root, LauncherName);
    }

    /// <summary>El comando que registra el MCP en Claude Code, con la ruta real de la instalación; null si no corre desde current (05 §7).</summary>
    public static string? McpCommand(string appBaseDirectory) =>
        LauncherPath(appBaseDirectory) is null
            ? null
            : $"claude mcp add --scope user pampapad -- \"{Path.Combine(appBaseDirectory, McpName)}\"";
}
