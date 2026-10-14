using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class LogTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Writes_one_json_line_per_entry_in_the_file_of_the_day()
    {
        var folder = _temp.Combine("log");
        await using var log = new Log(folder, _clock);

        log.Info("app", "Arrancó");
        log.Error("update", "No hay red", ErrorCodes.NoResponse);
        await log.FlushAsync();

        var lines = File.ReadAllLines(Path.Combine(folder, "2026-10-13.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Equal(
            "{\"at\":\"2026-10-13T12:00:00+00:00\",\"level\":\"info\",\"module\":\"app\",\"message\":\"Arrancó\"}",
            lines[0]);
        Assert.Equal(
            "{\"at\":\"2026-10-13T12:00:00+00:00\",\"level\":\"error\",\"module\":\"update\",\"message\":\"No hay red\",\"code\":\"no_response\"}",
            lines[1]);
    }

    [Fact]
    public async Task Keeps_only_the_last_500_entries_in_memory()
    {
        await using var log = new Log(_temp.Combine("log"), _clock);

        for (var i = 1; i <= Log.MemoryLimit + 1; i++)
        {
            log.Info("app", $"entrada {i}");
        }

        var recent = log.Recent();
        Assert.Equal(500, recent.Count);
        Assert.Equal("entrada 2", recent[0].Message);
        Assert.Equal("entrada 501", recent[^1].Message);
    }

    [Fact]
    public async Task Starts_a_file_per_day_and_deletes_the_ones_older_than_14_days()
    {
        var folder = _temp.Combine("log");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "2026-09-29.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(folder, "2026-09-30.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(folder, "notas.txt"), "no es del registro");
        await using var log = new Log(folder, _clock);

        log.Info("app", "hoy");
        await log.FlushAsync();

        Assert.False(File.Exists(Path.Combine(folder, "2026-09-29.jsonl")));
        Assert.True(File.Exists(Path.Combine(folder, "2026-09-30.jsonl")));
        Assert.True(File.Exists(Path.Combine(folder, "notas.txt")));

        _clock.Advance(TimeSpan.FromDays(1));
        log.Info("app", "mañana");
        await log.FlushAsync();

        Assert.False(File.Exists(Path.Combine(folder, "2026-09-30.jsonl")));
        Assert.Single(File.ReadAllLines(Path.Combine(folder, "2026-10-13.jsonl")));
        Assert.Contains("mañana", File.ReadAllText(Path.Combine(folder, "2026-10-14.jsonl")));
    }

    [Fact]
    public async Task An_old_file_that_cannot_be_deleted_does_not_stop_todays_entries()
    {
        var folder = _temp.Combine("log");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "2026-09-29.jsonl");
        File.WriteAllText(old, "{}\n");
        await using var log = new Log(folder, _clock);

        using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            log.Info("app", "hoy igual se escribe");
            await log.FlushAsync();
        }

        Assert.Contains("hoy igual se escribe", File.ReadAllText(Path.Combine(folder, "2026-10-13.jsonl")));
        Assert.Single(log.Recent(), entry => entry.Module == "log" && entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_valid_path_stays_in_memory_and_flush_still_returns()
    {
        await using var log = new Log("registro\0roto", _clock);

        log.Info("app", "no entra al disco");
        await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, log.Recent().Count);
        Assert.Single(log.Recent(), entry => entry.Module == "log" && entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_disk_failure_stays_in_memory_and_never_throws()
    {
        var blocked = _temp.Combine("ocupado");
        File.WriteAllText(blocked, "un archivo donde tendría que ir la carpeta");
        await using var log = new Log(blocked, _clock);

        log.Info("app", "no entra al disco");
        log.Info("app", "esta tampoco");
        await log.FlushAsync();

        var recent = log.Recent();
        Assert.Equal(3, recent.Count);
        Assert.Single(recent, entry => entry.Module == "log" && entry.Level == LogLevel.Error);
    }
}
