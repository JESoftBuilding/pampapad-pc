using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>
/// El bloque <c>console</c> (02 §4.10). Los valores por defecto no se escriben nunca: se resuelven al usar.
/// En esta etapa el programa sólo lee <c>muteSoundWhileRecording</c>, por los sonidos de la PC.
/// </summary>
public sealed record ConsoleSettings
{
    public const bool DefaultMuteSoundWhileRecording = true;

    public ScreenSettings? Screen { get; init; }

    public int? SleepAfterSec { get; init; }

    public int? HoldMs { get; init; }

    public IReadOnlyDictionary<string, AlertChannels>? Alerts { get; init; }

    public bool? MuteSoundWhileRecording { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}

public sealed record ScreenSettings
{
    public int? Brightness { get; init; }

    public int? DimAfterSec { get; init; }

    public int? DimBrightness { get; init; }

    public int? OffAfterSec { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}

public sealed record AlertChannels
{
    public bool? Light { get; init; }

    public bool? Sound { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
