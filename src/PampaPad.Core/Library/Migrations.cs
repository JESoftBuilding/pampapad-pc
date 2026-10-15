using System.Text.Json.Nodes;

namespace PampaPad.Core;

/// <summary>
/// Lleva un documento viejo a la versión actual, antes de deserializarlo (02 §11).
/// Hoy no existe ningún config.json anterior a la versión 1: lo único que hace es frenar uno más nuevo.
/// </summary>
public static class Migrations
{
    public const int CurrentVersion = 1;

    public static Result<JsonNode> Upgrade(JsonNode document, string libraryFolder)
    {
        // Una migración recibe la carpeta para poder mover archivos de scripts/. Todavía no hay ninguna.
        _ = libraryFolder;

        // Sin "version" no se frena acá: lo informa el validador como campo que falta.
        int version = document["version"] is JsonValue value && value.TryGetValue(out int number) ? number : CurrentVersion;
        if (version > CurrentVersion)
        {
            return Result<JsonNode>.Fail(
                ErrorCodes.ConfigNewer,
                $"config.json es de la versión {version} y este programa entiende hasta la {CurrentVersion}. No se carga ni se pisa: actualizá PampaPad.",
                "version");
        }

        return Result<JsonNode>.Success(document);
    }
}
