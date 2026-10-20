using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Los pedidos de obs.record se atienden en orden, uno por vez (decisión 35), con el mismo reloj falso que las pruebas de la guarda: el
/// que llega mientras otro espera su segundo o su evento espera su turno, con el turno lee el estado de nuevo, y cancelado en la espera no
/// manda nada.
/// </summary>
public class ObsRecordTurnTests
{
    // Las dos esperas que deja en el registro: la del segundo desde el último aviso (E4-11) y la del turno.
    private const string ASecond = "se espera 1000 ms antes de mandar (E4-11)";
    private const string ItsTurn = "se espera su turno (decisión 35)";

    private static readonly Invocation Chain = new(Guid.NewGuid(), Trigger.Key, "console", "key.3", Gesture.Press, 0, "grabar-y-parar");

    private static Step Do(string command) => JsonSerializer.Deserialize<Step>($$"""{ "type": "obs.record", "do": "{{command}}" }""", Json.Options)!;

    // El mismo reloj falso en el OBS falso y en el cliente, que es quien anota la hora de cada cambio de la grabación.
    private static Task<ObsSession> StartAsync(RecordingState recording, FakeTimeProvider clock) =>
        ObsSession.StartAsync(recording, obs => obs.Clock = clock, clock);

    private static ObsRecord StepOf(ObsSession session, TimeProvider clock) => new(session.Client, session.State, session.Log, clock);

    private static int Logged(ObsSession session, string text) =>
        session.Log.Recent().Count(entry => entry.Message.Contains(text, StringComparison.Ordinal));

    // El caso del repaso del 2026-10-19 (fila 11): grabar, parar retenido por su segundo y grabar en ese medio. Antes el grabar veía "OBS
    // ya está grabando", no mandaba nada y la toma terminaba detenida. Ahora espera su turno, sale después del parar y con su propio
    // segundo desde el STOPPED.
    [Fact]
    public async Task A_start_pressed_while_a_stop_waits_its_second_goes_out_after_it_and_the_take_ends_recording()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        await session.Obs.StartRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);
        ObsRecord record = StepOf(session, clock);

        Task<StepResult> stop = record.RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => Logged(session, ASecond) == 1);
        Task<StepResult> start = record.RunAsync(Do("start"), Chain, CancellationToken.None);
        await Poll.Until(() => Logged(session, ItsTurn) == 1);
        clock.Advance(ObsRecord.Settle);
        StepResult stopped = await stop.WaitAsync(TimeSpan.FromSeconds(10));
        await Poll.Until(() => Logged(session, ASecond) == 2);           // el del grabar, desde el STOPPED
        await Eventually.StepsAsync(clock, ObsRecord.Settle - TimeSpan.FromMilliseconds(1), 1);
        int sentEarly = session.Obs.Times("StartRecord");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        StepResult started = await start.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(stopped.Failure);
        Assert.Equal(0, sentEarly);
        Assert.Null(started.Failure);
        Assert.Equal(new[] { "StopRecord", "StartRecord" }, session.Obs.Received.Where(type => type is "StopRecord" or "StartRecord"));
        Assert.NotEqual(RecordingState.Stopped, session.State.Current.Obs.Recording);
    }

    // El que espera turno respeta el token de su cadena, que también trae el tope de la acción: cancelado en la espera es cancelled, no
    // manda nada y no se queda con el turno, que lo tiene el pedido que sigue.
    [Fact]
    public async Task Cancelled_while_it_waits_its_turn_it_is_cancelled_sends_nothing_and_leaves_the_turn()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        await session.Obs.StartRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);
        ObsRecord record = StepOf(session, clock);
        using var cancel = new CancellationTokenSource();

        Task<StepResult> stop = record.RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => Logged(session, ASecond) == 1);
        Task<StepResult> start = record.RunAsync(Do("start"), Chain, cancel.Token);
        await Poll.Until(() => Logged(session, ItsTurn) == 1);
        await cancel.CancelAsync();
        StepResult cancelled = await start.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(ObsRecord.Settle);
        StepResult stopped = await stop.WaitAsync(TimeSpan.FromSeconds(10));
        StepResult next = await record.RunAsync(Do("stop"), Chain, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.Cancelled, cancelled.Failure?.Code);
        Assert.Equal("Se canceló esperando su turno: no se mandó el pedido de grabar.", cancelled.Failure?.Message);
        Assert.Null(stopped.Failure);
        Assert.Null(next.Failure);
        Assert.Equal(0, session.Obs.Times("StartRecord"));
        Assert.Equal(1, session.Obs.Times("StopRecord"));
    }

    // Con su turno vuelve a leer el estado. Dos grabar seguidos desde detenido: el segundo espera que OBS avise el arranque del primero y no
    // manda nada, como cuando ya graba. Antes los dos mandaban StartRecord y el segundo contaba el 500 como hecho.
    [Fact]
    public async Task A_second_start_reads_the_state_after_its_turn_and_sends_nothing()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        ObsRecord record = StepOf(session, clock);

        Task<StepResult> first = record.RunAsync(Do("start"), Chain, CancellationToken.None);
        Task<StepResult> second = record.RunAsync(Do("start"), Chain, CancellationToken.None);
        StepResult[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(results, result => Assert.Null(result.Failure));
        Assert.Equal(1, session.Obs.Times("StartRecord"));
        Assert.Equal(1, Logged(session, ItsTurn));
        Assert.Contains(session.Log.Recent(), entry => entry.Message == "grabar: OBS ya está grabando, no se manda nada.");
    }

    // El turno de un pedido que OBS aceptó dura hasta que OBS avisa el cambio: la respuesta llega antes que la acción (03 §5) y el que
    // sigue leería el estado de antes. Sin aviso, hasta el tope de un pedido. Acá el OBS falso no graba: contesta 100 a la pausa y no avisa
    // nada, y el programa cree que graba. El cliente va con el reloj real, como en la del grabar que OBS no anuncia: la espera del aviso
    // empieza después de la respuesta, que viaja por el socket, y el reloj falso del paso se avanza sin vencer el pedido. Lo que el parar
    // manda después no se mira: si en ese medio el sondeo del cliente leyó que OBS no graba, no manda nada, y está bien.
    [Fact]
    public async Task The_turn_of_an_accepted_request_lasts_until_obs_announces_the_change_or_the_limit_of_a_request()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Stopped);
        var clock = new FakeTimeProvider();
        session.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });
        ObsRecord record = StepOf(session, clock);

        Task<StepResult> pause = record.RunAsync(Do("pause"), Chain, CancellationToken.None);
        Task<StepResult> stop = record.RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Obs.Times("PauseRecord") == 1);
        await Eventually.StepsAsync(clock, ObsClient.RequestTimeout - TimeSpan.FromMilliseconds(1), 1);
        int sentEarly = session.Obs.Times("StopRecord");
        await Eventually.AdvancingAsync(clock, TimeSpan.FromMilliseconds(250), () => stop.IsCompleted, "el parar termina");

        Assert.Equal(0, sentEarly);
        Assert.Null((await pause).Failure);
        Assert.Null((await stop).Failure);
    }
}
