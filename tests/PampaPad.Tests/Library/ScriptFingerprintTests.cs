using System.Security.Cryptography;
using System.Text;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La huella de un script y la codificación de sus archivos de texto (plan de la etapa MCP, decisión 2; 02 §9, punto 9).</summary>
public sealed class ScriptFingerprintTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // Revisión de la tanda 2 (should): la forma entera de la huella, para que un cambio sin querer no pase callado. Un .txt no cuenta: un
    // script puede escribir archivos en su carpeta sin cambiarle la huella a la ventana.
    [Fact]
    public void The_fingerprint_is_the_text_files_and_the_manifest_in_order_without_capitals_each_with_its_name_and_length()
    {
        string folder = _temp.Combine("hola");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "script.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "B.ps1"), "de");
        File.WriteAllText(Path.Combine(folder, "a.cmd"), "abc");
        File.WriteAllText(Path.Combine(folder, "salida.txt"), "lo que dejó el script");

        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes("a.cmd\n3\nabcB.ps1\n2\ndescript.json\n2\n{}"));

        Assert.Equal(Convert.ToHexStringLower(expected), ScriptFingerprint.Of(folder));
    }

    [Fact]
    public void A_ps1_goes_with_its_utf8_mark_a_py_without_and_a_cmd_with_crlf()
    {
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'x'], ScriptFingerprint.Encode("a.ps1", "x"));
        Assert.Equal("x"u8.ToArray(), ScriptFingerprint.Encode("a.py", "x"));
        Assert.Equal(ScriptLaunch.Oem.GetBytes("a\r\nb\r\nc"), ScriptFingerprint.Encode("a.CMD", "a\nb\r\nc"));
    }

    [Fact]
    public void Reading_a_utf8_mark_wins_over_the_table()
    {
        Assert.Equal("canción", ScriptFingerprint.Decode("a.cmd", [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("canción")]));
    }
}
