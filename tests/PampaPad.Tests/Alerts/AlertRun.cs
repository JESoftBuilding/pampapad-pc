using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>GraveAlerts a mano: un estado armado, una hora y la memoria que va quedando, como la llevaría AlertWatch.</summary>
internal sealed class AlertRun
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 4, 20, 0, 0, TimeSpan.Zero);

    public AlertMemory Memory { get; set; } = AlertMemory.Initial;

    public Config Config { get; set; } = new();

    public DateTimeOffset Now { get; set; } = T0;

    /// <summary>OBS listo, con el micrófono, en esa grabación.</summary>
    public static AppState Obs(RecordingState recording = RecordingState.Stopped, bool micMuted = false, TimeSpan recorded = default) =>
        AppState.Initial with
        {
            Obs = ObsState.Initial with
            {
                Link = ObsLinkState.Ready,
                Recording = recording,
                MicName = "Mic/Aux",
                MicMuted = micMuted,
                Recorded = recorded,
                RecordingChangedOn = recording == RecordingState.Stopped ? null : T0,
            },
        };

    public static AppState With(AppState state, Func<ObsState, ObsState> change) => state with { Obs = change(state.Obs) };

    public GraveAlert? Look(AppState state)
    {
        Memory = GraveAlerts.Step(Memory, state, Config, Now);
        return Memory.Shown;
    }

    /// <summary>El clic y la mirada siguiente, como AlertWatch.Attend.</summary>
    public GraveAlert? Click(AppState state)
    {
        Memory = GraveAlerts.Attend(Memory);
        return Look(state);
    }

    public GraveAlert? After(TimeSpan span, AppState state)
    {
        Now += span;
        return Look(state);
    }
}
