using System.Text;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class LocalSettingsTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly Log _log;
    private readonly string _path;

    public LocalSettingsTests()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _path = _temp.Combine("PampaPad", "local.json");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public void Without_a_file_it_loads_the_defaults_and_writes_nothing()
    {
        var settings = new LocalSettings(_path, _log);

        var result = settings.Load();

        Assert.True(result.Ok);
        Assert.Equal(LocalData.CurrentVersion, settings.Current.Version);
        Assert.Null(settings.Current.SkippedVersion);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void It_reads_a_file_with_a_byte_order_mark_and_missing_fields()
    {
        WriteFile("{ \"skippedVersion\": \"0.1.1\" }", withBom: true);
        var settings = new LocalSettings(_path, _log);

        var result = settings.Load();

        Assert.True(result.Ok);
        Assert.Equal(LocalData.CurrentVersion, settings.Current.Version);
        Assert.Equal("0.1.1", settings.Current.SkippedVersion);
    }

    [Fact]
    public async Task Saving_keeps_the_fields_this_version_does_not_know()
    {
        WriteFile("{\"version\":1,\"skippedVersion\":\"0.1.1\",\"consolePort\":\"COM7\",\"future\":{\"a\":1}}");
        var settings = new LocalSettings(_path, _log);
        settings.Load();

        var result = await settings.SaveAsync(data => data with { SkippedVersion = "0.1.2" }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("0.1.2", settings.Current.SkippedVersion);
        Assert.Equal(
            "{\n  \"version\": 1,\n  \"skippedVersion\": \"0.1.2\",\n  \"consolePort\": \"COM7\",\n  \"future\": {\n    \"a\": 1\n  }\n}\n",
            File.ReadAllText(_path));
        Assert.NotEqual((byte)0xEF, File.ReadAllBytes(_path)[0]);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public async Task Saving_writes_no_nulls_and_creates_the_folder()
    {
        var settings = new LocalSettings(_path, _log);
        settings.Load();

        var result = await settings.SaveAsync(data => data, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("{\n  \"version\": 1\n}\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_never_overwritten()
    {
        const string broken = "{ \"skippedVersion\": \"0.1.1\", }";
        WriteFile(broken);
        var settings = new LocalSettings(_path, _log);

        var loaded = settings.Load();
        var saved = await settings.SaveAsync(data => data with { SkippedVersion = "0.1.2" }, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigUnreadable, loaded.Failure?.Code);
        Assert.Equal(_path, loaded.Failure?.Where);
        Assert.Null(settings.Current.SkippedVersion);
        Assert.Equal(ErrorCodes.ConfigUnreadable, saved.Failure?.Code);
        Assert.Equal(broken, File.ReadAllText(_path));
        Assert.Contains(_log.Recent(), entry => entry.Module == "settings" && entry.Code == ErrorCodes.ConfigUnreadable);
    }

    [Fact]
    public async Task A_file_from_a_newer_program_is_neither_loaded_nor_overwritten()
    {
        const string newer = "{ \"version\": 2, \"skippedVersion\": \"9.9.9\" }";
        WriteFile(newer);
        var settings = new LocalSettings(_path, _log);

        var loaded = settings.Load();
        var saved = await settings.SaveAsync(data => data, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigNewer, loaded.Failure?.Code);
        Assert.Null(settings.Current.SkippedVersion);
        Assert.Equal(ErrorCodes.ConfigNewer, saved.Failure?.Code);
        Assert.Equal(newer, File.ReadAllText(_path));
    }

    [Fact]
    public void The_console_port_written_by_hand_is_read()
    {
        WriteFile("""{ "version": 1, "consolePort": "COM7" }""");
        var settings = new LocalSettings(_path, _log);

        var result = settings.Load();

        Assert.True(result.Ok);
        Assert.Equal("COM7", settings.Current.ConsolePort);
    }

    [Fact]
    public void Without_the_key_there_is_no_console_port()
    {
        WriteFile("""{ "version": 1 }""");
        var settings = new LocalSettings(_path, _log);

        settings.Load();

        Assert.Null(settings.Current.ConsolePort);
    }

    private void WriteFile(string text, bool withBom = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, text, new UTF8Encoding(withBom));
    }
}
