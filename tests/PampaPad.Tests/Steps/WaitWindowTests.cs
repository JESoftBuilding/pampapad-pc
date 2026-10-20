using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Las esperas de ventana de 01 §7: aparece al tercer sondeo, no aparece nunca, se cancela, tamaño equivocado y minimizada.</summary>
public class WaitWindowTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.3", Gesture.Tap, 0);
    private static readonly WindowInfo Mirror = new("bancocam", "scrcpy", 1823, 850, Minimized: false);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static Step Bancocam(int timeoutSec = 5) =>
        Parse($$"""{ "type": "wait.window", "title": "bancocam", "width": 1823, "height": 850, "timeoutSec": {{timeoutSec}} }""");

    // Un sondeo por avance: el PeriodicTimer guarda el tic, así que no importa si el paso ya estaba esperando.
    private static async Task PollsAsync(FakeWindows windows, FakeTimeProvider clock, int polls)
    {
        for (int list = 1; list < polls; list++)
        {
            await Poll.Until(() => windows.Lists == list);
            clock.Advance(WaitWindow.Poll);
        }
    }

    [Fact]
    public async Task A_window_that_appears_at_the_third_poll_ends_well_saying_its_size()
    {
        var clock = new FakeTimeProvider();
        var windows = new FakeWindows();
        windows.OpenFrom(3, Mirror);

        Task<StepResult> running = new WaitWindow(windows, clock).RunAsync(Bancocam(), Tap, CancellationToken.None);
        await PollsAsync(windows, clock, 3);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(result.Failure);
        Assert.Equal(3, windows.Lists);
        Assert.Equal("La ventana \"bancocam\" mide 1823 x 850.", result.Message);
    }

    [Fact]
    public async Task A_window_that_never_appears_is_window_not_found_at_the_limit()
    {
        var clock = new FakeTimeProvider();
        var windows = new FakeWindows();

        Task<StepResult> running = new WaitWindow(windows, clock).RunAsync(Bancocam(timeoutSec: 1), Tap, CancellationToken.None);
        await PollsAsync(windows, clock, 5);                    // 0, 250, 500, 750 y 1000 ms
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.WindowNotFound, result.Failure?.Code);
        Assert.Equal("Pasaron 1 s esperando la ventana. No hay una ventana \"bancocam\".", result.Failure?.Message);
        Assert.Equal(5, windows.Lists);
    }

    [Fact]
    public async Task A_window_with_another_size_says_both_sizes()
    {
        var clock = new FakeTimeProvider();
        var windows = new FakeWindows();
        windows.Open(Mirror with { Width = 2279, Height = 1063 });

        Task<StepResult> running = new WaitWindow(windows, clock).RunAsync(Bancocam(timeoutSec: 1), Tap, CancellationToken.None);
        await PollsAsync(windows, clock, 5);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.WindowNotFound, result.Failure?.Code);
        Assert.Equal("Pasaron 1 s esperando la ventana. La ventana \"bancocam\" mide 2279 x 1063 y se esperaba 1823 x 850.", result.Failure?.Message);
    }

    [Fact]
    public async Task A_minimized_window_is_waited_for_and_at_the_limit_says_it_is_minimized()
    {
        var clock = new FakeTimeProvider();
        var windows = new FakeWindows();
        windows.Open(Mirror with { Minimized = true, Width = 160, Height = 28 });

        Task<StepResult> running = new WaitWindow(windows, clock).RunAsync(Bancocam(timeoutSec: 1), Tap, CancellationToken.None);
        await PollsAsync(windows, clock, 5);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.WindowNotFound, result.Failure?.Code);
        Assert.EndsWith("La ventana \"bancocam\" está minimizada y se esperaba 1823 x 850.", result.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_while_it_waits_comes_out_as_cancellation()
    {
        var clock = new FakeTimeProvider();
        var windows = new FakeWindows();
        using var cancel = new CancellationTokenSource();

        Task<StepResult> running = new WaitWindow(windows, clock).RunAsync(Bancocam(), Tap, cancel.Token);
        await Poll.Until(() => windows.Lists == 1);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Without_a_size_any_window_with_the_title_is_enough_and_the_process_filters()
    {
        var folder = new WindowInfo("bancocam", "explorer", 960, 600, false);
        var anyWindows = new FakeWindows();
        anyWindows.Open(folder);
        var scrcpyWindows = new FakeWindows();
        scrcpyWindows.Open(folder);
        var clock = new FakeTimeProvider();

        StepResult any = await new WaitWindow(anyWindows, clock).RunAsync(Parse("""{ "type": "wait.window", "title": "bancocam", "timeoutSec": 1 }"""), Tap, CancellationToken.None);
        Task<StepResult> running = new WaitWindow(scrcpyWindows, clock).RunAsync(Parse("""{ "type": "wait.window", "title": "bancocam", "process": "scrcpy", "timeoutSec": 1 }"""), Tap, CancellationToken.None);
        await PollsAsync(scrcpyWindows, clock, 5);
        StepResult scrcpy = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(any.Failure);
        Assert.Equal(ErrorCodes.WindowNotFound, scrcpy.Failure?.Code);
        Assert.EndsWith("No hay una ventana \"bancocam\" de scrcpy.", scrcpy.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_size_a_minimized_window_is_enough()
    {
        // 02 §5: el tamaño se exige sólo "si se piden". Una ventana abierta y minimizada existe: no se espera a que la restauren.
        var windows = new FakeWindows();
        windows.Open(Mirror with { Minimized = true, Width = 160, Height = 28 });

        StepResult result = await new WaitWindow(windows, new FakeTimeProvider()).RunAsync(
            Parse("""{ "type": "wait.window", "title": "bancocam", "timeoutSec": 1 }"""), Tap, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal("La ventana \"bancocam\" está minimizada.", result.Message);
        Assert.Equal(1, windows.Lists);
    }

    [Theory]
    [InlineData("""{ "type": "wait.window", "timeoutSec": 5 }""", "title")]
    [InlineData("""{ "type": "wait.window", "title": "bancocam" }""", "timeoutSec")]
    [InlineData("""{ "type": "wait.window", "title": "", "timeoutSec": 5 }""", "title")]
    [InlineData("""{ "type": "wait.window", "title": "bancocam", "timeoutSec": 0 }""", "timeoutSec")]
    [InlineData("""{ "type": "wait.window", "title": "bancocam", "timeoutSec": 60 }""", "timeoutSec")]
    [InlineData("""{ "type": "wait.window", "title": "bancocam", "timeoutSec": 5, "width": 0 }""", "width")]
    [InlineData("""{ "type": "wait.window", "title": "bancocam", "timeoutSec": 5, "height": -1 }""", "height")]
    public void A_bad_parameter_is_a_problem_of_the_step(string json, string where)
    {
        Problem problem = Assert.Single(new WaitWindow(new FakeWindows(), TimeProvider.System).Validate(Parse(json)));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(59)]
    public void The_step_of_02_8_with_every_parameter_is_valid_from_1_s_to_under_the_limit_of_the_action(int timeoutSec)
    {
        // 02 §5: timeoutSec de 1 a 59; 60 es el tope de la acción, que vence primero (decisión 35). La de arriba prueba sólo lo que se
        // rechaza: un borde corrido no la haría fallar (revisión de E5-9).
        Step step = Parse($$"""{ "type": "wait.window", "title": "bancocam", "width": 1823, "height": 850, "timeoutSec": {{timeoutSec}}, "process": "scrcpy" }""");

        Assert.Empty(new WaitWindow(new FakeWindows(), TimeProvider.System).Validate(step));
    }

    [Fact]
    public void The_problem_at_the_limit_says_up_to_where_it_goes_and_why()
    {
        // Es lo que se lee en el editor con la tecla rota, y el número es el de la decisión 35 (revisión de E5-23).
        Problem problem = Assert.Single(new WaitWindow(new FakeWindows(), TimeProvider.System).Validate(Bancocam(60)));

        Assert.Equal("timeoutSec va en segundos, de 1 a 59: con 60 vence antes el tope de la acción.", problem.Message);
    }
}
