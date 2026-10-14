# Lo mismo que corre el CI: restaurar, compilar con advertencias como error, formato y pruebas.
# Uso, desde cualquier carpeta: powershell -ExecutionPolicy Bypass -File build\verify.ps1

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-Dotnet {
    param([string]$Name, [string[]]$Arguments)
    Write-Host "== $Name"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name fallo con codigo $LASTEXITCODE"
    }
}

Invoke-Dotnet 'restore' @('restore', 'PampaPad.slnx')
Invoke-Dotnet 'build' @('build', 'PampaPad.slnx', '-c', 'Release', '--no-restore')
Invoke-Dotnet 'format' @('format', 'PampaPad.slnx', '--verify-no-changes', '--no-restore')
Invoke-Dotnet 'test' @('test', 'PampaPad.slnx', '-c', 'Release', '--no-build')

Write-Host 'Verificacion en verde.'
