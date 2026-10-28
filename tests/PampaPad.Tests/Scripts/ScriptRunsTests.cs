using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Las últimas corridas de cada script, leídas de los archivos del registro y sumadas en vivo (plan de la etapa 8, E8-2).</summary>
public class ScriptRunsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task It_reads_the_closing_lines_of_earlier_days_and_an_old_line_does_not_break_it()
    {
        using var temp = new TempFolder();
        string folder = temp.Combine("log");
        Directory.CreateDirectory(folder);
        DateOnly today = DateOnly.FromDateTime(Now.DateTime);
        File.WriteAllLines(Path.Combine(folder, Log.FileName(today.AddDays(-2))),
        [
            """{"at":"2026-10-25T10:00:00+00:00","level":"info","module":"obs","message":"una línea vieja, sin script"}""",
            "esto no es JSON",
            Line(Closing("hola", Now.AddDays(-2), ok: true, "lista")),
        ]);
        File.WriteAllLines(Path.Combine(folder, Log.FileName(today.AddDays(-20))), [Line(Closing("hola", Now.AddDays(-20), ok: true, "vieja"))]);
        var clock = new FakeTimeProvider(Now);
        await using var log = new Log(folder, clock);

        using var runs = new ScriptRuns(log, folder, clock);

        LogEntry run = Assert.Single(runs.Last("hola", 5));
        Assert.Equal("lista", run.End!.Message);
        Assert.Empty(runs.Last("otro", 5));
    }

    [Fact]
    public async Task New_closing_lines_add_up_and_last_orders_newest_first_and_cuts()
    {
        using var temp = new TempFolder();
        var clock = new FakeTimeProvider(Now);
        await using var log = new Log(temp.Combine("log"), clock);
        using var runs = new ScriptRuns(log, temp.Combine("log"), clock);

        foreach (string message in new[] { "uno", "dos", "tres" })
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            log.Write(LogLevel.Info, "scripts", message, null, null, null, "hola", new ScriptEnd(true, 0, 10, message));
        }

        log.Write(LogLevel.Info, "scripts", "arrancó, sin cierre", null, null, null, "hola");

        Assert.Equal(new[] { "tres", "dos" }, runs.Last("hola", 2).Select(entry => entry.End!.Message));
    }

    private static LogEntry Closing(string script, DateTimeOffset at, bool ok, string message) =>
        new(at, LogLevel.Info, "scripts", message, null, Trigger.Window, Guid.NewGuid(), script, new ScriptEnd(ok, 0, 12, message));

    private static string Line(LogEntry entry) => JsonSerializer.Serialize(entry, Json.Compact);
}
