using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

// Lo que ObsMarkerTests, la del plan, no fija: los cinco tipos por el cable, la marca repetida, el estado atrasado,
// los pasos que no corren y la cancelación. Todo contra el OBS falso, por el mismo camino que el programa.
public class ObsMarkerEdgeTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.5", Gesture.Tap, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static Task<StepResult> Run(ObsMarker marker, string json, CancellationToken ct = default) => marker.RunAsync(Parse(json), Tap, ct);

    [Theory]
    [InlineData("short", null, "short")]
    [InlineData("inicio", "demo", "inicio demo")]
    [InlineData("fin", null, "fin")]
    [InlineData("sacar", "se cayó", "sacar se cayó")]
    [InlineData("largo", "capítulo 2", "largo capítulo 2")]
    [InlineData("short", "   ", "short")]
    public async Task Each_of_the_five_types_reaches_obs_with_its_word(string type, string? label, string title)
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);
        string json = label is null ? $$"""{ "type": "obs.marker", "marker": "{{type}}" }""" : $$"""{ "type": "obs.marker", "marker": "{{type}}", "label": "{{label}}" }""";

        StepResult result = await Run(marker, json);

        Assert.Null(result.Failure);
        Assert.Equal(new[] { title }, session.Obs.Chapters);
    }

    [Fact]
    public async Task The_same_marker_twice_in_a_row_is_two_chapters()
    {
        // README del plan: "dos pulsaciones son dos marcas". El paso no se acuerda de lo que mandó.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);

        await Run(marker, """{ "type": "obs.marker", "marker": "short", "label": "relevamiento" }""");
        await Run(marker, """{ "type": "obs.marker", "marker": "short", "label": "relevamiento" }""");

        Assert.Equal(new[] { "short relevamiento", "short relevamiento" }, session.Obs.Chapters);
    }

    [Fact]
    public async Task After_resuming_the_next_marker_goes_in()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Paused);
        var marker = new ObsMarker(session.Client, session.State);
        StepResult paused = await Run(marker, """{ "type": "obs.marker", "marker": "short" }""");
        await session.Obs.ResumeRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);

        StepResult resumed = await Run(marker, """{ "type": "obs.marker", "marker": "fin" }""");

        Assert.Equal(ErrorCodes.MarkerPaused, paused.Failure?.Code);
        Assert.Null(resumed.Failure);
        Assert.Equal(new[] { "fin" }, session.Obs.Chapters);
    }

    [Fact]
    public async Task A_known_state_that_is_behind_does_not_block_the_marker()
    {
        // 03 §4: la marca se manda siempre que haya conexión y el que decide es OBS. El estado conocido sólo elige el texto.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        session.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Stopped } });
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await Run(marker, """{ "type": "obs.marker", "marker": "short" }""");

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "short" }, session.Obs.Chapters);
    }

    [Theory]
    [InlineData(ObsLinkState.Closed)]
    [InlineData(ObsLinkState.NoWebsocket)]
    [InlineData(ObsLinkState.AuthFailed)]
    [InlineData(ObsLinkState.Loading)]
    [InlineData(ObsLinkState.Switching)]
    [InlineData(ObsLinkState.Lost)]
    [InlineData(ObsLinkState.Kicked)]
    [InlineData(ObsLinkState.Exiting)]
    public async Task Only_a_ready_link_sends_the_marker(ObsLinkState link)
    {
        // El cliente de verdad está conectado a un OBS que graba, pero el paso mira su propio estado: él es quien decide no mandar.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        await using var other = new StateStore();
        other.Update(s => s with { Obs = s.Obs.WithLink(link) with { SupportsMarkers = true } });
        var marker = new ObsMarker(session.Client, other);

        StepResult result = await Run(marker, """{ "type": "obs.marker", "marker": "short" }""");

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal("Sin OBS: la marca no entró.", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times(Markers.RequestType));
    }

    [Fact]
    public async Task A_step_with_a_marker_that_is_not_one_of_the_five_does_not_send_anything()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await Run(marker, """{ "type": "obs.marker", "marker": "Short", "label": "x" }""");

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Contains("\"Short\"", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times(Markers.RequestType));
    }

    [Theory]
    [InlineData("""{ "type": "obs.marker", "marker": 5 }""")]
    [InlineData("""{ "type": "obs.marker", "marker": "short", "label": 7 }""")]
    [InlineData("""{ "type": "obs.marker", "marker": ["short"] }""")]
    public async Task A_step_with_a_parameter_of_the_wrong_type_does_not_send_anything(string json)
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await Run(marker, json);

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Contains("no es del tipo esperado", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times(Markers.RequestType));
    }

    [Fact]
    public async Task A_step_without_the_marker_does_not_send_anything()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await Run(marker, """{ "type": "obs.marker", "label": "x" }""");

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times(Markers.RequestType));
    }

    [Fact]
    public async Task What_the_step_does_not_know_does_not_stop_it()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);

        StepResult result = await Run(marker, """{ "type": "obs.marker", "marker": "short", "inventado": { "a": 1 } }""");

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "short" }, session.Obs.Chapters);
    }

    [Fact]
    public async Task A_marker_that_obs_never_answers_is_cancelled_when_the_action_is_cancelled()
    {
        // El Executor corta una acción cancelando el token del paso: el paso tiene que volver cancelled, no Sin OBS.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        session.Obs.DoNotAnswer(Markers.RequestType);
        var marker = new ObsMarker(session.Client, session.State);
        using var cancel = new CancellationTokenSource();

        Task<StepResult> running = Run(marker, """{ "type": "obs.marker", "marker": "short" }""", cancel.Token);
        await Poll.Until(() => session.Obs.Times(Markers.RequestType) == 1);
        await cancel.CancelAsync();
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
    }

    [Fact]
    public async Task A_step_that_starts_already_cancelled_sends_nothing()
    {
        // El cliente de OBS manda el pedido y recién después mira el token: sin esta guarda el capítulo entraba y el paso decía cancelled.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        var marker = new ObsMarker(session.Client, session.State);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        StepResult result = await Run(marker, """{ "type": "obs.marker", "marker": "short" }""", cancel.Token);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal("Se canceló antes de mandar la marca: no entró.", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times(Markers.RequestType));
        Assert.Empty(session.Obs.Chapters);
    }

    [Theory]
    [InlineData("""{ "type": "obs.marker", "marker": "short" }""")]
    [InlineData("""{ "type": "obs.marker", "marker": "fin", "label": null }""")]
    [InlineData("""{ "type": "obs.marker", "marker": "inicio", "label": "" }""")]
    public async Task A_step_without_a_label_is_a_good_step(string json)
    {
        using var temp = new TempFolder();
        await using var log = new Log(temp.Combine("log"), TimeProvider.System);
        await using var state = new StateStore();
        await using var client = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, state, log, TimeProvider.System);
        var marker = new ObsMarker(client, state);

        Assert.Empty(marker.Validate(Parse(json)));
    }

    [Fact]
    public async Task The_step_declares_what_the_form_of_the_actions_screen_needs()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        var marker = new ObsMarker(session.Client, session.State);

        Assert.Equal("obs.marker", marker.Id);
        Assert.Equal("Marcar capítulo", marker.Name);
        Assert.Equal("OBS", marker.Group);
        Field type = marker.Fields[0];
        Field label = marker.Fields[1];
        Assert.Equal(2, marker.Fields.Count);
        Assert.Equal(("marker", FieldKind.Option, false), (type.Name, type.Kind, type.Optional));
        Assert.Equal(new[] { "short", "inicio", "fin", "sacar", "largo" }, type.Options);
        Assert.Equal(("label", FieldKind.Text, true), (label.Name, label.Kind, label.Optional));
        Assert.Null(label.Options);
    }
}
