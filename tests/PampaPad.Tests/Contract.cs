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
        bool write = Environment.GetEnvironmentVariable("PAMPAPAD_WRITE_SAMPLES") == "1";

        // Hallazgo E6-7: en modo escritura la prueba compara el archivo con lo que acaba de escribir y no puede fallar; en el CI dejaría el contrato sin control.
        Assert.False(write && Environment.GetEnvironmentVariable("CI") == "true", "PAMPAPAD_WRITE_SAMPLES=1 en el CI deja el contrato sin control.");
        if (write)
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
        using IDisposable hooks = Requests.RegisterAll(bridge, Sources(rig), Edit(rig));

        // Los de la cáscara (AppRequests) no se ven desde acá: van por RequestNames.Shell, y la prueba de humo los controla con el programa real.
        string[] windowRequests = [.. bridge.RegisteredFor(Caller.Window), .. RequestNames.Shell];

        Assert.Equal(windowRequests.Order(StringComparer.Ordinal), SampleNames("requests").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_constant_is_registered_by_the_core_or_named_as_shell()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var bridge = new Bridge(rig.State, rig.Log);
        using IDisposable hooks = Requests.RegisterAll(bridge, Sources(rig), Edit(rig));

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
        (StepTypeView[] stepTypes, ConditionTypeView[] conditionTypes) = Catalogs();
        var snapshot = new AppSnapshot("0.2.1", Migrations.CurrentVersion, config, SampleFingerprint, state, stepTypes, conditionTypes, new SettingsView(true, "COM20", null));
        Problem savedWarning = new("pages[0].keys[0].shortLabel", ProblemCodes.ShortLabelInvalid, "shortLabel lleva una o dos líneas de hasta 6 caracteres.", Severity.Warning);

        return new JsonObject
        {
            ["requests"] = new JsonObject
            {
                [RequestNames.AppSnapshot] = Pair(Envelope.Request(1, RequestNames.AppSnapshot, new NoData()), Envelope.Response(1, snapshot)),
                [RequestNames.LogRead] = Pair(Envelope.Request(2, RequestNames.LogRead, new LogReadRequest(Count: 200)), Envelope.Response(2, new LogReadResponse(entries))),
                [RequestNames.ActionCancel] = Pair(Envelope.Request(3, RequestNames.ActionCancel, new ActionCancelRequest(RunId: RunId)), Envelope.Response(3, new ActionCancelResponse(1))),
                [RequestNames.WindowReady] = Pair(Envelope.Request(4, RequestNames.WindowReady, new NoData()), Envelope.Response(4, new NoData())),
                [RequestNames.ConfigSave] = Pair(
                    Envelope.Request(5, RequestNames.ConfigSave, new ConfigSaveRequest(config, SampleFingerprint)),
                    Envelope.Response(5, new ConfigSaveResponse(SavedFingerprint, [savedWarning]))),
                [RequestNames.LibraryReload] = Pair(Envelope.Request(6, RequestNames.LibraryReload, new NoData()), Envelope.Response(6, new LibraryReloadResponse(SavedFingerprint, []))),
                [RequestNames.HotkeysSuspend] = Pair(Envelope.Request(8, RequestNames.HotkeysSuspend, new HotkeysSuspendRequest(true)), Envelope.Response(8, new NoData())),
                [RequestNames.ActionTest] = Pair(Envelope.Request(9, RequestNames.ActionTest, new ActionTestRequest(config.Actions![0])), Envelope.Response(9, new ActionTestResponse(RunId))),
                [RequestNames.WindowDirty] = Pair(Envelope.Request(11, RequestNames.WindowDirty, new WindowDirtyRequest(true)), Envelope.Response(11, new NoData())),
                [RequestNames.OptionsList] = Pair(
                    Envelope.Request(10, RequestNames.OptionsList, new OptionsListRequest(OptionsList.Scenes)),
                    Envelope.Response(10, new OptionsListResponse([new OptionItem("Cámara", "3da67a13-6704-4c87-9e09-c8be28efdf20"), new OptionItem("Banco", "900ebc25-03ef-443e-80cc-2ad8991268ff")]))),
            },
            ["events"] = new JsonObject
            {
                [EventNames.State] = Node(Envelope.Event(EventNames.State, state.Seq, state)),
                [EventNames.Log] = Node(Envelope.Event(EventNames.Log, state.Seq, entries[1])),
                [EventNames.ActionFinished] = Node(Envelope.Event(EventNames.ActionFinished, state.Seq, SampleFinished())),
                [EventNames.ConfigChanged] = Node(Envelope.Event(EventNames.ConfigChanged, state.Seq, new ConfigChangedView(config, SampleFingerprint))),
            },
            ["error"] = Node(Envelope.Error(5, new Failure(ErrorCodes.UnknownRequest, "No existe el pedido config.sav.", "request"))),

            // Un guardado que frena: el mensaje y el lugar son los del primer problema, y la lista trae todos (config_invalid).
            ["saveRejected"] = Node(Envelope.Error(7, new Failure(ErrorCodes.ConfigInvalid, "La tecla 5 de Grabación para o pausa la grabación con un toque.", "pages[0].keys[0].action")
            {
                Problems =
                [
                    new Problem("pages[0].keys[0].action", ProblemCodes.HoldRequired, "La tecla 5 de Grabación para o pausa la grabación con un toque.", Severity.Error),
                    savedWarning,
                ],
            })),
        };
    }

    private static readonly string SampleFingerprint = Fingerprints.Of("config.json de la muestra"u8);

    private static readonly string SavedFingerprint = Fingerprints.Of("config.json ya guardado"u8);

    private static StatusSources Sources(DispatcherRig rig) =>
        new("0.0.0", rig.State, rig.Library, rig.Dispatcher, rig.Log, [rig.Marker], [], () => new SettingsView(false, null, null));

    private static EditSources Edit(DispatcherRig rig) =>
        new(rig.Library, rig.State, rig.Log, rig.Executor, [rig.Marker], [], rig.Dispatcher, new FakeWindows(), rig.Hotkeys);

    private static IEnumerable<string> SampleNames(string section) => Samples()[section]!.AsObject().Select(pair => pair.Key);

    private static JsonObject Pair(string request, string response) => new() { ["request"] = Node(request), ["response"] = Node(response) };

    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    private static Config SampleConfig() => JsonSerializer.Deserialize<Config>(
        """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            { "id": "grabacion", "name": "Grabación", "keys": [
              { "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" },
              { "position": 6, "label": "Inicio", "action": "marca-inicio", "hotkey": "Ctrl+Shift+Alt+F6" }
            ] }
          ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-inicio", "name": "Marca: inicio", "steps": [ { "type": "obs.marker", "marker": "inicio" } ] }
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
        new(At.AddSeconds(12), LogLevel.Info, "scripts", "bancocam terminó con 0 en 2.4 s: cámara lista", null, Trigger.Key, RunId, "bancocam", new ScriptEnd(true, 0, 2400, "cámara lista")),
    ];

    private static ActionFinishedView SampleFinished() => new(
        "marca-fin", PausedRunId, Trigger.Hotkey, false, 1, ObsMarker.TypeId,
        new Failure(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."), 12,
        [new StepReport(StepOutcome.Failed, "Estás en pausa: la marca no entró.")]);

    // Los tipos de paso y de condición salen de los catálogos reales y no escritos a mano: una clase de campo nueva cambia
    // contract.samples.json y contract.test.ts la ve (hallazgo E6-7; plan de la etapa 5, E5-23a). El ObsClient no se conecta.
    private static (StepTypeView[] Steps, ConditionTypeView[] Conditions) Catalogs()
    {
        using var temp = new TempFolder();
        var state = new StateStore();
        var log = new Log(temp.Combine("log"), TimeProvider.System);
        var obs = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, state, log, TimeProvider.System);
        var library = new PampaPad.Core.Library(temp.Combine("biblioteca"), temp.Combine("backup"));
        try
        {
            Chains chains = TestChains.For(library, log);
            return (
                [.. Catalog.Build(obs, state, log, TimeProvider.System, chains).Select(StepTypeView.From)],
                [.. Conditions.Build(chains.Windows, state, obs).Select(ConditionTypeView.From)]);
        }
        finally
        {
            obs.DisposeAsync().AsTask().GetAwaiter().GetResult();
            state.DisposeAsync().AsTask().GetAwaiter().GetResult();
            log.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
