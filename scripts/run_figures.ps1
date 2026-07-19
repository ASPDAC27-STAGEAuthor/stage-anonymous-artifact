[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$Python = Join-Path $Root '.venv\Scripts\python.exe'
if (-not (Test-Path $Python)) { $Python = 'python' }
$env:PYTHONHASHSEED = '0'
$env:MPLCONFIGDIR = Join-Path $Root 'output\matplotlib'
New-Item -ItemType Directory -Force -Path $env:MPLCONFIGDIR | Out-Null
& $Python experiments/aspdac/scripts/plot_submitted_figures_20260719.py
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $Python scripts/verify_figures.py
exit $LASTEXITCODE
