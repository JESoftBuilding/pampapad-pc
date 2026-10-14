using PampaPad.Core;

namespace PampaPad.Tests;

public class NavigationTests
{
    private static readonly string[] Cycle = ["grabacion", "sala", "luces"];

    private static NavigationInput Input(string basePage, RecordingState recording = RecordingState.Stopped, string? destination = null, string? layer = null) =>
        new(basePage, layer, recording, "grabacion", destination, Cycle);

    [Fact]
    public void Stopped_and_without_a_request_nothing_changes()
    {
        Assert.Equal(new NavigationResult("sala", "sala", null), Navigation.Resolve(Input("sala")));
    }

    [Theory]
    [InlineData("sala", "sala")]
    [InlineData("capa", "capa")]
    [InlineData("main", "grabacion")]
    public void Stopped_a_destination_becomes_the_base_page(string destination, string expected)
    {
        NavigationResult result = Navigation.Resolve(Input("grabacion", destination: destination));

        Assert.Equal(expected, result.BasePage);
        Assert.Equal(expected, result.ActivePage);
        Assert.Null(result.Failure);
    }

    [Theory]
    [InlineData("grabacion", "next", "sala")]
    [InlineData("luces", "next", "grabacion")]
    [InlineData("grabacion", "previous", "luces")]
    [InlineData("sala", "previous", "grabacion")]
    [InlineData("capa", "next", "grabacion")]
    [InlineData("capa", "previous", "luces")]
    public void Next_and_previous_walk_the_cycle_and_wrap_around(string basePage, string destination, string expected)
    {
        Assert.Equal(expected, Navigation.Resolve(Input(basePage, destination: destination)).BasePage);
    }

    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    [InlineData(RecordingState.Stopping)]
    public void With_a_take_in_progress_the_base_page_is_the_main_one(RecordingState recording)
    {
        NavigationResult result = Navigation.Resolve(Input("sala", recording));

        Assert.Equal("grabacion", result.BasePage);
        Assert.Equal("grabacion", result.ActivePage);
        Assert.Null(result.Failure);
    }

    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    [InlineData(RecordingState.Stopping)]
    public void With_a_take_in_progress_going_to_another_page_is_blocked(RecordingState recording)
    {
        NavigationResult result = Navigation.Resolve(Input("grabacion", recording, destination: "sala"));

        Assert.Equal("grabacion", result.BasePage);
        Assert.Equal(ErrorCodes.NavigationBlocked, result.Failure?.Code);
    }

    [Theory]
    [InlineData(RecordingState.Stopped)]
    [InlineData(RecordingState.Recording)]
    public void A_held_layer_rules_while_it_is_held_and_never_changes_the_base(RecordingState recording)
    {
        NavigationResult result = Navigation.Resolve(Input("grabacion", recording, layer: "capa"));

        Assert.Equal("capa", result.ActivePage);
        Assert.Equal("grabacion", result.BasePage);
    }

    [Fact]
    public void Without_pages_in_the_cycle_next_stays_where_it_is()
    {
        var input = new NavigationInput("grabacion", null, RecordingState.Stopped, "grabacion", "next", []);

        Assert.Equal("grabacion", Navigation.Resolve(input).BasePage);
    }
}
