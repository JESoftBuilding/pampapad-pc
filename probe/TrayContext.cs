// Prueba descartable de la etapa 0: el ícono de bandeja y el ciclo de actualización.
using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace PampaPadProbe;

internal sealed class TrayContext : ApplicationContext
{
    // Con este argumento vuelve a arrancar la versión nueva después de un --auto. No es --auto a propósito:
    // si la actualización no se aplicara, la versión vieja reintentaría sin fin.
    private const string AfterAutoArg = "--tras-auto";

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _install = new("Instalar") { Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5_000 };
    private readonly UpdateManager _manager;
    private VelopackAsset? _ready;
    private bool _busy;
    private bool _autoPending;

    public TrayContext(bool auto)
    {
        _autoPending = auto;

        // El tercer argumento va en true porque las dos versiones de esta prueba se publican como pre-release.
        // El programa de verdad va con false: sus versiones son releases comunes.
        _manager = new UpdateManager(new GithubSource(Program.RepoUrl, null, true));
        bool installed = _manager.IsInstalled;
        string version = installed ? _manager.CurrentVersion!.ToString() : "sin instalar";
        _ready = installed ? _manager.UpdatePendingRestart : null;
        Program.Log($"IsInstalled={installed}; versión={version}; pendiente={_ready?.Version.ToString() ?? "ninguna"}; auto={auto}");

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"PampaPad Probe {version}") { Enabled = false });
        menu.Items.Add("Buscar actualización ahora", null, async (_, _) => await CheckAsync());
        menu.Items.Add(_install);
        menu.Items.Add("Salir", null, (_, _) =>
        {
            Program.Log("salir elegido en el menú");
            ExitThread();
        });
        _install.Click += (_, _) => Install(null);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"PampaPad Probe {version}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        ShowReady();

        // Primer chequeo a los 5 s (con --auto, a los 0,5 s y seguido de la aplicación); después, uno por minuto.
        if (auto) _timer.Interval = 500;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = 60_000;
            if (_autoPending)
            {
                _autoPending = false;
                await AutoAsync();
            }
            else
            {
                await CheckAsync();
            }
        };
        _timer.Start();
    }

    private async Task AutoAsync()
    {
        Program.Log("auto: busca, baja y aplica sin esperar al menú");
        await CheckAsync();
        if (_ready is null)
        {
            Program.Log("auto: no hay nada para aplicar; queda en la bandeja");
            return;
        }
        Install([AfterAutoArg]);
    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!_manager.IsInstalled)
            {
                Program.Log("chequeo salteado: no es una copia instalada");
                return;
            }
            var clock = Stopwatch.StartNew();
            UpdateInfo? info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                Program.Log($"chequeo: no hay versión nueva ({clock.ElapsedMilliseconds} ms)");
                return;
            }
            VelopackAsset target = info.TargetFullRelease;
            Program.Log($"chequeo: hay {target.Version} ({clock.ElapsedMilliseconds} ms); completo={target.FileName} de {target.Size} bytes; deltas={info.DeltasToTarget?.Length ?? 0}");
            if (_ready is not null && _ready.Version == target.Version)
            {
                Program.Log($"chequeo: la {target.Version} ya estaba bajada; no se baja de nuevo");
                return;
            }

            Program.Log($"descarga: empieza {target.Version}");
            clock.Restart();
            int lastStep = -1;
            await _manager.DownloadUpdatesAsync(info, percent =>
            {
                int step = percent / 25;
                if (step <= lastStep) return;
                lastStep = step;
                Program.Log($"descarga: {percent} %");
            });
            _ready = target;
            Program.Log($"bajada {_ready.Version} en {clock.ElapsedMilliseconds} ms; no se aplica sola");
            ShowReady();
        }
        catch (Exception ex)
        {
            Program.Log($"chequeo falló: {Program.Describe(ex)}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowReady()
    {
        if (_ready is null) return;
        _install.Text = $"Instalar versión {_ready.Version}";
        _install.Enabled = true;
        _icon.ShowBalloonTip(5000, "PampaPad Probe", $"Versión {_ready.Version} lista para instalar", ToolTipIcon.Info);
    }

    private void Install(string[]? restartArgs)
    {
        if (_ready is null) return;
        Program.Log($"aplicando {_ready.Version} y reiniciando; args del reinicio=[{string.Join(' ', restartArgs ?? [])}]");
        _icon.Visible = false;
        try
        {
            _manager.ApplyUpdatesAndRestart(_ready, restartArgs);
        }
        catch (Exception ex)
        {
            Program.Log($"aplicar falló: {Program.Describe(ex)}");
            _icon.Visible = true;
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
