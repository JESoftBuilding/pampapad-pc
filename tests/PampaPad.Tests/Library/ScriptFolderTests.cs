using System.Text;
using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class ScriptFolderTests : IDisposable
{
    // El entry que apunta, con una ruta absoluta, a un .cmd de la carpeta temporal, que no se conoce hasta correr la prueba.
    private const string Absolute = "(absoluta)";

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
        // contract 1 es el que conoce el programa, el del ejemplo de 02 §9 y el de bancocam: se corre.
        string folder = Script("hola", null, "hola.ps1");
        File.WriteAllBytes(Path.Combine(folder, "script.json"), [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("""{ "entry": "hola.ps1", "contract": 1, "inventado": 1 }""")]);

        Result<ScriptInfo> found = Read("hola");

        Assert.True(found.Ok);
        Assert.Equal(1, found.Value!.Manifest.Contract);
        Assert.True(found.Value.Manifest.Extra.ContainsKey("inventado"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../config")]
    [InlineData("Bancocam")]
    [InlineData("banco cam")]
    [InlineData("bancocam.cmd")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]   // 41: el tope es 40 (02 §4.2)
    public void An_id_that_is_not_an_id_is_not_found_and_never_leaves_the_folder(string id)
    {
        // Lo que encontraría el id si no se lo revisara: ".." es la biblioteca misma, "../config" una carpeta al lado de scripts/ y
        // "Bancocam" la carpeta bancocam, porque NTFS no distingue mayúsculas.
        Script(id, """{ "entry": "hola.cmd" }""", "hola.cmd");

        Result<ScriptInfo> found = Read(id);

        Assert.Equal(ErrorCodes.ScriptNotFound, found.Failure?.Code);
        Assert.StartsWith($"\"{id}\" no es un id de script", found.Failure?.Message, StringComparison.Ordinal);
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
    [InlineData(null, "no tiene ficha: falta scripts\\bancocam\\script.json.")]
    [InlineData("{ roto", "no se puede leer: error en la línea 1, columna 3.")]
    [InlineData("""{ "name": "Sin entry" }""", "falta \"entry\".")]
    [InlineData("""{ "entry": "" }""", "falta \"entry\".")]
    [InlineData("""{ "entry": 5 }""", "($.entry).")]
    public void Without_a_readable_manifest_with_an_entry_it_is_a_script_without_manifest(string? manifest, string says)
    {
        Script("bancocam", manifest, "bancocam.cmd");

        Result<ScriptInfo> found = Read("bancocam");

        // Lo que no se puede leer dice dónde, como config.json: la línea y la columna contadas desde 1, o el campo del tipo equivocado.
        Assert.Equal(ErrorCodes.ScriptWithoutManifest, found.Failure?.Code);
        Assert.EndsWith(says, found.Failure?.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"..\afuera.cmd", "sale de su carpeta")]
    [InlineData("../afuera.cmd", "sale de su carpeta")]
    [InlineData(@"..\otro\otro.cmd", "sale de su carpeta")]
    [InlineData(@"..\..\afuera.cmd", "sale de su carpeta")]
    [InlineData(Absolute, "sale de su carpeta")]
    [InlineData("NUL", "no queda dentro de scripts\\bancocam")]   // Windows lo resuelve a \\.\NUL: lo ataja la ruta ya resuelta
    [InlineData("otro.cmd", "No existe el archivo \"otro.cmd\"")]
    public void An_entry_outside_the_folder_or_that_does_not_exist_is_not_found(string entry, string says)
    {
        // Cada salida apunta a un .cmd que existe: si no se la rechazara, el script se encontraría y se podría correr.
        Script("bancocam", null, "bancocam.cmd");
        Script("otro", null, "otro.cmd");
        File.WriteAllText(_temp.Combine("biblioteca", "scripts", "afuera.cmd"), "@echo off\n");
        File.WriteAllText(_temp.Combine("biblioteca", "afuera.cmd"), "@echo off\n");
        File.WriteAllText(_temp.Combine("afuera.cmd"), "@echo off\n");
        string written = entry == Absolute ? _temp.Combine("afuera.cmd") : entry;
        File.WriteAllText(_temp.Combine("biblioteca", "scripts", "bancocam", "script.json"), JsonSerializer.Serialize(new { entry = written }));

        Result<ScriptInfo> found = Read("bancocam");

        Assert.Equal(ErrorCodes.ScriptNotFound, found.Failure?.Code);
        Assert.Contains(says, found.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_that_is_not_a_path_is_not_found()
    {
        // Un carácter nulo y 40.000 caracteres son texto válido en JSON, pero no una ruta: Path.GetFullPath tira con los dos.
        foreach (string entry in new[] { "hola\0.cmd", new string('a', 40_000) + ".cmd" })
        {
            Script("hola", JsonSerializer.Serialize(new { entry }), "hola.cmd");

            Result<ScriptInfo> found = Read("hola");

            Assert.Equal(ErrorCodes.ScriptNotFound, found.Failure?.Code);
            Assert.Equal("El entry del script \"hola\" no es una ruta de archivo: va el nombre de un archivo de scripts\\hola.", found.Failure?.Message);
        }
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
        // Sin la guarda se buscaría scripts\bancocam en la carpeta desde la que se lanzó el programa (Composition, sin CONOCIMIENTO).
        const string Message = "No hay biblioteca: el script \"bancocam\" no se puede buscar.";

        Assert.Equal(Message, ScriptFolder.Read("", "bancocam").Failure?.Message);
        Assert.Equal(Message, new CoreLibrary("", _temp.Combine("backup")).ReadScript("bancocam").Failure?.Message);
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
