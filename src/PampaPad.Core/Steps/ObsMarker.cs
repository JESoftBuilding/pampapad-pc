namespace PampaPad.Core;

public sealed record MarkerParameters(string? Marker, string? Label);

/// <summary>
/// obs.marker: pide CreateRecordChapter con el título "tipo etiqueta" (02 §5; 03 §4).
/// Con enlace se manda siempre, también en pausa o detenido: el que decide es OBS y el estado conocido sólo elige el texto.
/// </summary>
public sealed class ObsMarker(ObsClient obs, StateStore state) : StepType<MarkerParameters>
{
    public const string TypeId = "obs.marker";

    public override string Id => TypeId;

    public override string Name => "Marcar capítulo";

    public override string Group => "OBS";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("marker", "Tipo de marca", FieldKind.Option, Options: [.. MarkerTypes.All]),
        new Field("label", "Etiqueta", FieldKind.Text, Optional: true),
    ];

    protected override IEnumerable<Problem> Check(MarkerParameters p)
    {
        if (!MarkerTypes.TryParse(p.Marker, out _))
        {
            yield return Invalid("marker", $"\"{p.Marker}\" no es un tipo de marca: van short, inicio, fin, sacar o largo.");
        }
    }

    protected override async Task<StepResult> RunAsync(MarkerParameters p, Invocation invocation, CancellationToken ct)
    {
        if (!MarkerTypes.TryParse(p.Marker, out MarkerType type))
        {
            return new StepResult(new Failure(ErrorCodes.ConfigInvalid, $"\"{p.Marker}\" no es un tipo de marca."));
        }

        var known = state.Current.Obs;
        if (known.Link != ObsLinkState.Ready)
        {
            return new StepResult(new Failure(ErrorCodes.ObsDisconnected, Markers.NoObs));
        }

        if (!known.SupportsMarkers)
        {
            return new StepResult(new Failure(ErrorCodes.MarkerUnsupported, Markers.Unsupported));
        }

        // ObsClient manda el pedido y recién después mira el token: con uno ya cancelado la marca saldría igual.
        if (ct.IsCancellationRequested)
        {
            return new StepResult(new Failure(ErrorCodes.Cancelled, "Se canceló antes de mandar la marca: no entró."));
        }

        ObsResponse response = await obs.RequestAsync(Markers.RequestType, new { chapterName = Markers.Title(type, p.Label) }, ct).ConfigureAwait(false);
        return new StepResult(Markers.Interpret(response, known.Recording));
    }
}
