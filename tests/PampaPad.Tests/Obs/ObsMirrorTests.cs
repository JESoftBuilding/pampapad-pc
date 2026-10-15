using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsMirrorTests
{
    private const string MicUuid = "506b0448-d6b6-443e-83d9-5dd76fbf1d60";

    private static readonly ObsState Ready = ObsState.Initial with { Link = ObsLinkState.Ready, MicName = "Mic/Aux", MicUuid = MicUuid };

    private static readonly ObsResponse RecordPaused = Ok("""{"outputActive":true,"outputBytes":3355250,"outputDuration":9733,"outputPaused":true,"outputTimecode":"00:00:09.733"}""");
    private static readonly ObsResponse RecordStopped = Ok("""{"outputActive":false,"outputBytes":4689435,"outputDuration":0,"outputPaused":false,"outputTimecode":"00:00:00.000"}""");
    private static readonly ObsResponse StreamIdle = Ok("""{"outputActive":false,"outputBytes":0,"outputCongestion":0.0,"outputDuration":0,"outputReconnecting":false,"outputSkippedFrames":0,"outputTimecode":"00:00:00.000","outputTotalFrames":0}""");
    private static readonly ObsResponse SceneList = Ok("""{"currentPreviewSceneName":null,"currentPreviewSceneUuid":null,"currentProgramSceneName":"Pantalla","currentProgramSceneUuid":"f3637453-bc14-4e54-b4c5-1b76f4fb4a21","scenes":[]}""");
    private static readonly ObsResponse SpecialInputs = Ok("""{"desktop1":"Audio del escritorio","desktop2":null,"mic1":"Mic/Aux","mic2":null,"mic3":null,"mic4":null}""");
    private static readonly ObsResponse NoMic = Ok("""{"desktop1":"Audio del escritorio","desktop2":null,"mic1":null,"mic2":null,"mic3":null,"mic4":null}""");
    private static readonly ObsResponse InputList = Ok("""{"inputs":[{"inputKind":"wasapi_output_capture","inputName":"Audio del escritorio","inputUuid":"364259ab-1ec2-4e92-9a4e-5faca2f47d6f"},{"inputKind":"wasapi_input_capture","inputName":"Mic/Aux","inputUuid":"506b0448-d6b6-443e-83d9-5dd76fbf1d60"}]}""");

    private static JsonElement Data(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static ObsResponse Ok(string data) => new(true, 100, null, Data(data));

    private static ObsEvent Record(string outputState, bool outputActive) =>
        new("RecordStateChanged", 64, Data($$"""{"outputActive":{{(outputActive ? "true" : "false")}},"outputPath":null,"outputState":"{{outputState}}"}"""));

    private static ObsEvent StreamEvent(string outputState, bool outputActive) =>
        new("StreamStateChanged", 64, Data($$"""{"outputActive":{{(outputActive ? "true" : "false")}},"outputState":"{{outputState}}"}"""));

    private static ObsEvent Mute(string inputName, string inputUuid, bool muted) =>
        new("InputMuteStateChanged", 8, Data($$"""{"inputMuted":{{(muted ? "true" : "false")}},"inputName":"{{inputName}}","inputUuid":"{{inputUuid}}"}"""));

    [Fact]
    public void The_measured_take_goes_through_every_recording_state()
    {
        var states = new List<RecordingState>();
        var obs = Ready;
        foreach (var e in new[]
        {
            Record("OBS_WEBSOCKET_OUTPUT_STARTING", outputActive: false),
            Record("OBS_WEBSOCKET_OUTPUT_STARTED", outputActive: true),
            Record("OBS_WEBSOCKET_OUTPUT_PAUSED", outputActive: false),
            Record("OBS_WEBSOCKET_OUTPUT_RESUMED", outputActive: true),
            Record("OBS_WEBSOCKET_OUTPUT_STOPPING", outputActive: false),
            Record("OBS_WEBSOCKET_OUTPUT_STOPPED", outputActive: false),
        })
        {
            obs = ObsMirror.Apply(obs, e);
            states.Add(obs.Recording);
        }

        Assert.Equal(
            new[]
            {
                RecordingState.Starting, RecordingState.Recording, RecordingState.Paused,
                RecordingState.Recording, RecordingState.Stopping, RecordingState.Stopped,
            },
            states.ToArray());
    }

    [Fact]
    public void Paused_is_read_from_output_state_even_with_output_active_false()
    {
        var recording = Ready with { Recording = RecordingState.Recording };

        var paused = ObsMirror.Apply(recording, Record("OBS_WEBSOCKET_OUTPUT_PAUSED", outputActive: false));

        Assert.Equal(RecordingState.Paused, paused.Recording);
        Assert.True(paused.TakeInProgress);
    }

    [Fact]
    public void Started_resets_the_recorded_time()
    {
        var previous = Ready with { Recorded = TimeSpan.FromMinutes(34) };

        var started = ObsMirror.Apply(previous, Record("OBS_WEBSOCKET_OUTPUT_STARTED", outputActive: true));

        Assert.Equal(TimeSpan.Zero, started.Recorded);
    }

    [Fact]
    public void A_scene_change_updates_name_and_uuid()
    {
        var e = new ObsEvent("CurrentProgramSceneChanged", 4, Data("""{"sceneName":"Cámara","sceneUuid":"3da67a13-6704-4c87-9e09-c8be28efdf20"}"""));

        var obs = ObsMirror.Apply(Ready, e);

        Assert.Equal("Cámara", obs.SceneName);
        Assert.Equal("3da67a13-6704-4c87-9e09-c8be28efdf20", obs.SceneUuid);
    }

    [Fact]
    public void The_mute_follows_the_mic_and_no_other_input()
    {
        var muted = ObsMirror.Apply(Ready, Mute("Mic/Aux", MicUuid, muted: true));
        var other = ObsMirror.Apply(Ready, Mute("Dispositivo de captura de video", "0f0f0f0f-0000-0000-0000-000000000000", muted: true));

        Assert.True(muted.MicMuted);
        Assert.False(other.MicMuted);
    }

    [Fact]
    public void Streaming_is_true_from_starting_to_stopped_also_while_reconnecting()
    {
        var starting = ObsMirror.Apply(Ready, StreamEvent("OBS_WEBSOCKET_OUTPUT_STARTING", outputActive: false));
        var reconnecting = ObsMirror.Apply(starting, StreamEvent("OBS_WEBSOCKET_OUTPUT_RECONNECTING", outputActive: false));
        var stopped = ObsMirror.Apply(reconnecting, StreamEvent("OBS_WEBSOCKET_OUTPUT_STOPPED", outputActive: false));

        Assert.True(starting.Streaming);
        Assert.True(reconnecting.Streaming);
        Assert.False(stopped.Streaming);
    }

    // No está en el plan: el caso anterior arrastra el true de STARTING y no distingue si RECONNECTING cuenta por sí solo.
    [Theory]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STARTING")]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STARTED")]
    [InlineData("OBS_WEBSOCKET_OUTPUT_RECONNECTING")]
    [InlineData("OBS_WEBSOCKET_OUTPUT_RECONNECTED")]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STOPPING")]
    public void Each_stream_state_before_stopped_counts_as_streaming_on_its_own(string outputState)
    {
        var obs = ObsMirror.Apply(Ready, StreamEvent(outputState, outputActive: false));

        Assert.True(obs.Streaming);
    }

    [Fact]
    public void An_event_the_mirror_does_not_use_leaves_the_state_as_it_was()
    {
        var e = new ObsEvent("SceneTransitionStarted", 16, Data("""{"transitionName":"Desvanecimiento"}"""));

        Assert.Same(Ready, ObsMirror.Apply(Ready, e));
    }

    [Fact]
    public void The_initial_read_takes_recording_streaming_scene_and_mic()
    {
        var obs = ObsMirror.ApplyInitial(ObsState.Initial, [RecordPaused, StreamIdle, SceneList, SpecialInputs, InputList]);

        Assert.Equal(RecordingState.Paused, obs.Recording);       // el pedido, en pausa, dice activo y pausado
        Assert.Equal(TimeSpan.FromMilliseconds(9733), obs.Recorded);
        Assert.False(obs.Streaming);
        Assert.Equal("Pantalla", obs.SceneName);
        Assert.Equal("f3637453-bc14-4e54-b4c5-1b76f4fb4a21", obs.SceneUuid);
        Assert.Equal("Mic/Aux", obs.MicName);
        Assert.Equal(MicUuid, obs.MicUuid);
    }

    [Fact]
    public void The_initial_read_while_stopped_ignores_what_is_left_of_the_previous_take()
    {
        var before = Ready with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(12) };

        var obs = ObsMirror.ApplyInitial(before, [RecordStopped, StreamIdle, SceneList, SpecialInputs, InputList]);

        Assert.Equal(RecordingState.Stopped, obs.Recording);
        Assert.Equal(TimeSpan.Zero, obs.Recorded);
    }

    // No están en el plan. La spec 03 §5 dice que al conectar transmitir vale outputActive o outputReconnecting.
    [Theory]
    [InlineData("""{"outputActive":true,"outputReconnecting":false}""", true)]
    [InlineData("""{"outputActive":false,"outputReconnecting":true}""", true)]
    [InlineData("""{"outputActive":false,"outputReconnecting":false}""", false)]
    public void The_initial_read_counts_a_reconnecting_stream_as_streaming(string streamStatus, bool streaming)
    {
        var obs = ObsMirror.ApplyInitial(Ready, [RecordStopped, Ok(streamStatus), SceneList, SpecialInputs, InputList]);

        Assert.Equal(streaming, obs.Streaming);
    }

    [Fact]
    public void The_initial_read_while_stopped_ignores_a_duration_that_comes_with_it()
    {
        var staleDuration = Ok("""{"outputActive":false,"outputBytes":4689435,"outputDuration":12000,"outputPaused":false}""");

        var obs = ObsMirror.ApplyInitial(Ready, [staleDuration, StreamIdle, SceneList, SpecialInputs, InputList]);

        Assert.Equal(RecordingState.Stopped, obs.Recording);
        Assert.Equal(TimeSpan.Zero, obs.Recorded);
    }

    [Fact]
    public void The_initial_read_without_a_mic_leaves_it_unknown()
    {
        var obs = ObsMirror.ApplyInitial(Ready with { MicMuted = true }, [RecordStopped, StreamIdle, SceneList, NoMic, InputList]);

        Assert.Null(obs.MicName);
        Assert.Null(obs.MicUuid);
        Assert.False(obs.MicMuted);
    }

    [Fact]
    public void A_failed_item_of_the_initial_read_leaves_its_data_as_it_was()
    {
        var before = Ready with { SceneName = "Banco", SceneUuid = "900ebc25-03ef-443e-80cc-2ad8991268ff" };
        var failed = new ObsResponse(false, 600, "No source was found", default);

        var obs = ObsMirror.ApplyInitial(before, [RecordPaused, StreamIdle, failed, SpecialInputs, InputList]);

        Assert.Equal("Banco", obs.SceneName);
        Assert.Equal(RecordingState.Paused, obs.Recording);
    }

    [Fact]
    public void The_poll_updates_the_recorded_time_only_while_the_output_is_active()
    {
        var recording = Ready with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(4) };

        var polled = ObsMirror.ApplyRecordStatus(recording, RecordPaused);
        var stopped = ObsMirror.ApplyRecordStatus(recording, RecordStopped);
        var unanswered = ObsMirror.ApplyRecordStatus(recording, ObsResponse.Fail("obs_timeout"));

        Assert.Equal(TimeSpan.FromMilliseconds(9733), polled.Recorded);
        Assert.Equal(RecordingState.Recording, polled.Recording);     // el estado lo dan los eventos, no el sondeo
        Assert.Equal(TimeSpan.FromSeconds(4), stopped.Recorded);      // detenido, OBS contesta 0: no pisa nada
        Assert.Equal(TimeSpan.FromSeconds(4), unanswered.Recorded);
    }

    [Fact]
    public void The_version_tells_if_this_obs_has_chapters()
    {
        var with = Ok("""{"availableRequests":["GetVersion","CreateRecordChapter"],"obsWebSocketVersion":"5.7.4"}""");
        var without = Ok("""{"availableRequests":["GetVersion"],"obsWebSocketVersion":"5.4.2"}""");

        Assert.True(ObsMirror.ApplyVersion(Ready, with).SupportsMarkers);
        Assert.False(ObsMirror.ApplyVersion(Ready with { SupportsMarkers = true }, without).SupportsMarkers);
    }

    [Fact]
    public void The_mute_reply_sets_the_mute()
    {
        var obs = ObsMirror.ApplyMicMute(Ready, Ok("""{"inputMuted":true}"""));
        var failed = ObsMirror.ApplyMicMute(Ready with { MicMuted = true }, new ObsResponse(false, 604, "The specified input does not support audio.", default));

        Assert.True(obs.MicMuted);
        Assert.True(failed.MicMuted);
    }
}
