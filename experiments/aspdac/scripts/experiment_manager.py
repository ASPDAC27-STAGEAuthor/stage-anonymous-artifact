#!/usr/bin/env python3
"""Config-driven, resumable ASP-DAC final experiment manager."""

from __future__ import annotations

import argparse
import csv
import hashlib
import itertools
import json
import os
import platform
import shutil
import socket
import subprocess
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import jsonschema
import yaml


REPO_ROOT = Path(__file__).resolve().parents[3]
MANAGER_VERSION = "aspg-experiment-manager-1.0"
PLAN_SCHEMA_PATH = REPO_ROOT / "experiments/aspdac/specs/final_sweeps/experiment_plan.schema.json"
DEFAULT_RUNNER_DLL = REPO_ROOT / "tools/HardwareSim.AspdacRunner/bin/Release/net8.0/HardwareSim.AspdacRunner.dll"
BOOKSIM_TRACE_RUNNER = REPO_ROOT / "experiments/aspdac/scripts/run_booksim_cnn_trace_case.py"
MNIST_PE_RUNNER = REPO_ROOT / "experiments/aspdac/scripts/run_mnist_pe_precision.py"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def atomic_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    content = json.dumps(value, indent=2, ensure_ascii=False) + "\n"
    temporary.write_text(content, encoding="utf-8")
    try:
        os.replace(temporary, path)
    except PermissionError:
        # Managed Windows workspaces can deny replace-over-existing while still allowing
        # scoped writes. Preserve checkpoint progress with a flushed direct-write fallback.
        with path.open("w", encoding="utf-8") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        temporary.unlink(missing_ok=True)


def relative_to_repo(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return str(path.resolve())


def resolve_repo_path(value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else REPO_ROOT / path


def read_yaml(path: Path) -> dict[str, Any]:
    loaded = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(loaded, dict):
        raise ValueError(f"Expected a mapping in {path}")
    return loaded


def command_output(command: list[str]) -> str:
    try:
        return subprocess.run(
            command,
            cwd=REPO_ROOT,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        ).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "unavailable"


def environment_snapshot() -> dict[str, Any]:
    return {
        "captured_utc": utc_now(),
        "manager_version": MANAGER_VERSION,
        "git_commit": command_output(["git", "rev-parse", "HEAD"]),
        "git_status_porcelain_sha256": sha256_text(command_output(["git", "status", "--porcelain=v1"])),
        "host": socket.gethostname(),
        "platform": platform.platform(),
        "python": sys.version.split()[0],
        "dotnet": command_output(["dotnet", "--version"]),
    }


@dataclass(frozen=True)
class Candidate:
    candidate_id: str
    experiment_name: str
    provider: str
    scenario: str
    evidence_level: str
    config_id: str
    config_hash: str
    axes: dict[str, Any]
    resolved: dict[str, Any]

    def as_dict(self) -> dict[str, Any]:
        return {
            "candidate_id": self.candidate_id,
            "experiment_name": self.experiment_name,
            "provider": self.provider,
            "scenario": self.scenario,
            "evidence_level": self.evidence_level,
            "config_id": self.config_id,
            "config_hash": self.config_hash,
            "axes": self.axes,
            "resolved": self.resolved,
        }


class ExperimentManager:
    def __init__(self, plan_path: Path) -> None:
        self.plan_path = plan_path.resolve()
        self.plan = read_yaml(self.plan_path)
        schema = json.loads(PLAN_SCHEMA_PATH.read_text(encoding="utf-8"))
        jsonschema.validate(self.plan, schema)
        self.bundle = resolve_repo_path(str(self.plan["bundle_root"])).resolve()
        expected_root = (REPO_ROOT / "experiments/aspdac/results").resolve()
        if expected_root not in self.bundle.parents:
            raise ValueError(f"Bundle must be below {expected_root}: {self.bundle}")
        self.directories = {
            name: self.bundle / name
            for name in ("config", "raw", "summary", "figures", "manifests", "failures", "logs", "runtime")
        }
        self.configs: dict[str, dict[str, Any]] = {}
        self.config_hashes: dict[str, str] = {}
        self.environment = environment_snapshot()

    def initialize(self) -> None:
        for directory in self.directories.values():
            directory.mkdir(parents=True, exist_ok=True)
        self._freeze_configs()
        plan_destination = self.directories["manifests"] / f"plan_{self.plan['name']}.yaml"
        if plan_destination.exists() and sha256_file(plan_destination) != sha256_file(self.plan_path):
            raise ValueError(f"Frozen plan changed in place: {plan_destination}")
        if not plan_destination.exists():
            shutil.copyfile(self.plan_path, plan_destination)
        manifest_path = self.directories["manifests"] / "bundle_manifest.json"
        previous_manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
        plans = dict(previous_manifest.get("plans") or {})
        plans[str(self.plan["name"])] = {
            "path": relative_to_repo(self.plan_path),
            "sha256": sha256_file(self.plan_path),
        }
        config_hashes = dict(previous_manifest.get("config_hashes") or {})
        for config_id, digest in self.config_hashes.items():
            existing = config_hashes.get(config_id)
            if existing is not None and existing != digest:
                raise ValueError(f"Frozen config changed in place: {config_id}")
            config_hashes[config_id] = digest
        manifest = {
            "schema_version": "aspg-final-bundle-manifest-1.0",
            "bundle_root": relative_to_repo(self.bundle),
            "created_or_refreshed_utc": utc_now(),
            "plans": plans,
            "external_draft_policy": "read_only",
            "environment": self.environment,
            "config_hashes": config_hashes,
        }
        atomic_json(manifest_path, manifest)

    def _freeze_configs(self) -> None:
        frozen_path = self.directories["manifests"] / "frozen_configs.json"
        previous = json.loads(frozen_path.read_text(encoding="utf-8")) if frozen_path.exists() else []
        frozen_by_id = {str(entry["config_id"]): entry for entry in previous}
        for config_ref in self.plan["configs"]:
            source = resolve_repo_path(str(config_ref)).resolve()
            config = read_yaml(source)
            config_id = str(config["config_id"])
            resolved_json = canonical_json(config)
            digest = sha256_text(resolved_json)
            if config_id in self.configs and self.config_hashes[config_id] != digest:
                raise ValueError(f"Conflicting config_id {config_id}")
            self.configs[config_id] = config
            self.config_hashes[config_id] = digest
            stem = config_id.lower().replace("-", "_")
            yaml_destination = self.directories["config"] / f"{stem}.resolved.yaml"
            json_destination = self.directories["config"] / f"{stem}.resolved.json"
            yaml_destination.write_text(source.read_text(encoding="utf-8"), encoding="utf-8")
            json_destination.write_text(json.dumps(config, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8")
            existing = frozen_by_id.get(config_id)
            if existing is not None and existing["resolved_sha256"] != digest:
                raise ValueError(f"Frozen config changed in place: {config_id}")
            frozen_by_id[config_id] = {
                "config_id": config_id,
                "source": relative_to_repo(source),
                "source_sha256": sha256_file(source),
                "resolved_sha256": digest,
                "resolved_yaml": relative_to_repo(yaml_destination),
                "resolved_json": relative_to_repo(json_destination),
            }
        atomic_json(frozen_path, [frozen_by_id[key] for key in sorted(frozen_by_id)])

    def candidates(self, experiment_filter: str | None = None) -> list[Candidate]:
        candidates: list[Candidate] = []
        for experiment in self.plan["experiments"]:
            name = str(experiment["name"])
            if experiment_filter and experiment_filter.lower() not in name.lower():
                continue
            config_id = str(experiment["base_config"])
            if config_id not in self.configs:
                raise ValueError(f"Experiment {name} references missing config {config_id}")
            axes = experiment.get("axes") or {}
            keys = sorted(axes)
            case_rows = experiment.get("cases") or [{}]
            case_ids = [str(case.get("case_id", "")) for case in case_rows if case]
            if len(case_ids) != len(set(case_ids)):
                raise ValueError(f"Experiment {name} has duplicate case_id values")
            for case in case_rows:
                products: Iterable[tuple[Any, ...]] = itertools.product(*(axes[key] for key in keys)) if keys else [()]
                for values in products:
                    variable = dict(zip(keys, values))
                    parameters = dict(experiment.get("fixed") or {})
                    parameters.update(case)
                    parameters.update(variable)
                    identity_axes = dict(variable)
                    if case:
                        identity_axes["case_id"] = str(case["case_id"])
                    resolved = {
                        "schema_version": "aspg-candidate-config-1.0",
                        "base_config": self.configs[config_id],
                        "parameters": parameters,
                    }
                    identity = {
                        "experiment_name": name,
                        "provider": experiment["provider"],
                        "scenario": experiment["scenario"],
                        "evidence_level": experiment["evidence_level"],
                        "config_id": config_id,
                        "config_hash": self.config_hashes[config_id],
                        "axes": identity_axes,
                        "resolved": resolved,
                    }
                    candidate_id = "c-" + sha256_text(canonical_json(identity))[:16]
                    candidates.append(
                        Candidate(
                            candidate_id=candidate_id,
                            experiment_name=name,
                            provider=str(experiment["provider"]),
                            scenario=str(experiment["scenario"]),
                            evidence_level=str(experiment["evidence_level"]),
                            config_id=config_id,
                            config_hash=self.config_hashes[config_id],
                            axes=identity_axes,
                            resolved=resolved,
                        )
                    )
        ids = [candidate.candidate_id for candidate in candidates]
        if len(ids) != len(set(ids)):
            raise ValueError("Candidate identity collision detected")
        return candidates

    def write_candidate_manifest(self, candidates: list[Candidate]) -> None:
        manifest = {
            "schema_version": "aspg-candidate-manifest-1.0",
            "plan": self.plan["name"],
            "candidate_count": len(candidates),
            "candidates": [candidate.as_dict() for candidate in candidates],
        }
        atomic_json(self.directories["manifests"] / f"candidates_{self.plan['name']}.json", manifest)

    def run(self, candidates: list[Candidate], force: bool = False, limit: int | None = None) -> int:
        self.write_candidate_manifest(candidates)
        selected = candidates[:limit] if limit is not None else candidates
        completed = failed = skipped = resumed = 0
        for index, candidate in enumerate(selected, start=1):
            raw_path = self.directories["raw"] / candidate.experiment_name / f"{candidate.candidate_id}.json"
            if raw_path.exists() and not force:
                existing = json.loads(raw_path.read_text(encoding="utf-8"))
                if existing.get("candidate_id") == candidate.candidate_id and existing.get("config_hash") == candidate.config_hash:
                    resumed += 1
                    self._checkpoint(selected, index, completed, failed, skipped, resumed, candidate.candidate_id)
                    continue
            try:
                result = self._run_candidate(candidate)
                status = str(result.get("status", "completed"))
                if status == "skipped":
                    skipped += 1
                else:
                    completed += 1
                payload = {
                    "schema_version": "aspg-case-result-1.0",
                    "candidate_id": candidate.candidate_id,
                    "config_hash": candidate.config_hash,
                    "experiment_name": candidate.experiment_name,
                    "provider": candidate.provider,
                    "scenario": candidate.scenario,
                    "evidence_level": candidate.evidence_level,
                    "axes": candidate.axes,
                    "resolved": candidate.resolved,
                    "environment": self.environment,
                    **result,
                }
                atomic_json(raw_path, payload)
            except Exception as exception:  # failure evidence is intentionally preserved
                failed += 1
                failure = {
                    "schema_version": "aspg-case-failure-1.0",
                    "candidate": candidate.as_dict(),
                    "failed_utc": utc_now(),
                    "exception_type": type(exception).__name__,
                    "message": str(exception),
                }
                atomic_json(self.directories["failures"] / f"{candidate.candidate_id}.json", failure)
            self._checkpoint(selected, index, completed, failed, skipped, resumed, candidate.candidate_id)
            print(
                f"[{index}/{len(selected)}] {candidate.candidate_id} "
                f"completed={completed} failed={failed} skipped={skipped} resumed={resumed}",
                flush=True,
            )
        self.write_summary()
        return 1 if failed else 0

    def _run_candidate(self, candidate: Candidate) -> dict[str, Any]:
        if candidate.provider == "config_audit":
            return self._config_audit(candidate)
        if candidate.provider == "stage":
            return self._stage(candidate)
        if candidate.provider == "booksim2_trace":
            return self._booksim2_trace(candidate)
        if candidate.provider == "mnist_pe":
            return self._mnist_pe(candidate)
        return {
            "status": "skipped",
            "skip_reason": f"Unsupported provider: {candidate.provider}",
            "completed_utc": utc_now(),
        }

    def _config_audit(self, candidate: Candidate) -> dict[str, Any]:
        config = candidate.resolved["base_config"]
        assertions: list[tuple[str, bool]] = []
        if candidate.config_id == "V-TL":
            assertions = [
                ("compute_units_16", config["compute"]["units"] == 16),
                ("aggregate_16_mac_per_cycle", config["compute"]["aggregate_macs_per_cycle"] == 16),
                ("timeloop_model", config["reference"]["tool"] == "timeloop-model"),
            ]
        elif candidate.config_id == "V-SS":
            assertions = [
                ("array_4x4", config["array"]["rows"] == 4 and config["array"]["columns"] == 4),
                ("single_mac_cells", config["array"]["cell_macs_per_cycle"] == 1),
                ("weight_stationary", config["array"]["dataflow"] == "weight_stationary"),
                ("matched_request_queues", config["queues"]["read_request_entries"] == 32 and config["queues"]["write_request_entries"] == 32),
                ("matched_ifmap_filter_ports", config["ports"]["ifmap_sram_ports"] == 2 and config["ports"]["filter_sram_ports"] == 2),
                ("matched_sram_capacity", all(config["storage"][key] == 8 for key in ("ifmap_sram_kib", "filter_sram_kib", "ofmap_sram_kib"))),
                ("matched_user_bandwidth", all(config["bandwidth"][key] == 8 for key in ("ifmap_words_per_cycle", "filter_words_per_cycle", "ofmap_words_per_cycle"))),
            ]
        elif candidate.config_id == "S-Native":
            assertions = [
                ("sixteen_pe", config["compute"]["processing_elements"] == 16),
                ("pe_rate_256", config["compute"]["macs_per_pe_per_cycle"] == 256),
                ("one_memory_port", config["memory"]["ports"] == 1),
                ("memory_latency_5", config["memory"]["latency_cycles"] == 5),
                ("reduction_2", config["special_units"]["reduction_latency_cycles"] == 2),
                ("softmax_8", config["special_units"]["softmax_latency_cycles"] == 8),
            ]
        elif candidate.config_id == "S-CIM":
            assertions = [
                ("same_workload", config["pairing"]["same_workload"] is True),
                ("same_operation_count", config["pairing"]["same_operation_count"] is True),
                ("unknown_not_zero", "never coerced to zero" in config["unknown_policy"]),
            ]
        if not assertions or not all(passed for _, passed in assertions):
            failed_names = [name for name, passed in assertions if not passed]
            raise ValueError(f"Config audit failed: {failed_names or ['no audit registered']}")
        return {
            "status": "completed",
            "completed_utc": utc_now(),
            "measurement_kind": "configuration_contract_audit",
            "assertions": [{"name": name, "passed": passed} for name, passed in assertions],
            "metrics": {"assertions_passed": len(assertions), "assertions_total": len(assertions)},
            "limitations": ["This smoke point validates the frozen contract; it is not a workload runtime measurement."],
        }

    def _stage(self, candidate: Candidate) -> dict[str, Any]:
        runner_input = self.directories["manifests"] / "runner_inputs" / f"{candidate.candidate_id}.json"
        runner_output = self.directories["manifests"] / "runner_outputs" / f"{candidate.candidate_id}.json"
        atomic_json(runner_input, candidate.as_dict())
        if not DEFAULT_RUNNER_DLL.exists():
            raise FileNotFoundError(f"Build the Release ASP-DAC runner before executing a STAGE candidate: {DEFAULT_RUNNER_DLL}")
        command = [
            "dotnet",
            str(DEFAULT_RUNNER_DLL),
            "--input",
            str(runner_input),
            "--output",
            str(runner_output),
        ]
        started = time.monotonic()
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=1800,
        )
        elapsed = time.monotonic() - started
        if completed.returncode != 0:
            raise RuntimeError(
                f"STAGE runner failed with exit {completed.returncode}: "
                f"{completed.stderr[-4000:] or completed.stdout[-4000:]}"
            )
        result = json.loads(runner_output.read_text(encoding="utf-8"))
        return {
            **result,
            "command": command,
            "elapsed_seconds": elapsed,
            "stdout": completed.stdout,
            "stderr": completed.stderr,
            "runner_input_sha256": sha256_file(runner_input),
            "runner_output_sha256": sha256_file(runner_output),
        }

    def _mnist_pe(self, candidate: Candidate) -> dict[str, Any]:
        runner_input = self.directories["manifests"] / "runner_inputs" / f"{candidate.candidate_id}.json"
        runner_output = self.directories["manifests"] / "runner_outputs" / f"{candidate.candidate_id}.json"
        atomic_json(runner_input, candidate.as_dict())
        if not MNIST_PE_RUNNER.exists():
            raise FileNotFoundError(f"MNIST PE runner is missing: {MNIST_PE_RUNNER}")
        command = [
            sys.executable,
            str(MNIST_PE_RUNNER),
            "--input",
            str(runner_input),
            "--output",
            str(runner_output),
            "--bundle-root",
            str(self.bundle),
        ]
        runtime_tmp = self.directories["runtime"] / "tmp"
        runtime_tmp.mkdir(parents=True, exist_ok=True)
        environment = os.environ.copy()
        for key in ("TMP", "TEMP", "TMPDIR"):
            environment[key] = str(runtime_tmp)
        environment["TORCH_HOME"] = str(self.directories["runtime"] / "torch")
        environment["MPLCONFIGDIR"] = str(self.directories["runtime"] / "matplotlib")
        started = time.monotonic()
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=7200,
            env=environment,
        )
        elapsed = time.monotonic() - started
        stdout_path = self.directories["logs"] / f"{candidate.candidate_id}.stdout.log"
        stderr_path = self.directories["logs"] / f"{candidate.candidate_id}.stderr.log"
        stdout_path.write_text(completed.stdout, encoding="utf-8")
        stderr_path.write_text(completed.stderr, encoding="utf-8")
        if completed.returncode != 0:
            raise RuntimeError(
                f"MNIST PE runner failed with exit {completed.returncode}: "
                f"{completed.stderr[-4000:] or completed.stdout[-4000:]}"
            )
        if not runner_output.exists():
            raise RuntimeError(f"MNIST PE runner did not create {runner_output}")
        result = json.loads(runner_output.read_text(encoding="utf-8"))
        return {
            **result,
            "command": command,
            "elapsed_seconds": elapsed,
            "stdout": completed.stdout,
            "stderr": completed.stderr,
            "stdout_log": relative_to_repo(stdout_path),
            "stderr_log": relative_to_repo(stderr_path),
            "runner_input_sha256": sha256_file(runner_input),
            "runner_output_sha256": sha256_file(runner_output),
        }
    def _booksim2_trace(self, candidate: Candidate) -> dict[str, Any]:
        runner_input = self.directories["manifests"] / "runner_inputs" / f"{candidate.candidate_id}.json"
        runner_output = self.directories["manifests"] / "runner_outputs" / f"{candidate.candidate_id}.json"
        atomic_json(runner_input, candidate.as_dict())
        if not BOOKSIM_TRACE_RUNNER.exists():
            raise FileNotFoundError(f"BookSim2 trace runner is missing: {BOOKSIM_TRACE_RUNNER}")
        command = [
            sys.executable,
            str(BOOKSIM_TRACE_RUNNER),
            "--input",
            str(runner_input),
            "--output",
            str(runner_output),
        ]
        started = time.monotonic()
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=1800,
        )
        elapsed = time.monotonic() - started
        if completed.returncode != 0:
            raise RuntimeError(
                f"BookSim2 trace runner failed with exit {completed.returncode}: "
                f"{completed.stderr[-4000:] or completed.stdout[-4000:]}"
            )
        if not runner_output.exists():
            raise RuntimeError(f"BookSim2 trace runner did not create {runner_output}")
        result = json.loads(runner_output.read_text(encoding="utf-8"))
        return {
            **result,
            "command": command,
            "elapsed_seconds": elapsed,
            "stdout": completed.stdout,
            "stderr": completed.stderr,
            "runner_input_sha256": sha256_file(runner_input),
            "runner_output_sha256": sha256_file(runner_output),
        }

    def _checkpoint(
        self,
        selected: list[Candidate],
        index: int,
        completed: int,
        failed: int,
        skipped: int,
        resumed: int,
        current: str,
    ) -> None:
        checkpoint = {
            "schema_version": "aspg-checkpoint-1.0",
            "plan": self.plan["name"],
            "updated_utc": utc_now(),
            "processed": index,
            "total": len(selected),
            "completed_this_invocation": completed,
            "failed_this_invocation": failed,
            "skipped_this_invocation": skipped,
            "resumed": resumed,
            "current_candidate_id": current,
        }
        atomic_json(self.directories["manifests"] / f"checkpoint_{self.plan['name']}.json", checkpoint)

    def write_summary(self) -> None:
        rows: list[dict[str, Any]] = []
        for raw_path in sorted(self.directories["raw"].glob("*/*.json")):
            payload = json.loads(raw_path.read_text(encoding="utf-8"))
            row: dict[str, Any] = {
                "candidate_id": payload.get("candidate_id"),
                "experiment_name": payload.get("experiment_name"),
                "provider": payload.get("provider"),
                "scenario": payload.get("scenario"),
                "evidence_level": payload.get("evidence_level"),
                "status": payload.get("status"),
                "config_hash": payload.get("config_hash"),
                "raw_path": relative_to_repo(raw_path),
            }
            for key, value in sorted((payload.get("axes") or {}).items()):
                row[f"axis_{key}"] = value
            for key, value in sorted((payload.get("resolved", {}).get("parameters") or {}).items()):
                if not isinstance(value, (dict, list)):
                    row.setdefault(key, value)
            for key, value in sorted((payload.get("metrics") or {}).items()):
                if not isinstance(value, (dict, list)):
                    row[key] = value
            rows.append(row)
        failures = sorted(self.directories["failures"].glob("*.json"))
        fieldnames = sorted({key for row in rows for key in row})
        summary_path = self.directories["summary"] / "candidate_results.csv"
        with summary_path.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=fieldnames)
            if fieldnames:
                writer.writeheader()
                writer.writerows(rows)
        progress = [
            "# Final experiment progress",
            "",
            f"Updated: {utc_now()}",
            "",
            f"- Completed or skipped raw cases: {len(rows)}",
            f"- Failure records: {len(failures)}",
            f"- Final bundle: `{relative_to_repo(self.bundle)}`",
            "",
            "No failed or unsupported case is removed from this bundle.",
        ]
        (self.directories["summary"] / "PROGRESS.md").write_text("\n".join(progress) + "\n", encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plan", type=Path, help="Experiment plan YAML")
    parser.add_argument("--action", choices=("plan", "run", "status"), default="plan")
    parser.add_argument("--experiment", help="Case-insensitive experiment name filter")
    parser.add_argument("--limit", type=int, help="Limit candidates for smoke/debug execution")
    parser.add_argument("--force", action="store_true", help="Re-run matching completed candidates")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manager = ExperimentManager(args.plan)
    manager.initialize()
    candidates = manager.candidates(args.experiment)
    manager.write_candidate_manifest(candidates)
    if args.action == "plan":
        print(json.dumps({"plan": manager.plan["name"], "candidate_count": len(candidates), "bundle": str(manager.bundle)}, indent=2))
        return 0
    if args.action == "status":
        manager.write_summary()
        print((manager.directories["summary"] / "PROGRESS.md").read_text(encoding="utf-8"))
        return 0
    return manager.run(candidates, force=args.force, limit=args.limit)


if __name__ == "__main__":
    raise SystemExit(main())
