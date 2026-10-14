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

    // Lo que sigue cierra huecos que el plan no probaba (cada caso hace fallar una mutación a mano; ver el informe de E2-6).

    [Fact]
    public void Reconnect_is_offered_in_exactly_two_of_the_nine_link_states()
    {
        var offered = Enum.GetValues<ObsLinkState>().Where(link => For(ObsState.Initial with { Link = link }).CanReconnect);

        Assert.Equal([ObsLinkState.AuthFailed, ObsLinkState.Kicked], offered.Order());
    }

    [Fact]
    public void Paused_wins_over_streaming_and_the_tooltip_names_both()
    {
        var summary = For(Connected with { Recording = RecordingState.Paused, Streaming = true });

        Assert.Equal(TrayIcon.Paused, summary.Icon);
        Assert.Equal("En pausa: las marcas no entran · Transmitiendo", summary.Tooltip);
    }

    [Fact]
    public void The_recorded_time_is_cut_not_rounded_and_the_hours_go_past_a_day()
    {
        var cut = For(Connected with { Recording = RecordingState.Recording, Recorded = new TimeSpan(0, 0, 34, 12, 999) });
        var days = For(Connected with { Recording = RecordingState.Recording, Recorded = new TimeSpan(1, 6, 5, 7) });

        Assert.Equal("Grabando 0:34:12", cut.Tooltip);
        Assert.Equal("Grabando 30:05:07", days.Tooltip);
    }

    [Fact]
    public void No_obs_goes_above_an_offered_update_and_the_tooltip_names_both()
    {
        var closed = AppState.Initial with { Update = new UpdateState(UpdatePhase.Available, "0.1.1") };
        var authFailed = closed with { Obs = ObsState.Initial with { Link = ObsLinkState.AuthFailed } };

        Assert.Equal(TrayIcon.NoObs, TraySummary.From(closed).Icon);
        Assert.Equal("Sin OBS: no está abierto · Versión 0.1.1 lista para instalar", TraySummary.From(closed).Tooltip);
        Assert.Equal("Sin OBS: clave rechazada · Versión 0.1.1 lista para instalar", TraySummary.From(authFailed).Tooltip);
        Assert.True(TraySummary.From(authFailed).CanReconnect);
    }

    [Fact]
    public void An_offered_update_goes_after_a_paused_or_streaming_state_and_before_the_muted_mic()
    {
        var update = new UpdateState(UpdatePhase.Available, "0.1.1");
        var paused = AppState.Initial with { Obs = Connected with { Recording = RecordingState.Paused }, Update = update };
        var streaming = AppState.Initial with { Obs = Connected with { Streaming = true, MicMuted = true }, Update = update };
        var idle = AppState.Initial with { Obs = Connected with { MicMuted = true }, Update = update };

        Assert.Equal(TrayIcon.Paused, TraySummary.From(paused).Icon);
        Assert.Equal("En pausa: las marcas no entran · Versión 0.1.1 lista para instalar", TraySummary.From(paused).Tooltip);
        Assert.Equal(TrayIcon.Streaming, TraySummary.From(streaming).Icon);
        Assert.Equal("Transmitiendo · Versión 0.1.1 lista para instalar · micrófono silenciado", TraySummary.From(streaming).Tooltip);
        Assert.Equal(TrayIcon.UpdateReady, TraySummary.From(idle).Icon);
        Assert.Equal("Versión 0.1.1 lista para instalar · micrófono silenciado", TraySummary.From(idle).Tooltip);
    }

    [Theory]
    [InlineData(UpdatePhase.Available, "0.1.1", null, "Versión 0.1.1 lista para instalar")]
    [InlineData(UpdatePhase.Available, "0.1.1", "Se está grabando", "Versión 0.1.1 lista para instalar. Se está grabando")]
    [InlineData(UpdatePhase.Downloading, "0.1.1", null, "Bajando la versión 0.1.1")]
    [InlineData(UpdatePhase.Applying, "0.1.1", null, "Instalando la versión 0.1.1")]
    [InlineData(UpdatePhase.Idle, "0.1.1", null, null)]
    [InlineData(UpdatePhase.Idle, null, null, null)]
    [InlineData(UpdatePhase.Available, null, null, null)]
    [InlineData(UpdatePhase.Downloading, null, null, null)]
    [InlineData(UpdatePhase.Applying, null, null, null)]
    public void The_update_text_follows_the_phase(UpdatePhase phase, string? version, string? detail, string? expected)
    {
        Assert.Equal(expected, TraySummary.UpdateText(new UpdateState(phase, version, detail)));
    }

    [Fact]
    public void Looking_again_while_an_update_is_offered_keeps_the_offer_in_the_tray()
    {
        // UpdateCycle.CheckAsync publica "before with { Phase = Checking }": la versión y el motivo siguen ahí hasta que
        // la búsqueda termine, y el ítem "Instalar" del menú sigue visible. La flecha no puede apagarse y prenderse.
        var checking = AppState.Initial with { Obs = Connected, Update = new UpdateState(UpdatePhase.Checking, "0.1.1") };
        var withReason = checking with { Update = new UpdateState(UpdatePhase.Checking, "0.1.1", "Se está grabando") };
        var first = AppState.Initial with { Obs = Connected, Update = new UpdateState(UpdatePhase.Checking) };

        Assert.Equal(TrayIcon.UpdateReady, TraySummary.From(checking).Icon);
        Assert.Equal("Versión 0.1.1 lista para instalar", TraySummary.From(checking).Tooltip);
        Assert.Equal("Versión 0.1.1 lista para instalar. Se está grabando", TraySummary.From(withReason).Tooltip);
        Assert.Equal(TrayIcon.Idle, TraySummary.From(first).Icon);
        Assert.Equal("PampaPad · OBS conectado", TraySummary.From(first).Tooltip);
    }
}
