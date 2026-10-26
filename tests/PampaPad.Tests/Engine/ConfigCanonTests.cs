using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class ConfigCanonTests : IAsyncLifetime
{
    private readonly RealCatalog _catalog = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _catalog.DisposeAsync();

    private static Config Read(string json) => JsonSerializer.Deserialize<Config>(json, Json.Options)!;

    private static string Write(Config config) => JsonSerializer.Serialize(config, Json.Options) + "\n";

    private Config Order(Config config) => ConfigCanon.Order(config, _catalog.Steps, _catalog.Conditions);

    [Fact]
    public void The_parameters_of_a_known_step_go_in_the_order_of_its_fields_and_the_unknown_ones_after_them()
    {
        Config shuffled = Read("""
            {
              "version": 1,
              "actions": [ { "id": "a", "name": "A", "steps": [
                { "type": "wait.window", "inventado": 1, "process": "scrcpy", "timeoutSec": 5, "height": 850, "width": 1823, "title": "bancocam", "otro": 2 }
              ] } ]
            }
            """);

        string[] names = [.. Order(shuffled).Actions![0].Steps![0].Parameters.Keys];

        Assert.Equal(new[] { "title", "width", "height", "timeoutSec", "process", "inventado", "otro" }, names);
    }

    [Fact]
    public void A_null_parameter_is_left_out_but_a_step_of_an_unknown_type_is_not_touched()
    {
        Config config = Read("""
            {
              "version": 1,
              "actions": [ { "id": "a", "name": "A", "steps": [
                { "type": "obs.marker", "label": null, "marker": "short" },
                { "type": "lights", "zeta": 1, "alfa": null }
              ] } ]
            }
            """);

        IReadOnlyList<Step> steps = Order(config).Actions![0].Steps!;

        Assert.Equal(new[] { "marker" }, steps[0].Parameters.Keys);
        Assert.Equal(new[] { "zeta", "alfa" }, steps[1].Parameters.Keys);
    }

    [Fact]
    public void The_condition_of_a_step_and_the_one_of_a_check_are_ordered_too_and_negate_stays_last()
    {
        Config config = Read("""
            {
              "version": 1,
              "actions": [ { "id": "a", "name": "A", "steps": [
                { "type": "obs.marker", "marker": "short", "onlyIf": { "negate": true, "process": "scrcpy", "title": "bancocam", "type": "window.exists" } }
              ] } ],
              "preflight": [ { "condition": { "process": "scrcpy", "title": "bancocam", "type": "window.exists" }, "message": "Falta el espejo" } ]
            }
            """);

        Config ordered = Order(config);

        Assert.Equal(new[] { "title", "process" }, ordered.Actions![0].Steps![0].OnlyIf!.Parameters.Keys);
        Assert.Contains("\"process\": \"scrcpy\",\n            \"negate\": true", Write(ordered), StringComparison.Ordinal);
        Assert.Equal(new[] { "title", "process" }, ordered.Preflight![0].Condition!.Parameters.Keys);
    }

    [Fact]
    public void The_example_library_and_the_minimal_file_are_already_in_canonical_order()
    {
        string example = DispatcherRig.Example;
        string minimal = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "config.minimal.json"));

        Assert.Equal(example, Write(Order(Read(example))));
        Assert.Equal(minimal, Write(Order(Read(minimal))));
    }

    [Fact]
    public void Ordering_twice_changes_nothing()
    {
        Config once = Order(Read("""
            { "version": 1, "actions": [ { "id": "a", "name": "A", "steps": [ { "type": "wait.window", "process": "x", "title": "t", "timeoutSec": 5 } ] } ] }
            """));

        Assert.Equal(Write(once), Write(Order(once)));
    }

    [Fact]
    public void A_list_with_nothing_in_it_is_left_as_it_was()
    {
        Assert.Equal("{\n  \"version\": 1\n}\n", Write(Order(Read("""{ "version": 1 }"""))));
        Assert.Equal("{\n  \"actions\": []\n}\n", Write(Order(Read("""{ "actions": [] }"""))));
    }
}
