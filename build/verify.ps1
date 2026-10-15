# Lo mismo que corre el CI (01, seccion 7). Primero la pagina, en ui/: instalar, lint, tipos, Vitest, Playwright y armarla. Despues .NET:
# restaurar, compilar con advertencias como error, formato y pruebas. La pagina va primero para que la compilacion la copie al lado
# del ejecutable.
# Uso, desde cualquier carpeta: powershell -ExecutionPolicy Bypass -File build\verify.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Dotnet {
    param([string]$Name, [string[]]$Arguments)
    Write-Host "== $Name"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name fallo con codigo $LASTEXITCODE"
    }
}

# pnpm.cmd antes que pnpm: en una PowerShell con los scripts apagados, pnpm es pnpm.ps1 y no corre. En el CI esta uno de los dos.
$pnpm = Get-Command 'pnpm.cmd' -ErrorAction SilentlyContinue
if ($null -eq $pnpm) {
    $pnpm = Get-Command 'pnpm' -ErrorAction Stop
}

function Invoke-Pnpm {
    param([string]$Name, [string[]]$Arguments)
    Write-Host "== ui: $Name"
    & $pnpm.Source -C (Join-Path $root 'ui') @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ui: $Name fallo con codigo $LASTEXITCODE"
    }
}

Invoke-Pnpm 'install' @('install', '--frozen-lockfile')
Invoke-Pnpm 'lint' @('lint')
Invoke-Pnpm 'tsc' @('typecheck')
Invoke-Pnpm 'vitest' @('test')
Invoke-Pnpm 'playwright' @('e2e')
Invoke-Pnpm 'build' @('build')

Invoke-Dotnet 'restore' @('restore', 'PampaPad.slnx')
Invoke-Dotnet 'build' @('build', 'PampaPad.slnx', '-c', 'Release', '--no-restore')
Invoke-Dotnet 'format' @('format', 'PampaPad.slnx', '--verify-no-changes', '--no-restore')
Invoke-Dotnet 'test' @('test', 'PampaPad.slnx', '-c', 'Release', '--no-build')

Write-Host 'Verificacion en verde.'
