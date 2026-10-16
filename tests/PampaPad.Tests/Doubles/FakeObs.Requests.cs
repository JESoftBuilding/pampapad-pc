using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

// Lo que contesta cada pedido, con la forma de obs-websocket. El estado que consultan está en FakeObs.State.cs.
public sealed partial class FakeObs
{
    private static JsonObject Scene(int index, string name, string uuid) =>
        new() { ["sceneIndex"] = index, ["sceneName"] = name, ["sceneUuid"] = uuid };

    private static JsonObject Input(string name, string uuid, string kind) =>
        new() { ["inputKind"] = kind, ["inputName"] = name, ["inputUuid"] = uuid, ["unversionedInputKind"] = kind };

    private static JsonObject RecordEvent(string state, bool active, string? path) =>
        new() { ["outputActive"] = active, ["outputPath"] = path, ["outputState"] = state };

    private async Task AnswerRequestAsync(Session session, JsonObject d)
    {
        var type = (string)d["requestType"]!;
        Received.Enqueue(type);
        if (_silent.ContainsKey(type))
        {
            return;
        }

        var (result, events) = Run(type, d["requestData"] as JsonObject);
        result["requestId"] = (string)d["requestId"]!;
        await session.SendAsync(Envelope(7, result), _stop.Token);
        await EmitAllAsync(events);
    }

    private async Task AnswerBatchAsync(Session session, JsonObject d)
    {
        var results = new JsonArray();
        var events = new List<FakeEvent>();
        foreach (var item in d["requests"]!.AsArray())
        {
            var request = item!.AsObject();
            var type = (string)request["requestType"]!;
            Received.Enqueue(type);
            var (result, after) = Run(type, request["requestData"] as JsonObject);
            results.Add(result);
            events.AddRange(after);
        }

        var reply = new JsonObject { ["requestId"] = (string)d["requestId"]!, ["results"] = results };
        await session.SendAsync(Envelope(9, reply), _stop.Token);
        await EmitAllAsync(events);
    }

    private async Task EmitAllAsync(List<FakeEvent> events)
    {
        foreach (var e in events)
        {
            if (e.After > TimeSpan.Zero)
            {
                await DelayAsync(e.After);
            }

            await EmitAsync(e.Type, e.Intent, e.Data);
        }
    }

    // Atiende un pedido y devuelve el resultado con la forma de obs-websocket, más los eventos que le siguen.
    private (JsonObject Result, List<FakeEvent> Events) Run(string type, JsonObject? data)
    {
        Reply reply;
        lock (_sync)
        {
            reply = Loading ? Reply.Fail(207, "OBS is not ready to perform the request.") : Answer(type, data);
        }

        var status = new JsonObject { ["code"] = reply.Code, ["result"] = reply.Code == 100 };
        if (reply.Comment is not null)
        {
            status["comment"] = reply.Comment;
        }

        var result = new JsonObject { ["requestStatus"] = status, ["requestType"] = type };
        if (reply.Data is not null)
        {
            result["responseData"] = reply.Data;
        }

        return (result, reply.Events);
    }

    private Reply Answer(string type, JsonObject? data) => type switch
    {
        "GetVersion" => Reply.Ok(new JsonObject
        {
            ["availableRequests"] = new JsonArray(AvailableRequests.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["obsStudioVersion"] = "32.2.2",
            ["obsWebSocketVersion"] = "5.7.4",
            ["platform"] = "windows",
            ["rpcVersion"] = 1,
        }),
        "GetRecordStatus" => Reply.Ok(new JsonObject
        {
            ["outputActive"] = RecordingActive,
            ["outputBytes"] = RecordingActive ? RecordedMs * 345 : _lastTakeBytes,
            ["outputDuration"] = RecordingActive ? RecordedMs : 0,
            ["outputPaused"] = RecordingPaused,
            ["outputTimecode"] = TimeSpan.FromMilliseconds(RecordingActive ? RecordedMs : 0).ToString(@"hh\:mm\:ss\.fff"),
        }),
        "GetStreamStatus" => Reply.Ok(new JsonObject
        {
            ["outputActive"] = Streaming,
            ["outputBytes"] = 0,
            ["outputDuration"] = 0,
            ["outputReconnecting"] = false,
            ["outputTimecode"] = "00:00:00.000",
        }),
        "GetSceneList" => Reply.Ok(SceneList()),
        "GetSpecialInputs" => Reply.Ok(new JsonObject
        {
            ["desktop1"] = "Audio del escritorio",
            ["desktop2"] = null,
            ["mic1"] = HasMic ? MicName : null,
            ["mic2"] = null,
        }),
        "GetInputList" => Reply.Ok(InputList()),
        "GetInputMute" => InputMute(data),
        "SetInputMute" => SetInputMute(data),
        "SetCurrentProgramScene" => SetProgramScene(data),
        "GetStreamServiceSettings" => Reply.Ok(StreamServiceSettings()),
        "StartRecord" => StartRecord(),
        "StopRecord" => StopRecord(),
        "PauseRecord" => PauseRecord(),
        "ResumeRecord" => ResumeRecord(),
        "CreateRecordChapter" => Chapter(data),
        _ => Reply.Fail(204, "Your request type is not valid."),
    };

    private Reply StartRecord()
    {
        if (RecordingActive)
        {
            return Reply.Fail(500);
        }

        RecordingActive = true;
        RecordingPaused = false;
        RecordedMs = 0;
        return Reply.Ok()
            .Then("RecordStateChanged", OutputsIntent, RecordEvent("OBS_WEBSOCKET_OUTPUT_STARTING", active: false, path: null))
            .Then("RecordStateChanged", OutputsIntent, RecordEvent("OBS_WEBSOCKET_OUTPUT_STARTED", active: true, path: TakePath), StartDelay);
    }

    private Reply StopRecord()
    {
        if (!RecordingActive)
        {
            return Reply.Fail(501);
        }

        RecordingActive = false;
        RecordingPaused = false;
        _lastTakeBytes = MeasuredTakeBytes;
        return Reply.Ok(new JsonObject { ["outputPath"] = TakePath })
            .Then("RecordStateChanged", OutputsIntent, RecordEvent("OBS_WEBSOCKET_OUTPUT_STOPPING", active: false, path: null))
            .Then("RecordStateChanged", OutputsIntent, RecordEvent("OBS_WEBSOCKET_OUTPUT_STOPPED", active: false, path: TakePath), StopDelay);
    }

    private Reply PauseRecord()
    {
        if (!RecordingActive)
        {
            return Reply.Ok();            // sin grabar contesta 100 y no hace nada (spec 03 §3)
        }

        if (RecordingPaused)
        {
            return Reply.Fail(502);
        }

        RecordingPaused = true;
        return Reply.Ok().Then("RecordStateChanged", OutputsIntent, RecordEvent("OBS_WEBSOCKET_OUTPUT_PAUSED", active: false, path: null));
    }

    private Reply ResumeRecord()
    {
        if (!RecordingPaused)
        {
            return Reply.Fail(503);
        }

        RecordingPaused = false;
        return Reply.Ok().Then("RecordStateChanged", OutputsIntent, RecordEvent("OBS_WEBSOCKET_OUTPUT_RESUMED", active: true, path: null));
    }

    private Reply Chapter(JsonObject? data)
    {
        if (!RecordingActive)
        {
            return Reply.Fail(501);                               // medido: sin comentario
        }

        if (RecordingPaused || ChaptersUnsupported)
        {
            return Reply.Fail(702, ChapterComment);
        }

        Chapters.Add((string?)data?["chapterName"] ?? string.Empty);
        return Reply.Ok();
    }

    private sealed record FakeEvent(string Type, int Intent, JsonObject Data, TimeSpan After);

    private sealed record Reply(int Code, string? Comment = null, JsonObject? Data = null)
    {
        public List<FakeEvent> Events { get; } = [];

        public static Reply Ok(JsonObject? data = null) => new(100, null, data);

        public static Reply Fail(int code, string? comment = null) => new(code, comment);

        public Reply Then(string type, int intent, JsonObject data, TimeSpan after = default)
        {
            Events.Add(new FakeEvent(type, intent, data, after));
            return this;
        }
    }
}
