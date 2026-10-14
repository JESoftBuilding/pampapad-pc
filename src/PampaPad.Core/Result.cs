using System.Diagnostics.CodeAnalysis;

namespace PampaPad.Core;

/// <summary>Una falla esperada. Viaja como dato, nunca como excepción (01 §4.1).</summary>
public sealed record Failure(string Code, string Message, string? Where = null);

/// <summary>El resultado de algo que puede fallar y no devuelve nada.</summary>
public readonly record struct Result
{
    private Result(Failure? failure) => Failure = failure;

    public Failure? Failure { get; }

    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Ok => Failure is null;

    public static Result Success => default;

    public static Result Fail(string code, string message, string? where = null) =>
        new(new Failure(code, message, where));

    public static implicit operator Result(Failure failure) => new(failure);
}

/// <summary>El resultado de algo que puede fallar y, si anda, devuelve un valor.</summary>
public readonly record struct Result<T>
{
    private Result(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Ok => Failure is null;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Fail(string code, string message, string? where = null) =>
        new(default, new Failure(code, message, where));

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(Failure failure) => new(default, failure);
}

/// <summary>Los códigos de falla. La lista es la de 01 §4.1: no se agregan ni se renombran acá.</summary>
public static class ErrorCodes
{
    // Configuración y puente
    public const string ConfigInvalid = "config_invalid";
    public const string ConfigUnreadable = "config_unreadable";
    public const string ConfigNewer = "config_newer";
    public const string ConfigChangedOnDisk = "config_changed_on_disk";
    public const string NavigationBlocked = "navigation_blocked";
    public const string ReadOnlyWhileRecording = "read_only_while_recording";
    public const string NoPermission = "no_permission";
    public const string Cancelled = "cancelled";
    public const string UnknownRequest = "unknown_request";
    public const string InvalidData = "invalid_data";
    public const string InternalError = "internal_error";
    public const string NoResponse = "no_response";
    public const string AppNotRunning = "app_not_running";

    // OBS y marcas
    public const string ObsDisconnected = "obs_disconnected";
    public const string ObsTimeout = "obs_timeout";
    public const string ObsRequestFailed = "obs_request_failed";
    public const string ObsRefNotFound = "obs_ref_not_found";
    public const string NotRecording = "not_recording";
    public const string MarkerNotRecording = "marker_not_recording";
    public const string MarkerPaused = "marker_paused";
    public const string MarkerRejected = "marker_rejected";
    public const string MarkerUnsupported = "marker_unsupported";

    // Scripts, ventanas, consola y atajos
    public const string ScriptNotFound = "script_not_found";
    public const string ScriptWithoutManifest = "script_without_manifest";
    public const string ScriptFailed = "script_failed";
    public const string ScriptTimeout = "script_timeout";
    public const string ScriptNotApproved = "script_not_approved";
    public const string WindowNotFound = "window_not_found";
    public const string WindowElevated = "window_elevated";
    public const string ConsoleDisconnected = "console_disconnected";
    public const string HotkeyTaken = "hotkey_taken";
}
