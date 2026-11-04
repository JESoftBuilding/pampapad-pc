using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PampaPad.Core;

/// <summary>
/// La etiqueta corta de una tecla en la consola (04 §9; plan de la 9a, decisión 12). Es la misma regla que ui/src/model/labels.ts, que la
/// muestra en Teclas: ShortLabelsTests y labels.test.ts corren los mismos casos (labels.cases.json). Si cambia una, cambia la otra.
/// </summary>
public static partial class ShortLabels
{
    public const int LineLength = 6;
    public const int MaxLines = 2;

    /// <summary>Las líneas de la tecla: su shortLabel si lo tiene, en lo que la consola dibuja; si no, las que salen de label.</summary>
    public static IReadOnlyList<string> Of(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.ShortLabel is { } written)
        {
            List<string> lines = [.. written.Take(MaxLines).Select(line => Cut(Screen(line ?? "").Trim(), LineLength))];
            return lines.Count == 0 ? [""] : lines;
        }

        return For(key.Label ?? "");
    }

    /// <summary>Las líneas que salen de una etiqueta: la cuenta de shortLabelFor en labels.ts.</summary>
    public static IReadOnlyList<string> For(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        Match match = Parenthesis().Match(label);
        string main = Plain(match.Success ? match.Groups[1].Value : label);
        var lines = new List<string>();
        Pack(main, lines);
        if (match.Success && Plain(match.Groups[2].Value) is { Length: > 0 } inner)
        {
            // Lo de entre paréntesis va siempre a la segunda línea, aunque entre en la primera (Mic (on) da MIC / ON).
            var second = new List<string>();
            Pack(inner, second);
            return [lines.Count > 0 ? lines[0] : "", second.Count > 0 ? second[0] : ""];
        }

        return lines.Count == 0 ? [""] : lines;
    }

    /// <summary>Lo que dibuja la letra de la consola: mayúsculas, sin acentos y sólo ASCII imprimible (04 §2). La ñ queda N.</summary>
    public static string Screen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var plain = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD).ToUpperInvariant())
        {
            if (c is >= ' ' and <= '~')
            {
                plain.Append(c);
            }
        }

        return plain.ToString();
    }

    public static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    // El plain de labels.ts: además de Screen, sólo letras, números, espacio y + - . /.
    private static string Plain(string text) => Unwanted().Replace(Screen(text), "").Trim();

    // Junta palabras en una línea mientras entren en seis con su espacio; una de más de seis se corta a cinco y un punto (PANTA.).
    private static void Pack(string text, List<string> lines)
    {
        foreach (string raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw.Length > LineLength ? string.Concat(raw.AsSpan(0, LineLength - 1), ".") : raw;
            int last = lines.Count - 1;
            if (last >= 0 && lines[last].Length > 0 && lines[last].Length + 1 + word.Length <= LineLength)
            {
                lines[last] = string.Create(CultureInfo.InvariantCulture, $"{lines[last]} {word}");
            }
            else if (lines.Count < MaxLines)
            {
                lines.Add(word);
            }
            else
            {
                return;
            }
        }
    }

    [GeneratedRegex(@"^([^()]*)\(([^)]*)\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"[^A-Z0-9 +\-./]")]
    private static partial Regex Unwanted();
}
