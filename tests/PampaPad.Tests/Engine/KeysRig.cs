using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>Un Dispatcher de verdad sobre una biblioteca de dos teclas, con reloj falso y un paso que anota lo que corrió.</summary>
internal sealed class KeysRig : IAsyncDisposable
{
    public const string Config = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [
            {
              "id": "grabacion",
              "name": "Grabación",
              "keys": [
                { "position": 5, "label": "Short", "action": "marca-short" },
                { "position": 12, "label": "Tramo", "action": "marca-inicio", "onHold": { "action": "marca-fin" } }
              ]
            }
          ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-inicio", "name": "Marca: inicio", "steps": [ { "type": "obs.marker", "marker": "inicio" } ] },
            { "id": "marca-fin", "name": "Marca: fin", "steps": [ { "type": "obs.marker", "marker": "fin" } ] }
          ]
        }
        """;

    private readonly TempFolder _temp = new();
    private readonly Channel<(string Marker, Trigger By)> _ran = Channel.CreateUnbounded<(string Marker, Trigger By)>();
    private int _runs;

    public KeysRig()
    {
        Log = new Log(_temp.Combine("log"), Clock);
        Marker.Behavior = (step, invocation, _) =>
        {
            Interlocked.Increment(ref _runs);
            _ran.Writer.TryWrite((step.Parameters["marker"].GetString() ?? string.Empty, invocation.RequestedBy));
            return Task.FromResult(new StepResult(null));
        };
        var gestures = new Gestures(Clock, Gestures.HoldFrom(null), Gestures.DefaultFreshness);
        Dispatcher = new Dispatcher(LoadLibrary(_temp), Store, new Executor([Marker], Clock), gestures, Log);
    }

    public FakeTimeProvider Clock { get; } = new();

    public StateStore Store { get; } = new();

    public FakeStep Marker { get; } = new("obs.marker");

    public Log Log { get; }

    public Dispatcher Dispatcher { get; }

    /// <summary>Cuántas veces corrió el paso hasta ahora.</summary>
    public int Runs => Volatile.Read(ref _runs);

    /// <summary>Escribe la biblioteca de prueba en una carpeta temporal y la carga.</summary>
    public static CoreLibrary LoadLibrary(TempFolder temp)
    {
        var folder = temp.Combine("library");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "config.json"), Config);
        var library = new CoreLibrary(folder, temp.Combine("backup"));
        library.Load();
        return library;
    }

    /// <summary>La próxima marca que corrió y quién la pidió. El tope es sólo para que una prueba rota no cuelgue el CI.</summary>
    public async Task<(string Marker, Trigger By)> NextRun()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _ran.Reader.ReadAsync(limit.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }
}
