using PampaPad.Core;

namespace PampaPad.Tests;

public class RecordRulesTests
{
    [Theory]
    [InlineData(RecordCommand.Start, RecordingState.Stopped, RecordMove.Send)]
    [InlineData(RecordCommand.Start, RecordingState.Recording, RecordMove.AlreadyThere)]
    [InlineData(RecordCommand.Start, RecordingState.Paused, RecordMove.AlreadyThere)]
    [InlineData(RecordCommand.Stop, RecordingState.Stopped, RecordMove.AlreadyThere)]
    [InlineData(RecordCommand.Stop, RecordingState.Recording, RecordMove.Send)]
    [InlineData(RecordCommand.Stop, RecordingState.Paused, RecordMove.Send)]
    [InlineData(RecordCommand.Pause, RecordingState.Stopped, RecordMove.NotRecording)]
    [InlineData(RecordCommand.Pause, RecordingState.Recording, RecordMove.Send)]
    [InlineData(RecordCommand.Pause, RecordingState.Paused, RecordMove.AlreadyThere)]
    [InlineData(RecordCommand.Resume, RecordingState.Stopped, RecordMove.NotRecording)]
    [InlineData(RecordCommand.Resume, RecordingState.Recording, RecordMove.AlreadyThere)]
    [InlineData(RecordCommand.Resume, RecordingState.Paused, RecordMove.Send)]
    [InlineData(RecordCommand.Start, RecordingState.Starting, RecordMove.Wait)]
    [InlineData(RecordCommand.Stop, RecordingState.Starting, RecordMove.Wait)]
    [InlineData(RecordCommand.Pause, RecordingState.Stopping, RecordMove.Wait)]
    [InlineData(RecordCommand.Resume, RecordingState.Stopping, RecordMove.Wait)]
    public void The_table_of_03_section_3(RecordCommand command, RecordingState known, RecordMove move)
    {
        Assert.Equal(move, RecordRules.Decide(command, known));
    }

    [Theory]
    [InlineData(RecordCommand.Start, "StartRecord", 500)]
    [InlineData(RecordCommand.Stop, "StopRecord", 501)]
    [InlineData(RecordCommand.Pause, "PauseRecord", 502)]
    [InlineData(RecordCommand.Resume, "ResumeRecord", 503)]
    public void Each_order_has_its_explicit_request_and_its_race_code(RecordCommand command, string request, int race)
    {
        Assert.Equal(request, RecordRules.RequestType(command));
        Assert.Equal(race, RecordRules.RaceCode(command));
    }

    [Theory]
    [InlineData(RecordCommand.Resume, RecordingState.Stopped, false)]
    [InlineData(RecordCommand.Resume, RecordingState.Recording, true)]
    [InlineData(RecordCommand.Start, RecordingState.Recording, true)]
    [InlineData(RecordCommand.Stop, RecordingState.Stopped, true)]
    [InlineData(RecordCommand.Pause, RecordingState.Paused, true)]
    public void After_a_race_only_resume_with_the_take_stopped_is_a_failure(RecordCommand command, RecordingState now, bool well)
    {
        Assert.Equal(well, RecordRules.RaceEndsWell(command, now));
    }

    [Theory]
    [InlineData("start", true)]
    [InlineData("stop", true)]
    [InlineData("pause", true)]
    [InlineData("resume", true)]
    [InlineData("Start", false)]
    [InlineData("grabar", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_four_words_are_orders(string? text, bool ok)
    {
        Assert.Equal(ok, RecordRules.TryParse(text, out _));
    }
}
