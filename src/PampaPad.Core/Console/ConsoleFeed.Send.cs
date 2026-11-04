namespace PampaPad.Core;

/// <summary>La parte de ConsoleFeed que escribe: cada mensaje, lo último que salió de cada uno y la pausa cada 7 líneas.</summary>
public sealed partial class ConsoleFeed
{
    private async Task<bool> SendSettingsAsync(string line, DateTimeOffset now, CancellationToken ct)
    {
        if (!await SendAsync(line, now, ct).ConfigureAwait(false))
        {
            return false;
        }

        _settingsSent++;
        _settingsAt = now;
        return true;
    }

    private async Task<bool> SendScreenAsync(ScreenFrame screen, DateTimeOffset now, CancellationToken ct)
    {
        if (!await SendAsync(ConsoleProtocol.ScreenStateLine(screen), now, ct).ConfigureAwait(false))
        {
            return false;
        }

        _lastScreen = screen;
        _screenAt = now;
        return true;
    }

    /// <summary>Todas, o las parejas que cambiaron más la que toca rotar cada segundo.</summary>
    private async Task<bool> SendPairsAsync(ConsoleFrame frame, bool all, DateTimeOffset now, CancellationToken ct)
    {
        var rotate = !all && now - _labelAt >= LabelEvery ? _rotation++ % PairCount : -1;
        for (var pair = 0; pair < PairCount; pair++)
        {
            KeyFace a = frame.Key(2 * pair), b = frame.Key((2 * pair) + 1);
            if (!all && pair != rotate && a == _lastKeys[2 * pair] && b == _lastKeys[(2 * pair) + 1])
            {
                continue;
            }

            if (!await SendAsync(ConsoleProtocol.KeyLabelsLine(frame.Screen.Page, (2 * pair) + 1, a, b), now, ct).ConfigureAwait(false))
            {
                return false;
            }

            _lastKeys[2 * pair] = a;
            _lastKeys[(2 * pair) + 1] = b;
        }

        if (all || rotate >= 0)
        {
            _labelAt = now;
        }

        return true;
    }

    /// <summary>Los dos, o sólo el que cambió. El ref de la línea de escena y del renglón chico es la versión, 1 (WireMessages.h:121).</summary>
    private async Task<bool> SendTextsAsync(ConsoleFrame frame, bool all, DateTimeOffset now, CancellationToken ct)
    {
        if (all || frame.SceneLine != _lastScene)
        {
            if (!await SendAsync(ConsoleProtocol.ScreenTextLine(0, frame.SceneLine.Tone, 1, frame.SceneLine.Text), now, ct).ConfigureAwait(false))
            {
                return false;
            }

            _lastScene = frame.SceneLine;
        }

        if (all || frame.SmallLine != _lastSmall)
        {
            if (!await SendAsync(ConsoleProtocol.ScreenTextLine(1, frame.SmallLine.Tone, 1, frame.SmallLine.Text), now, ct).ConfigureAwait(false))
            {
                return false;
            }

            _lastSmall = frame.SmallLine;
        }

        if (all)
        {
            _textAt = now;
        }

        return true;
    }

    private async Task<bool> SendStripAsync(IReadOnlyList<LedColor> strip, DateTimeOffset now, CancellationToken ct)
    {
        if (!await SendAsync(ConsoleProtocol.StripLine(strip), now, ct).ConfigureAwait(false))
        {
            return false;
        }

        _lastStrip = strip;
        _stripAt = now;
        return true;
    }

    private bool SameStrip(IReadOnlyList<LedColor> strip) => _lastStrip is not null && strip.SequenceEqual(_lastStrip);

    /// <summary>
    /// Una línea por el candado de SendAsync, con la pausa de 10 ms cada 7. Si falla, la consola se desenchufó o se trabó: no se anota
    /// (lo anota la lectura al perder el enlace) y se calla hasta un enlace nuevo, o hasta QuietAfterFailure, y entonces manda todo.
    /// </summary>
    private async Task<bool> SendAsync(string line, DateTimeOffset now, CancellationToken ct)
    {
        if (_inBurst == Burst)
        {
            await Task.Delay(Pause, _clock, ct).ConfigureAwait(false);
            _inBurst = 0;
        }

        var result = await _send(line, ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            _quietUntil = now + QuietAfterFailure;
            _sendAll = 1;
            return false;
        }

        _inBurst++;
        return true;
    }
}
