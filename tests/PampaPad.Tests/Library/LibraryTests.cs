using System.Text;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class LibraryTests : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _folder = Directory.CreateTempSubdirectory("pampapad-library-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    private string ConfigFile => Path.Combine(_folder, CoreLibrary.FileName);

    private CoreLibrary NewLibrary(string? folder = null) => new(folder ?? _folder, Path.Combine(_folder, "backup"));

    private CoreLibrary With(byte[] bytes)
    {
        File.WriteAllBytes(ConfigFile, bytes);
        return NewLibrary();
    }

    private CoreLibrary With(string json) => With(Utf8.GetBytes(json));

    [Fact]
    public void Before_loading_there_is_nothing()
    {
        CoreLibrary library = NewLibrary();

        Assert.Equal(LibraryStatus.NotLoaded, library.Status);
        Assert.Null(library.Current.Pages);
        Assert.Empty(library.Problems);
    }

    [Fact]
    public void Loads_a_file_with_byte_order_mark()
    {
        byte[] withMark = [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(Data("config.minimal.json"))];
        CoreLibrary library = With(withMark);

        IReadOnlyList<Problem> problems = library.Load();

        Assert.Empty(problems);
        Assert.Equal(LibraryStatus.Loaded, library.Status);
        Assert.Equal("grabacion", library.Current.MainPage);
        Assert.Equal(withMark, File.ReadAllBytes(ConfigFile));
    }

    [Fact]
    public void A_trailing_comma_is_unreadable_with_its_line()
    {
        string json = "{\n  \"version\": 1,\n  \"pages\": [],\n}\n";
        CoreLibrary library = With(json);

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Equal(ErrorCodes.ConfigUnreadable, problem.Code);
        Assert.Equal(Severity.Error, problem.Severity);
        Assert.Contains("línea 4", problem.Message);
        Assert.Null(library.Current.Pages);
        Assert.Equal(json, File.ReadAllText(ConfigFile));
    }

    [Fact]
    public void A_mistyped_action_breaks_only_its_key()
    {
        CoreLibrary library = With(File.ReadAllBytes(Data("config.broken.json")));

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Loaded, library.Status);
        Assert.Equal("pages[0].keys[1].action", problem.Where);
        Assert.Equal(3, library.Current.Pages![0].Keys!.Count);
        Assert.Same(library.Problems[0], problem);
    }

    [Fact]
    public void Git_conflict_markers_are_unreadable()
    {
        CoreLibrary library = With("{\n<<<<<<< HEAD\n  \"version\": 1\n=======\n  \"version\": 1,\n>>>>>>> otra\n}\n");

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Contains("línea 2", problem.Message);
    }

    [Fact]
    public void A_value_of_the_wrong_type_is_unreadable_and_names_the_place()
    {
        CoreLibrary library = With("""{ "version": 1, "pages": [ { "id": "a", "keys": [ { "position": "cinco" } ] } ] }""");

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Equal(ErrorCodes.ConfigUnreadable, problem.Code);
        Assert.Contains("pages[0].keys[0].position", problem.Message);
    }

    [Fact]
    public void A_newer_version_is_not_loaded_and_not_touched()
    {
        string json = """{ "version": 2, "pages": [ { "id": "a", "name": "A" } ] }""";
        CoreLibrary library = With(json);

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Newer, library.Status);
        Assert.Equal(ErrorCodes.ConfigNewer, problem.Code);
        Assert.Null(library.Current.Pages);
        Assert.Equal(json, File.ReadAllText(ConfigFile));
    }

    [Fact]
    public void Without_config_json_loads_empty_and_warns()
    {
        CoreLibrary library = NewLibrary();

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.Loaded, library.Status);
        Assert.Equal(ProblemCodes.ConfigMissing, problem.Code);
        Assert.Equal(Severity.Warning, problem.Severity);
        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public void A_folder_that_is_not_there_is_no_library_and_names_the_path()
    {
        string missing = Path.Combine(_folder, "no-esta");
        CoreLibrary library = NewLibrary(missing);

        Problem problem = Assert.Single(library.Load());

        Assert.Equal(LibraryStatus.NoLibrary, library.Status);
        Assert.Equal(ProblemCodes.LibraryMissing, problem.Code);
        Assert.Contains(missing, problem.Message);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Without_a_chosen_folder_there_is_no_library()
    {
        CoreLibrary library = NewLibrary("");

        Assert.Single(library.Load());
        Assert.Equal(LibraryStatus.NoLibrary, library.Status);
    }

    [Fact]
    public void Reloading_reads_the_file_again_and_raises_Changed()
    {
        CoreLibrary library = With("""{ "version": 1, "mainPage": "uno", "pages": [ { "id": "uno", "name": "Uno" } ] }""");
        var seen = new List<string?>();
        library.Changed += config => seen.Add(config.MainPage);
        library.Load();

        File.WriteAllText(ConfigFile, """{ "version": 1, "mainPage": "dos", "pages": [ { "id": "dos", "name": "Dos" } ] }""");
        library.Load();

        Assert.Equal(new[] { "uno", "dos" }, seen);
        Assert.Equal("dos", library.Current.MainPage);
    }

    [Fact]
    public void A_reload_that_fails_leaves_no_keys_active()
    {
        CoreLibrary library = With(File.ReadAllBytes(Data("config.minimal.json")));
        library.Load();

        File.WriteAllText(ConfigFile, "{ roto");
        library.Load();

        Assert.Equal(LibraryStatus.Unreadable, library.Status);
        Assert.Null(library.Current.Pages);
    }

    [Fact]
    public void The_example_library_loads_clean()
    {
        var library = new CoreLibrary(Path.Combine(AppContext.BaseDirectory, "Data", "example-library"), Path.Combine(_folder, "backup"));

        Assert.Empty(library.Load());
        Assert.Equal(10, library.Current.Pages![0].Keys!.Count);      // ir al banco, escenas, las cinco marcas, el micrófono, la pausa y grabar
        Assert.Equal(12, library.Current.Pages![1].Keys!.Count);      // la grilla de escenas (decisión 27)
        Assert.Equal(24, library.Current.Actions!.Count);

        // Cada tecla de escenas termina en su obs.scene: de ese último paso sale el rol "Escena", la tecla que la consola marca al aire (04 §2).
        Assert.All(library.Current.Pages![1].Keys!, key =>
            Assert.Equal("obs.scene", library.Current.Actions!.Single(action => action.Id == key.Action).Steps![^1].Type));

        // Grabar y las cinco marcas van en la lista general, que vale con cualquier página de base (decisión 32); en las teclas de grabacion
        // quedan los atajos de ir al banco y del micrófono.
        Assert.Equal(
            new[] { "parar", "grabar", "marca-short", "marca-inicio", "marca-fin", "marca-sacar", "marca-largo" },
            library.Current.Hotkeys!.Select(binding => binding.Action));
        Assert.Equal(
            new[] { "Ctrl+Shift+Alt+B", "Ctrl+Shift+Alt+M" },
            library.Current.Pages![0].Keys!.Where(key => key.Hotkey is not null).Select(key => key.Hotkey));
    }
}
