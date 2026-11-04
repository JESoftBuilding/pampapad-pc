using PampaPad.Core;
using static PampaPad.Tests.AlertRun;

namespace PampaPad.Tests;

/// <summary>mic_muted, long_pause y still_recording, aviso por aviso: qué los hace saltar, qué no y cómo se apagan (04 §5; plan de la 9b, E9b-3).</summary>
public class GraveAlertsTakeTests
{
    private static readonly AppState Stopped = Obs();
    private static readonly AppState Recording = Obs(RecordingState.Recording);

    [Fact]
    public void Starting_to_record_with_the_mic_muted_shows_mic_muted_in_red_with_the_texts_of_04()
    {
        var run = new AlertRun();
        run.Look(Obs(micMuted: true));

        GraveAlert? alert = run.Look(Obs(RecordingState.Recording, micMuted: true));

        Assert.Equal(new GraveAlert(AlertTexts.MicMuted, 2, 2, "MIC MUDO", "ESTAS GRABANDO", "", "MIC: ABRIR   CLICK: YA SE", T0, true, true), alert);
    }

    [Fact]
    public void Muting_from_obs_in_the_middle_of_the_take_shows_it_and_opening_the_mic_turns_it_off()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        run.Look(Recording);

        Assert.Equal(AlertTexts.MicMuted, run.After(TimeSpan.FromSeconds(5), Obs(RecordingState.Recording, micMuted: true))?.Id);
        Assert.Null(run.After(TimeSpan.FromSeconds(5), Recording));
    }

    [Fact]
    public void Muting_from_the_pad_does_not_alert_but_a_mute_long_after_the_pad_asked_does()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        run.Look(Recording);
        AppState fromPad = With(Obs(RecordingState.Recording, micMuted: true), obs => obs with { OwnMuteAt = run.Now.AddMilliseconds(-300) });

        Assert.Null(run.Look(fromPad));
        Assert.Null(run.After(TimeSpan.FromSeconds(10), fromPad));   // sigue mudo, pedido por el pad: no salta después

        run.Look(Recording);
        AppState late = With(Obs(RecordingState.Recording, micMuted: true), obs => obs with { OwnMuteAt = run.Now.AddSeconds(-3) });
        Assert.Equal(AlertTexts.MicMuted, run.Look(late)?.Id);
    }

    [Fact]
    public void Muting_while_stopped_does_not_alert()
    {
        var run = new AlertRun();
        run.Look(Stopped);

        Assert.Null(run.Look(Obs(micMuted: true)));
    }

    [Fact]
    public void A_click_silences_mic_muted_for_the_rest_of_the_take_and_the_next_take_alerts_again()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        AppState muted = Obs(RecordingState.Recording, micMuted: true);
        run.Look(muted);

        Assert.Null(run.Click(muted));
        run.Look(Recording);
        Assert.Null(run.Look(muted));                                // otra vez mudo en la misma toma: no vuelve

        run.Look(Obs(micMuted: true));
        Assert.Equal(AlertTexts.MicMuted, run.Look(muted)?.Id);      // la toma siguiente arranca muda
    }

    [Fact]
    public void Long_pause_shows_at_three_minutes_its_clock_keeps_counting_and_resuming_turns_it_off()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        run.Look(Recording);
        AppState paused = Obs(RecordingState.Paused);

        Assert.Null(run.After(TimeSpan.FromSeconds(179), paused));
        GraveAlert? alert = run.After(TimeSpan.FromSeconds(1), paused);
        Assert.Equal(new GraveAlert(AlertTexts.LongPause, 3, 1, "EN PAUSA", "HACE 3:00", "", "NO SE GRABA NADA", T0.AddSeconds(180), true, true), alert);
        Assert.Equal("HACE 3:10", run.After(TimeSpan.FromSeconds(10), paused)?.Line1);
        Assert.Equal(T0.AddSeconds(180), run.Memory.Shown?.Since);    // el mismo aviso: no aparece de nuevo con cada segundo
        Assert.Null(run.Look(Recording));
    }

    [Fact]
    public void Without_the_time_of_the_pause_it_counts_from_the_first_time_it_was_seen()
    {
        var run = new AlertRun();
        AppState paused = With(Obs(RecordingState.Paused), obs => obs with { RecordingChangedOn = null });
        run.Now = T0.AddHours(1);
        run.Look(paused);

        Assert.Null(run.After(TimeSpan.FromSeconds(179), paused));
        Assert.Equal(AlertTexts.LongPause, run.After(TimeSpan.FromSeconds(1), paused)?.Id);
    }

    [Fact]
    public void Long_pause_goes_away_with_a_click_or_stopping_and_does_not_come_back_in_the_same_pause()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        run.Look(Recording);
        AppState paused = Obs(RecordingState.Paused);
        run.After(TimeSpan.FromMinutes(3), paused);

        Assert.Null(run.Click(paused));
        Assert.Null(run.After(TimeSpan.FromMinutes(5), paused));

        var other = new AlertRun();
        other.Look(Stopped);
        other.Look(Recording);
        other.After(TimeSpan.FromMinutes(3), paused);
        Assert.Null(other.Look(Stopped));
    }

    [Fact]
    public void Still_recording_shows_at_three_hours_of_take_and_a_click_silences_it_thirty_minutes_of_take()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        run.Look(Recording);

        Assert.Null(run.Look(Obs(RecordingState.Recording, recorded: new TimeSpan(2, 59, 59))));
        GraveAlert? alert = run.Look(Obs(RecordingState.Recording, recorded: TimeSpan.FromHours(3)));
        Assert.Equal(new GraveAlert(AlertTexts.StillRecording, 4, 2, "GRABANDO", "HACE 3:00:00", "SEGUIS GRABANDO?", "CLICK: SIGO   REC: CORTAR", T0, true, true), alert);

        Assert.Null(run.Click(Obs(RecordingState.Recording, recorded: TimeSpan.FromHours(3))));
        Assert.Null(run.Look(Obs(RecordingState.Paused, recorded: new TimeSpan(3, 29, 59))));
        Assert.Equal("HACE 3:30:00", run.Look(Obs(RecordingState.Recording, recorded: new TimeSpan(3, 30, 0)))?.Line1);
        Assert.Null(run.Look(Stopped));
    }
}
