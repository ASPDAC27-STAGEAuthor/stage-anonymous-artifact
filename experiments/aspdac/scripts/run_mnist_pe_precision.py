#!/usr/bin/env python3
"""Deterministic MNIST replay under the frozen STAGE digital-PE arithmetic contract."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import os
import platform
import random
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import torch
import torch.nn.functional as F
from torch import nn
from torch.utils.data import DataLoader, Subset
from torchvision import datasets, transforms


REPO_ROOT = Path(__file__).resolve().parents[3]
LAYER_NAMES = ("conv1", "conv2", "fc1", "fc2", "fc3")
POSITIVE_FP8 = np.array(
    [0.0 if bits == 0 else (bits & 7) * 2.0**-9 if (bits >> 3) == 0
     else (1.0 + (bits & 7) / 8.0) * 2.0 ** ((bits >> 3) - 7)
     for bits in range(0x7f)], dtype=np.float64)


class SmallMnistCnn(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.conv1 = nn.Conv2d(1, 6, 5)
        self.conv2 = nn.Conv2d(6, 16, 5)
        self.fc1 = nn.Linear(16 * 4 * 4, 120)
        self.fc2 = nn.Linear(120, 84)
        self.fc3 = nn.Linear(84, 10)


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def repo_relative(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT).as_posix()


def fp8_scalar(value: float) -> tuple[float, int]:
    negative = bool(np.signbit(value))
    magnitude = abs(float(value))
    if magnitude <= 0:
        selected = 0
    elif magnitude >= POSITIVE_FP8[-1]:
        selected = len(POSITIVE_FP8) - 1
    else:
        upper = int(np.searchsorted(POSITIVE_FP8, magnitude, side="left"))
        lower = upper - 1
        lower_distance = magnitude - POSITIVE_FP8[lower]
        upper_distance = POSITIVE_FP8[upper] - magnitude
        selected = lower if lower_distance < upper_distance else upper
        if lower_distance == upper_distance:
            selected = lower if lower % 2 == 0 else upper
    code = selected | (0x80 if negative else 0)
    decoded = float(POSITIVE_FP8[selected]) * (-1.0 if negative else 1.0)
    return decoded, code


def fp8_codes_numpy(value: np.ndarray) -> np.ndarray:
    magnitude = np.abs(value.astype(np.float64, copy=False))
    upper = np.searchsorted(POSITIVE_FP8, magnitude, side="left").clip(1, len(POSITIVE_FP8) - 1)
    lower = upper - 1
    lower_distance = magnitude - POSITIVE_FP8[lower]
    upper_distance = POSITIVE_FP8[upper] - magnitude
    choose_lower = (lower_distance < upper_distance) | ((lower_distance == upper_distance) & ((lower & 1) == 0))
    selected = np.where(choose_lower, lower, upper)
    selected = np.where(magnitude == 0, 0, selected)
    selected = np.where(magnitude >= POSITIVE_FP8[-1], len(POSITIVE_FP8) - 1, selected)
    return (selected | (np.signbit(value).astype(np.int64) << 7)).astype(np.uint8)

def half_scalar(value: float) -> tuple[float, int]:
    clipped = max(-65504.0, min(65504.0, float(value)))
    encoded = np.float16(clipped)
    return float(encoded), int(encoded.view(np.uint16))


def single_scalar(value: float) -> tuple[float, int]:
    clipped = max(-float(np.finfo(np.float32).max), min(float(np.finfo(np.float32).max), float(value)))
    encoded = np.float32(clipped)
    return float(encoded), int(encoded.view(np.uint32))


def quantize_scalar(value: float, dtype: str) -> tuple[float, int]:
    return fp8_scalar(value) if dtype == "fp8" else half_scalar(value) if dtype == "fp16" else single_scalar(value)


def fp8_tensor(value: torch.Tensor, positive: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
    magnitude = value.abs().to(torch.float64)
    upper = torch.bucketize(magnitude, positive).clamp(1, len(POSITIVE_FP8) - 1)
    lower = upper - 1
    lower_value = positive[lower]
    upper_value = positive[upper]
    lower_distance = magnitude - lower_value
    upper_distance = upper_value - magnitude
    choose_lower = (lower_distance < upper_distance) | ((lower_distance == upper_distance) & ((lower & 1) == 0))
    selected = torch.where(choose_lower, lower, upper)
    selected = torch.where(magnitude == 0, torch.zeros_like(selected), selected)
    selected = torch.where(magnitude >= positive[-1], torch.full_like(selected, len(POSITIVE_FP8) - 1), selected)
    sign = torch.signbit(value).to(torch.long) << 7
    code = (selected | sign).to(torch.uint8)
    decoded = positive[selected].to(torch.float32)
    decoded = torch.where(torch.signbit(value), -decoded, decoded)
    return decoded, code


def half_tensor(value: torch.Tensor) -> torch.Tensor:
    return value.clamp(-65504.0, 65504.0).to(torch.float16)


def decode_fp8_codes(codes: torch.Tensor, positive: torch.Tensor) -> torch.Tensor:
    magnitude = positive[(codes.to(torch.long) & 0x7f).clamp_max(len(POSITIVE_FP8) - 1)].to(torch.float32)
    return torch.where((codes & 0x80) != 0, -magnitude, magnitude)


def build_fp8_tables(cache: Path, device: torch.device) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    cache.parent.mkdir(parents=True, exist_ok=True)
    values = np.zeros(256, dtype=np.float64)
    values[:0x7f] = POSITIVE_FP8
    values[0x80:0xff] = -POSITIVE_FP8
    product = (values[:, None] * values[None, :]).astype(np.float32)
    if cache.exists():
        transition = np.load(cache, allow_pickle=False)
    else:
        transition = np.zeros((256, 256, 256), dtype=np.uint8)
        product64 = values[:, None] * values[None, :]
        for accumulator_code in range(256):
            transition[accumulator_code] = fp8_codes_numpy(values[accumulator_code] + product64)
        np.save(cache, transition, allow_pickle=False)
    positive = torch.tensor(POSITIVE_FP8, dtype=torch.float64, device=device)
    return positive, torch.from_numpy(product.reshape(-1)).to(device), torch.from_numpy(transition.reshape(-1)).to(device)


def encode_tensor(value: torch.Tensor, dtype: str, fp8_codes: torch.Tensor | None = None) -> bytes:
    if dtype == "fp8":
        if fp8_codes is None:
            raise ValueError("FP8 output codes are required")
        return fp8_codes.detach().cpu().contiguous().numpy().tobytes()
    array = value.detach().cpu().contiguous().numpy()
    return array.view(np.uint16 if dtype == "fp16" else np.uint32).tobytes()

class ArithmeticBackend:
    def __init__(self, profile: dict[str, Any], device: torch.device, runtime: Path) -> None:
        self.profile = profile
        self.device = device
        if str(profile["input_dtype"]) == "fp8":
            self.positive, self.fp8_products, self.fp8_transition = build_fp8_tables(
                runtime / "fp8_transition_e4m3fn.npy", device)
        else:
            self.positive = torch.tensor(POSITIVE_FP8, dtype=torch.float64, device=device)
            self.fp8_products = torch.empty(0, dtype=torch.float32, device=device)
            self.fp8_transition = torch.empty(0, dtype=torch.uint8, device=device)

    @property
    def profile_id(self) -> str:
        return str(self.profile["profile_id"])

    def vmm(self, activation: torch.Tensor, weights: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor | None]:
        if activation.ndim != 2 or weights.ndim != 2 or activation.shape[1] != weights.shape[0]:
            raise ValueError(f"Invalid VMM shapes {tuple(activation.shape)} @ {tuple(weights.shape)}")
        points, rows = activation.shape
        columns = weights.shape[1]
        profile_id = self.profile_id
        if profile_id == "fp32_a32":
            qa = activation.to(torch.float32)
            qw = weights.to(torch.float32)
            accumulator = torch.zeros((points, columns), dtype=torch.float32, device=self.device)
            for row in range(rows):
                updated = accumulator.double() + qa[:, row, None].double() * qw[row, None, :].double()
                accumulator = updated.to(torch.float32)
            return accumulator, None
        if profile_id == "fp16_a16":
            qa = half_tensor(activation)
            qw = half_tensor(weights)
            accumulator = torch.zeros((points, columns), dtype=torch.float16, device=self.device)
            for row in range(rows):
                updated = accumulator.float() + qa[:, row, None].float() * qw[row, None, :].float()
                accumulator = half_tensor(updated)
            return accumulator, None
        _, activation_codes = fp8_tensor(activation, self.positive)
        _, weight_codes = fp8_tensor(weights, self.positive)
        if profile_id == "fp8_a16":
            accumulator = torch.zeros((points, columns), dtype=torch.float16, device=self.device)
            for row in range(rows):
                product_index = (activation_codes[:, row, None].long() << 8) | weight_codes[row, None, :].long()
                updated = accumulator.float() + self.fp8_products[product_index]
                accumulator = half_tensor(updated)
            output, output_codes = fp8_tensor(accumulator.float(), self.positive)
            return output, output_codes
        if profile_id != "fp8_a8":
            raise ValueError(f"Unsupported arithmetic profile {profile_id}")
        accumulator_codes = torch.zeros((points, columns), dtype=torch.uint8, device=self.device)
        for row in range(rows):
            transition_index = (
                (accumulator_codes.long() << 16)
                | (activation_codes[:, row, None].long() << 8)
                | weight_codes[row, None, :].long()
            )
            accumulator_codes = self.fp8_transition[transition_index]
        return decode_fp8_codes(accumulator_codes, self.positive), accumulator_codes

    def scalar_audit(self, activation: torch.Tensor, weights: torch.Tensor) -> dict[str, Any]:
        activation_values = activation.detach().cpu().numpy().astype(np.float64)
        weight_values = weights.detach().cpu().numpy().astype(np.float64)
        rows, columns = weight_values.shape
        profile = self.profile
        reference_values: list[float] = []
        reference_codes: list[int] = []
        for column in range(columns):
            accumulator = quantize_scalar(0.0, str(profile["accumulate_dtype"]))[0]
            for row in range(rows):
                input_value = quantize_scalar(float(activation_values[0, row]), str(profile["input_dtype"]))[0]
                weight_value = quantize_scalar(float(weight_values[row, column]), str(profile["weight_dtype"]))[0]
                accumulator = quantize_scalar(
                    accumulator + input_value * weight_value, str(profile["accumulate_dtype"]))[0]
            value, code = quantize_scalar(accumulator, str(profile["output_dtype"]))
            reference_values.append(value)
            reference_codes.append(code)
        actual, actual_codes = self.vmm(activation, weights)
        if actual_codes is None:
            raw = encode_tensor(actual, str(profile["output_dtype"]))
            width = int(profile["payload_bits"]) // 8
            actual_code_list = [int.from_bytes(raw[i:i + width], "little") for i in range(0, len(raw), width)]
        else:
            actual_code_list = [int(item) for item in actual_codes.detach().cpu().reshape(-1)]
        return {
            "shape": [rows, columns],
            "reference_codes": reference_codes,
            "actual_codes": actual_code_list,
            "encoded_exact": reference_codes == actual_code_list,
            "reference_values": reference_values,
        }


def materialized_im2col(value: torch.Tensor, kernel: int = 5) -> torch.Tensor:
    batch, channels, height, width = value.shape
    windows = value.unfold(2, kernel, 1).unfold(3, kernel, 1)
    return windows.permute(0, 2, 3, 1, 4, 5).contiguous().reshape(
        batch, (height - kernel + 1) * (width - kernel + 1), channels * kernel * kernel)


def lowering_audit(value: torch.Tensor) -> dict[str, Any]:
    primary = materialized_im2col(value)
    independent = F.unfold(value, kernel_size=5).transpose(1, 2).contiguous()
    difference = (primary - independent).abs()
    return {
        "primary_shape": list(primary.shape),
        "independent_shape": list(independent.shape),
        "shape_equal": primary.shape == independent.shape,
        "max_abs_error": float(difference.max().item()),
        "exact": bool(torch.equal(primary, independent)),
    }


def conv_layer(
    value: torch.Tensor,
    layer: nn.Conv2d,
    backend: ArithmeticBackend,
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor | None]:
    batch = value.shape[0]
    lowered = materialized_im2col(value)
    matrix = lowered.reshape(-1, lowered.shape[-1])
    weights = layer.weight.reshape(layer.out_channels, -1).transpose(0, 1).contiguous()
    pe_output, codes = backend.vmm(matrix, weights)
    spatial = int(math.isqrt(lowered.shape[1]))
    pe_output = pe_output.reshape(batch, spatial, spatial, layer.out_channels).permute(0, 3, 1, 2).contiguous()
    if codes is not None:
        codes = codes.reshape(batch, spatial, spatial, layer.out_channels).permute(0, 3, 1, 2).contiguous()
    post_op = F.avg_pool2d(F.relu(pe_output.float() + layer.bias[None, :, None, None]), 2)
    return post_op, pe_output, codes


def linear_layer(
    value: torch.Tensor,
    layer: nn.Linear,
    backend: ArithmeticBackend,
    relu: bool,
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor | None]:
    pe_output, codes = backend.vmm(value, layer.weight.transpose(0, 1).contiguous())
    post_op = pe_output.float() + layer.bias[None, :]
    return (F.relu(post_op) if relu else post_op), pe_output, codes


def forward_bridge(
    model: SmallMnistCnn,
    values: torch.Tensor,
    backend: ArithmeticBackend,
) -> tuple[torch.Tensor, dict[str, tuple[torch.Tensor, torch.Tensor | None]]]:
    value, conv1, conv1_codes = conv_layer(values, model.conv1, backend)
    value, conv2, conv2_codes = conv_layer(value, model.conv2, backend)
    value = value.reshape(value.shape[0], -1)
    value, fc1, fc1_codes = linear_layer(value, model.fc1, backend, True)
    value, fc2, fc2_codes = linear_layer(value, model.fc2, backend, True)
    logits, fc3, fc3_codes = linear_layer(value, model.fc3, backend, False)
    return logits, {
        "conv1": (conv1, conv1_codes), "conv2": (conv2, conv2_codes),
        "fc1": (fc1, fc1_codes), "fc2": (fc2, fc2_codes), "fc3": (fc3, fc3_codes),
    }

def verify_frozen_inputs(config: dict[str, Any], image_count: int) -> dict[str, Any]:
    model = config["model"]
    checkpoint = REPO_ROOT / model["checkpoint_path"]
    prediction = REPO_ROOT / model["original_prediction_path"]
    if sha256_file(checkpoint) != model["checkpoint_sha256"]:
        raise RuntimeError("Frozen checkpoint SHA-256 changed")
    if sha256_file(prediction) != model["original_prediction_file_sha256"]:
        raise RuntimeError("Frozen original-prediction SHA-256 changed")
    rows: list[dict[str, Any]] = []
    for expected in config["dataset"]["files"]:
        path = REPO_ROOT / expected["path"]
        actual = {"path": expected["path"], "bytes": path.stat().st_size, "sha256": sha256_file(path)}
        if actual != expected:
            raise RuntimeError(f"Frozen dataset file changed: {expected['path']}")
        rows.append(actual)
    manifest_hash = sha256_bytes(canonical_json(rows).encode("utf-8"))
    if manifest_hash != config["dataset"]["dataset_manifest_sha256"]:
        raise RuntimeError("Frozen dataset manifest SHA-256 changed")
    indices = list(range(image_count))
    order_hash = sha256_bytes(canonical_json(indices).encode("utf-8"))
    expected_order_hash = (
        config["dataset"]["smoke_order_sha256"] if image_count == len(config["dataset"]["smoke_indices"])
        else config["dataset"]["sample_order_sha256"])
    if order_hash != expected_order_hash:
        raise RuntimeError("Frozen sample order SHA-256 changed")
    return {
        "checkpoint_sha256": model["checkpoint_sha256"],
        "original_prediction_file_sha256": model["original_prediction_file_sha256"],
        "original_prediction_semantic_sha256": model["original_prediction_semantic_sha256"],
        "dataset_manifest_sha256": manifest_hash,
        "sample_order_sha256": order_hash,
        "normalization_sha256": config["dataset"]["normalization_sha256"],
        "architecture_sha256": model["architecture_sha256"],
        "lowering_sha256": config["contracts"]["lowering_sha256"],
        "post_op_sha256": config["contracts"]["post_op_sha256"],
    }


def set_determinism(seed: int) -> None:
    os.environ["CUBLAS_WORKSPACE_CONFIG"] = ":4096:8"
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)
    torch.use_deterministic_algorithms(True)


def git_output(*arguments: str) -> str:
    return subprocess.run(
        ["git", *arguments], cwd=REPO_ROOT, check=True, capture_output=True,
        text=True, encoding="utf-8", errors="replace").stdout.strip()


def aggregate_layer_metrics(batch_rows: list[dict[str, Any]]) -> dict[str, dict[str, float]]:
    output: dict[str, dict[str, float]] = {}
    for layer in LAYER_NAMES:
        count = sum(int(row["layer_metrics"][layer]["count"]) for row in batch_rows)
        squared = sum(float(row["layer_metrics"][layer]["sum_squared_error"]) for row in batch_rows)
        dot = sum(float(row["layer_metrics"][layer]["dot"]) for row in batch_rows)
        norm_actual = sum(float(row["layer_metrics"][layer]["norm_actual_squared"]) for row in batch_rows)
        norm_reference = sum(float(row["layer_metrics"][layer]["norm_reference_squared"]) for row in batch_rows)
        denominator = math.sqrt(norm_actual * norm_reference)
        output[layer] = {
            "elements": count,
            "rmse": math.sqrt(squared / count) if count else 0.0,
            "max_abs_error": max(float(row["layer_metrics"][layer]["max_abs_error"]) for row in batch_rows),
            "cosine_similarity": dot / denominator if denominator else 1.0,
        }
    return output


@torch.no_grad()
def run_once(
    *,
    candidate_id: str,
    pass_index: int,
    image_count: int,
    model: SmallMnistCnn,
    loader: DataLoader,
    backend: ArithmeticBackend,
    profile: dict[str, Any],
    runtime: Path,
    baseline_writer: bool,
    smoke: bool,
) -> dict[str, Any]:
    checkpoint_dir = runtime / "checkpoints" / candidate_id / f"pass_{pass_index}"
    checkpoint_dir.mkdir(parents=True, exist_ok=True)
    baseline_dir = runtime / f"fp32_layer_baseline_{image_count}"
    baseline_dir.mkdir(parents=True, exist_ok=True)
    output_dtype = str(profile["output_dtype"])
    if torch.cuda.is_available():
        torch.cuda.reset_peak_memory_stats()
    started = time.perf_counter()
    batch_rows: list[dict[str, Any]] = []
    for batch_index, (values, labels) in enumerate(loader):
        checkpoint = checkpoint_dir / f"batch_{batch_index:04d}.json"
        baseline_path = baseline_dir / f"batch_{batch_index:04d}.pt"
        if checkpoint.exists():
            batch_rows.append(json.loads(checkpoint.read_text(encoding="utf-8")))
            continue
        values = values.to(backend.device, non_blocking=False)
        labels = labels.to(backend.device, non_blocking=False)
        lowering = lowering_audit(values[: min(2, len(values))]) if smoke and batch_index == 0 else None
        scalar = None
        if smoke and batch_index == 0:
            lowered = materialized_im2col(values[:1]).reshape(-1, 25)
            weights = model.conv1.weight.reshape(6, 25).transpose(0, 1).contiguous()
            scalar = backend.scalar_audit(lowered[:1], weights[:, :3])
        logits, layer_outputs = forward_bridge(model, values, backend)
        predictions = logits.argmax(dim=1)
        if baseline_writer:
            torch.save({name: layer_outputs[name][0].detach().cpu().float() for name in LAYER_NAMES}, baseline_path)
        if not baseline_path.exists():
            raise RuntimeError(f"Missing FP32 baseline batch for paired layer error: {baseline_path}")
        reference = torch.load(baseline_path, map_location="cpu")
        layer_hashes: dict[str, str] = {}
        layer_metrics: dict[str, dict[str, float | int]] = {}
        non_finite = 0
        for name in LAYER_NAMES:
            actual, codes = layer_outputs[name]
            layer_hashes[name] = sha256_bytes(encode_tensor(actual, output_dtype, codes))
            actual_fp64 = actual.double()
            reference_fp64 = reference[name].to(backend.device).double()
            difference = actual_fp64 - reference_fp64
            non_finite += int((~torch.isfinite(actual_fp64)).sum().item())
            layer_metrics[name] = {
                "count": actual.numel(),
                "sum_squared_error": float((difference * difference).sum().item()),
                "max_abs_error": float(difference.abs().max().item()),
                "dot": float((actual_fp64 * reference_fp64).sum().item()),
                "norm_actual_squared": float((actual_fp64 * actual_fp64).sum().item()),
                "norm_reference_squared": float((reference_fp64 * reference_fp64).sum().item()),
            }
        row = {
            "batch_index": batch_index,
            "sample_start": batch_index * loader.batch_size,
            "sample_count": int(labels.numel()),
            "labels": [int(item) for item in labels.cpu()],
            "predictions": [int(item) for item in predictions.cpu()],
            "correct": int((predictions == labels).sum().item()),
            "layer_hashes": layer_hashes,
            "layer_metrics": layer_metrics,
            "non_finite_values": non_finite,
            "lowering_audit": lowering,
            "scalar_backend_audit": scalar,
        }
        write_json(checkpoint, row)
        batch_rows.append(row)
        print(f"batch={batch_index + 1}/{len(loader)} profile={backend.profile_id} pass={pass_index}", flush=True)
    labels_all = [item for row in batch_rows for item in row["labels"]]
    predictions_all = [item for row in batch_rows for item in row["predictions"]]
    if len(predictions_all) != image_count:
        raise RuntimeError(f"Expected {image_count} predictions, received {len(predictions_all)}")
    prediction_bytes = bytes(
        component for label, prediction in zip(labels_all, predictions_all)
        for component in (label, prediction, int(label == prediction)))
    layer_hashes = {
        layer: sha256_bytes(canonical_json([row["layer_hashes"][layer] for row in batch_rows]).encode("utf-8"))
        for layer in LAYER_NAMES
    }
    prediction_hash = sha256_bytes(prediction_bytes)
    correct = sum(int(label == prediction) for label, prediction in zip(labels_all, predictions_all))
    artifact = runtime / "candidates" / f"{candidate_id}_pass{pass_index}_predictions.json"
    write_json(artifact, {"indices": list(range(image_count)), "labels": labels_all, "predictions": predictions_all})
    result_core = {
        "profile_id": backend.profile_id,
        "pass_index": pass_index,
        "images": image_count,
        "correct": correct,
        "accuracy": correct / image_count,
        "prediction_hash": prediction_hash,
        "layer_hashes": layer_hashes,
        "layer_metrics_vs_fp32": aggregate_layer_metrics(batch_rows),
        "non_finite_values": sum(int(row["non_finite_values"]) for row in batch_rows),
        "lowering_audit": next((row["lowering_audit"] for row in batch_rows if row["lowering_audit"]), None),
        "scalar_backend_audit": next((row["scalar_backend_audit"] for row in batch_rows if row["scalar_backend_audit"]), None),
        "prediction_artifact": repo_relative(artifact),
        "prediction_artifact_sha256": sha256_file(artifact),
    }
    deterministic_summary = {
        key: result_core[key]
        for key in (
            "profile_id", "images", "correct", "accuracy", "prediction_hash", "layer_hashes",
            "layer_metrics_vs_fp32", "non_finite_values", "lowering_audit", "scalar_backend_audit")
    }
    result_core["summary_hash"] = sha256_bytes(canonical_json(deterministic_summary).encode("utf-8"))
    result_core["elapsed_seconds"] = time.perf_counter() - started
    result_core["peak_gpu_memory_bytes"] = int(torch.cuda.max_memory_allocated()) if torch.cuda.is_available() else 0
    return result_core

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--bundle-root", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    candidate = json.loads(args.input.read_text(encoding="utf-8"))
    candidate_id = str(candidate["candidate_id"])
    scenario = str(candidate["scenario"])
    if scenario not in ("mnist_pe_smoke", "mnist_pe_functional"):
        raise ValueError(f"Unsupported MNIST PE scenario {scenario}")
    config = candidate["resolved"]["base_config"]
    parameters = candidate["resolved"]["parameters"]
    profile_id = str(parameters["profile_id"])
    profiles = {str(item["profile_id"]): item for item in config["arithmetic_profiles"]}
    if profile_id not in profiles:
        raise ValueError(f"Missing arithmetic profile {profile_id}")
    profile = profiles[profile_id]
    image_count = int(parameters["image_count"])
    if image_count not in (100, 10000):
        raise ValueError(f"MNIST PE runner requires the frozen 100 or 10000 image count, received {image_count}")
    if not torch.cuda.is_available():
        raise RuntimeError("The frozen functional bridge requires CUDA; CPU fallback is not authorized")
    seed = int(config["functional_bridge"]["seed"])
    set_determinism(seed)
    freeze = verify_frozen_inputs(config, image_count)
    device = torch.device("cuda")
    runtime = args.bundle_root.resolve() / "runtime"
    runtime.mkdir(parents=True, exist_ok=True)
    model = SmallMnistCnn().to(device)
    checkpoint = REPO_ROOT / config["model"]["checkpoint_path"]
    model.load_state_dict(torch.load(checkpoint, map_location=device))
    model.eval()
    transform = transforms.Compose([
        transforms.ToTensor(),
        transforms.Normalize(tuple(config["dataset"]["normalization"]["mean"]), tuple(config["dataset"]["normalization"]["std"])),
    ])
    dataset = datasets.MNIST(REPO_ROOT / config["dataset"]["root"], train=False, download=False, transform=transform)
    indices = list(range(image_count))
    loader = DataLoader(
        Subset(dataset, indices), batch_size=int(config["functional_bridge"]["batch_size"]),
        shuffle=False, num_workers=0, pin_memory=False)
    backend = ArithmeticBackend(profile, device, runtime)
    smoke = scenario == "mnist_pe_smoke"
    repeat_axis = int(parameters.get("repeat", 0))
    run_count = int(parameters["internal_repeats"]) if smoke else 1
    runs: list[dict[str, Any]] = []
    started_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    for pass_index in range(run_count):
        baseline_writer = profile_id == "fp32_a32" and (
            (smoke and pass_index == 0) or (not smoke and repeat_axis == 0))
        runs.append(run_once(
            candidate_id=candidate_id,
            pass_index=pass_index,
            image_count=image_count,
            model=model,
            loader=loader,
            backend=backend,
            profile=profile,
            runtime=runtime,
            baseline_writer=baseline_writer,
            smoke=smoke,
        ))
    repeat_exact = None
    if smoke:
        repeat_exact = all(
            runs[0][key] == runs[1][key]
            for key in ("prediction_hash", "layer_hashes", "summary_hash"))
        gate_checks = {
            "repeat_exact": repeat_exact,
            "lowering_exact": bool(runs[0]["lowering_audit"]["exact"]),
            "scalar_backend_encoded_exact": bool(runs[0]["scalar_backend_audit"]["encoded_exact"]),
            "no_non_finite": all(run["non_finite_values"] == 0 for run in runs),
            "all_images_present": all(run["images"] == image_count for run in runs),
            "all_layers_present": all(set(run["layer_hashes"]) == set(LAYER_NAMES) for run in runs),
        }
        if not all(gate_checks.values()):
            raise RuntimeError(f"MNIST PE smoke gate failed: {gate_checks}")
    else:
        gate_checks = {"complete_test_set": runs[0]["images"] == 10000, "no_non_finite": runs[0]["non_finite_values"] == 0}
        if not all(gate_checks.values()):
            raise RuntimeError(f"MNIST PE functional gate failed: {gate_checks}")
    primary = runs[0]
    git_status = git_output("status", "--porcelain=v1")
    result = {
        "status": "completed",
        "completed_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "started_utc": started_utc,
        "measurement_kind": "measured_functional_bridge",
        "metrics": {
            "images": primary["images"],
            "correct": primary["correct"],
            "accuracy": primary["accuracy"],
            "repeat_exact": repeat_exact,
            "non_finite_values": primary["non_finite_values"],
            "peak_gpu_memory_bytes": max(run["peak_gpu_memory_bytes"] for run in runs),
            "functional_wall_seconds": sum(run["elapsed_seconds"] for run in runs),
        },
        "profile": profile,
        "freeze": freeze,
        "gate_checks": gate_checks,
        "runs": runs,
        "seed": seed,
        "bootstrap_seed": int(config["functional_bridge"]["bootstrap_seed"]),
        "git_commit": git_output("rev-parse", "HEAD"),
        "dirty_worktree": bool(git_status),
        "git_status_porcelain_sha256": sha256_bytes(git_status.encode("utf-8")),
        "host": platform.node(),
        "python_version": platform.python_version(),
        "runner_source_sha256": sha256_file(Path(__file__).resolve()),
        "torch_version": torch.__version__,
        "cuda_version": torch.version.cuda,
        "device": torch.cuda.get_device_name(device),
        "command": [os.fspath(Path(__file__).resolve()), *os.sys.argv[1:]],
        "limitations": [
            "Bias, ReLU, AvgPool, layer orchestration, and argmax run in a deterministic functional harness.",
            "This is not native end-to-end STAGE CNN execution and is not silicon accuracy evidence.",
        ],
        "claim_boundary": {
            "accuracy_evidence": "Measured functional bridge",
            "native_stage_full_cnn": False,
            "C-MNIST-STAGE-ENDTOEND": "not_supported",
        },
    }
    write_json(args.output, result)
    print(json.dumps({
        "candidate_id": candidate_id, "scenario": scenario, "profile_id": profile_id,
        "accuracy": primary["accuracy"], "correct": primary["correct"],
        "prediction_hash": primary["prediction_hash"], "repeat_exact": repeat_exact,
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())