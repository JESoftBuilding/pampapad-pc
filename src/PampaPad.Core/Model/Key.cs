using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Una tecla de una página (02 §4.3). Se identifica por el id de su página y su <c>position</c>.</summary>
public sealed record Key
{
    public int? Position { get; init; }

    public string? Label { get; init; }

    public IReadOnlyList<string>? ShortLabel { get; init; }

    public string? Action { get; init; }

    public string? Hotkey { get; init; }

    public HoldAction? OnHold { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}

/// <summary>Lo que sale al mantener. Es un objeto para poder sumarle campos sin migrar.</summary>
public sealed record HoldAction
{
    public string? Action { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
