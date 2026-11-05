using System.Globalization;

namespace PampaPad.Core;

/// <summary>
/// Los avisos graves de 04 §5: sus ids, su número del byte 15 (04 §7), la prioridad (plan de la 9b, decisión 6), sus nombres y sus textos.
/// Los textos van en mayúsculas ASCII, como los de consola.py (ALERTS), y cada uno entra en su renglón.
/// </summary>
public static class AlertTexts
{
    public const string MarkerFailed = Notifier.MarkerFailed;
    public const string MicMuted = "mic_muted";
    public const string LongPause = "long_pause";
    public const string StillRecording = "still_recording";
    public const string ObsLost = "obs_lost";
    public const string RecordingWithFailure = "recording_with_failure";

    /// <summary>El único aviso que decide la consola sola (04 §5): no es grave, pero tiene canales en console.alerts.</summary>
    public const string NoPc = "no_pc";

    public const int TitleLetters = 8;
    public const int LineLetters = 17;
    public const int HintLetters = 25;

    /// <summary>El renglón chico de la banda: 16 letras (04 §2). La falla del chequeo se recorta a esto, que entra también en un renglón del aviso.</summary>
    public const int SmallLetters = 16;

    /// <summary>De la más importante a la menos (decisión 6). Si valen dos, se ve la primera y la otra espera.</summary>
    public static IReadOnlyList<string> Priority { get; } = [ObsLost, MicMuted, StillRecording, RecordingWithFailure, LongPause, MarkerFailed];

    /// <summary>El byte 15 de ScreenState (04 §7). 0 es ninguno.</summary>
    public static int Number(string id) => id switch
    {
        MarkerFailed => 1,
        MicMuted => 2,
        LongPause => 3,
        StillRecording => 4,
        ObsLost => 5,
        RecordingWithFailure => 6,
        _ => 0,
    };

    /// <summary>Rojo si ya arruina la toma, ámbar si la puede arruinar (04 §5).</summary>
    public static int Tone(string id) => id is MicMuted or StillRecording or ObsLost ? 2 : 1;

    /// <summary>El título, destino 2 (04 §5).</summary>
    public static string Title(string id) => id switch
    {
        MarkerFailed => "NO ENTRO",
        MicMuted => "MIC MUDO",
        LongPause => "EN PAUSA",
        StillRecording => "GRABANDO",
        ObsLost => "SIN OBS",
        _ => "OJO",
    };

    /// <summary>Cómo lo nombra el texto de ayuda de la bandeja (05 §5).</summary>
    public static string Name(string id) => id switch
    {
        MarkerFailed => "la marca no entró",
        MicMuted => "micrófono silenciado grabando",
        LongPause => "pausa larga",
        StillRecording => "tres horas grabando",
        ObsLost => "OBS se perdió con una toma en curso",
        RecordingWithFailure => "grabando con una falla",
        _ => id,
    };

    /// <summary>Los renglones de marker_failed, según el código del error del paso (04 §5).</summary>
    public static (string Line1, string Line2, string Hint) Marker(string? code) => code switch
    {
        ErrorCodes.MarkerPaused => ("ESTAS EN PAUSA", "", "SEGUI Y MARCA"),
        ErrorCodes.MarkerNotRecording => ("NO ESTAS GRABANDO", "", "SEGUI Y MARCA"),
        ErrorCodes.MarkerRejected => ("OBS RECHAZO", "LA MARCA", ""),
        ErrorCodes.MarkerUnsupported => ("ESTE OBS NO", "ADMITE MARCAS", ""),
        ErrorCodes.ObsDisconnected => ("SIN OBS", "", ""),
        ErrorCodes.ObsTimeout => ("OBS NO CONTESTO", "", ""),
        ErrorCodes.Cancelled => ("SE CANCELO", "", ""),
        _ => ("OBS RECHAZO", "LA MARCA", ""),
    };

    /// <summary>
    /// La falla de una comprobación del chequeo en letras de la consola, para el renglón chico y para recording_with_failure. Las siete del
    /// programa tienen la suya; otra, su mensaje recortado.
    /// </summary>
    public static string Failure(CheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        string text = check.Type switch
        {
            "obs.sourceHasImage" => "FUENTE SIN IMAGEN",
            "obs.cameraAudioOff" => "CAMARA CON AUDIO",
            "obs.cameraNotCovered" => "CAMARA TAPADA",
            "obs.recordFormatIs" => "FORMATO SIN MARCA",
            "obs.micNotMuted" => "MIC MUDO",
            "obs.canvasFilled" => "ESCENA ANGOSTA",
            "obs.captureHasMonitor" => "SIN MONITOR",
            _ => check.Message,
        };
        return Letters(text, SmallLetters);
    }

    /// <summary>En mayúsculas, sin acentos y recortado a max letras.</summary>
    public static string Letters(string text, int max) => ShortLabels.Cut(ShortLabels.Screen(text), max).TrimEnd();

    /// <summary>Un tiempo como 0:34:12, o como 3:00 si no llega a la hora y minutes es true.</summary>
    public static string Clock(TimeSpan time, bool minutes = false)
    {
        var whole = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(time.TotalSeconds)));
        return minutes && whole.TotalHours < 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalMinutes}:{whole.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalHours}:{whole.Minutes:00}:{whole.Seconds:00}");
    }
}
