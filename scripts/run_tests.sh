#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet restore STAGE.sln --disable-build-servers -m:1
dotnet build STAGE.sln -c Release --no-restore --disable-build-servers -m:1
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group golden
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group paper
