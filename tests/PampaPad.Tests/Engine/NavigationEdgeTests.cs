using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Los bordes de la navegación que las pruebas del plan dejan abiertos: capa y toma a la vez, recorrido de una sola página, pedidos a la misma página.</summary>
public class NavigationEdgeTests
{
    private static readonly string[] Cycle = ["grabacion", "sala", "luces"];

    [Theory]
    [InlineData("main")]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("sala")]
    public void With_a_take_in_progress_any_request_is_blocked_even_the_one_to_the_main_page(string destination)
    {
        NavigationResult result = Navigation.Resolve(new("grabacion", null, RecordingState.Recording, "grabacion", destination, Cycle));

        Assert.Equal(("grabacion", "grabacion", ErrorCodes.NavigationBlocked), (result.ActivePage, result.BasePage, result.Failure?.Code));
    }

    [Fact]
    public void A_held_layer_shows_while_a_request_is_blocked_and_the_base_stays_the_main_page()
    {
        NavigationResult result = Navigation.Resolve(new("sala", "capa", RecordingState.Paused, "grabacion", "luces", Cycle));

        Assert.Equal(("capa", "grabacion", ErrorCodes.NavigationBlocked), (result.ActivePage, result.BasePage, result.Failure?.Code));
    }

    [Fact]
    public void Stopped_a_request_moves_the_base_and_a_held_layer_still_shows()
    {
        NavigationResult result = Navigation.Resolve(new("grabacion", "capa", RecordingState.Stopped, "grabacion", "sala", Cycle));

        Assert.Equal(("capa", "sala", null), (result.ActivePage, result.BasePage, result.Failure?.Code));
    }

    [Theory]
    [InlineData("next")]
    [InlineData("previous")]
    public void A_cycle_of_one_page_stays_on_it(string destination)
    {
        NavigationResult result = Navigation.Resolve(new("grabacion", null, RecordingState.Stopped, "grabacion", destination, ["grabacion"]));

        Assert.Equal("grabacion", result.BasePage);
    }

    [Theory]
    [InlineData("next", "grabacion")]
    [InlineData("previous", "luces")]
    public void From_a_page_outside_the_cycle_next_goes_to_the_first_and_previous_to_the_last(string destination, string expected)
    {
        Assert.Equal(expected, Navigation.Resolve(new("fuera", null, RecordingState.Stopped, "grabacion", destination, Cycle)).BasePage);
    }

    [Theory]
    [InlineData("next")]
    [InlineData("previous")]
    public void Without_pages_in_the_cycle_next_and_previous_stay_where_they_are_and_not_on_the_main_page(string destination)
    {
        Assert.Equal("luces", Navigation.Resolve(new("luces", null, RecordingState.Stopped, "grabacion", destination, [])).BasePage);
    }

    [Fact]
    public void Going_to_the_page_it_is_already_on_changes_nothing()
    {
        Assert.Equal(new NavigationResult("sala", "sala", null), Navigation.Resolve(new("sala", null, RecordingState.Stopped, "grabacion", "sala", Cycle)));
    }

    [Fact]
    public void Main_goes_to_the_main_page_whatever_the_cycle_says()
    {
        Assert.Equal("grabacion", Navigation.Resolve(new("luces", null, RecordingState.Stopped, "grabacion", "main", [])).BasePage);
    }

    [Fact]
    public void Walking_the_whole_cycle_forward_and_back_returns_to_where_it_started()
    {
        string page = "sala";
        for (int i = 0; i < Cycle.Length; i++)
        {
            page = Navigation.Resolve(new(page, null, RecordingState.Stopped, "grabacion", Navigation.Next, Cycle)).BasePage;
        }

        Assert.Equal("sala", page);
        for (int i = 0; i < Cycle.Length; i++)
        {
            page = Navigation.Resolve(new(page, null, RecordingState.Stopped, "grabacion", Navigation.Previous, Cycle)).BasePage;
        }

        Assert.Equal("sala", page);
    }

    [Fact]
    public void The_reserved_words_are_the_ones_the_validator_forbids_as_ids()
    {
        Assert.Equal(new[] { "next", "previous", "main" }, new[] { Navigation.Next, Navigation.Previous, Navigation.Main });
        foreach (string word in new[] { Navigation.Next, Navigation.Previous, Navigation.Main })
        {
            var config = new Config { Version = 1, Pages = [new Page { Id = word, Name = "x", Keys = [] }] };

            Assert.Contains(Validator.Validate(config), problem => problem.Code == ProblemCodes.IdInvalid && problem.Where == "pages[0].id");
        }
    }
}
