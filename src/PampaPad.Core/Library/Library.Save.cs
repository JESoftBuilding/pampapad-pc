using System.Text;
using System.Text.Json;

namespace PampaPad.Core;

// Escribir config.json (02 §3, reglas 8 a 11 y 02 §10): lo que guarda la ventana y, más adelante, el MCP. Va aparte de Library.cs por el
// tope de 300 líneas por archivo (01 §1).
public sealed partial class Library
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Guarda la configuración entera si el disco todavía tiene la huella con la que se leyó. Ver el otro SaveAsync.</summary>
    public Task<Result> SaveAsync(Config next, string expectedFingerprint, CancellationToken ct) =>
        SaveAsync(next, expectedFingerprint, [], ct);

    /// <summary>
    /// Guarda la configuración entera en forma canónica (UTF-8 sin marca, sangría de dos, \n; 02 §3, regla 8). En este orden, y sin escribir
    /// nada si falla uno: la biblioteca tiene que poder guardarse (ni ilegible, ni más nueva, ni sin carpeta); el disco tiene que tener
    /// la huella que se mandó (config_changed_on_disk); y no puede haber un problema de gravedad error, ni en el archivo (Validator) ni
    /// en los que ya encontró quien llama, con el catálogo de pasos (extra): config_invalid, con todos los problemas. Después copia la
    /// generación anterior a la carpeta local (una sola), escribe de forma atómica, vuelve a leer lo escrito (Current es lo que daría una
    /// recarga) y avisa con Changed. Dos guardados superpuestos salen en orden: el segundo ya no encuentra la huella que mandó.
    /// </summary>
    public async Task<Result> SaveAsync(Config next, string expectedFingerprint, IReadOnlyList<Problem> extra, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(extra);
        try
        {
            // Fuera del hilo que pide: el disco puede tardar (un antivirus, una carpeta de red).
            return await Task.Run(() => Save(next, expectedFingerprint, extra), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result.Fail(ErrorCodes.Cancelled, "Se canceló el guardado de config.json.", FileName);
        }
    }

    private Result Save(Config next, string expectedFingerprint, IReadOnlyList<Problem> extra)
    {
        Loaded saved;
        lock (_gate)
        {
            if (Refusal() is { } refused)
            {
                return refused;
            }

            string file = Path.Combine(Folder, FileName);
            byte[]? onDisk;
            try
            {
                onDisk = File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Result.Fail(ErrorCodes.ConfigUnreadable, $"config.json no se puede abrir: {e.Message}", FileName);
            }

            if ((onDisk is null ? "" : Fingerprints.Of(onDisk)) != expectedFingerprint)
            {
                return Result.Fail(
                    ErrorCodes.ConfigChangedOnDisk,
                    "config.json cambió en el disco desde que se leyó (un git pull o una edición a mano): recargá la biblioteca antes de guardar.",
                    FileName);
            }

            IReadOnlyList<Problem> problems = [.. Validator.Validate(next).Concat(extra).DistinctBy(problem => (problem.Where, problem.Code))];
            if (problems.FirstOrDefault(problem => problem.Severity == Severity.Error) is { } first)
            {
                return new Failure(ErrorCodes.ConfigInvalid, first.Message, first.Where) { Problems = problems };
            }

            byte[] bytes = Utf8.GetBytes(JsonSerializer.Serialize(next, Json.Options) + "\n");
            if (onDisk is not null && bytes.AsSpan().SequenceEqual(onDisk))
            {
                return Result.Success;
            }

            try
            {
                if (onDisk is not null)
                {
                    Directory.CreateDirectory(BackupFolder);
                    File.Copy(file, Path.Combine(BackupFolder, FileName), overwrite: true);
                }

                AtomicWrite.Replace(file, bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Result.Fail(ErrorCodes.InternalError, $"No se pudo guardar config.json: {e.Message}", FileName);
            }

            saved = Read();
            _loaded = saved;
        }

        Changed?.Invoke(saved.Config);
        return Result.Success;
    }

    // Una biblioteca que no se pudo leer o es más nueva no se pisa (02 §1 y §3, regla 7): la ventana no deja guardar.
    private Failure? Refusal() => _loaded.Status switch
    {
        LibraryStatus.NoLibrary or LibraryStatus.NotLoaded => new Failure(
            ErrorCodes.ConfigInvalid, "No hay una carpeta de biblioteca cargada donde guardar: elegí la carpeta de datos o recargá la biblioteca.", Folder.Length == 0 ? null : Folder),
        LibraryStatus.Unreadable => new Failure(
            ErrorCodes.ConfigUnreadable, "config.json no se puede leer y por eso no se pisa: arreglalo a mano y recargá la biblioteca antes de guardar.", FileName),
        LibraryStatus.Newer => new Failure(
            ErrorCodes.ConfigNewer, "config.json es de una versión más nueva que este programa y no se pisa: actualizá PampaPad.", FileName),
        _ => null,
    };
}
