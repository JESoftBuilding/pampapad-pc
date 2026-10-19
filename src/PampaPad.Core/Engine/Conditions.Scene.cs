using System.Globalization;
using System.Text.Json;

namespace PampaPad.Core;

// Las condiciones de OBS que miran la imagen de la escena al aire (02 §5; 03 §7): fuentes con imagen, el monitor de la captura, la cámara
// tapada y el lienzo lleno. Sólo cuentan las fuentes de la escena al aire; lo que la protección deja afuera da "no aplica" (03 §7).
public static partial class Conditions
{
    public const string SourceHasImage = "obs.sourceHasImage";
    public const string CaptureHasMonitor = "obs.captureHasMonitor";
    public const string CameraNotCovered = "obs.cameraNotCovered";
    public const string CanvasFilled = "obs.canvasFilled";

    /// <summary>Por debajo de este desvío la cámara está tapada o en negro (03 §7: tapada 0,6; destapada, más de 50). Propuesto, sin medir en el medio.</summary>
    public const double CoveredDeviation = 5;

    /// <summary>Por debajo de esta parte del lienzo la escena no lo llena (03 §7; Decidido 18 que sea falla). El 90 % es Propuesto.</summary>
    public const double FilledShare = 0.9;

    // Las que pueden quedar sin imagen: las de sólo audio también miden 0 x 0 (03 §7).
    private static readonly string[] ImageKinds = ["monitor_capture", "window_capture", CameraKind];

    /// <summary>Las fuentes de video visibles de la escena al aire no miden 0 x 0 (03 §7): es lo que ve el espejo del celular caído.</summary>
    public static ConditionType Image(ObsQueries obs) => new(
        SourceHasImage,
        "Las fuentes tienen imagen",
        [Source],
        async (p, ct) =>
        {
            Result<IReadOnlyList<SceneSource>> sources = await SourcesAsync(obs, p, ImageKinds, ct).ConfigureAwait(false);
            if (!sources.Ok)
            {
                return sources.Failure;
            }

            if (sources.Value!.Count == 0)
            {
                return Verdict.NotApplicable($"no hay capturas ni cámaras en \"{obs.SceneName}\".");
            }

            SceneSource? empty = sources.Value.FirstOrDefault(source => source.Width <= 0 || source.Height <= 0);
            return empty is null
                ? new Verdict(true, $"Las capturas de \"{obs.SceneName}\" tienen imagen.")
                : new Verdict(false, $"\"{empty.Name}\" mide {empty.Width:0} x {empty.Height:0}: no tiene imagen.");
        });

    /// <summary>
    /// La captura de pantalla apunta a un monitor que OBS todavía lista (03 §7): monitor_id de GetInputSettings contra los itemValue de
    /// GetInputPropertiesListPropertyItems. Sin probar con un monitor ausente: va como falla común.
    /// </summary>
    public static ConditionType Monitor(ObsQueries obs) => new(
        CaptureHasMonitor,
        "La captura tiene monitor",
        [Source],
        async (p, ct) =>
        {
            Result<IReadOnlyList<SceneSource>> captures = await SourcesAsync(obs, p, ["monitor_capture"], ct).ConfigureAwait(false);
            if (!captures.Ok)
            {
                return captures.Failure;
            }

            if (captures.Value!.Count == 0)
            {
                return Verdict.NotApplicable($"no hay captura de pantalla en \"{obs.SceneName}\".");
            }

            foreach (SceneSource capture in captures.Value)
            {
                Result<JsonElement> settings = await obs.AskAsync("GetInputSettings", new { inputUuid = capture.Uuid }, $"los ajustes de \"{capture.Name}\"", ct).ConfigureAwait(false);
                Result<JsonElement> monitors = settings.Ok
                    ? await obs.AskAsync("GetInputPropertiesListPropertyItems", new { inputUuid = capture.Uuid, propertyName = "monitor_id" }, "los monitores", ct).ConfigureAwait(false)
                    : settings.Failure;
                if (!monitors.Ok)
                {
                    return monitors.Failure;
                }

                ObsMessages.Has(settings.Value, "inputSettings", out JsonElement inputSettings);
                string? chosen = ObsMessages.Text(inputSettings, "monitor_id");
                if (!ObsMessages.Items(monitors.Value, "propertyItems").Any(item => ObsMessages.Text(item, "itemValue") == chosen))
                {
                    return new Verdict(false, $"\"{capture.Name}\" apunta a un monitor que OBS no lista: ¿está apagado o en otra PC?");
                }
            }

            return new Verdict(true, $"Las capturas de \"{obs.SceneName}\" tienen su monitor.");
        });

    /// <summary>
    /// La cámara no está tapada ni en negro (03 §7): captura de la fuente, no de la escena, en bmp de 160 de ancho, y el desvío del brillo.
    /// Sólo con la fuente activa y sólo detenido: con una toma en curso no se pide una captura y no aplica.
    /// </summary>
    public static ConditionType Covered(ObsQueries obs, StateStore state) => new(
        CameraNotCovered,
        "La cámara no está tapada",
        [Source],
        async (p, ct) =>
        {
            if (state.Current.Obs.Link == ObsLinkState.Ready && state.Current.Obs.TakeInProgress)
            {
                return Verdict.NotApplicable("con una toma en curso no se pide una captura de la cámara.");
            }

            Result<IReadOnlyList<SceneSource>> cameras = await CamerasAsync(obs, p, ct).ConfigureAwait(false);
            if (!cameras.Ok)
            {
                return cameras.Failure;
            }

            if (cameras.Value!.Count == 0)
            {
                return Verdict.NotApplicable($"no hay cámara en \"{obs.SceneName}\".");
            }

            int measured = 0;
            foreach (SceneSource camera in cameras.Value)
            {
                Result<JsonElement> active = await obs.AskAsync("GetSourceActive", new { sourceUuid = camera.Uuid }, $"si \"{camera.Name}\" está activa", ct).ConfigureAwait(false);
                if (!active.Ok)
                {
                    return active.Failure;
                }

                if (!ObsMessages.Flag(active.Value, "videoActive"))
                {
                    continue;
                }

                measured++;
                Result<JsonElement> shot = await obs.AskAsync("GetSourceScreenshot", new { sourceUuid = camera.Uuid, imageFormat = "bmp", imageWidth = 160 }, $"la imagen de \"{camera.Name}\"", ct).ConfigureAwait(false);
                if (!shot.Ok)
                {
                    return shot.Failure;
                }

                if (ImageStats.FromImageData(ObsMessages.Text(shot.Value, "imageData")) is not { } light)
                {
                    return new Failure(ErrorCodes.ObsRequestFailed, $"La imagen de \"{camera.Name}\" no se pudo leer.");
                }

                if (light.Deviation < CoveredDeviation)
                {
                    return new Verdict(false, $"La cámara \"{camera.Name}\" se ve tapada o en negro: desvío {Decimal(light.Deviation)}; se pide {Decimal(CoveredDeviation)} o más.");
                }
            }

            // Sin ninguna cámara activa no se midió nada: la protección de 03 §7 las dejó afuera, y eso es "no aplica", no "tiene imagen".
            return measured == 0
                ? Verdict.NotApplicable($"la cámara de \"{obs.SceneName}\" no está activa.")
                : new Verdict(true, $"La cámara de \"{obs.SceneName}\" tiene imagen.");
        });

    /// <summary>
    /// La escena al aire llena el lienzo (03 §7; Decidido 18): la unión del área real de sus elementos visibles de primer nivel sobre
    /// baseWidth x baseHeight. Una pieza mide el lienzo entero (03 §7).
    /// </summary>
    public static ConditionType Canvas(ObsQueries obs) => new(
        CanvasFilled,
        "La escena llena el lienzo",
        [],
        async (_, ct) =>
        {
            Result<JsonElement> video = await obs.AskAsync("GetVideoSettings", null, "el lienzo", ct).ConfigureAwait(false);
            Result<IReadOnlyList<JsonElement>> items = video.Ok ? await obs.ProgramItemsAsync(ct).ConfigureAwait(false) : video.Failure;
            if (!items.Ok)
            {
                return items.Failure;
            }

            double width = ObsMessages.Number(video.Value, "baseWidth");
            double height = ObsMessages.Number(video.Value, "baseHeight");
            IReadOnlyList<CanvasRect> rects = [.. items.Value!
                .Select(item => ObsMessages.Has(item, "sceneItemTransform", out JsonElement transform) ? CanvasCoverage.RectOf(transform) : null)
                .OfType<CanvasRect>()];
            double share = CanvasCoverage.Covered(rects, width, height);
            return new Verdict(share >= FilledShare,
                $"\"{obs.SceneName}\" llena el {Decimal(share * 100)} % del lienzo de {width:0} x {height:0}; se pide {FilledShare * 100:0} %.");
        });

    // Las fuentes de esas clases que se ven en la escena al aire; con source, sólo esa, si está al aire (si no, no aplica).
    private static async Task<Result<IReadOnlyList<SceneSource>>> SourcesAsync(ObsQueries obs, JsonElement p, string[] kinds, CancellationToken ct)
    {
        Result<IReadOnlyList<SceneSource>> visible = await obs.VisibleSourcesAsync(ct).ConfigureAwait(false);
        if (!visible.Ok)
        {
            return visible.Failure;
        }

        if (ConditionRules.Reference(p, "source") is not { } wanted)
        {
            return Result<IReadOnlyList<SceneSource>>.Success([.. visible.Value!.Where(source => source.InputKind is { } kind && kinds.Contains(kind))]);
        }

        Result<ObsItem> input = obs.FindInput(wanted);
        return input.Ok
            ? Result<IReadOnlyList<SceneSource>>.Success([.. visible.Value!.Where(source => source.Uuid == input.Value!.Uuid)])
            : input.Failure;
    }

    private static string Decimal(double value) => value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}
