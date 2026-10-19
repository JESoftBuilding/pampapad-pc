using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que el motor le suma al estado, a la política de reinicio y al registro, en los bordes que las pruebas del plan no tocan.</summary>
public sealed class RunRecordEdgeTests : IDisposable
{
    private static readonly RunningAction Running = new(Guid.NewGuid(), "ir-al-banco", Trigger.Key);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void An_action_in_progress_is_named_before_any_other_reason()
    {
        AppState state = new AppState() with
        {
            Obs = ObsState.Initial with { Link = ObsLinkState.Ready, Recording = RecordingState.Paused, Streaming = true },
            Running = [Running],
        };

        Assert.Equal(new RestartVerdict(false, "Hay una acción en curso"), RestartPolicy.Evaluate(state));
    }

    [Fact]
    public void Without_actions_in_progress_the_restart_is_up_to_obs_only()
    {
        Assert.True(RestartPolicy.Evaluate(new AppState() with { Running = [] }).Allowed);
        Assert.False(RestartPolicy.Evaluate(new AppState() with { Obs = ObsState.Initial with { Recording = RecordingState.Recording }, Running = [] }).Allowed);
    }

    [Fact]
    public void While_an_action_runs_install_goes_dark_with_the_reason_and_the_tooltip_says_why()
    {
        AppState offered = new AppState() with { Update = new UpdateState(UpdatePhase.Available, "0.2.0") };

        UpdateMenu free = TraySummary.UpdateItems(offered);
        UpdateMenu busy = TraySummary.UpdateItems(offered with { Running = [Running] });

        Assert.Equal(new UpdateMenu(true, "Buscar actualización", true, true, "Instalar versión 0.2.0"), free);
        Assert.Equal(new UpdateMenu(true, "Buscar actualización", true, false, "Instalar versión 0.2.0 (Hay una acción en curso)"), busy);
        Assert.Equal("Versión 0.2.0 lista para instalar. Hay una acción en curso", TraySummary.UpdateText(offered with { Running = [Running] }));
    }

    [Fact]
    public void The_running_actions_alone_only_turn_on_cancel_all()
    {
        // Desde la etapa 5 la bandeja ofrece "Cancelar todo" si hay alguna en curso (05 §3); el ícono y el texto no cambian.
        AppState idle = new() { BasePage = "grabacion", MainPage = "grabacion" };

        Assert.Equal(TraySummary.From(idle) with { CanCancel = true }, TraySummary.From(idle with { Running = [Running], ActivePage = "grabacion" }));
    }

    [Fact]
    public async Task A_line_with_a_run_but_without_who_asked_and_one_with_who_asked_but_without_a_run_write_only_what_they_have()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        string folder = _temp.Combine("log");
        Guid run = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await using var log = new Log(folder, clock);

        log.Write(LogLevel.Warning, "dispatcher", "con corrida", "internal_error", null, run);
        log.Write(LogLevel.Info, "dispatcher", "con quién", null, Trigger.Mcp);
        log.Warning("app", "común", "config_invalid");
        await log.FlushAsync();

        string[] lines = File.ReadAllLines(Path.Combine(folder, "2026-10-13.jsonl"));
        using JsonDocument first = JsonDocument.Parse(lines[0]);
        using JsonDocument second = JsonDocument.Parse(lines[1]);
        using JsonDocument third = JsonDocument.Parse(lines[2]);
        Assert.False(first.RootElement.TryGetProperty("requestedBy", out _));
        Assert.Equal("11111111-2222-3333-4444-555555555555", first.RootElement.GetProperty("runId").GetString());
        Assert.Equal("mcp", second.RootElement.GetProperty("requestedBy").GetString());
        Assert.False(second.RootElement.TryGetProperty("runId", out _));
        Assert.False(third.RootElement.TryGetProperty("requestedBy", out _));
        Assert.False(third.RootElement.TryGetProperty("runId", out _));
        Assert.Equal("config_invalid", third.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(Trigger.Key, "key")]
    [InlineData(Trigger.Hotkey, "hotkey")]
    [InlineData(Trigger.Window, "window")]
    [InlineData(Trigger.Mcp, "mcp")]
    [InlineData(Trigger.Tray, "tray")]
    [InlineData(Trigger.Internal, "internal")]
    public async Task Who_asked_is_written_in_lower_case(Trigger trigger, string expected)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        string folder = _temp.Combine("log");
        await using var log = new Log(folder, clock);

        log.Write(LogLevel.Info, "dispatcher", "x", null, trigger, Guid.Empty);
        await log.FlushAsync();

        using JsonDocument line = JsonDocument.Parse(File.ReadAllLines(Path.Combine(folder, "2026-10-13.jsonl"))[0]);
        Assert.Equal(expected, line.RootElement.GetProperty("requestedBy").GetString());
    }

    [Fact]
    public void Two_states_with_the_same_page_and_the_same_running_list_are_equal()
    {
        IReadOnlyList<RunningAction> list = [Running];

        Assert.Equal(new AppState() with { BasePage = "a", ActivePage = "a", Running = list }, new AppState() with { BasePage = "a", ActivePage = "a", Running = list });
        Assert.NotEqual(new AppState() with { BasePage = "a" }, new AppState() with { BasePage = "b" });
        Assert.NotEqual(new AppState() with { ActivePage = "a" }, new AppState() with { ActivePage = "b" });
    }
}
