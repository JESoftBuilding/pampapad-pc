namespace PampaPad.Core;

/// <summary>
/// El aviso grave a la vista (04 §5; plan de la 9b, E9b-3). Number es el del byte 15 de ScreenState; Tone, el del título: 1 ámbar o 2 rojo.
/// Title, Line1, Line2 y Hint son los destinos 2 a 5 de ScreenText, en ASCII y sin pasar de 8, 17, 17 y 25 letras; uno vacío no se dibuja.
/// Since es cuándo apareció. Light y Sound son sus canales de console.alerts, fijados al aparecer: el zumbador no mira la toma (decisión 9).
/// </summary>
public sealed record GraveAlert(
    string Id,
    int Number,
    int Tone,
    string Title,
    string Line1,
    string Line2,
    string Hint,
    DateTimeOffset Since,
    bool Light,
    bool Sound);
