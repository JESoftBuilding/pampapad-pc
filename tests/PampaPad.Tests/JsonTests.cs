using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class JsonTests
{
    public enum Sample
    {
        FirstValue,
        SecondValue,
    }

    public sealed record Doc(string Name, string? Missing, Sample Kind, string Hotkey);

    private static readonly Doc Example = new("Cámara", null, Sample.SecondValue, "Ctrl+Shift+Alt+F8");

    [Fact]
    public void Options_write_camel_case_with_two_spaces_and_line_feeds_and_skip_nulls()
    {
        var text = JsonSerializer.Serialize(Example, Json.Options);

        Assert.Equal(
            "{\n  \"name\": \"Cámara\",\n  \"kind\": \"secondValue\",\n  \"hotkey\": \"Ctrl+Shift+Alt+F8\"\n}",
            text);
    }

    [Fact]
    public void Compact_writes_a_single_line()
    {
        var text = JsonSerializer.Serialize(Example, Json.Compact);

        Assert.Equal("{\"name\":\"Cámara\",\"kind\":\"secondValue\",\"hotkey\":\"Ctrl+Shift+Alt+F8\"}", text);
    }

    [Fact]
    public void Reading_takes_enums_as_camel_case_text_and_tolerates_missing_fields()
    {
        var doc = JsonSerializer.Deserialize<Doc>("{ \"name\": \"Banco\", \"kind\": \"firstValue\", \"hotkey\": \"F9\" }", Json.Options);

        Assert.Equal(new Doc("Banco", null, Sample.FirstValue, "F9"), doc);
    }
}
