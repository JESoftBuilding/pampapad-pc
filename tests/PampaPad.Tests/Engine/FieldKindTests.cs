using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class FieldKindTests
{
    // Los nombres de 01 §6, paso 5. Fijarlos acá es lo que permite copiarlos a contract.ts al mezclar con la etapa 6.
    [Theory]
    [InlineData(FieldKind.Text, "\"text\"")]
    [InlineData(FieldKind.Option, "\"option\"")]
    [InlineData(FieldKind.ObsScene, "\"obsScene\"")]
    [InlineData(FieldKind.ObsInput, "\"obsInput\"")]
    [InlineData(FieldKind.Json, "\"json\"")]
    [InlineData(FieldKind.Number, "\"number\"")]
    [InlineData(FieldKind.Duration, "\"duration\"")]
    [InlineData(FieldKind.Boolean, "\"boolean\"")]
    [InlineData(FieldKind.Script, "\"script\"")]
    [InlineData(FieldKind.Window, "\"window\"")]
    [InlineData(FieldKind.Page, "\"page\"")]
    public void Each_field_kind_travels_with_the_name_of_01_section_6(FieldKind kind, string json)
    {
        Assert.Equal(json, JsonSerializer.Serialize(kind, Json.Compact));
    }
}
