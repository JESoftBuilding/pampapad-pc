using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Cómo se lleva "Falló una acción" con los demás estados de la bandeja (05 §3: es el de más arriba).</summary>
public class TraySummaryFailureEdgeTests
{
    private static readonly ActionFailure Failure = new("marker_failed", "Falló Marca: fin: paso 1 (obs.marker)", "No estás grabando: la marca no entró.");
    private const string FailureText = "Falló Marca: fin: paso 1 (obs.marker). No estás grabando: la marca no entró.";

    private static AppState With(ObsState obs) => new AppState { Obs = obs } with { LastFailure = Failure };

    [Theory]
    [InlineData(ObsLinkState.Closed, "Sin OBS: no está abierto")]
    [InlineData(ObsLinkState.NoWebsocket, "Sin OBS: está abierto, sin WebSocket")]
    [InlineData(ObsLinkState.AuthFailed, "Sin OBS: clave rechazada")]
    public void The_failure_goes_before_the_text_of_a_lost_link(ObsLinkState link, string text)
    {
        TraySummary summary = TraySummary.From(With(ObsState.Initial with { Link = link }));

        Assert.Equal(TrayIcon.ActionFailed, summary.Icon);
        Assert.Equal($"{FailureText} · {text}", summary.Tooltip);
    }

    [Fact]
    public void The_failure_goes_before_a_take_in_progress()
    {
        var recording = ObsState.Initial with { Link = ObsLinkState.Ready, Recording = RecordingState.Recording, Recorded = new TimeSpan(0, 5, 3) };

        TraySummary summary = TraySummary.From(With(recording));

        Assert.Equal(TrayIcon.ActionFailed, summary.Icon);
        Assert.Equal($"{FailureText} · Grabando 0:05:03", summary.Tooltip);
    }

    [Fact]
    public void The_failure_beats_streaming_the_config_problem_and_the_update_and_the_text_names_them_all()
    {
        AppState state = With(ObsState.Initial with { Link = ObsLinkState.Ready, Streaming = true }) with
        {
            Library = new LibraryState(LibraryStatus.NoLibrary, ""),
            Update = new UpdateState(UpdatePhase.Available, "1.3.0"),
        };

        TraySummary summary = TraySummary.From(state);

        Assert.Equal(TrayIcon.ActionFailed, summary.Icon);
        Assert.Equal($"{FailureText} · Transmitiendo · Sin biblioteca: elegí la carpeta de datos · Versión 1.3.0 lista para instalar. Se está transmitiendo", summary.Tooltip);
    }

    [Fact]
    public void A_failure_with_no_message_is_just_the_summary()
    {
        TraySummary summary = TraySummary.From(With(ObsState.Initial with { Link = ObsLinkState.Ready }) with { LastFailure = Failure with { Message = "" } });

        Assert.Equal(TrayIcon.ActionFailed, summary.Icon);
        Assert.Equal("Falló Marca: fin: paso 1 (obs.marker)", summary.Tooltip);
    }

    [Fact]
    public void A_failure_alone_replaces_the_idle_text()
    {
        TraySummary summary = TraySummary.From(With(ObsState.Initial with { Link = ObsLinkState.Ready }));

        Assert.Equal(TrayIcon.ActionFailed, summary.Icon);
        Assert.Equal(FailureText, summary.Tooltip);
    }

    [Theory]
    [InlineData(ObsLinkState.AuthFailed, true)]
    [InlineData(ObsLinkState.Kicked, true)]
    [InlineData(ObsLinkState.Ready, false)]
    public void The_failure_does_not_change_whether_it_offers_to_reconnect(ObsLinkState link, bool offers)
    {
        Assert.Equal(offers, TraySummary.From(With(ObsState.Initial with { Link = link })).CanReconnect);
    }

    [Fact]
    public void Clearing_the_failure_gives_back_the_icon_and_the_text_it_covered()
    {
        AppState failed = With(ObsState.Initial with { Link = ObsLinkState.Ready, Recording = RecordingState.Paused });

        TraySummary summary = TraySummary.From(failed with { LastFailure = null });

        Assert.Equal(TrayIcon.Paused, summary.Icon);
        Assert.Equal("En pausa: las marcas no entran", summary.Tooltip);
    }

    [Fact]
    public void The_update_items_do_not_depend_on_the_failure()
    {
        var update = new UpdateState(UpdatePhase.Available, "1.3.0");
        AppState clean = new AppState { Update = update };

        Assert.Equal(TraySummary.UpdateItems(clean), TraySummary.UpdateItems(clean with { LastFailure = Failure }));
        Assert.Equal(TraySummary.UpdateText(clean), TraySummary.UpdateText(clean with { LastFailure = Failure }));
    }
}
