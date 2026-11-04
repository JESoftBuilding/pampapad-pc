namespace PampaPad.Core;

/// <summary>Un texto de la pantalla y su tono: 0 normal, 1 ámbar, 2 rojo (WireMessages.h:115).</summary>
public sealed record ConsoleText(string Text, int Tone = 0);

/// <summary>
/// Todo lo que la consola tiene que mostrar en un instante: la banda, las 12 teclas de la página que rige (la 1 primero), la línea de
/// escena, el renglón chico y los LED de la tira. Lo arma la proyección (E9-5) y lo manda ConsoleFeed. Las teclas que falten van vacías.
/// </summary>
public sealed record ConsoleFrame(
    ScreenFrame Screen,
    IReadOnlyList<KeyFace> Keys,
    ConsoleText SceneLine,
    ConsoleText SmallLine,
    IReadOnlyList<LedColor> Strip)
{
    public KeyFace Key(int index) => index < Keys.Count ? Keys[index] : KeyFace.Empty;
}
