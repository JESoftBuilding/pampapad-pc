using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsStepTests
{
    [Theory]
    [InlineData("obs_timeout", "obs_timeout")]
    [InlineData("cancelled", "cancelled")]
    [InlineData("obs_disconnected", "obs_disconnected")]
    [InlineData("obs_request_failed", "obs_request_failed")]
    public void Without_an_answer_the_code_of_the_program_decides(string comment, string code)
    {
        Assert.Equal(code, ObsStep.Rejected(ObsResponse.Fail(comment), "el pedido de prueba").Code);
    }

    [Fact]
    public void An_answer_without_status_is_not_a_missing_obs()
    {
        Failure failure = ObsStep.Rejected(ObsResponse.Fail(ErrorCodes.ObsRequestFailed), "el cambio de escena");

        Assert.DoesNotContain("Sin OBS", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_of_obs_goes_with_its_comment()
    {
        Failure failure = ObsStep.Rejected(new ObsResponse(false, 604, "The specified input does not support audio.", default), "el silencio");

        Assert.Equal(ErrorCodes.ObsRequestFailed, failure.Code);
        Assert.Equal("OBS rechazó el silencio (código 604): The specified input does not support audio.", failure.Message);
    }

    [Fact]
    public void A_code_without_comment_leaves_nothing_hanging()
    {
        Failure failure = ObsStep.Rejected(new ObsResponse(false, 501, null, default), "el pedido de parar");

        Assert.Equal("OBS rechazó el pedido de parar (código 501).", failure.Message);
    }

    [Theory]
    [InlineData(ObsLinkState.Ready, null)]
    [InlineData(ObsLinkState.Loading, "obs_disconnected")]
    [InlineData(ObsLinkState.Switching, "obs_disconnected")]
    [InlineData(ObsLinkState.Lost, "obs_disconnected")]
    [InlineData(ObsLinkState.Closed, "obs_disconnected")]
    public void Only_a_ready_link_sends(ObsLinkState link, string? code)
    {
        Assert.Equal(code, ObsStep.Unready(ObsState.Initial with { Link = link }, "el cambio de escena")?.Code);
    }

    [Fact]
    public void A_token_already_cancelled_sends_nothing()
    {
        using var cut = new CancellationTokenSource();
        cut.Cancel();

        Assert.Null(ObsStep.CancelledBefore(CancellationToken.None, "el cambio de escena"));
        Assert.Equal(ErrorCodes.Cancelled, ObsStep.CancelledBefore(cut.Token, "el cambio de escena")?.Code);
    }
}
