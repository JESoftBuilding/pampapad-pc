using System.Text;
using System.Text.Json;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public class ModelTests
{
    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    private static Config Read(string json) => JsonSerializer.Deserialize<Config>(json, Json.Options)!;

    // La forma canónica termina en un salto de línea, como cualquier archivo de texto del repo.
    private static string Canonical(Config config) => JsonSerializer.Serialize(config, Json.Options) + "\n";

    [Fact]
    public void Round_trip_keeps_the_same_bytes()
    {
        string original = File.ReadAllText(Data("config.minimal.json"), new UTF8Encoding(false));

        Assert.Equal(original, Canonical(Read(original)));
    }

    [Fact]
    public void The_minimal_file_keeps_an_invented_field_at_every_level()
    {
        Config config = Read(File.ReadAllText(Data("config.minimal.json")));
        Page page = config.Pages![0];
        Key key = page.Keys![0];
        PadAction action = config.Actions![0];
        Step step = action.Steps![0];

        Assert.True(config.Extra.ContainsKey("inventado"));
        Assert.True(page.Extra.ContainsKey("inventado"));
        Assert.True(key.Extra.ContainsKey("inventado"));
        Assert.True(key.OnHold!.Extra.ContainsKey("inventado"));
        Assert.True(action.Extra.ContainsKey("inventado"));
        Assert.True(step.Parameters.ContainsKey("inventado"));
        Assert.True(step.OnlyIf!.Parameters.ContainsKey("inventado"));
        Assert.True(config.Hotkeys![0].Extra.ContainsKey("inventado"));
        Assert.True(config.Preflight![0].Extra.ContainsKey("inventado"));
        Assert.True(config.ModuleKey!.Extra.ContainsKey("inventado"));
        Assert.True(config.Console!.Extra.ContainsKey("inventado"));
        Assert.True(config.Console.Screen!.Extra.ContainsKey("inventado"));
        Assert.True(config.Console.Alerts!["marker_failed"].Extra.ContainsKey("inventado"));
    }

    [Fact]
    public void An_empty_object_loads_and_writes_back_empty()
    {
        Config config = Read("{}");

        Assert.Null(config.Version);
        Assert.Null(config.Pages);
        Assert.Null(config.ResolveMainPage());
        Assert.Equal("{}\n", Canonical(config));
    }

    [Fact]
    public void What_is_missing_is_never_written_as_null_or_as_a_default()
    {
        string written = Canonical(Read("""{ "version": 1, "pages": [ { "id": "a" } ], "actions": [ { "id": "x", "steps": [ { "marker": "short" } ] } ] }"""));

        Assert.DoesNotContain("null", written);
        Assert.DoesNotContain("\"type\"", written);
        Assert.DoesNotContain("\"show\"", written);
        Assert.DoesNotContain("\"keys\"", written);
    }

    [Fact]
    public void A_step_writes_type_then_parameters_then_onlyIf()
    {
        string json = """{ "actions": [ { "steps": [ { "onlyIf": { "negate": true, "title": "bancocam", "type": "window.exists" }, "script": "bancocam", "type": "script.run" } ] } ] }""";

        string written = Canonical(Read(json));

        int type = written.IndexOf("\"type\": \"script.run\"", StringComparison.Ordinal);
        int script = written.IndexOf("\"script\"", StringComparison.Ordinal);
        int onlyIf = written.IndexOf("\"onlyIf\"", StringComparison.Ordinal);
        int title = written.IndexOf("\"title\"", StringComparison.Ordinal);
        int negate = written.IndexOf("\"negate\"", StringComparison.Ordinal);
        Assert.True(type >= 0 && type < script && script < onlyIf, written);
        Assert.True(onlyIf < title && title < negate, written);
    }

    [Fact]
    public void The_example_of_the_spec_loads_whole()
    {
        Config config = Read(File.ReadAllText(Data("config.v1.json")));

        Assert.Equal(1, config.Version);
        Assert.Equal("grabacion", config.ResolveMainPage());
        Assert.Equal(3, config.Pages!.Count);
        Assert.Equal(10, config.Pages[0].Keys!.Count);
        Assert.Equal(16, config.Actions!.Count);
        Assert.Equal(3, config.Hotkeys!.Count);
        Assert.Equal(4, config.Preflight!.Count);
        Assert.Equal("capa", config.ModuleKey!.Layer);
        Assert.Equal("parar", config.Pages[0].Keys![9].OnHold!.Action);
        Assert.Equal("Ir al banco", config.FindAction("ir-al-banco")!.Name);
        Assert.Equal("window.exists", config.FindAction("ir-al-banco")!.Steps![0].OnlyIf!.Type);
        Assert.Equal("Sala", config.FindPage("sala")!.Name);
    }

    [Fact]
    public void Without_mainPage_the_main_page_is_the_first()
    {
        Config config = Read("""{ "pages": [ { "id": "uno" }, { "id": "dos" } ] }""");

        Assert.Equal("uno", config.ResolveMainPage());
    }

    [Fact]
    public void Indexed_keeps_the_index_of_the_file_and_skips_what_is_not_there()
    {
        Config config = Read("""{ "pages": [ null, { "id": "dos" } ] }""");

        (int index, Page page) = Assert.Single(config.Pages.Indexed());
        Assert.Equal(1, index);
        Assert.Equal("dos", page.Id);
        Assert.Empty(config.Actions.Indexed());
    }
}
