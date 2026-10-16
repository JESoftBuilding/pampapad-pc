using Microsoft.Extensions.Time.Testing;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class FakeObsClockTests
{
    [Fact]
    public async Task With_a_fake_clock_STARTED_arrives_only_when_the_clock_moves()
    {
        var clock = new FakeTimeProvider();
        await using var fake = FakeObs.Start();
        fake.Clock = clock;
        fake.StartDelay = TimeSpan.FromMilliseconds(90);
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        Task acting = fake.StartRecordingAsync();
        var starting = await raw.ReadAsync();
        await Poll.Until(() => fake.Waiting == 1);
        bool doneBefore = acting.IsCompleted;
        clock.Advance(TimeSpan.FromMilliseconds(90));
        var started = await raw.ReadAsync();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(doneBefore);
        Assert.Equal("OBS_WEBSOCKET_OUTPUT_STARTING", (string)starting!["d"]!["eventData"]!["outputState"]!);
        Assert.Equal("OBS_WEBSOCKET_OUTPUT_STARTED", (string)started!["d"]!["eventData"]!["outputState"]!);
        Assert.Equal(0, fake.Waiting);
    }

    [Fact]
    public async Task Without_a_clock_the_delays_are_real_as_before()
    {
        await using var fake = FakeObs.Start();
        fake.StartDelay = TimeSpan.FromMilliseconds(20);
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await fake.StartRecordingAsync();
        await raw.ReadAsync();
        var started = await raw.ReadAsync();

        Assert.Same(TimeProvider.System, fake.Clock);
        Assert.Equal("OBS_WEBSOCKET_OUTPUT_STARTED", (string)started!["d"]!["eventData"]!["outputState"]!);
    }
}
