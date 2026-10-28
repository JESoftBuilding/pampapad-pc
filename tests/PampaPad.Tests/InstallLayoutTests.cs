using System.Text.RegularExpressions;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La carpeta que deja el instalador de Velopack, medida en E0-6, de la que depende StartWithWindows.</summary>
public class InstallLayoutTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "PampaPad");

    [Fact]
    public void The_launcher_is_next_to_current_not_inside_it()
    {
        var current = Path.Combine(Root, "current") + Path.DirectorySeparatorChar;

        Assert.Equal(Path.Combine(Root, "PampaPad.exe"), InstallLayout.LauncherPath(current));
    }

    [Fact]
    public void Without_the_trailing_separator_the_launcher_is_the_same()
    {
        Assert.Equal(Path.Combine(Root, "PampaPad.exe"), InstallLayout.LauncherPath(Path.Combine(Root, "current")));
    }

    [Fact]
    public void A_copy_that_does_not_run_from_current_has_no_launcher()
    {
        var bin = Path.Combine(Root, "src", "PampaPad.App", "bin", "Release", "net10.0-windows") + Path.DirectorySeparatorChar;

        Assert.Null(InstallLayout.LauncherPath(bin));
    }

    [Fact]
    public void The_mcp_command_points_to_the_mcp_inside_current_and_a_copy_outside_current_has_none()
    {
        var current = Path.Combine(Root, "current") + Path.DirectorySeparatorChar;
        var bin = Path.Combine(Root, "src", "PampaPad.App", "bin", "Debug", "net10.0-windows") + Path.DirectorySeparatorChar;

        Assert.Equal($"claude mcp add --scope user pampapad -- \"{Path.Combine(Root, "current", "PampaPad.Mcp.exe")}\"", InstallLayout.McpCommand(current));
        Assert.Null(InstallLayout.McpCommand(bin));
    }

    [Fact]
    public void The_launcher_is_named_like_the_pack_title_in_publish_ps1()
    {
        var script = File.ReadAllText(Path.Combine(Architecture.RepoRoot(), "build", "publish.ps1"));
        var title = Regex.Match(script, @"^\$packTitle = '(?<title>[^']+)'", RegexOptions.Multiline);

        Assert.True(title.Success, "build/publish.ps1 no define $packTitle");
        Assert.Equal(InstallLayout.LauncherName, title.Groups["title"].Value + ".exe");
    }
}
