namespace PampaPad.Core;

public sealed record PageGoParameters(string? Target);

/// <summary>
/// page.go: cambia la página base (02 §5 y §7; preguntas abiertas 12 y 44, escrito (a)). Con una toma en curso falla con
/// navigation_blocked y corta la cadena: lo decide Navigation, a través de Dispatcher.Go. Un target que no nombra una página, o que nombra
/// una que no es de teclas y por eso no puede ser la base (Decidido 35), es el problema 4 de 02 §10, de gravedad error: la acción no corre.
/// </summary>
public sealed class PageGo(Library library, Func<string, Failure?> go) : StepType<PageGoParameters>
{
    public const string TypeId = "page.go";

    public override string Id => TypeId;

    public override string Name => "Ir a otra página";

    public override string Group => "Cadenas";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("target", "Página", FieldKind.Page),
    ];

    protected override IEnumerable<Problem> Check(PageGoParameters p) => Check(p, library.Current);

    // La página se busca en la configuración que se valida: la cargada al revisar el archivo, la candidata al guardar (hallazgo E5-11).
    protected override IEnumerable<Problem> Check(PageGoParameters p, Config config)
    {
        if (string.IsNullOrWhiteSpace(p.Target))
        {
            yield return Invalid("target", "Falta la página: va su id, o next, previous o main.");
        }
        else if (p.Target is not (Navigation.Main or Navigation.Next or Navigation.Previous))
        {
            Page? page = config.FindPage(p.Target);
            if (page is null)
            {
                yield return new Problem("target", ProblemCodes.PageNotFound, $"page.go nombra la página \"{p.Target}\", que no existe.", Severity.Error);
            }
            else if (!Dispatcher.IsKeys(page))
            {
                // Lo mismo que rechaza Dispatcher.Go al correr, pero al cargar: si no, la acción corría y se cortaba en el page.go, con lo
                // de antes ya hecho.
                string message = $"page.go nombra la página \"{p.Target}\", de tipo {page.Type}, que no es de teclas: no puede ser la base.";
                yield return new Problem("target", ProblemCodes.PageNotFound, message, Severity.Error);
            }
        }
    }

    protected override Task<StepResult> RunAsync(PageGoParameters p, Invocation invocation, CancellationToken ct) =>
        Task.FromResult(new StepResult(go(p.Target ?? "")));
}
