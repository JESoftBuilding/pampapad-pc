namespace PampaPad.Core;

/// <summary>
/// El menú fijo de pasos (Decidido 8): una línea por tipo, y cada uno recibe por constructor sólo lo que usa. La marca va primera.
/// Un tipo nuevo es un archivo en Steps/ y una línea acá (01 §6).
/// </summary>
public static class Catalog
{
    public static IReadOnlyList<IStepType> Build(ObsClient obs, StateStore state, Log log, TimeProvider clock) =>
    [
        new ObsMarker(obs, state),
    ];
}
