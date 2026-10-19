using System.Globalization;

namespace PampaPad.Core;

public sealed record WaitWindowParameters(string? Title, int? TimeoutSec, int? Width, int? Height, string? Process);

/// <summary>
/// wait.window: espera hasta que exista una ventana con ese título y, si se piden, con ese tamaño (02 §5). Sondea cada 250 ms y compara
/// el tamaño exacto, como window.sizeIs. Al llegar al tope falla con window_not_found y el detalle dice si no apareció, si apareció con
/// otro tamaño o si está minimizada (01 §9). Con un tamaño pedido, una minimizada se sigue esperando: se la puede restaurar; sin tamaño,
/// alcanza con que exista, como en window.exists. El sondeo va con un PeriodicTimer
/// del reloj inyectado, que guarda el tic si llega antes de esperarlo: con el reloj falso, cada avance es un sondeo.
/// </summary>
public sealed class WaitWindow(IWindows windows, TimeProvider clock) : StepType<WaitWindowParameters>
{
    public const string TypeId = "wait.window";

    /// <summary>Cada cuánto se mira la lista de ventanas (02 §5). Propuesto, sin medir.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    public override string Id => TypeId;

    public override string Name => "Esperar una ventana";

    public override string Group => "Cadenas";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("title", "Título de la ventana", FieldKind.Window),
        new Field("width", "Ancho", FieldKind.Number, Optional: true),
        new Field("height", "Alto", FieldKind.Number, Optional: true),
        new Field("timeoutSec", "Cuánto esperar, en segundos", FieldKind.Number),
        new Field("process", "Programa", FieldKind.Text, Optional: true),
    ];

    protected override IEnumerable<Problem> Check(WaitWindowParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Title))
        {
            yield return Invalid("title", "Falta el título de la ventana.");
        }

        if (p.TimeoutSec is not > 0 || p.TimeoutSec > Executor.ActionTimeout.TotalSeconds)
        {
            yield return Invalid("timeoutSec", $"timeoutSec va en segundos, de 1 a {Executor.ActionTimeout.TotalSeconds:0}: el tope de la acción.");
        }

        if (p.Width is <= 0)
        {
            yield return Invalid("width", "width va en píxeles, mayor que 0.");
        }

        if (p.Height is <= 0)
        {
            yield return Invalid("height", "height va en píxeles, mayor que 0.");
        }
    }

    protected override async Task<StepResult> RunAsync(WaitWindowParameters p, Invocation invocation, CancellationToken ct)
    {
        string title = p.Title ?? "";
        var limit = TimeSpan.FromSeconds(p.TimeoutSec ?? 0);
        using var poll = new PeriodicTimer(Poll, clock);
        long started = clock.GetTimestamp();
        while (true)
        {
            IReadOnlyList<WindowInfo> found = WindowMatch.Find(windows.List(), title, p.Process);
            string detail = WindowMatch.Describe(found, title, p.Process, p.Width, p.Height);
            if (found.Any(window => WindowMatch.HasSize(window, p.Width, p.Height)))
            {
                return new StepResult(null, detail);
            }

            if (clock.GetElapsedTime(started) >= limit)
            {
                return new StepResult(new Failure(ErrorCodes.WindowNotFound,
                    $"Pasaron {limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s esperando la ventana. {detail}"));
            }

            await poll.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
    }
}
