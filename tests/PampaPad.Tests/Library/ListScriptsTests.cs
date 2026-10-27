using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class ListScriptsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Folder => _temp.Combine("biblioteca");

    private void Script(string id, string? manifest, string? entry = "run.cmd")
    {
        Directory.CreateDirectory(Path.Combine(Folder, "scripts", id));
        if (manifest is not null)
        {
            File.WriteAllText(Path.Combine(Folder, "scripts", id, "script.json"), manifest);
        }

        if (entry is not null)
        {
            File.WriteAllText(Path.Combine(Folder, "scripts", id, entry), "@echo off\r\n");
        }
    }

    [Fact]
    public void It_lists_the_scripts_with_a_readable_ficha_in_order_of_id()
    {
        Script("zeta", """{ "entry": "run.cmd", "name": "La última" }""");
        Script("alfa", """{ "entry": "run.cmd", "name": "La primera" }""");
        Script("sin-ficha", null);
        Script("ficha-rota", "{ no es json");
        Script("sin-entry", """{ "name": "No dice qué correr" }""");
        Script("entry-que-falta", """{ "entry": "otro.cmd" }""");
        Script("Mayuscula", """{ "entry": "run.cmd" }""");
        Directory.CreateDirectory(Path.Combine(Folder, "scripts", "vacio"));
        var library = new CoreLibrary(Folder, _temp.Combine("backup"));

        IReadOnlyList<ScriptInfo> scripts = library.ListScripts();

        Assert.Equal(new[] { "alfa", "zeta" }, scripts.Select(script => script.Id));
        Assert.Equal("La primera", scripts[0].Manifest.Name);
    }

    [Fact]
    public void Without_a_library_or_without_a_scripts_folder_the_list_is_empty()
    {
        Directory.CreateDirectory(Folder);

        Assert.Empty(new CoreLibrary("", _temp.Combine("backup")).ListScripts());
        Assert.Empty(new CoreLibrary(Folder, _temp.Combine("backup")).ListScripts());
    }
}
