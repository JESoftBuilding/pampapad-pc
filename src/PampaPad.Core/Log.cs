using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace PampaPad.Core;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Una línea del registro. En la etapa 3 suma quién disparó la ejecución y su id (05 §9).</summary>
public sealed record LogEntry(DateTimeOffset At, LogLevel Level, string Module, string Message, string? Code = null);

/// <summary>Lo último que pasó: las últimas 500 entradas en memoria y un archivo por día, una línea JSON por entrada, 14 días (05 §9).</summary>
public sealed class Log : IAsyncDisposable
{
    public const int MemoryLimit = 500;
    public const int DaysKept = 14;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _recent = new();
    private readonly Channel<Item> _pending = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private DateOnly _prunedOn;
    private bool _diskFailed;

    public Log(string folder, TimeProvider clock)
    {
        _folder = folder;
        _clock = clock;
        _writer = Task.Run(WriteLoopAsync);
    }

    public static string FileName(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";

    public void Write(LogLevel level, string module, string message, string? code = null)
    {
        var entry = new LogEntry(_clock.GetLocalNow(), level, module, message, code);
        Remember(entry);
        _pending.Writer.TryWrite(new Item(entry, null));
    }

    public void Info(string module, string message) => Write(LogLevel.Info, module, message);

    public void Warning(string module, string message, string? code = null) => Write(LogLevel.Warning, module, message, code);

    public void Error(string module, string message, string? code = null) => Write(LogLevel.Error, module, message, code);

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
        {
            return _recent.ToArray();
        }
    }

    /// <summary>Termina cuando todo lo escrito hasta acá llegó al disco.</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.Writer.TryWrite(new Item(null, done)))
        {
            done.TrySetResult();
        }

        return done.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var item in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Entry is not null)
            {
                Append(item.Entry);
            }

            item.Flushed?.TrySetResult();
        }
    }

    private void Append(LogEntry entry)
    {
        try
        {
            var day = DateOnly.FromDateTime(entry.At.DateTime);
            Directory.CreateDirectory(_folder);
            if (day != _prunedOn)
            {
                Prune(day);
                _prunedOn = day;
            }

            var line = JsonSerializer.Serialize(entry, Json.Compact) + "\n";
            File.AppendAllText(Path.Combine(_folder, FileName(day)), line, Utf8);
            _diskFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_diskFailed)
            {
                _diskFailed = true;
                Remember(new LogEntry(entry.At, LogLevel.Error, "log", "No se pudo escribir el registro en disco: " + ex.Message));
            }
        }
    }

    private void Prune(DateOnly today)
    {
        var oldestKept = today.AddDays(1 - DaysKept);
        foreach (var file in Directory.EnumerateFiles(_folder, "*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var isOurs = DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day);
            if (isOurs && day < oldestKept)
            {
                File.Delete(file);
            }
        }
    }

    private void Remember(LogEntry entry)
    {
        lock (_gate)
        {
            _recent.Enqueue(entry);
            while (_recent.Count > MemoryLimit)
            {
                _recent.Dequeue();
            }
        }
    }

    private readonly record struct Item(LogEntry? Entry, TaskCompletionSource? Flushed);
}
