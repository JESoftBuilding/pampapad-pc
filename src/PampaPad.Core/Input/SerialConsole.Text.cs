namespace PampaPad.Core;

/// <summary>La parte de SerialConsole que anota las líneas de texto de la consola: el arranque, estado:, luces: y pantalla: (plan de la 9a, decisión 14).</summary>
public sealed partial class SerialConsole
{
    /// <summary>Cuántas líneas de texto se anotan por minuto. Las que pasan se cuentan y se dicen con la primera del minuto siguiente.</summary>
    public const int MaxTextLinesPerMinute = 20;

    private const int MaxTextLength = 256;
    private static readonly TimeSpan TextWindow = TimeSpan.FromMinutes(1);

    private DateTimeOffset _textWindowStart = DateTimeOffset.MinValue;
    private int _textInWindow;
    private int _textDropped;

    private void LogText(ReadOnlySpan<char> line)
    {
        if (line.IsWhiteSpace())
        {
            return;
        }

        var now = _clock.GetUtcNow();
        if (now - _textWindowStart >= TextWindow)
        {
            if (_textDropped > 0)
            {
                _log.Info(SourceId, $"consola: se tiraron {_textDropped} líneas de texto en el último minuto, por el tope de {MaxTextLinesPerMinute}.");
            }

            _textWindowStart = now;
            _textInWindow = 0;
            _textDropped = 0;
        }

        if (++_textInWindow > MaxTextLinesPerMinute)
        {
            _textDropped++;
            return;
        }

        // luces: dice que una luz no salió por radio (Radio.cpp:88): es la única que pide que alguien mire.
        var text = "consola: " + line.Trim().ToString();
        if (line.StartsWith("luces:", StringComparison.Ordinal))
        {
            _log.Warning(SourceId, text);
        }
        else
        {
            _log.Info(SourceId, text);
        }
    }
}
