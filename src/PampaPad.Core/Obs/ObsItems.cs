using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Una escena o una entrada de OBS como la lista OBS: su uuid y su nombre de hoy (03 §1).</summary>
public sealed record ObsItem(string Uuid, string Name);

/// <summary>
/// Las listas de escenas y entradas que refleja el estado, y cómo se encuentra en ellas lo que nombra un ObsRef (02 §4.8; 03 §1): por uuid
/// y, si no está, por nombre. OBS no deja dos fuentes con el mismo nombre, así que por nombre hay una o ninguna. Todo es puro, y lo que no
/// cambia nada devuelve la misma lista: así el StateStore no numera un estado igual al anterior.
/// </summary>
public static class ObsItems
{
    public static ObsItem? Resolve(ObsRef wanted, IReadOnlyList<ObsItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (wanted.Uuid is { Length: > 0 } uuid && items.FirstOrDefault(item => item.Uuid == uuid) is { } byUuid)
        {
            return byUuid;
        }

        return string.IsNullOrEmpty(wanted.Name) ? null : items.FirstOrDefault(item => item.Name == wanted.Name);
    }

    /// <summary>La lista de una respuesta (GetSceneList, GetInputList). Lo que venga sin nombre o sin uuid se saltea.</summary>
    public static IReadOnlyList<ObsItem> Read(JsonElement data, string list, string nameField, string uuidField) =>
    [
        .. ObsMessages.Items(data, list)
            .Select(item => (Name: ObsMessages.Text(item, nameField), Uuid: ObsMessages.Text(item, uuidField)))
            .Where(item => item.Name is not null && item.Uuid is not null)
            .Select(item => new ObsItem(item.Uuid!, item.Name!)),
    ];

    public static IReadOnlyList<ObsItem> With(IReadOnlyList<ObsItem> items, ObsItem added)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(added);
        return items.Any(item => item.Uuid == added.Uuid) ? items : [.. items, added];
    }

    public static IReadOnlyList<ObsItem> Without(IReadOnlyList<ObsItem> items, string? uuid)
    {
        ArgumentNullException.ThrowIfNull(items);
        return uuid is null || items.All(item => item.Uuid != uuid) ? items : [.. items.Where(item => item.Uuid != uuid)];
    }

    public static IReadOnlyList<ObsItem> Renamed(IReadOnlyList<ObsItem> items, string? uuid, string? name)
    {
        ArgumentNullException.ThrowIfNull(items);
        return uuid is null || name is null || items.All(item => item.Uuid != uuid)
            ? items
            : [.. items.Select(item => item.Uuid == uuid ? item with { Name = name } : item)];
    }
}
