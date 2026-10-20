using PampaPad.Core;

namespace PampaPad.Tests;

// Bordes de la mitad de SerialConsole que parte lo que llega en líneas y saca los eventos (SerialConsole.Lines.cs).
public class SerialConsoleLineTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";
    private const string AllUp = "#301110AAAAAAAAAAA=\n";

    [Theory]
    [InlineData(35, "\n")]
    [InlineData(36, "\n")]
    [InlineData(35, "\r\n")]
    [InlineData(36, "\r\n")]
    public async Task A_line_of_the_longest_length_is_read_whole_with_either_line_ending(int payloadLength, string ending)
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        var payload = new byte[payloadLength];
        payload[0] = 0x04;

        cable.Feed(Line(payload).TrimEnd('\n') + ending);

        Assert.Equal(55, Line(payload).Length - 1);         // la línea más larga que admite 04 §7, sin el fin de línea
        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(0, rig.Source.MalformedLines);
    }

    [Fact]
    public async Task A_line_one_character_too_long_is_malformed_even_if_its_start_is_a_valid_state()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        var payload = new byte[36];
        payload[0] = 0x01;                                  // la tecla 1: si la línea entrara, el primer evento sería Press(1)

        cable.Feed(Line(payload).TrimEnd('\n') + "A\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(1, rig.Source.MalformedLines);
    }

    [Fact]
    public async Task Bytes_arriving_one_at_a_time_give_the_same_events()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        foreach (var c in Key3Down + AllUp)
        {
            cable.Feed(c.ToString());
        }

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());
    }

    [Fact]
    public async Task A_carriage_return_at_the_end_of_a_read_does_not_break_the_line()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#301110BAAAAAAAAAA=\r");
        cable.Feed("\n");

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(0, rig.Source.MalformedLines);
    }

    [Fact]
    public async Task A_burst_bigger_than_the_read_buffer_loses_nothing_and_keeps_the_order()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(string.Concat(Enumerable.Repeat(Key3Down + AllUp, 100)));

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(Press(3), await rig.NextEvent());
            Assert.Equal(Release(3), await rig.NextEvent());
        }

        Assert.Equal(200, rig.EventCount);
    }

    [Fact]
    public async Task Several_keys_in_one_state_come_out_in_key_order_and_leave_the_same_way()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Line([0x05, 0, 0, 0, 0, 0, 0, 0]) + AllUp);      // teclas 1 y 3

        Assert.Equal(Press(1), await rig.NextEvent());
        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(1), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());
    }

    [Fact]
    public async Task A_broken_line_between_two_states_does_not_make_it_forget_the_pressed_key()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down + "#zz\n" + AllUp);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());
        Assert.Equal(1, rig.Source.MalformedLines);
    }

    [Fact]
    public async Task A_message_of_an_unknown_type_between_two_states_does_not_make_it_forget_the_pressed_key()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down + "#7F1110AAAAAAAAAAA=\n" + AllUp);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());
        Assert.Equal(1, rig.Source.UnknownMessages);
    }

    [Fact]
    public async Task A_half_line_from_the_old_cable_does_not_join_the_new_one()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        first.Feed("#30111");                               // media línea, y el cable se va
        await Poll.Until(() => rig.OpenCalls == 1);
        first.Break();
        await Poll.Until(() => first.Closed);
        await Poll.Until(() => rig.Store.Current.Console.State == ConsoleLinkState.Waiting && rig.Log.Recent().Any(e => e.Module == SerialConsole.SourceId && e.Level == LogLevel.Warning));
        rig.Clock.Advance(SerialConsole.RetryDelay);
        await Poll.Until(() => rig.OpenCalls == 2);

        // "0BAAAAAAAAAA=" pegado a "#30111" sería la tecla 3. Solo es texto de arranque. La tecla 5 marca que todo se leyó.
        second.Feed("0BAAAAAAAAAA=\n" + Line([0x10, 0, 0, 0, 0, 0, 0, 0]));

        Assert.Equal(Press(5), await rig.NextEvent());
        Assert.Equal(1, rig.EventCount);
        Assert.Equal(0, rig.Source.MalformedLines);
    }

    [Fact]
    public async Task A_listener_that_throws_is_logged_as_an_internal_error_and_the_next_event_still_arrives()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        rig.Source.Input += _ => throw new InvalidOperationException("roto a propósito");
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down + AllUp);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(Release(3), await rig.NextEvent());

        // NextEvent vuelve apenas el oyente del arnés recibe el evento, que puede ser antes de que el que tira anote su error (hallazgo E3b-6).
        await Poll.Until(() => rig.Log.Recent().Count(e => e.Level == LogLevel.Error) == 2);
        var errors = rig.Log.Recent().Where(e => e.Level == LogLevel.Error).ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(ErrorCodes.InternalError, e.Code));
        Assert.All(errors, e => Assert.Equal(SerialConsole.SourceId, e.Module));
        Assert.Contains("roto a propósito", errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("key.3", errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_unknown_type_and_the_first_broken_line_are_each_logged_once_when_they_arrive()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#311110AAAAAAAAAAA=\n#zz\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var logged = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId).ToList();
        Assert.Single(logged, e => e.Level == LogLevel.Info && e.Message.Contains("0x31", StringComparison.Ordinal));
        Assert.Single(logged, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task The_next_unknown_types_and_broken_lines_are_only_counted()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(string.Concat(Enumerable.Repeat("#311110AAAAAAAAAAA=\n#zz\n", 5)) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(5, rig.Source.UnknownMessages);
        Assert.Equal(5, rig.Source.MalformedLines);
        var logged = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId).ToList();
        Assert.Single(logged, e => e.Level == LogLevel.Info && e.Message.Contains("0x31", StringComparison.Ordinal));
        Assert.Single(logged, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Apply_returns_no_problems_for_any_page()
    {
        await using var rig = new SerialConsoleRig();

        Assert.Empty(rig.Source.Apply(new Page(), []));
        Assert.Equal(SerialConsole.SourceId, rig.Source.Id);
        Assert.Equal("console", SerialConsole.SourceId);
    }

    private static string Line(byte[] payload) => "#301110" + Convert.ToBase64String(payload) + "\n";

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, $"key.{key}", Gesture.Press);

    private static InputEvent Release(int key) => new(SerialConsole.SourceId, $"key.{key}", Gesture.Release);
}
