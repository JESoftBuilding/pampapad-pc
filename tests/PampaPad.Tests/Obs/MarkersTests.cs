using PampaPad.Core;

namespace PampaPad.Tests;

public class MarkersTests
{
    private const string CommentOfObs = "Verify that the output being used supports chapter markers.";

    private static ObsResponse Answer(int code, string? comment = null) => new(code == 100, code, comment, default);

    [Theory]
    [InlineData(MarkerType.Short, null, "short")]
    [InlineData(MarkerType.Inicio, "demo motores", "inicio demo motores")]
    [InlineData(MarkerType.Fin, "  ", "fin")]
    [InlineData(MarkerType.Sacar, " se cayó el cable ", "sacar se cayó el cable")]
    [InlineData(MarkerType.Largo, "", "largo")]
    public void The_title_is_the_type_and_then_the_label(MarkerType type, string? label, string title)
    {
        Assert.Equal(title, Markers.Title(type, label));
    }

    [Theory]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    public void A_100_is_a_marker_that_went_in(RecordingState recording)
    {
        Assert.Null(Markers.Interpret(Answer(100), recording));
    }

    [Theory]
    [InlineData(501, null, RecordingState.Stopped, "marker_not_recording", "No estás grabando: la marca no entró.")]
    [InlineData(501, null, RecordingState.Recording, "marker_not_recording", "No estás grabando: la marca no entró.")]
    [InlineData(702, CommentOfObs, RecordingState.Paused, "marker_paused", "Estás en pausa: la marca no entró.")]
    [InlineData(702, CommentOfObs, RecordingState.Recording, "marker_rejected", "OBS rechazó la marca: el formato de grabación no admite capítulos.")]
    [InlineData(204, null, RecordingState.Recording, "marker_unsupported", "Este OBS no admite marcas.")]
    [InlineData(0, "obs_disconnected", RecordingState.Recording, "obs_disconnected", "Sin OBS: la marca no entró.")]
    [InlineData(0, "obs_timeout", RecordingState.Recording, "obs_timeout", "OBS no contestó: la marca no entró.")]
    public void A_rejection_becomes_the_code_and_the_text_of_the_table(int code, string? comment, RecordingState recording, string expectedCode, string expectedMessage)
    {
        Failure? failure = Markers.Interpret(Answer(code, comment), recording);

        Assert.NotNull(failure);
        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(expectedMessage, failure.Message);
    }

    [Fact]
    public void Any_other_code_is_a_failed_request_with_the_code_and_the_comment_of_obs()
    {
        Failure? failure = Markers.Interpret(Answer(604, "The specified input does not support audio."), RecordingState.Recording);

        Assert.NotNull(failure);
        Assert.Equal(ErrorCodes.ObsRequestFailed, failure.Code);
        Assert.Contains("604", failure.Message);
        Assert.Contains("does not support audio", failure.Message);
    }
}
