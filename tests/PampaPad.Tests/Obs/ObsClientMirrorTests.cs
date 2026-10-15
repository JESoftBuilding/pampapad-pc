using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsClientMirrorTests
{
    private const string CameraUuid = "3da67a13-6704-4c87-9e09-c8be28efdf20";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static Task UntilRecordingAsync(ObsHarness h, RecordingState recording) =>
        Eventually.UntilAsync(() => h.State.Current.Obs.Recording == recording, $"grabación {recording}");

    [Fact]
    public async Task On_connect_it_reads_recording_scene_and_mic()
    {
        await using var fake = FakeObs.Start();
        await fake.StartRecordingAsync();
        fake.RecordedMs = 9733;
        await fake.PauseRecordingAsync();
        await fake.SetMicMutedAsync(true);
        await fake.SetSceneAsync("Cámara", CameraUuid);

        await using var h = await ObsHarness.ReadyAsync(fake);       // el programa arranca con la toma en curso
        var obs = h.State.Current.Obs;

        Assert.Equal(RecordingState.Paused, obs.Recording);
        Assert.Equal(TimeSpan.FromMilliseconds(9733), obs.Recorded);
        Assert.Equal("Cámara", obs.SceneName);
        Assert.Equal(CameraUuid, obs.SceneUuid);
        Assert.Equal("Mic/Aux", obs.MicName);
        Assert.Equal(FakeObs.MicUuid, obs.MicUuid);
        Assert.True(obs.MicMuted);
        Assert.False(obs.Streaming);
        Assert.True(obs.SupportsMarkers);
    }

    [Fact]
    public async Task It_follows_the_take_by_events()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);

        await fake.StartRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Recording);
        await fake.PauseRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Paused);          // el evento de pausa trae outputActive en false
        await fake.ResumeRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Recording);
        await fake.StopRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Stopped);

        Assert.Contains("grabación: Paused", TestLogs.Text(h.Log));
    }

    [Fact]
    public async Task It_follows_scene_mic_and_streaming_by_events()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);

        await fake.SetSceneAsync("Cámara", CameraUuid);
        await fake.SetMicMutedAsync(true);
        await fake.SetStreamingAsync(true);

        await Eventually.UntilAsync(
            () => h.State.Current.Obs is { SceneName: "Cámara", MicMuted: true, Streaming: true },
            "escena, micrófono y transmisión en el estado");
    }

    [Fact]
    public async Task While_recording_it_asks_the_recorded_time_every_second()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        await fake.StartRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Recording);
        var start = h.Clock.GetUtcNow();

        fake.RecordedMs = 5000;
        await Eventually.AdvancingAsync(
            h.Clock,
            TimeSpan.FromSeconds(1),
            () => h.State.Current.Obs.Recorded == TimeSpan.FromSeconds(5),
            "el tiempo grabado en el estado");

        Assert.InRange(h.Clock.GetUtcNow() - start, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));    // detenido tardaría 5 s
    }

    [Fact]
    public async Task After_obs_restarts_the_state_is_read_again_and_the_next_marker_goes_in()
    {
        await using var fake = FakeObs.Start();
        await fake.StartRecordingAsync();
        await using var h = await ObsHarness.ReadyAsync(fake);
        var port = fake.Port;
        var before = h.State.Current.Obs.Recording;

        await fake.DisposeAsync();                                    // OBS se cerró con la toma en curso
        await h.AdvanceUntilLinkAsync(ObsLinkState.Closed);
        var whileClosed = h.State.Current.Obs.Recording;

        await using var reopened = FakeObs.Start(port);               // vuelve a abrir, detenido y en otra escena
        await reopened.SetSceneAsync("Cámara", CameraUuid);
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);
        await Eventually.UntilAsync(() => h.State.Current.Obs.SceneName == "Cámara", "la escena del OBS que volvió");
        var afterReopen = h.State.Current.Obs.Recording;

        await reopened.StartRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Recording);
        var marker = await h.Client.RequestAsync("CreateRecordChapter", new { chapterName = "short prueba" }, CancellationToken.None).WaitAsync(Limit);

        Assert.Equal(RecordingState.Recording, before);
        Assert.Equal(RecordingState.Stopped, whileClosed);            // en closed ni se graba ni se transmite
        Assert.Equal(RecordingState.Stopped, afterReopen);            // releído del OBS que volvió
        Assert.True(marker.Ok);
        Assert.Equal(100, marker.Code);
        Assert.Equal(new[] { "short prueba" }, reopened.Chapters.ToArray());       // la marca siguiente entró
    }

    [Fact]
    public async Task A_cut_with_obs_alive_keeps_the_last_known_take()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);
        await fake.StartRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Recording);

        fake.DropSessions();
        await h.UntilLinkAsync(ObsLinkState.Lost);

        Assert.Equal(RecordingState.Recording, h.State.Current.Obs.Recording);      // dato viejo: OBS puede seguir grabando
    }

    // No está en el plan: la lectura inicial es lo que reemplaza el dato viejo de lost (spec 03 §2).
    [Fact]
    public async Task After_a_cut_the_old_take_is_replaced_by_what_obs_says_now()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);
        await fake.StartRecordingAsync();
        await UntilRecordingAsync(h, RecordingState.Recording);
        fake.DropSessions();
        await h.UntilLinkAsync(ObsLinkState.Lost);

        await fake.StopRecordingAsync();                              // con el enlace cortado nadie se entera
        var whileLost = h.State.Current.Obs.Recording;
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);

        Assert.Equal(RecordingState.Recording, whileLost);
        Assert.Equal(RecordingState.Stopped, h.State.Current.Obs.Recording);
    }
}
