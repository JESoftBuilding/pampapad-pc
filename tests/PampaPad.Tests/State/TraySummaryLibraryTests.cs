using PampaPad.Core;

namespace PampaPad.Tests;

public class TraySummaryLibraryTests
{
    private static AppState Connected()
    {
        var empty = new AppState();
        return empty with
        {
            Obs = empty.Obs with { Link = ObsLinkState.Ready },
            Library = new LibraryState(LibraryStatus.Loaded, @"D:\datos\pampapad"),
        };
    }

    private static AppState WithProblems(params string[] places) => Connected() with
    {
        Problems = [.. places.Select(place => new Problem(place, ProblemCodes.ActionNotFound, "Nombra una acción que no existe.", Severity.Error))],
    };

    [Fact]
    public void A_fresh_state_has_no_library_text()
    {
        Assert.Equal(LibraryStatus.NotLoaded, new AppState().Library.Status);
        Assert.Empty(new AppState().Problems);
        Assert.Null(TraySummary.LibraryText(new AppState()));
        Assert.Null(TraySummary.LibraryText(Connected()));
    }

    [Fact]
    public void A_problem_in_a_key_shows_the_config_icon_and_counts_keys()
    {
        TraySummary summary = TraySummary.From(WithProblems("pages[0].keys[1].action", "pages[0].keys[1].hotkey"));

        Assert.Equal(TrayIcon.ConfigProblem, summary.Icon);
        Assert.Contains("1 tecla con problema", summary.Tooltip);
    }

    [Fact]
    public void Problems_in_two_keys_are_two_keys()
    {
        Assert.Equal("2 teclas con problema", TraySummary.LibraryText(WithProblems("pages[0].keys[1].action", "pages[0].keys[4].hotkey")));
    }

    [Fact]
    public void Problems_outside_the_keys_are_counted_as_problems()
    {
        Assert.Equal("2 problemas de configuración", TraySummary.LibraryText(WithProblems("pages[0].keys[1].action", "actions[2].steps[0].type")));
        Assert.Equal("1 problema de configuración", TraySummary.LibraryText(WithProblems("mainPage")));
    }

    [Fact]
    public void Without_library_the_text_says_what_to_do()
    {
        AppState none = Connected() with { Library = new LibraryState(LibraryStatus.NoLibrary, "") };
        AppState missing = Connected() with { Library = new LibraryState(LibraryStatus.NoLibrary, @"E:\Conocimiento\Areas\marca-personal\pampapad") };

        Assert.Equal("Sin biblioteca: elegí la carpeta de datos", TraySummary.LibraryText(none));
        Assert.Equal(@"Sin biblioteca: no encuentro E:\Conocimiento\Areas\marca-personal\pampapad", TraySummary.LibraryText(missing));
        Assert.Equal(TrayIcon.ConfigProblem, TraySummary.From(none).Icon);
    }

    [Fact]
    public void An_unreadable_file_shows_its_message()
    {
        AppState state = Connected() with
        {
            Library = new LibraryState(LibraryStatus.Unreadable, @"D:\datos\pampapad"),
            Problems = [new Problem("", ErrorCodes.ConfigUnreadable, "config.json no se puede leer: error de sintaxis en la línea 4, columna 1.", Severity.Error)],
        };

        Assert.Contains("línea 4", TraySummary.From(state).Tooltip);
        Assert.Equal(TrayIcon.ConfigProblem, TraySummary.From(state).Icon);
    }

    [Fact]
    public void Recording_wins_the_icon_and_the_tooltip_still_names_the_problem()
    {
        AppState problems = WithProblems("pages[0].keys[1].action");
        AppState recording = problems with { Obs = problems.Obs with { Recording = RecordingState.Recording } };

        TraySummary summary = TraySummary.From(recording);

        Assert.NotEqual(TrayIcon.ConfigProblem, summary.Icon);
        Assert.Contains("1 tecla con problema", summary.Tooltip);
    }
}
