using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Una página (02 §4.2). El id se fija al crear y no cambia nunca.</summary>
public sealed record Page
{
    /// <summary>knob: el giro de la perilla recorre las páginas. Es lo mismo que no tener el campo.</summary>
    public const string KnobPages = "pages";

    /// <summary>knob: el giro manda un adjustBrightness a las luces (plan de la 1.3.0, decisión 7).</summary>
    public const string KnobBrightness = "brightness";

    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Type { get; init; }

    public IReadOnlyList<Key>? Keys { get; init; }

    public bool? Show { get; init; }

    /// <summary>Qué hace el giro de la perilla mientras esta página se ve: KnobPages (o ausente) o KnobBrightness. Otro valor se conserva.</summary>
    public string? Knob { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
