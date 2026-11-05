namespace PampaPad.Core;

// El aviso grave en la bandeja (plan de la 9b, E9b-3), aparte por el tope de 300 líneas (01 §1).
public sealed partial record TraySummary
{
    /// <summary>
    /// El ícono y el texto del aviso a la vista: el de "Falló una acción" con su nombre; obs_lost, el de "OBS no conectado" (05 §5).
    /// marker_failed no suma nada: la falla de la acción ya está, y la de la ventana o del MCP no cambia el ícono.
    /// </summary>
    private static (TrayIcon Icon, string Text)? AlertText(AppState state) => state.Alert switch
    {
        null or { Id: AlertTexts.MarkerFailed } => null,
        { Id: AlertTexts.ObsLost } alert => (TrayIcon.NoObs, $"Aviso: {AlertTexts.Name(alert.Id)}"),
        { } alert => (TrayIcon.ActionFailed, $"Aviso: {AlertTexts.Name(alert.Id)}"),
    };
}
