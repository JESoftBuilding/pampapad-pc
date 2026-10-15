using PampaPad.Core;

namespace PampaPad.Tests;

// Lo que MarkersTests, la del plan, no fija: el cancelado, el texto que sólo depende del estado y los bordes de cada código.
public class MarkersEdgeTests
{
    private static ObsResponse Answer(int code, string? comment = null) => new(code == 100, code, comment, default);

    public static TheoryData<RecordingState> AllStates => new()
    {
        RecordingState.Stopped,
        RecordingState.Starting,
        RecordingState.Recording,
        RecordingState.Paused,
        RecordingState.Stopping,
    };

    [Theory]
    [MemberData(nameof(AllStates))]
    public void A_cancelled_request_is_cancelled_and_not_a_missing_obs(RecordingState recording)
    {
        // 01 §4.5: un paso cancelado devuelve cancelled. Decir "Sin OBS" a quien canceló, o a quien pasó el tope de 60 s, sería falso.
        Failure? failure = Markers.Interpret(Answer(0, ErrorCodes.Cancelled), recording);

        Assert.NotNull(failure);
        Assert.Equal(ErrorCodes.Cancelled, failure.Code);
        Assert.Equal("Se canceló: no se sabe si la marca entró.", failure.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("algo que el cliente no dice")]
    public void An_answer_that_never_came_for_any_other_reason_is_a_missing_obs(string? comment)
    {
        Failure? failure = Markers.Interpret(Answer(0, comment), RecordingState.Recording);

        Assert.Equal(ErrorCodes.ObsDisconnected, failure?.Code);
        Assert.Equal("Sin OBS: la marca no entró.", failure?.Message);
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void A_501_is_never_recording_whatever_the_known_state_says(RecordingState recording)
    {
        // El estado conocido puede estar un instante atrasado: manda lo que contesta OBS.
        Failure? failure = Markers.Interpret(Answer(501), recording);

        Assert.Equal(ErrorCodes.MarkerNotRecording, failure?.Code);
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void A_204_is_an_unsupported_obs_whatever_the_known_state_says(RecordingState recording)
    {
        Failure? failure = Markers.Interpret(Answer(204, "Your request type is not valid."), recording);

        Assert.Equal(ErrorCodes.MarkerUnsupported, failure?.Code);
    }

    [Theory]
    [InlineData(RecordingState.Stopped)]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Stopping)]
    public void A_702_is_a_rejected_format_in_any_state_but_paused(RecordingState recording)
    {
        Failure? failure = Markers.Interpret(Answer(702, "Verify that the output being used supports chapter markers."), recording);

        Assert.Equal(ErrorCodes.MarkerRejected, failure?.Code);
    }

    [Theory]
    [InlineData(null, RecordingState.Paused, ErrorCodes.MarkerPaused)]
    [InlineData("otro comentario", RecordingState.Paused, ErrorCodes.MarkerPaused)]
    [InlineData(null, RecordingState.Recording, ErrorCodes.MarkerRejected)]
    [InlineData("otro comentario", RecordingState.Recording, ErrorCodes.MarkerRejected)]
    public void The_text_of_a_702_comes_from_the_known_state_and_never_from_the_comment_of_obs(string? comment, RecordingState recording, string expected)
    {
        Failure? failure = Markers.Interpret(Answer(702, comment), recording);

        Assert.Equal(expected, failure?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_failed_request_without_a_comment_does_not_end_in_a_dangling_colon(string? comment)
    {
        Failure? failure = Markers.Interpret(Answer(600, comment), RecordingState.Recording);

        Assert.Equal(ErrorCodes.ObsRequestFailed, failure?.Code);
        Assert.Equal("OBS rechazó la marca (código 600).", failure?.Message);
    }

    [Fact]
    public void A_failed_request_with_a_comment_says_the_code_and_then_the_comment()
    {
        Failure? failure = Markers.Interpret(Answer(600, "No source was found."), RecordingState.Recording);

        Assert.Equal("OBS rechazó la marca (código 600): No source was found.", failure?.Message);
    }

    [Fact]
    public void The_request_type_is_the_one_of_obs_websocket()
    {
        // ObsMessages.Supports lo busca en availableRequests por este nombre: si cambia, ningún OBS "admite" marcas.
        Assert.Equal("CreateRecordChapter", Markers.RequestType);
    }

    [Theory]
    [InlineData("a\tb", "short a\tb")]
    [InlineData("  con  espacios  adentro  ", "short con  espacios  adentro")]
    public void The_label_is_trimmed_at_the_ends_and_nothing_else_is_touched(string label, string title)
    {
        Assert.Equal(title, Markers.Title(MarkerType.Short, label));
    }

    [Fact]
    public void The_five_words_are_the_ones_of_the_contract()
    {
        Assert.Equal(
            new[] { "short", "inicio", "fin", "sacar", "largo" },
            Enum.GetValues<MarkerType>().Select(type => Markers.Title(type, null)));
    }
}
