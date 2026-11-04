using PampaPad.Core;
using static PampaPad.Tests.ProjectionCase;

namespace PampaPad.Tests;

/// <summary>Las 12 teclas de la página que rige: su rol, sus letras y las dos máscaras (plan de la 9a, decisiones 4 y 11; E9-5).</summary>
public class ConsoleProjectionKeysTests
{
    [Fact]
    public void Each_key_gets_the_role_of_its_action_and_the_hold_bit_if_it_has_on_hold()
    {
        ConsoleFrame frame = Project(State());

        Assert.Equal(new KeyFace(KeyRole.Danger, "GRABAR", "", Hold: true), frame.Key(0));
        Assert.Equal(new KeyFace(KeyRole.Danger, "MIC", ""), frame.Key(1));
        Assert.Equal(new KeyFace(KeyRole.Scene, "CAMARA", ""), frame.Key(2));
        Assert.Equal(new KeyFace(KeyRole.Scene, "BANCO", ""), frame.Key(3));
        Assert.Equal(new KeyFace(KeyRole.Marker, "SHORT", ""), frame.Key(4));
        Assert.Equal(new KeyFace(KeyRole.Caution, "SEGUIR", "", Hold: true), frame.Key(5));
        Assert.Equal(new KeyFace(KeyRole.Disabled, "ROTA", ""), frame.Key(6));
        Assert.Equal(new KeyFace(KeyRole.Normal, "IR AL", "BANCO"), frame.Key(7));
        Assert.Equal(12, frame.Keys.Count);
    }

    [Fact]
    public void The_word_is_the_one_emmanuel_wrote_whatever_the_state()
    {
        AppState recording = State(RecordingState.Recording) with { Obs = State().Obs with { Recording = RecordingState.Recording, MicMuted = true } };

        Assert.Equal(Project(State()).Keys, Project(recording).Keys);
    }

    [Fact]
    public void The_scene_on_air_is_active_and_the_others_are_not()
    {
        AppState banco = State() with { Obs = State().Obs with { SceneName = "Banco viejo", SceneUuid = "u-banco" } };

        Assert.True(Bit(Project(State()).Screen.ActiveMask, 3));
        Assert.False(Bit(Project(State()).Screen.ActiveMask, 4));
        Assert.True(Bit(Project(banco).Screen.ActiveMask, 4));
        Assert.False(Bit(Project(banco).Screen.ActiveMask, 3));
    }

    [Theory]
    [InlineData(RecordingState.Stopped, false, false)]
    [InlineData(RecordingState.Recording, true, false)]
    [InlineData(RecordingState.Paused, true, true)]
    public void The_record_key_is_active_while_recording_and_the_pause_key_while_paused(RecordingState recording, bool record, bool pause)
    {
        int active = Project(State(recording)).Screen.ActiveMask;

        Assert.Equal((record, pause), (Bit(active, 1), Bit(active, 6)));
    }

    [Fact]
    public void The_mic_key_is_active_with_the_mic_muted()
    {
        AppState muted = State() with { Obs = State().Obs with { MicMuted = true } };

        Assert.True(Bit(Project(muted).Screen.ActiveMask, 2));
        Assert.False(Bit(Project(State()).Screen.ActiveMask, 2));
    }

    [Fact]
    public void Empty_cells_and_a_key_whose_action_is_missing_go_in_the_off_mask()
    {
        ConsoleFrame frame = Project(State());

        Assert.Equal(0b1111_0100_0000, frame.Screen.OffMask);
        Assert.All(frame.Keys.Skip(8), key => Assert.Equal(KeyFace.Empty, key));
    }

    [Fact]
    public void Without_obs_ready_nothing_is_active()
    {
        AppState lost = State(RecordingState.Recording, ObsLinkState.Lost) with { Obs = State().Obs with { Link = ObsLinkState.Lost, MicMuted = true } };

        Assert.Equal(0, Project(lost).Screen.ActiveMask);
    }

    [Fact]
    public void The_page_is_its_index_in_pages_and_its_keys_are_the_ones_shown()
    {
        ConsoleFrame sala = Project(State(page: "sala"));

        Assert.Equal(1, sala.Screen.Page);
        Assert.Equal(KeyFace.Empty, sala.Key(0));
        Assert.Equal(new KeyFace(KeyRole.Normal, "LUCES", ""), sala.Key(1));
        Assert.Equal(0b1111_1111_1101, sala.Screen.OffMask);
    }

    [Fact]
    public void A_page_that_does_not_exist_or_no_library_does_not_throw_and_shows_empty_keys()
    {
        ConsoleFrame missing = Project(State(page: "no-existe"));
        ConsoleFrame none = ConsoleProjection.Project(AppState.Initial, new Config(), Now);

        Assert.Equal(0, missing.Screen.Page);
        Assert.All(missing.Keys, key => Assert.Equal(KeyFace.Empty, key));
        Assert.Equal(0xFFF, none.Screen.OffMask);
        Assert.Equal(ConsoleRecording.NoObs, none.Screen.Recording);
    }

    // Revisión de la tanda 2 de la 9a (should): una tecla de toque normal con un mantenido de micrófono, escena o marca toma el rol del mantenido.
    [Theory]
    [InlineData("mic", KeyRole.Danger)]
    [InlineData("camara", KeyRole.Scene)]
    [InlineData("marca", KeyRole.Marker)]
    public void A_normal_tap_with_a_special_hold_takes_the_role_of_the_hold(string hold, KeyRole expected)
    {
        var key = new Key { Position = 1, Label = "X", Action = "script", OnHold = new HoldAction { Action = hold } };

        (KeyKind kind, _) = KeyRoles.Of(key, Sample);

        Assert.Equal(expected, KeyRoles.RoleOf(kind));
    }
}
