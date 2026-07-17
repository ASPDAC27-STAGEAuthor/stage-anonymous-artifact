[CmdletBinding()]
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
if (-not $NoBuild) {
    dotnet restore STAGE.sln --disable-build-servers -m:1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build STAGE.sln -c Release --no-restore --disable-build-servers -m:1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group golden
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group paper
exit $LASTEXITCODE
