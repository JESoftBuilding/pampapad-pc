namespace PampaPad.Core;

public sealed record PageOverlayParameters(string? Target);

/// <summary>
/// page.overlay: pone una página encima de la que se ve, o la saca si ya estaba (02 §5 y §7, regla 8; plan de la 1.3.0, decisión 5).
/// Vale grabando: no cambia la base. Lo hace Dispatcher.ToggleOverlay. El target sigue las reglas de page.go: una página que no existe,
/// o que no es de teclas (Decidido 35), es el problema 4 de 02 §10, de gravedad error. A diferencia de page.go, no lleva next, previous
/// ni main: esas son destinos de la base.
/// </summary>
public sealed class PageOverlay(Library library, Func<string, Failure?> toggle) : StepType<PageOverlayParameters>
{
    public const string TypeId = "page.overlay";

    public override string Id => TypeId;

    public override string Name => "Abrir una página encima";

    public override string Group => "Cadenas";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("target", "Página", FieldKind.Page),
    ];

    protected override IEnumerable<Problem> Check(PageOverlayParameters p) => Check(p, library.Current);

    // Como page.go: la página se busca en la configuración que se valida, la cargada o la candidata al guardar (hallazgo E5-11).
    protected override IEnumerable<Problem> Check(PageOverlayParameters p, Config config)
    {
        if (string.IsNullOrWhiteSpace(p.Target))
        {
            yield return Invalid("target", "Falta la página que va encima: va su id.");
        }
        else if (config.FindPage(p.Target) is not { } page)
        {
            yield return new Problem("target", ProblemCodes.PageNotFound, $"page.overlay nombra la página \"{p.Target}\", que no existe.", Severity.Error);
        }
        else if (!Dispatcher.IsKeys(page))
        {
            string message = $"page.overlay nombra la página \"{p.Target}\", de tipo {page.Type}, que no es de teclas: no puede ir encima.";
            yield return new Problem("target", ProblemCodes.PageNotFound, message, Severity.Error);
        }
    }

    protected override Task<StepResult> RunAsync(PageOverlayParameters p, Invocation invocation, CancellationToken ct) =>
        Task.FromResult(new StepResult(toggle(p.Target ?? "")));
}
