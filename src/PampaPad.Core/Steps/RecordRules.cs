namespace PampaPad.Core;

public enum RecordCommand
{
    Start,
    Stop,
    Pause,
    Resume,
}

/// <summary>Qué hace obs.record con el estado que el programa ya conoce (03 §3).</summary>
public enum RecordMove
{
    /// <summary>Manda el pedido.</summary>
    Send,

    /// <summary>OBS ya está donde se lo quiere llevar: no manda nada y termina bien (pregunta abierta 45, escrito (a)).</summary>
    AlreadyThere,

    /// <summary>Pausar o seguir sin grabación: falla con not_recording sin mandar nada.</summary>
    NotRecording,

    /// <summary>OBS está arrancando o deteniendo: hay que esperar el evento que cierra la transición.</summary>
    Wait,
}

/// <summary>
/// La tabla de obs.record de 03 §3, la única de la especificación, como función pura (Propuesto). Los pedidos son los cuatro explícitos,
/// nunca ToggleRecord: el programa mira el estado antes de mandar. Los códigos 500 a 503 sólo salen de una carrera entre la lectura y el
/// pedido, y dicen que OBS ya estaba donde se lo quería llevar (03 §3; Verificado contra el código de obs-websocket, no provocados).
/// </summary>
public static class RecordRules
{
    public static IReadOnlyList<string> Words { get; } = ["start", "stop", "pause", "resume"];

    public static bool TryParse(string? text, out RecordCommand command)
    {
        (bool ok, command) = text switch
        {
            "start" => (true, RecordCommand.Start),
            "stop" => (true, RecordCommand.Stop),
            "pause" => (true, RecordCommand.Pause),
            "resume" => (true, RecordCommand.Resume),
            _ => (false, RecordCommand.Start),
        };
        return ok;
    }

    public static string RequestType(RecordCommand command) => command switch
    {
        RecordCommand.Start => "StartRecord",
        RecordCommand.Stop => "StopRecord",
        RecordCommand.Pause => "PauseRecord",
        _ => "ResumeRecord",
    };

    public static RecordMove Decide(RecordCommand command, RecordingState known) => (command, known) switch
    {
        (_, RecordingState.Starting or RecordingState.Stopping) => RecordMove.Wait,
        (RecordCommand.Start, RecordingState.Stopped) => RecordMove.Send,
        (RecordCommand.Start, _) => RecordMove.AlreadyThere,
        (RecordCommand.Stop, RecordingState.Stopped) => RecordMove.AlreadyThere,
        (RecordCommand.Stop, _) => RecordMove.Send,
        (_, RecordingState.Stopped) => RecordMove.NotRecording,
        (RecordCommand.Pause, RecordingState.Paused) => RecordMove.AlreadyThere,
        (RecordCommand.Resume, RecordingState.Recording) => RecordMove.AlreadyThere,
        _ => RecordMove.Send,
    };

    /// <summary>El código con que OBS dice que ya estaba ahí: 500 ya graba, 501 no graba, 502 ya está en pausa, 503 no está en pausa.</summary>
    public static int RaceCode(RecordCommand command) => command switch
    {
        RecordCommand.Start => 500,
        RecordCommand.Stop => 501,
        RecordCommand.Pause => 502,
        _ => 503,
    };

    /// <summary>Después de un código de carrera: cuenta como hecho, salvo seguir con la grabación detenida, que es not_recording (03 §3, Inferido).</summary>
    public static bool RaceEndsWell(RecordCommand command, RecordingState now) =>
        command != RecordCommand.Resume || now != RecordingState.Stopped;

    public static string Describe(RecordingState state) => state switch
    {
        RecordingState.Stopped => "detenido",
        RecordingState.Recording => "grabando",
        RecordingState.Paused => "en pausa",
        RecordingState.Starting => "arrancando",
        _ => "deteniendo",
    };
}
