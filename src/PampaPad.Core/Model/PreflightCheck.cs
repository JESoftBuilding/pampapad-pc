using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Una comprobación del chequeo previo (02 §4.9). En esta etapa se lee y se conserva.</summary>
public sealed record PreflightCheck
{
    public Condition? Condition { get; init; }

    public string? Message { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
