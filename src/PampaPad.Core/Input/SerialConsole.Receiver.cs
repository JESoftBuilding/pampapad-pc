namespace PampaPad.Core;

/// <summary>La parte de SerialConsole que atiende el saludo del receptor por radio (plan de la 1.4.0, R-2, decisiones 2 y 3).</summary>
public sealed partial class SerialConsole
{
    private void HandleReceiverHello(byte[] payload)
    {
        if (!ConsoleProtocol.TryDecodeReceiverHello(payload, out var hello))
        {
            CountMalformed();
            return;
        }

        var previous = _state.Current.Console.Receiver;
        var receiver = new ReceiverLink(hello.Version, hello.HearsConsole);
        _state.Update(s => s with
        {
            // Sin señal se olvida el saludo de la consola, para no mostrar datos viejos: al volver saluda sola cada 2 s y lo llena.
            Console = hello.HearsConsole
                ? s.Console with { Receiver = receiver }
                : s.Console with
                {
                    Receiver = receiver,
                    Firmware = null,
                    Capabilities = null,
                    Energy = null,
                    SettingsFingerprint = null,
                    SettingsInSync = null,
                },
        });

        // Una línea al primer saludo del enlace y después sólo los cambios: llega cada 2 s.
        if (previous is null)
        {
            var hears = hello.HearsConsole ? "escucha a la consola" : "consola sin señal";
            _log.Info(SourceId, $"El receptor saluda: firmware {hello.Version}, {hears}.");
        }
        else if (previous.HearsConsole != hello.HearsConsole)
        {
            _log.Info(SourceId, hello.HearsConsole
                ? "El receptor volvió a escuchar a la consola."
                : "El receptor dejó de escuchar a la consola: consola sin señal.");
        }
    }
}
