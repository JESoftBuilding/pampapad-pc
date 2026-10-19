using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>
/// La ficha de un script, script.json, con la forma del JSON (02 §9). El id no va adentro: es el nombre de la carpeta. Lo que falta se
/// resuelve al correr (02 §3, regla 3): wait verdadero, timeoutSec 15, contract 1. Lo que el programa no conoce queda en Extra.
/// </summary>
public sealed record ScriptManifest
{
    public string? Entry { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public bool? Wait { get; init; }

    public int? TimeoutSec { get; init; }

    public int? Contract { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}
