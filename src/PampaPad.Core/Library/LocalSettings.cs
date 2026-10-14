using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>Lo que guarda local.json. Lo que esta versión no conoce se conserva y se vuelve a escribir igual (02 §2).</summary>
public sealed record LocalData
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>La versión del programa que Emmanuel eligió no instalar (05 §8).</summary>
    public string? SkippedVersion { get; init; }

    /// <summary>Si esta copia toma los atajos globales. Sin valor, la instalada sí y la de desarrollo no (05 §10).</summary>
    public bool? HotkeysEnabled { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Unknown { get; init; } = [];
}

/// <summary>Los ajustes de esta PC: local.json, en la carpeta local (02 §2). Lee tolerante y nunca pisa un archivo que no pudo leer.</summary>
public sealed class LocalSettings(string path, Log log)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalData _current = new();
    private Failure? _unreadable;

    public LocalData Current => Volatile.Read(ref _current);

    /// <summary>Lee el archivo. Si falla, deja los valores por defecto en memoria, lo anota y bloquea los guardados.</summary>
    public Result Load()
    {
        _unreadable = null;
        if (!File.Exists(path))
        {
            Volatile.Write(ref _current, new LocalData());
            return Result.Success;
        }

        try
        {
            // ReadAllText acepta la marca de orden de bytes; el lector de JSON, no.
            var data = JsonSerializer.Deserialize<LocalData>(File.ReadAllText(path), Json.Options) ?? new LocalData();
            if (data.Version > LocalData.CurrentVersion)
            {
                return Refuse(
                    ErrorCodes.ConfigNewer,
                    $"local.json es de la versión {data.Version} y este programa conoce hasta la {LocalData.CurrentVersion}");
            }

            Volatile.Write(ref _current, data);
            return Result.Success;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Refuse(ErrorCodes.ConfigUnreadable, "local.json no se puede leer: " + ex.Message);
        }
    }

    /// <summary>Aplica el cambio y lo escribe: temporal en la misma carpeta y reemplazo en una sola operación.</summary>
    public async Task<Result> SaveAsync(Func<LocalData, LocalData> change, CancellationToken ct)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_unreadable is not null)
            {
                return _unreadable;
            }

            var next = change(Current);
            var text = JsonSerializer.Serialize(next, Json.Options) + "\n";
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, text, Utf8, ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            Volatile.Write(ref _current, next);
            return Result.Success;
        }
        catch (OperationCanceledException)
        {
            return Result.Fail(ErrorCodes.Cancelled, "Se canceló el guardado de local.json", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("settings", "No se pudo guardar local.json: " + ex.Message, ErrorCodes.InternalError);
            return Result.Fail(ErrorCodes.InternalError, "No se pudo guardar local.json: " + ex.Message, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Result Refuse(string code, string message)
    {
        var failure = new Failure(code, message, path);
        _unreadable = failure;
        Volatile.Write(ref _current, new LocalData());
        log.Error("settings", message, code);
        return failure;
    }
}
