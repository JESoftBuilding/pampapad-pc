using System.Text;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class ObsFilesProfileTests : IDisposable
{
    // La forma del perfil real (Areas/marca-personal/obs/respaldo/profile_Sin_Título_basic.ini, líneas 1-2 y 119-122).
    private const string ProfileWithHotkeys = """
        [General]
        Name=Sin Título

        [Output]
        Mode=Advanced

        [Hotkeys]
        OBSBasic.AddChapterMarker={"bindings":[{"alt":true,"control":true,"key":"OBS_KEY_F9","shift":true}]}
        OBSBasic.PauseRecording={"bindings":[{"shift":true,"control":true,"alt":true,"key":"OBS_KEY_F10"}]}
        OBSBasic.UnpauseRecording={"bindings":[{"shift":true,"control":true,"alt":true,"key":"OBS_KEY_F11"}]}
        OBSBasic.StartRecording={"bindings":[]}
        """;

    private const string ProfileWithoutHotkeys = """
        [General]
        Name=Reels vertical

        [Output]
        Mode=Simple
        """;

    private static readonly UTF8Encoding WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private void WriteProfile(string folderName, string ini)
    {
        var path = _temp.Combine("basic", "profiles", folderName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "basic.ini"), ini, WithBom);
    }

    [Fact]
    public void Reads_the_hotkeys_of_the_active_profile_by_its_name_not_its_folder()
    {
        WriteProfile("Reels_vertical", ProfileWithoutHotkeys);
        WriteProfile("Sin_Título", ProfileWithHotkeys);

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Sin Título");

        Assert.True(read.Ok);
        Assert.Equal(
            new[]
            {
                new ObsHotkey("OBSBasic.AddChapterMarker", "OBS_KEY_F9", Control: true, Shift: true, Alt: true, Command: false),
                new ObsHotkey("OBSBasic.PauseRecording", "OBS_KEY_F10", Control: true, Shift: true, Alt: true, Command: false),
                new ObsHotkey("OBSBasic.UnpauseRecording", "OBS_KEY_F11", Control: true, Shift: true, Alt: true, Command: false),
            },
            read.Value!.ToArray());
    }

    [Fact]
    public void Reads_the_profile_with_windows_line_endings()
    {
        WriteProfile("Sin_Título", ProfileWithHotkeys.Replace("\n", "\r\n"));

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Sin Título");

        Assert.True(read.Ok);
        Assert.Equal(3, read.Value!.Count);
        Assert.Equal("OBS_KEY_F9", read.Value[0].Key);
    }

    [Fact]
    public void Every_binding_of_a_hotkey_counts_and_the_command_modifier_is_kept()
    {
        WriteProfile("Dos", """
            [General]
            Name=Dos

            [Hotkeys]
            OBSBasic.StartRecording={"bindings":[{"key":"OBS_KEY_F1"},{"command":true,"key":"OBS_KEY_R","shift":true}]}
            """);

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Dos");

        Assert.True(read.Ok);
        Assert.Equal(
            new[]
            {
                new ObsHotkey("OBSBasic.StartRecording", "OBS_KEY_F1", Control: false, Shift: false, Alt: false, Command: false),
                new ObsHotkey("OBSBasic.StartRecording", "OBS_KEY_R", Control: false, Shift: true, Alt: false, Command: true),
            },
            read.Value!.ToArray());
    }

    [Fact]
    public void A_broken_hotkey_line_is_skipped_and_the_others_are_read()
    {
        WriteProfile("Rota", """
            [General]
            Name=Rota

            [Hotkeys]
            OBSBasic.A={"bindings":[{"key":"OBS_KEY_F1"
            OBSBasic.B=no es json
            OBSBasic.C={"bindings":"no es una lista"}
            OBSBasic.E={"bindings":[{"key":"\ud800"}]}
            OBSBasic.D={"bindings":[{"control":true},{"key":7},{"key":"OBS_KEY_F4","control":true}]}
            """);

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Rota");

        Assert.True(read.Ok);
        var only = Assert.Single(read.Value!);
        Assert.Equal(new ObsHotkey("OBSBasic.D", "OBS_KEY_F4", Control: true, Shift: false, Alt: false, Command: false), only);
    }

    [Fact]
    public void A_profile_without_hotkeys_gives_an_empty_list()
    {
        WriteProfile("Reels_vertical", ProfileWithoutHotkeys);

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Reels vertical");

        Assert.True(read.Ok);
        Assert.Empty(read.Value!);
    }

    [Fact]
    public void An_unknown_profile_is_a_failure()
    {
        WriteProfile("Sin_Título", ProfileWithHotkeys);

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("No existe");

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
    }

    [Fact]
    public void Without_a_profiles_folder_it_is_a_failure()
    {
        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Sin Título");

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
        Assert.Contains("profiles", read.Failure!.Where);
    }

    [Fact]
    public void A_profile_folder_without_a_readable_ini_does_not_hide_the_others()
    {
        Directory.CreateDirectory(_temp.Combine("basic", "profiles", "Vacia"));
        WriteProfile("Sin_Título", ProfileWithHotkeys);

        var read = new ObsFiles(_temp.Root).ReadProfileHotkeys("Sin Título");

        Assert.True(read.Ok);
        Assert.Equal(3, read.Value!.Count);
    }
}
