using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class RunRecordTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_fresh_state_has_no_page_and_nothing_running()
    {
        var state = new AppState();

        Assert.Equal("", state.BasePage);
        Assert.Equal("", state.ActivePage);
        Assert.Empty(state.Running);
    }

    [Fact]
    public void A_running_action_forbids_a_restart()
    {
        AppState state = new AppState() with { Running = [new RunningAction(Guid.NewGuid(), "ir-al-banco", Trigger.Hotkey)] };

        RestartVerdict verdict = RestartPolicy.Evaluate(state);

        Assert.False(verdict.Allowed);
        Assert.Equal("Hay una acción en curso", verdict.Reason);
    }

    [Fact]
    public async Task A_line_of_an_action_says_who_asked_and_which_run()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        string folder = _temp.Combine("log");
        Guid run = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await using var log = new Log(folder, clock);

        log.Write(LogLevel.Info, "dispatcher", "marca-short terminó bien", null, Trigger.Hotkey, run);
        log.Info("app", "una línea común");
        await log.FlushAsync();

        string[] lines = File.ReadAllLines(Path.Combine(folder, "2026-10-13.jsonl"));
        Assert.EndsWith("\"requestedBy\":\"hotkey\",\"runId\":\"11111111-2222-3333-4444-555555555555\"}", lines[0]);
        Assert.DoesNotContain("runId", lines[1]);
        LogEntry entry = log.Recent()[0];
        Assert.Equal(run, entry.RunId);
        Assert.Equal(Trigger.Hotkey, entry.RequestedBy);
    }
}
