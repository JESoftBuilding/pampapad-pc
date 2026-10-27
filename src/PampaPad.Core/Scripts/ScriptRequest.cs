namespace PampaPad.Core;

/// <summary>
/// Lo que se le pide a ScriptRunner. Wait y Timeout pisan los de la ficha (02 §5). OnLine recibe cada renglón de la salida; OnFinished,
/// cómo terminó la corrida, en todos los caminos y una sola vez (sin esperar, cuando el script sale de verdad).
/// </summary>
public sealed record ScriptRequest(
    string Id, bool? Wait, TimeSpan? Timeout, Invocation Invocation, System.Action<string>? OnLine = null, System.Action<ScriptResult>? OnFinished = null);

/// <summary>Cómo terminó un script. Sin esperar, ExitCode y Message van vacíos: el código de salida queda en el registro (02 §9, punto 11).</summary>
public sealed record ScriptResult(int? ExitCode, string? Message, TimeSpan Duration);
