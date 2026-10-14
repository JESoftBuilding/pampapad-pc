using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Una página (02 §4.2). El id se fija al crear y no cambia nunca.</summary>
public sealed record Page
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Type { get; init; }

    public IReadOnlyList<Key>? Keys { get; init; }

    public bool? Show { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
