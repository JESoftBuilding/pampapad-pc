using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La copia a las luces de la PC (plan de la 1.6.0, decisión 2): el paso lights y la perilla le pasan el mismo LightCommand, también sin
/// consola, y el resultado sigue siendo el de la consola.
/// </summary>
public class PcLightsCopyTests
{
    private const string KnobConfig = """{ "version": 1, "pages": [ { "id": "luces", "knob": "brightness", "keys": [] } ] }""";

    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.3", Gesture.Tap, 0);

    private readonly ConcurrentQueue<LightCommand> _copied = new();

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static Step Red => Parse("""{ "type": "lights", "command": "color", "color": "#FF0000" }""");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_step_copies_the_command_and_its_result_is_the_one_of_the_console(bool consoleOk)
    {
        Result reply = consoleOk ? Result.Success : Result.Fail(ErrorCodes.ConsoleDisconnected, "La consola no está conectada.");
        var lights = new Lights((_, _) => Task.FromResult(reply), _copied.Enqueue);

        StepResult result = await lights.RunAsync(Red, Tap, CancellationToken.None);

        Assert.Equal(new LightCommand(1, 255, 0, 0, 0), Assert.Single(_copied));
        Assert.Equal(reply.Failure, result.Failure);
    }

    [Fact]
    public async Task Without_console_it_copies_too_and_the_step_is_console_disconnected_as_today()
    {
        StepResult result = await new Lights(null, _copied.Enqueue).RunAsync(Red, Tap, CancellationToken.None);

        Assert.Single(_copied);
        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
    }

    [Fact]
    public async Task The_copy_does_not_wait_for_the_console_and_a_copy_that_throws_changes_nothing()
    {
        var reply = new TaskCompletionSource<Result>();
        var lights = new Lights((_, _) => reply.Task, command =>
        {
            _copied.Enqueue(command);
            throw new InvalidOperationException("roto");
        });

        Task<StepResult> running = lights.RunAsync(Red, Tap, CancellationToken.None);

        Assert.Single(_copied);
        Assert.False(running.IsCompleted);
        reply.SetResult(Result.Success);
        Assert.Null((await running).Failure);
    }

    [Fact]
    public async Task Checking_or_an_invalid_step_does_not_copy()
    {
        var lights = new Lights((_, _) => Task.FromResult(Result.Success), _copied.Enqueue);
        Step invalid = Parse("""{ "type": "lights", "command": "preset", "value": 10 }""");

        Assert.Empty(lights.Validate(Red));
        Assert.NotEmpty(lights.Validate(invalid));
        await lights.RunAsync(invalid, Tap, CancellationToken.None);

        Assert.Empty(_copied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task With_openrgb_or_without_it_the_step_ends_as_the_console_says(int failConnects)
    {
        await using var pc = new PcLightsRig(PcLightsRig.Chosen, failConnects).Start();
        var lights = new Lights((_, _) => Task.FromResult(Result.Success), pc.Lights.Apply);

        StepResult result = await lights.RunAsync(Red, Tap, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(new PcLightState(true, new Rgb(255, 0, 0), 255), pc.Lights.Model);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_knob_copies_its_brightness_also_without_console(bool console)
    {
        await using var rig = new DispatcherRig(
            KnobConfig, clock: new FakeTimeProvider(), send: console ? (_, _) => Task.FromResult(Result.Success) : null, copyToPc: _copied.Enqueue);

        Turn(rig, +2);

        Assert.Equal(Lights.AdjustBrightness(32), Assert.Single(_copied));
    }

    [Fact]
    public async Task Ten_turns_of_the_knob_end_at_the_right_brightness_with_few_writes()
    {
        await using var pc = new PcLightsRig(PcLightsRig.Chosen).Start();
        await using var rig = new DispatcherRig(KnobConfig, clock: new FakeTimeProvider(), copyToPc: pc.Lights.Apply);
        await pc.UntilAsync(() => pc.Link == PcLightsLinkState.Connected, "conectado");
        pc.Lights.Apply(new LightCommand(1, 255, 255, 255, 0));
        await pc.UntilAsync(() => pc.OpenRgb.Writes.Count == 1, "el color");

        for (int turn = 0; turn < 10; turn++)
        {
            Turn(rig, -1);
        }

        await Eventually.AdvancingAsync(pc.Clock, PcLights.WriteEvery, () => pc.OpenRgb.Writes[^1].Leds?[5] == new Rgb(95, 95, 95), "brillo 95");
        await PcLightsRig.SettleAsync();

        Assert.Equal(95, pc.Lights.Model.Brightness);
        Assert.InRange(pc.OpenRgb.Writes.Count, 2, 3);
    }

    private static void Turn(DispatcherRig rig, int detents) =>
        rig.Dispatcher.Receive(new InputEvent("console", Controls.Knob, Gesture.Turn, detents));
}
