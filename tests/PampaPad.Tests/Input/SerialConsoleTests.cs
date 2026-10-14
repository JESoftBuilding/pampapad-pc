using PampaPad.Core;

namespace PampaPad.Tests;

public class SerialConsoleTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";
    private const string AllUp = "#301110AAAAAAAAAAA=\n";

    [Fact]
    public async Task A_key_line_comes_out_as_a_press()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(ConsoleLinkState.Connected, rig.Link);
    }

    [Fact]
    public async Task A_line_split_across_two_reads_is_put_together()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#301110BAAA");
        cable.Feed("AAAAAAA=\r\n");

        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Two_lines_in_one_read_come_out_in_order()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#301110BAAAAAAAAAA=\r\n#301110AAAAAAAAAAA=\r\n");

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());
    }

    [Fact]
    public async Task The_same_state_again_is_the_refresh()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Boot_text_is_ignored_and_not_counted()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("ESP-ROM:esp32s3-20210327\r\nrst:0x1 (POWERON),boot:0x8 (SPI_FAST_FLASH_BOOT)\r\n\r\n");
        cable.Feed(new string('x', 300) + "\r\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(0, rig.Source.MalformedLines);
        Assert.Equal(0, rig.Source.UnknownMessages);
    }

    [Fact]
    public async Task An_unknown_type_and_broken_lines_are_counted_and_do_not_stop_the_rest()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#311110AAAAAAAAAAA=\n#30111\n#301110BAAAAAAAAA==\n#" + new string('A', 80) + "\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(1, rig.Source.UnknownMessages);
        Assert.Equal(3, rig.Source.MalformedLines);
    }

    [Fact]
    public async Task A_listener_that_throws_does_not_cut_the_link()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        var first = true;
        rig.Source.Input += _ =>
        {
            if (first)
            {
                first = false;
                throw new InvalidOperationException("roto a propósito");
            }
        };
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down + AllUp);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Unplugged_with_a_key_down_forgets_the_key_and_reconnects()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        first.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());

        first.Break();
        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);

        Assert.True(first.Closed);
        Assert.Equal(1, rig.EventCount);                    // no inventa un "soltó": lo pendiente lo cancela Gestures

        rig.Clock.Advance(SerialConsole.RetryDelay);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        second.Feed(AllUp + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());      // el "todo suelto" no suelta una tecla que ya se olvidó
        Assert.Equal(2, rig.OpenCalls);
    }

    [Fact]
    public async Task A_port_held_by_another_program_is_retried_until_it_opens()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextFails("Access to the path 'COM7' is denied.");
        rig.NextFails("Access to the path 'COM7' is denied.");
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        await rig.AdvanceUntil(() => rig.Link == ConsoleLinkState.Connected);

        Assert.Equal(3, rig.OpenCalls);
        cable.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Release_closes_the_port_and_taking_it_back_reopens_it()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        rig.Source.Release(true);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);

        Assert.True(first.Closed);
        rig.Clock.Advance(TimeSpan.FromMinutes(1));         // soltada no reintenta: lo dice el 2 de más abajo

        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        Assert.Equal(2, rig.OpenCalls);
        second.Feed(Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
    }

    [Fact]
    public async Task Release_while_waiting_to_retry_does_not_wait_for_the_timer()
    {
        await using var rig = new SerialConsoleRig();
        rig.NextFails("Access to the path 'COM7' is denied.");
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.OpenCalls == 1);

        rig.Source.Release(true);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);
        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        Assert.Equal(2, rig.OpenCalls);
        Assert.False(cable.Closed);
    }

    [Fact]
    public async Task Sending_is_not_part_of_this_stage_and_says_console_disconnected()
    {
        await using var rig = new SerialConsoleRig();

        var result = await rig.Source.SendAsync("#101013BQAAAAI=", CancellationToken.None);

        Assert.Equal(ErrorCodes.ConsoleDisconnected, result.Failure?.Code);
    }

    [Fact]
    public async Task Stopping_the_program_closes_the_port()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        using var life = new CancellationTokenSource();
        await rig.Source.StartAsync(life.Token);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        await life.CancelAsync();

        await Poll.Until(() => cable.Closed);
    }

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, $"key.{key}", Gesture.Press);

    private static InputEvent Release(int key) => new(SerialConsole.SourceId, $"key.{key}", Gesture.Release);
}
