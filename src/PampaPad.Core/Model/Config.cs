using System.Text.Json;
using System.Text.Json.Serialization;

namespace PampaPad.Core;

/// <summary>
/// La raíz de <c>config.json</c>, versión 1 del esquema (02 §4.1). Es la forma del JSON: no hay otro modelo.
/// Lo que el programa no conoce queda en <c>Extra</c> y se vuelve a escribir igual (02 §3, regla 1).
/// </summary>
public sealed record Config
{
    public int? Version { get; init; }

    public string? MainPage { get; init; }

    public IReadOnlyList<Page>? Pages { get; init; }

    public IReadOnlyList<Action>? Actions { get; init; }

    public IReadOnlyList<HotkeyBinding>? Hotkeys { get; init; }

    public IReadOnlyList<PreflightCheck>? Preflight { get; init; }

    public ModuleKey? ModuleKey { get; init; }

    public ConsoleSettings? Console { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];

    /// <summary>La página principal: <c>mainPage</c> o, si falta, la primera de <c>pages</c>.</summary>
    public string? ResolveMainPage() => MainPage ?? Pages.Indexed().Select(page => page.Item.Id).FirstOrDefault(id => id is not null);

    public Page? FindPage(string? id) =>
        id is null ? null : Pages.Indexed().Select(page => page.Item).FirstOrDefault(page => page.Id == id);

    public Action? FindAction(string? id) =>
        id is null ? null : Actions.Indexed().Select(action => action.Item).FirstOrDefault(action => action.Id == id);
}

/// <summary>La tecla del módulo: <c>layer</c> es la página que rige mientras se la mantiene. Se construye con la consola.</summary>
public sealed record ModuleKey
{
    public string? Layer { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];
}

public static class ModelLists
{
    /// <summary>Recorre una lista del modelo con el índice que tiene en el archivo. Aguanta la lista ausente y los elementos <c>null</c>.</summary>
    public static IEnumerable<(int Index, T Item)> Indexed<T>(this IReadOnlyList<T>? list)
        where T : class
    {
        if (list is null)
        {
            yield break;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is { } item)
            {
                yield return (i, item);
            }
        }
    }
}
