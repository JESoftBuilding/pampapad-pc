using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsClientRequestTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Without_a_ready_link_a_request_fails_disconnected_and_nothing_is_sent()
    {
        await using var fake = FakeObs.Start();
        await using var h = new ObsHarness(fake.Address);       // sin Start: el enlace sigue en closed

        var reply = await h.Client.RequestAsync("GetRecordStatus", null, CancellationToken.None);

        Assert.False(reply.Ok);
        Assert.Equal("obs_disconnected", reply.Error);
        Assert.Empty(fake.Received);
    }

    [Fact]
    public async Task Each_response_goes_back_to_its_request_by_id()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        fake.DoNotAnswer("GetSceneList");
        var asked = fake.Times("GetSceneList");                 // la lectura inicial ya pidió una

        var slow = h.Client.RequestAsync("GetSceneList", null, CancellationToken.None);
        await Eventually.UntilAsync(() => fake.Times("GetSceneList") == asked + 1, "que el pedido sin respuesta llegue a OBS");
        var status = await h.Client.RequestAsync("GetRecordStatus", null, CancellationToken.None).WaitAsync(Limit);
        var special = await h.Client.RequestAsync("GetSpecialInputs", null, CancellationToken.None).WaitAsync(Limit);

        Assert.True(status.Ok);
        Assert.Equal(100, status.Code);
        Assert.False(status.Data.GetProperty("outputActive").GetBoolean());
        Assert.Equal("Mic/Aux", special.Data.GetProperty("mic1").GetString());
        Assert.False(slow.IsCompleted);                 // el que no tuvo respuesta sigue esperando la suya
    }

    [Fact]
    public async Task A_failure_of_obs_comes_back_with_its_code_and_comment()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        await fake.StartRecordingAsync();
        await fake.PauseRecordingAsync();

        var reply = await h.Client.RequestAsync("CreateRecordChapter", new { chapterName = "sacar en pausa" }, CancellationToken.None).WaitAsync(Limit);

        Assert.False(reply.Ok);
        Assert.Equal(702, reply.Code);
        Assert.Equal(FakeObs.ChapterComment, reply.Comment);
        Assert.Null(reply.Error);                       // OBS contestó: no es una falla del enlace
    }

    [Fact]
    public async Task A_request_without_an_answer_times_out_at_2_s()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        fake.DoNotAnswer("GetSceneList");
        var asked = fake.Times("GetSceneList");                 // la lectura inicial ya pidió una

        var asking = h.Client.RequestAsync("GetSceneList", null, CancellationToken.None);
        await Eventually.UntilAsync(() => fake.Times("GetSceneList") == asked + 1, "que el pedido llegue a OBS");

        h.Clock.Advance(TimeSpan.FromMilliseconds(1900));
        await Task.Delay(50);
        Assert.False(asking.IsCompleted);               // a los 1,9 s todavía espera

        h.Clock.Advance(TimeSpan.FromMilliseconds(100));
        var reply = await asking.WaitAsync(Limit);

        Assert.False(reply.Ok);
        Assert.Equal("obs_timeout", reply.Error);
        Assert.Equal(ObsLinkState.Ready, h.Link);       // uno solo no alcanza para dar el enlace por perdido
    }

    [Fact]
    public async Task A_cancelled_request_comes_back_as_cancelled()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        fake.DoNotAnswer("GetSceneList");
        var asked = fake.Times("GetSceneList");                 // la lectura inicial ya pidió una
        using var cancel = new CancellationTokenSource();

        var asking = h.Client.RequestAsync("GetSceneList", null, cancel.Token);
        await Eventually.UntilAsync(() => fake.Times("GetSceneList") == asked + 1, "que el pedido llegue a OBS");
        await cancel.CancelAsync();
        var reply = await asking.WaitAsync(Limit);

        Assert.Equal("cancelled", reply.Error);
    }

    [Fact]
    public async Task Events_reach_the_subscriber_in_order()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        var seen = new List<ObsEvent>();
        h.Client.Event += e =>
        {
            lock (seen)
            {
                seen.Add(e);
            }
        };

        await fake.SetSceneAsync("Cámara", "3da67a13-6704-4c87-9e09-c8be28efdf20");
        await fake.SetMicMutedAsync(true);
        await Eventually.UntilAsync(
            () =>
            {
                lock (seen)
                {
                    return seen.Count == 2;
                }
            },
            "que lleguen los dos eventos");

        lock (seen)
        {
            Assert.Equal("CurrentProgramSceneChanged", seen[0].Type);
            Assert.Equal(4, seen[0].Intent);
            Assert.Equal("Cámara", seen[0].Data.GetProperty("sceneName").GetString());
            Assert.Equal("InputMuteStateChanged", seen[1].Type);
            Assert.True(seen[1].Data.GetProperty("inputMuted").GetBoolean());
        }
    }

    [Fact]
    public async Task A_throwing_event_subscriber_does_not_cut_the_link()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        h.Client.Event += _ => throw new InvalidOperationException("roto a propósito");

        await fake.SetMicMutedAsync(true);
        var reply = await h.Client.RequestAsync("GetRecordStatus", null, CancellationToken.None).WaitAsync(Limit);

        Assert.True(reply.Ok);
        Assert.Equal(ObsLinkState.Ready, h.Link);
        Assert.Contains("internal_error", TestLogs.Text(h.Log));
    }
}
