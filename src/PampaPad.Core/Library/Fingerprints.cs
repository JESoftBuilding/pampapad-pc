using System.Security.Cryptography;

namespace PampaPad.Core;

/// <summary>La huella de config.json: el SHA-256 de sus bytes, en hexadecimal y en minúsculas (02 §3, regla 11; el algoritmo es Propuesto).</summary>
public static class Fingerprints
{
    public static string Of(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
