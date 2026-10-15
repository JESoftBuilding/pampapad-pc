using System.Text.Encodings.Web;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

// El único lugar de estas pruebas que sabe cómo se arma un Log y cómo se leen sus entradas.
internal static class TestLogs
{
    private static readonly JsonSerializerOptions Plain = new(Json.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // El registro escribe en la carpeta temporal de la prueba, que es de quien la creó: la borra él al terminar.
    public static Log New(TempFolder temp) => new(temp.Combine("log"), TimeProvider.System);

    // Todo lo que el registro tiene en memoria, como texto y sin escapar, para buscar lo que no tiene que estar.
    public static string Text(Log log) => string.Join('\n', log.Recent().Select(entry => JsonSerializer.Serialize(entry, Plain)));
}
