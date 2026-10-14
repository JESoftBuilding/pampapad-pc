using System.Text.Json.Nodes;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsClientWatchTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task While_obs_loads_it_stays_loading_and_reads_again_every_second()
    {
        await using var fake = FakeObs.Start();
        fake.Loading = true;                                          // todo contesta 207
        await using var h = new ObsHarness(fake.Address);

        h.Start();
        await h.UntilLinkAsync(ObsLinkState.Loading);
        await Eventually.UntilAsync(() => fake.Times("GetVersion") == 1, "la primera lectura");
        await Eventually.AdvancingAsync(h.Clock, OneSecond, () => fake.Times("GetVersion") >= 3, "que reintente la lectura");
        var linkWhileLoading = h.Link;

        fake.Loading = false;
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);

        Assert.Equal(ObsLinkState.Loading, linkWhileLoading);
    }

    [Fact]
    public async Task During_a_collection_change_it_asks_nothing_and_then_reads_again()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);

        await fake.EmitAsync("CurrentSceneCollectionChanging", 2, new JsonObject { ["sceneCollectionName"] = "Sin Título" });
        await h.UntilLinkAsync(ObsLinkState.Switching);
        var asked = fake.Received.Count;
        var reply = await h.Client.RequestAsync("GetSceneList", null, CancellationToken.None);
        await Eventually.StepsAsync(h.Clock, OneSecond, 7);           // pasa de largo el latido de 5 s
        var askedWhileSwitching = fake.Received.Count;

        await fake.EmitAsync("CurrentSceneCollectionChanged", 2, new JsonObject { ["sceneCollectionName"] = "Otra" });
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);

        Assert.Equal("obs_disconnected", reply.Error);
        Assert.Equal(asked, askedWhileSwitching);                     // nada llegó a OBS durante el cambio
        Assert.True(fake.Times("GetVersion") >= 2);                   // y después releyó
    }

    [Fact]
    public async Task Stopped_it_asks_the_record_status_every_5_s_as_a_heartbeat()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        var before = fake.Times("GetRecordStatus");
        var start = h.Clock.GetUtcNow();

        await Eventually.StepsAsync(h.Clock, OneSecond, 4);
        var afterFourSeconds = fake.Times("GetRecordStatus");
        await Eventually.AdvancingAsync(h.Clock, OneSecond, () => fake.Times("GetRecordStatus") == before + 1, "el primer latido");

        Assert.Equal(before, afterFourSeconds);                       // a los 4 s todavía no preguntó
        Assert.InRange(h.Clock.GetUtcNow() - start, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8));
        Assert.Equal(ObsLinkState.Ready, h.Link);
    }

    [Fact]
    public async Task A_hung_socket_after_suspend_is_detected_and_rebuilt()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);

        fake.FreezeSessions();          // la PC "volvió": el socket sigue abierto y OBS ya no contesta por él
        await Eventually.AdvancingAsync(
            h.Clock,
            TimeSpan.FromMilliseconds(500),
            () => fake.Identified >= 2 && h.Link == ObsLinkState.Ready,
            "que el enlace colgado se rearme solo");
        await Eventually.UntilAsync(() => h.LinksSeen().Count(link => link == ObsLinkState.Ready) >= 2, "el aviso del segundo ready");

        var links = h.LinksSeen();
        var lost = links.IndexOf(ObsLinkState.Lost);
        Assert.True(lost > links.IndexOf(ObsLinkState.Ready), "tiene que pasar por lost después de haber estado ready");
        Assert.Equal(ObsLinkState.Ready, links[^1]);
    }
}
