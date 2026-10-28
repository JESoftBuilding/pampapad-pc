using PampaPad.Core;

namespace PampaPad.Tests;

public class LogQueryTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 23, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Without_filters_it_brings_the_last_entries_oldest_first()
    {
        LogEntry[] recent = [.. Enumerable.Range(1, 5).Select(i => Entry(i, LogLevel.Info))];

        LogReadResponse response = Read(recent, new LogReadRequest(Count: 3));

        Assert.Equal(new[] { "3", "4", "5" }, response.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void Without_count_it_brings_200()
    {
        LogEntry[] recent = [.. Enumerable.Range(1, 250).Select(i => Entry(i, LogLevel.Info))];

        LogReadResponse response = Read(recent, new LogReadRequest());

        Assert.Equal(200, response.Entries.Count);
        Assert.Equal("51", response.Entries[0].Message);
    }

    [Fact]
    public void The_level_is_a_minimum()
    {
        LogEntry[] recent = [Entry(1, LogLevel.Info), Entry(2, LogLevel.Warning), Entry(3, LogLevel.Error)];

        Assert.Equal(new[] { "2", "3" }, Read(recent, new LogReadRequest(Level: LogLevel.Warning)).Entries.Select(entry => entry.Message));
        Assert.Equal(new[] { "3" }, Read(recent, new LogReadRequest(Level: LogLevel.Error)).Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void RequestedBy_and_run_id_are_exact()
    {
        var runId = Guid.NewGuid();
        LogEntry[] recent =
        [
            Entry(1, LogLevel.Info) with { RequestedBy = Trigger.Key, RunId = runId },
            Entry(2, LogLevel.Info) with { RequestedBy = Trigger.Hotkey },
            Entry(3, LogLevel.Info) with { RequestedBy = Trigger.Key },
        ];

        Assert.Equal(new[] { "1", "3" }, Read(recent, new LogReadRequest(RequestedBy: Trigger.Key)).Entries.Select(entry => entry.Message));
        Assert.Equal(new[] { "1" }, Read(recent, new LogReadRequest(RunId: runId)).Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void The_script_is_exact()
    {
        LogEntry[] recent = [Entry(1, LogLevel.Info) with { Script = "hola" }, Entry(2, LogLevel.Info), Entry(3, LogLevel.Info) with { Script = "hola-2" }];

        Assert.Equal(new[] { "1" }, Read(recent, new LogReadRequest(Script: "hola")).Entries.Select(entry => entry.Message));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void A_count_outside_1_to_500_is_invalid_data(int count)
    {
        Result<LogReadResponse> result = LogQuery.Read([], new LogReadRequest(Count: count));

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.InvalidData, result.Failure?.Code);
        Assert.Equal("count", result.Failure?.Where);
    }

    [Fact]
    public void Count_500_is_the_whole_memory()
    {
        LogEntry[] recent = [.. Enumerable.Range(1, 500).Select(i => Entry(i, LogLevel.Info))];

        Assert.Equal(500, Read(recent, new LogReadRequest(Count: 500)).Entries.Count);
    }

    private static LogReadResponse Read(IReadOnlyList<LogEntry> recent, LogReadRequest request)
    {
        Result<LogReadResponse> result = LogQuery.Read(recent, request);
        Assert.True(result.Ok);
        return result.Value!;
    }

    private static LogEntry Entry(int number, LogLevel level) =>
        new(At.AddSeconds(number), level, "prueba", number.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
