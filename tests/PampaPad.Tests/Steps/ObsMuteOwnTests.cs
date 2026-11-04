using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La marca del silencio propio (plan de la 9b, E9b-2): se pone antes de mandar SetInputMute, así el evento de OBS no le gana.</summary>
public class ObsMuteOwnTests
{
    private static readonly Invocation Press = new(Guid.NewGuid(), Trigger.Key, "console", "key.10", Gesture.Press, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    [Fact]
    public async Task The_mark_is_already_in_the_state_when_obs_receives_the_request()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.Zero));
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        DateTimeOffset? seen = DateTimeOffset.MinValue;
        session.Obs.OnRequest = type =>
        {
            if (type == ObsMute.RequestType)
            {
                seen = session.State.Current.Obs.OwnMuteAt;
            }
        };

        StepResult result = await new ObsMute(session.Client, session.State, clock).RunAsync(Parse("""{ "type": "obs.mute", "do": "mute" }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(clock.GetUtcNow(), seen);
    }

    [Fact]
    public async Task Another_input_does_not_mark_the_mic()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await new ObsMute(session.Client, session.State, new FakeTimeProvider()).RunAsync(
            Parse($$"""{ "type": "obs.mute", "do": "mute", "input": { "uuid": "{{Doubles.FakeObs.StingUuid}}", "name": "Sting triste" } }"""), Press, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Null(session.State.Current.Obs.OwnMuteAt);
    }
}
