namespace PampaPad.Core;

/// <summary>Qué ícono muestra la bandeja. Las formas y los colores los pone la cáscara (spec 05 §3); acá sólo se elige cuál.</summary>
public enum TrayIcon { Idle, NoObs, Recording, Paused, Streaming, UpdateReady }

/// <summary>Lo que la bandeja muestra, calculado del estado. Función pura: la bandeja sólo pinta.</summary>
public sealed record TraySummary(TrayIcon Icon, string Tooltip, bool CanReconnect)
{
    private const string Separator = " · ";

    /// <summary>Si valen dos estados a la vez, el ícono es el de más arriba en la tabla de 05 §3 y el texto los nombra a todos.</summary>
    public static TraySummary From(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var obs = state.Obs;
        var icons = new List<TrayIcon>();
        var texts = new List<string>();
        if (obs.Link == ObsLinkState.Ready)
        {
            if (obs.Recording == RecordingState.Recording)
            {
                Add(TrayIcon.Recording, $"Grabando {Clock(obs.Recorded)}");
            }

            if (obs.Recording == RecordingState.Paused)
            {
                Add(TrayIcon.Paused, "En pausa: las marcas no entran");
            }

            if (obs.Streaming)
            {
                Add(TrayIcon.Streaming, "Transmitiendo");
            }
        }
        else
        {
            // Sin enlace el programa no sabe si OBS sigue grabando: se muestra "sin OBS" y lo último que se supo.
            Add(TrayIcon.NoObs, WithoutObs(obs.Link));
            if (obs.Recording == RecordingState.Recording)
            {
                texts.Add("lo último que se supo: grabando");
            }

            if (obs.Recording == RecordingState.Paused)
            {
                texts.Add("lo último que se supo: en pausa");
            }
        }

        // "Actualización lista" va debajo de "OBS no conectado" y arriba de "sin novedades" (05 §3).
        if (UpdateText(state.Update) is { } update)
        {
            Add(TrayIcon.UpdateReady, update);
        }

        if (icons.Count == 0)
        {
            Add(TrayIcon.Idle, "PampaPad · OBS conectado");
        }

        if (obs.Link == ObsLinkState.Ready && obs.MicMuted)
        {
            texts.Add("micrófono silenciado");
        }

        var canReconnect = obs.Link is ObsLinkState.AuthFailed or ObsLinkState.Kicked;
        return new TraySummary(icons[0], string.Join(Separator, texts), canReconnect);

        void Add(TrayIcon icon, string text)
        {
            icons.Add(icon);
            texts.Add(text);
        }
    }

    /// <summary>Lo que el texto de ayuda dice de la actualización; null si no hay ninguna ofrecida. E1-4 lo tenía en Tray.</summary>
    public static string? UpdateText(UpdateState update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return update.Version is null ? null : update.Phase switch
        {
            UpdatePhase.Available when update.Detail is null => $"Versión {update.Version} lista para instalar",
            UpdatePhase.Available => $"Versión {update.Version} lista para instalar. {update.Detail}",
            UpdatePhase.Downloading => $"Bajando la versión {update.Version}",
            UpdatePhase.Applying => $"Instalando la versión {update.Version}",
            _ => null,
        };
    }

    // Tiempo grabado como 0:34:12; las horas no se cortan en 24.
    private static string Clock(TimeSpan recorded) => $"{(int)recorded.TotalHours}:{recorded.Minutes:00}:{recorded.Seconds:00}";

    private static string WithoutObs(ObsLinkState link) => link switch
    {
        ObsLinkState.Closed => "Sin OBS: no está abierto",
        ObsLinkState.NoWebsocket => "Sin OBS: está abierto, sin WebSocket",
        ObsLinkState.AuthFailed => "Sin OBS: clave rechazada",
        ObsLinkState.Loading => "Sin OBS: conectando",
        ObsLinkState.Switching => "Sin OBS: cambiando de colección",
        ObsLinkState.Lost => "Sin OBS: se cortó la conexión",
        ObsLinkState.Kicked => "Sin OBS: OBS cerró la sesión",
        ObsLinkState.Exiting => "Sin OBS: se está cerrando",
        _ => "Sin OBS",
    };
}
