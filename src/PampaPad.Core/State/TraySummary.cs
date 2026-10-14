namespace PampaPad.Core;

/// <summary>Qué ícono muestra la bandeja. Las formas y los colores los pone la cáscara (spec 05 §3); acá sólo se elige cuál.</summary>
public enum TrayIcon { Idle, NoObs, Recording, Paused, Streaming, UpdateReady }

/// <summary>
/// Los dos ítems de actualización del menú (05 §3 y §8), calculados del estado. Sólo los muestra la copia instalada.
/// InstallText es "Instalar versión X", con el motivo si no se puede, o lo que está haciendo mientras baja e instala.
/// </summary>
public sealed record UpdateMenu(bool CheckEnabled, string CheckText, bool InstallVisible, bool InstallEnabled, string InstallText);

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
        if (UpdateText(state) is { } update)
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

    /// <summary>
    /// Lo que el texto de ayuda dice de la actualización; null si no hay ninguna ofrecida. E1-4 lo tenía en Tray.
    /// Buscar de nuevo con una oferta a la vista (Checking) no la retira: UpdateCycle conserva la versión hasta que la
    /// búsqueda termina, y el ítem "Instalar" del menú sigue visible; la flecha no puede apagarse y prenderse.
    /// Detail, la última falla al bajar o instalar, va siempre; el motivo de RestartPolicy, sólo mientras rige.
    /// </summary>
    public static string? UpdateText(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var update = state.Update;
        return update.Version is null ? null : update.Phase switch
        {
            UpdatePhase.Available or UpdatePhase.Checking => Offer(update, Blocked(state)),
            UpdatePhase.Downloading => $"Bajando la versión {update.Version}",
            UpdatePhase.Applying => $"Instalando la versión {update.Version}",
            _ => null,
        };
    }

    /// <summary>
    /// Los ítems "Buscar actualización" e "Instalar versión X". Mientras rige RestartPolicy, "Instalar" figura apagado y con
    /// el motivo al lado (05 §8). El veredicto es el del estado que se pinta: al terminar la toma se prende solo.
    /// </summary>
    public static UpdateMenu UpdateItems(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var update = state.Update;
        var blocked = Blocked(state);
        var install = update.Version is null ? "Instalar" : update.Phase switch
        {
            UpdatePhase.Downloading => $"Bajando la versión {update.Version}…",
            UpdatePhase.Applying => $"Instalando la versión {update.Version}…",
            _ when blocked is not null => $"Instalar versión {update.Version} ({blocked})",
            _ => $"Instalar versión {update.Version}",
        };

        return new UpdateMenu(
            CheckEnabled: update.Phase is UpdatePhase.Idle or UpdatePhase.Available,
            CheckText: update.Phase == UpdatePhase.Checking ? "Buscando actualización…" : "Buscar actualización",
            InstallVisible: update.Version is not null && update.Phase != UpdatePhase.Idle,
            InstallEnabled: update.Version is not null && update.Phase == UpdatePhase.Available && blocked is null,
            InstallText: install);
    }

    // Por qué no se puede instalar ahora la versión ofrecida; null si se puede o si no hay una oferta a la vista.
    private static string? Blocked(AppState state)
    {
        if (state.Update.Version is null || state.Update.Phase is not (UpdatePhase.Available or UpdatePhase.Checking))
        {
            return null;
        }

        var verdict = RestartPolicy.Evaluate(state);
        return verdict.Allowed ? null : verdict.Reason ?? RestartPolicy.UnnamedReason;
    }

    private static string Offer(UpdateState update, string? blocked)
    {
        var text = $"Versión {update.Version} lista para instalar";
        if (update.Detail is not null)
        {
            text += ". " + update.Detail;
        }

        if (blocked is not null)
        {
            text += ". " + blocked;
        }

        return text;
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
