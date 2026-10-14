using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsClientReconnectTests
{
    private const string Password = "clave-de-prueba";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HalfSecond = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task If_obs_is_not_there_it_retries_and_connects_when_it_opens()
    {
        var port = FakeObs.FreePort();
        await using var h = new ObsHarness(new Uri($"ws://127.0.0.1:{port}")) { ObsRunning = true };

        h.Start();
        await h.AdvanceUntilLinkAsync(ObsLinkState.NoWebsocket);      // hay proceso obs64 y el puerto rechaza

        await using var fake = FakeObs.Start(port);                   // OBS termina de abrir
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);

        Assert.True(fake.Identified >= 1);
        Assert.True(fake.Times("GetVersion") >= 1);
    }

    [Fact]
    public async Task When_obs_closes_and_reopens_it_reconnects_and_reads_again()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake);
        var port = fake.Port;

        await fake.DisposeAsync();                                    // OBS se cerró: no queda proceso
        await h.AdvanceUntilLinkAsync(ObsLinkState.Closed);

        await using var reopened = FakeObs.Start(port);
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);
        var reply = await h.Client.RequestAsync("GetRecordStatus", null, CancellationToken.None).WaitAsync(Limit);

        Assert.True(reopened.Times("GetVersion") >= 1);               // volvió a leer
        Assert.True(reply.Ok);                                        // y el pedido siguiente entra
    }

    [Fact]
    public async Task A_cut_with_obs_alive_is_lost_and_pending_requests_end_disconnected()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);
        fake.DoNotAnswer("GetSceneList");
        var asked = fake.Times("GetSceneList");                       // la lectura inicial ya pidió una
        var pending = h.Client.RequestAsync("GetSceneList", null, CancellationToken.None);
        await Eventually.UntilAsync(() => fake.Times("GetSceneList") == asked + 1, "que el pedido llegue a OBS");

        fake.DropSessions();                                          // corte seco, sin ExitStarted
        var reply = await pending.WaitAsync(Limit);
        await h.UntilLinkAsync(ObsLinkState.Lost);
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);            // el puerto sigue abierto: reconecta sola

        Assert.Equal("obs_disconnected", reply.Error);
        Assert.True(fake.Identified >= 2);
    }

    [Fact]
    public async Task A_rejected_password_does_not_retry_until_the_obs_file_changes()
    {
        await using var fake = FakeObs.Start(password: Password);
        await using var h = new ObsHarness(fake.Address, "otra-clave");

        h.Start();
        await h.UntilLinkAsync(ObsLinkState.AuthFailed);
        var attempts = fake.Accepted;
        await Eventually.StepsAsync(h.Clock, HalfSecond, 20);         // 10 s
        var later = fake.Accepted;

        h.Password = Password;                                        // cambió el config.json de OBS
        await h.AdvanceUntilLinkAsync(ObsLinkState.Ready);

        var text = TestLogs.Text(h.Log);
        Assert.Equal(attempts, later);                                // no reintenta en bucle
        Assert.Contains("AuthFailed", text);
        Assert.DoesNotContain("otra-clave", text);
        Assert.DoesNotContain(Password, text);
    }

    [Fact]
    public async Task Reconnect_retries_after_a_rejected_password()
    {
        await using var fake = FakeObs.Start(password: Password);
        await using var h = new ObsHarness(fake.Address, "otra-clave");
        h.Start();
        await h.UntilLinkAsync(ObsLinkState.AuthFailed);

        fake.Password = "otra-clave";                                 // ahora OBS acepta la que el programa tiene
        h.Client.Reconnect();

        await h.UntilLinkAsync(ObsLinkState.Ready);
    }

    [Fact]
    public async Task A_kicked_session_does_not_reconnect_until_asked()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);

        await fake.CloseSessionsAsync(4011, "Session invalidated.");
        await h.UntilLinkAsync(ObsLinkState.Kicked);
        await Eventually.StepsAsync(h.Clock, HalfSecond, 20);         // 10 s
        var linkAfterWaiting = h.Link;
        var identifiedAfterWaiting = fake.Identified;

        h.Client.Reconnect();
        await h.UntilLinkAsync(ObsLinkState.Ready);

        Assert.Equal(ObsLinkState.Kicked, linkAfterWaiting);
        Assert.Equal(1, identifiedAfterWaiting);
        Assert.Equal(2, fake.Identified);
    }

    [Fact]
    public async Task ExitStarted_means_exiting_and_closed_once_the_process_is_gone()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);

        await fake.AnnounceExitAsync();
        await h.UntilLinkAsync(ObsLinkState.Exiting);

        await fake.DisposeAsync();                                    // OBS cerró el socket y el proceso sigue
        await Eventually.StepsAsync(h.Clock, HalfSecond, 12);         // 6 s
        var linkWhileProcessLives = h.Link;

        h.ObsRunning = false;                                         // el proceso desapareció
        await h.AdvanceUntilLinkAsync(ObsLinkState.Closed);

        Assert.Equal(ObsLinkState.Exiting, linkWhileProcessLives);
    }

    [Fact]
    public async Task Reconnect_while_connected_rebuilds_the_link_at_once()
    {
        await using var fake = FakeObs.Start();
        await using var h = await ObsHarness.ReadyAsync(fake, obsRunning: true);

        h.Client.Reconnect();                                         // lo que hace el aviso de reanudación de Windows

        await Eventually.UntilAsync(() => fake.Identified == 2 && h.Link == ObsLinkState.Ready, "que el enlace se rearme sin esperar el reintento");
        await Eventually.UntilAsync(() => h.LinksSeen().Contains(ObsLinkState.Lost), "el aviso del paso por lost");
    }

    [Fact]
    public async Task Cancelling_ends_the_loop()
    {
        await using var fake = FakeObs.Start();
        var h = await ObsHarness.ReadyAsync(fake);

        await h.StopAsync();

        Assert.True(h.Loop.IsCompletedSuccessfully);
        Assert.DoesNotContain("se cortó sin cierre", TestLogs.Text(h.Log));     // apagar el programa no es un corte del enlace
    }
}
