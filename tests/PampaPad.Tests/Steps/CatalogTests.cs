using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class CatalogTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private Log _log = null!;
    private ObsClient _obs = null!;
    private Chains _chains = null!;

    public Task InitializeAsync()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _obs = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, _state, _log, TimeProvider.System);
        _chains = TestChains.For(new CoreLibrary("", _temp.Combine("backup")), _log);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _obs.DisposeAsync();
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public void The_catalog_has_the_marker_first_then_the_steps_of_stage_4_and_then_the_chains()
    {
        Assert.Equal(new[] { "obs.marker", "obs.scene", "obs.record", "obs.mute", "obs.request", "wait.time" }, Catalog.Build(_obs, _state, _log, TimeProvider.System, _chains).Select(type => type.Id));
    }

    [Fact]
    public void Ids_are_not_repeated()
    {
        IReadOnlyList<IStepType> catalog = Catalog.Build(_obs, _state, _log, TimeProvider.System, _chains);

        Assert.Equal(catalog.Count, catalog.Select(type => type.Id).Distinct().Count());
    }

    [Fact]
    public void Every_field_is_a_property_of_the_parameters_of_its_type()
    {
        foreach (IStepType type in Catalog.Build(_obs, _state, _log, TimeProvider.System, _chains))
        {
            Type parameters = type.GetType().BaseType!.GetGenericArguments()[0];
            HashSet<string> names = [.. parameters.GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))];

            Assert.All(type.Fields, field => Assert.Contains(field.Name, names));
            Assert.False(string.IsNullOrWhiteSpace(type.Name));
            Assert.False(string.IsNullOrWhiteSpace(type.Group));
        }
    }
}
