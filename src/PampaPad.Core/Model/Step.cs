using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Un paso de una acción. Los parámetros van al mismo nivel que <c>type</c> (02 §4.5).</summary>
[JsonConverter(typeof(StepJsonConverter))]
public sealed record Step
{
    public required string Type { get; init; }

    public Condition? OnlyIf { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];
}

internal sealed class StepJsonConverter : JsonConverter<Step>
{
    public override Step Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Un paso tiene que ser un objeto.");
        }

        string type = "";
        Condition? onlyIf = null;
        var parameters = new Dictionary<string, JsonElement>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name == "type" && reader.TokenType == JsonTokenType.String)
            {
                type = reader.GetString()!;
            }
            else if (name == "onlyIf" && reader.TokenType == JsonTokenType.StartObject)
            {
                onlyIf = JsonSerializer.Deserialize<Condition>(ref reader, options);
            }
            else
            {
                parameters[name] = JsonElement.ParseValue(ref reader);
            }
        }

        return new Step { Type = type, OnlyIf = onlyIf, Parameters = parameters };
    }

    public override void Write(Utf8JsonWriter writer, Step value, JsonSerializerOptions options)
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

        if (value.OnlyIf is not null)
        {
            writer.WritePropertyName("onlyIf");
            JsonSerializer.Serialize(writer, value.OnlyIf, options);
        }

        writer.WriteEndObject();
    }
}
