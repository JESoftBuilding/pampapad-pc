using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Lo que el servidor de obs-websocket tiene guardado. ToString no muestra la clave: un registro o un
/// mensaje armado con este valor no la puede filtrar.
/// </summary>
public sealed record ObsServerSettings(int Port, string? Password, bool Enabled, bool AuthRequired)
{
    public override string ToString() =>
        $"ObsServerSettings {{ Port = {Port}, Enabled = {Enabled}, AuthRequired = {AuthRequired} }}";
}

/// <summary>Un atajo de OBS como está en el basic.ini del perfil: "OBSBasic.PauseRecording" con OBS_KEY_F10 y sus modificadores.</summary>
public sealed record ObsHotkey(string Name, string Key, bool Control, bool Shift, bool Alt, bool Command);

/// <summary>
/// Lee, sin escribir nunca, los archivos del propio OBS (spec 03 §2 y §4). No guarda nada en memoria:
/// cada llamada vuelve al disco, porque OBS puede haber cambiado el archivo.
/// </summary>
public sealed class ObsFiles(string obsFolder)
{
    public const int DefaultPort = 4455;

    /// <summary>%APPDATA%\obs-studio.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");

    private string ServerFile => Path.Combine(obsFolder, "plugin_config", "obs-websocket", "config.json");

    private string ProfilesFolder => Path.Combine(obsFolder, "basic", "profiles");

    /// <summary>
    /// El puerto, la clave y los dos indicadores del servidor. Un puerto ausente o que no sirve (no es un entero entre
    /// 1 y 65535) da el de fábrica: con uno fuera de rango, armar la dirección tiraría una excepción en cada intento.
    /// Una clave que no es texto válido (UTF-8 mal formado, un escape suelto) hace ilegible el archivo, no tira.
    /// </summary>
    public Result<ObsServerSettings> ReadServer()
    {
        var bytes = ReadBytes(ServerFile);
        if (!bytes.Ok)
        {
            return bytes.Failure;
        }

        if (!TryParse(WithoutBom(bytes.Value!), out var json))
        {
            return Unreadable<ObsServerSettings>(ServerFile);
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Unreadable<ObsServerSettings>(ServerFile);
            }

            var port = root.TryGetProperty("server_port", out var number)
                && number.ValueKind == JsonValueKind.Number
                && number.TryGetInt32(out var value)
                && value is >= 1 and <= 65535
                    ? value
                    : DefaultPort;
            string? password = null;
            if (root.TryGetProperty("server_password", out var text) && text.ValueKind == JsonValueKind.String
                && !TryGetText(text, out password))
            {
                return Unreadable<ObsServerSettings>(ServerFile);
            }

            return Result<ObsServerSettings>.Success(new ObsServerSettings(port, password, On(root, "server_enabled"), On(root, "auth_required")));
        }
    }

    /// <summary>Por defecto, ws://127.0.0.1:&lt;server_port&gt; (spec 03 §2). Si el archivo no se pudo leer, el puerto de fábrica.</summary>
    public Uri Address()
    {
        var server = ReadServer();
        return new Uri($"ws://127.0.0.1:{(server.Ok ? server.Value!.Port : DefaultPort)}");
    }

    /// <summary>La clave del archivo de OBS, o null si no se pudo leer.</summary>
    public string? Password()
    {
        var server = ReadServer();
        return server.Ok ? server.Value!.Password : null;
    }

    /// <summary>
    /// Los atajos generales del perfil activo: la sección [Hotkeys] del basic.ini cuya [General] Name es ese perfil.
    /// La carpeta no lleva el nombre tal cual ("Sin Título" está en Sin_Título): por eso se abren todas.
    /// </summary>
    public Result<IReadOnlyList<ObsHotkey>> ReadProfileHotkeys(string profileName)
    {
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(ProfilesFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable<IReadOnlyList<ObsHotkey>>(ProfilesFolder);
        }

        foreach (var folder in folders)
        {
            var bytes = ReadBytes(Path.Combine(folder, "basic.ini"));
            if (!bytes.Ok)
            {
                continue;
            }

            var ini = ParseIni(Encoding.UTF8.GetString(WithoutBom(bytes.Value!).Span));
            var name = ini.GetValueOrDefault("General")?.FirstOrDefault(pair => pair.Key == "Name").Value;
            if (name != profileName)
            {
                continue;
            }

            return Result<IReadOnlyList<ObsHotkey>>.Success(Hotkeys(ini.GetValueOrDefault("Hotkeys")));
        }

        return new Failure(ErrorCodes.ConfigUnreadable, $"OBS no tiene un perfil llamado \"{profileName}\"", ProfilesFolder);
    }

    private static List<ObsHotkey> Hotkeys(List<KeyValuePair<string, string>>? section)
    {
        var found = new List<ObsHotkey>();
        if (section is null)
        {
            return found;
        }

        foreach (var (name, value) in section)
        {
            if (!TryParse(Encoding.UTF8.GetBytes(value), out var json))
            {
                continue;
            }

            using (json)
            {
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("bindings", out var bindings)
                    || bindings.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var binding in bindings.EnumerateArray())
                {
                    if (binding.ValueKind != JsonValueKind.Object
                        || !binding.TryGetProperty("key", out var key)
                        || key.ValueKind != JsonValueKind.String
                        || !TryGetText(key, out var keyName))
                    {
                        continue;
                    }

                    found.Add(new ObsHotkey(name, keyName, On(binding, "control"), On(binding, "shift"), On(binding, "alt"), On(binding, "command")));
                }
            }
        }

        return found;
    }

    // Secciones de un .ini, cada una con sus pares clave y valor en el orden del archivo, sin interpretar los valores.
    private static Dictionary<string, List<KeyValuePair<string, string>>> ParseIni(string text)
    {
        var sections = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        List<KeyValuePair<string, string>>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                current = [];
                sections[line[1..^1]] = current;
                continue;
            }

            var cut = line.IndexOf('=');
            if (current is not null && cut > 0)
            {
                current.Add(new KeyValuePair<string, string>(line[..cut], line[(cut + 1)..]));
            }
        }

        return sections;
    }

    private static bool On(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    // JsonDocument.Parse no valida el texto de las cadenas (UTF-8 mal formado, escapes sueltos): GetString tira recién
    // acá. Un archivo editado a mano y guardado en ANSI llega hasta este punto.
    private static bool TryGetText(JsonElement element, [NotNullWhen(true)] out string? text)
    {
        try
        {
            text = element.GetString();
            return text is not null;
        }
        catch (InvalidOperationException)
        {
            text = null;
            return false;
        }
    }

    private static bool TryParse(ReadOnlyMemory<byte> utf8, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(utf8);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static Result<byte[]> ReadBytes(string file)
    {
        try
        {
            // OBS puede estar escribiéndolo: se abre sin pedir exclusividad.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return Result<byte[]>.Success(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable<byte[]>(file);
        }
    }

    private static Result<T> Unreadable<T>(string file) =>
        Result<T>.Fail(ErrorCodes.ConfigUnreadable, "No se pudo leer un archivo de OBS", file);

    private static ReadOnlyMemory<byte> WithoutBom(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hasBom ? bytes.AsMemory(3) : bytes.AsMemory();
    }
}
