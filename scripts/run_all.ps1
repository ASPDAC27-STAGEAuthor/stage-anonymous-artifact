[CmdletBinding()]
param(
    [switch]$SkipBootstrap,
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$SkipFigures
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot
$Output = Join-Path $Root 'output'
$Logs = Join-Path $Output 'logs'
New-Item -ItemType Directory -Force -Path $Output, $Logs, (Join-Path $Output 'matplotlib') | Out-Null
Set-Location $Root

function Invoke-NativeLogged {
    param([string]$Name, [string]$Executable, [string[]]$Arguments)
    $log = Join-Path $Logs ($Name + '.log')
    Write-Host "==> $Name"
    if ($Executable -eq 'dotnet') {
        & $Executable @Arguments
        $exitCode = $LASTEXITCODE
        [IO.File]::WriteAllText($log, "Exit code: $exitCode`n.NET output was streamed directly to the console on Windows.`n", [Text.UTF8Encoding]::new($false))
    } else {
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $log
        $exitCode = $LASTEXITCODE
    }
    if ($exitCode -ne 0) { throw "$Name failed with exit code $exitCode. See $log" }
}

$VenvPython = Join-Path $Root '.venv\Scripts\python.exe'
if (-not $SkipBootstrap) {
    if (-not (Test-Path $VenvPython)) {
        Invoke-NativeLogged 'python-venv' 'python' @('-m', 'venv', '.venv')
    }
    Invoke-NativeLogged 'pip-upgrade' $VenvPython @('-m', 'pip', 'install', '--upgrade', 'pip')
    Invoke-NativeLogged 'pip-requirements' $VenvPython @('-m', 'pip', 'install', '-r', 'requirements.txt')
} elseif (-not (Test-Path $VenvPython)) {
    $VenvPython = 'python'
}

$env:PYTHONHASHSEED = '0'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MPLCONFIGDIR = Join-Path $Output 'matplotlib'
Invoke-NativeLogged 'environment' $VenvPython @('scripts/check_environment.py')
Invoke-NativeLogged 'anonymization' $VenvPython @('scripts/anonymization_scan.py')
Invoke-NativeLogged 'manifest' $VenvPython @('scripts/build_manifest.py', '--verify')

if (-not $SkipBuild) {
    Write-Host '==> dotnet-restore'
    dotnet restore STAGE.sln --disable-build-servers -m:1
    if ($LASTEXITCODE -ne 0) { throw "dotnet-restore failed with exit code $LASTEXITCODE" }
    [IO.File]::WriteAllText((Join-Path $Logs 'dotnet-restore.log'), "Exit code: 0`n.NET output was streamed directly to the console on Windows.`n", [Text.UTF8Encoding]::new($false))

    Write-Host '==> dotnet-build'
    dotnet build STAGE.sln -c Release --no-restore --disable-build-servers -m:1
    if ($LASTEXITCODE -ne 0) { throw "dotnet-build failed with exit code $LASTEXITCODE" }
    [IO.File]::WriteAllText((Join-Path $Logs 'dotnet-build.log'), "Exit code: 0`n.NET output was streamed directly to the console on Windows.`n", [Text.UTF8Encoding]::new($false))
}

if (-not $SkipTests) {
    Write-Host '==> tests-golden'
    dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group golden
    if ($LASTEXITCODE -ne 0) { throw "tests-golden failed with exit code $LASTEXITCODE" }
    [IO.File]::WriteAllText((Join-Path $Logs 'tests-golden.log'), "Exit code: 0`nSeven exact-cycle golden tests passed; output was streamed directly to the console.`n", [Text.UTF8Encoding]::new($false))

    Write-Host '==> tests-paper'
    dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group paper
    if ($LASTEXITCODE -ne 0) { throw "tests-paper failed with exit code $LASTEXITCODE" }
    [IO.File]::WriteAllText((Join-Path $Logs 'tests-paper.log'), "Exit code: 0`nThirty-one paper-claim tests passed; output was streamed directly to the console.`n", [Text.UTF8Encoding]::new($false))
}

Invoke-NativeLogged 'frozen-data' $VenvPython @('scripts/validate_frozen.py')
if (-not $SkipFigures) {
    Invoke-NativeLogged 'paper-figures' $VenvPython @('experiments/aspdac/scripts/plot_submitted_figures_20260719.py')
    Invoke-NativeLogged 'figure-verification' $VenvPython @('scripts/verify_figures.py')
}

$summary = @{
    status = 'pass'
    golden_tests = -not $SkipTests
    paper_tests = -not $SkipTests
    frozen_data = $true
    figures = -not $SkipFigures
    output = 'output/'
} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $Output 'run_summary.json'), $summary + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Artifact validation passed. Outputs: $Output"
