using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Una biblioteca con una tecla de cada rol y un OBS listo, detenido y con Cámara al aire: la base de las pruebas de la proyección.</summary>
internal static class ProjectionCase
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 20, 0, 0, TimeSpan.Zero);

    public static Config Sample { get; } = JsonSerializer.Deserialize<Config>(
        """
        {
          "version": 1,
          "mainPage": "toma",
          "pages": [
            { "id": "toma", "name": "Toma", "keys": [
              { "position": 1, "label": "Grabar", "action": "grabar", "onHold": { "action": "parar" } },
              { "position": 2, "label": "Mic", "action": "mic" },
              { "position": 3, "label": "Cámara", "action": "camara" },
              { "position": 4, "label": "Banco", "action": "banco" },
              { "position": 5, "label": "Short", "action": "marca" },
              { "position": 6, "label": "Pausa", "shortLabel": ["Seguir"], "action": "seguir", "onHold": { "action": "pausar" } },
              { "position": 7, "label": "Rota", "action": "no-existe" },
              { "position": 8, "label": "Ir al banco", "action": "script" }
            ] },
            { "id": "sala", "name": "Sala", "keys": [ { "position": 2, "label": "Luces", "action": "script" } ] }
          ],
          "actions": [
            { "id": "grabar", "steps": [ { "type": "obs.record", "do": "start" } ] },
            { "id": "parar", "steps": [ { "type": "obs.record", "do": "stop" } ] },
            { "id": "seguir", "steps": [ { "type": "obs.record", "do": "resume" } ] },
            { "id": "pausar", "steps": [ { "type": "obs.record", "do": "pause" } ] },
            { "id": "mic", "steps": [ { "type": "obs.mute", "do": "toggle" } ] },
            { "id": "camara", "steps": [ { "type": "obs.scene", "scene": "Cámara" } ] },
            { "id": "banco", "steps": [ { "type": "wait.time", "ms": 10 }, { "type": "obs.scene", "scene": { "uuid": "u-banco", "name": "Banco" } } ] },
            { "id": "marca", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "script", "steps": [ { "type": "script.run", "script": "banco" } ] }
          ]
        }
        """,
        Json.Options)!;

    public static AppState State(RecordingState recording = RecordingState.Stopped, ObsLinkState link = ObsLinkState.Ready, string page = "toma") => AppState.Initial with
    {
        ActivePage = page,
        BasePage = page,
        MainPage = "toma",
        Obs = ObsState.Initial with { Link = link, Recording = recording, SceneName = "Cámara", SceneUuid = "u-cam" },
    };

    public static ConsoleFrame Project(AppState state) => ConsoleProjection.Project(state, Sample, Now);

    public static bool Bit(int mask, int position) => (mask & (1 << (position - 1))) != 0;
}
