namespace PampaPad.Core;

/// <summary>
/// Lo que comparten los pasos de OBS de la etapa 4 (03 §3, "Falla de cualquier paso de OBS"): sin el enlace en ready no se manda nada,
/// con el token ya cancelado tampoco (ObsConnection manda con su propio tope: hallazgo E2-3), y una respuesta que no es 100 es una sola
/// falla con el código y el comentario de OBS en el mensaje, que el Dispatcher deja en el registro. obs.marker tiene su tabla (Markers).
/// </summary>
public static class ObsStep
{
    /// <summary>Recurso no encontrado: lo que se pidió ya no está en OBS.</summary>
    public const int ResourceNotFound = 600;

    public static Failure? Unready(ObsState known, string what)
    {
        ArgumentNullException.ThrowIfNull(known);
        return known.Link == ObsLinkState.Ready ? null : new Failure(ErrorCodes.ObsDisconnected, $"Sin OBS: no se mandó {what}.");
    }

    public static Failure? CancelledBefore(CancellationToken ct, string what) =>
        ct.IsCancellationRequested ? new Failure(ErrorCodes.Cancelled, $"Se canceló antes de mandar {what}.") : null;

    /// <summary>
    /// Code 0 es "sin respuesta" y Comment trae el código del programa (ObsResponse): ahí no se sabe si OBS lo hizo. Sin respuesta y con
    /// obs_request_failed es una respuesta sin requestStatus (ObsMessages.ToResponse), no un OBS ausente.
    /// </summary>
    public static Failure Rejected(ObsResponse response, string what) => response.Code switch
    {
        0 when response.Comment == ErrorCodes.ObsTimeout =>
            new Failure(ErrorCodes.ObsTimeout, $"OBS no contestó {what} en {ObsClient.RequestTimeout.TotalSeconds:0} s: no se sabe si lo hizo."),
        0 when response.Comment == ErrorCodes.Cancelled =>
            new Failure(ErrorCodes.Cancelled, $"Se canceló esperando la respuesta a {what}: no se sabe si OBS lo hizo."),
        0 when response.Comment == ErrorCodes.ObsDisconnected =>
            new Failure(ErrorCodes.ObsDisconnected, $"Se cortó OBS esperando la respuesta a {what}."),
        0 => new Failure(ErrorCodes.ObsRequestFailed, $"OBS contestó {what} sin decir cómo le fue."),
        _ => new Failure(ErrorCodes.ObsRequestFailed, string.IsNullOrWhiteSpace(response.Comment)
            ? $"OBS rechazó {what} (código {response.Code})."
            : $"OBS rechazó {what} (código {response.Code}): {response.Comment}"),
    };
}
