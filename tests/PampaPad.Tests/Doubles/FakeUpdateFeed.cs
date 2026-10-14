using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Hace de Velopack en las pruebas: contesta lo que se le cargó y cuenta las llamadas.</summary>
internal sealed class FakeUpdateFeed
{
    private readonly SemaphoreSlim _found = new(0);

    public Result<string?> FindResult { get; set; } = Result<string?>.Success(null);

    public Result DownloadResult { get; set; } = Result.Success;

    public Result ApplyResult { get; set; } = Result.Success;

    public int Finds { get; private set; }

    public int Downloads { get; private set; }

    public int Applies { get; private set; }

    public UpdateFeed AsFeed() => new(Find, Download, Apply);

    /// <summary>Espera a la próxima búsqueda. Falso si no llegó en cinco segundos.</summary>
    public Task<bool> WaitForFindAsync() => WaitForFindAsync(TimeSpan.FromSeconds(5));

    /// <summary>Espera a la próxima búsqueda con el tope dado. Falso si no llegó.</summary>
    public Task<bool> WaitForFindAsync(TimeSpan patience) => _found.WaitAsync(patience);

    private Task<Result<string?>> Find(CancellationToken ct)
    {
        Finds++;
        _found.Release();
        return Task.FromResult(FindResult);
    }

    private Task<Result> Download(CancellationToken ct)
    {
        Downloads++;
        return Task.FromResult(DownloadResult);
    }

    private Result Apply()
    {
        Applies++;
        return ApplyResult;
    }
}
