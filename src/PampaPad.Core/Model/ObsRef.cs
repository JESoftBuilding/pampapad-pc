using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>
/// Referencia a una escena o a una entrada de OBS (02 §4.8): se resuelve por <c>uuid</c> y, si no está, por <c>name</c>.
/// Vive dentro de los parámetros de un paso, que se conservan tal cual: por eso no guarda campos desconocidos.
/// </summary>
[JsonConverter(typeof(ObsRefJsonConverter))]
public readonly record struct ObsRef(string? Uuid, string Name);

internal sealed class ObsRefJsonConverter : JsonConverter<ObsRef>
{
    public override ObsRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Para escribir a mano, el lector acepta también el texto solo: "scene": "Banco".
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ObsRef(null, reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Una referencia a OBS es un texto o un objeto con uuid y name.");
        }

        string? uuid = null;
        string name = "";
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string property = reader.GetString()!;
            reader.Read();
            if (property == "uuid" && reader.TokenType == JsonTokenType.String)
            {
                uuid = reader.GetString();
            }
            else if (property == "name" && reader.TokenType == JsonTokenType.String)
            {
                name = reader.GetString()!;
            }
            else
            {
                reader.Skip();
            }
        }

        return new ObsRef(uuid, name);
    }

    public override void Write(Utf8JsonWriter writer, ObsRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Uuid is not null)
        {
            writer.WriteString("uuid", value.Uuid);
        }

        writer.WriteString("name", value.Name);
        writer.WriteEndObject();
    }
}
