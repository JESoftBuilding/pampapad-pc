using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Qué es una tecla para la consola: de eso salen su rol y cuándo va en la máscara de activas (plan de la 9a, decisión 11).</summary>
public enum KeyKind
{
    /// <summary>Sin acción: rol 0, sin letras y en la máscara de apagadas.</summary>
    Empty,

    /// <summary>Nombra una acción que no está en la biblioteca: apagada, con su palabra.</summary>
    Missing,
    Normal,
    Marker,
    Mic,
    Record,
    Pause,
    Scene,
}

/// <summary>
/// El rol de cada tecla, deducido de su acción y de la de onHold con la tabla de 04:157-159. No se guarda: se calcula en cada proyección.
/// Un paso obs.record en cualquiera de las dos manda, porque es lo que corta la toma; si no, la del toque y, si es normal, la del mantenido.
/// </summary>
public static class KeyRoles
{
    public static (KeyKind Kind, ObsRef? Scene) Of(Key key, Config config)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(config);
        if (key.Action is null && key.OnHold?.Action is null)
        {
            return (KeyKind.Empty, null);
        }

        Action? tap = config.FindAction(key.Action);
        Action? hold = config.FindAction(key.OnHold?.Action);
        if (tap is null && hold is null)
        {
            return (KeyKind.Missing, null);
        }

        if ((RecordKind(tap) ?? RecordKind(hold)) is { } record)
        {
            return (record, null);
        }

        (KeyKind Kind, ObsRef? Scene) first = Classify(tap);
        return first.Kind == KeyKind.Normal && hold is not null ? Classify(hold) : first;
    }

    /// <summary>El rol del cable: grabar y el micrófono en rojo, la pausa en ámbar (04 §7, "Del byte de estilo al dibujo").</summary>
    public static KeyRole RoleOf(KeyKind kind) => kind switch
    {
        KeyKind.Empty => KeyRole.None,
        KeyKind.Missing => KeyRole.Disabled,
        KeyKind.Marker => KeyRole.Marker,
        KeyKind.Mic or KeyKind.Record => KeyRole.Danger,
        KeyKind.Pause => KeyRole.Caution,
        KeyKind.Scene => KeyRole.Scene,
        _ => KeyRole.Normal,
    };

    private static KeyKind? RecordKind(Action? action)
    {
        foreach ((int _, Step step) in action?.Steps.Indexed() ?? [])
        {
            if (step.Type == ObsRecord.TypeId)
            {
                return Text(step, "do") is "pause" or "resume" ? KeyKind.Pause : KeyKind.Record;
            }
        }

        return null;
    }

    private static (KeyKind Kind, ObsRef? Scene) Classify(Action? action)
    {
        Step[] steps = [.. action?.Steps.Indexed().Select(step => step.Item) ?? []];
        if (steps.Length == 1 && steps[0].Type == ObsMarker.TypeId)
        {
            return (KeyKind.Marker, null);
        }

        if (steps.Length == 1 && steps[0].Type == ObsMute.TypeId)
        {
            return (KeyKind.Mic, null);
        }

        // La escena tiene que ser el último paso: ese ObsRef es el que se marca al aire.
        if (steps.Length > 0 && steps[^1].Type == ObsScene.TypeId && SceneOf(steps[^1]) is { } scene)
        {
            return (KeyKind.Scene, scene);
        }

        return (KeyKind.Normal, null);
    }

    // Un parámetro mal escrito no puede tirar la proyección: el validador ya lo marca en Teclas.
    private static ObsRef? SceneOf(Step step)
    {
        try
        {
            return step.Parameters.TryGetValue("scene", out JsonElement value) ? value.Deserialize<ObsRef>(Json.Options) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(Step step, string name) =>
        step.Parameters.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
