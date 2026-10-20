namespace PampaPad.Core;

/// <summary>La clase de un campo de un paso o de una condición. Entran con la etapa que las usa (01 §6, paso 5).</summary>
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

    /// <summary>Un número entero: timeoutSec, width y height. La unidad va en el nombre del campo (02 §3, regla 3).</summary>
    Number,

    /// <summary>Un tiempo en milisegundos: el durationMs de wait.time.</summary>
    Duration,

    /// <summary>Verdadero o falso: el wait de script.run. E5-1 la suma a 01 §6 (decisión 32).</summary>
    Boolean,

    /// <summary>El id de un script de la biblioteca (02 §9). La trae script.run.</summary>
    Script,

    /// <summary>El título de una ventana de Windows. La traen wait.window, window.exists y window.sizeIs.</summary>
    Window,

    /// <summary>El id de una página, o next, previous o main. La trae page.go.</summary>
    Page,
}

/// <summary>Un campo de un tipo de paso, declarado como dato: de acá va a salir el formulario de la pantalla Acciones.</summary>
public sealed record Field(string Name, string Label, FieldKind Kind, bool Optional = false, string[]? Options = null);
