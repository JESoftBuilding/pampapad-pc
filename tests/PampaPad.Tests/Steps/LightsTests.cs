using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public class LightsTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.3", Gesture.Tap, 0);

    private readonly List<string> _sent = [];

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private Lights Recording() => new((line, _) =>
    {
        _sent.Add(line);
        return Task.FromResult(Result.Success);
    });

    [Theory]
    [InlineData("""{ "type": "lights", "command": "power", "value": "off" }""", 5, 0, 0, 0, 0)]
    [InlineData("""{ "type": "lights", "command": "power", "value": "on" }""", 5, 0, 0, 0, 1)]
    [InlineData("""{ "type": "lights", "command": "power", "value": "toggle" }""", 5, 0, 0, 0, 2)]
    [InlineData("""{ "type": "lights", "command": "brightness", "value": 0 }""", 2, 0, 0, 0, 0)]
    [InlineData("""{ "type": "lights", "command": "brightness", "value": 255 }""", 2, 0, 0, 0, 255)]
    [InlineData("""{ "type": "lights", "command": "adjustBrightness", "value": -127 }""", 3, 0, 0, 0, 0x81)]
    [InlineData("""{ "type": "lights", "command": "adjustBrightness", "value": 127 }""", 3, 0, 0, 0, 127)]
    [InlineData("""{ "type": "lights", "command": "preset", "value": 0 }""", 4, 0, 0, 0, 0)]
    [InlineData("""{ "type": "lights", "command": "preset", "value": 5 }""", 4, 0, 0, 0, 5)]
    [InlineData("""{ "type": "lights", "command": "color", "color": "#6d5df6" }""", 1, 0x6D, 0x5D, 0xF6, 0)]
    [InlineData("""{ "type": "lights", "command": "color", "color": "#FFA500" }""", 1, 0xFF, 0xA5, 0x00, 0)]
    public async Task Each_command_builds_its_five_bytes_and_one_line_reaches_the_console(string json, byte cmd, byte r, byte g, byte b, byte value)
    {
        var lights = Recording();
        Step step = Parse(json);

        StepResult result = await lights.RunAsync(step, Tap, CancellationToken.None);

        Assert.Empty(lights.Validate(step));
        Assert.Null(result.Failure);
        Assert.Equal(ConsoleProtocol.LightLine(new LightCommand(cmd, r, g, b, value)), Assert.Single(_sent));
    }

    [Fact]
    public async Task The_brand_violet_in_lowercase_is_cmd_1_with_its_three_bytes()
    {
        await Recording().RunAsync(Parse("""{ "type": "lights", "command": "color", "color": "#6d5df6" }"""), Tap, CancellationToken.None);

        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(Assert.Single(_sent), out var message));
        Assert.Equal(new byte[] { 1, 0x6D, 0x5D, 0xF6, 0 }, message.Payload);
    }

    [Theory]
    [InlineData("""{ "type": "lights", "command": "brightness", "value": 256 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "brightness", "value": "on" }""", "value")]
    [InlineData("""{ "type": "lights", "command": "brightness", "value": "50" }""", "value")]
    [InlineData("""{ "type": "lights", "command": "brightness", "value": 1.5 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "brightness" }""", "value")]
    [InlineData("""{ "type": "lights", "command": "adjustBrightness", "value": 0 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "adjustBrightness", "value": 128 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "adjustBrightness", "value": -128 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "preset", "value": 6 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "preset", "value": -1 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "power", "value": "medio" }""", "value")]
    [InlineData("""{ "type": "lights", "command": "power", "value": 1 }""", "value")]
    [InlineData("""{ "type": "lights", "command": "color", "color": "6d5df6" }""", "color")]
    [InlineData("""{ "type": "lights", "command": "color", "color": "#abc" }""", "color")]
    [InlineData("""{ "type": "lights", "command": "color", "color": "#6d5dfz" }""", "color")]
    [InlineData("""{ "type": "lights", "command": "color" }""", "color")]
    [InlineData("""{ "type": "lights", "command": "strobe", "value": 1 }""", "command")]
    [InlineData("""{ "type": "lights", "value": 1 }""", "command")]
    public void A_value_that_does_not_fit_its_command_is_parameter_invalid_where_it_fails(string json, string where)
    {
        Problem problem = Assert.Single(Recording().Validate(Parse(json)));

        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
        Assert.Equal(where, problem.Where);
    }

    [Fact]
    public async Task Without_a_console_in_this_program_it_is_console_disconnected()
    {
        StepResult result = await new Lights(null).RunAsync(Parse("""{ "type": "lights", "command": "power", "value": "toggle" }"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
        Assert.Equal("No hay consola en este programa.", result.Failure?.Message);
    }

    [Fact]
    public async Task When_the_console_cannot_write_the_action_fails_at_the_lights_step_with_its_reason()
    {
        var lights = new Lights((_, _) => Task.FromResult(Result.Fail(ErrorCodes.ConsoleDisconnected, "La consola está soltada.")));
        var after = new FakeStep("a");
        var executor = new Executor([lights, after], TimeProvider.System);
        PadAction action = JsonSerializer.Deserialize<PadAction>(
            """{ "id": "luz", "name": "Luz", "steps": [ { "type": "lights", "command": "power", "value": "toggle" }, { "type": "a" } ] }""", Json.Options)!;

        ActionResult result = await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
        Assert.Equal("La consola está soltada.", result.Failure?.Message);
        Assert.Equal((1, "lights"), (result.StepNumber, result.StepType));
        Assert.Empty(after.Runs);
    }

    [Fact]
    public async Task A_lights_step_runs_while_recording()
    {
        // Las luces son para la toma (Decidido 2) y no pasan por OBS: grabando, la tecla anda igual (plan de la 8b, decisión 11).
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Combine("biblioteca"));
        File.WriteAllText(temp.Combine("biblioteca", "config.json"), """
            {
              "version": 1,
              "mainPage": "grabacion",
              "pages": [ { "id": "grabacion", "name": "Grabación", "keys": [ { "position": 2, "label": "Luz", "action": "luz" } ] } ],
              "actions": [ { "id": "luz", "name": "Luz", "steps": [ { "type": "lights", "command": "power", "value": "toggle" } ] } ]
            }
            """);
        await using var state = new StateStore();
        await using var log = new Log(temp.Combine("log"), TimeProvider.System);
        var library = new CoreLibrary(temp.Combine("biblioteca"), temp.Combine("backup"));
        library.Load();
        var finished = new ConcurrentQueue<ActionResult>();
        var dispatcher = new Dispatcher(library, state, new Executor([Recording()], TimeProvider.System), new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), log);
        dispatcher.Finished += finished.Enqueue;
        state.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        dispatcher.Receive(new InputEvent("console", Controls.Key(2), Gesture.Press));
        await Poll.Until(() => !finished.IsEmpty);

        ActionResult result = Assert.Single(finished);
        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("#101013BQAAAAI=", Assert.Single(_sent));
    }
}
