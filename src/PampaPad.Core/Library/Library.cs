using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PampaPad.Core;

public enum LibraryStatus
{
    /// <summary>Todavía no se llamó a Load.</summary>
    NotLoaded,
    Loaded,

    /// <summary>No hay carpeta de datos elegida, o no está.</summary>
    NoLibrary,
    Unreadable,
    Newer,
}

/// <summary>
/// La biblioteca del usuario: una carpeta con config.json (02 §1). En esta etapa sólo lee: nunca escribe el archivo.
/// Sin vigilancia del archivo: se recarga a pedido.
/// </summary>
public sealed class Library(string folder, string backupFolder)
{
    public const string FileName = "config.json";

    private readonly Lock _gate = new();
    private volatile Loaded _loaded = new(new Config(), LibraryStatus.NotLoaded, []);

    public event Action<Config>? Changed;

    public string Folder { get; } = folder;

    /// <summary>Carpeta local para la generación anterior de config.json. La usan las migraciones y el guardado, que todavía no existen.</summary>
    public string BackupFolder { get; } = backupFolder;

    public Config Current => _loaded.Config;

    public LibraryStatus Status => _loaded.Status;

    /// <summary>Los problemas de la última carga: los de lectura o, si cargó, los del validador.</summary>
    public IReadOnlyList<Problem> Problems => _loaded.Problems;

    /// <summary>Lee config.json de nuevo. Si no se puede leer o es de un esquema más nuevo, la configuración queda vacía: no hay teclas activas.</summary>
    public IReadOnlyList<Problem> Load()
    {
        Loaded loaded;
        lock (_gate)
        {
            loaded = Read();
            _loaded = loaded;
        }

        Changed?.Invoke(loaded.Config);
        return loaded.Problems;
    }

    private static Loaded Fail(LibraryStatus status, string code, string message) =>
        new(new Config(), status, [new Problem("", code, message, Severity.Error)]);

    private Loaded Read()
    {
        if (Folder.Length == 0)
        {
            return Fail(LibraryStatus.NoLibrary, ProblemCodes.LibraryMissing, "No hay carpeta de datos elegida.");
        }

        if (!Directory.Exists(Folder))
        {
            return Fail(LibraryStatus.NoLibrary, ProblemCodes.LibraryMissing, $"No encuentro la carpeta de la biblioteca: {Folder}");
        }

        string file = Path.Combine(Folder, FileName);
        if (!File.Exists(file))
        {
            return new Loaded(new Config(), LibraryStatus.Loaded,
                [new Problem("", ProblemCodes.ConfigMissing, $"No hay config.json en {Folder}: se cargó una configuración vacía.", Severity.Warning)]);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(LibraryStatus.Unreadable, ErrorCodes.ConfigUnreadable, $"config.json no se puede abrir: {e.Message}");
        }

        // Al leer se acepta la marca de orden de bytes (02 §3, regla 8).
        int skip = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(bytes.AsSpan(skip));
        }
        catch (JsonException e)
        {
            // El análisis de sintaxis es el único que conoce la línea del archivo del usuario.
            return Fail(LibraryStatus.Unreadable, ErrorCodes.ConfigUnreadable,
                $"config.json no se puede leer: error de sintaxis en la línea {(e.LineNumber ?? 0) + 1}, columna {(e.BytePositionInLine ?? 0) + 1}.");
        }

        if (node is not JsonObject document)
        {
            return Fail(LibraryStatus.Unreadable, ErrorCodes.ConfigUnreadable, "config.json no se puede leer: tiene que ser un objeto.");
        }

        try
        {
            return Build(document);
        }
        catch (JsonException e)
        {
            return Fail(LibraryStatus.Unreadable, ErrorCodes.ConfigUnreadable,
                $"config.json no se puede leer: el valor de {e.Path} no es del tipo esperado.");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Una clave repetida en un objeto salta recién al recorrerlo.
            return Fail(LibraryStatus.Unreadable, ErrorCodes.ConfigUnreadable, $"config.json no se puede leer: {e.Message}");
        }
    }

    private Loaded Build(JsonObject document)
    {
        Result<JsonNode> upgraded = Migrations.Upgrade(document, Folder);
        if (!upgraded.Ok)
        {
            Failure failure = upgraded.Failure!;
            return new Loaded(new Config(), LibraryStatus.Newer, [new Problem(failure.Where ?? "", failure.Code, failure.Message, Severity.Error)]);
        }

        Config config = upgraded.Value!.Deserialize<Config>(Json.Options) ?? new Config();
        return new Loaded(config, LibraryStatus.Loaded, Validator.Validate(config));
    }

    private sealed record Loaded(Config Config, LibraryStatus Status, IReadOnlyList<Problem> Problems);
}
