using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Las únicas JsonSerializerOptions del programa (01 §2).</summary>
public static class Json
{
    /// <summary>Para archivos que lee una persona y versiona git: sangría de dos espacios y fin de línea \n (02 §3, regla 8).</summary>
    public static JsonSerializerOptions Options { get; } = Create(indented: true);

    /// <summary>Para lo que va en una línea: el registro y, más adelante, los sobres del puente y los mensajes de OBS.</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Deja pasar acentos y el "+" de los atajos. Nada de esto se incrusta en HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = indented,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n",
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
