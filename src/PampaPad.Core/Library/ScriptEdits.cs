using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PampaPad.Core;

/// <summary>
/// Lo que escribe la pantalla Scripts en scripts/ (05 §4.3): la ficha de un script, un script importado y quitar uno, que lo mueve al
/// respaldo local. Un solo cerrojo para los tres: dos pedidos a la vez no pisan la misma carpeta. Las fallas vuelven como Result.
/// </summary>
public static partial class ScriptEdits
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Lock Gate = new();

    /// <summary>
    /// Pide los archivos al diálogo, crea scripts/&lt;id&gt;/, los copia y escribe una ficha mínima con el primero como entrada. El id sale
    /// del nombre del primero; si ya existe, suma -2, -3. Cancelar el diálogo contesta bien, con el id vacío, y no crea nada.
    /// </summary>
    public static async Task<Result<ScriptsImportResponse>> ImportAsync(ScriptsSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Library.Folder.Length == 0)
        {
            return Result<ScriptsImportResponse>.Fail(ErrorCodes.ConfigInvalid, "No hay una carpeta de biblioteca donde importar: elegí la carpeta de datos.");
        }

        IReadOnlyList<string> files = await sources.Shell.PickFiles().ConfigureAwait(false);
        if (files.Count == 0)
        {
            return new ScriptsImportResponse("");
        }

        lock (Gate)
        {
            string root = Path.Combine(sources.Library.Folder, ScriptFolder.FolderName);
            string first = Path.GetFileName(files[0]);
            string id = ProposeId(Path.GetFileNameWithoutExtension(first), taken => Directory.Exists(Path.Combine(root, taken)) || File.Exists(Path.Combine(root, taken)));
            string target = Path.Combine(root, id);
            try
            {
                Directory.CreateDirectory(target);
                foreach (string file in files)
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
                }

                var manifest = new ScriptManifest { Entry = first, Name = Path.GetFileNameWithoutExtension(first), Contract = ScriptFolder.KnownContract };
                AtomicWrite.Replace(Path.Combine(target, ScriptFolder.ManifestName), Utf8.GetBytes(JsonSerializer.Serialize(manifest, Json.Options) + "\n"));
                return new ScriptsImportResponse(id);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                TryDelete(target);
                return Result<ScriptsImportResponse>.Fail(ErrorCodes.InternalError, $"No se pudo importar {first}: {error.Message}");
            }
        }
    }

    /// <summary>
    /// Se niega si alguna acción lo corre (config_invalid, con cada ubicación) o si está corriendo. Si no, copia la carpeta al respaldo
    /// local, comprueba la copia y recién después borra el original: el respaldo está en otro disco y Directory.Move no cruza discos.
    /// </summary>
    public static Result<ScriptsDeleteResponse> Delete(ScriptsSources sources, string id)
    {
        ArgumentNullException.ThrowIfNull(sources);
        lock (Gate)
        {
            if (ScriptFolder.Inspect(sources.Library.Folder, id ?? "") is not { } found)
            {
                return Result<ScriptsDeleteResponse>.Fail(ErrorCodes.ScriptNotFound, $"No hay nada que se llame \"{id}\" en scripts\\.", "id");
            }

            if (found.Loose)
            {
                return Result<ScriptsDeleteResponse>.Fail(ErrorCodes.ScriptWithoutManifest, $"\"{id}\" es un archivo suelto, no un script: se quita a mano desde la carpeta.", "id");
            }

            IReadOnlyList<ScriptUse> uses = ScriptCatalog.UsedBy(sources.Library.Current, found.Id);
            if (uses.Count > 0)
            {
                Problem[] problems = [.. uses.Select(use => new Problem(use.Where, ProblemCodes.ScriptInUse, $"La acción \"{use.ActionName ?? use.Action}\" corre el script \"{found.Id}\".", Severity.Error))];
                return new Failure(ErrorCodes.ConfigInvalid, $"No se quita \"{found.Id}\": lo corre la acción \"{uses[0].ActionName ?? uses[0].Action}\"{(uses.Count > 1 ? $" y {uses.Count - 1} más" : "")}.", problems[0].Where) { Problems = problems };
            }

            if (sources.Runner.Running.Contains(found.Id))
            {
                return Result<ScriptsDeleteResponse>.Fail(ErrorCodes.Cancelled, $"El script \"{found.Id}\" está corriendo: esperá a que termine o cancelalo antes de quitarlo.", "id");
            }

            string stamp = sources.Clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(sources.BackupFolder, ScriptFolder.FolderName, $"{found.Id}-{stamp}");
            for (int n = 2; Directory.Exists(target) || File.Exists(target); n++)
            {
                target = Path.Combine(sources.BackupFolder, ScriptFolder.FolderName, $"{found.Id}-{stamp}-{n}");
            }

            try
            {
                CopyTree(found.Path, target);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                TryDelete(target);
                return Result<ScriptsDeleteResponse>.Fail(ErrorCodes.InternalError, $"No se pudo copiar \"{found.Id}\" al respaldo ({error.Message}): el script quedó donde estaba.", "id");
            }

            try
            {
                Directory.Delete(found.Path, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Result<ScriptsDeleteResponse>.Fail(ErrorCodes.InternalError, $"La copia de \"{found.Id}\" quedó en {target}, pero el original no se pudo borrar: {error.Message}", "id");
            }

            return new ScriptsDeleteResponse(target);
        }
    }

    /// <summary>El id de un nombre de archivo, con la forma de los ids (02 §4.2): sin tildes, en minúsculas y con guiones. Si está tomado, suma -2, -3.</summary>
    public static string ProposeId(string name, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        string plain = new([.. (name ?? "").Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
        string id = NotIdChars().Replace(plain.ToLowerInvariant(), "-").Trim('-');
        id = id.Length > Validator.MaxIdLength - 4 ? id[..(Validator.MaxIdLength - 4)].TrimEnd('-') : id;
        id = id.Length == 0 ? "script" : id;
        string candidate = id;
        for (int n = 2; taken(candidate); n++)
        {
            candidate = $"{id}-{n}";
        }

        return candidate;
    }

    // Copia la carpeta entera y comprueba que cada archivo llegó con su tamaño: si no, tira y el original no se toca.
    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, folder)));
        }

        string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: false);
        }

        foreach (string file in files)
        {
            var copy = new FileInfo(Path.Combine(target, Path.GetRelativePath(source, file)));
            if (!copy.Exists || copy.Length != new FileInfo(file).Length)
            {
                throw new IOException($"{Path.GetRelativePath(source, file)} no quedó igual en la copia");
            }
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Lo que quedó a medias no tapa la falla de verdad, que es la que se contesta.
        }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotIdChars();
}
