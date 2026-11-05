namespace PampaPad.Core;

/// <summary>Un texto de la pantalla y su tono: 0 normal, 1 ámbar, 2 rojo (WireMessages.h:115).</summary>
public sealed record ConsoleText(string Text, int Tone = 0);

/// <summary>
/// Los textos del aviso grave a la vista, destinos 2 a 5 de ScreenText, con Number como referencia: la consola los dibuja sólo mientras
/// el byte 15 sea ese número (ConsoleModel.h). El tono del título da el color del cuerpo; los otros van con tono 0, como en consola.py.
/// </summary>
public sealed record ConsoleAlert(int Number, ConsoleText Title, ConsoleText Line1, ConsoleText Line2, ConsoleText Hint)
{
    public const int FirstTarget = 2;
    public const int Targets = 4;

    public ConsoleText Text(int target) => target switch
    {
        2 => Title,
        3 => Line1,
        4 => Line2,
        5 => Hint,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Los textos de un aviso van a los destinos 2 a 5."),
    };
}

/// <summary>
/// Todo lo que la consola tiene que mostrar en un instante: la banda, las 12 teclas de la página que rige (la 1 primero), la línea de
/// escena, el renglón chico, los LED de la tira y, si hay, los textos del aviso grave. Lo arma la proyección y lo manda ConsoleFeed.
/// Las teclas que falten van vacías.
/// </summary>
public sealed record ConsoleFrame(
    ScreenFrame Screen,
    IReadOnlyList<KeyFace> Keys,
    ConsoleText SceneLine,
    ConsoleText SmallLine,
    IReadOnlyList<LedColor> Strip,
    ConsoleAlert? Alert = null)
{
    public KeyFace Key(int index) => index < Keys.Count ? Keys[index] : KeyFace.Empty;
}
