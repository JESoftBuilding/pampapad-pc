using System.Text;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class ObsFilesTests : IDisposable
{
    private const string Password = "clave-de-prueba";

    private const string IniWithMarker = """
        [General]
        Name=Sin Título

        [Hotkeys]
        OBSBasic.AddChapterMarker={"bindings":[{"key":"OBS_KEY_F9"}]}
        """;

    private static readonly UTF8Encoding WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string ServerJson(int port) =>
        $$"""{"alerts_enabled":false,"auth_required":true,"first_load":false,"server_enabled":true,"server_password":"{{Password}}","server_port":{{port}}}""";

    private string ServerFile => _temp.Combine("plugin_config", "obs-websocket", "config.json");

    private void WriteServer(string json, Encoding encoding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ServerFile)!);
        File.WriteAllText(ServerFile, json, encoding);
    }

    [Fact]
    public void Reads_port_password_and_flags_of_the_obs_file()
    {
        WriteServer(ServerJson(4455), WithoutBom);

        var read = new ObsFiles(_temp.Root).ReadServer();

        Assert.True(read.Ok);
        Assert.Equal(4455, read.Value!.Port);
        Assert.Equal(Password, read.Value.Password);
        Assert.True(read.Value.Enabled);
        Assert.True(read.Value.AuthRequired);
    }

    [Fact]
    public void Reads_the_file_even_with_a_bom()
    {
        WriteServer(ServerJson(4460), WithBom);

        var read = new ObsFiles(_temp.Root).ReadServer();

        Assert.True(read.Ok);
        Assert.Equal(4460, read.Value!.Port);
    }

    [Fact]
    public void A_missing_file_is_a_failure_and_the_defaults_apply()
    {
        var files = new ObsFiles(_temp.Root);

        var read = files.ReadServer();

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
        Assert.Contains("config.json", read.Failure!.Where);
        Assert.Equal(new Uri("ws://127.0.0.1:4455"), files.Address());
        Assert.Null(files.Password());
    }

    [Fact]
    public void A_broken_file_is_a_failure_that_does_not_repeat_its_content()
    {
        WriteServer($$"""{ "server_password": "{{Password}}" esto no cierra""", WithoutBom);

        var read = new ObsFiles(_temp.Root).ReadServer();

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
        Assert.DoesNotContain(Password, read.Failure!.Message);
        Assert.DoesNotContain(Password, read.Failure!.Where);
    }

    // JsonDocument.Parse no valida el texto de las cadenas: GetString tira recién al pedirlo. Un archivo editado a mano
    // y guardado en ANSI (una ñ en la clave) o con un escape suelto llega hasta ahí.
    private void WriteServerBytes(byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ServerFile)!);
        File.WriteAllBytes(ServerFile, bytes);
    }

    private void AssertUnreadableAndHarmless()
    {
        var files = new ObsFiles(_temp.Root);

        var read = files.ReadServer();

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
        Assert.Equal(new Uri("ws://127.0.0.1:4455"), files.Address());
        Assert.Null(files.Password());
    }

    [Fact]
    public void A_password_that_is_not_valid_utf8_is_a_failure_not_an_exception()
    {
        var head = Encoding.UTF8.GetBytes("""{"server_port":4460,"server_password":"abc""");
        var tail = Encoding.UTF8.GetBytes("""def"}""");
        WriteServerBytes([.. head, 0xC3, 0x28, .. tail]);

        AssertUnreadableAndHarmless();
    }

    [Fact]
    public void A_password_with_a_lone_surrogate_escape_is_a_failure_not_an_exception()
    {
        WriteServer("""{"server_port":4460,"server_password":"\ud800"}""", WithoutBom);

        AssertUnreadableAndHarmless();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("42")]
    public void A_file_that_is_not_a_json_object_is_a_failure(string content)
    {
        WriteServer(content, WithoutBom);

        var read = new ObsFiles(_temp.Root).ReadServer();

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
    }

    [Fact]
    public void Address_and_password_are_read_again_on_every_call()
    {
        var files = new ObsFiles(_temp.Root);
        WriteServer(ServerJson(4455), WithoutBom);
        var first = files.Address();

        WriteServer(ServerJson(4460), WithoutBom);
        var second = files.Address();

        Assert.Equal(new Uri("ws://127.0.0.1:4455"), first);
        Assert.Equal(new Uri("ws://127.0.0.1:4460"), second);
        Assert.Equal(Password, files.Password());
    }

    [Theory]
    [InlineData("""{"server_enabled":true}""")]
    [InlineData("""{"server_port":"4455"}""")]
    [InlineData("""{"server_port":0}""")]
    [InlineData("""{"server_port":-1}""")]
    [InlineData("""{"server_port":65536}""")]
    [InlineData("""{"server_port":4455.5}""")]
    [InlineData("""{"server_port":99999999999}""")]
    public void A_missing_or_unusable_port_falls_back_to_the_default_and_never_throws(string content)
    {
        WriteServer(content, WithoutBom);
        var files = new ObsFiles(_temp.Root);

        var address = files.Address();

        Assert.Equal(new Uri("ws://127.0.0.1:4455"), address);
        Assert.Equal(ObsFiles.DefaultPort, files.ReadServer().Value!.Port);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void The_edges_of_the_port_range_are_valid(int port)
    {
        WriteServer(ServerJson(port), WithoutBom);

        Assert.Equal(new Uri($"ws://127.0.0.1:{port}"), new ObsFiles(_temp.Root).Address());
    }

    [Fact]
    public void ToString_never_shows_the_password()
    {
        WriteServer(ServerJson(4455), WithoutBom);

        var read = new ObsFiles(_temp.Root).ReadServer();

        Assert.DoesNotContain(Password, read.Value!.ToString());
        Assert.DoesNotContain(Password, $"{read.Value}");
    }

    [Fact]
    public void The_default_folder_is_obs_studio_inside_appdata()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");

        Assert.Equal(expected, ObsFiles.DefaultFolder);
    }

    [Fact]
    public void A_file_that_obs_is_writing_can_be_read()
    {
        WriteServer(ServerJson(4455), WithoutBom);
        using var obs = new FileStream(ServerFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        var read = new ObsFiles(_temp.Root).ReadServer();

        Assert.True(read.Ok);
        Assert.Equal(4455, read.Value!.Port);
    }

    [Fact]
    public void A_file_locked_by_someone_else_is_a_failure_not_an_exception()
    {
        WriteServer(ServerJson(4455), WithoutBom);
        using var locked = new FileStream(ServerFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var files = new ObsFiles(_temp.Root);

        var read = files.ReadServer();

        Assert.False(read.Ok);
        Assert.Equal("config_unreadable", read.Failure!.Code);
        Assert.Equal(new Uri("ws://127.0.0.1:4455"), files.Address());
        Assert.Null(files.Password());
    }

    [Fact]
    public void Reading_never_creates_or_changes_anything()
    {
        var files = new ObsFiles(_temp.Root);
        files.ReadServer();
        files.Address();
        files.Password();
        files.ReadProfileHotkeys("Sin Título");
        Assert.Empty(Directory.GetFileSystemEntries(_temp.Root));

        WriteServer(ServerJson(4455), WithBom);
        var profile = _temp.Combine("basic", "profiles", "Sin_Título");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "basic.ini"), IniWithMarker, WithBom);
        var before = Snapshot();

        files.ReadServer();
        files.Address();
        files.Password();
        files.ReadProfileHotkeys("Sin Título");

        Assert.Equal(before, Snapshot());
    }

    // Cada archivo con su contenido, para ver que leer no cambia nada.
    private string[] Snapshot() =>
        Directory.GetFiles(_temp.Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{file}|{Convert.ToHexString(File.ReadAllBytes(file))}|{File.GetLastWriteTimeUtc(file):O}")
            .ToArray();
}
