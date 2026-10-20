using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class EnvelopeTests
{
    [Fact]
    public void A_request_opens_into_id_name_and_data()
    {
        Result<IncomingRequest> parsed = Envelope.Parse("""{ "id": 17, "request": "log.read", "data": { "count": 5 } }""");

        Assert.True(parsed.Ok);
        Assert.Equal(17, parsed.Value!.Id);
        Assert.Equal("log.read", parsed.Value.Request);
        Assert.Equal(5, parsed.Value.Data.GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("""{ "id": 1, "request": "app.snapshot" }""")]
    [InlineData("""{ "id": 1, "request": "app.snapshot", "data": null }""")]
    public void Without_data_the_data_is_an_empty_object(string json)
    {
        Result<IncomingRequest> parsed = Envelope.Parse(json);

        Assert.True(parsed.Ok);
        Assert.Equal(JsonValueKind.Object, parsed.Value!.Data.ValueKind);
        Assert.Empty(parsed.Value.Data.EnumerateObject());
    }

    [Theory]
    [InlineData("no es json", null)]
    [InlineData("[1, 2]", null)]
    [InlineData("""{ "request": "app.snapshot" }""", "id")]
    [InlineData("""{ "id": "17", "request": "app.snapshot" }""", "id")]
    [InlineData("""{ "id": 1.5, "request": "app.snapshot" }""", "id")]
    [InlineData("""{ "id": 17 }""", "request")]
    [InlineData("""{ "id": 17, "request": "" }""", "request")]
    [InlineData("""{ "id": 17, "request": 3 }""", "request")]
    public void A_broken_request_is_invalid_data(string json, string? where)
    {
        Result<IncomingRequest> parsed = Envelope.Parse(json);

        Assert.False(parsed.Ok);
        Assert.Equal(ErrorCodes.InvalidData, parsed.Failure?.Code);
        Assert.Equal(where, parsed.Failure?.Where);
    }

    [Theory]
    [InlineData("""{ "id": 9, "request": 3 }""", 9L)]
    [InlineData("no es json", 0L)]
    [InlineData("""{ "id": "9" }""", 0L)]
    public void The_id_of_a_broken_request_is_rescued_when_it_can_be(string json, long id)
    {
        Assert.Equal(id, Envelope.IdOf(json));
    }

    [Fact]
    public void A_response_is_id_ok_and_data()
    {
        Assert.Equal("""{"id":17,"ok":true,"data":{"cancelled":2}}""", Envelope.Response(17, new ActionCancelResponse(2)));
    }

    [Fact]
    public void An_error_is_the_example_of_the_spec_letter_by_letter()
    {
        string json = Envelope.Error(17, new Failure(ErrorCodes.ConfigInvalid, "La tecla 3 nombra una acción que no existe", "pages[0].keys[2].action"));

        Assert.Equal(
            """{"id":17,"ok":false,"error":{"code":"config_invalid","message":"La tecla 3 nombra una acción que no existe","where":"pages[0].keys[2].action"}}""",
            json);
    }

    [Fact]
    public void An_error_without_a_place_leaves_where_out()
    {
        Assert.Equal("""{"id":3,"ok":false,"error":{"code":"no_response","message":"Nada"}}""", Envelope.Error(3, new Failure(ErrorCodes.NoResponse, "Nada")));
    }

    [Fact]
    public void An_event_carries_name_seq_and_data()
    {
        Assert.Equal("""{"event":"state","seq":412,"data":{}}""", Envelope.Event(EventNames.State, 412, new NoData()));
    }

    [Fact]
    public void A_request_is_written_the_way_the_page_writes_it()
    {
        Assert.Equal("""{"id":1,"request":"log.read","data":{"count":5}}""", Envelope.Request(1, RequestNames.LogRead, new LogReadRequest(Count: 5)));
    }
}
