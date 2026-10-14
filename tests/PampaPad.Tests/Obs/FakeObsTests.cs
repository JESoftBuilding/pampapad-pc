using System.Text.Json.Nodes;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class FakeObsTests
{
    private static int Code(JsonObject response) => (int)response["requestStatus"]!["code"]!;

    private static void AssertRecordEvent(JsonObject? message, string state, bool active, string? path)
    {
        Assert.Equal(5, (int)message!["op"]!);
        var d = message["d"]!;
        Assert.Equal("RecordStateChanged", (string)d["eventType"]!);
        Assert.Equal(64, (int)d["eventIntent"]!);
        Assert.Equal(state, (string)d["eventData"]!["outputState"]!);
        Assert.Equal(active, (bool)d["eventData"]!["outputActive"]!);
        Assert.Equal(path, (string?)d["eventData"]!["outputPath"]);
    }

    [Fact]
    public async Task Without_password_it_greets_identifies_and_answers_by_request_id()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);

        var hello = await raw.ReadAsync();
        await raw.SendAsync(ObsMessages.Identify(207, null));
        var identified = await raw.ReadAsync();
        var status = await raw.AskAsync("41", "GetRecordStatus");

        Assert.Equal(0, (int)hello!["op"]!);
        Assert.Equal("5.7.4", (string)hello["d"]!["obsWebSocketVersion"]!);
        Assert.Null(hello["d"]!["authentication"]);
        Assert.Equal(2, (int)identified!["op"]!);
        Assert.Equal(1, (int)identified["d"]!["negotiatedRpcVersion"]!);
        Assert.Equal("41", (string)status["requestId"]!);
        Assert.Equal(100, Code(status));
        Assert.False((bool)status["responseData"]!["outputActive"]!);
        Assert.Equal(1, fake.Identified);
    }

    [Fact]
    public async Task With_password_it_asks_for_authentication_and_accepts_the_right_one()
    {
        await using var fake = FakeObs.Start(password: "clave-de-prueba");
        await using var raw = await RawObs.ConnectAsync(fake.Address);

        var reply = await raw.IdentifyAsync("clave-de-prueba");

        Assert.Equal(2, (int)reply!["op"]!);
    }

    [Fact]
    public async Task A_wrong_password_closes_with_4009()
    {
        await using var fake = FakeObs.Start(password: "clave-de-prueba");
        await using var raw = await RawObs.ConnectAsync(fake.Address);

        var reply = await raw.IdentifyAsync("otra-clave");

        Assert.Null(reply);
        Assert.Equal(4009, raw.CloseCode);
        Assert.Equal(0, fake.Identified);
    }

    [Fact]
    public async Task Chapters_answer_what_was_measured()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var stopped = await raw.AskAsync("1", "CreateRecordChapter", new { chapterName = "relevamiento sin grabar" });
        await raw.AskAsync("2", "StartRecord");
        var recording = await raw.AskAsync("3", "CreateRecordChapter", new { chapterName = "short relevamiento" });
        await raw.AskAsync("4", "PauseRecord");
        var paused = await raw.AskAsync("5", "CreateRecordChapter", new { chapterName = "sacar en pausa" });
        await raw.AskAsync("6", "ResumeRecord");
        var resumed = await raw.AskAsync("7", "CreateRecordChapter", new { chapterName = "largo relevamiento" });
        await raw.AskAsync("8", "StopRecord");
        var after = await raw.AskAsync("9", "CreateRecordChapter", new { chapterName = "relevamiento detenido" });

        Assert.Equal(501, Code(stopped));
        Assert.Null(stopped["requestStatus"]!["comment"]);
        Assert.Equal(100, Code(recording));
        Assert.Equal(702, Code(paused));
        Assert.Equal("Verify that the output being used supports chapter markers.", (string)paused["requestStatus"]!["comment"]!);
        Assert.Equal(100, Code(resumed));
        Assert.Equal(501, Code(after));
        Assert.Equal(new[] { "short relevamiento", "largo relevamiento" }, fake.Chapters.ToArray());
    }

    [Fact]
    public async Task A_format_without_chapters_answers_702_while_recording()
    {
        await using var fake = FakeObs.Start();
        fake.ChaptersUnsupported = true;
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();
        await raw.AskAsync("1", "StartRecord");

        var reply = await raw.AskAsync("2", "CreateRecordChapter", new { chapterName = "short prueba" });

        Assert.Equal(702, Code(reply));
        Assert.Equal(FakeObs.ChapterComment, (string)reply["requestStatus"]!["comment"]!);
    }

    [Fact]
    public async Task Record_events_come_after_the_response_and_in_the_measured_order()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await raw.SendAsync(ObsMessages.Request("1", "StartRecord", null));
        var started = await raw.ReadAsync();
        AssertRecordEvent(await raw.ReadAsync(), "OBS_WEBSOCKET_OUTPUT_STARTING", active: false, path: null);
        AssertRecordEvent(await raw.ReadAsync(), "OBS_WEBSOCKET_OUTPUT_STARTED", active: true, path: FakeObs.TakePath);

        await raw.SendAsync(ObsMessages.Request("2", "PauseRecord", null));
        var paused = await raw.ReadAsync();
        AssertRecordEvent(await raw.ReadAsync(), "OBS_WEBSOCKET_OUTPUT_PAUSED", active: false, path: null);     // medido: false en la pausa
        var status = await raw.AskAsync("3", "GetRecordStatus");

        await raw.SendAsync(ObsMessages.Request("4", "ResumeRecord", null));
        var resumed = await raw.ReadAsync();
        AssertRecordEvent(await raw.ReadAsync(), "OBS_WEBSOCKET_OUTPUT_RESUMED", active: true, path: null);

        await raw.SendAsync(ObsMessages.Request("5", "StopRecord", null));
        var stopped = await raw.ReadAsync();
        AssertRecordEvent(await raw.ReadAsync(), "OBS_WEBSOCKET_OUTPUT_STOPPING", active: false, path: null);
        AssertRecordEvent(await raw.ReadAsync(), "OBS_WEBSOCKET_OUTPUT_STOPPED", active: false, path: FakeObs.TakePath);
        var idle = await raw.AskAsync("6", "GetRecordStatus");

        Assert.Equal(7, (int)started!["op"]!);
        Assert.Equal(7, (int)paused!["op"]!);
        Assert.Equal(7, (int)resumed!["op"]!);
        Assert.Equal(FakeObs.TakePath, (string)stopped!["d"]!["responseData"]!["outputPath"]!);
        Assert.True((bool)status["responseData"]!["outputActive"]!);        // en pausa, el pedido dice activo y pausado
        Assert.True((bool)status["responseData"]!["outputPaused"]!);
        Assert.False((bool)idle["responseData"]!["outputActive"]!);
        Assert.Equal(0, (long)idle["responseData"]!["outputDuration"]!);
        Assert.Equal(4689435, (long)idle["responseData"]!["outputBytes"]!);  // medido: quedan los bytes de la toma anterior
    }

    [Fact]
    public async Task Record_requests_answer_the_code_of_each_state()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var stopWhileStopped = await raw.AskAsync("1", "StopRecord");
        var resumeWhileStopped = await raw.AskAsync("2", "ResumeRecord");
        var pauseWhileStopped = await raw.AskAsync("3", "PauseRecord");
        await raw.AskAsync("4", "StartRecord");
        var startWhileRecording = await raw.AskAsync("5", "StartRecord");
        await raw.AskAsync("6", "PauseRecord");
        var pauseWhilePaused = await raw.AskAsync("7", "PauseRecord");

        Assert.Equal(501, Code(stopWhileStopped));
        Assert.Equal(503, Code(resumeWhileStopped));
        Assert.Equal(100, Code(pauseWhileStopped));         // sin grabar contesta 100 y no hace nada (03 §3)
        Assert.Equal(500, Code(startWhileRecording));
        Assert.Equal(502, Code(pauseWhilePaused));
    }

    [Fact]
    public async Task A_batch_answers_in_order()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await raw.SendAsync(ObsMessages.Batch("b1", [new ObsRequest("GetSceneList"), new ObsRequest("GetSpecialInputs"), new ObsRequest("NoExiste")]));
        var reply = await raw.ReadAsync();
        var results = reply!["d"]!["results"]!.AsArray();

        Assert.Equal(9, (int)reply["op"]!);
        Assert.Equal("b1", (string)reply["d"]!["requestId"]!);
        Assert.Equal("GetSceneList", (string)results[0]!["requestType"]!);
        Assert.Equal("Pantalla", (string)results[0]!["responseData"]!["currentProgramSceneName"]!);
        Assert.Equal("Mic/Aux", (string)results[1]!["responseData"]!["mic1"]!);
        Assert.Equal(204, (int)results[2]!["requestStatus"]!["code"]!);
    }

    [Fact]
    public async Task While_loading_every_request_answers_207()
    {
        await using var fake = FakeObs.Start();
        fake.Loading = true;
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var reply = await raw.AskAsync("1", "GetVersion");

        Assert.Equal(207, Code(reply));
        Assert.False((bool)reply["requestStatus"]!["result"]!);
    }

    [Fact]
    public async Task An_unanswered_request_does_not_block_the_next_one()
    {
        await using var fake = FakeObs.Start();
        fake.DoNotAnswer("GetSceneList");
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await raw.SendAsync(ObsMessages.Request("1", "GetSceneList", null));
        var reply = await raw.AskAsync("2", "GetStreamStatus");

        Assert.Equal("2", (string)reply["requestId"]!);
        Assert.Equal(1, fake.Times("GetSceneList"));        // llegó, pero nadie lo contestó
    }

    [Fact]
    public async Task What_happens_in_obs_reaches_the_client_as_events()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await fake.SetSceneAsync("Cámara", "3da67a13-6704-4c87-9e09-c8be28efdf20");
        var scene = await raw.ReadAsync();
        await fake.SetMicMutedAsync(true);
        var mute = await raw.ReadAsync();
        await fake.SetStreamingAsync(true);
        var stream = await raw.ReadAsync();
        var muted = await raw.AskAsync("1", "GetInputMute", new { inputName = "Mic/Aux" });

        Assert.Equal("CurrentProgramSceneChanged", (string)scene!["d"]!["eventType"]!);
        Assert.Equal("Cámara", (string)scene["d"]!["eventData"]!["sceneName"]!);
        Assert.Equal("InputMuteStateChanged", (string)mute!["d"]!["eventType"]!);
        Assert.Equal(FakeObs.MicUuid, (string)mute["d"]!["eventData"]!["inputUuid"]!);
        Assert.Equal("StreamStateChanged", (string)stream!["d"]!["eventType"]!);
        Assert.True((bool)muted["responseData"]!["inputMuted"]!);
    }

    [Fact]
    public async Task Asking_for_the_scene_already_on_air_sends_no_event()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await fake.SetSceneAsync(fake.SceneName, fake.SceneUuid);     // la que ya está al aire: OBS no avisa (E0-1)
        await fake.SetMicMutedAsync(true);
        var next = await raw.ReadAsync();

        Assert.Equal("InputMuteStateChanged", (string)next!["d"]!["eventType"]!);
        Assert.Equal("Pantalla", fake.SceneName);
    }

    [Fact]
    public async Task Sessions_can_be_closed_with_a_code()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await fake.CloseSessionsAsync(4011, "Session invalidated.");

        Assert.Null(await raw.ReadAsync());
        Assert.Equal(4011, raw.CloseCode);
    }

    [Fact]
    public async Task Sessions_can_be_dropped_without_a_close()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        fake.DropSessions();

        await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => raw.ReadAsync());
    }

    [Fact]
    public async Task It_picks_a_free_port_and_can_reopen_on_the_same_one()
    {
        var first = FakeObs.Start();
        var port = first.Port;
        await using (var raw = await RawObs.ConnectAsync(first.Address))
        {
            await raw.IdentifyAsync();
        }
        await first.DisposeAsync();

        await using var second = FakeObs.Start(port);
        await using var again = await RawObs.ConnectAsync(second.Address);
        var reply = await again.IdentifyAsync();

        Assert.NotEqual(0, port);
        Assert.Equal(port, second.Port);
        Assert.Equal(2, (int)reply!["op"]!);
    }
}
