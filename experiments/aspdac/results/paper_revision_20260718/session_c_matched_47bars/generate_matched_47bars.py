#!/usr/bin/env python3
"""Generate RF-C-MATCHED-47BAR-V1 from the frozen Session A evidence bundle."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import shutil
import subprocess
from datetime import datetime, timezone
from decimal import Decimal, localcontext
from pathlib import Path
from typing import Any, Iterable

from matched_47bar_figure import draw as draw_paired_figure
from PIL import Image


TASK_ID = "RF-C-MATCHED-47BAR-V1"
SOURCE_REL = Path(
    "experiments/aspdac/results/results_first_20260718/session_a_matched_tools"
)
OUTPUT_REL = Path(
    "experiments/aspdac/results/paper_revision_20260718/session_c_matched_47bars"
)
PAPER_FIGURE_REL = Path("latex/final_revision/figure")
FIGURE_STEM = "STAGE_Figure2_Matched_47Bars"
SMALL_RESULT_RELATIVE_THRESHOLD = Decimal("1e-12")
NORMALIZATION_TOLERANCE = Decimal("1e-15")

FAMILIES = (
    "square_gemm",
    "rectangular_gemm",
    "mlp_l1",
    "mlp_l2",
    "attention_qk",
    "attention_pv",
)
FAMILY_LABELS = {
    "square_gemm": "Square GEMM",
    "rectangular_gemm": "Rectangular GEMM",
    "mlp_l1": "MLP-L1",
    "mlp_l2": "MLP-L2",
    "attention_qk": "Attention QK",
    "attention_pv": "Attention PV",
}
EXPECTED_FAMILY_COUNTS = {
    "square_gemm": 7,
    "rectangular_gemm": 8,
    "mlp_l1": 6,
    "mlp_l2": 6,
    "attention_qk": 10,
    "attention_pv": 10,
}

INPUTS = (
    ("session_a_handoff", Path("paper_handoff.md")),
    ("family_claim_matrix", Path("summary/matched_claim_matrix.csv")),
    ("matched_values", Path("summary/matched_all_points.csv")),
    ("manifest_index", Path("manifests/manifest_index.json")),
    ("existing_cycles_pdf", Path("figures/fig_matched_cycles.pdf")),
    ("existing_cycles_png", Path("figures/fig_matched_cycles.png")),
    ("existing_energy_pdf", Path("figures/fig_matched_energy.pdf")),
    ("existing_energy_png", Path("figures/fig_matched_energy.png")),
)

VALUE_COLUMNS = (
    "config_id",
    "family",
    "family_label",
    "family_position",
    "workload",
    "shape",
    "M",
    "N",
    "K",
    "precision",
    "timeloop_compute_cycles",
    "stage_matched_compute_cycles",
    "cycle_pair_sum",
    "timeloop_cycle_fraction",
    "stage_cycle_fraction",
    "timeloop_cycle_percent",
    "stage_cycle_percent",
    "cycle_signed_error",
    "cycle_absolute_error",
    "cycle_relative_error",
    "cycle_exact_match",
    "accelergy_shared_ert_energy_pj",
    "stage_matched_energy_pj",
    "energy_pair_sum_pj",
    "accelergy_energy_fraction",
    "stage_energy_fraction",
    "accelergy_energy_percent",
    "stage_energy_percent",
    "energy_signed_error_pj",
    "energy_absolute_error_pj",
    "energy_relative_error",
    "energy_exact_match",
    "cycle_small_result_flag",
    "energy_small_result_flag",
    "cycle_zero_component_flag",
    "energy_zero_component_flag",
    "normalization_status",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def repo_relative(path: Path, repo_root: Path) -> str:
    return path.resolve().relative_to(repo_root.resolve()).as_posix()


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8", newline="") as stream:
        return list(csv.DictReader(stream))


def write_csv(path: Path, fieldnames: Iterable[str], rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(fieldnames), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def parse_decimal(value: str, case_id: str, field: str) -> Decimal:
    try:
        parsed = Decimal(value)
    except Exception as exc:
        raise ValueError(f"{case_id}: invalid {field}: {value!r}") from exc
    if not parsed.is_finite():
        raise ValueError(f"{case_id}: non-finite {field}: {value!r}")
    if parsed < 0:
        raise ValueError(f"{case_id}: negative {field}: {value!r}")
    return parsed


def decimal_fixed(value: Decimal) -> str:
    return format(value, "f")


def decimal_scientific(value: Decimal) -> str:
    return format(value, ".18E")


def bool_text(value: bool) -> str:
    return "True" if value else "False"


def find_repo_root(explicit: str | None) -> Path:
    if explicit:
        root = Path(explicit).resolve()
        if not (root / "VisualHardwareAiCoDesignSimulator.sln").exists():
            raise FileNotFoundError(f"Not the repository root: {root}")
        return root
    for candidate in Path(__file__).resolve().parents:
        if (candidate / "VisualHardwareAiCoDesignSimulator.sln").exists():
            return candidate
    raise FileNotFoundError("Could not locate VisualHardwareAiCoDesignSimulator.sln")


def validate_source_bundle(source: Path) -> tuple[list[dict[str, str]], list[dict[str, Any]]]:
    for _, relative in INPUTS:
        path = source / relative
        if not path.is_file():
            raise FileNotFoundError(path)

    handoff = (source / "paper_handoff.md").read_text(encoding="utf-8")
    if "Status: EVIDENCE_COMPLETE" not in handoff or "All 47" not in handoff:
        raise ValueError("Session A handoff is not the expected 47-case complete evidence")

    claim_rows = read_csv(source / "summary/matched_claim_matrix.csv")
    matrix_counts = {row["family"]: int(row["case_count"]) for row in claim_rows}
    if matrix_counts != EXPECTED_FAMILY_COUNTS:
        raise ValueError(f"Unexpected claim matrix family counts: {matrix_counts}")
    for row in claim_rows:
        count = int(row["case_count"])
        for gate in (
            "mac_exact_passes",
            "cycle_exact_passes",
            "access_exact_passes",
            "energy_exact_passes",
        ):
            if int(row[gate]) != count:
                raise ValueError(f"Claim matrix gate failed for {row['family']}:{gate}")

    manifest = json.loads((source / "manifests/manifest_index.json").read_text(encoding="utf-8"))
    artifacts = manifest.get("artifacts", [])
    if len(artifacts) != int(manifest.get("artifact_count_excluding_index", -1)):
        raise ValueError("manifest_index.json artifact count is inconsistent")
    manifest_hashes = {item["path"]: item["sha256"] for item in artifacts}

    input_rows: list[dict[str, Any]] = []
    for role, relative in INPUTS:
        path = source / relative
        actual = sha256(path)
        recorded = ""
        manifest_match = "not_applicable"
        if relative.as_posix() != "manifests/manifest_index.json":
            recorded = manifest_hashes.get(relative.as_posix(), "")
            if not recorded:
                raise ValueError(f"Input absent from manifest index: {relative.as_posix()}")
            manifest_match = bool_text(recorded == actual)
            if recorded != actual and role not in {"existing_cycles_pdf", "existing_energy_pdf"}:
                raise ValueError(f"Input hash differs from manifest: {relative.as_posix()}")
        input_rows.append(
            {
                "role": role,
                "path": relative.as_posix(),
                "bytes": path.stat().st_size,
                "sha256": actual,
                "manifest_recorded_sha256": recorded,
                "manifest_match": manifest_match,
            }
        )

    points = read_csv(source / "summary/matched_all_points.csv")
    if len(points) != 47:
        raise ValueError(f"Expected 47 matched rows, found {len(points)}")
    if len({row["case_id"] for row in points}) != 47:
        raise ValueError("Matched rows contain duplicate case_id values")
    return points, input_rows


def sort_points(points: list[dict[str, str]]) -> list[dict[str, str]]:
    family_rank = {family: index for index, family in enumerate(FAMILIES)}
    unknown = sorted({row["family"] for row in points} - set(FAMILIES))
    if unknown:
        raise ValueError(f"Unknown workload families: {unknown}")
    return sorted(
        points,
        key=lambda row: (
            family_rank[row["family"]],
            int(row["M"]),
            int(row["N"]),
            int(row["K"]),
            row["case_id"],
        ),
    )


def build_value_rows(points: list[dict[str, str]]) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    ordered = sort_points(points)
    parsed: list[dict[str, Any]] = []
    for source_row in ordered:
        case_id = source_row["case_id"]
        if source_row["status"] != "completed":
            raise ValueError(f"{case_id}: status is not completed")
        if source_row["cycle_exact_match"] != "True" or source_row["energy_exact_match"] != "True":
            raise ValueError(f"{case_id}: matched gate is not true")
        tl_cycles = parse_decimal(source_row["timeloop_compute_cycles"], case_id, "Timeloop cycles")
        stage_cycles = parse_decimal(source_row["stage_compute_cycles"], case_id, "STAGE cycles")
        accelergy = parse_decimal(
            source_row["accelergy_shared_ert_energy_pj"], case_id, "Accelergy energy"
        )
        stage_energy = parse_decimal(
            source_row["stage_shared_ert_energy_pj"], case_id, "STAGE energy"
        )
        cycle_total = tl_cycles + stage_cycles
        energy_total = accelergy + stage_energy
        if cycle_total == 0:
            raise ZeroDivisionError(f"{case_id}: both cycle values are zero")
        if energy_total == 0:
            raise ZeroDivisionError(f"{case_id}: both energy values are zero")
        parsed.append(
            {
                "source": source_row,
                "tl_cycles": tl_cycles,
                "stage_cycles": stage_cycles,
                "accelergy": accelergy,
                "stage_energy": stage_energy,
                "cycle_total": cycle_total,
                "energy_total": energy_total,
            }
        )

    max_cycle_total = max(item["cycle_total"] for item in parsed)
    max_energy_total = max(item["energy_total"] for item in parsed)
    rows: list[dict[str, Any]] = []
    family_positions = {family: 0 for family in FAMILIES}
    cycle_small_cases: list[str] = []
    energy_small_cases: list[str] = []
    cycle_zero_cases: list[str] = []
    energy_zero_cases: list[str] = []

    with localcontext() as context:
        context.prec = 60
        for index, item in enumerate(parsed, start=1):
            source_row = item["source"]
            family = source_row["family"]
            family_positions[family] += 1
            config_id = f"C{index:02d}"

            cycle_base_fraction = item["tl_cycles"] / item["cycle_total"]
            cycle_stage_fraction = item["stage_cycles"] / item["cycle_total"]
            energy_base_fraction = item["accelergy"] / item["energy_total"]
            energy_stage_fraction = item["stage_energy"] / item["energy_total"]
            for metric, base_fraction, stage_fraction in (
                ("cycle", cycle_base_fraction, cycle_stage_fraction),
                ("energy", energy_base_fraction, energy_stage_fraction),
            ):
                normalized_error = abs((base_fraction + stage_fraction) - Decimal(1))
                if normalized_error > NORMALIZATION_TOLERANCE:
                    raise ArithmeticError(
                        f"{source_row['case_id']}: {metric} normalized sum error {normalized_error}"
                    )

            cycle_signed = item["stage_cycles"] - item["tl_cycles"]
            energy_signed = Decimal(source_row["energy_absolute_error_pj"])
            if not energy_signed.is_finite():
                raise ValueError(f"{source_row['case_id']}: non-finite source energy error")
            cycle_relative = abs(cycle_signed) / item["tl_cycles"] if item["tl_cycles"] else Decimal("Infinity")
            energy_relative = abs(Decimal(source_row["energy_relative_error"]))
            if not energy_relative.is_finite():
                raise ValueError(f"{source_row['case_id']}: non-finite source relative energy error")
            cycle_small = item["cycle_total"] < max_cycle_total * SMALL_RESULT_RELATIVE_THRESHOLD
            energy_small = item["energy_total"] < max_energy_total * SMALL_RESULT_RELATIVE_THRESHOLD
            cycle_zero = item["tl_cycles"] == 0 or item["stage_cycles"] == 0
            energy_zero = item["accelergy"] == 0 or item["stage_energy"] == 0
            if cycle_small:
                cycle_small_cases.append(config_id)
            if energy_small:
                energy_small_cases.append(config_id)
            if cycle_zero:
                cycle_zero_cases.append(config_id)
            if energy_zero:
                energy_zero_cases.append(config_id)

            row = {
                "config_id": config_id,
                "family": family,
                "family_label": FAMILY_LABELS[family],
                "family_position": family_positions[family],
                "workload": source_row["case_id"],
                "shape": f"M={source_row['M']};N={source_row['N']};K={source_row['K']}",
                "M": source_row["M"],
                "N": source_row["N"],
                "K": source_row["K"],
                "precision": source_row["precision"],
                "timeloop_compute_cycles": source_row["timeloop_compute_cycles"],
                "stage_matched_compute_cycles": source_row["stage_compute_cycles"],
                "cycle_pair_sum": decimal_fixed(item["cycle_total"]),
                "timeloop_cycle_fraction": format(cycle_base_fraction, ".18f"),
                "stage_cycle_fraction": format(cycle_stage_fraction, ".18f"),
                "timeloop_cycle_percent": format(cycle_base_fraction * 100, ".15f"),
                "stage_cycle_percent": format(cycle_stage_fraction * 100, ".15f"),
                "cycle_signed_error": decimal_fixed(cycle_signed),
                "cycle_absolute_error": decimal_fixed(abs(cycle_signed)),
                "cycle_relative_error": decimal_scientific(cycle_relative),
                "cycle_exact_match": bool_text(cycle_signed == 0),
                "accelergy_shared_ert_energy_pj": source_row["accelergy_shared_ert_energy_pj"],
                "stage_matched_energy_pj": source_row["stage_shared_ert_energy_pj"],
                "energy_pair_sum_pj": decimal_fixed(item["energy_total"]),
                "accelergy_energy_fraction": format(energy_base_fraction, ".18f"),
                "stage_energy_fraction": format(energy_stage_fraction, ".18f"),
                "accelergy_energy_percent": format(energy_base_fraction * 100, ".15f"),
                "stage_energy_percent": format(energy_stage_fraction * 100, ".15f"),
                "energy_signed_error_pj": decimal_fixed(energy_signed),
                "energy_absolute_error_pj": decimal_fixed(abs(energy_signed)),
                "energy_relative_error": decimal_scientific(energy_relative),
                "energy_exact_match": source_row["energy_exact_match"],
                "cycle_small_result_flag": bool_text(cycle_small),
                "energy_small_result_flag": bool_text(energy_small),
                "cycle_zero_component_flag": bool_text(cycle_zero),
                "energy_zero_component_flag": bool_text(energy_zero),
                "normalization_status": "ok",
            }
            rows.append(row)

    actual_counts = {family: sum(row["family"] == family for row in rows) for family in FAMILIES}
    if actual_counts != EXPECTED_FAMILY_COUNTS:
        raise ValueError(f"Unexpected sorted family counts: {actual_counts}")

    cycle_values = [Decimal(row["timeloop_compute_cycles"]) for row in rows] + [
        Decimal(row["stage_matched_compute_cycles"]) for row in rows
    ]
    energy_values = [Decimal(row["accelergy_shared_ert_energy_pj"]) for row in rows] + [
        Decimal(row["stage_matched_energy_pj"]) for row in rows
    ]
    audit = {
        "threshold_relative_to_max_pair_sum": decimal_scientific(SMALL_RESULT_RELATIVE_THRESHOLD),
        "cycle_min_raw": decimal_fixed(min(cycle_values)),
        "cycle_max_raw": decimal_fixed(max(cycle_values)),
        "energy_min_raw_pj": decimal_fixed(min(energy_values)),
        "energy_max_raw_pj": decimal_fixed(max(energy_values)),
        "cycle_small_result_cases": cycle_small_cases,
        "energy_small_result_cases": energy_small_cases,
        "cycle_zero_component_cases": cycle_zero_cases,
        "energy_zero_component_cases": energy_zero_cases,
        "zero_denominator_cases": [],
        "silent_drop_count": 0,
    }
    return rows, audit


def draw_figure(repo_root: Path, png_path: Path, pdf_path: Path) -> None:
    draw_paired_figure(repo_root, png_path, pdf_path)

def output_hashes(repo_root: Path, paths: dict[str, Path]) -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    for role, path in paths.items():
        if path.is_file():
            result[role] = {
                "path": repo_relative(path, repo_root),
                "bytes": path.stat().st_size,
                "sha256": sha256(path),
            }
    return result


def write_handoff(repo_root: Path, output: Path, status: dict[str, Any]) -> None:
    input_hash_rows = read_csv(output / "input_sha256.csv")
    audit = status["small_result_audit"]
    inspection = status["visual_inspection"]
    inspection_notes = inspection.get("notes", "Visual inspection has not yet been recorded.")
    input_lines = "\n".join(
        f"- `{row['path']}` - `{row['sha256']}`" for row in input_hash_rows
    )
    output_lines = "\n".join(
        f"- `{item['path']}` - `{item['sha256']}`"
        for item in status["output_sha256"].values()
    )
    warnings = status["input_integrity"]["warnings"]
    warning_lines = (
        "\n".join(
            f"- {item['path']}: current {item['actual_sha256']}; manifest {item['manifest_sha256']}."
            for item in warnings
        )
        if warnings
        else "- None."
    )
    script_rel = repo_relative(output / "generate_matched_47bars.py", repo_root)
    pdf_rel = repo_relative(output / f"{FIGURE_STEM}.pdf", repo_root)
    inspection_prefix = repo_relative(
        output / "visual_inspection" / f"{FIGURE_STEM}_pdf_render", repo_root
    )
    command = f"python {script_rel} --repo-root ."
    render_command = f"python {script_rel} --repo-root . --render-pdf-check"
    record_command = (
        f'python {script_rel} --repo-root . --record-visual-inspection '
        '"PASS: inspect the generated PNG and the 300-dpi PDF render before recording this assertion."'
    )
    text = f"""# RF-C matched 47-bar paper handoff

Task: {TASK_ID}
Status: {status['status']}
Generated UTC: {status['generated_utc']}

## Result

The frozen Session A evidence produced one two-panel Figure 2 candidate with 47 paired configurations per panel. Each panel uses log-scale raw-value markers plus a separate absolute-relative-error strip normalized to the frozen 1e-15 tolerance. Panel (a) contains only Timeloop and STAGE matched compute cycles. Panel (b) contains only Accelergy and STAGE matched shared-ERT energy. No access counts are plotted. This encoding supersedes the earlier 100% stacked-bar trial after explicit user visual approval.

The configuration index is family-sorted as Square GEMM (C01-C07), Rectangular GEMM (C08-C15), MLP-L1 (C16-C21), MLP-L2 (C22-C27), Attention QK (C28-C37), and Attention PV (C38-C47). Exact workload and shape mappings, raw values, normalized proportions, and signed/absolute/relative errors are in `matched_47bar_values.csv`.

## Numerical checks

- Retained cases: 47/47; silently dropped: {audit['silent_drop_count']}.
- Cycle equality: 47/47 exact; maximum absolute cycle error: {status['metrics']['max_cycle_absolute_error']}.
- Energy gate: 47/47 pass; maximum absolute energy error: {status['metrics']['max_energy_absolute_error_pj']} pJ; maximum relative energy error: {status['metrics']['max_energy_relative_error']}.
- Zero denominators: {len(audit['zero_denominator_cases'])}; zero-valued components: cycles={len(audit['cycle_zero_component_cases'])}, energy={len(audit['energy_zero_component_cases'])}.
- Small-result threshold: pair sum below {audit['threshold_relative_to_max_pair_sum']} of the metric maximum; flagged cycles={audit['cycle_small_result_cases']}, energy={audit['energy_small_result_cases']}.
- Raw range audit: cycles {audit['cycle_min_raw']} to {audit['cycle_max_raw']}; energy {audit['energy_min_raw_pj']} to {audit['energy_max_raw_pj']} pJ.

## Source integrity note

All numerical inputs, the Session A handoff, the claim matrix, and the existing PNG references match manifest_index.json. The following pre-existing PDF visual references do not match their recorded manifest hashes; they are not used as numerical inputs and were not modified by this task:

{warning_lines}

## Visual inspection

Status: {inspection['status']}

{inspection_notes}

## Deliverables

- `matched_47bar_values.csv` - C01-C47 index and all plotted values.
- `generate_matched_47bars.py` - deterministic generator and validation gates.
- `matched_47bar_figure.py` - approved paired-marker and tolerance-strip renderer.
- `{FIGURE_STEM}.pdf` - paper candidate PDF.
- `{FIGURE_STEM}.png` - 300-dpi PNG ({status['figure']['png_width_px']}x{status['figure']['png_height_px']} px).
- `input_sha256.csv` - frozen input hashes and manifest checks.
- `status.json` - machine-readable completion and validation state.

Paper candidate copies are written only to `latex/final_revision/figure/{FIGURE_STEM}.pdf` and `.png`. No TeX file or pre-existing Figure 2 asset is modified.

## Exact reproduction commands

Run from the repository root:

```powershell
{command}
{render_command}
# Inspect both PNG files, then record the manual visual result:
{record_command}
```

The first command regenerates the index, hashes, figure, candidate copies, handoff, and status. The second renders the PDF at 300 dpi for an independent layout check. The third records the operator's completed visual inspection; its note must describe what was actually checked.

## Input SHA-256

{input_lines}

## Output SHA-256

{output_lines}
"""
    (output / "paper_handoff.md").write_text(text, encoding="utf-8")


def generate(repo_root: Path) -> None:
    source = repo_root / SOURCE_REL
    output = repo_root / OUTPUT_REL
    paper_figure_dir = repo_root / PAPER_FIGURE_REL
    output.mkdir(parents=True, exist_ok=True)
    (output / "visual_inspection").mkdir(parents=True, exist_ok=True)
    paper_figure_dir.mkdir(parents=True, exist_ok=True)

    points, input_rows = validate_source_bundle(source)
    rows, audit = build_value_rows(points)
    values_path = output / "matched_47bar_values.csv"
    input_hash_path = output / "input_sha256.csv"
    png_path = output / f"{FIGURE_STEM}.png"
    pdf_path = output / f"{FIGURE_STEM}.pdf"
    candidate_png = paper_figure_dir / f"{FIGURE_STEM}.png"
    candidate_pdf = paper_figure_dir / f"{FIGURE_STEM}.pdf"

    write_csv(values_path, VALUE_COLUMNS, rows)
    write_csv(
        input_hash_path,
        ("role", "path", "bytes", "sha256", "manifest_recorded_sha256", "manifest_match"),
        input_rows,
    )
    draw_figure(repo_root, png_path, pdf_path)
    shutil.copy2(png_path, candidate_png)
    shutil.copy2(pdf_path, candidate_pdf)

    with Image.open(png_path) as image:
        width, height = image.size
        dpi = image.info.get("dpi", (0.0, 0.0))
    if (width, height) != (2175, 1500):
        raise ValueError(f"Unexpected PNG dimensions: {(width, height)}")
    if not (299.0 <= float(dpi[0]) <= 301.0 and 299.0 <= float(dpi[1]) <= 301.0):
        raise ValueError(f"PNG is not 300 dpi: {dpi}")
    if pdf_path.read_bytes()[:5] != b"%PDF-":
        raise ValueError("Generated PDF does not have a PDF header")
    if sha256(png_path) != sha256(candidate_png) or sha256(pdf_path) != sha256(candidate_pdf):
        raise ValueError("Paper candidate copies do not match generated figures")

    cycle_abs = [Decimal(row["cycle_absolute_error"]) for row in rows]
    energy_abs = [Decimal(row["energy_absolute_error_pj"]) for row in rows]
    energy_rel = [Decimal(row["energy_relative_error"]) for row in rows]
    core_paths = {
        "generator": Path(__file__).resolve(),
        "figure_generator": output / "matched_47bar_figure.py",
        "values_index": values_path,
        "input_hashes": input_hash_path,
        "figure_pdf": pdf_path,
        "figure_png": png_path,
        "paper_candidate_pdf": candidate_pdf,
        "paper_candidate_png": candidate_png,
    }
    input_warnings = [
        {
            "path": row["path"],
            "actual_sha256": row["sha256"],
            "manifest_sha256": row["manifest_recorded_sha256"],
        }
        for row in input_rows
        if row["manifest_match"] == "False"
    ]
    status = {
        "schema_version": "rf-c-matched-47bar-status-1.0",
        "task_id": TASK_ID,
        "status": "EVIDENCE_GENERATED_VISUAL_CHECK_PENDING",
        "generated_utc": utc_now(),
        "repo_root": repo_root.as_posix(),
        "source_bundle": SOURCE_REL.as_posix(),
        "output_directory": OUTPUT_REL.as_posix(),
        "case_count": len(rows),
        "family_counts": EXPECTED_FAMILY_COUNTS,
        "input_integrity": {
            "numerical_and_png_inputs_match_manifest": all(
                row["manifest_match"] != "False"
                for row in input_rows
                if row["role"] not in {"existing_cycles_pdf", "existing_energy_pdf"}
            ),
            "warnings": input_warnings,
        },
        "metrics": {
            "cycle_exact_matches": sum(row["cycle_exact_match"] == "True" for row in rows),
            "energy_exact_matches": sum(row["energy_exact_match"] == "True" for row in rows),
            "max_cycle_absolute_error": decimal_fixed(max(cycle_abs)),
            "max_energy_absolute_error_pj": decimal_fixed(max(energy_abs)),
            "max_energy_relative_error": decimal_scientific(max(energy_rel)),
        },
        "small_result_audit": audit,
        "normalization": {
            "index_csv_definition": "reference_value / (reference_value + stage_value) and stage_value / the same sum",
            "figure_uses_pair_normalization": False,
            "error_strip_tolerance": decimal_scientific(NORMALIZATION_TOLERANCE),
            "all_47_cycle_pairs_valid": True,
            "all_47_energy_pairs_valid": True,
        },
        "figure": {
            "panels": {
                "a": "Timeloop and STAGE matched compute cycles plus cycle-error strip",
                "b": "Accelergy and STAGE matched shared-ERT energy plus energy-error strip",
            },
            "encoding": "paired raw-value markers on log scale with absolute-relative-error tolerance strips",
            "configurations_per_panel": 47,
            "png_dpi": [float(dpi[0]), float(dpi[1])],
            "png_width_px": width,
            "png_height_px": height,
            "pdf_header_valid": True,
        },
        "scope_guards": {
            "paper_tex_modified": False,
            "preexisting_figure2_overwritten": False,
            "other_parallel_task_directories_modified": False,
        },
        "visual_inspection": {
            "status": "PENDING",
            "notes": "Render the PDF at 300 dpi and inspect it together with the generated PNG.",
        },
        "output_sha256": output_hashes(repo_root, core_paths),
    }
    write_json(output / "status.json", status)
    write_handoff(repo_root, output, status)
    print(json.dumps({"task_id": TASK_ID, "status": status["status"], "output": str(output)}, indent=2))


def render_pdf_check(repo_root: Path) -> None:
    output = repo_root / OUTPUT_REL
    pdf_path = output / f"{FIGURE_STEM}.pdf"
    render_path = output / "visual_inspection" / f"{FIGURE_STEM}_pdf_render.png"
    bundled = (
        Path.home()
        / ".cache/codex-runtimes/codex-primary-runtime/dependencies/native/poppler/Library/bin/pdftoppm.exe"
    )
    executable = str(bundled) if bundled.is_file() else shutil.which("pdftoppm")
    if not executable:
        raise FileNotFoundError("pdftoppm is unavailable")
    render_path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            executable,
            "-png",
            "-r",
            "300",
            "-singlefile",
            str(pdf_path),
            str(render_path.with_suffix("")),
        ],
        check=True,
    )
    with Image.open(render_path) as rendered:
        if rendered.size != (2175, 1500):
            raise ValueError(f"Unexpected PDF render dimensions: {rendered.size}")
    print(json.dumps({"pdf_render": str(render_path), "dimensions": [2175, 1500]}, indent=2))


def record_visual_inspection(repo_root: Path, notes: str) -> None:
    output = repo_root / OUTPUT_REL
    status_path = output / "status.json"
    if not status_path.is_file():
        raise FileNotFoundError("Generate the artifact before recording visual inspection")
    status = json.loads(status_path.read_text(encoding="utf-8"))
    if status.get("task_id") != TASK_ID:
        raise ValueError("status.json belongs to a different task")
    png_path = output / f"{FIGURE_STEM}.png"
    pdf_render = output / "visual_inspection" / f"{FIGURE_STEM}_pdf_render.png"
    if not pdf_render.is_file():
        raise FileNotFoundError(
            f"Missing independent PDF render: {pdf_render}. Run the pdftoppm command in paper_handoff.md."
        )
    with Image.open(png_path) as png_image, Image.open(pdf_render) as rendered_image:
        png_dimensions = list(png_image.size)
        render_dimensions = list(rendered_image.size)
    if png_dimensions != [2175, 1500] or render_dimensions != [2175, 1500]:
        raise ValueError(
            f"Unexpected inspection dimensions: PNG={png_dimensions}, PDF render={render_dimensions}"
        )
    if not notes.strip().upper().startswith("PASS:"):
        raise ValueError("Visual inspection note must start with 'PASS:'")
    status["status"] = "COMPLETE"
    status["completed_utc"] = utc_now()
    status["visual_inspection"] = {
        "status": "PASS",
        "recorded_utc": utc_now(),
        "notes": notes.strip(),
        "checked_png": repo_relative(png_path, repo_root),
        "checked_pdf_render": repo_relative(pdf_render, repo_root),
        "png_dimensions": png_dimensions,
        "pdf_render_dimensions": render_dimensions,
        "checks": {
            "two_metric_separated_panels": True,
            "47_paired_configurations_visible_per_panel": True,
            "configuration_index_labels_removed": True,
            "six_family_groups_visible": True,
            "legend_and_axes_legible": True,
            "no_clipping_or_overlap_observed": True,
        },
    }
    status["output_sha256"]["pdf_visual_inspection_render"] = {
        "path": repo_relative(pdf_render, repo_root),
        "bytes": pdf_render.stat().st_size,
        "sha256": sha256(pdf_render),
    }
    write_json(status_path, status)
    write_handoff(repo_root, output, status)
    print(json.dumps({"task_id": TASK_ID, "status": status["status"]}, indent=2))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", help="Repository root; auto-detected when omitted")
    parser.add_argument(
        "--render-pdf-check",
        action="store_true",
        help="Render the generated PDF at 300 dpi for independent visual inspection",
    )
    parser.add_argument(
        "--record-visual-inspection",
        metavar="PASS_NOTE",
        help="Record a completed manual PNG/PDF-render inspection without regenerating artifacts",
    )
    args = parser.parse_args()
    repo_root = find_repo_root(args.repo_root)
    if args.render_pdf_check:
        render_pdf_check(repo_root)
    elif args.record_visual_inspection:
        record_visual_inspection(repo_root, args.record_visual_inspection)
    else:
        generate(repo_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
