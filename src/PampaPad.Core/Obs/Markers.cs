namespace PampaPad.Core;

/// <summary>
/// El contrato de marcas del canal (Decidido 3) y la tabla medida de 03 §4: qué título lleva un capítulo
/// y qué quiere decir cada respuesta de OBS a CreateRecordChapter.
/// </summary>
public static class Markers
{
    public const string RequestType = "CreateRecordChapter";
    public const string NoObs = "Sin OBS: la marca no entró.";
    public const string Unsupported = "Este OBS no admite marcas.";

    private const int NoAnswer = 0;            // el programa no llegó a tener respuesta: Comment trae su código
    private const int UnknownRequest = 204;    // un obs-websocket anterior a la 5.5.0, que no tiene el pedido
    private const int NotRecording = 501;      // medido: sin grabar, antes y después de la toma
    private const int ProcessingFailed = 702;  // medido en pausa; el código de OBS dice que también sale con un formato sin capítulos

    /// <summary>El título del capítulo: el tipo, en minúsculas, y después la etiqueta, si hay.</summary>
    public static string Title(MarkerType type, string? label)
    {
        string word = MarkerTypes.Text(type);
        return string.IsNullOrWhiteSpace(label) ? word : $"{word} {label.Trim()}";
    }

    /// <summary>La falla que corresponde a la respuesta de OBS, o <c>null</c> si la marca entró.</summary>
    public static Failure? Interpret(ObsResponse response, RecordingState recording)
    {
        if (response.Ok)
        {
            return null;
        }

        return response.Code switch
        {
            NoAnswer when response.Comment == ErrorCodes.ObsTimeout => new Failure(ErrorCodes.ObsTimeout, "OBS no contestó: la marca no entró."),
            NoAnswer => new Failure(ErrorCodes.ObsDisconnected, NoObs),
            NotRecording => new Failure(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró."),
            ProcessingFailed when recording == RecordingState.Paused => new Failure(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."),
            ProcessingFailed => new Failure(ErrorCodes.MarkerRejected, "OBS rechazó la marca: el formato de grabación no admite capítulos."),
            UnknownRequest => new Failure(ErrorCodes.MarkerUnsupported, Unsupported),
            _ => new Failure(ErrorCodes.ObsRequestFailed, $"OBS rechazó la marca (código {response.Code}): {response.Comment}"),
        };
    }
}
