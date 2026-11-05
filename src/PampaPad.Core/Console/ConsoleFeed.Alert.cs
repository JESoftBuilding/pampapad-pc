namespace PampaPad.Core;

/// <summary>
/// Los textos del aviso grave (plan de la 9b, decisión 10). La consola guarda uno por destino y lo dibuja sólo si su referencia es el
/// número del byte 15 (ConsoleModel.h): por eso un aviso nuevo, o el cambio a otro, manda los cuatro dos veces, y mientras dura uno cada
/// medio segundo, rotando. El ScreenState con el número ya salió antes en la misma vuelta.
/// </summary>
public sealed partial class ConsoleFeed
{
    private static readonly TimeSpan AlertTextEvery = TimeSpan.FromMilliseconds(500);

    private ConsoleAlert? _lastAlert;
    private DateTimeOffset _alertAt;
    private int _alertRotation;

    private async Task<bool> SendAlertAsync(ConsoleAlert? alert, bool all, DateTimeOffset now, CancellationToken ct)
    {
        if (alert is null)
        {
            _lastAlert = null;
            return true;
        }

        if (all || alert.Number != _lastAlert?.Number)
        {
            _lastAlert = null;
            for (int copy = 0; copy < 2; copy++)
            {
                for (int target = ConsoleAlert.FirstTarget; target < ConsoleAlert.FirstTarget + ConsoleAlert.Targets; target++)
                {
                    if (!await SendAlertTextAsync(alert, target, now, ct).ConfigureAwait(false))
                    {
                        return false;
                    }
                }
            }

            _lastAlert = alert;
            _alertAt = now;
            _alertRotation = 0;
            return true;
        }

        // Un texto que cambia, como el reloj de long_pause, sale en esta vuelta.
        for (int target = ConsoleAlert.FirstTarget; target < ConsoleAlert.FirstTarget + ConsoleAlert.Targets; target++)
        {
            if (alert.Text(target) != _lastAlert.Text(target) && !await SendAlertTextAsync(alert, target, now, ct).ConfigureAwait(false))
            {
                return false;
            }
        }

        _lastAlert = alert;
        if (now - _alertAt < AlertTextEvery)
        {
            return true;
        }

        int next = ConsoleAlert.FirstTarget + (_alertRotation++ % ConsoleAlert.Targets);
        if (!await SendAlertTextAsync(alert, next, now, ct).ConfigureAwait(false))
        {
            return false;
        }

        _alertAt = now;
        return true;
    }

    private Task<bool> SendAlertTextAsync(ConsoleAlert alert, int target, DateTimeOffset now, CancellationToken ct)
    {
        ConsoleText text = alert.Text(target);
        return SendAsync(ConsoleProtocol.ScreenTextLine(target, text.Tone, alert.Number, text.Text), now, ct);
    }
}
