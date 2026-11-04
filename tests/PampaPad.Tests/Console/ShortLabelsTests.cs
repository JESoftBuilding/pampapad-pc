using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La etiqueta corta en C# contra los mismos casos que corre labels.test.ts (plan de la 9a, decisión 12).</summary>
public class ShortLabelsTests
{
    public static TheoryData<string, string[]> Shared()
    {
        string file = Path.Combine(Architecture.RepoRoot(), "ui", "src", "model", "labels.cases.json");
        var data = new TheoryData<string, string[]>();
        foreach (JsonElement item in JsonDocument.Parse(File.ReadAllText(file)).RootElement.EnumerateArray())
        {
            data.Add(item.GetProperty("label").GetString()!, [.. item.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!)]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Shared))]
    public void The_same_cases_as_the_page_give_the_same_lines(string label, string[] lines) =>
        Assert.Equal(lines, ShortLabels.For(label));

    [Fact]
    public void A_written_short_label_goes_out_in_capitals_without_accents_and_cut_to_six()
    {
        var key = new Key { Label = "Cámara", ShortLabel = ["ñandú", "demasiado"] };

        Assert.Equal(["NANDU", "DEMASI"], ShortLabels.Of(key));
    }

    [Fact]
    public void Without_a_short_label_the_lines_come_from_the_label_and_a_key_without_label_has_none()
    {
        Assert.Equal(["CAMARA", "ZOOM"], ShortLabels.Of(new Key { Label = "Cámara (zoom)" }));
        Assert.Equal([""], ShortLabels.Of(new Key { ShortLabel = [] }));
    }

    [Fact]
    public void Screen_text_is_ascii_in_capitals_and_keeps_signs_the_font_draws()
    {
        Assert.Equal("FALLO P2: CAMARA", ShortLabels.Screen("Falló P2: Cámara"));
        Assert.Equal("NANDU", ShortLabels.Screen("Ñandú\u0001"));
    }
}
