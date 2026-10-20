using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class LogWrittenTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly Log _log;

    public LogWrittenTests()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public void Every_new_entry_is_announced_with_its_data()
    {
        var seen = new List<LogEntry>();
        var runId = Guid.NewGuid();
        _log.Written += seen.Add;

        _log.Write(LogLevel.Warning, "app", "hola", "algo", Trigger.Tray, runId);

        LogEntry entry = Assert.Single(seen);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("app", entry.Module);
        Assert.Equal("hola", entry.Message);
        Assert.Equal("algo", entry.Code);
        Assert.Equal(Trigger.Tray, entry.RequestedBy);
        Assert.Equal(runId, entry.RunId);
    }

    [Fact]
    public void A_subscriber_that_throws_does_not_reach_the_writer()
    {
        int calls = 0;
        _log.Written += _ =>
        {
            calls++;
            throw new InvalidOperationException("roto");
        };

        _log.Info("app", "uno");

        Assert.Equal(1, calls);
        IReadOnlyList<LogEntry> recent = _log.Recent();
        Assert.Contains(recent, entry => entry.Message == "uno");
        Assert.Contains(recent, entry => entry.Module == "log" && entry.Level == LogLevel.Error && entry.Message.StartsWith("Un suscriptor del registro tiró", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_subscribers_writing_is_the_same_as_before()
    {
        _log.Info("app", "solo");

        Assert.Equal("solo", Assert.Single(_log.Recent()).Message);
    }
}
