using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

/// <summary>Las condiciones de perfil, formato, micrófono y audio de la cámara, contra el OBS falso con lo medido el 2026-10-13.</summary>
public class ObsConditionsTests
{
    [Fact]
    public async Task The_profile_is_the_one_on_air()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult same = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.profileIs", "profile": "Sin Título" }""");
        ConditionResult other = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.profileIs", "profile": "Reels vertical" }""");

        Assert.Equal(ConditionOutcome.Pass, same.Outcome);
        Assert.Equal(ConditionOutcome.Fail, other.Outcome);
        Assert.Equal("El perfil activo es \"Sin Título\"; se pedía \"Reels vertical\".", other.Detail);
    }

    [Fact]
    public async Task The_record_format_is_read_from_the_output_mode_of_the_profile()
    {
        await using ObsSession session = await ObsSession.StartAsync(before: obs => obs.RecordFormat = "mkv");

        ConditionResult advanced = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.recordFormatIs", "format": "hybrid_mp4" }""");
        session.Obs.OutputMode = "Simple";
        ConditionResult simple = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.recordFormatIs", "format": "mkv" }""");
        session.Obs.OutputMode = null;      // sin parameterValue: vale defaultParameterValue, Simple (03 §7)
        ConditionResult unwritten = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.recordFormatIs", "format": "mkv" }""");

        Assert.Equal(ConditionOutcome.Fail, advanced.Outcome);
        Assert.Equal("El perfil graba en mkv (salida Advanced); se pedía hybrid_mp4.", advanced.Detail);
        Assert.Equal(ConditionOutcome.Pass, simple.Outcome);
        Assert.Equal(ConditionOutcome.Pass, unwritten.Outcome);
        Assert.Equal("El perfil graba en mkv (salida Simple); se pedía mkv.", unwritten.Detail);
    }

    [Fact]
    public async Task The_mic_is_the_one_the_state_reflects_and_its_events()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult open = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.micNotMuted" }""");
        await session.Obs.SetMicMutedAsync(true);
        await Poll.Until(() => session.State.Current.Obs.MicMuted);
        ConditionResult muted = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.micNotMuted" }""");

        Assert.Equal(ConditionOutcome.Pass, open.Outcome);
        Assert.Equal("El micrófono \"Mic/Aux\" está abierto.", open.Detail);
        Assert.Equal(ConditionOutcome.Fail, muted.Outcome);
    }

    [Fact]
    public async Task Another_input_is_asked_and_without_mic1_it_does_not_apply()
    {
        await using ObsSession session = await ObsSession.StartAsync(before: obs => obs.HasMic = false);

        ConditionResult sting = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.micNotMuted", "source": "Sting triste" }""");
        ConditionResult none = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.micNotMuted" }""");
        ConditionResult missing = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.micNotMuted", "source": "No existe" }""");

        Assert.Equal(ConditionOutcome.Pass, sting.Outcome);
        Assert.Equal(ConditionOutcome.Pass, none.Outcome);
        Assert.StartsWith("No aplica", none.Detail, StringComparison.Ordinal);
        Assert.Equal(ConditionOutcome.Skipped, missing.Outcome);
        Assert.Equal(ErrorCodes.ObsRefNotFound, missing.Failure?.Code);
    }

    [Fact]
    public async Task The_camera_two_pieces_inside_pantalla_is_muted_and_without_tracks_until_it_is_opened()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult silent = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraAudioOff" }""");
        session.Obs.SetAudioTrack(FakeObs.CameraInputUuid, 6, on: true);
        ConditionResult track = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraAudioOff" }""");
        await session.Client.RequestAsync("SetInputMute", new { inputUuid = FakeObs.CameraInputUuid, inputMuted = false }, CancellationToken.None);
        ConditionResult loud = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraAudioOff" }""");

        Assert.Equal(ConditionOutcome.Pass, silent.Outcome);
        Assert.Equal(ConditionOutcome.Fail, track.Outcome);         // silenciada, pero la pista 6 la sigue mandando a la grabación
        Assert.Equal("La cámara \"Dispositivo de captura de video\" manda audio por la pista 6.", track.Detail);
        Assert.Equal(ConditionOutcome.Fail, loud.Outcome);
        Assert.Equal("La cámara \"Dispositivo de captura de video\" no está silenciada.", loud.Detail);
    }

    [Fact]
    public async Task The_camera_audio_does_not_apply_to_the_phone_window_nor_to_a_scene_without_camera()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await ConditionProbe.OnAirAsync(session, "Banco", FakeObs.BancoUuid);

        ConditionResult banco = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraAudioOff" }""");
        ConditionResult phone = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraAudioOff", "source": "Celu (scrcpy)" }""");

        Assert.Equal(ConditionOutcome.Pass, banco.Outcome);
        Assert.Equal("No aplica: no hay cámara en \"Banco\".", banco.Detail);
        Assert.Equal(ConditionOutcome.Pass, phone.Outcome);
        Assert.Equal("No aplica: \"Celu (scrcpy)\" no es una cámara al aire en \"Banco\".", phone.Detail);
    }

    [Theory]
    [InlineData("""{ "type": "obs.profileIs", "profile": "Sin Título" }""")]
    [InlineData("""{ "type": "obs.recordFormatIs", "format": "hybrid_mp4" }""")]
    [InlineData("""{ "type": "obs.micNotMuted" }""")]
    [InlineData("""{ "type": "obs.cameraAudioOff" }""")]
    public async Task Without_obs_each_one_is_skipped_and_never_counts_as_fine(string json)
    {
        await using var offline = new OfflineObs();

        ConditionResult result = await ConditionProbe.EvaluateAsync(offline.Client, offline.State, json);

        Assert.Equal(ConditionOutcome.Skipped, result.Outcome);
        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
    }

    // El token llega a OBS: el chequeo corta con su tope cancelando (E5-17), y sin esto cada pedido esperaría su propio tope.
    [Theory]
    [InlineData("""{ "type": "obs.profileIs", "profile": "Sin Título" }""")]
    [InlineData("""{ "type": "obs.recordFormatIs", "format": "hybrid_mp4" }""")]
    [InlineData("""{ "type": "obs.micNotMuted", "source": "Sting triste" }""")]
    [InlineData("""{ "type": "obs.cameraAudioOff" }""")]
    public async Task With_the_token_already_cancelled_each_one_is_skipped_as_cancelled(string json)
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult result = await ConditionProbe.EvaluateAsync(session, json, new CancellationToken(true));

        Assert.Equal(ConditionOutcome.Skipped, result.Outcome);
        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
    }

    [Theory]
    [InlineData("""{ "type": "obs.profileIs" }""", "profile")]
    [InlineData("""{ "type": "obs.profileIs", "profile": "" }""", "profile")]
    [InlineData("""{ "type": "obs.recordFormatIs", "format": 4 }""", "format")]
    [InlineData("""{ "type": "obs.micNotMuted", "source": 5 }""", "source")]
    [InlineData("""{ "type": "obs.cameraAudioOff", "source": { "uuid": "668dffa0-e0b7-4ae5-b469-a99fd220fb96" } }""", "source")]
    public async Task A_missing_text_is_a_problem_of_the_condition(string json, string where)
    {
        await using var offline = new OfflineObs();
        Condition condition = System.Text.Json.JsonSerializer.Deserialize<Condition>(json, Json.Options)!;
        ConditionType type = Conditions.Build(new FakeWindows(), offline.State, offline.Client).Single(candidate => candidate.Id == condition.Type);

        Problem problem = Assert.Single(ConditionRules.Validate(type, condition));

        Assert.Equal(where, problem.Where);
    }
}
