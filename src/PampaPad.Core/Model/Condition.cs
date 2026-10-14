using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Una condición de <c>onlyIf</c> o del chequeo previo (02 §4.6). En esta etapa se lee y se conserva: evaluarla es de la etapa 5.</summary>
[JsonConverter(typeof(ConditionJsonConverter))]
public sealed record Condition
{
    public required string Type { get; init; }

    public bool? Negate { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];
}

internal sealed class ConditionJsonConverter : JsonConverter<Condition>
{
    public override Condition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Una condición tiene que ser un objeto.");
        }

        string type = "";
        bool? negate = null;
        var parameters = new Dictionary<string, JsonElement>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name == "type" && reader.TokenType == JsonTokenType.String)
            {
                type = reader.GetString()!;
            }
            else if (name == "negate" && reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            {
                negate = reader.GetBoolean();
            }
            else
            {
                parameters[name] = JsonElement.ParseValue(ref reader);
            }
        }

        return new Condition { Type = type, Negate = negate, Parameters = parameters };
    }

    public override void Write(Utf8JsonWriter writer, Condition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Type.Length > 0)
        {
            writer.WriteString("type", value.Type);
        }

        foreach ((string name, JsonElement parameter) in value.Parameters)
        {
            writer.WritePropertyName(name);
            parameter.WriteTo(writer);
        }

        if (value.Negate is { } negate)
        {
            writer.WriteBoolean("negate", negate);
        }

        writer.WriteEndObject();
    }
}
