# Publica PampaPad: pagina (ui) -> dotnet publish -> rutas -> prueba de humo -> vpk pack -> (con -Upload) vpk upload.
# Uso: powershell -ExecutionPolicy Bypass -File build\publish.ps1 -Version 0.1.0
# Con -Upload crea la version en GitHub: eso lo hace solo el workflow publish, con una etiqueta v*.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$repoUrl = 'https://github.com/JESoftBuilding/pampapad-pc'
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish'
$releaseDir = Join-Path $artifacts 'releases'

# El ejecutable de arranque de la raiz de instalacion se llama como el titulo mas ".exe" (medido en E0-6).
# StartWithWindows apunta ahi: InstallLayout.LauncherName es este valor mas ".exe", y InstallLayoutTests lo controla.
$packTitle = 'PampaPad'

function Invoke-Dotnet {
    param([string]$Name, [string[]]$Arguments)
    Write-Host "== $Name"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name fallo con codigo $LASTEXITCODE"
    }
}

Set-Location $root

# vpk y el paquete Velopack van en la misma version (05, seccion 8).
[xml]$packages = Get-Content (Join-Path $root 'Directory.Packages.props')
$library = ($packages.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'Velopack' }).Version
$manifest = Get-Content (Join-Path $root '.config\dotnet-tools.json') -Raw | ConvertFrom-Json
$tool = $manifest.tools.vpk.version
if ($library -ne $tool) {
    throw "El paquete Velopack ($library) y vpk ($tool) tienen que ser la misma version"
}

if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Invoke-Dotnet 'tool restore' @('tool', 'restore')

# La pagina (01, seccion 2: "ui -> publish -> humo -> vpk"). PampaPad.App.csproj la copia a ui\ al lado del ejecutable, que es la carpeta
# que mapea el host virtual de WebView2 (01, seccion 5). Si falta, la prueba de humo sale con 4.
$pnpm = Get-Command 'pnpm.cmd' -ErrorAction SilentlyContinue
if ($null -eq $pnpm) {
    $pnpm = Get-Command 'pnpm' -ErrorAction Stop
}

Write-Host '== ui'
& $pnpm.Source -C (Join-Path $root 'ui') install --frozen-lockfile
if ($LASTEXITCODE -ne 0) {
    throw "pnpm install fallo con codigo $LASTEXITCODE"
}

& $pnpm.Source -C (Join-Path $root 'ui') build
if ($LASTEXITCODE -ne 0) {
    throw "pnpm build fallo con codigo $LASTEXITCODE"
}

# Dependiente del framework (Decidido 23): el instalador pone .NET y WebView2 solo si faltan.
# dotnet publish compila como el CI (ContinuousIntegrationBuild, en Directory.Build.props): rutas relativas al repo.
Invoke-Dotnet 'publish' @(
    'publish', 'src/PampaPad.App/PampaPad.App.csproj',
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'false',
    "-p:Version=$Version",
    '-o', $publishDir)

# Ninguna DLL publicada lleva la ruta de la carpeta donde se compilo: la del programa de prueba de la etapa 0 la llevaba.
Write-Host '== rutas'
$leaks = @(Get-ChildItem $publishDir -Filter 'PampaPad*.dll' |
    Select-String -Pattern $root -SimpleMatch -List |
    ForEach-Object { $_.Filename })
if ($leaks.Count -gt 0) {
    throw "Llevan la ruta $root adentro: $($leaks -join ', ')"
}

# Prueba de humo sobre la carpeta publicada (01, seccion 7). PampaPad.exe es de ventanas:
# PowerShell no lo espera solo, por eso Start-Process con -Wait.
Write-Host '== humo'
$smokeOut = Join-Path $artifacts 'smoke.out.txt'
$smokeErr = Join-Path $artifacts 'smoke.err.txt'
$smoke = Start-Process -FilePath (Join-Path $publishDir 'PampaPad.exe') -ArgumentList '--smoke-test' `
    -Wait -PassThru -RedirectStandardOutput $smokeOut -RedirectStandardError $smokeErr
if ($smoke.ExitCode -ne 0) {
    Get-Content $smokeErr | Write-Host
    throw "La prueba de humo fallo con codigo $($smoke.ExitCode)"
}

# La version anterior hace falta para armar el paquete de diferencias. Va sin --pre, como lee el programa: las
# versiones de prueba de E0-6 son previas y no cuentan. Sin version anterior en el canal, vpk avisa
# "No full / applicable release was found to download" y sale con 0 (medido en E0-6): la primera publicacion no falla.
Write-Host '== vpk download'
$downloadArgs = @('vpk', 'download', 'github', '--repoUrl', $repoUrl, '--outputDir', $releaseDir, '--channel', 'win')
if ($env:GITHUB_TOKEN) {
    $downloadArgs += @('--token', $env:GITHUB_TOKEN)
}
& dotnet @downloadArgs
if ($LASTEXITCODE -ne 0) {
    Write-Warning "vpk download salio con codigo $LASTEXITCODE; el paquete sale sin diferencias y la actualizacion baja el completo."
}

# --runtime win-x64: sin el, vpk avisa que empaqueta como x86 (E0-6). El programa es x64.
Invoke-Dotnet 'vpk pack' @(
    'vpk', 'pack',
    '--packId', 'PampaPad',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'PampaPad.exe',
    '--packTitle', $packTitle,
    '--packAuthors', 'JESoftBuilding',
    '--framework', 'net10.0-x64-desktop,webview2',
    '--runtime', 'win-x64',
    '--channel', 'win',
    '--shortcuts', 'StartMenuRoot',
    '--outputDir', $releaseDir)

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) {
        throw 'Para subir hace falta GITHUB_TOKEN'
    }

    Invoke-Dotnet 'vpk upload' @(
        'vpk', 'upload', 'github',
        '--repoUrl', $repoUrl,
        '--outputDir', $releaseDir,
        '--channel', 'win',
        '--publish',
        '--releaseName', "PampaPad $Version",
        '--tag', "v$Version",
        '--token', $env:GITHUB_TOKEN)
}

Write-Host "Listo. Paquetes en $releaseDir"
Get-ChildItem $releaseDir | Select-Object Name, Length
