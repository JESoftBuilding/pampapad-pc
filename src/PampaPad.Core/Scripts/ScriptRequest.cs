namespace PampaPad.Core;

/// <summary>
/// Lo que se le pide a ScriptRunner. Wait y Timeout pisan los de la ficha (02 §5). OnLine recibe cada renglón de la salida, también los
/// de la salida de errores, que llegan con ErrorMark adelante; OnFinished, cómo terminó la corrida, en todos los caminos y una sola vez
/// (sin esperar, cuando el script sale de verdad).
/// </summary>
public sealed record ScriptRequest(
    string Id, bool? Wait, TimeSpan? Timeout, Invocation Invocation, System.Action<string>? OnLine = null, System.Action<ScriptResult>? OnFinished = null)
{
    /// <summary>
    /// Lo que marca un renglón de la salida de errores en OnLine. shortcut: un script que escribe un NUL al principio de un renglón se
    /// leería como error; si hiciera falta, OnLine pasaría a recibir la salida aparte.
    /// </summary>
    public const string ErrorMark = "\0";
}

/// <summary>
/// Cómo terminó un script. Sin esperar, ExitCode y Message van vacíos: el código de salida queda en el registro (02 §9, punto 11). Ok es
/// el de la línea de cierre: sin él, OnFinished no distingue un tope (sin código) de una corrida sin esperar que salió bien.
/// </summary>
public sealed record ScriptResult(int? ExitCode, string? Message, TimeSpan Duration, bool Ok = true);
