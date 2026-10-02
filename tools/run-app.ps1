# ==============================================================================
# LocalFileAgent v2 — Development & Desktop Runner Script
# Launches the standalone desktop application
# ==============================================================================

[CmdletBinding()]
param(
    [switch]$FromSource
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($FromSource) {
    Write-Host "Starting LocalFileAgent.App from source..." -ForegroundColor Cyan
    $appProj = Join-Path $repoRoot "src\LocalFileAgent.App\LocalFileAgent.App.csproj"
    dotnet run --project $appProj -c Debug
} else {
    $distExe = Join-Path $repoRoot "dist\LocalFileAgent\LocalFileAgent.App.exe"
    if (-not (Test-Path $distExe)) {
        Write-Host "Self-contained build not found. Running tools\publish.ps1 first..." -ForegroundColor Yellow
        & (Join-Path $PSScriptRoot "publish.ps1")
    }

    Write-Host "Launching LocalFileAgent v2 Desktop Application..." -ForegroundColor Green
    Start-Process -FilePath $distExe -WorkingDirectory (Split-Path $distExe)
}
