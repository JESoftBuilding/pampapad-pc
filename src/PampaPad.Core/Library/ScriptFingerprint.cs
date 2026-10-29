using System.Security.Cryptography;
using System.Text;

namespace PampaPad.Core;

/// <summary>
/// La huella de un script (plan de la etapa MCP, decisión 2): SHA-256 sobre script.json y sus archivos de texto, ordenados por nombre,
/// cada uno con su nombre y su largo adelante. Así guardar no pisa sin aviso un .cmd que cambió en el disco después de leerlo. Una
/// carpeta que no existe da la huella vacía, la que se manda para crear. También la tabla de qué archivos son texto y cómo se codifican.
/// </summary>
public static class ScriptFingerprint
{
    /// <summary>El tope de un archivo de texto que viaja por el puente (05 §7, sin medir).</summary>
    public const int MaxTextBytes = 256 * 1024;

    /// <summary>Los archivos que Claude puede leer y escribir: los del contrato 1 que no son binarios (02 §9).</summary>
    public static IReadOnlyList<string> TextExtensions { get; } = [".cmd", ".ps1", ".py"];

    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static bool IsText(string name) => TextExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// La codificación con que se guarda cada extensión (02 §9, punto 9): un .cmd en la página OEM, un .ps1 en UTF-8 con marca y un .py en
    /// UTF-8. Al leer, una marca de UTF-8 manda sobre la tabla.
    /// </summary>
    public static Encoding EncodingOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".cmd" => ScriptLaunch.Oem,
        ".ps1" => Utf8Bom,
        _ => ScriptLaunch.Utf8,
    };

    public static string Decode(string name, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            ? ScriptLaunch.Utf8.GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length)
            : EncodingOf(name).GetString(bytes);
    }

    public static byte[] Encode(string name, string text)
    {
        Encoding encoding = EncodingOf(name);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
    }

    public static string Of(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return "";
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles()
            .Where(file => file.Name.Equals(ScriptFolder.ManifestName, StringComparison.OrdinalIgnoreCase) || IsText(file.Name))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes = File.ReadAllBytes(file.FullName);
            hash.AppendData(Encoding.UTF8.GetBytes($"{file.Name}\n{bytes.Length}\n"));
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
