using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Las marcas de la toma (plan de la 9b, E9b-2 y decisión 11): suman las que entraron y vuelven a 0 al empezar a grabar.</summary>
public class ObsMarkerCountTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.6", Gesture.Tap, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    [Fact]
    public async Task Each_marker_that_went_in_counts_and_a_failed_one_does_not()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);
        Step step = Parse("""{ "type": "obs.marker", "marker": "short" }""");

        await marker.RunAsync(step, Tap, CancellationToken.None);
        await marker.RunAsync(step, Tap, CancellationToken.None);
        session.Obs.ChaptersUnsupported = true;
        StepResult failed = await marker.RunAsync(step, Tap, CancellationToken.None);

        Assert.NotNull(failed.Failure);
        Assert.Equal(2, session.State.Current.Obs.TakeMarks);
    }

    [Fact]
    public void Starting_to_record_puts_the_count_back_to_zero_and_pausing_does_not()
    {
        var take = ObsState.Initial with { Link = ObsLinkState.Ready, Recording = RecordingState.Recording, TakeMarks = 7 };

        ObsState paused = ObsMirror.Apply(take, Record("OBS_WEBSOCKET_OUTPUT_PAUSED"));
        ObsState stopped = ObsMirror.Apply(paused, Record("OBS_WEBSOCKET_OUTPUT_STOPPED"));
        ObsState again = ObsMirror.Apply(stopped, Record("OBS_WEBSOCKET_OUTPUT_STARTED"));

        Assert.Equal(7, paused.TakeMarks);
        Assert.Equal(7, stopped.TakeMarks);
        Assert.Equal(0, again.TakeMarks);
    }

    private static ObsEvent Record(string outputState)
    {
        using var document = JsonDocument.Parse($$"""{"outputActive":false,"outputPath":null,"outputState":"{{outputState}}"}""");
        return new ObsEvent("RecordStateChanged", 64, document.RootElement.Clone());
    }
}
