namespace PampaPad.Core;

/// <summary>La clase de un campo de un paso. Entran de a una, con el primer paso que la necesita (01 §6).</summary>
public enum FieldKind
{
    Text,
    Option,
}

/// <summary>Un campo de un tipo de paso, declarado como dato: de acá va a salir el formulario de la pantalla Acciones.</summary>
public sealed record Field(string Name, string Label, FieldKind Kind, bool Optional = false, string[]? Options = null);
