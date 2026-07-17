#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
PYTHON="$ROOT/.venv/bin/python"
[[ -x "$PYTHON" ]] || PYTHON=python3
export PYTHONHASHSEED=0
export MPLCONFIGDIR="$ROOT/output/matplotlib"
mkdir -p "$MPLCONFIGDIR"
"$PYTHON" experiments/aspdac/scripts/plot_current_overleaf_v1.py
"$PYTHON" scripts/verify_figures.py
