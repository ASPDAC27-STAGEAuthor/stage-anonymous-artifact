#!/usr/bin/env python3
"""Train a deterministic small CNN and evaluate all 10,000 MNIST test images."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import platform
import random
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader
from torchvision import datasets, transforms


class SmallMnistCnn(nn.Module):
    """LeNet-style network with two convolutions and three fully connected layers."""

    def __init__(self) -> None:
        super().__init__()
        self.conv1 = nn.Conv2d(1, 6, kernel_size=5)
        self.conv2 = nn.Conv2d(6, 16, kernel_size=5)
        self.pool = nn.AvgPool2d(2)
        self.relu = nn.ReLU()
        self.fc1 = nn.Linear(16 * 4 * 4, 120)
        self.fc2 = nn.Linear(120, 84)
        self.fc3 = nn.Linear(84, 10)

    def forward(self, value: torch.Tensor) -> torch.Tensor:
        value = self.pool(self.relu(self.conv1(value)))
        value = self.pool(self.relu(self.conv2(value)))
        value = torch.flatten(value, 1)
        value = self.relu(self.fc1(value))
        value = self.relu(self.fc2(value))
        return self.fc3(value)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_sha256(value: Any) -> str:
    payload = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    if not rows:
        raise RuntimeError(f"No rows for {path}")
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def set_determinism(seed: int) -> None:
    os.environ["CUBLAS_WORKSPACE_CONFIG"] = ":4096:8"
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)
    torch.use_deterministic_algorithms(True)


def train_epoch(
    model: nn.Module,
    loader: DataLoader,
    optimizer: torch.optim.Optimizer,
    criterion: nn.Module,
    device: torch.device,
) -> tuple[float, float]:
    model.train()
    loss_total = 0.0
    correct = 0
    seen = 0
    for values, labels in loader:
        values, labels = values.to(device), labels.to(device)
        optimizer.zero_grad(set_to_none=True)
        logits = model(values)
        loss = criterion(logits, labels)
        loss.backward()
        optimizer.step()
        loss_total += float(loss.item()) * labels.numel()
        correct += int((logits.argmax(dim=1) == labels).sum().item())
        seen += labels.numel()
    return loss_total / seen, correct / seen


@torch.no_grad()
def evaluate(model: nn.Module, loader: DataLoader, device: torch.device) -> dict[str, Any]:
    model.eval()
    rows: list[dict[str, Any]] = []
    confusion = [[0 for _ in range(10)] for _ in range(10)]
    prediction_bytes = bytearray()
    correct = 0
    index = 0
    started = time.perf_counter()
    for values, labels in loader:
        values, labels = values.to(device), labels.to(device)
        logits = model(values)
        probabilities = torch.softmax(logits, dim=1)
        confidence, predicted = probabilities.max(dim=1)
        for label, prediction, score in zip(labels.cpu(), predicted.cpu(), confidence.cpu()):
            truth = int(label.item())
            guess = int(prediction.item())
            ok = truth == guess
            rows.append({
                "index": index,
                "label": truth,
                "prediction": guess,
                "correct": ok,
                "confidence": round(float(score.item()), 9),
            })
            confusion[truth][guess] += 1
            prediction_bytes.extend((truth, guess, 1 if ok else 0))
            correct += int(ok)
            index += 1
    elapsed = time.perf_counter() - started
    return {
        "samples": index,
        "correct": correct,
        "accuracy": correct / index,
        "elapsed_seconds": elapsed,
        "predictions": rows,
        "confusion": confusion,
        "prediction_sha256": hashlib.sha256(prediction_bytes).hexdigest(),
    }


def layer_rows(test_images: int) -> list[dict[str, Any]]:
    layers = [
        ("conv1", "conv", 576, 6, 25, "1x28x28", "6x24x24", 6 * 25),
        ("conv2", "conv", 64, 16, 150, "6x12x12", "16x8x8", 16 * 150),
        ("fc1", "linear", 1, 120, 256, "256", "120", 120 * 256),
        ("fc2", "linear", 1, 84, 120, "120", "84", 84 * 120),
        ("fc3", "linear", 1, 10, 84, "84", "10", 10 * 84),
    ]
    rows = []
    for name, kind, m, n, k, input_shape, output_shape, weights in layers:
        macs = m * n * k
        rows.append({
            "layer": name,
            "kind": kind,
            "im2col_M": m,
            "im2col_N": n,
            "im2col_K": k,
            "input_shape": input_shape,
            "output_shape": output_shape,
            "macs_per_image": macs,
            "macs_full_test_set": macs * test_images,
            "weights": weights,
            "weight_bits_fp32": weights * 32,
            "stage_lowering": "matched-matmul-cycle-runtime",
        })
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, default=Path("experiments/aspdac/data"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--epochs", type=int, default=3)
    parser.add_argument("--batch-size", type=int, default=128)
    parser.add_argument("--test-batch-size", type=int, default=256)
    parser.add_argument("--seed", type=int, default=20260716)
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()
    if args.epochs <= 0:
        raise SystemExit("epochs must be positive")
    args.output.mkdir(parents=True, exist_ok=True)
    set_determinism(args.seed)
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    transform = transforms.Compose([
        transforms.ToTensor(),
        transforms.Normalize((0.1307,), (0.3081,)),
    ])
    train_set = datasets.MNIST(args.data_root, train=True, download=False, transform=transform)
    test_set = datasets.MNIST(args.data_root, train=False, download=False, transform=transform)
    generator = torch.Generator().manual_seed(args.seed)
    train_loader = DataLoader(
        train_set,
        batch_size=args.batch_size,
        shuffle=True,
        num_workers=0,
        generator=generator,
    )
    test_loader = DataLoader(test_set, batch_size=args.test_batch_size, shuffle=False, num_workers=0)
    model = SmallMnistCnn().to(device)
    optimizer = torch.optim.Adam(model.parameters(), lr=0.001)
    criterion = nn.CrossEntropyLoss()
    checkpoint = args.output / "checkpoint.pt"
    history: list[dict[str, Any]] = []
    start_epoch = 0
    if args.resume and checkpoint.exists():
        state = torch.load(checkpoint, map_location=device)
        model.load_state_dict(state["model"])
        optimizer.load_state_dict(state["optimizer"])
        history = state["history"]
        start_epoch = int(state["epoch"])
    started = datetime.now(timezone.utc).isoformat()
    for epoch in range(start_epoch, args.epochs):
        before = time.perf_counter()
        loss, accuracy = train_epoch(model, train_loader, optimizer, criterion, device)
        row = {
            "epoch": epoch + 1,
            "train_loss": loss,
            "train_accuracy": accuracy,
            "elapsed_seconds": time.perf_counter() - before,
        }
        history.append(row)
        torch.save({
            "epoch": epoch + 1,
            "model": model.state_dict(),
            "optimizer": optimizer.state_dict(),
            "history": history,
            "seed": args.seed,
        }, checkpoint)
        write_json(args.output / "checkpoint.json", {
            "schema_version": "aspg-mnist-cnn-checkpoint-1.0",
            "completed_epochs": epoch + 1,
            "target_epochs": args.epochs,
            "latest": row,
            "checkpoint_sha256": sha256(checkpoint),
        })
        print(json.dumps(row), flush=True)
    model_path = args.output / "small_mnist_cnn_state.pt"
    if not (args.resume and model_path.exists()):
        torch.save(model.state_dict(), model_path)
    first = evaluate(model, test_loader, device)
    second = evaluate(model, test_loader, device)
    repeat_equal = first["prediction_sha256"] == second["prediction_sha256"]
    if first["samples"] != 10_000 or not repeat_equal:
        raise RuntimeError("Full MNIST test set or deterministic repeat contract failed")
    write_csv(args.output / "training_history.csv", history)
    write_csv(args.output / "test_predictions.csv", first.pop("predictions"))
    second.pop("predictions")
    confusion_rows = [
        {"true_label": label, **{f"pred_{index}": count for index, count in enumerate(row)}}
        for label, row in enumerate(first.pop("confusion"))
    ]
    second.pop("confusion")
    write_csv(args.output / "confusion_matrix.csv", confusion_rows)
    layers = layer_rows(10_000)
    write_csv(args.output / "layer_profiles.csv", layers)
    raw_files = sorted(path for path in (args.data_root / "MNIST/raw").glob("*") if path.is_file())
    repo = Path(__file__).resolve().parents[3]
    git_commit = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=repo, check=True, capture_output=True, text=True
    ).stdout.strip()
    git_status = subprocess.run(
        ["git", "status", "--porcelain=v1"], cwd=repo, check=True, capture_output=True
    ).stdout
    invocation = [sys.executable, str(Path(__file__).resolve()), *sys.argv[1:]]
    summary = {
        "schema_version": "aspg-mnist-cnn-feasibility-1.0",
        "started_utc": started,
        "completed_utc": datetime.now(timezone.utc).isoformat(),
        "status": "completed",
        "git_commit": git_commit,
        "git_status_porcelain_sha256": hashlib.sha256(git_status).hexdigest(),
        "host": platform.node(),
        "command": invocation,
        "model": "SmallMnistCnn/LeNet-style",
        "architecture": "Conv1(1->6,5x5)-ReLU-AvgPool2-Conv2(6->16,5x5)-ReLU-AvgPool2-FC256-120-84-10",
        "seed": args.seed,
        "epochs": args.epochs,
        "train_samples": len(train_set),
        "test_samples": len(test_set),
        "device": str(device),
        "torch_version": torch.__version__,
        "python_version": platform.python_version(),
        "model_parameters": sum(parameter.numel() for parameter in model.parameters()),
        "model_sha256": sha256(model_path),
        "dataset_files": [
            {"path": str(path), "bytes": path.stat().st_size, "sha256": sha256(path)}
            for path in raw_files
        ],
        "test_repeat_1": first,
        "test_repeat_2": second,
        "repeat_prediction_hash_identical": repeat_equal,
        "layer_profile_sha256": canonical_sha256(layers),
        "macs_per_image": sum(row["macs_per_image"] for row in layers),
        "macs_full_test_set": sum(row["macs_full_test_set"] for row in layers),
        "claim_boundary": (
            "End-to-end numerical accuracy is a PyTorch functional oracle. STAGE consumes the exact "
            "Conv/im2col and FC layer shapes for cycle/data-movement feasibility; pooling/ReLU numerical "
            "execution is not claimed as a STAGE neural-network kernel."
        ),
    }
    write_json(args.output / "summary.json", summary)
    manifest_path = args.output / "manifest.json"
    manifest_files = sorted(
        path for path in args.output.rglob("*")
        if path.is_file() and path != manifest_path
    )
    write_json(manifest_path, {
        "schema_version": "aspg-mnist-cnn-manifest-1.0",
        "files": [
            {"path": str(path.relative_to(args.output)), "bytes": path.stat().st_size, "sha256": sha256(path)}
            for path in manifest_files
        ],
        "summary_sha256": sha256(args.output / "summary.json"),
    })
    print(json.dumps({
        "status": "completed",
        "git_commit": git_commit,
        "git_status_porcelain_sha256": hashlib.sha256(git_status).hexdigest(),
        "host": platform.node(),
        "command": invocation,
        "accuracy": first["accuracy"],
        "correct": first["correct"],
        "test_samples": first["samples"],
        "repeat_hash_identical": repeat_equal,
        "macs_per_image": summary["macs_per_image"],
        "output": str(args.output),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
