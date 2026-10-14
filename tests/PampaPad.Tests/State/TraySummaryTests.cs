using PampaPad.Core;

namespace PampaPad.Tests;

public class TraySummaryTests
{
    private static readonly ObsState Connected = ObsState.Initial with { Link = ObsLinkState.Ready };

    private static TraySummary For(ObsState obs) => TraySummary.From(AppState.Initial with { Obs = obs });

    [Fact]
    public void Connected_and_stopped_is_idle()
    {
        var summary = For(Connected);

        Assert.Equal(TrayIcon.Idle, summary.Icon);
        Assert.Equal("PampaPad · OBS conectado", summary.Tooltip);
        Assert.False(summary.CanReconnect);
    }

    [Fact]
    public void Recording_shows_the_recorded_time()
    {
        var summary = For(Connected with { Recording = RecordingState.Recording, Recorded = new TimeSpan(0, 34, 12) });

        Assert.Equal(TrayIcon.Recording, summary.Icon);
        Assert.Equal("Grabando 0:34:12", summary.Tooltip);
    }

    [Fact]
    public void A_long_take_keeps_counting_the_hours()
    {
        var summary = For(Connected with { Recording = RecordingState.Recording, Recorded = new TimeSpan(12, 27, 3) });

        Assert.Equal("Grabando 12:27:03", summary.Tooltip);
    }

    [Fact]
    public void Paused_says_that_markers_do_not_go_in()
    {
        var summary = For(Connected with { Recording = RecordingState.Paused, Recorded = TimeSpan.FromSeconds(9) });

        Assert.Equal(TrayIcon.Paused, summary.Icon);
        Assert.Equal("En pausa: las marcas no entran", summary.Tooltip);
    }

    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Stopping)]
    public void A_transition_still_looks_idle(RecordingState recording)
    {
        var summary = For(Connected with { Recording = recording });

        Assert.Equal(TrayIcon.Idle, summary.Icon);
    }

    [Fact]
    public void Streaming_alone_shows_streaming()
    {
        var summary = For(Connected with { Streaming = true });

        Assert.Equal(TrayIcon.Streaming, summary.Icon);
        Assert.Equal("Transmitiendo", summary.Tooltip);
    }

    [Fact]
    public void When_two_apply_the_icon_is_the_first_and_the_tooltip_names_both()
    {
        var summary = For(Connected with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(5), Streaming = true });

        Assert.Equal(TrayIcon.Recording, summary.Icon);
        Assert.Equal("Grabando 0:00:05 · Transmitiendo", summary.Tooltip);
    }

    [Fact]
    public void A_muted_mic_is_named_in_the_tooltip()
    {
        var idle = For(Connected with { MicMuted = true });
        var recording = For(Connected with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(5), MicMuted = true });

        Assert.Equal("PampaPad · OBS conectado · micrófono silenciado", idle.Tooltip);
        Assert.Equal("Grabando 0:00:05 · micrófono silenciado", recording.Tooltip);
    }

    [Theory]
    [InlineData(ObsLinkState.Closed, "Sin OBS: no está abierto")]
    [InlineData(ObsLinkState.NoWebsocket, "Sin OBS: está abierto, sin WebSocket")]
    [InlineData(ObsLinkState.AuthFailed, "Sin OBS: clave rechazada")]
    [InlineData(ObsLinkState.Loading, "Sin OBS: conectando")]
    [InlineData(ObsLinkState.Switching, "Sin OBS: cambiando de colección")]
    [InlineData(ObsLinkState.Lost, "Sin OBS: se cortó la conexión")]
    [InlineData(ObsLinkState.Kicked, "Sin OBS: OBS cerró la sesión")]
    [InlineData(ObsLinkState.Exiting, "Sin OBS: se está cerrando")]
    public void Without_a_ready_link_the_icon_is_no_obs_and_the_tooltip_says_why(ObsLinkState link, string tooltip)
    {
        var summary = For(ObsState.Initial with { Link = link, MicMuted = true });

        Assert.Equal(TrayIcon.NoObs, summary.Icon);
        Assert.Equal(tooltip, summary.Tooltip);         // sin enlace no se dice nada del micrófono
    }

    [Fact]
    public void A_cut_link_shows_no_obs_and_names_the_last_known_take()
    {
        var recording = (Connected with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromMinutes(3) }).WithLink(ObsLinkState.Lost);
        var paused = (Connected with { Recording = RecordingState.Paused }).WithLink(ObsLinkState.Exiting);

        Assert.Equal(TrayIcon.NoObs, For(recording).Icon);
        Assert.Equal("Sin OBS: se cortó la conexión · lo último que se supo: grabando", For(recording).Tooltip);
        Assert.Equal("Sin OBS: se está cerrando · lo último que se supo: en pausa", For(paused).Tooltip);
    }

    [Theory]
    [InlineData(ObsLinkState.AuthFailed, true)]
    [InlineData(ObsLinkState.Kicked, true)]
    [InlineData(ObsLinkState.Closed, false)]
    [InlineData(ObsLinkState.Lost, false)]
    [InlineData(ObsLinkState.Ready, false)]
    public void Reconnect_is_offered_only_where_the_link_does_not_retry_alone(ObsLinkState link, bool offered)
    {
        var summary = For(ObsState.Initial with { Link = link });

        Assert.Equal(offered, summary.CanReconnect);
    }

    [Fact]
    public void An_offered_update_shows_the_arrow_only_when_nothing_above_it_applies()
    {
        var offered = AppState.Initial with { Obs = Connected, Update = new UpdateState(UpdatePhase.Available, "0.1.1") };
        var recording = offered with { Obs = Connected with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(5) } };

        Assert.Equal(TrayIcon.UpdateReady, TraySummary.From(offered).Icon);
        Assert.Equal("Versión 0.1.1 lista para instalar", TraySummary.From(offered).Tooltip);
        Assert.Equal(TrayIcon.Recording, TraySummary.From(recording).Icon);
        Assert.Equal("Grabando 0:00:05 · Versión 0.1.1 lista para instalar", TraySummary.From(recording).Tooltip);
    }
}
