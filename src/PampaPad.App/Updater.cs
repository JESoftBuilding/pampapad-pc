using PampaPad.Core;
using Velopack;
using Velopack.Sources;

namespace PampaPad.App;

/// <summary>El adaptador de Velopack sobre GitHub Releases. Sin reglas: las tiene UpdateCycle, en el núcleo y con prueba.</summary>
internal sealed class Updater
{
    private const string RepoUrl = "https://github.com/JESoftBuilding/pampapad-pc";

    private readonly Log _log;
    private readonly UpdateManager _manager;
    private readonly UpdateCycle _cycle;
    private UpdateInfo? _found;

    public Updater(StateStore state, LocalSettings settings, Log log, TimeProvider clock)
    {
        _log = log;
        // Repo público: se lee sin credencial (Decidido 21). El tercer argumento deja afuera las versiones previas, como las
        // v0.0.1 y v0.0.2 de la prueba E0-6. El canal no se pasa: queda el del paquete instalado, win (Medido en E0-6).
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
        _cycle = new UpdateCycle(
            state,
            settings,
            log,
            clock,
            new UpdateFeed(FindAsync, DownloadAsync, ApplyOnExit),
            RestartPolicy.Evaluate);
    }

    /// <summary>Falso en la copia de desarrollo y en la carpeta publicada sin instalar: ahí no hay actualizaciones.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public Task KeepCheckingAsync(CancellationToken ct)
    {
        if (!IsInstalled)
        {
            _log.Info("update", "Copia sin instalar: no busca actualizaciones");
            return Task.CompletedTask;
        }

        return _cycle.KeepCheckingAsync(ct);
    }

    public Task<Result> CheckAsync(CancellationToken ct) => _cycle.CheckAsync(ct);

    public Task<Result> DownloadAndApplyAsync(CancellationToken ct) => _cycle.DownloadAndApplyAsync(ct);

    private async Task<Result<string?>> FindAsync(CancellationToken ct)
    {
        try
        {
            _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            return Result<string?>.Success(_found?.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            return Result<string?>.Fail(ErrorCodes.NoResponse, ex.Message);
        }
    }

    private async Task<Result> DownloadAsync(CancellationToken ct)
    {
        if (_found is null)
        {
            return Result.Fail(ErrorCodes.InvalidData, "No hay una versión buscada para bajar");
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_found, null, ct).ConfigureAwait(false);
            return Result.Success;
        }
        catch (OperationCanceledException)
        {
            return Result.Fail(ErrorCodes.Cancelled, "Se canceló la descarga");
        }
        catch (Exception ex)
        {
            return Result.Fail(ErrorCodes.NoResponse, ex.Message);
        }
    }

    private Result ApplyOnExit()
    {
        if (_found is null)
        {
            return Result.Fail(ErrorCodes.InvalidData, "No hay una versión bajada para instalar");
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_found.TargetFullRelease, false, true);
            return Result.Success;
        }
        catch (Exception ex)
        {
            return Result.Fail(ErrorCodes.InternalError, ex.Message);
        }
    }
}
