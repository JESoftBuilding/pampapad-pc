using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class FakeObsClockTests
{
    [Fact]
    public async Task With_a_fake_clock_STARTED_arrives_only_when_the_clock_moves()
    {
        // Una hora: con el reloj real no llegaría dentro de los 5 s de RawObs.ReadAsync, así que un OBS falso que ignorara su reloj
        // da rojo acá (revisión de E5-2: con 90 ms la prueba pasaba igual, a los 90 ms reales).
        var clock = new FakeTimeProvider();
        await using var fake = FakeObs.Start();
        fake.Clock = clock;
        fake.StartDelay = TimeSpan.FromHours(1);
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        Task acting = fake.StartRecordingAsync();
        var starting = await raw.ReadAsync();
        await Poll.Until(() => fake.Waiting == 1);

        // Antes de mover el reloj, lo próximo que llega es la respuesta a este pedido: el STARTED todavía no salió.
        await raw.SendAsync(ObsMessages.Request("antes", "GetRecordStatus", null));
        var before = await raw.ReadAsync();
        bool doneBefore = acting.IsCompleted;
        clock.Advance(TimeSpan.FromHours(1));
        var started = await raw.ReadAsync();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(doneBefore);
        Assert.Equal(ObsMessages.OpRequestResponse, (int)before!["op"]!);
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
