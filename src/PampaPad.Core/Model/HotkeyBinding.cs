using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Un atajo de la lista general: vale en cualquier página (02 §4.7).</summary>
public sealed record HotkeyBinding
{
    public string? Hotkey { get; init; }

    public string? Action { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
