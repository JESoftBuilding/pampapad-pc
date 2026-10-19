using System.Diagnostics;
using System.Text;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ScriptLaunchTests
{
    private const string Library = @"D:\Conocimiento\Areas\marca-personal\pampapad";

    private static readonly Invocation Hotkey = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.3", Gesture.Tap, 0, "ir-al-banco");

    private static ScriptInfo Script(string entry) =>
        new("bancocam", Path.Combine(Library, "scripts", "bancocam"), Path.Combine(Library, "scripts", "bancocam", entry), new ScriptManifest { Entry = entry });

    [Fact]
    public void A_cmd_runs_with_cmd_without_autorun_and_reads_in_the_oem_page()
    {
        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("bancocam.cmd"), Library, Hotkey, @"D:\Conocimiento");

        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal(new[] { "/d", "/c", Path.Combine(Library, "scripts", "bancocam", "bancocam.cmd") }, info.ArgumentList);
        Assert.Same(ScriptLaunch.Oem, info.StandardOutputEncoding);
        Assert.Same(ScriptLaunch.Oem, info.StandardErrorEncoding);
    }

    [Fact]
    public void A_ps1_runs_with_windows_powershell_like_bancocam_cmd()
    {
        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("bancocam.ps1"), Library, Hotkey, null);

        Assert.Equal("powershell.exe", info.FileName);
        Assert.Equal(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Library, "scripts", "bancocam", "bancocam.ps1") }, info.ArgumentList);
        Assert.Same(ScriptLaunch.Oem, info.StandardOutputEncoding);
    }

    [Fact]
    public void A_py_runs_with_py_3_in_utf_8_and_fixes_both_python_variables()
    {
        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("medir.py"), Library, Hotkey, null);

        Assert.Equal("py.exe", info.FileName);
        Assert.Equal("-3", info.ArgumentList[0]);
        Assert.Equal("1", info.Environment["PYTHONUTF8"]);
        Assert.Equal("utf-8", info.Environment["PYTHONIOENCODING"]);
        Assert.Equal(Encoding.UTF8.WebName, info.StandardOutputEncoding!.WebName);
    }

    [Fact]
    public void An_exe_runs_directly()
    {
        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("medir.exe"), Library, Hotkey, null);

        Assert.Equal(Path.Combine(Library, "scripts", "bancocam", "medir.exe"), info.FileName);
        Assert.Empty(info.ArgumentList);
    }

    [Fact]
    public void Every_script_runs_without_a_window_in_the_scripts_folder_and_without_input()
    {
        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("bancocam.cmd"), Library, Hotkey, null);

        Assert.True(info.CreateNoWindow);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardInput);
        Assert.Equal(Path.Combine(Library, "scripts"), info.WorkingDirectory);
    }

    [Fact]
    public void The_variables_say_what_ran_it_and_from_where()
    {
        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("bancocam.cmd"), Library, Hotkey, @"D:\Conocimiento");

        Assert.Equal(Library, info.Environment["PAMPAPAD_LIBRARY"]);
        Assert.Equal(Path.Combine(Library, "scripts", "bancocam"), info.Environment["PAMPAPAD_SCRIPT_DIR"]);
        Assert.Equal("ir-al-banco", info.Environment["PAMPAPAD_ACTION"]);
        Assert.Equal("hotkey", info.Environment["PAMPAPAD_TRIGGER"]);
        Assert.Equal("hotkeys", info.Environment["PAMPAPAD_SOURCE"]);
        Assert.Equal(@"D:\Conocimiento", info.Environment["CONOCIMIENTO"]);
    }

    [Fact]
    public void A_test_from_the_window_has_no_action_nor_source()
    {
        var test = new Invocation(Guid.NewGuid(), Trigger.Window, null, null, null, null);

        ProcessStartInfo info = ScriptLaunch.StartInfo(Script("bancocam.cmd"), Library, test, null);

        Assert.Equal("", info.Environment["PAMPAPAD_ACTION"]);
        Assert.Equal("window", info.Environment["PAMPAPAD_TRIGGER"]);
        Assert.Equal("", info.Environment["PAMPAPAD_SOURCE"]);
    }

    [Fact]
    public async Task The_output_is_cut_by_lf_without_the_cr_blank_lines_do_not_count_and_a_huge_line_is_trimmed()
    {
        var lines = new List<string>();
        var output = new ScriptOutput(lines.Add);
        string huge = new('x', ScriptOutput.MaxLine + 100);

        await output.ReadAsync(new StringReader($"uno\r\ndos\n\n   \r\n{huge}\ntres sin fin"));

        Assert.Equal(new[] { "uno", "dos", new string('x', ScriptOutput.MaxLine), "tres sin fin" }, lines);
        Assert.Equal("tres sin fin", output.Last);
        Assert.Equal(4, output.Lines);
    }
}
