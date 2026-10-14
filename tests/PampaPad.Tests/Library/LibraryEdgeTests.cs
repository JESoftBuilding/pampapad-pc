using System.Text;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// Lo que las pruebas del plan de E3-3 no fijan: el borde de cada rama de <c>Library.Load</c>. Una mutación a mano
/// de cada rama sobrevivía a <see cref="LibraryTests"/>; cada caso de acá cierra una.
/// </summary>
public sealed class LibraryEdgeTests : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _folder = Directory.CreateTempSubdirectory("pampapad-library-edge-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string ConfigFile => Path.Combine(_folder, CoreLibrary.FileName);

    private CoreLibrary With(string json)
    {
        File.WriteAllBytes(ConfigFile, Utf8.GetBytes(json));
        return new CoreLibrary(_folder, Path.Combine(_folder, "backup"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("\"hola\"")]
    public void A_document_that_is_not_an_object_is_unreadable(string json)
    {
        CoreLibrary library = With(json);

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Equal(ErrorCodes.ConfigUnreadable, problem.Code);
        Assert.Contains("tiene que ser un objeto", problem.Message);
        Assert.Null(library.Current.Pages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void An_empty_file_is_unreadable_and_not_a_crash(string json)
    {
        CoreLibrary library = With(json);

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Equal(ErrorCodes.ConfigUnreadable, problem.Code);
        Assert.Equal(json, File.ReadAllText(ConfigFile));
    }

    [Fact]
    public void A_syntax_error_names_line_and_column()
    {
        CoreLibrary library = With("{\n  \"version\": 1,\n  \"pages\": [],\n}\n");

        Problem problem = Assert.Single(library.Load());

        Assert.Contains("línea 4, columna 1", problem.Message);
        Assert.Equal("", problem.Where);
    }

    [Fact]
    public void A_repeated_key_is_unreadable_and_not_a_crash()
    {
        CoreLibrary library = With("""{ "version": 1, "version": 1 }""");

        Problem problem = Assert.Single(library.Load());

        // .NET rechaza la clave repetida al recorrer el objeto, con su propio mensaje: el programa no tira y la configuración queda vacía.
        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Equal(ErrorCodes.ConfigUnreadable, problem.Code);
        Assert.StartsWith("config.json no se puede leer: ", problem.Message);
        Assert.Null(library.Current.Pages);
    }

    [Fact]
    public void A_newer_version_wins_over_a_shape_this_program_does_not_know()
    {
        // Un esquema nuevo puede cambiar la forma de cualquier campo: se frena por la versión, antes de deserializar.
        CoreLibrary library = With("""{ "version": 2, "pages": 5, "actions": { "uno": 1 } }""");

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Newer, library.Status);
        Assert.Equal(ErrorCodes.ConfigNewer, problem.Code);
        Assert.Equal("version", problem.Where);
        Assert.Equal(Severity.Error, problem.Severity);
        Assert.Contains("versión 2", problem.Message);
    }

    [Fact]
    public void A_file_held_by_another_program_is_unreadable_and_untouched()
    {
        CoreLibrary library = With("""{ "version": 1 }""");

        using (new FileStream(ConfigFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Problem problem = Assert.Single(library.Load());

            Assert.Equal(LibraryStatus.Unreadable, library.Status);
            Assert.Equal(ErrorCodes.ConfigUnreadable, problem.Code);
            Assert.Contains("no se puede abrir", problem.Message);
        }

        Assert.Equal("""{ "version": 1 }""", File.ReadAllText(ConfigFile));
        Assert.Empty(library.Load());
        Assert.Equal(LibraryStatus.Loaded, library.Status);
    }

    [Fact]
    public void An_empty_folder_name_says_there_is_no_folder_chosen_and_not_a_path()
    {
        var library = new CoreLibrary("", Path.Combine(_folder, "backup"));

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(ProblemCodes.LibraryMissing, problem.Code);
        Assert.Equal(Severity.Error, problem.Severity);
        Assert.Contains("No hay carpeta de datos elegida", problem.Message);
    }

    [Fact]
    public void Changed_is_raised_also_when_the_load_fails_and_carries_the_empty_config()
    {
        CoreLibrary library = With("""{ "version": 1, "mainPage": "uno", "pages": [ { "id": "uno", "name": "Uno" } ] }""");
        var seen = new List<(string? MainPage, bool HasPages)>();
        library.Changed += config => seen.Add((config.MainPage, config.Pages is not null));
        library.Load();

        File.WriteAllText(ConfigFile, "{ roto");
        library.Load();

        Assert.Equal([("uno", true), (null, false)], seen);
    }

    [Fact]
    public void The_problems_of_a_failed_reload_replace_the_old_ones()
    {
        CoreLibrary library = With("""{ "version": 1, "pages": [ { "id": "uno" } ] }""");
        Assert.NotEmpty(library.Load());

        File.WriteAllText(ConfigFile, """{ "version": 1 }""");

        Assert.Empty(library.Load());
        Assert.Empty(library.Problems);
        Assert.Equal(LibraryStatus.Loaded, library.Status);
    }

    [Fact]
    public void Folder_and_backup_folder_are_what_the_constructor_got()
    {
        var library = new CoreLibrary(@"D:\datos\pampapad", @"C:\copia");

        Assert.Equal(@"D:\datos\pampapad", library.Folder);
        Assert.Equal(@"C:\copia", library.BackupFolder);
    }

    [Fact]
    public void Loading_never_writes_anything_in_the_folder()
    {
        CoreLibrary library = With("""{ "version": 1, "pages": [ { "id": "uno", "name": "Uno" } ] }""");
        string[] before = Directory.GetFileSystemEntries(_folder, "*", SearchOption.AllDirectories);
        DateTime stamp = File.GetLastWriteTimeUtc(ConfigFile);

        library.Load();
        library.Load();

        Assert.Equal(before, Directory.GetFileSystemEntries(_folder, "*", SearchOption.AllDirectories));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(ConfigFile));
    }

    [Fact]
    public void The_example_library_is_in_canonical_form()
    {
        // Es lo que escribiría el programa: el día que el guardado exista, abrir y guardar la biblioteca de ejemplo no la cambia.
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "example-library", "config.json");
        string original = File.ReadAllText(path, Utf8);
        Config config = System.Text.Json.JsonSerializer.Deserialize<Config>(original, Json.Options)!;

        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(config, Json.Options) + "\n");
    }
}
