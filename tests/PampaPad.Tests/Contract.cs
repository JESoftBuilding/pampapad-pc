using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// El contrato entre C# y la página, sin generador (01 §5): una muestra de cada pedido, respuesta y evento, serializada con las opciones
/// reales, tiene que ser igual a ui/src/bridge/contract.samples.json. contract.test.ts valida las mismas muestras contra contract.ts, y
/// fakeBridge las usa como datos. Con PAMPAPAD_WRITE_SAMPLES=1 la prueba escribe el archivo en vez de compararlo (01 §6): se corre así al
/// cambiar un pedido; el CI la corre sin la variable.
/// </summary>
public class Contract
{
    private static readonly Guid RunId = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid ShortRunId = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid PausedRunId = new("5c1b2f0e-3d4a-4b6c-9e8f-a1b2c3d4e5f6");
    private static readonly DateTimeOffset At = new(2026, 10, 14, 23, 30, 0, TimeSpan.FromHours(-3));

    private static string SamplesFile => Path.Combine(Architecture.RepoRoot(), "ui", "src", "bridge", "contract.samples.json");

    [Fact]
    public void Samples_match_the_file_the_page_reads()
    {
        string expected = Samples().ToJsonString(Json.Options) + "\n";
        if (Environment.GetEnvironmentVariable("PAMPAPAD_WRITE_SAMPLES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SamplesFile)!);
            File.WriteAllText(SamplesFile, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        Assert.True(File.Exists(SamplesFile), $"Falta {SamplesFile}: se escribe corriendo esta prueba con PAMPAPAD_WRITE_SAMPLES=1.");
        string actual = File.ReadAllText(SamplesFile).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            Assert.Fail("contract.samples.json no es lo que escriben los records de Dtos.cs: correr esta prueba con PAMPAPAD_WRITE_SAMPLES=1, mirar el diff y llevarlo a ui/src/bridge/contract.ts.");
        }
    }

    [Fact]
    public async Task Every_request_the_window_can_send_has_a_sample()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var bridge = new Bridge(rig.State, rig.Log);
        using IDisposable hooks = Requests.RegisterAll(bridge, Sources(rig));

        // Los de la cáscara (AppRequests) no se ven desde acá: van por RequestNames.Shell, y la prueba de humo los controla con el programa real.
        string[] windowRequests = [.. bridge.RegisteredFor(Caller.Window), .. RequestNames.Shell];

        Assert.Equal(windowRequests.Order(StringComparer.Ordinal), SampleNames("requests").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_constant_is_registered_by_the_core_or_named_as_shell()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var bridge = new Bridge(rig.State, rig.Log);
        using IDisposable hooks = Requests.RegisterAll(bridge, Sources(rig));

        Assert.Equal(RequestNames.All.Order(StringComparer.Ordinal), bridge.Registered.Concat(RequestNames.Shell).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_event_has_a_sample()
    {
        Assert.Equal(EventNames.All.Order(StringComparer.Ordinal), SampleNames("events").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_sample_request_opens_with_the_real_envelope()
    {
        foreach ((string name, JsonNode? pair) in Samples()["requests"]!.AsObject())
        {
            Result<IncomingRequest> parsed = Envelope.Parse(pair!["request"]!.ToJsonString());

            Assert.True(parsed.Ok, name);
            Assert.Equal(name, parsed.Value!.Request);
        }
    }

    internal static JsonObject Samples()
    {
        Config config = SampleConfig();
        StateView state = SampleState();
        LogEntry[] entries = SampleLog();
        var snapshot = new AppSnapshot("0.2.1", Migrations.CurrentVersion, config, state, [Marker()], [], new SettingsView(true, "COM20", null));

        return new JsonObject
        {
            ["requests"] = new JsonObject
            {
                [RequestNames.AppSnapshot] = Pair(Envelope.Request(1, RequestNames.AppSnapshot, new NoData()), Envelope.Response(1, snapshot)),
                [RequestNames.LogRead] = Pair(Envelope.Request(2, RequestNames.LogRead, new LogReadRequest(Count: 200)), Envelope.Response(2, new LogReadResponse(entries))),
                [RequestNames.ActionCancel] = Pair(Envelope.Request(3, RequestNames.ActionCancel, new ActionCancelRequest(RunId: RunId)), Envelope.Response(3, new ActionCancelResponse(1))),
                [RequestNames.WindowReady] = Pair(Envelope.Request(4, RequestNames.WindowReady, new NoData()), Envelope.Response(4, new NoData())),
            },
            ["events"] = new JsonObject
            {
                [EventNames.State] = Node(Envelope.Event(EventNames.State, state.Seq, state)),
                [EventNames.Log] = Node(Envelope.Event(EventNames.Log, state.Seq, entries[1])),
                [EventNames.ActionFinished] = Node(Envelope.Event(EventNames.ActionFinished, state.Seq, SampleFinished())),
                [EventNames.ConfigChanged] = Node(Envelope.Event(EventNames.ConfigChanged, state.Seq, new ConfigChangedView(config))),
            },
            ["error"] = Node(Envelope.Error(5, new Failure(ErrorCodes.UnknownRequest, "No existe el pedido config.sav.", "request"))),
        };
    }

    private static StatusSources Sources(DispatcherRig rig) =>
        new("0.0.0", rig.State, rig.Library, rig.Dispatcher, rig.Log, [rig.Marker], () => new SettingsView(false, null, null));

    private static IEnumerable<string> SampleNames(string section) => Samples()[section]!.AsObject().Select(pair => pair.Key);

    private static JsonObject Pair(string request, string response) => new() { ["request"] = Node(request), ["response"] = Node(response) };

    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    private static Config SampleConfig() => JsonSerializer.Deserialize<Config>(
        """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            { "id": "grabacion", "name": "Grabación", "keys": [ { "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" } ] }
          ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] }
          ]
        }
        """,
        Json.Options)!;

    private static StateView SampleState() => StateView.From(AppState.Initial with
    {
        Seq = 42,
        Obs = ObsState.Initial with
        {
            Link = ObsLinkState.Ready,
            Recording = RecordingState.Recording,
            Recorded = TimeSpan.FromMilliseconds(754_000),
            SceneName = "Pantalla",
            MicName = "Mic/Aux",
            SupportsMarkers = true,
        },
        Library = new LibraryState(LibraryStatus.Loaded, @"D:\Conocimiento\Areas\marca-personal\pampapad"),
        Problems = [new Problem("pages[0].keys[1].hotkey", ErrorCodes.HotkeyTaken, "Windows no dejó registrar Ctrl+Shift+Alt+F6: la tiene otro programa.", Severity.Warning)],
        Hotkeys = new HotkeysState(4, 1),
        BasePage = "grabacion",
        ActivePage = "grabacion",
        Running = [new RunningAction(RunId, "marca-largo", Trigger.Key)],
        Console = new ConsoleLink(ConsoleLinkState.Connected, "COM20"),
    });

    private static LogEntry[] SampleLog() =>
    [
        new(At, LogLevel.Info, "obs", "enlace con OBS: Ready (lectura inicial completa)"),
        new(At.AddSeconds(4), LogLevel.Info, "dispatcher", "marca-short terminó bien en 18 ms.", null, Trigger.Key, ShortRunId),
        new(At.AddSeconds(9), LogLevel.Warning, "dispatcher", "marca-fin falló en el paso 1 (obs.marker): Estás en pausa: la marca no entró.", ErrorCodes.MarkerPaused, Trigger.Hotkey, PausedRunId),
    ];

    private static ActionFinishedView SampleFinished() => new(
        "marca-fin", PausedRunId, Trigger.Hotkey, false, 1, ObsMarker.TypeId,
        new Failure(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."), 12,
        [new StepReport(StepOutcome.Failed, "Estás en pausa: la marca no entró.")]);

    private static StepTypeView Marker() => new(
        ObsMarker.TypeId,
        "Marcar capítulo",
        "OBS",
        [
            new Field("marker", "Tipo de marca", FieldKind.Option, Options: ["short", "inicio", "fin", "sacar", "largo"]),
            new Field("label", "Etiqueta", FieldKind.Text, Optional: true),
        ]);
}
