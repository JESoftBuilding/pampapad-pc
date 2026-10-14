using System.Text;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsMessagesTests
{
    private static ObsIncoming Parse(string json)
    {
        Assert.True(ObsMessages.TryParse(Encoding.UTF8.GetBytes(json), out var message));
        return message;
    }

    [Fact]
    public void Authentication_matches_the_example_of_the_protocol()
    {
        var value = ObsMessages.Authentication(
            "supersecretpassword",
            salt: "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=",
            challenge: "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=");

        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=", value);
    }

    [Fact]
    public void Identify_carries_version_subscriptions_and_authentication()
    {
        using var json = JsonDocument.Parse(ObsMessages.Identify(207, "abc+/="));
        var d = json.RootElement.GetProperty("d");

        Assert.Equal(1, json.RootElement.GetProperty("op").GetInt32());
        Assert.Equal(1, d.GetProperty("rpcVersion").GetInt32());
        Assert.Equal(207, d.GetProperty("eventSubscriptions").GetInt32());
        Assert.Equal("abc+/=", d.GetProperty("authentication").GetString());
    }

    [Fact]
    public void Identify_without_password_sends_no_authentication()
    {
        using var json = JsonDocument.Parse(ObsMessages.Identify(207, null));

        Assert.False(json.RootElement.GetProperty("d").TryGetProperty("authentication", out _));
    }

    [Fact]
    public void Request_carries_type_id_and_data()
    {
        using var json = JsonDocument.Parse(ObsMessages.Request("7", "CreateRecordChapter", new { chapterName = "short prueba" }));
        var d = json.RootElement.GetProperty("d");

        Assert.Equal(6, json.RootElement.GetProperty("op").GetInt32());
        Assert.Equal("CreateRecordChapter", d.GetProperty("requestType").GetString());
        Assert.Equal("7", d.GetProperty("requestId").GetString());
        Assert.Equal("short prueba", d.GetProperty("requestData").GetProperty("chapterName").GetString());
    }

    [Fact]
    public void Request_keeps_accents_and_symbols_of_a_marker_title()
    {
        const string title = "inicio cámara ñandú 🎬 \"con comillas\"";

        using var json = JsonDocument.Parse(ObsMessages.Request("9", "CreateRecordChapter", new { chapterName = title }));

        Assert.Equal(title, json.RootElement.GetProperty("d").GetProperty("requestData").GetProperty("chapterName").GetString());
    }

    [Fact]
    public void Request_without_data_sends_no_request_data()
    {
        using var json = JsonDocument.Parse(ObsMessages.Request("8", "StartRecord", null));

        Assert.False(json.RootElement.GetProperty("d").TryGetProperty("requestData", out _));
    }

    [Fact]
    public void Batch_carries_the_requests_in_order()
    {
        var text = ObsMessages.Batch("b1", [new ObsRequest("GetRecordStatus"), new ObsRequest("GetInputMute", new { inputName = "Mic/Aux" })]);
        using var json = JsonDocument.Parse(text);
        var d = json.RootElement.GetProperty("d");
        var requests = d.GetProperty("requests");

        Assert.Equal(8, json.RootElement.GetProperty("op").GetInt32());
        Assert.Equal("b1", d.GetProperty("requestId").GetString());
        Assert.Equal(2, requests.GetArrayLength());
        Assert.Equal("GetRecordStatus", requests[0].GetProperty("requestType").GetString());
        Assert.False(requests[0].TryGetProperty("requestData", out _));
        Assert.Equal("Mic/Aux", requests[1].GetProperty("requestData").GetProperty("inputName").GetString());
    }

    [Fact]
    public void A_failed_response_keeps_code_and_comment()
    {
        var message = Parse("""{"op":7,"d":{"requestId":"13","requestStatus":{"code":702,"comment":"Verify that the output being used supports chapter markers.","result":false},"requestType":"CreateRecordChapter"}}""");

        var response = ObsMessages.ToResponse(message.D);

        Assert.Equal(7, message.Op);
        Assert.Equal("13", ObsMessages.RequestId(message.D));
        Assert.False(response.Ok);
        Assert.Equal(702, response.Code);
        Assert.Equal("Verify that the output being used supports chapter markers.", response.Comment);
        Assert.Null(response.Error);                // OBS contestó: no es una falla del enlace
        Assert.Equal(JsonValueKind.Undefined, response.Data.ValueKind);
    }

    [Fact]
    public void A_response_with_data_is_read()
    {
        var message = Parse("""{"op":7,"d":{"requestId":"12","requestStatus":{"code":100,"result":true},"requestType":"GetRecordStatus","responseData":{"outputActive":true,"outputBytes":3355250,"outputDuration":9733,"outputPaused":true,"outputTimecode":"00:00:09.733"}}}""");

        var response = ObsMessages.ToResponse(message.D);

        Assert.True(response.Ok);
        Assert.Equal(100, response.Code);
        Assert.Null(response.Comment);
        Assert.True(ObsMessages.Flag(response.Data, "outputPaused"));
        Assert.Equal(9733, ObsMessages.Number(response.Data, "outputDuration"));
        Assert.Equal("00:00:09.733", ObsMessages.Text(response.Data, "outputTimecode"));
    }

    [Fact]
    public void An_event_is_read()
    {
        var message = Parse("""{"op":5,"d":{"eventData":{"outputActive":false,"outputPath":null,"outputState":"OBS_WEBSOCKET_OUTPUT_PAUSED"},"eventIntent":64,"eventType":"RecordStateChanged"}}""");

        var e = ObsMessages.ToEvent(message.D);

        Assert.Equal(5, message.Op);
        Assert.Equal("RecordStateChanged", e.Type);
        Assert.Equal(64, e.Intent);
        Assert.Equal("OBS_WEBSOCKET_OUTPUT_PAUSED", ObsMessages.Text(e.Data, "outputState"));
        Assert.False(ObsMessages.Flag(e.Data, "outputActive"));
        Assert.Null(ObsMessages.Text(e.Data, "outputPath"));
    }

    [Fact]
    public void The_greeting_is_read_with_and_without_a_challenge()
    {
        var open = ObsMessages.ToHello(Parse("""{"op":0,"d":{"obsStudioVersion":"32.2.2","obsWebSocketVersion":"5.7.4","rpcVersion":1}}""").D);
        var locked = ObsMessages.ToHello(Parse("""{"op":0,"d":{"authentication":{"challenge":"c","salt":"s"},"obsStudioVersion":"32.2.2","obsWebSocketVersion":"5.7.4","rpcVersion":1}}""").D);

        Assert.Equal("32.2.2", open.ObsVersion);
        Assert.Equal("5.7.4", open.WebSocketVersion);
        Assert.Equal(1, open.RpcVersion);
        Assert.Null(open.Challenge);
        Assert.Null(open.Salt);
        Assert.Equal("c", locked.Challenge);
        Assert.Equal("s", locked.Salt);
    }

    [Fact]
    public void A_batch_reply_gives_one_response_per_request()
    {
        var message = Parse("""{"op":9,"d":{"requestId":"b1","results":[{"requestStatus":{"code":100,"result":true},"requestType":"GetRecordStatus","responseData":{"outputActive":false}},{"requestStatus":{"code":207,"comment":"OBS is not ready to perform the request.","result":false},"requestType":"GetSceneList"}]}}""");

        var results = ObsMessages.ToBatch(message.D);

        Assert.Equal("b1", ObsMessages.RequestId(message.D));
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Ok);
        Assert.Equal(207, results[1].Code);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"d":{}}""")]
    [InlineData("""{"op":"siete","d":{}}""")]
    [InlineData("")]
    [InlineData("""{"op":7,"d":""")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("""{"op":1.5,"d":{}}""")]
    [InlineData("""{"op":99999999999,"d":{}}""")]
    public void What_is_not_a_message_is_rejected(string text)
    {
        Assert.False(ObsMessages.TryParse(Encoding.UTF8.GetBytes(text), out _));
    }

    [Fact]
    public void Supports_looks_in_the_available_requests()
    {
        var version = ObsMessages.ToResponse(Parse("""{"op":7,"d":{"requestId":"1","requestStatus":{"code":100,"result":true},"requestType":"GetVersion","responseData":{"availableRequests":["GetVersion","CreateRecordChapter"]}}}""").D);

        Assert.True(ObsMessages.Supports(version, "CreateRecordChapter"));
        Assert.False(ObsMessages.Supports(version, "SplitRecordFile"));
        Assert.False(ObsMessages.Supports(ObsResponse.Fail("obs_timeout"), "CreateRecordChapter"));
    }

    [Fact]
    public void Fail_means_obs_never_answered()
    {
        var response = ObsResponse.Fail("obs_timeout");

        Assert.False(response.Ok);
        Assert.Equal(0, response.Code);
        Assert.Equal("obs_timeout", response.Error);
    }
}
