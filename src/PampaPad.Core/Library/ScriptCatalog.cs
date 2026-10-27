using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// La vista de cada fila de scripts/ para la pantalla Scripts (05 §4.3): la ficha con sus valores por defecto, el estado, los problemas,
/// quién lo usa (calculado de la configuración, no se guarda) y la última corrida, del registro. Función pura salvo por leer el disco.
/// </summary>
public static class ScriptCatalog
{
    public static IReadOnlyList<ScriptView> List(ScriptsSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Config config = sources.Library.Current;
        IReadOnlySet<string> running = sources.Runner.Running;
        return [.. ScriptFolder.Inspect(sources.Library.Folder).Select(found => View(found, config, running, sources.Runs))];
    }

    public static ScriptView View(ScriptFound found, Config config, IReadOnlySet<string> running, ScriptRuns runs)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(runs);
        ScriptManifest? manifest = found.Manifest;
        ScriptStatus status = found.WithoutManifest ? ScriptStatus.NoManifest
            : found.Problems.Count > 0 ? ScriptStatus.Problem
            : running.Contains(found.Id) ? ScriptStatus.Running
            : ScriptStatus.Ready;
        return new ScriptView(
            found.Id,
            manifest?.Name is { Length: > 0 } name ? name : found.Id,
            manifest?.Description,
            manifest?.Entry,
            manifest?.Wait ?? true,
            manifest?.TimeoutSec ?? (int)ScriptRunner.DefaultTimeout.TotalSeconds,
            manifest?.Contract,
            manifest?.Tags ?? [],
            status,
            found.Problems,
            found.Loose ? [] : UsedBy(config, found.Id),
            runs.Last(found.Id, 1).Select(Run).FirstOrDefault());
    }

    public static ScriptRunView Run(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ScriptRunView(entry.At, entry.RequestedBy, entry.End!);
    }

    /// <summary>Cada acción con un paso script.run de ese script y, por cada tecla, mantenido o atajo general que la dispara, dónde.</summary>
    public static IReadOnlyList<ScriptUse> UsedBy(Config config, string id)
    {
        ArgumentNullException.ThrowIfNull(config);
        var uses = new List<ScriptUse>();
        foreach ((int a, Action action) in config.Actions.Indexed())
        {
            int? step = action.Steps.Indexed().Where(pair => Runs(pair.Item, id)).Select(pair => (int?)pair.Index).FirstOrDefault();
            if (step is int found && action.Id is { } actionId)
            {
                uses.Add(new ScriptUse(actionId, action.Name, $"actions[{a}].steps[{found}].script", Places(config, actionId)));
            }
        }

        return uses;
    }

    private static bool Runs(Step step, string id) =>
        step.Type == ScriptRun.TypeId && step.Parameters.TryGetValue("script", out JsonElement script)
        && script.ValueKind == JsonValueKind.String && script.GetString() == id;

    private static List<ScriptPlace> Places(Config config, string actionId)
    {
        var places = new List<ScriptPlace>();
        foreach ((int p, Page page) in config.Pages.Indexed())
        {
            foreach ((int k, Key key) in page.Keys.Indexed())
            {
                if (key.Action == actionId)
                {
                    places.Add(new ScriptPlace(PlaceKind.Key, $"pages[{p}].keys[{k}].action", page.Id, page.Name, key.Position, key.Label));
                }

                if (key.OnHold?.Action == actionId)
                {
                    places.Add(new ScriptPlace(PlaceKind.Hold, $"pages[{p}].keys[{k}].onHold.action", page.Id, page.Name, key.Position, key.Label));
                }
            }
        }

        foreach ((int h, HotkeyBinding binding) in config.Hotkeys.Indexed())
        {
            if (binding.Action == actionId)
            {
                places.Add(new ScriptPlace(PlaceKind.Hotkey, $"hotkeys[{h}].action", Hotkey: binding.Hotkey));
            }
        }

        return places;
    }
}
