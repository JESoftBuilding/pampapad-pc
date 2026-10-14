using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>Lo que las pruebas del plan de E3-3 no fijan de <see cref="DataRoot"/>: espacios, la copia de desarrollo con raíz y los archivos que no se pisan.</summary>
public sealed class DataRootEdgeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pampapad-root-edge-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(value => value.Name == name).Select(value => value.Value).FirstOrDefault();

    [Fact]
    public void The_root_and_the_dev_library_are_trimmed()
    {
        Assert.Equal(@"D:\Conocimiento", DataRoot.Read(Env((DataRoot.Variable, "  D:\\Conocimiento \n"))));
        Assert.Equal(@"D:\otra", DataRoot.LibraryFolder(null, dev: true, Env((DataRoot.DevLibraryVariable, " D:\\otra ")), @"C:\app"));
    }

    [Fact]
    public void A_blank_dev_variable_falls_back_to_the_example_library()
    {
        Assert.Equal(@"C:\app\examples\library", DataRoot.LibraryFolder(null, dev: true, Env((DataRoot.DevLibraryVariable, "   ")), @"C:\app"));
    }

    [Fact]
    public void The_dev_copy_never_falls_back_to_the_real_library_even_with_a_root()
    {
        // En Debug, con CONOCIMIENTO puesta y sin PAMPAPAD_LIBRARY, no se toca la biblioteca de verdad: es la de ejemplo.
        Assert.Equal(@"C:\app\examples\library", DataRoot.LibraryFolder(@"D:\Conocimiento", dev: true, Env(), @"C:\app"));
    }

    [Fact]
    public void The_relative_path_is_the_decided_one()
    {
        Assert.Equal(@"Areas\marca-personal\pampapad", DataRoot.RelativePath);
        Assert.Equal("CONOCIMIENTO", DataRoot.Variable);
        Assert.Equal("PAMPAPAD_LIBRARY", DataRoot.DevLibraryVariable);
    }

    [Fact]
    public void Without_a_library_folder_nothing_is_offered_even_if_the_root_exists()
    {
        Assert.False(DataRoot.CanCreateLibrary(_folder, null, Directory.Exists));
        Assert.False(DataRoot.CanCreateLibrary(null, Path.Combine(_folder, "biblioteca"), Directory.Exists));
    }

    [Fact]
    public void Without_a_root_nothing_is_offered_whatever_the_disk_says()
    {
        string library = Path.Combine(_folder, "biblioteca");

        // Un disco que dice que todo existe salvo la biblioteca: sin raíz, igual no se ofrece crear nada.
        Assert.False(DataRoot.CanCreateLibrary(null, library, path => path != library));
    }

    [Fact]
    public void Offering_asks_the_two_questions_it_needs_and_nothing_else()
    {
        string library = Path.Combine(_folder, "biblioteca");
        var asked = new List<string>();

        Assert.True(DataRoot.CanCreateLibrary(_folder, library, path =>
        {
            asked.Add(path);
            return Directory.Exists(path);
        }));

        Assert.Equal([_folder, library], asked);
    }

    [Fact]
    public void A_created_library_has_no_byte_order_mark_and_uses_line_feed()
    {
        string folder = Path.Combine(_folder, "biblioteca");

        DataRoot.CreateLibrary(folder);

        Assert.Equal("{\n  \"version\": 1\n}\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(folder, "config.json")));
        Assert.Equal("config.json.tmp\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(folder, ".gitignore")));
    }

    [Fact]
    public void Creating_never_overwrites_a_gitignore_that_is_already_there()
    {
        File.WriteAllText(Path.Combine(_folder, ".gitignore"), "lo mío\n");

        DataRoot.CreateLibrary(_folder);

        Assert.Equal("lo mío\n", File.ReadAllText(Path.Combine(_folder, ".gitignore")));
        Assert.True(File.Exists(Path.Combine(_folder, "config.json")));
        Assert.True(Directory.Exists(Path.Combine(_folder, "scripts")));
    }

    [Fact]
    public void Creating_twice_changes_nothing_the_second_time()
    {
        string folder = Path.Combine(_folder, "biblioteca");
        DataRoot.CreateLibrary(folder);
        File.WriteAllText(Path.Combine(folder, "config.json"), """{ "version": 1, "pages": [] }""");

        DataRoot.CreateLibrary(folder);

        Assert.Equal("""{ "version": 1, "pages": [] }""", File.ReadAllText(Path.Combine(folder, "config.json")));
    }

    [Fact]
    public void A_created_library_is_what_the_program_would_not_offer_to_create_again()
    {
        string folder = Path.Combine(_folder, "biblioteca");
        Assert.True(DataRoot.CanCreateLibrary(_folder, folder, Directory.Exists));

        DataRoot.CreateLibrary(folder);

        Assert.False(DataRoot.CanCreateLibrary(_folder, folder, Directory.Exists));
        Assert.Equal(LibraryStatus.Loaded, Load(folder));
    }

    private LibraryStatus Load(string folder)
    {
        var library = new CoreLibrary(folder, Path.Combine(_folder, "backup"));
        library.Load();
        return library.Status;
    }

    [Theory]
    [InlineData("""{ "version": 1 }""", true)]
    [InlineData("""{ "version": 2 }""", false)]
    [InlineData("""{ "version": 99 }""", false)]
    [InlineData("""{ "pages": [] }""", true)]
    public void Upgrade_only_refuses_a_version_above_the_current_one(string json, bool passes)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(json)!;

        Assert.Equal(passes, Migrations.Upgrade(document, _folder).Ok);
        Assert.Equal(1, Migrations.CurrentVersion);
    }
}
