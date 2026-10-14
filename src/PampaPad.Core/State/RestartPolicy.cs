namespace PampaPad.Core;

/// <summary>Si se puede reiniciar el programa ahora y, si no, por qué. El motivo es el que se muestra al lado de "Instalar versión X".</summary>
public readonly record struct RestartVerdict(bool Allowed, string? Reason = null);

/// <summary>
/// Función pura sobre el estado (05 §8): nunca se reinicia grabando, en pausa, transmitiendo ni con una cadena en curso.
/// En la etapa 1 el estado no sabe nada de eso y siempre deja; E2-5 suma la grabación y la transmisión, y E3-7, las cadenas.
/// </summary>
public static class RestartPolicy
{
    public static RestartVerdict Evaluate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new RestartVerdict(true);
    }
}
