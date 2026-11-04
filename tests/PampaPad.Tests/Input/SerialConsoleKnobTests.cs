using PampaPad.Core;

namespace PampaPad.Tests;

// La perilla, PUSH y la tecla del módulo que saca SerialConsole de cada InputState (plan de la 9a, decisiones 8 a 10).
public class SerialConsoleKnobTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";

    [Fact]
    public async Task The_first_reading_of_the_knob_is_the_base_and_gives_no_steps()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(State(0, 77) + State(0x004, 77));

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(1, rig.EventCount);
    }

    [Theory]
    [InlineData(250, 3, 9)]
    [InlineData(3, 250, -9)]
    [InlineData(10, 11, 1)]
    [InlineData(0, 255, -1)]
    [InlineData(255, 0, 1)]
    public async Task A_turn_is_the_difference_with_the_last_reading_as_a_signed_byte(int from, int to, int steps)
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(State(0, from) + State(0, to));

        Assert.Equal(Turn(steps), await rig.NextEvent());
    }

    [Fact]
    public async Task The_three_copies_of_the_same_state_turn_only_once()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(State(0, 5) + State(0, 7) + State(0, 7) + State(0, 7) + State(0, 6) + Key3Down);

        Assert.Equal(Turn(2), await rig.NextEvent());
        Assert.Equal(Turn(-1), await rig.NextEvent());
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task A_new_connection_takes_the_base_again()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        first.Feed(State(0, 10) + State(0, 11));
        Assert.Equal(Turn(1), await rig.NextEvent());

        first.Break();
        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);
        rig.Clock.Advance(SerialConsole.RetryDelay);
        await Poll.Until(() => rig.OpenCalls == 2);
        second.Feed(State(0, 100) + State(0x004, 100));

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(2, rig.EventCount);
    }

    [Fact]
    public async Task A_hello_that_says_just_booted_takes_the_base_again_and_is_announced()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        var reboots = 0;
        rig.Source.ConsoleRebooted += () => Interlocked.Increment(ref reboots);
        await rig.Source.StartAsync(CancellationToken.None);
        cable.Feed(State(0, 200) + State(0, 201));
        Assert.Equal(Turn(1), await rig.NextEvent());

        // La placa se reinició: la perilla vuelve a 0, y desde 201 eso serían 55 pasos de mentira.
        cable.Feed("#311110AQABAMOAAAA=\n" + State(0, 0) + State(0, 2));

        Assert.Equal(Turn(2), await rig.NextEvent());
        Assert.Equal(1, Volatile.Read(ref reboots));
    }

    [Fact]
    public async Task Push_and_the_module_key_are_pressed_by_level_and_released_once()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(State(0x1000, 0) + State(0x3000, 0) + State(0x2000, 0) + State(0, 0) + Key3Down);

        Assert.Equal(Press(Controls.Knob), await rig.NextEvent());
        Assert.Equal(Press(Controls.Knob), await rig.NextEvent());
        Assert.Equal(Press(Controls.ModuleKey), await rig.NextEvent());
        Assert.Equal(Release(Controls.Knob), await rig.NextEvent());
        Assert.Equal(Press(Controls.ModuleKey), await rig.NextEvent());
        Assert.Equal(Release(Controls.ModuleKey), await rig.NextEvent());
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task The_firmware_vector_gives_key_1_and_the_module_key_and_takes_the_knob_as_base()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#301110ASAAAAAAAAM=\n" + State(0, 4));

        Assert.Equal(Press(1), await rig.NextEvent());
        Assert.Equal(Press(Controls.ModuleKey), await rig.NextEvent());
        Assert.Equal(new InputEvent(SerialConsole.SourceId, Controls.Key(1), Gesture.Release), await rig.NextEvent());
        Assert.Equal(Release(Controls.ModuleKey), await rig.NextEvent());
        Assert.Equal(Turn(1), await rig.NextEvent());
    }

    [Fact]
    public async Task Losing_the_link_forgets_push_and_the_module_key()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        first.Feed(State(0x2000, 0));
        Assert.Equal(Press(Controls.ModuleKey), await rig.NextEvent());

        first.Break();
        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);
        rig.Clock.Advance(SerialConsole.RetryDelay);
        await Poll.Until(() => rig.OpenCalls == 2);
        second.Feed(State(0, 0) + Key3Down);

        // Sin Release: la de antes no se soltó en este enlace, y Gestures ya la dio por soltada al perder la frescura.
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    private static string State(int buttons, int knob) =>
        "#301110" + Convert.ToBase64String(new byte[] { (byte)buttons, (byte)(buttons >> 8), 0, 0, 0, 0, 0, (byte)knob }) + "\n";

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, Controls.Key(key), Gesture.Press);

    private static InputEvent Press(string control) => new(SerialConsole.SourceId, control, Gesture.Press);

    private static InputEvent Release(string control) => new(SerialConsole.SourceId, control, Gesture.Release);

    private static InputEvent Turn(int steps) => new(SerialConsole.SourceId, Controls.Knob, Gesture.Turn, steps);
}
