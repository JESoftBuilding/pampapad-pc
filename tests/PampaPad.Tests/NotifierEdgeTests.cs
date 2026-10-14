using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que las pruebas del plan no fijan del avisador: la regla entera para cada estado de la toma y los nombres y pasos raros.</summary>
public class NotifierEdgeTests
{
    private const string NotRecording = "No estás grabando: la marca no entró.";

    [Theory]
    [InlineData(RecordingState.Stopped, false, true, true, true)]
    [InlineData(RecordingState.Stopped, false, false, true, true)]
    [InlineData(RecordingState.Stopped, true, true, false, false)]
    [InlineData(RecordingState.Stopped, true, false, true, false)]
    [InlineData(RecordingState.Starting, false, true, false, false)]
    [InlineData(RecordingState.Starting, false, false, true, false)]
    [InlineData(RecordingState.Starting, true, true, false, false)]
    [InlineData(RecordingState.Starting, true, false, true, false)]
    [InlineData(RecordingState.Recording, false, true, false, false)]
    [InlineData(RecordingState.Recording, false, false, true, false)]
    [InlineData(RecordingState.Recording, true, true, false, false)]
    [InlineData(RecordingState.Recording, true, false, true, false)]
    [InlineData(RecordingState.Paused, false, true, false, false)]
    [InlineData(RecordingState.Paused, false, false, true, false)]
    [InlineData(RecordingState.Paused, true, true, false, false)]
    [InlineData(RecordingState.Paused, true, false, true, false)]
    [InlineData(RecordingState.Stopping, false, true, false, false)]
    [InlineData(RecordingState.Stopping, false, false, true, false)]
    [InlineData(RecordingState.Stopping, true, true, false, false)]
    [InlineData(RecordingState.Stopping, true, false, true, false)]
    public void The_rule_for_every_state_of_the_take(RecordingState recording, bool streaming, bool mute, bool sound, bool balloon)
    {
        Assert.Equal(new NoticeChannels(sound, balloon), Notifier.Decide(recording, streaming, mute));
    }

    [Theory]
    [InlineData(RecordingState.Stopped, false, 1, 1)]
    [InlineData(RecordingState.Starting, false, 0, 0)]
    [InlineData(RecordingState.Recording, false, 0, 0)]
    [InlineData(RecordingState.Paused, false, 0, 0)]
    [InlineData(RecordingState.Stopping, false, 0, 0)]
    [InlineData(RecordingState.Stopped, true, 0, 0)]
    public async Task Each_channel_gets_one_notice_or_none_on_the_real_notifier(RecordingState recording, bool streaming, int sounds, int balloons)
    {
        await using var rig = new NotifierRig(recording: recording, streaming: streaming);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording));

        Assert.Equal(sounds, rig.Sounds.Count);
        Assert.Equal(balloons, rig.Balloons.Count);
        Assert.NotNull(rig.State.Current.LastFailure);
    }

    [Fact]
    public async Task The_link_is_not_what_decides_only_what_was_last_known_about_the_take()
    {
        // Sin enlace el programa no sabe si OBS sigue grabando: lo último que supo manda, y grabando no hay cartel.
        await using var closed = new NotifierRig(recording: RecordingState.Stopped, link: ObsLinkState.Closed);
        await using var lost = new NotifierRig(recording: RecordingState.Recording, link: ObsLinkState.Lost);

        closed.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.ObsDisconnected, "Sin OBS: la marca no entró."));
        lost.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.ObsDisconnected, "Sin OBS: la marca no entró."));

        Assert.Single(closed.Sounds);
        Assert.Single(closed.Balloons);
        Assert.Empty(lost.Sounds);
        Assert.Empty(lost.Balloons);
        Assert.NotNull(lost.State.Current.LastFailure);
    }

    [Theory]
    [InlineData("""{ "muteSoundWhileRecording": true }""")]
    [InlineData("{}")]
    public async Task The_mute_rule_is_on_unless_the_file_turns_it_off(string console)
    {
        await using var rig = new NotifierRig($$"""{ "version": 1, "actions": [ {{NotifierRig.ShortAction}} ], "console": {{console}} }""", RecordingState.Recording);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording));

        Assert.Empty(rig.Sounds);
        Assert.Empty(rig.Balloons);
    }

    [Fact]
    public async Task Without_a_library_the_default_rule_still_applies()
    {
        await using var rig = new NotifierRig(recording: RecordingState.Recording, config: "{ roto");

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording));

        Assert.Empty(rig.Sounds);
        Assert.Equal(new ActionFailure("marker_failed", "Falló marca-short: paso 1 (obs.marker)", NotRecording), rig.State.Current.LastFailure);
    }

    [Fact]
    public async Task A_step_that_is_not_a_marker_is_an_action_failed_with_its_own_number_and_type()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 2, "wait.window", ErrorCodes.Cancelled, "Pasó el tope de 60 s de la acción."));

        Assert.Equal(new ActionFailure("action_failed", "Falló Marca: short: paso 2 (wait.window)", "Pasó el tope de 60 s de la acción."), rig.State.Current.LastFailure);
        Assert.Equal("action_failed", Assert.Single(rig.Sounds).Id);
        Assert.Equal("action_failed", Assert.Single(rig.Balloons).Id);
    }

    [Fact]
    public async Task A_marker_with_a_bad_parameter_is_still_a_marker_failed()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.ConfigInvalid, "\"corto\" no es un tipo de marca."));

        Assert.Equal("marker_failed", rig.State.Current.LastFailure?.NoticeId);
    }

    [Fact]
    public async Task The_notice_carries_the_summary_as_title_and_the_message_as_text_in_both_channels()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording));

        var expected = new Notice("marker_failed", "Falló Marca: short: paso 1 (obs.marker)", NotRecording);
        Assert.Equal(expected, Assert.Single(rig.Sounds));
        Assert.Equal(expected, Assert.Single(rig.Balloons));
    }

    [Fact]
    public async Task An_action_without_steps_numbers_nothing()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", null, null, ErrorCodes.ConfigInvalid, "La acción tiene un problema y no corre."));

        Assert.Equal("Falló Marca: short", rig.State.Current.LastFailure?.Summary);
    }

    [Fact]
    public async Task A_step_with_a_number_but_no_type_does_not_print_empty_parentheses()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 3, null, ErrorCodes.InternalError, "El paso reventó."));
        Assert.Equal("Falló Marca: short: paso 3", rig.State.Current.LastFailure?.Summary);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 3, "", ErrorCodes.InternalError, "El paso reventó."));
        Assert.Equal("Falló Marca: short: paso 3", rig.State.Current.LastFailure?.Summary);
    }

    [Theory]
    [InlineData("""{ "id": "marca-short", "steps": [] }""")]
    [InlineData("""{ "id": "marca-short", "name": "", "steps": [] }""")]
    [InlineData("""{ "id": "marca-short", "name": "   ", "steps": [] }""")]
    public async Task An_action_without_a_name_is_called_by_its_id(string action)
    {
        await using var rig = new NotifierRig($$"""{ "version": 1, "actions": [ {{action}} ] }""");

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, "obs.marker", ErrorCodes.MarkerNotRecording, NotRecording));

        Assert.Equal("Falló marca-short: paso 1 (obs.marker)", rig.State.Current.LastFailure?.Summary);
    }

    [Fact]
    public async Task A_result_with_no_action_id_still_says_an_action_failed()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("", null, null, ErrorCodes.InternalError, "La acción reventó."));

        Assert.Equal("Falló una acción", rig.State.Current.LastFailure?.Summary);
    }

    [Fact]
    public async Task A_failure_with_no_message_still_notifies()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.InternalError, null));

        Assert.Equal(new ActionFailure("marker_failed", "Falló Marca: short: paso 1 (obs.marker)", ""), rig.State.Current.LastFailure);
        Assert.Equal(new Notice("marker_failed", "Falló Marca: short: paso 1 (obs.marker)", ""), Assert.Single(rig.Balloons));
    }
}
