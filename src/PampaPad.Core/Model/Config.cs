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

    // La principal mientras OBS no está abierto (02 §7, regla 9). Va después de MainPage para salir en su lugar (02 §3, regla 8).
    public string? MainPageWithoutObs { get; init; }

    public IReadOnlyList<Page>? Pages { get; init; }

    public IReadOnlyList<Action>? Actions { get; init; }

    public IReadOnlyList<HotkeyBinding>? Hotkeys { get; init; }

    public IReadOnlyList<PreflightCheck>? Preflight { get; init; }

    public ModuleKey? ModuleKey { get; init; }

    public ConsoleSettings? Console { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];

    /// <summary>
    /// La página principal de ese momento (02 §7, regla 9): con OBS abierto, <c>mainPage</c> o, si falta, la primera de <c>pages</c>; cerrado,
    /// <c>mainPageWithoutObs</c> si nombra una página de teclas que existe. Si no, lo mismo que abierto: el problema 4 no deja la consola sin teclas.
    /// </summary>
    public string? ResolveMainPage(bool obsOpen) =>
        !obsOpen && FindPage(MainPageWithoutObs) is { } withoutObs && Dispatcher.IsKeys(withoutObs)
            ? MainPageWithoutObs
            : MainPage ?? Pages.Indexed().Select(page => page.Item.Id).FirstOrDefault(id => id is not null);

    public Page? FindPage(string? id) =>
        id is null ? null : Pages.Indexed().Select(page => page.Item).FirstOrDefault(page => page.Id == id);

    public Action? FindAction(string? id) =>
        id is null ? null : Actions.Indexed().Select(action => action.Item).FirstOrDefault(action => action.Id == id);
}

/// <summary>
/// La tecla del módulo: <c>layer</c> es la página que rige mientras se la mantiene y <c>doubleTap</c>, la acción del doble toque
/// (plan de la 1.3.0, decisión 8). Se construye con la consola.
/// </summary>
public sealed record ModuleKey
{
    public string? Layer { get; init; }

    public string? DoubleTap { get; init; }

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
