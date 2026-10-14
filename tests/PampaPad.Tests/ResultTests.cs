using System.Reflection;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ResultTests
{
    [Fact]
    public void Success_has_no_failure()
    {
        var result = Result.Success;

        Assert.True(result.Ok);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Fail_carries_code_message_and_where()
    {
        var result = Result.Fail(ErrorCodes.ConfigUnreadable, "No se puede leer", "local.json");

        Assert.False(result.Ok);
        Assert.Equal(new Failure("config_unreadable", "No se puede leer", "local.json"), result.Failure);
    }

    [Fact]
    public void A_failure_converts_to_both_kinds_of_result()
    {
        var failure = new Failure(ErrorCodes.Cancelled, "Se canceló");

        Result plain = failure;
        Result<int> typed = failure;

        Assert.Same(failure, plain.Failure);
        Assert.Same(failure, typed.Failure);
        Assert.False(typed.Ok);
    }

    [Fact]
    public void A_value_converts_to_a_successful_result()
    {
        Result<string> result = "0.1.1";

        Assert.True(result.Ok);
        Assert.Null(result.Failure);
        Assert.Equal("0.1.1", result.Value);
    }

    [Fact]
    public void Typed_fail_has_no_value()
    {
        var result = Result<string>.Fail(ErrorCodes.NoResponse, "Sin red");

        Assert.False(result.Ok);
        Assert.Null(result.Value);
        Assert.Equal("no_response", result.Failure?.Code);
    }

    [Fact]
    public void Error_codes_are_exactly_the_31_of_the_spec()
    {
        string[] expected =
        [
            "config_invalid", "config_unreadable", "config_newer", "config_changed_on_disk", "navigation_blocked",
            "read_only_while_recording", "no_permission", "cancelled", "unknown_request", "invalid_data",
            "internal_error", "no_response", "app_not_running",
            "obs_disconnected", "obs_timeout", "obs_request_failed", "obs_ref_not_found", "not_recording",
            "marker_not_recording", "marker_paused", "marker_rejected", "marker_unsupported",
            "script_not_found", "script_without_manifest", "script_failed", "script_timeout", "script_not_approved",
            "window_not_found", "window_elevated", "console_disconnected", "hotkey_taken",
        ];

        var actual = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(31, expected.Length);
        Assert.Equal(expected.Order(), actual.Order());
        Assert.All(actual, code => Assert.Matches("^[a-z]+(_[a-z]+)*$", code));
    }
}
