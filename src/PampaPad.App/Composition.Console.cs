using PampaPad.Core;

namespace PampaPad.App;

// La consola por cable, aparte por el tope de 300 líneas (01 §1): se lee de ella y, desde la etapa 8b, se le escriben las luces.
internal sealed partial class Composition
{
    /// <summary>
    /// Las fuentes de entrada van después del Dispatcher (01 §4.3). Sin puerto en local.json no hay consola, que es como arranca la copia
    /// de desarrollo (05 §10). La prueba de humo no abre puertos. El puerto se lee una vez, acá: cambiarlo en Ajustes se aplica al volver
    /// a abrir PampaPad (plan de la 8b, decisión 3).
    /// </summary>
    private void ComposeConsole(string? consolePort, StateStore state, Dispatcher dispatcher, Log log, TimeProvider clock, bool headless)
    {
        if (headless || string.IsNullOrWhiteSpace(consolePort))
        {
            return;
        }

        state.Update(s => s with { Console = new ConsoleLink(ConsoleLinkState.Waiting, consolePort) });
        _console = new SerialConsole(SerialPorts.Opener(consolePort), state, log, clock);

        // La consola entra por la misma puerta que los atajos, y el Dispatcher le aplica la página que rige.
        dispatcher.Attach(_console);
    }

    /// <summary>El SendToConsole de las cadenas: lo pide lights al correr, cuando la consola ya está armada o se sabe que no hay.</summary>
    private Task<Result> SendToConsoleAsync(string line, CancellationToken ct) =>
        _console is { } console
            ? console.SendAsync(line, ct)
            : Task.FromResult(Result.Fail(ErrorCodes.ConsoleDisconnected, "No hay puerto de la consola: se elige en Ajustes, en Conexión con la consola."));
}
