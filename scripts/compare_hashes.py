#!/usr/bin/env python3
"""Compare two evidence files by SHA-256 and report the first localizable difference."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def first_json_difference(left: Any, right: Any, path: str = "$") -> str | None:
    if type(left) is not type(right):
        return f"{path}: type {type(left).__name__} != {type(right).__name__}"
    if isinstance(left, dict):
        left_keys = set(left)
        right_keys = set(right)
        if left_keys != right_keys:
            missing = sorted(left_keys - right_keys)
            extra = sorted(right_keys - left_keys)
            return f"{path}: missing_right={missing}, extra_right={extra}"
        for key in sorted(left):
            difference = first_json_difference(left[key], right[key], f"{path}.{key}")
            if difference:
                return difference
        return None
    if isinstance(left, list):
        if len(left) != len(right):
            return f"{path}: length {len(left)} != {len(right)}"
        for index, (left_item, right_item) in enumerate(zip(left, right)):
            difference = first_json_difference(left_item, right_item, f"{path}[{index}]")
            if difference:
                return difference
        return None
    if left != right:
        return f"{path}: {left!r} != {right!r}"
    return None


def first_byte_difference(left: bytes, right: bytes) -> dict[str, object]:
    common = min(len(left), len(right))
    offset = next((index for index in range(common) if left[index] != right[index]), common)
    line = left[:offset].count(b"\n") + 1
    last_newline = left.rfind(b"\n", 0, offset)
    column = offset - last_newline
    return {
        "byte_offset": offset,
        "line": line,
        "column": column,
        "left_context_hex": left[max(0, offset - 16):offset + 16].hex(),
        "right_context_hex": right[max(0, offset - 16):offset + 16].hex(),
        "left_bytes": len(left),
        "right_bytes": len(right),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("expected", type=Path)
    parser.add_argument("actual", type=Path)
    args = parser.parse_args()

    expected = args.expected.read_bytes()
    actual = args.actual.read_bytes()
    report: dict[str, object] = {
        "expected": args.expected.as_posix(),
        "actual": args.actual.as_posix(),
        "expected_sha256": sha256(expected),
        "actual_sha256": sha256(actual),
        "byte_identical": expected == actual,
    }
    if expected == actual:
        report["status"] = "pass"
        print(json.dumps(report, indent=2))
        return 0

    report["status"] = "mismatch"
    try:
        left_json = json.loads(expected.decode("utf-8"))
        right_json = json.loads(actual.decode("utf-8"))
        report["first_json_difference"] = first_json_difference(left_json, right_json) or "JSON values match; serialization bytes differ"
    except (UnicodeDecodeError, json.JSONDecodeError):
        report["first_byte_difference"] = first_byte_difference(expected, actual)
    else:
        report["first_byte_difference"] = first_byte_difference(expected, actual)
    print(json.dumps(report, indent=2))
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
