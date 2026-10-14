using System.Text.Json.Nodes;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class DataRootTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pampapad-root-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(value => value.Name == name).Select(value => value.Value).FirstOrDefault();

    [Fact]
    public void Without_the_variable_there_is_no_root()
    {
        Assert.Null(DataRoot.Read(Env()));
        Assert.Null(DataRoot.Read(Env((DataRoot.Variable, "   "))));
        Assert.Equal(@"D:\Conocimiento", DataRoot.Read(Env((DataRoot.Variable, @"D:\Conocimiento"))));
    }

    [Fact]
    public void The_installed_copy_looks_under_the_root_and_ignores_the_dev_variable()
    {
        Func<string, string?> env = Env((DataRoot.DevLibraryVariable, @"D:\otra"));

        Assert.Equal(@"D:\Conocimiento\Areas\marca-personal\pampapad", DataRoot.LibraryFolder(@"D:\Conocimiento", dev: false, env, @"C:\app"));
        Assert.Null(DataRoot.LibraryFolder(null, dev: false, env, @"C:\app"));
    }

    [Fact]
    public void The_dev_copy_uses_its_variable_or_the_example_library()
    {
        Assert.Equal(@"D:\otra", DataRoot.LibraryFolder(@"D:\Conocimiento", dev: true, Env((DataRoot.DevLibraryVariable, @"D:\otra")), @"C:\app"));
        Assert.Equal(@"C:\app\examples\library", DataRoot.LibraryFolder(null, dev: true, Env(), @"C:\app"));
    }

    [Fact]
    public void Offers_to_create_only_when_the_root_exists_and_the_library_does_not()
    {
        string library = Path.Combine(_folder, "biblioteca");

        Assert.True(DataRoot.CanCreateLibrary(_folder, library, Directory.Exists));
        Assert.False(DataRoot.CanCreateLibrary(Path.Combine(_folder, "no-esta"), library, Directory.Exists));
        Assert.False(DataRoot.CanCreateLibrary(_folder, _folder, Directory.Exists));
        Assert.False(DataRoot.CanCreateLibrary(null, null, Directory.Exists));
    }

    [Fact]
    public void A_created_library_is_empty_and_loads_clean()
    {
        string folder = Path.Combine(_folder, "biblioteca");

        DataRoot.CreateLibrary(folder);

        Assert.Equal("{\n  \"version\": 1\n}\n", File.ReadAllText(Path.Combine(folder, "config.json")));
        Assert.Equal("config.json.tmp\n", File.ReadAllText(Path.Combine(folder, ".gitignore")));
        Assert.True(Directory.Exists(Path.Combine(folder, "scripts")));
        var library = new CoreLibrary(folder, Path.Combine(_folder, "backup"));
        Assert.Empty(library.Load());
        Assert.Equal(1, library.Current.Version);
    }

    [Fact]
    public void Creating_never_overwrites_a_config_that_is_already_there()
    {
        File.WriteAllText(Path.Combine(_folder, "config.json"), "lo mío");

        DataRoot.CreateLibrary(_folder);

        Assert.Equal("lo mío", File.ReadAllText(Path.Combine(_folder, "config.json")));
    }

    [Fact]
    public void A_document_of_this_version_or_without_version_is_left_as_it_is()
    {
        JsonNode current = JsonNode.Parse("""{ "version": 1, "pages": [] }""")!;

        Assert.Same(current, Migrations.Upgrade(current, _folder).Value);
        Assert.True(Migrations.Upgrade(JsonNode.Parse("{}")!, _folder).Ok);
    }

    [Fact]
    public void A_newer_document_is_refused()
    {
        Result<JsonNode> result = Migrations.Upgrade(JsonNode.Parse("""{ "version": 2 }""")!, _folder);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ConfigNewer, result.Failure!.Code);
        Assert.Equal("version", result.Failure.Where);
    }
}
