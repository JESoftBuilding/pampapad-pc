using System.Text;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class ScriptFolderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // Una carpeta scripts/<id>/ con su ficha y sus archivos.
    private string Script(string id, string? manifest, params string[] files)
    {
        string folder = _temp.Combine("biblioteca", "scripts", id);
        Directory.CreateDirectory(folder);
        if (manifest is not null)
        {
            File.WriteAllText(Path.Combine(folder, "script.json"), manifest);
        }

        foreach (string file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), "@echo off\n");
        }

        return folder;
    }

    private Result<ScriptInfo> Read(string id) => ScriptFolder.Read(_temp.Combine("biblioteca"), id);

    [Fact]
    public void A_folder_with_its_manifest_is_a_script_and_what_is_missing_stays_missing()
    {
        string folder = Script("bancocam", """{ "entry": "bancocam.cmd", "name": "Cámara del banco", "timeoutSec": 30 }""", "bancocam.cmd", "bancocam.ps1");

        Result<ScriptInfo> found = Read("bancocam");

        Assert.True(found.Ok);
        Assert.Equal("bancocam", found.Value!.Id);
        Assert.Equal(folder, found.Value.Folder);
        Assert.Equal(Path.Combine(folder, "bancocam.cmd"), found.Value.EntryPath);
        Assert.Equal("Cámara del banco", found.Value.Manifest.Name);
        Assert.Equal(30, found.Value.Manifest.TimeoutSec);
        Assert.Null(found.Value.Manifest.Wait);              // se resuelve al correr: 02 §3, regla 3
        Assert.Null(found.Value.Manifest.Contract);
    }

    [Fact]
    public void A_manifest_with_a_bom_and_unknown_fields_is_read_and_keeps_them()
    {
        string folder = Script("hola", null, "hola.ps1");
        File.WriteAllBytes(Path.Combine(folder, "script.json"), [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("""{ "entry": "hola.ps1", "inventado": 1 }""")]);

        Result<ScriptInfo> found = Read("hola");

        Assert.True(found.Ok);
        Assert.True(found.Value!.Manifest.Extra.ContainsKey("inventado"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../config")]
    [InlineData("Bancocam")]
    [InlineData("banco cam")]
    [InlineData("bancocam.cmd")]
    public void An_id_that_is_not_an_id_is_not_found_and_never_leaves_the_folder(string id)
    {
        Assert.Equal(ErrorCodes.ScriptNotFound, Read(id).Failure?.Code);
    }

    [Fact]
    public void A_missing_folder_is_not_found()
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca", "scripts"));

        Result<ScriptInfo> found = Read("bancocam");

        Assert.Equal(ErrorCodes.ScriptNotFound, found.Failure?.Code);
        Assert.Equal("No existe el script \"bancocam\": falta la carpeta scripts\\bancocam en la biblioteca.", found.Failure?.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ roto")]
    [InlineData("""{ "name": "Sin entry" }""")]
    [InlineData("""{ "entry": "" }""")]
    [InlineData("""{ "entry": 5 }""")]
    public void Without_a_readable_manifest_with_an_entry_it_is_a_script_without_manifest(string? manifest)
    {
        Script("bancocam", manifest, "bancocam.cmd");

        Assert.Equal(ErrorCodes.ScriptWithoutManifest, Read("bancocam").Failure?.Code);
    }

    [Theory]
    [InlineData("..\\\\config.json")]
    [InlineData("../config.json")]
    [InlineData("C:\\\\Windows\\\\System32\\\\cmd.exe")]
    [InlineData("otro.cmd")]
    public void An_entry_outside_the_folder_or_that_does_not_exist_is_not_found(string entry)
    {
        Script("bancocam", $$"""{ "entry": "{{entry}}" }""", "bancocam.cmd");
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), "{}");

        Assert.Equal(ErrorCodes.ScriptNotFound, Read("bancocam").Failure?.Code);
    }

    [Fact]
    public void An_extension_the_contract_does_not_run_fails()
    {
        Script("viejo", """{ "entry": "viejo.bat" }""", "viejo.bat");

        Result<ScriptInfo> found = Read("viejo");

        Assert.Equal(ErrorCodes.ScriptFailed, found.Failure?.Code);
        Assert.Contains(".cmd, .ps1, .py o .exe", found.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_contract_fails()
    {
        Script("nuevo", """{ "entry": "nuevo.cmd", "contract": 2 }""", "nuevo.cmd");

        Assert.Equal(ErrorCodes.ScriptFailed, Read("nuevo").Failure?.Code);
    }

    [Fact]
    public void Without_a_library_nothing_is_searched()
    {
        Assert.Equal(ErrorCodes.ScriptNotFound, ScriptFolder.Read("", "bancocam").Failure?.Code);
        Assert.Equal(ErrorCodes.ScriptNotFound, new CoreLibrary("", _temp.Combine("backup")).ReadScript("bancocam").Failure?.Code);
    }

    [Fact]
    public void The_library_reads_the_disk_each_time()
    {
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        Directory.CreateDirectory(_temp.Combine("biblioteca"));

        Result<ScriptInfo> before = library.ReadScript("hola");
        Script("hola", """{ "entry": "hola.cmd" }""", "hola.cmd");
        Result<ScriptInfo> after = library.ReadScript("hola");

        Assert.False(before.Ok);
        Assert.True(after.Ok);
    }
}
