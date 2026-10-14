using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Lo que las pruebas del plan de E3-3 no fijan de la bandeja con biblioteca: la prioridad entre estados
/// (05 §3: pierde contra lo de arriba de la tabla y le gana a la actualización y a "sin novedades") y el conteo de teclas.
/// </summary>
public class TraySummaryLibraryEdgeTests
{
    private static AppState WithLink(ObsLinkState link, params string[] places)
    {
        var empty = new AppState();
        return empty with
        {
            Obs = empty.Obs with { Link = link },
            Library = new LibraryState(LibraryStatus.Loaded, @"D:\datos\pampapad"),
            Problems = [.. places.Select(place => new Problem(place, ProblemCodes.ActionNotFound, "Nombra una acción que no existe.", Severity.Error))],
        };
    }

    private static AppState Ready(params string[] places) => WithLink(ObsLinkState.Ready, places);

    private static AppState Offering(AppState state) =>
        state with { Update = new UpdateState(UpdatePhase.Available, "9.9.9") };

    [Fact]
    public void Without_obs_the_x_wins_and_the_tooltip_names_both()
    {
        TraySummary summary = TraySummary.From(WithLink(ObsLinkState.Closed, "pages[0].keys[1].action"));

        Assert.Equal(TrayIcon.NoObs, summary.Icon);
        Assert.Equal("Sin OBS: no está abierto · 1 tecla con problema", summary.Tooltip);
    }

    [Fact]
    public void Paused_wins_over_the_config_problem()
    {
        AppState state = Ready("pages[0].keys[1].action");
        state = state with { Obs = state.Obs with { Recording = RecordingState.Paused } };

        TraySummary summary = TraySummary.From(state);

        Assert.Equal(TrayIcon.Paused, summary.Icon);
        Assert.Equal("En pausa: las marcas no entran · 1 tecla con problema", summary.Tooltip);
    }

    [Fact]
    public void Streaming_wins_over_the_config_problem()
    {
        AppState state = Ready("pages[0].keys[1].action");
        state = state with { Obs = state.Obs with { Streaming = true } };

        TraySummary summary = TraySummary.From(state);

        Assert.Equal(TrayIcon.Streaming, summary.Icon);
        Assert.Equal("Transmitiendo · 1 tecla con problema", summary.Tooltip);
    }

    [Fact]
    public void The_config_problem_wins_over_an_update_that_is_ready()
    {
        TraySummary summary = TraySummary.From(Offering(Ready("pages[0].keys[1].action")));

        Assert.Equal(TrayIcon.ConfigProblem, summary.Icon);
        Assert.Equal("1 tecla con problema · Versión 9.9.9 lista para instalar", summary.Tooltip);
    }

    [Fact]
    public void An_update_alone_still_shows_the_arrow()
    {
        AppState state = Offering(Ready());

        Assert.Equal(TrayIcon.UpdateReady, TraySummary.From(state).Icon);
    }

    [Fact]
    public void The_config_problem_replaces_the_idle_text_and_does_not_add_to_it()
    {
        TraySummary summary = TraySummary.From(Ready("mainPage"));

        Assert.Equal(TrayIcon.ConfigProblem, summary.Icon);
        Assert.Equal("1 problema de configuración", summary.Tooltip);
    }

    [Fact]
    public void Without_a_library_the_tooltip_is_only_the_instruction()
    {
        AppState none = Ready() with { Library = new LibraryState(LibraryStatus.NoLibrary, "") };

        Assert.Equal("Sin biblioteca: elegí la carpeta de datos", TraySummary.From(none).Tooltip);
    }

    [Fact]
    public void Without_obs_and_without_library_both_are_named_and_the_x_wins()
    {
        AppState state = WithLink(ObsLinkState.Closed) with { Library = new LibraryState(LibraryStatus.NoLibrary, "") };

        TraySummary summary = TraySummary.From(state);

        Assert.Equal(TrayIcon.NoObs, summary.Icon);
        Assert.Equal("Sin OBS: no está abierto · Sin biblioteca: elegí la carpeta de datos", summary.Tooltip);
    }

    [Fact]
    public void A_newer_file_shows_its_message_and_not_a_count()
    {
        AppState state = Ready() with
        {
            Library = new LibraryState(LibraryStatus.Newer, @"D:\datos\pampapad"),
            Problems = [new Problem("version", ErrorCodes.ConfigNewer, "config.json es de la versión 2 y este programa entiende hasta la 1.", Severity.Error)],
        };

        Assert.Equal("config.json es de la versión 2 y este programa entiende hasta la 1.", TraySummary.LibraryText(state));
        Assert.Equal(TrayIcon.ConfigProblem, TraySummary.From(state).Icon);
    }

    [Theory]
    [InlineData(LibraryStatus.Unreadable)]
    [InlineData(LibraryStatus.Newer)]
    public void An_unreadable_or_newer_file_without_its_problem_still_says_something(LibraryStatus status)
    {
        AppState state = Ready() with { Library = new LibraryState(status, @"D:\datos\pampapad") };

        Assert.Equal("config.json no se puede leer", TraySummary.LibraryText(state));
    }

    [Fact]
    public void A_problem_that_is_not_in_a_key_does_not_count_as_a_key()
    {
        // Los atajos generales viven en "hotkeys[n]", no en "pages[n].keys[m]".
        Assert.Equal("1 problema de configuración", TraySummary.LibraryText(Ready("hotkeys[0].hotkey")));
        Assert.Equal("2 problemas de configuración", TraySummary.LibraryText(Ready("pages[0].keys[1].action", "hotkeys[0].hotkey")));
        Assert.Equal("1 problema de configuración", TraySummary.LibraryText(Ready("pages[0].name")));
        Assert.Equal("1 problema de configuración", TraySummary.LibraryText(Ready("")));
        // Sólo cuenta como tecla lo que cuelga de una página: "pages[n].keys[m]", no otro lugar que se llame parecido.
        Assert.Equal("1 problema de configuración", TraySummary.LibraryText(Ready("moduleKey.keys[0].layer")));
        Assert.Equal("1 problema de configuración", TraySummary.LibraryText(Ready("pages[0].hotkeys[1].hotkey")));
    }

    [Fact]
    public void The_same_key_number_in_two_pages_is_two_keys()
    {
        Assert.Equal("2 teclas con problema", TraySummary.LibraryText(Ready("pages[0].keys[1].action", "pages[1].keys[1].action")));
    }

    [Fact]
    public void Three_problems_in_three_keys_are_three_keys()
    {
        Assert.Equal(
            "3 teclas con problema",
            TraySummary.LibraryText(Ready("pages[0].keys[0].label", "pages[0].keys[1].action", "pages[0].keys[2].hotkey")));
    }

    [Fact]
    public void Warnings_count_like_errors()
    {
        AppState state = Ready() with
        {
            Problems = [new Problem("pages[0].keys[1].hotkey", ProblemCodes.HotkeyRepeated, "Repite un atajo.", Severity.Warning)],
        };

        Assert.Equal("1 tecla con problema", TraySummary.LibraryText(state));
    }

    [Fact]
    public void A_loaded_library_with_no_problems_says_nothing_and_leaves_the_idle_icon()
    {
        TraySummary summary = TraySummary.From(Ready());

        Assert.Equal(TrayIcon.Idle, summary.Icon);
        Assert.Equal("PampaPad · OBS conectado", summary.Tooltip);
    }

    [Fact]
    public void The_config_problem_icon_is_the_last_of_the_enum_so_no_other_value_moves()
    {
        Assert.Equal(
            ["Idle", "NoObs", "Recording", "Paused", "Streaming", "UpdateReady", "ConfigProblem"],
            Enum.GetNames<TrayIcon>());
    }
}
