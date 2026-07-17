#!/usr/bin/env python3
"""Plan, freeze, and analyze reviewer-extension P2 interventions.

This module deliberately has no run command.  It builds candidate contracts
from baseline-only raw evidence, freezes a prospective replication manifest,
and analyzes only observations that bind themselves to that frozen manifest.
"""

from __future__ import annotations

import argparse
import copy
import csv
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import yaml


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_SPEC = REPO_ROOT / "experiments/aspdac/specs/reviewer_extension_20260717/p2_interventions.yaml"
DEFAULT_PLAN = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/config/p2_intervention_plan.json"
DEFAULT_PREREG = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_intervention_preregistration.json"
DEFAULT_CANDIDATE_MANIFEST = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_candidate_manifest.json"
DEFAULT_CHECKPOINT = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_checkpoint.json"
DEFAULT_RUNNER_INPUTS = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_runner_inputs"
DEFAULT_RUNNER_OUTPUTS = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_runner_outputs"
DEFAULT_RAW = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/raw/p2_interventions"
DEFAULT_OBSERVATION_MANIFEST = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_observation_manifest.json"
DEFAULT_ANALYSIS = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_intervention_analysis.json"
DEFAULT_TRACE_GUIDED_SUMMARY = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/summary/trace_guided_interventions.csv"
DEFAULT_PREDICTION_ERROR_SUMMARY = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/summary/intervention_prediction_error.csv"
DEFAULT_INTERVENTION_FIGURE = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/figures/fig_reviewer_intervention.pdf"
DEFAULT_REPORT_MANIFEST = REPO_ROOT / "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_reporting_manifest.json"

CRITICAL_FIELDS = {
    "compute": "ComputeCriticalCycles",
    "memory": "MemoryCriticalCycles",
    "noc": "NocCriticalCycles",
    "reduction": "ReductionCriticalCycles",
    "softmax": "SoftmaxCriticalCycles",
    "conversion": "ConversionCriticalCycles",
}
REQUIRED_METRIC_FIELDS = (
    "TotalCycles",
    "ComputeServiceDemandCycles",
    "MemoryServiceDemandCycles",
    "NocServiceDemandCycles",
    *CRITICAL_FIELDS.values(),
    "DominantBottleneck",
    "CanonicalTraceSha256",
)


class ContractError(RuntimeError):
    """Raised when evidence does not satisfy the frozen P2 contract."""


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def sha256_object(value: Any) -> str:
    return sha256_text(canonical_json(value))


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def repo_path(repo_root: Path, path: Path) -> str:
    return path.resolve().relative_to(repo_root.resolve()).as_posix()


def resolve_path(repo_root: Path, value: str | Path) -> Path:
    path = Path(value)
    return path if path.is_absolute() else repo_root / path


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ContractError(f"Expected a JSON object: {path}")
    return value


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    require(bool(rows), f"Refusing to write empty CSV: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def load_spec(path: Path) -> dict[str, Any]:
    value = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ContractError(f"Expected a YAML object: {path}")
    validate_spec(value)
    return value


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def integer_metric(metrics: dict[str, Any], field: str) -> int:
    value = metrics.get(field)
    if isinstance(value, bool) or not isinstance(value, int):
        raise ContractError(f"Metric {field} must be an integer")
    require(value >= 0, f"Metric {field} must be non-negative")
    return value


def dominant_set(metrics: dict[str, Any]) -> list[str]:
    values = {category: integer_metric(metrics, field) for category, field in CRITICAL_FIELDS.items()}
    largest = max(values.values())
    return sorted(category for category, value in values.items() if value == largest)


def validate_critical_sum(metrics: dict[str, Any], context: str) -> None:
    attributed = sum(integer_metric(metrics, field) for field in CRITICAL_FIELDS.values())
    total = integer_metric(metrics, "TotalCycles")
    require(attributed == total, f"Critical attribution does not sum to TotalCycles for {context}")


def validate_interval(interval: Any, point: int, context: str) -> None:
    require(
        isinstance(interval, list)
        and len(interval) == 2
        and all(isinstance(value, int) and not isinstance(value, bool) for value in interval),
        f"{context} must be a two-integer interval",
    )
    require(interval == [point, point], f"{context} must be a singleton registered interval")


def validate_spec(spec: dict[str, Any]) -> None:
    require(spec.get("schema_version") == "stage-reviewer-p2-spec-1.0", "Unsupported P2 spec version")
    require(spec.get("classification") == "prospective_preregistered_replication", "P2 must be a prospective replication")
    disclosure = spec.get("prior_observation_disclosure", {})
    require(disclosure.get("prior_result_known") is True, "P2 must disclose that the prior result is known")
    repeats = spec.get("repeat_contract", {}).get("repeat_indices")
    require(repeats == [0, 1], "P2 requires exact repeats 0 and 1")
    pairs = spec.get("interventions")
    require(isinstance(pairs, list) and len(pairs) == 2, "P2 requires exactly two interventions")
    require(len({pair.get("pair_id") for pair in pairs}) == 2, "P2 pair IDs must be unique")
    require(
        {pair["axis"]["parameter"] for pair in pairs} == {"memory_ports", "link_bits_per_cycle"},
        "P2 axes must be memory_ports and link_bits_per_cycle",
    )
    for pair in pairs:
        pair_id = str(pair["pair_id"])
        metrics_by_role = pair.get("registered_metrics", {})
        require(set(metrics_by_role) == {"baseline", "intervention"}, f"Missing registered metrics for {pair_id}")
        for role, metrics in metrics_by_role.items():
            validate_critical_sum(metrics, f"{pair_id}/{role}/registered")
        before = metrics_by_role["baseline"]
        after = metrics_by_role["intervention"]
        prediction = pair["registered_prediction"]
        total_delta = integer_metric(after, "TotalCycles") - integer_metric(before, "TotalCycles")
        require(prediction["total_after_point"] == after["TotalCycles"], f"Total point mismatch for {pair_id}")
        require(prediction["total_delta_point"] == total_delta, f"Total delta mismatch for {pair_id}")
        validate_interval(prediction["total_after_interval"], prediction["total_after_point"], f"{pair_id} total-after interval")
        validate_interval(prediction["total_delta_interval"], prediction["total_delta_point"], f"{pair_id} total-delta interval")
        for metric_prefix, metric_field in (
            ("target_service", pair["target"]["service_metric"]),
            ("target_critical", pair["target"]["critical_metric"]),
        ):
            delta = integer_metric(after, metric_field) - integer_metric(before, metric_field)
            point_key = f"{metric_prefix}_delta_point"
            interval_key = f"{metric_prefix}_delta_interval"
            require(prediction[point_key] == delta, f"{point_key} mismatch for {pair_id}")
            validate_interval(prediction[interval_key], delta, f"{pair_id} {metric_prefix} interval")
        before_set, after_set = dominant_set(before), dominant_set(after)
        require(prediction["baseline_dominant_set"] == before_set, f"Baseline dominant set mismatch for {pair_id}")
        require(prediction["intervention_dominant_set"] == after_set, f"Intervention dominant set mismatch for {pair_id}")
        require(before["DominantBottleneck"] == before_set[0], f"Baseline tie-break mismatch for {pair_id}")
        require(after["DominantBottleneck"] == after_set[0], f"Intervention tie-break mismatch for {pair_id}")
        require(prediction["intervention_selected_dominant"] == after_set[0], f"Selected dominant mismatch for {pair_id}")


def integrity_wrap(payload: dict[str, Any]) -> dict[str, Any]:
    result = copy.deepcopy(payload)
    result["integrity"] = {
        "algorithm": "sha256",
        "canonicalization": "sorted-key compact UTF-8 JSON",
        "scope": "document excluding integrity",
        "content_sha256": sha256_object(payload),
    }
    return result


def verify_integrity(document: dict[str, Any], context: str) -> str:
    integrity = document.get("integrity")
    require(isinstance(integrity, dict), f"Missing integrity object in {context}")
    registered = integrity.get("content_sha256")
    payload = {key: value for key, value in document.items() if key != "integrity"}
    actual = sha256_object(payload)
    require(registered == actual, f"Preregistration integrity hash mismatch in {context}")
    return actual


def _final_index(repo_root: Path, spec: dict[str, Any]) -> tuple[Path, dict[str, dict[str, Any]]]:
    index_path = resolve_path(repo_root, spec["baseline_bundle"]["final_manifest_index"])
    require(index_path.is_file(), f"Missing final manifest index: {index_path}")
    index = read_json(index_path)
    files = index.get("files")
    require(isinstance(files, list), "Final manifest index has no files list")
    return index_path, {str(item["path"]): item for item in files}


def _load_baseline(
    repo_root: Path,
    spec: dict[str, Any],
    pair: dict[str, Any],
    indexed_files: dict[str, dict[str, Any]],
) -> tuple[dict[str, Any], dict[str, Any]]:
    raw_path = resolve_path(repo_root, pair["baseline"]["raw_path"])
    require(raw_path.is_file(), f"Missing baseline raw: {raw_path}")
    relative = repo_path(repo_root, raw_path)
    require(relative in indexed_files, f"Baseline raw is absent from final_manifest_index: {relative}")
    actual_sha = sha256_path(raw_path)
    require(indexed_files[relative].get("sha256") == actual_sha, f"Final-index SHA mismatch for {relative}")
    record = read_json(raw_path)
    require(record.get("status") == "completed", f"Baseline raw is not completed: {relative}")
    require(record.get("candidate_id") == pair["baseline"]["candidate_id"], f"Baseline candidate mismatch: {relative}")
    require(record.get("provider") == spec["runtime_contract"]["provider"], f"Baseline provider mismatch: {relative}")
    require(record.get("scenario") == spec["runtime_contract"]["scenario"], f"Baseline scenario mismatch: {relative}")
    parameters = record.get("resolved", {}).get("parameters")
    metrics = record.get("metrics")
    require(isinstance(parameters, dict) and isinstance(metrics, dict), f"Incomplete baseline raw: {relative}")
    axis = pair["axis"]
    require(parameters.get(axis["parameter"]) == axis["baseline_value"], f"Baseline axis mismatch: {relative}")
    registered = pair["registered_metrics"]["baseline"]
    for field, expected in registered.items():
        require(metrics.get(field) == expected, f"Baseline metric {field} differs from registration source: {relative}")
    validate_critical_sum(metrics, relative)
    require(dominant_set(metrics) == pair["registered_prediction"]["baseline_dominant_set"], f"Baseline dominant set mismatch: {relative}")
    artifact = {
        "pair_id": pair["pair_id"],
        "role": "baseline_only_source",
        "path": relative,
        "file_sha256": actual_sha,
        "candidate_id": record["candidate_id"],
        "canonical_trace_sha256": metrics["CanonicalTraceSha256"],
    }
    return record, artifact


def _invariant_contract(spec: dict[str, Any], pair: dict[str, Any], parameters: dict[str, Any]) -> dict[str, Any]:
    axis_parameter = pair["axis"]["parameter"]
    names = [name for name in spec["invariant_parameter_fields"] if name != axis_parameter]
    missing = [name for name in names if name not in parameters]
    require(not missing, f"Missing invariant parameters for {pair['pair_id']}: {missing}")
    runtime = spec["runtime_contract"]
    values = {
        "parameters": {name: parameters[name] for name in names},
        "runtime_identity": {
            "provider": runtime["provider"],
            "scenario": runtime["scenario"],
            "runtime": runtime["runtime"],
            "deterministic": runtime["deterministic"],
        },
        "seed_contract_hash": sha256_object(runtime["seed_contract"]),
        "runtime_constants": runtime["constants"],
    }
    return {
        "excluded_axis_parameter": axis_parameter,
        "parameter_fields": names,
        "values": values,
        "invariant_pair_hash": sha256_object(values),
    }


def _candidate_parameters(pair: dict[str, Any], baseline_parameters: dict[str, Any], role: str, repeat: int) -> dict[str, Any]:
    parameters = {
        key: value
        for key, value in baseline_parameters.items()
        if key not in {"case_id", "axis", "axis_value", "repeat"}
    }
    axis = pair["axis"]
    axis_value = axis["baseline_value"] if role == "baseline" else axis["intervention_value"]
    parameters.update({
        axis["parameter"]: axis_value,
        "case_id": f"{pair['pair_id']}_{role}_r{repeat}",
        "axis": axis["name"],
        "axis_value": axis_value,
        "repeat": repeat,
    })
    return parameters


def _candidate(
    experiment_id: str,
    pair: dict[str, Any],
    invariant: dict[str, Any],
    intervention_pair_hash: str,
    seed_contract_hash: str,
    baseline_parameters: dict[str, Any],
    role: str,
    repeat: int,
) -> dict[str, Any]:
    parameters = _candidate_parameters(pair, baseline_parameters, role, repeat)
    identity = {
        "schema_version": "stage-reviewer-p2-candidate-1.0",
        "experiment_id": experiment_id,
        "pair_id": pair["pair_id"],
        "role": role,
        "repeat": repeat,
        "parameters": parameters,
        "invariant_pair_hash": invariant["invariant_pair_hash"],
        "intervention_pair_hash": intervention_pair_hash,
        "seed_contract_hash": seed_contract_hash,
    }
    identity_sha = sha256_object(identity)
    return {
        "candidate_id": f"p2-{identity_sha[:16]}",
        "candidate_identity_sha256": identity_sha,
        "identity": identity,
        "pair_id": pair["pair_id"],
        "role": role,
        "repeat": repeat,
        "parameters": parameters,
        "invariant_pair_hash": invariant["invariant_pair_hash"],
        "intervention_pair_hash": intervention_pair_hash,
    }


def build_contract(spec_path: Path = DEFAULT_SPEC, repo_root: Path = REPO_ROOT) -> dict[str, Any]:
    spec = load_spec(spec_path)
    final_index_path, indexed_files = _final_index(repo_root, spec)
    seed_contract_hash = sha256_object(spec["runtime_contract"]["seed_contract"])
    artifacts: list[dict[str, Any]] = []
    registered_pairs: list[dict[str, Any]] = []
    candidates: list[dict[str, Any]] = []
    for pair in spec["interventions"]:
        baseline, artifact = _load_baseline(repo_root, spec, pair, indexed_files)
        artifacts.append(artifact)
        parameters = baseline["resolved"]["parameters"]
        invariant = _invariant_contract(spec, pair, parameters)
        pair_identity = {
            "experiment_id": spec["experiment_id"],
            "pair_id": pair["pair_id"],
            "invariant_pair_hash": invariant["invariant_pair_hash"],
            "axis_name": pair["axis"]["name"],
            "axis_parameter": pair["axis"]["parameter"],
            "baseline_value": pair["axis"]["baseline_value"],
            "intervention_value": pair["axis"]["intervention_value"],
        }
        intervention_pair_hash = sha256_object(pair_identity)
        pair_candidates = [
            _candidate(
                spec["experiment_id"], pair, invariant, intervention_pair_hash,
                seed_contract_hash, parameters, role, repeat,
            )
            for role in ("baseline", "intervention")
            for repeat in spec["repeat_contract"]["repeat_indices"]
        ]
        candidates.extend(pair_candidates)
        registered_pairs.append({
            "pair_id": pair["pair_id"],
            "axis": pair["axis"],
            "target": pair["target"],
            "source_baseline": artifact,
            "invariant_contract": invariant,
            "pair_identity": pair_identity,
            "intervention_pair_hash": intervention_pair_hash,
            "registered_metrics": pair["registered_metrics"],
            "registered_prediction": pair["registered_prediction"],
            "runner_contract": {
                "experiment_name": spec["experiment_id"],
                "provider": spec["runtime_contract"]["provider"],
                "scenario": spec["runtime_contract"]["scenario"],
                "evidence_level": "measured",
                "config_id": baseline["resolved"]["base_config"]["config_id"],
                "config_hash": baseline["config_hash"],
                "base_config": baseline["resolved"]["base_config"],
            },
            "candidates": pair_candidates,
        })
    require(len(candidates) == 8, "P2 must expand to exactly eight candidates")
    require(len({candidate["candidate_id"] for candidate in candidates}) == 8, "P2 candidate IDs are not unique")
    baseline_index = {
        "schema_version": "stage-reviewer-p2-baseline-index-1.0",
        "source_bundle_root": spec["baseline_bundle"]["root"],
        "source_final_manifest_index": {
            "path": repo_path(repo_root, final_index_path),
            "file_sha256": sha256_path(final_index_path),
        },
        "selection_rule": "only the two registered pre-intervention baseline raw artifacts",
        "prior_result_known": True,
        "artifacts": sorted(artifacts, key=lambda item: item["pair_id"]),
    }
    return {
        "spec": spec,
        "spec_path": repo_path(repo_root, spec_path),
        "spec_file_sha256": sha256_path(spec_path),
        "seed_contract_hash": seed_contract_hash,
        "baseline_only_index": baseline_index,
        "baseline_only_index_sha256": sha256_object(baseline_index),
        "pairs": registered_pairs,
        "candidates": candidates,
    }


def build_plan(
    spec_path: Path = DEFAULT_SPEC,
    repo_root: Path = REPO_ROOT,
    generated_utc: str | None = None,
) -> dict[str, Any]:
    contract = build_contract(spec_path, repo_root)
    spec = contract["spec"]
    return integrity_wrap({
        "schema_version": "stage-reviewer-p2-plan-1.0",
        "experiment_id": spec["experiment_id"],
        "generated_utc": generated_utc or utc_now(),
        "status": "plan_only_unfrozen_no_measurements",
        "classification": spec["classification"],
        "prior_observation_disclosure": spec["prior_observation_disclosure"],
        "spec": {"path": contract["spec_path"], "file_sha256": contract["spec_file_sha256"]},
        "baseline_only_index_sha256": contract["baseline_only_index_sha256"],
        "repeat_contract": spec["repeat_contract"],
        "execution_integrity_contract": spec["execution_integrity_contract"],
        "pair_count": len(contract["pairs"]),
        "candidate_count": len(contract["candidates"]),
        "pairs": contract["pairs"],
        "candidate_execution_order": [candidate["candidate_id"] for candidate in contract["candidates"]],
        "measurement_state": "not_run",
    })


def build_preregistration(
    spec_path: Path = DEFAULT_SPEC,
    repo_root: Path = REPO_ROOT,
    created_utc: str | None = None,
) -> dict[str, Any]:
    contract = build_contract(spec_path, repo_root)
    spec = contract["spec"]
    payload = {
        "schema_version": "stage-reviewer-p2-preregistration-1.0",
        "preregistration_id": spec["experiment_id"],
        "created_utc": created_utc or utc_now(),
        "status": "frozen_before_intervention",
        "classification": spec["classification"],
        "prior_observation_disclosure": spec["prior_observation_disclosure"],
        "spec": {"path": contract["spec_path"], "file_sha256": contract["spec_file_sha256"]},
        "source_baseline_bundle": {
            "root": spec["baseline_bundle"]["root"],
            "baseline_only_index": contract["baseline_only_index"],
            "baseline_only_index_sha256": contract["baseline_only_index_sha256"],
        },
        "runtime_contract": spec["runtime_contract"],
        "seed_contract_hash": contract["seed_contract_hash"],
        "repeat_contract": spec["repeat_contract"],
        "execution_integrity_contract": spec["execution_integrity_contract"],
        "attribution_contract": spec["attribution_contract"],
        "error_contract": spec["error_contract"],
        "hash_contract": {
            "canonicalization": "sorted-key compact UTF-8 JSON",
            "candidate_identity_sha256": "sha256(canonical candidate identity object)",
            "invariant_pair_hash": "sha256(canonical invariant values excluding the intervention axis and repeat)",
            "intervention_pair_hash": "sha256(canonical invariant hash plus pair axis and before/after values)",
        },
        "pairs": contract["pairs"],
        "candidate_count": len(contract["candidates"]),
        "output_contract": {
            "observation_schema": "experiments/aspdac/specs/reviewer_extension_20260717/p2_observation.schema.json",
            "expected_raw_directory": "experiments/aspdac/results/reviewer_extension_20260717/raw/p2_interventions",
            "analysis_manifest": "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_intervention_analysis.json",
            "candidate_manifest": "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_candidate_manifest.json",
            "checkpoint": "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_checkpoint.json",
            "runner_input_directory": "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_runner_inputs",
            "runner_output_directory": "experiments/aspdac/results/reviewer_extension_20260717/manifests/p2_runner_outputs",
            "observation_producer": "reviewer_extension_p2.py observe",
            "no_measurement_generated_by_preregistration": True,
        },
        "claim_boundary": spec["claim_boundary"],
    }
    return integrity_wrap(payload)


def _runner_input(
    preregistration: dict[str, Any],
    pair: dict[str, Any],
    candidate: dict[str, Any],
    prereg_hash: str,
) -> dict[str, Any]:
    contract = pair["runner_contract"]
    return {
        "candidate_id": candidate["candidate_id"],
        "experiment_name": contract["experiment_name"],
        "provider": contract["provider"],
        "scenario": contract["scenario"],
        "evidence_level": contract["evidence_level"],
        "config_id": contract["config_id"],
        "config_hash": contract["config_hash"],
        "axes": {
            "case_id": candidate["parameters"]["case_id"],
            "pair_id": candidate["pair_id"],
            "role": candidate["role"],
            "repeat": candidate["repeat"],
        },
        "resolved": {
            "schema_version": "aspg-candidate-config-1.0",
            "base_config": contract["base_config"],
            "parameters": candidate["parameters"],
        },
        "p2_binding": {
            "preregistration_content_sha256": prereg_hash,
            "candidate_identity_sha256": candidate["candidate_identity_sha256"],
            "invariant_pair_hash": candidate["invariant_pair_hash"],
            "intervention_pair_hash": candidate["intervention_pair_hash"],
        },
    }


def build_candidate_manifest(
    preregistration: dict[str, Any],
    preregistration_path: Path,
    runner_input_dir: Path = DEFAULT_RUNNER_INPUTS,
    runner_output_dir: Path = DEFAULT_RUNNER_OUTPUTS,
    observation_dir: Path = DEFAULT_RAW,
) -> dict[str, Any]:
    prereg_hash = verify_integrity(preregistration, str(preregistration_path))
    pairs = _pair_map(preregistration)
    candidates = [candidate for pair in preregistration["pairs"] for candidate in pair["candidates"]]
    runner_input_dir.mkdir(parents=True, exist_ok=True)
    registered_candidates: list[dict[str, Any]] = []
    runner_dll = REPO_ROOT / "tools/HardwareSim.AspdacRunner/bin/Release/net8.0/HardwareSim.AspdacRunner.dll"
    for candidate in candidates:
        pair = pairs[candidate["pair_id"]]
        runner_input = _runner_input(preregistration, pair, candidate, prereg_hash)
        runner_input_path = runner_input_dir / f"{candidate['candidate_id']}.json"
        runner_output_path = runner_output_dir / f"{candidate['candidate_id']}.json"
        observation_path = observation_dir / f"{candidate['candidate_id']}.json"
        write_json(runner_input_path, runner_input)
        registered_candidates.append({
            **candidate,
            "runner_input_path": str(runner_input_path),
            "runner_input_file_sha256": sha256_path(runner_input_path),
            "runner_output_path": str(runner_output_path),
            "observation_path": str(observation_path),
            "runner_command": [
                "dotnet", str(runner_dll), "--input", str(runner_input_path),
                "--output", str(runner_output_path),
            ],
            "expected_observation_filename": f"{candidate['candidate_id']}.json",
            "state": "planned_not_run",
        })
    payload = {
        "schema_version": "stage-reviewer-p2-candidate-manifest-1.0",
        "preregistration": {
            "path": str(preregistration_path),
            "content_sha256": prereg_hash,
            "file_sha256": sha256_path(preregistration_path),
        },
        "run_authorization": "authorized_only_after_this_preregistration_hash_was_frozen",
        "candidate_count": len(candidates),
        "candidates": registered_candidates,
        "resume_checkpoint_failure_contract": preregistration["execution_integrity_contract"],
        "measurement_state": "not_run",
    }
    require(payload["candidate_count"] == 8, "Candidate manifest must contain exactly eight candidates")
    require(len({candidate["candidate_id"] for candidate in candidates}) == 8, "Candidate manifest IDs are not unique")
    return integrity_wrap(payload)


def verify_candidate_manifest(
    manifest: dict[str, Any],
    preregistration: dict[str, Any],
    prereg_hash: str,
    context: str,
) -> str:
    manifest_hash = verify_integrity(manifest, context)
    require(manifest.get("schema_version") == "stage-reviewer-p2-candidate-manifest-1.0", "Candidate manifest schema mismatch")
    require(manifest.get("measurement_state") == "not_run", "Candidate manifest must remain a pre-run contract")
    require(manifest.get("preregistration", {}).get("content_sha256") == prereg_hash, "Candidate manifest preregistration hash mismatch")
    frozen = _candidate_map(preregistration)
    registered = manifest.get("candidates")
    require(isinstance(registered, list) and len(registered) == 8, "Candidate manifest must contain eight candidates")
    by_id = {str(candidate.get("candidate_id")): candidate for candidate in registered}
    require(set(by_id) == set(frozen), "Candidate manifest candidate set mismatch")
    for candidate_id, expected in frozen.items():
        actual = by_id[candidate_id]
        for field in (
            "candidate_identity_sha256", "pair_id", "role", "repeat", "parameters",
            "invariant_pair_hash", "intervention_pair_hash",
        ):
            require(actual.get(field) == expected[field], f"Candidate manifest {field} mismatch: {candidate_id}")
        require(actual.get("state") == "planned_not_run", f"Candidate manifest state mismatch: {candidate_id}")
        runner_input_path = Path(actual.get("runner_input_path", ""))
        require(runner_input_path.is_file(), f"Missing frozen runner input: {candidate_id}")
        require(
            sha256_path(runner_input_path) == actual.get("runner_input_file_sha256"),
            f"Frozen runner input SHA mismatch: {candidate_id}",
        )
        runner_input = read_json(runner_input_path)
        require(runner_input.get("candidate_id") == candidate_id, f"Runner input candidate mismatch: {candidate_id}")
        require(runner_input.get("provider") == "stage", f"Runner input provider mismatch: {candidate_id}")
        require(runner_input.get("scenario") == "codesign_bottleneck", f"Runner input scenario mismatch: {candidate_id}")
        require(
            runner_input.get("p2_binding", {}).get("preregistration_content_sha256") == prereg_hash,
            f"Runner input preregistration mismatch: {candidate_id}",
        )
        require(
            runner_input.get("resolved", {}).get("parameters") == expected["parameters"],
            f"Runner input parameters mismatch: {candidate_id}",
        )
        command = actual.get("runner_command")
        require(
            isinstance(command, list)
            and len(command) == 6
            and command[0] == "dotnet"
            and command[2] == "--input"
            and command[4] == "--output",
            f"Runner command contract mismatch: {candidate_id}",
        )
    return manifest_hash


def build_initial_checkpoint(
    candidate_manifest: dict[str, Any],
    candidate_manifest_path: Path,
) -> dict[str, Any]:
    manifest_hash = verify_integrity(candidate_manifest, str(candidate_manifest_path))
    candidates = candidate_manifest["candidates"]
    payload = {
        "schema_version": "stage-reviewer-p2-checkpoint-1.0",
        "generated_utc": utc_now(),
        "state": "preregistered_not_run",
        "preregistration_content_sha256": candidate_manifest["preregistration"]["content_sha256"],
        "candidate_manifest": {
            "path": str(candidate_manifest_path),
            "content_sha256": manifest_hash,
            "file_sha256": sha256_path(candidate_manifest_path),
        },
        "counts": {
            "planned": len(candidates),
            "completed": 0,
            "failed": 0,
            "pending": len(candidates),
        },
        "completed_candidates": [],
        "failed_candidates": [],
        "pending_candidate_ids": [candidate["candidate_id"] for candidate in candidates],
        "resume_rule": candidate_manifest["resume_checkpoint_failure_contract"]["resume_contract"],
        "failure_rule": candidate_manifest["resume_checkpoint_failure_contract"]["failure_contract"],
        "measurement_state": "not_run",
    }
    require(payload["counts"] == {"planned": 8, "completed": 0, "failed": 0, "pending": 8}, "Initial checkpoint counts are invalid")
    return integrity_wrap(payload)


def _pair_map(preregistration: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {str(pair["pair_id"]): pair for pair in preregistration["pairs"]}


def _candidate_map(preregistration: dict[str, Any]) -> dict[str, dict[str, Any]]:
    candidates = [candidate for pair in preregistration["pairs"] for candidate in pair["candidates"]]
    require(len(candidates) == preregistration.get("candidate_count") == 8, "Frozen candidate count is not eight")
    require(len({candidate["candidate_id"] for candidate in candidates}) == 8, "Frozen candidate IDs are not unique")
    for candidate in candidates:
        expected_sha = sha256_object(candidate["identity"])
        require(expected_sha == candidate["candidate_identity_sha256"], f"Frozen candidate identity mismatch: {candidate['candidate_id']}")
        require(candidate["candidate_id"] == f"p2-{expected_sha[:16]}", f"Frozen candidate ID mismatch: {candidate['candidate_id']}")
    return {str(candidate["candidate_id"]): candidate for candidate in candidates}


def _actual_invariant_values(pair: dict[str, Any], parameters: dict[str, Any]) -> dict[str, Any]:
    expected = pair["invariant_contract"]["values"]
    names = pair["invariant_contract"]["parameter_fields"]
    missing = [name for name in names if name not in parameters]
    require(not missing, f"Missing invariant fields for {pair['pair_id']}: {missing}")
    return {
        "parameters": {name: parameters[name] for name in names},
        "runtime_identity": expected["runtime_identity"],
        "seed_contract_hash": expected["seed_contract_hash"],
        "runtime_constants": expected["runtime_constants"],
    }


def _validate_observation(
    record: dict[str, Any],
    expected: dict[str, Any],
    pair: dict[str, Any],
    prereg_hash: str,
    path: Path,
) -> None:
    context = str(path)
    require(record.get("schema_version") == "stage-reviewer-p2-observation-1.0", f"Observation schema mismatch: {context}")
    require(record.get("status") == "completed", f"Observation is not completed: {context}")
    require(record.get("candidate_id") == expected["candidate_id"], f"Candidate ID mismatch: {context}")
    require(record.get("candidate_identity_sha256") == expected["candidate_identity_sha256"], f"Candidate identity hash mismatch: {context}")
    require(record.get("preregistration_content_sha256") == prereg_hash, f"Preregistration hash mismatch: {context}")
    require(record.get("pair_id") == expected["pair_id"], f"Pair ID mismatch: {context}")
    require(record.get("role") == expected["role"], f"Role mismatch: {context}")
    require(record.get("repeat") == expected["repeat"], f"Repeat index mismatch: {context}")
    require(record.get("intervention_pair_hash") == pair["intervention_pair_hash"], f"Intervention pair hash mismatch: {context}")
    require(record.get("invariant_pair_hash") == pair["invariant_contract"]["invariant_pair_hash"], f"Declared invariant pair hash mismatch: {context}")
    parameters = record.get("resolved", {}).get("parameters")
    require(isinstance(parameters, dict), f"Missing resolved parameters: {context}")
    actual_invariant = _actual_invariant_values(pair, parameters)
    require(
        sha256_object(actual_invariant) == pair["invariant_contract"]["invariant_pair_hash"],
        f"Recomputed invariant pair hash mismatch: {context}",
    )
    require(parameters == expected["parameters"], f"Resolved candidate parameters mismatch: {context}")
    metrics = record.get("metrics")
    require(isinstance(metrics, dict), f"Missing metrics: {context}")
    for field in REQUIRED_METRIC_FIELDS:
        require(field in metrics, f"Missing metric {field}: {context}")
    for field in REQUIRED_METRIC_FIELDS:
        if field not in {"DominantBottleneck", "CanonicalTraceSha256"}:
            integer_metric(metrics, field)
    trace_hash = metrics["CanonicalTraceSha256"]
    require(isinstance(trace_hash, str) and len(trace_hash) == 64 and all(ch in "0123456789abcdef" for ch in trace_hash), f"Invalid trace SHA: {context}")
    validate_critical_sum(metrics, context)
    measured_set = dominant_set(metrics)
    selected = metrics["DominantBottleneck"]
    require(selected == measured_set[0], f"Dominant tie-break mismatch: {context}")


def _read_observations(
    raw_dir: Path,
    candidates: dict[str, dict[str, Any]],
    pairs: dict[str, dict[str, Any]],
    prereg_hash: str,
) -> tuple[dict[str, dict[str, Any]], list[dict[str, Any]]]:
    require(raw_dir.is_dir(), f"Missing P2 raw directory: {raw_dir}")
    paths = sorted(raw_dir.glob("*.json"))
    require(len(paths) == len(candidates), f"Expected {len(candidates)} observation files, found {len(paths)}")
    records: dict[str, dict[str, Any]] = {}
    inputs: list[dict[str, Any]] = []
    for path in paths:
        record = read_json(path)
        candidate_id = str(record.get("candidate_id", ""))
        require(candidate_id in candidates, f"Unexpected candidate observation: {candidate_id or path.name}")
        require(candidate_id not in records, f"Duplicate candidate observation: {candidate_id}")
        expected = candidates[candidate_id]
        pair = pairs[expected["pair_id"]]
        _validate_observation(record, expected, pair, prereg_hash, path)
        records[candidate_id] = record
        inputs.append({"path": str(path), "file_sha256": sha256_path(path), "candidate_id": candidate_id})
    require(set(records) == set(candidates), "Observation candidate set does not match preregistration")
    return records, inputs


def _observation_from_runner_output(
    candidate: dict[str, Any],
    runner_output: dict[str, Any],
    prereg_hash: str,
    runner_output_path: Path,
) -> dict[str, Any]:
    context = str(runner_output_path)
    require(runner_output.get("status") == "completed", f"Runner output is not completed: {context}")
    metrics = runner_output.get("metrics")
    provenance = runner_output.get("provenance")
    require(isinstance(metrics, dict) and isinstance(provenance, dict), f"Incomplete runner output: {context}")
    parameter_metric_fields = {
        "workload_id": "WorkloadId",
        "mac_count": "MacCount",
        "packet_count": "PacketCount",
        "macs_per_pe_per_cycle": "MacsPerPePerCycle",
        "link_bits_per_cycle": "LinkBitsPerCycle",
        "memory_ports": "MemoryPorts",
        "queue_depth": "QueueDepth",
        "graph_hash": "GraphHash",
        "workload_hash": "WorkloadHash",
        "mapping_hash": "MappingHash",
        "model_hash": "ModelHash",
    }
    for parameter, metric in parameter_metric_fields.items():
        require(
            metrics.get(metric) == candidate["parameters"].get(parameter),
            f"Runner output {metric} does not match frozen parameters: {context}",
        )
    for field in REQUIRED_METRIC_FIELDS:
        require(field in metrics, f"Runner output is missing metric {field}: {context}")
    validate_critical_sum(metrics, context)
    measured_set = dominant_set(metrics)
    require(metrics["DominantBottleneck"] == measured_set[0], f"Runner output dominant tie-break mismatch: {context}")
    return {
        "schema_version": "stage-reviewer-p2-observation-1.0",
        "status": "completed",
        "candidate_id": candidate["candidate_id"],
        "candidate_identity_sha256": candidate["candidate_identity_sha256"],
        "preregistration_content_sha256": prereg_hash,
        "pair_id": candidate["pair_id"],
        "role": candidate["role"],
        "repeat": candidate["repeat"],
        "invariant_pair_hash": candidate["invariant_pair_hash"],
        "intervention_pair_hash": candidate["intervention_pair_hash"],
        "resolved": {"parameters": candidate["parameters"]},
        "metrics": metrics,
        "provenance": {
            "measurement_kind": runner_output.get("measurement_kind"),
            "runner_provenance": provenance,
            "runner_input_path": candidate["runner_input_path"],
            "runner_input_file_sha256": candidate["runner_input_file_sha256"],
            "runner_output_path": str(runner_output_path),
            "runner_output_file_sha256": sha256_path(runner_output_path),
            "limitations": runner_output.get("limitations", []),
        },
    }


def produce_observations(
    preregistration_path: Path = DEFAULT_PREREG,
    candidate_manifest_path: Path = DEFAULT_CANDIDATE_MANIFEST,
    observation_dir: Path | None = None,
) -> dict[str, Any]:
    preregistration = read_json(preregistration_path)
    require(preregistration.get("status") == "frozen_before_intervention", "Preregistration is not frozen")
    prereg_hash = verify_integrity(preregistration, str(preregistration_path))
    candidate_manifest = read_json(candidate_manifest_path)
    candidate_manifest_hash = verify_candidate_manifest(
        candidate_manifest, preregistration, prereg_hash, str(candidate_manifest_path),
    )
    outputs: list[dict[str, Any]] = []
    resumed = 0
    for candidate in candidate_manifest["candidates"]:
        runner_output_path = Path(candidate["runner_output_path"])
        require(runner_output_path.is_file(), f"Missing real runner output: {runner_output_path}")
        runner_output = read_json(runner_output_path)
        observation = _observation_from_runner_output(candidate, runner_output, prereg_hash, runner_output_path)
        output_path = (
            observation_dir / f"{candidate['candidate_id']}.json"
            if observation_dir is not None
            else Path(candidate["observation_path"])
        )
        if output_path.exists():
            existing = read_json(output_path)
            require(
                canonical_json(existing) == canonical_json(observation),
                f"Resume observation mismatch; preserving existing artifact: {output_path}",
            )
            resumed += 1
        else:
            write_json(output_path, observation)
        outputs.append({
            "candidate_id": candidate["candidate_id"],
            "runner_output_path": str(runner_output_path),
            "runner_output_file_sha256": sha256_path(runner_output_path),
            "observation_path": str(output_path),
            "observation_file_sha256": sha256_path(output_path),
        })
    payload = {
        "schema_version": "stage-reviewer-p2-observation-manifest-1.0",
        "generated_utc": utc_now(),
        "preregistration_content_sha256": prereg_hash,
        "candidate_manifest_content_sha256": candidate_manifest_hash,
        "candidate_count": len(outputs),
        "completed_count": len(outputs),
        "failed_count": 0,
        "resumed_count": resumed,
        "outputs": outputs,
        "measurement_source": "real HardwareSim.AspdacRunner outputs; no synthetic observations",
    }
    require(len(outputs) == 8, "Observation producer must bind exactly eight runner outputs")
    return integrity_wrap(payload)


def _exact_repeat_view(metrics: dict[str, Any], fields: Iterable[str]) -> dict[str, Any]:
    return {field: metrics[field] for field in fields}


def _metric_error(before: int, after: int, point: int, interval: list[int]) -> dict[str, Any]:
    measured_delta = after - before
    absolute_error = abs(measured_delta - point)
    return {
        "before": before,
        "after": after,
        "measured_delta": measured_delta,
        "measured_relative_delta": measured_delta / max(abs(before), 1),
        "registered_delta_point": point,
        "registered_delta_interval": interval,
        "absolute_prediction_error": absolute_error,
        "relative_prediction_error": absolute_error / max(abs(point), 1),
        "interval_pass": interval[0] <= measured_delta <= interval[1],
    }


def _analyze_pair(
    pair: dict[str, Any],
    records: dict[str, dict[str, Any]],
    repeat_fields: list[str],
) -> dict[str, Any]:
    by_role_repeat: dict[tuple[str, int], dict[str, Any]] = {}
    for candidate in pair["candidates"]:
        key = (candidate["role"], int(candidate["repeat"]))
        by_role_repeat[key] = records[candidate["candidate_id"]]
    require(
        set(by_role_repeat) == {("baseline", 0), ("baseline", 1), ("intervention", 0), ("intervention", 1)},
        f"Repeat set mismatch for {pair['pair_id']}",
    )
    for role in ("baseline", "intervention"):
        first = _exact_repeat_view(by_role_repeat[(role, 0)]["metrics"], repeat_fields)
        second = _exact_repeat_view(by_role_repeat[(role, 1)]["metrics"], repeat_fields)
        require(first == second, f"Exact repeat mismatch for {pair['pair_id']}/{role}")
    before = by_role_repeat[("baseline", 0)]["metrics"]
    after = by_role_repeat[("intervention", 0)]["metrics"]
    prediction = pair["registered_prediction"]
    target = pair["target"]
    total_error = _metric_error(
        integer_metric(before, "TotalCycles"), integer_metric(after, "TotalCycles"),
        prediction["total_delta_point"], prediction["total_delta_interval"],
    )
    service_error = _metric_error(
        integer_metric(before, target["service_metric"]), integer_metric(after, target["service_metric"]),
        prediction["target_service_delta_point"], prediction["target_service_delta_interval"],
    )
    critical_error = _metric_error(
        integer_metric(before, target["critical_metric"]), integer_metric(after, target["critical_metric"]),
        prediction["target_critical_delta_point"], prediction["target_critical_delta_interval"],
    )
    before_registered = all(before.get(field) == value for field, value in pair["registered_metrics"]["baseline"].items())
    after_registered = all(after.get(field) == value for field, value in pair["registered_metrics"]["intervention"].items())
    after_total_interval = prediction["total_after_interval"]
    total_after_pass = after_total_interval[0] <= integer_metric(after, "TotalCycles") <= after_total_interval[1]
    before_set, after_set = dominant_set(before), dominant_set(after)
    dominant_set_pass = (
        before_set == prediction["baseline_dominant_set"]
        and after_set == prediction["intervention_dominant_set"]
    )
    selected_pass = after["DominantBottleneck"] == prediction["intervention_selected_dominant"]
    acceptance = {
        "exact_repeat_pass": True,
        "registered_baseline_metrics_pass": before_registered,
        "registered_intervention_metrics_pass": after_registered,
        "total_after_interval_pass": total_after_pass,
        "all_delta_intervals_pass": all(item["interval_pass"] for item in (total_error, service_error, critical_error)),
        "dominant_set_pass": dominant_set_pass,
        "selected_dominant_pass": selected_pass,
    }
    acceptance["pair_acceptance_pass"] = all(acceptance.values())
    return {
        "pair_id": pair["pair_id"],
        "axis": pair["axis"],
        "invariant_pair_hash": pair["invariant_contract"]["invariant_pair_hash"],
        "intervention_pair_hash": pair["intervention_pair_hash"],
        "repeat_trace_sha256": {
            role: by_role_repeat[(role, 0)]["metrics"]["CanonicalTraceSha256"]
            for role in ("baseline", "intervention")
        },
        "prediction_error": {
            "total_cycles": total_error,
            "target_service_cycles": service_error,
            "target_critical_cycles": critical_error,
        },
        "dominant_evaluation": {
            "measured_baseline_set": before_set,
            "measured_intervention_set": after_set,
            "measured_selected_intervention": after["DominantBottleneck"],
            "registered_baseline_set": prediction["baseline_dominant_set"],
            "registered_intervention_set": prediction["intervention_dominant_set"],
            "registered_selected_intervention": prediction["intervention_selected_dominant"],
        },
        "acceptance": acceptance,
    }


def analyze(
    preregistration_path: Path = DEFAULT_PREREG,
    raw_dir: Path = DEFAULT_RAW,
    candidate_manifest_path: Path = DEFAULT_CANDIDATE_MANIFEST,
) -> dict[str, Any]:
    preregistration = read_json(preregistration_path)
    require(preregistration.get("status") == "frozen_before_intervention", "Preregistration is not frozen")
    prereg_hash = verify_integrity(preregistration, str(preregistration_path))
    candidate_manifest = read_json(candidate_manifest_path)
    candidate_manifest_hash = verify_candidate_manifest(
        candidate_manifest, preregistration, prereg_hash, str(candidate_manifest_path),
    )
    pairs = _pair_map(preregistration)
    candidates = _candidate_map(preregistration)
    records, inputs = _read_observations(raw_dir, candidates, pairs, prereg_hash)
    repeat_fields = list(preregistration["repeat_contract"]["exact_metric_fields"])
    pair_results = [_analyze_pair(pair, records, repeat_fields) for pair in preregistration["pairs"]]
    payload = {
        "schema_version": "stage-reviewer-p2-analysis-1.0",
        "generated_utc": utc_now(),
        "preregistration": {
            "path": str(preregistration_path),
            "content_sha256": prereg_hash,
            "file_sha256": sha256_path(preregistration_path),
        },
        "candidate_manifest": {
            "path": str(candidate_manifest_path),
            "content_sha256": candidate_manifest_hash,
            "file_sha256": sha256_path(candidate_manifest_path),
        },
        "inputs": inputs,
        "candidate_count": len(records),
        "validation": {
            "preregistration_integrity_pass": True,
            "candidate_identity_pass": True,
            "candidate_manifest_pass": True,
            "invariant_and_pair_hash_pass": True,
            "repeat_contract_pass": True,
        },
        "pairs": pair_results,
        "overall_acceptance_pass": all(result["acceptance"]["pair_acceptance_pass"] for result in pair_results),
        "claim_boundary": preregistration["claim_boundary"],
    }
    return integrity_wrap(payload)


def _reporting_rows(
    analysis: dict[str, Any],
    analysis_hash: str,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    require(analysis.get("schema_version") == "stage-reviewer-p2-analysis-1.0", "Unsupported P2 analysis schema")
    require(analysis.get("candidate_count") == 8, "Reporting requires exactly eight observations")
    require(analysis.get("overall_acceptance_pass") is True, "Reporting requires overall_acceptance_pass=true")
    validation = analysis.get("validation")
    require(isinstance(validation, dict) and all(validation.values()), "Reporting requires all analysis validation checks to pass")
    pairs = analysis.get("pairs")
    require(isinstance(pairs, list) and len(pairs) == 2, "Reporting requires exactly two accepted intervention pairs")
    intervention_rows: list[dict[str, Any]] = []
    error_rows: list[dict[str, Any]] = []
    for pair in pairs:
        acceptance = pair["acceptance"]
        require(acceptance.get("pair_acceptance_pass") is True, f"Pair is not accepted: {pair['pair_id']}")
        axis = pair["axis"]
        errors = pair["prediction_error"]
        dominant = pair["dominant_evaluation"]
        total = errors["total_cycles"]
        service = errors["target_service_cycles"]
        critical = errors["target_critical_cycles"]
        target_category = (
            "memory" if axis["parameter"] == "memory_ports"
            else "noc" if axis["parameter"] == "link_bits_per_cycle"
            else "registered_target"
        )
        intervention_rows.append({
            "pair_id": pair["pair_id"],
            "axis_name": axis["name"],
            "axis_parameter": axis["parameter"],
            "baseline_value": axis["baseline_value"],
            "intervention_value": axis["intervention_value"],
            "target_category": target_category,
            "before_total_cycles": total["before"],
            "after_total_cycles": total["after"],
            "measured_total_delta": total["measured_delta"],
            "measured_total_relative_delta": total["measured_relative_delta"],
            "before_target_service_cycles": service["before"],
            "after_target_service_cycles": service["after"],
            "measured_target_service_delta": service["measured_delta"],
            "before_target_critical_cycles": critical["before"],
            "after_target_critical_cycles": critical["after"],
            "measured_target_critical_delta": critical["measured_delta"],
            "measured_baseline_dominant_set": ";".join(dominant["measured_baseline_set"]),
            "measured_intervention_dominant_set": ";".join(dominant["measured_intervention_set"]),
            "measured_selected_intervention": dominant["measured_selected_intervention"],
            "registered_intervention_dominant_set": ";".join(dominant["registered_intervention_set"]),
            "registered_selected_intervention": dominant["registered_selected_intervention"],
            "baseline_trace_sha256": pair["repeat_trace_sha256"]["baseline"],
            "intervention_trace_sha256": pair["repeat_trace_sha256"]["intervention"],
            "invariant_pair_hash": pair["invariant_pair_hash"],
            "intervention_pair_hash": pair["intervention_pair_hash"],
            "exact_repeat_pass": acceptance["exact_repeat_pass"],
            "all_delta_intervals_pass": acceptance["all_delta_intervals_pass"],
            "dominant_set_pass": acceptance["dominant_set_pass"],
            "pair_acceptance_pass": acceptance["pair_acceptance_pass"],
            "analysis_content_sha256": analysis_hash,
            "evidence_boundary": analysis["claim_boundary"],
        })
        for metric_name, metric in (
            ("total_cycles", total),
            ("target_service_cycles", service),
            ("target_critical_cycles", critical),
        ):
            error_rows.append({
                "pair_id": pair["pair_id"],
                "axis_name": axis["name"],
                "metric": metric_name,
                "before": metric["before"],
                "after": metric["after"],
                "measured_delta": metric["measured_delta"],
                "measured_relative_delta": metric["measured_relative_delta"],
                "registered_delta_point": metric["registered_delta_point"],
                "registered_interval_low": metric["registered_delta_interval"][0],
                "registered_interval_high": metric["registered_delta_interval"][1],
                "absolute_prediction_error": metric["absolute_prediction_error"],
                "relative_prediction_error": metric["relative_prediction_error"],
                "interval_pass": metric["interval_pass"],
                "pair_acceptance_pass": acceptance["pair_acceptance_pass"],
                "analysis_content_sha256": analysis_hash,
            })
    return intervention_rows, error_rows


def _plot_intervention_report(rows: list[dict[str, Any]], output: Path) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    labels = [
        f"{str(row['axis_name']).replace('_', ' ').title()}\n{row['baseline_value']} -> {row['intervention_value']}"
        for row in rows
    ]
    x_values = list(range(len(rows)))
    width = 0.34
    before_color = "#6B7280"
    after_color = "#0F766E"
    panels = (
        ("before_total_cycles", "after_total_cycles", "measured_total_relative_delta", "(a) End-to-end cycles"),
        ("before_target_service_cycles", "after_target_service_cycles", None, "(b) Target service demand"),
        ("before_target_critical_cycles", "after_target_critical_cycles", None, "(c) Attributed critical cycles"),
    )
    plt.rcParams.update({
        "font.family": "DejaVu Sans",
        "font.size": 9,
        "axes.titleweight": "bold",
        "axes.spines.top": False,
        "axes.spines.right": False,
    })
    figure, axes = plt.subplots(1, 3, figsize=(11.8, 4.55))
    for panel_index, (before_key, after_key, relative_key, title) in enumerate(panels):
        axis = axes[panel_index]
        before = [int(row[before_key]) for row in rows]
        after = [int(row[after_key]) for row in rows]
        before_bars = axis.bar(
            [value - width / 2 for value in x_values],
            before,
            width,
            color=before_color,
            label="Baseline",
        )
        after_bars = axis.bar(
            [value + width / 2 for value in x_values],
            after,
            width,
            color=after_color,
            label="Intervention",
        )
        axis.bar_label(before_bars, fmt="%d", padding=3, fontsize=8)
        axis.bar_label(after_bars, fmt="%d", padding=3, fontsize=8)
        maximum = max(before + after)
        axis.set_ylim(0, maximum * 1.28 if maximum else 1)
        axis.set_xticks(x_values, labels)
        axis.set_ylabel("Cycles")
        axis.set_title(title)
        axis.grid(axis="y", alpha=0.22, linewidth=0.7)
        axis.set_axisbelow(True)
        if relative_key is not None:
            for index, row in enumerate(rows):
                axis.text(
                    index,
                    maximum * 1.17,
                    f"{float(row[relative_key]) * 100:.1f}%",
                    ha="center",
                    va="center",
                    fontsize=8,
                    fontweight="bold",
                    color="#1F2937",
                )
    figure.suptitle(
        "Preregistered single-axis interventions: 8/8 exact observations accepted",
        fontsize=13,
        fontweight="bold",
        y=0.985,
    )
    figure.legend(
        [before_bars[0], after_bars[0]],
        ["Baseline", "Intervention"],
        frameon=False,
        loc="upper center",
        bbox_to_anchor=(0.5, 0.925),
        ncol=2,
    )
    figure.text(
        0.5,
        0.025,
        "Deterministic within-model counterfactual evidence; not hardware or cross-tool timing accuracy.",
        ha="center",
        fontsize=8.5,
        color="#4B5563",
    )
    figure.tight_layout(rect=(0.015, 0.08, 0.995, 0.84), w_pad=2.0)
    output.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(
        output,
        bbox_inches="tight",
        metadata={"Title": "STAGE preregistered intervention results"},
    )
    figure.savefig(output.with_suffix(".png"), dpi=240, bbox_inches="tight")
    plt.close(figure)


def build_reporting_outputs(
    analysis_path: Path = DEFAULT_ANALYSIS,
    trace_summary_path: Path = DEFAULT_TRACE_GUIDED_SUMMARY,
    prediction_error_path: Path = DEFAULT_PREDICTION_ERROR_SUMMARY,
    figure_path: Path = DEFAULT_INTERVENTION_FIGURE,
) -> dict[str, Any]:
    analysis = read_json(analysis_path)
    analysis_hash = verify_integrity(analysis, str(analysis_path))
    intervention_rows, error_rows = _reporting_rows(analysis, analysis_hash)
    write_csv(trace_summary_path, intervention_rows)
    write_csv(prediction_error_path, error_rows)
    _plot_intervention_report(intervention_rows, figure_path)
    outputs = [
        trace_summary_path,
        prediction_error_path,
        figure_path,
        figure_path.with_suffix(".png"),
    ]
    payload = {
        "schema_version": "stage-reviewer-p2-reporting-manifest-1.0",
        "generated_utc": utc_now(),
        "source_analysis": {
            "path": str(analysis_path),
            "content_sha256": analysis_hash,
            "file_sha256": sha256_path(analysis_path),
            "candidate_count": analysis["candidate_count"],
            "overall_acceptance_pass": analysis["overall_acceptance_pass"],
        },
        "outputs": [
            {"path": str(path), "file_sha256": sha256_path(path)}
            for path in outputs
        ],
        "trace_guided_intervention_rows": len(intervention_rows),
        "prediction_error_rows": len(error_rows),
        "claim_boundary": analysis["claim_boundary"],
    }
    require(len(intervention_rows) == 2, "Expected two trace-guided intervention rows")
    require(len(error_rows) == 6, "Expected six prediction-error rows")
    return integrity_wrap(payload)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    plan_parser = subparsers.add_parser("plan", help="Build an unfrozen eight-candidate plan")
    plan_parser.add_argument("--spec", type=Path, default=DEFAULT_SPEC)
    plan_parser.add_argument("--output", type=Path, default=DEFAULT_PLAN)
    plan_parser.add_argument("--generated-utc")

    prereg_parser = subparsers.add_parser("preregister", help="Freeze baseline-only evidence and predictions")
    prereg_parser.add_argument("--spec", type=Path, default=DEFAULT_SPEC)
    prereg_parser.add_argument("--output", type=Path, default=DEFAULT_PREREG)
    prereg_parser.add_argument("--candidate-manifest-output", type=Path, default=DEFAULT_CANDIDATE_MANIFEST)
    prereg_parser.add_argument("--checkpoint-output", type=Path, default=DEFAULT_CHECKPOINT)
    prereg_parser.add_argument("--runner-input-dir", type=Path, default=DEFAULT_RUNNER_INPUTS)
    prereg_parser.add_argument("--runner-output-dir", type=Path, default=DEFAULT_RUNNER_OUTPUTS)
    prereg_parser.add_argument("--observation-dir", type=Path, default=DEFAULT_RAW)
    prereg_parser.add_argument("--created-utc")

    observe_parser = subparsers.add_parser("observe", help="Bind eight real runner outputs into P2 observations")
    observe_parser.add_argument("--preregistration", type=Path, default=DEFAULT_PREREG)
    observe_parser.add_argument("--candidate-manifest", type=Path, default=DEFAULT_CANDIDATE_MANIFEST)
    observe_parser.add_argument("--observation-dir", type=Path, default=DEFAULT_RAW)
    observe_parser.add_argument("--output", type=Path, default=DEFAULT_OBSERVATION_MANIFEST)

    analysis_parser = subparsers.add_parser("analyze", help="Validate and analyze eight bound observations")
    analysis_parser.add_argument("--preregistration", type=Path, default=DEFAULT_PREREG)
    analysis_parser.add_argument("--raw-dir", type=Path, default=DEFAULT_RAW)
    analysis_parser.add_argument("--candidate-manifest", type=Path, default=DEFAULT_CANDIDATE_MANIFEST)
    analysis_parser.add_argument("--output", type=Path, default=DEFAULT_ANALYSIS)

    report_parser = subparsers.add_parser("report", help="Derive CSV and figure outputs from accepted P2 analysis")
    report_parser.add_argument("--analysis", type=Path, default=DEFAULT_ANALYSIS)
    report_parser.add_argument("--trace-summary", type=Path, default=DEFAULT_TRACE_GUIDED_SUMMARY)
    report_parser.add_argument("--prediction-error", type=Path, default=DEFAULT_PREDICTION_ERROR_SUMMARY)
    report_parser.add_argument("--figure", type=Path, default=DEFAULT_INTERVENTION_FIGURE)
    report_parser.add_argument("--output", type=Path, default=DEFAULT_REPORT_MANIFEST)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.command == "plan":
        result = build_plan(args.spec, REPO_ROOT, args.generated_utc)
        write_json(args.output, result)
        summary = {
            "command": "plan",
            "output": str(args.output),
            "candidate_count": result["candidate_count"],
            "content_sha256": result["integrity"]["content_sha256"],
            "measurement_state": result["measurement_state"],
        }
    elif args.command == "preregister":
        result = build_preregistration(args.spec, REPO_ROOT, args.created_utc)
        write_json(args.output, result)
        candidate_manifest = build_candidate_manifest(
            result,
            args.output,
            args.runner_input_dir,
            args.runner_output_dir,
            args.observation_dir,
        )
        write_json(args.candidate_manifest_output, candidate_manifest)
        checkpoint = build_initial_checkpoint(candidate_manifest, args.candidate_manifest_output)
        write_json(args.checkpoint_output, checkpoint)
        summary = {
            "command": "preregister",
            "output": str(args.output),
            "candidate_count": result["candidate_count"],
            "content_sha256": result["integrity"]["content_sha256"],
            "candidate_manifest": str(args.candidate_manifest_output),
            "candidate_manifest_content_sha256": candidate_manifest["integrity"]["content_sha256"],
            "checkpoint": str(args.checkpoint_output),
            "checkpoint_content_sha256": checkpoint["integrity"]["content_sha256"],
            "status": result["status"],
        }
    elif args.command == "observe":
        result = produce_observations(
            args.preregistration,
            args.candidate_manifest,
            args.observation_dir,
        )
        write_json(args.output, result)
        summary = {
            "command": "observe",
            "output": str(args.output),
            "candidate_count": result["candidate_count"],
            "completed_count": result["completed_count"],
            "content_sha256": result["integrity"]["content_sha256"],
        }
    elif args.command == "report":
        result = build_reporting_outputs(
            args.analysis,
            args.trace_summary,
            args.prediction_error,
            args.figure,
        )
        write_json(args.output, result)
        summary = {
            "command": "report",
            "output": str(args.output),
            "source_analysis_content_sha256": result["source_analysis"]["content_sha256"],
            "trace_guided_intervention_rows": result["trace_guided_intervention_rows"],
            "prediction_error_rows": result["prediction_error_rows"],
            "content_sha256": result["integrity"]["content_sha256"],
        }
    else:
        result = analyze(args.preregistration, args.raw_dir, args.candidate_manifest)
        write_json(args.output, result)
        summary = {
            "command": "analyze",
            "output": str(args.output),
            "candidate_count": result["candidate_count"],
            "overall_acceptance_pass": result["overall_acceptance_pass"],
            "content_sha256": result["integrity"]["content_sha256"],
        }
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
