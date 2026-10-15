namespace PampaPad.Core;

/// <summary>Si se puede reiniciar el programa ahora y, si no, por qué. El motivo es el que se muestra al lado de "Instalar versión X".</summary>
public readonly record struct RestartVerdict(bool Allowed, string? Reason = null);

/// <summary>
/// Función pura sobre el estado (05 §8): nunca se reinicia grabando, en pausa, transmitiendo ni con una cadena en curso.
/// Mira la grabación y la transmisión de OBS (E2-5) y las acciones en curso (E3-7).
/// </summary>
public static class RestartPolicy
{
    /// <summary>Lo que se dice si un veredicto que no deja llega sin motivo. Evaluate siempre lo trae; UpdateCycle recibe la política de afuera.</summary>
    internal const string UnnamedReason = "Ahora no se puede reiniciar el programa";

    public static RestartVerdict Evaluate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Running.Count > 0)
        {
            return new RestartVerdict(false, "Hay una acción en curso");
        }

        // "Se graba, se está en pausa o se transmite" (03 §5). Con el enlace cortado vale lo último que se supo.
        if (state.Obs.Recording == RecordingState.Paused)
        {
            return new RestartVerdict(false, "La grabación está en pausa");
        }

        if (state.Obs.TakeInProgress)
        {
            return new RestartVerdict(false, "Se está grabando");
        }

        if (state.Obs.Streaming)
        {
            return new RestartVerdict(false, "Se está transmitiendo");
        }

        return new RestartVerdict(true);
    }
}
