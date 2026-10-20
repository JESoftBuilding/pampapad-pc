using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El globo del chequeo previo de la bandeja (01 §8, fila 5), con la regla de Notifier: con una toma en curso no sale.</summary>
public class NotifierPreflightTests
{
    private static CheckResult Pass(string type) => new(type, "Mensaje", ConditionOutcome.Pass, "Bien.");

    private static PreflightReport Report(params CheckResult[] checks) => new(Guid.NewGuid(), DateTimeOffset.Now, Trigger.Tray, true, checks);

    private static readonly CheckResult Canvas = new("obs.canvasFilled", "La escena no llena el lienzo", ConditionOutcome.Fail, "\"Vertical SolidWorks\" llena el 31,8 % del lienzo de 1920 x 1080; se pide 90 %.");
    private static readonly CheckResult Camera = new("obs.cameraNotCovered", "La cámara está tapada o en negro", ConditionOutcome.Fail, "desvío 0,6.");
    private static readonly CheckResult NoObs = new("obs.micNotMuted", "El micrófono está silenciado", ConditionOutcome.Skipped, "Sin OBS: no se pudo leer el micrófono.", ErrorCodes.ObsDisconnected);

    [Fact]
    public async Task Stopped_with_failures_the_balloon_says_how_many_and_the_first_one()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnPreflight(Report(Pass("obs.captureHasMonitor"), Canvas, Camera));

        Notice notice = Assert.Single(rig.Balloons);
        Assert.Equal(Notifier.PreflightResult, notice.Id);
        Assert.Equal("Chequeo previo: 2 fallas", notice.Title);
        Assert.Equal("La escena no llena el lienzo. \"Vertical SolidWorks\" llena el 31,8 % del lienzo de 1920 x 1080; se pide 90 %. (y 1 más en el registro)", notice.Text);
        Assert.True(notice.Warning);
        Assert.Empty(rig.Sounds);
    }

    [Theory]
    [InlineData(RecordingState.Recording, false)]
    [InlineData(RecordingState.Paused, false)]
    [InlineData(RecordingState.Starting, false)]
    [InlineData(RecordingState.Stopped, true)]
    public async Task With_a_take_in_progress_there_is_no_balloon_and_the_log_stays(RecordingState recording, bool streaming)
    {
        await using var rig = new NotifierRig(recording: recording, streaming: streaming);

        rig.Notifier.OnPreflight(Report(Canvas));

        Assert.Empty(rig.Balloons);
        Assert.Contains(rig.Log.Recent(), entry => entry.Module == "notifier" && entry.Message.EndsWith("Cartel: no.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recording_with_sound_allowed_still_has_no_balloon()
    {
        // El ajuste de la tarjeta Avisos (05 §4.5) sólo deja sonar: el globo saldría en el video igual.
        await using var rig = new NotifierRig(
            $$"""{ "version": 1, "actions": [ {{NotifierRig.ShortAction}} ], "console": { "muteSoundWhileRecording": false } }""",
            RecordingState.Recording);

        rig.Notifier.OnPreflight(Report(Canvas));

        Assert.Empty(rig.Balloons);
    }

    [Fact]
    public async Task All_fine_is_a_balloon_that_is_not_a_warning()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnPreflight(Report(Pass("obs.captureHasMonitor"), Pass("obs.canvasFilled")));

        Notice notice = Assert.Single(rig.Balloons);
        Assert.Equal("Chequeo previo: todo bien", notice.Title);
        Assert.Equal("Las 2 comprobaciones dieron bien.", notice.Text);
        Assert.False(notice.Warning);
    }

    [Fact]
    public void Without_failures_what_could_not_be_checked_is_never_all_fine()
    {
        Notice notice = Notifier.Summarize(Report(Pass("obs.captureHasMonitor"), NoObs));

        Assert.Equal("Chequeo previo: 1 sin comprobar", notice.Title);
        Assert.Equal("Sin OBS: no se pudo leer el micrófono.", notice.Text);
        Assert.True(notice.Warning);
    }

    [Fact]
    public void Failures_win_over_what_could_not_be_checked_and_the_title_adds_how_many_were_not()
    {
        // Con fallas y algo sin comprobar a la vez, como con un OBS que se cae a mitad del chequeo, manda la primera falla (01 §8), en
        // singular y sin "y 0 más"; el título suma cuántas quedaron sin comprobar, que antes no se veían (decisión 35).
        Notice notice = Notifier.Summarize(Report(NoObs, Canvas));

        Assert.Equal("Chequeo previo: 1 falla y 1 sin comprobar", notice.Title);
        Assert.Equal("La escena no llena el lienzo. \"Vertical SolidWorks\" llena el 31,8 % del lienzo de 1920 x 1080; se pide 90 %.", notice.Text);
        Assert.True(notice.Warning);
    }

    [Fact]
    public void Several_failures_and_several_unchecked_count_both_and_the_text_counts_only_the_other_failures()
    {
        // "y 1 más en el registro" son las otras fallas: lo sin comprobar ya va contado en el título (decisión 35).
        Notice notice = Notifier.Summarize(Report(NoObs, Canvas, NoObs, Pass("obs.captureHasMonitor"), Camera, NoObs));

        Assert.Equal("Chequeo previo: 2 fallas y 3 sin comprobar", notice.Title);
        Assert.EndsWith("se pide 90 %. (y 1 más en el registro)", notice.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_list_says_so()
    {
        Notice notice = Notifier.Summarize(Report());

        Assert.Equal("Chequeo previo: no hay comprobaciones", notice.Title);
        Assert.False(notice.Warning);
    }

    [Fact]
    public async Task A_balloon_that_throws_does_not_stop_the_log()
    {
        await using var rig = new NotifierRig(balloon: _ => throw new InvalidOperationException("bandeja cerrada"));

        rig.Notifier.OnPreflight(Report(Canvas));

        Assert.Contains(rig.Log.Recent(), entry => entry.Level == LogLevel.Error && entry.Message.Contains("bandeja cerrada", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Recent(), entry => entry.Message.StartsWith("Aviso preflight_result", StringComparison.Ordinal));
    }
}
