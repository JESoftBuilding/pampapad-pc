using System.Text.Json;

namespace PampaPad.Core;

// scripts.write (05 §4.3 y §11): la ficha y, desde el MCP, los archivos de texto del script (plan de la etapa MCP, E-MCP-7). Va aparte
// de ScriptEdits.cs por el tope de 300 líneas.
public static partial class ScriptEdits
{
    /// <summary>
    /// Escribe los archivos de texto que vengan y al final script.json, cada uno por AtomicWrite. Sin files, sólo la ficha y la carpeta
    /// tiene que existir; con files, si no existe se crea, con la huella esperada vacía. Todo lo que puede rechazarse se mira antes de
    /// escribir: un rechazo deja la carpeta como estaba. Nada se escribe fuera de scripts/&lt;id&gt;/. Lo que el programa no conoce de la
    /// ficha y está en el disco queda. entry tiene que ser un archivo de la carpeta después de escribir, y timeoutSec ir de 1 a 59: por
    /// debajo del tope de 60 s de una acción (decisión 35).
    /// </summary>
    public static Result<ScriptsWriteResponse> Write(ScriptsSources sources, ScriptsWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(request);
        string id = request.Id ?? "";
        if (request.Manifest is not { } manifest)
        {
            return Result<ScriptsWriteResponse>.Fail(ErrorCodes.InvalidData, "scripts.write lleva la ficha en manifest.", "manifest");
        }

        if (!ScriptFolder.IsId(id))
        {
            return Result<ScriptsWriteResponse>.Fail(ErrorCodes.InvalidData, $"\"{id}\" no es un id de script: van minúsculas, números y guiones, hasta {Validator.MaxIdLength} caracteres.", "id");
        }

        if (sources.Library.Folder.Length == 0)
        {
            return Result<ScriptsWriteResponse>.Fail(ErrorCodes.ConfigInvalid, "No hay una carpeta de biblioteca donde escribir: elegí la carpeta de datos.");
        }

        IReadOnlyList<ScriptText> files = request.Files ?? [];
        lock (Gate)
        {
            ScriptFound? found = ScriptFolder.Inspect(sources.Library.Folder, id);
            if (found is { Loose: true } || (found is null && request.Files is null))
            {
                return Result<ScriptsWriteResponse>.Fail(ErrorCodes.ScriptNotFound, $"No existe el script \"{id}\": falta la carpeta scripts\\{id}.", "id");
            }

            string folder = found?.Path ?? Path.Combine(sources.Library.Folder, ScriptFolder.FolderName, id);
            if (CheckFiles(folder, id, files) is { } invalid)
            {
                return invalid;
            }

            if (manifest.Entry is not { Length: > 0 } entry || ScriptFolder.ResolveEntry(folder, id, entry, out string path) is not null
                || !(File.Exists(path) || files.Any(file => file.Name.Equals(entry, StringComparison.OrdinalIgnoreCase))))
            {
                return Result<ScriptsWriteResponse>.Fail(ErrorCodes.InvalidData, $"entry va el nombre de un archivo de scripts\\{id}, y \"{manifest.Entry}\" no está.", "manifest.entry");
            }

            if (manifest.TimeoutSec is int seconds && (seconds < 1 || seconds >= Executor.ActionTimeout.TotalSeconds))
            {
                return Result<ScriptsWriteResponse>.Fail(
                    ErrorCodes.InvalidData, $"timeoutSec va en segundos, de 1 a {Executor.ActionTimeout.TotalSeconds - 1:0}: con {Executor.ActionTimeout.TotalSeconds:0} vence antes el tope de la acción.", "manifest.timeoutSec");
            }

            try
            {
                // La huella de lo que está en el disco ahora, con la ficha y los archivos de texto (decisión 2).
                if (ScriptFingerprint.Of(folder) != request.ExpectedFingerprint)
                {
                    return Result<ScriptsWriteResponse>.Fail(
                        ErrorCodes.ConfigChangedOnDisk, $"\"{id}\" cambió en el disco desde que se leyó: volvé a abrirlo antes de guardar.", "expectedFingerprint");
                }

                Dictionary<string, JsonElement> extra = new(found?.Manifest?.Extra ?? []);
                foreach ((string key, JsonElement value) in manifest.Extra)
                {
                    extra[key] = value;
                }

                byte[] bytes = Utf8.GetBytes(JsonSerializer.Serialize(manifest with { Extra = extra }, Json.Options) + "\n");
                WriteAll(folder, files, bytes, creating: found is null);
                return new ScriptsWriteResponse(ScriptFingerprint.Of(folder));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Result<ScriptsWriteResponse>.Fail(ErrorCodes.InternalError, $"No se pudo guardar \"{id}\": {error.Message}", "id");
            }
        }
    }

    // Cada nombre es sólo el de un archivo de texto de la carpeta, sin repetir, y cada texto entra en el tope. La última defensa es la de
    // entry: la ruta resuelta tiene que quedar dentro de la carpeta (un nombre de dispositivo pasa las de antes).
    private static Failure? CheckFiles(string folder, string id, IReadOnlyList<ScriptText> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Count; i++)
        {
            string at = $"files[{i}]";
            if (files[i] is not { Name: { Length: > 0 } name, Text: { } text })
            {
                return new Failure(ErrorCodes.InvalidData, $"{at} lleva name y text.", at);
            }

            if (Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/', StringComparison.Ordinal)
                || !ScriptFingerprint.IsText(name) || ScriptFolder.ResolveEntry(folder, id, name, out _) is not null)
            {
                return new Failure(ErrorCodes.InvalidData, $"\"{name}\" no sirve: va sólo el nombre de un archivo .cmd, .ps1 o .py de scripts\\{id}.", at + ".name");
            }

            if (!names.Add(name))
            {
                return new Failure(ErrorCodes.InvalidData, $"\"{name}\" está dos veces en files.", at + ".name");
            }

            if (ScriptFingerprint.Encode(name, text).Length > ScriptFingerprint.MaxTextBytes)
            {
                return new Failure(ErrorCodes.InvalidData, $"\"{name}\" pasa de {ScriptFingerprint.MaxTextBytes / 1024} KB.", at + ".text");
            }
        }

        return null;
    }

    // Primero los archivos y al final la ficha. Si el disco falla a mitad, lo escrito vuelve atrás: la carpeta nueva se borra y en una
    // que ya estaba cada archivo vuelve a lo que tenía.
    private static void WriteAll(string folder, IReadOnlyList<ScriptText> files, byte[] manifest, bool creating)
    {
        var previous = new List<(string Path, byte[]? Bytes)>();
        try
        {
            Directory.CreateDirectory(folder);
            foreach (ScriptText file in files)
            {
                Put(Path.Combine(folder, file.Name), ScriptFingerprint.Encode(file.Name, file.Text), previous);
            }

            Put(Path.Combine(folder, ScriptFolder.ManifestName), manifest, previous);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (creating)
            {
                TryDelete(folder);
            }
            else
            {
                Restore(previous);
            }

            throw;
        }
    }

    private static void Put(string path, byte[] bytes, List<(string Path, byte[]? Bytes)> previous)
    {
        previous.Add((path, File.Exists(path) ? File.ReadAllBytes(path) : null));
        AtomicWrite.Replace(path, bytes);
    }

    private static void Restore(List<(string Path, byte[]? Bytes)> previous)
    {
        foreach ((string path, byte[]? bytes) in Enumerable.Reverse(previous))
        {
            try
            {
                if (bytes is null)
                {
                    File.Delete(path);
                }
                else
                {
                    AtomicWrite.Replace(path, bytes);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Lo que no se pudo volver atrás no tapa la falla de verdad, que es la que se contesta.
            }
        }
    }
}
