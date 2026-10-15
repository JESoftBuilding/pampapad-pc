namespace PampaPad.Core;

/// <summary>La clase de un campo de un paso. Entran de a una, con el primer paso que la necesita (01 §6).</summary>
public enum FieldKind
{
    Text,
    Option,

    /// <summary>Una escena de OBS, que se guarda como ObsRef. La trae obs.scene.</summary>
    ObsScene,

    /// <summary>Una entrada de OBS, que se guarda como ObsRef. La trae obs.mute.</summary>
    ObsInput,

    /// <summary>Un objeto JSON que va tal cual: el requestData de obs.request (01 §6, paso 5, Propuesto).</summary>
    Json,
}

/// <summary>Un campo de un tipo de paso, declarado como dato: de acá va a salir el formulario de la pantalla Acciones.</summary>
public sealed record Field(string Name, string Label, FieldKind Kind, bool Optional = false, string[]? Options = null);
