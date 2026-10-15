using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// El avisador armado como lo arma Composition, con una biblioteca en una carpeta temporal y dos canales que sólo anotan.
/// Sin config es "sin carpeta elegida". Los avisos de los canales quedan en <c>Sounds</c> y <c>Balloons</c>.
/// </summary>
internal sealed class NotifierRig : IAsyncDisposable
{
    public const string ShortAction = """{ "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""";

    private readonly TempFolder _temp = new();

    public NotifierRig(
        string? config = null,
        RecordingState recording = RecordingState.Stopped,
        bool streaming = false,
        ObsLinkState link = ObsLinkState.Ready,
        Action<Notice>? sound = null,
        Action<Notice>? balloon = null)
    {
        config ??= $$"""{ "version": 1, "actions": [ {{ShortAction}} ] }""";
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), config);
        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        Library.Load();
        State.Update(s => s with { Obs = s.Obs with { Link = link, Recording = recording, Streaming = streaming } });
        Notifier = new Notifier(State, Library, Log, sound ?? Sounds.Add, balloon ?? Balloons.Add);
    }

    public StateStore State { get; } = new();

    public Log Log { get; }

    public CoreLibrary Library { get; }

    public Notifier Notifier { get; }

    public List<Notice> Sounds { get; } = [];

    public List<Notice> Balloons { get; } = [];

    /// <summary>Una acción que terminó mal en el paso <paramref name="step"/> (cuenta desde 1) de tipo <paramref name="type"/>.</summary>
    public static ActionResult Failed(string actionId, int? step, string? type, string code, string? message, Trigger by = Trigger.Hotkey) => new(
        actionId, Guid.NewGuid(), by, false, step, type, message is null ? null : new Failure(code, message, "steps[0]"), TimeSpan.Zero, []);

    public static ActionResult Succeeded(string actionId = "marca-short") => new(
        actionId, Guid.NewGuid(), Trigger.Hotkey, true, null, null, null, TimeSpan.Zero, [new StepReport(StepOutcome.Ok)]);

    public async ValueTask DisposeAsync()
    {
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }
}
