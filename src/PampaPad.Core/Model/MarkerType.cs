namespace PampaPad.Core;

/// <summary>
/// Los tipos del contrato de marcas del canal (Decidido 3). Las palabras no se traducen: las leen los scripts de corte.
/// Un tipo nuevo se agrega primero en la nota de decisión del canal y después acá.
/// </summary>
public enum MarkerType
{
    Short,
    Inicio,
    Fin,
    Sacar,
    Largo,
}

public static class MarkerTypes
{
    private static readonly string[] Words = ["short", "inicio", "fin", "sacar", "largo"];

    public static IReadOnlyList<string> All => Words;

    public static string Text(MarkerType type) => Words[(int)type];

    /// <summary>Exacto y en minúsculas: "Short" no es un tipo.</summary>
    public static bool TryParse(string? text, out MarkerType type)
    {
        int index = text is null ? -1 : Array.IndexOf(Words, text);
        type = index < 0 ? default : (MarkerType)index;
        return index >= 0;
    }
}
