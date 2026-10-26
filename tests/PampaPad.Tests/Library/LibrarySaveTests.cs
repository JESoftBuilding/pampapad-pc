using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class LibrarySaveTests : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Folder => _temp.Combine("biblioteca");

    private string ConfigFile => Path.Combine(Folder, CoreLibrary.FileName);

    private string BackupFile => _temp.Combine("backup", CoreLibrary.FileName);

    private static string Data(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", name), Utf8);

    // La carpeta existe; config.json sólo si se le da un texto. Carga como lo hace Composition.
    private CoreLibrary Open(string? json = null)
    {
        Directory.CreateDirectory(Folder);
        if (json is not null)
        {
            File.WriteAllText(ConfigFile, json, Utf8);
        }

        var library = new CoreLibrary(Folder, _temp.Combine("backup"));
        library.Load();
        return library;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // Un cambio válido: el nombre de la única página del archivo mínimo.
    private static Config Renamed(CoreLibrary library, string name) => library.Current with { Pages = [library.Current.Pages![0] with { Name = name }] };

    [Fact]
    public void The_fingerprint_is_the_sha256_of_the_bytes_that_were_read_and_empty_without_a_file()
    {
        CoreLibrary without = Open();
        CoreLibrary loaded = Open(Data("config.minimal.json"));

        Assert.Equal("", without.Fingerprint);
        Assert.Equal(Sha256(File.ReadAllBytes(ConfigFile)), loaded.Fingerprint);
        Assert.Equal((loaded.Current, loaded.Fingerprint), loaded.Latest());
    }

    [Fact]
    public async Task A_real_change_and_its_undo_leave_the_example_library_byte_for_byte_as_it_was()
    {
        string original = DispatcherRig.Example;
        CoreLibrary library = Open(original);

        Result changed = await library.SaveAsync(library.Current with { MainPage = "escenas" }, library.Fingerprint, CancellationToken.None);
        string afterChange = File.ReadAllText(ConfigFile, Utf8);
        Result undone = await library.SaveAsync(library.Current with { MainPage = "grabacion" }, library.Fingerprint, CancellationToken.None);

        Assert.True(changed.Ok, changed.Failure?.Message);
        Assert.Equal(original.Replace("\"mainPage\": \"grabacion\"", "\"mainPage\": \"escenas\"", StringComparison.Ordinal), afterChange);
        Assert.True(undone.Ok, undone.Failure?.Message);
        Assert.Equal(original, File.ReadAllText(ConfigFile, Utf8));
    }

    [Fact]
    public async Task Saving_through_the_library_keeps_the_invented_field_of_every_level()
    {
        // 02 §3, regla 1, "también cuando el guardado pasa por la ventana": sólo cambia el nombre de la página.
        string minimal = Data("config.minimal.json");
        CoreLibrary library = Open(minimal);

        Result saved = await library.SaveAsync(library.Current with { Pages = [library.Current.Pages![0] with { Name = "Otra" }] }, library.Fingerprint, CancellationToken.None);

        Assert.True(saved.Ok, saved.Failure?.Message);
        Assert.Equal(minimal.Replace("\"name\": \"Grabación\"", "\"name\": \"Otra\"", StringComparison.Ordinal), File.ReadAllText(ConfigFile, Utf8));
        Assert.Equal(13, File.ReadAllText(ConfigFile, Utf8).Split("inventado").Length - 1);
    }

    [Fact]
    public async Task Saving_what_is_already_on_disk_writes_nothing()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        DateTime before = File.GetLastWriteTimeUtc(ConfigFile);
        int changes = 0;
        library.Changed += _ => changes++;

        Result saved = await library.SaveAsync(library.Current, library.Fingerprint, CancellationToken.None);

        Assert.True(saved.Ok);
        Assert.Equal(before, File.GetLastWriteTimeUtc(ConfigFile));
        Assert.False(File.Exists(BackupFile));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_stale_fingerprint_does_not_write_and_says_it_changed_on_disk()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        string stale = library.Fingerprint;
        File.WriteAllText(ConfigFile, "{ \"version\": 1, \"mainPage\": \"otra-copia\" }\n", Utf8);

        Result saved = await library.SaveAsync(Renamed(library, "Otra"), stale, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, saved.Failure?.Code);
        Assert.Equal("{ \"version\": 1, \"mainPage\": \"otra-copia\" }\n", File.ReadAllText(ConfigFile, Utf8));
        Assert.False(File.Exists(BackupFile));
        Assert.False(File.Exists(ConfigFile + ".tmp"));
    }

    [Fact]
    public async Task The_first_save_creates_the_file_without_a_backup()
    {
        CoreLibrary library = Open();

        Result saved = await library.SaveAsync(new Config { Version = 1 }, "", CancellationToken.None);

        Assert.True(saved.Ok, saved.Failure?.Message);
        Assert.Equal("{\n  \"version\": 1\n}\n", File.ReadAllText(ConfigFile, Utf8));
        Assert.Equal(Sha256(File.ReadAllBytes(ConfigFile)), library.Fingerprint);
        Assert.Equal(LibraryStatus.Loaded, library.Status);
        Assert.False(File.Exists(BackupFile));
        Assert.False(File.Exists(ConfigFile + ".tmp"));
    }

    [Fact]
    public async Task The_backup_is_the_generation_before_the_last_save_and_only_one()
    {
        string original = Data("config.minimal.json");
        CoreLibrary library = Open(original);

        await library.SaveAsync(Renamed(library, "Una"), library.Fingerprint, CancellationToken.None);
        string first = File.ReadAllText(ConfigFile, Utf8);
        Assert.Equal(original, File.ReadAllText(BackupFile, Utf8));

        await library.SaveAsync(Renamed(library, "Otra"), library.Fingerprint, CancellationToken.None);

        Assert.Equal(first, File.ReadAllText(BackupFile, Utf8));
        Assert.Single(Directory.GetFiles(_temp.Combine("backup")));
    }

    [Fact]
    public async Task A_problem_of_the_error_kind_does_not_write_and_comes_back_with_its_list()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        string before = File.ReadAllText(ConfigFile, Utf8);
        Config repeated = library.Current with { Actions = [.. library.Current.Actions!, library.Current.Actions![0]] };

        Result saved = await library.SaveAsync(repeated, library.Fingerprint, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigInvalid, saved.Failure?.Code);
        Assert.Equal("actions[2].id", saved.Failure?.Where);
        Assert.Contains(saved.Failure!.Problems!, problem => problem.Where == "actions[2].id" && problem.Code == ProblemCodes.IdRepeated);
        Assert.Equal(before, File.ReadAllText(ConfigFile, Utf8));
        Assert.False(File.Exists(BackupFile));
    }

    [Fact]
    public async Task A_problem_the_caller_found_in_the_steps_also_stops_the_save()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        var found = new Problem("actions[0].steps[0].target", ProblemCodes.PageNotFound, "No existe.", Severity.Error);

        Result saved = await library.SaveAsync(library.Current, library.Fingerprint, [found], CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigInvalid, saved.Failure?.Code);
        Assert.Equal("actions[0].steps[0].target", saved.Failure?.Where);
    }

    [Fact]
    public async Task A_warning_is_saved_and_stays_in_the_problems_of_the_library()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        Key longLabel = library.Current.Pages![0].Keys![0] with { ShortLabel = ["DEMASIADO"] };

        Result saved = await library.SaveAsync(
            library.Current with { Pages = [library.Current.Pages[0] with { Keys = [longLabel] }] }, library.Fingerprint, CancellationToken.None);

        Assert.True(saved.Ok, saved.Failure?.Message);
        Assert.Contains(library.Problems, problem => problem.Code == ProblemCodes.ShortLabelInvalid && problem.Severity == Severity.Warning);
        Assert.Contains("DEMASIADO", File.ReadAllText(ConfigFile, Utf8), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_and_the_event_have_the_new_configuration_when_the_save_returns()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        string? nameSeen = null;
        string? fingerprintSeen = null;
        library.Changed += config =>
        {
            nameSeen = config.Pages![0].Name;
            fingerprintSeen = library.Fingerprint;
        };

        await library.SaveAsync(Renamed(library, "Otra"), library.Fingerprint, CancellationToken.None);

        Assert.Equal("Otra", nameSeen);
        Assert.Equal("Otra", library.Current.Pages![0].Name);
        Assert.Equal(library.Fingerprint, fingerprintSeen);
        Assert.Equal(Sha256(File.ReadAllBytes(ConfigFile)), library.Fingerprint);
    }

    [Fact]
    public async Task Two_saves_from_the_same_fingerprint_leave_exactly_one_winner()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        string fingerprint = library.Fingerprint;

        Result[] results = await Task.WhenAll(
            library.SaveAsync(Renamed(library, "Una"), fingerprint, CancellationToken.None),
            library.SaveAsync(Renamed(library, "Otra"), fingerprint, CancellationToken.None));

        Assert.Single(results, result => result.Ok);
        Assert.Single(results, result => result.Failure?.Code == ErrorCodes.ConfigChangedOnDisk);
    }

    [Fact]
    public async Task A_leftover_temporary_does_not_get_in_the_way_and_is_gone_afterwards()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        File.WriteAllText(ConfigFile + ".tmp", "basura de un guardado que se cortó", Utf8);

        Result saved = await library.SaveAsync(Renamed(library, "Otra"), library.Fingerprint, CancellationToken.None);

        Assert.True(saved.Ok, saved.Failure?.Message);
        Assert.False(File.Exists(ConfigFile + ".tmp"));
    }

    [Fact]
    public async Task A_file_with_a_mark_and_with_crlf_is_written_back_as_canonical_utf8_with_lf()
    {
        Directory.CreateDirectory(Folder);
        byte[] crlf = [.. Encoding.UTF8.Preamble, .. Utf8.GetBytes("{\r\n  \"version\": 1,\r\n  \"actions\": []\r\n}\r\n")];
        File.WriteAllBytes(ConfigFile, crlf);
        var library = new CoreLibrary(Folder, _temp.Combine("backup"));
        library.Load();
        string before = library.Fingerprint;

        Result saved = await library.SaveAsync(library.Current, before, CancellationToken.None);

        Assert.True(saved.Ok, saved.Failure?.Message);
        Assert.Equal("{\n  \"version\": 1,\n  \"actions\": []\n}\n", File.ReadAllText(ConfigFile, Utf8));
        Assert.NotEqual(before, library.Fingerprint);
        Assert.NotEqual(Encoding.UTF8.Preamble[0], File.ReadAllBytes(ConfigFile)[0]);
    }

    [Fact]
    public async Task An_unreadable_file_is_never_overwritten()
    {
        CoreLibrary library = Open("{ no es json");

        Result saved = await library.SaveAsync(new Config { Version = 1 }, library.Fingerprint, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigUnreadable, saved.Failure?.Code);
        Assert.Equal("{ no es json", File.ReadAllText(ConfigFile, Utf8));
    }

    [Fact]
    public async Task A_newer_file_is_never_overwritten()
    {
        CoreLibrary library = Open("{ \"version\": 2 }\n");

        Result saved = await library.SaveAsync(new Config { Version = 1 }, library.Fingerprint, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigNewer, saved.Failure?.Code);
        Assert.Equal("{ \"version\": 2 }\n", File.ReadAllText(ConfigFile, Utf8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_library_there_is_nowhere_to_save(bool loaded)
    {
        var library = new CoreLibrary("", _temp.Combine("backup"));
        if (loaded)
        {
            library.Load();
        }

        Result saved = await library.SaveAsync(new Config { Version = 1 }, "", CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigInvalid, saved.Failure?.Code);
    }

    [Fact]
    public async Task A_file_another_program_holds_open_is_reported_and_left_as_it_was()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        string before = File.ReadAllText(ConfigFile, Utf8);
        Result saved;
        using (new FileStream(ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saved = await library.SaveAsync(Renamed(library, "Otra"), library.Fingerprint, CancellationToken.None);
        }

        Assert.False(saved.Ok);
        Assert.Equal(before, File.ReadAllText(ConfigFile, Utf8));
        Assert.False(File.Exists(ConfigFile + ".tmp"));
    }

    [Fact]
    public async Task A_save_that_was_cancelled_before_it_started_writes_nothing()
    {
        CoreLibrary library = Open(Data("config.minimal.json"));
        string before = File.ReadAllText(ConfigFile, Utf8);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Result saved = await library.SaveAsync(Renamed(library, "Otra"), library.Fingerprint, cancelled.Token);

        Assert.Equal(ErrorCodes.Cancelled, saved.Failure?.Code);
        Assert.Equal(before, File.ReadAllText(ConfigFile, Utf8));
    }
}
