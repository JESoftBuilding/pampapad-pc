using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class ObsClientHandshakeTests
{
    private const string Password = "clave-de-prueba";

    [Fact]
    public async Task Connects_without_password_and_becomes_ready()
    {
        await using var fake = FakeObs.Start();
        await using var h = new ObsHarness(fake.Address);

        h.Start();
        await h.UntilLinkAsync(ObsLinkState.Ready);

        Assert.Equal(1, fake.Identified);
        Assert.Equal(1, fake.Times("GetVersion"));
        Assert.True(h.State.Current.Obs.SupportsMarkers);
    }

    [Fact]
    public async Task Connects_with_the_password_it_is_given()
    {
        await using var fake = FakeObs.Start(password: Password);
        await using var h = new ObsHarness(fake.Address, Password);

        h.Start();
        await h.UntilLinkAsync(ObsLinkState.Ready);

        Assert.Equal(1, fake.Identified);
    }

    [Fact]
    public async Task An_obs_without_chapters_is_noted_in_the_state()
    {
        await using var fake = FakeObs.Start();
        fake.AvailableRequests.Remove("CreateRecordChapter");
        await using var h = new ObsHarness(fake.Address);

        h.Start();
        await h.UntilLinkAsync(ObsLinkState.Ready);

        Assert.False(h.State.Current.Obs.SupportsMarkers);
    }

    [Fact]
    public async Task The_password_never_reaches_the_log()
    {
        await using var fake = FakeObs.Start(password: Password);
        await using var h = new ObsHarness(fake.Address, Password);

        h.Start();
        await h.UntilLinkAsync(ObsLinkState.Ready);

        var text = TestLogs.Text(h.Log);
        Assert.Contains("enlace con OBS", text);        // el registro tiene líneas del enlace: la prueba no pasa de vacía
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain(ObsMessages.Authentication(Password, FakeObs.Salt, FakeObs.Challenge), text);
    }
}
