using System.Text.RegularExpressions;

namespace PampaPad.Core;

/// <summary>
/// Reglas puras sobre el archivo (02 §10). No mira OBS, Windows ni el catálogo de pasos:
/// los tipos de paso desconocidos y los parámetros inválidos los informa Executor.Check.
/// </summary>
public static partial class Validator
{
    public const int KeyCount = 12;
    public const int MaxIdLength = 40;

    private static readonly string[] ReservedIds = ["next", "previous", "main"];

    public static IReadOnlyList<Problem> Validate(Config config)
    {
        var found = new List<Problem>();
        if (config.Version is null)
        {
            found.Add(Missing("version", "version"));
        }

        HashSet<string> pages = Ids(config.Pages.Indexed().Select(page => (page.Index, page.Item.Id)), "pages", found);
        HashSet<string> actions = Ids(config.Actions.Indexed().Select(action => (action.Index, action.Item.Id)), "actions", found);

        if (config.MainPage is { } main && !pages.Contains(main))
        {
            found.Add(Error("mainPage", ProblemCodes.PageNotFound, $"mainPage nombra la página \"{main}\", que no existe."));
        }

        if (config.ModuleKey?.Layer is { } layer && !pages.Contains(layer))
        {
            found.Add(Error("moduleKey.layer", ProblemCodes.PageNotFound, $"moduleKey.layer nombra la página \"{layer}\", que no existe."));
        }

        Dictionary<Hotkey, string> general = GeneralHotkeys(config, actions, found);
        foreach ((int index, Page page) in config.Pages.Indexed())
        {
            CheckPage(page, $"pages[{index}]", actions, general, found);
        }

        foreach ((int index, Action action) in config.Actions.Indexed())
        {
            CheckAction(action, $"actions[{index}]", found);
        }

        foreach ((int index, PreflightCheck check) in config.Preflight.Indexed())
        {
            CheckPreflight(check, $"preflight[{index}]", found);
        }

        CheckHold(config, found);
        CheckRequests(config, found);
        return found;
    }

    // \z y no $: en .NET el $ también acepta un salto de línea al final, y "a\n" pasaría por un id válido.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex IdFormat();

    private static HashSet<string> Ids(IEnumerable<(int Index, string? Id)> items, string list, List<Problem> found)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((int index, string? id) in items)
        {
            string where = $"{list}[{index}].id";
            if (id is null)
            {
                found.Add(Missing(where, "id"));
                continue;
            }

            if (id.Length > MaxIdLength || !IdFormat().IsMatch(id))
            {
                found.Add(Error(where, ProblemCodes.IdInvalid, $"El id \"{id}\" no sirve: van minúsculas, números y guiones, hasta {MaxIdLength} caracteres."));
            }
            else if (ReservedIds.Contains(id))
            {
                found.Add(Error(where, ProblemCodes.IdInvalid, $"El id \"{id}\" está reservado: next, previous y main no se pueden usar."));
            }

            if (!seen.Add(id))
            {
                found.Add(Error(where, ProblemCodes.IdRepeated, $"El id \"{id}\" está repetido en {list}."));
            }
        }

        return seen;
    }

    private static Dictionary<Hotkey, string> GeneralHotkeys(Config config, HashSet<string> actions, List<Problem> found)
    {
        var seen = new Dictionary<Hotkey, string>();
        foreach ((int index, HotkeyBinding binding) in config.Hotkeys.Indexed())
        {
            string where = $"hotkeys[{index}]";
            if (binding.Hotkey is null)
            {
                found.Add(Error($"{where}.hotkey", ProblemCodes.FieldMissing, $"Al atajo general {index + 1} le falta la combinación."));
            }
            else
            {
                CheckHotkey(binding.Hotkey, $"{where}.hotkey", $"el atajo general {index + 1}", seen, null, found);
            }

            if (binding.Action is null)
            {
                found.Add(Error($"{where}.action", ProblemCodes.FieldMissing, $"Al atajo general {index + 1} le falta la acción."));
            }
            else
            {
                CheckActionRef(binding.Action, $"{where}.action", actions, found);
            }
        }

        return seen;
    }

    private static void CheckPage(Page page, string where, HashSet<string> actions, Dictionary<Hotkey, string> general, List<Problem> found)
    {
        if (page.Name is null)
        {
            found.Add(Missing($"{where}.name", "name"));
        }

        string pageName = page.Name ?? page.Id ?? "?";
        var positions = new HashSet<int>();
        var hotkeys = new Dictionary<Hotkey, string>();
        foreach ((int index, Key key) in page.Keys.Indexed())
        {
            string at = $"{where}.keys[{index}]";
            if (key.Position is not { } position)
            {
                found.Add(Missing($"{at}.position", "position"));
            }
            else if (position is < 1 or > KeyCount)
            {
                found.Add(Error($"{at}.position", ProblemCodes.PositionInvalid, $"position vale {position} y tiene que ir de 1 a {KeyCount}."));
            }
            else if (!positions.Add(position))
            {
                found.Add(Error($"{at}.position", ProblemCodes.PositionRepeated, $"La tecla {position} está dos veces en la página {pageName}."));
            }

            if (key.Label is null)
            {
                found.Add(Missing($"{at}.label", "label"));
            }

            if (key.Action is not null)
            {
                CheckActionRef(key.Action, $"{at}.action", actions, found);
            }

            if (key.OnHold is { } hold)
            {
                if (hold.Action is null)
                {
                    found.Add(Missing($"{at}.onHold.action", "action"));
                }
                else
                {
                    CheckActionRef(hold.Action, $"{at}.onHold.action", actions, found);
                }
            }

            if (key.Hotkey is not null)
            {
                CheckHotkey(key.Hotkey, $"{at}.hotkey", $"la tecla {key.Position} de {pageName}", hotkeys, general, found);
            }

            if (key.ShortLabel is { } lines && (lines.Count is 0 or > 2 || lines.Any(line => line is null || line.Length > 6)))
            {
                found.Add(new Problem($"{at}.shortLabel", ProblemCodes.ShortLabelInvalid, "shortLabel lleva una o dos líneas de hasta 6 caracteres.", Severity.Warning));
            }
        }
    }

    private static void CheckHotkey(string text, string where, string owner, Dictionary<Hotkey, string> scope, Dictionary<Hotkey, string>? general, List<Problem> found)
    {
        if (Hotkey.Parse(text) is not { } hotkey)
        {
            found.Add(Error(where, ProblemCodes.HotkeyInvalid, $"No entiendo el atajo \"{text}\": van de uno a tres entre Ctrl, Shift y Alt y una tecla (A a Z, 0 a 9, F1 a F24), unidos con +."));
            return;
        }

        if (scope.TryGetValue(hotkey, out string? other) || (general is not null && general.TryGetValue(hotkey, out other)))
        {
            found.Add(Error(where, ProblemCodes.HotkeyRepeated, $"{hotkey} está repetida: ya la usa {other}."));
        }
        else
        {
            scope[hotkey] = owner;
        }
    }

    private static void CheckActionRef(string id, string where, HashSet<string> actions, List<Problem> found)
    {
        if (!actions.Contains(id))
        {
            found.Add(Error(where, ProblemCodes.ActionNotFound, $"Nombra la acción \"{id}\", que no existe."));
        }
    }

    private static void CheckAction(Action action, string where, List<Problem> found)
    {
        if (action.Name is null)
        {
            found.Add(Missing($"{where}.name", "name"));
        }

        if (action.Steps is null)
        {
            found.Add(Missing($"{where}.steps", "steps"));
            return;
        }

        foreach ((int index, Step step) in action.Steps.Indexed())
        {
            if (step.Type.Length == 0)
            {
                found.Add(Missing($"{where}.steps[{index}].type", "type"));
            }

            if (step.OnlyIf is { Type.Length: 0 })
            {
                found.Add(Missing($"{where}.steps[{index}].onlyIf.type", "type"));
            }
        }
    }

    private static void CheckPreflight(PreflightCheck check, string where, List<Problem> found)
    {
        if (check.Condition is null)
        {
            found.Add(Missing($"{where}.condition", "condition"));
        }
        else if (check.Condition.Type.Length == 0)
        {
            found.Add(Missing($"{where}.condition.type", "type"));
        }

        if (check.Message is null)
        {
            found.Add(Missing($"{where}.message", "message"));
        }
    }

    private static Problem Missing(string where, string field) => Error(where, ProblemCodes.FieldMissing, $"Falta \"{field}\".");

    private static Problem Error(string where, string code, string message) => new(where, code, message, Severity.Error);
}
