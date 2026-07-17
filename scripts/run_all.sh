#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
SKIP_BOOTSTRAP=0
for arg in "$@"; do
  case "$arg" in
    --skip-bootstrap) SKIP_BOOTSTRAP=1 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done
mkdir -p output/logs output/matplotlib
PYTHON="$ROOT/.venv/bin/python"
if [[ "$SKIP_BOOTSTRAP" -eq 0 ]]; then
  [[ -x "$PYTHON" ]] || python3 -m venv .venv
  "$PYTHON" -m pip install --upgrade pip
  "$PYTHON" -m pip install -r requirements.txt
elif [[ ! -x "$PYTHON" ]]; then
  PYTHON=python3
fi
export PYTHONHASHSEED=0
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export MPLCONFIGDIR="$ROOT/output/matplotlib"
run_logged() {
  local name="$1"; shift
  echo "==> $name"
  "$@" 2>&1 | tee "output/logs/${name}.log"
}
run_logged environment "$PYTHON" scripts/check_environment.py
run_logged anonymization "$PYTHON" scripts/anonymization_scan.py
run_logged manifest "$PYTHON" scripts/build_manifest.py --verify
run_logged dotnet-restore dotnet restore STAGE.sln --disable-build-servers -m:1
run_logged dotnet-build dotnet build STAGE.sln -c Release --no-restore --disable-build-servers -m:1
run_logged tests-golden dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group golden
run_logged tests-paper dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group paper
run_logged frozen-data "$PYTHON" scripts/validate_frozen.py
run_logged paper-figures "$PYTHON" experiments/aspdac/scripts/plot_current_overleaf_v1.py
run_logged figure-verification "$PYTHON" scripts/verify_figures.py
printf '{"status":"pass","output":"output/"}\n' > output/run_summary.json
echo "Artifact validation passed. Outputs: $ROOT/output"
