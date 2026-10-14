namespace PampaPad.Core;

/// <summary>
/// Lo que hizo un control (02 §6). Press, Release y Turn llegan de la consola; Tap y Hold se reconocen en la PC.
/// Los atajos de teclado entregan Tap directo: Windows no avisa cuando la tecla se suelta.
/// </summary>
public enum Gesture
{
    Press,
    Release,
    Turn,
    Tap,
    Hold,
}
