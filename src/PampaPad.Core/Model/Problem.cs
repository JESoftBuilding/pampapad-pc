namespace PampaPad.Core;

/// <summary><c>Error</c> frena el guardado y <c>Warning</c> no (02 §10). La carga los tolera a los dos.</summary>
public enum Severity
{
    Error,
    Warning,
}

/// <summary>
/// Un problema de la configuración. <c>Where</c> es la ubicación en config.json, como <c>pages[0].keys[2].hotkey</c>,
/// o vacía si el problema es del archivo entero. Un problema rompe sólo lo que toca.
/// </summary>
public sealed record Problem(string Where, string Code, string Message, Severity Severity);

/// <summary>
/// Códigos de los problemas de configuración. No son ErrorCodes: dicen qué está mal en el archivo, no por qué falló una acción.
/// Los problemas de carga usan además ErrorCodes.ConfigUnreadable y ConfigNewer, y la fuente de atajos, ErrorCodes.HotkeyTaken.
/// </summary>
public static class ProblemCodes
{
    public const string FieldMissing = "field_missing";
    public const string PositionInvalid = "position_invalid";
    public const string PositionRepeated = "position_repeated";
    public const string IdInvalid = "id_invalid";
    public const string IdRepeated = "id_repeated";
    public const string PageNotFound = "page_not_found";
    public const string ActionNotFound = "action_not_found";
    public const string StepTypeUnknown = "step_type_unknown";
    public const string ConditionUnknown = "condition_unknown";
    public const string ParameterInvalid = "parameter_invalid";
    public const string HotkeyInvalid = "hotkey_invalid";
    public const string HotkeyRepeated = "hotkey_repeated";
    public const string ShortLabelInvalid = "short_label_invalid";
    public const string LibraryMissing = "library_missing";
    public const string ConfigMissing = "config_missing";

    /// <summary>Los de un script en la pantalla Scripts (05 §4.3): sin ficha, ficha ilegible, entrada que no existe, contrato más nuevo y extensión sin intérprete.</summary>
    public const string ManifestMissing = "manifest_missing";
    public const string ManifestUnreadable = "manifest_unreadable";
    public const string EntryMissing = "entry_missing";
    public const string ContractNewer = "contract_newer";
    public const string NoInterpreter = "no_interpreter";

    /// <summary>Problema 10 de 02 §10: una tecla para o pausa con el toque. Parar y pausar van en onHold, que pide mantener.</summary>
    public const string HoldRequired = "hold_required";

    /// <summary>Problema 12 de 02 §10: obs.request con uno de los dos pedidos de atajos (Decidido 10).</summary>
    public const string RequestForbidden = "request_forbidden";
}
