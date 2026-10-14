using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Una acción: una lista de pasos que corren en orden (02 §4.4). Choca con System.Action: ver la pregunta abierta 58.</summary>
public sealed record Action
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<Step>? Steps { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
