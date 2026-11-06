using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La principal de cada momento (02 §7, regla 9): Config.ResolveMainPage con OBS abierto y cerrado.</summary>
public class MainPageWithoutObsTests
{
    private static Config Read(string json) => JsonSerializer.Deserialize<Config>(json, Json.Options)!;

    private static Config With(string? mainPage, string? withoutObs) => Read($$"""
        {
          {{(mainPage is null ? "" : $"\"mainPage\": \"{mainPage}\",")}}
          {{(withoutObs is null ? "" : $"\"mainPageWithoutObs\": \"{withoutObs}\",")}}
          "pages": [ { "id": "escenas" }, { "id": "grabacion" }, { "id": "escritorio", "type": "keys" }, { "id": "sin-pc", "type": "local" }, { "id": "chequeo", "type": "preflight" } ]
        }
        """);

    [Theory]
    [InlineData("grabacion", "escritorio", "grabacion")]
    [InlineData(null, "escritorio", "escenas")]
    [InlineData("grabacion", null, "grabacion")]
    public void With_obs_open_it_is_mainPage_or_the_first(string? mainPage, string? withoutObs, string expected) =>
        Assert.Equal(expected, With(mainPage, withoutObs).ResolveMainPage(obsOpen: true));

    [Theory]
    [InlineData("grabacion", "escritorio", "escritorio")]
    [InlineData(null, "escritorio", "escritorio")]
    [InlineData("grabacion", null, "grabacion")]
    [InlineData(null, null, "escenas")]
    public void With_obs_closed_it_is_the_field_and_without_it_the_same_as_open(string? mainPage, string? withoutObs, string expected) =>
        Assert.Equal(expected, With(mainPage, withoutObs).ResolveMainPage(obsOpen: false));

    [Theory]
    [InlineData("no-existe")]
    [InlineData("sin-pc")]
    [InlineData("chequeo")]
    public void With_obs_closed_a_field_that_cannot_be_the_base_is_ignored_and_mainPage_rules(string withoutObs)
    {
        Assert.Equal("grabacion", With("grabacion", withoutObs).ResolveMainPage(obsOpen: false));
        Assert.Equal("escenas", With(null, withoutObs).ResolveMainPage(obsOpen: false));
    }
}
