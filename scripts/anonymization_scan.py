#!/usr/bin/env python3
"""Fail on common identity leaks in paths, text, binary metadata, or Git state."""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output"
SKIP_PARTS = {".git", ".venv", "bin", "obj", "__pycache__", "output"}
TEXT_SUFFIXES = {".cs", ".csproj", ".sln", ".py", ".ps1", ".sh", ".md", ".txt", ".json", ".yaml", ".yml", ".csv", ".xml", ".toml", ".gitignore"}
IDENTITY_TOKEN = "mine" + "crash"
OLD_REPO = "F:" + "\\" + "github" + "\\" + "visual-hardware-ai-codesign-simulator"
WINDOWS_USER_ROOT = "C:" + "\\" + "Users" + "\\"
UNIX_HOME_ROOT = "/" + "home" + "/"
BANNED_TEXT = [
    re.compile(IDENTITY_TOKEN, re.IGNORECASE),
    re.compile(re.escape(OLD_REPO), re.IGNORECASE),
    re.compile(re.escape(WINDOWS_USER_ROOT) + r"[^\\\s,\"']+", re.IGNORECASE),
    re.compile(re.escape(UNIX_HOME_ROOT) + r"[^/\s,\"']+", re.IGNORECASE),
]
EMAIL = re.compile(r"(?<![\w.+-])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}(?![\w.-])")
ALLOWED_EMAIL_SUFFIXES = {"@users.noreply.github.com", "@example.invalid"}


def main() -> int:
    leaks: list[dict[str, object]] = []
    for git_marker in sorted(ROOT.rglob(".git")):
        relative = git_marker.relative_to(ROOT)
        if len(relative.parts) > 1:
            leaks.append({"path": relative.as_posix(), "reason": "nested Git history must not be shipped"})
    for path in sorted(ROOT.rglob("*")):
        if not path.is_file() or any(part in SKIP_PARTS for part in path.relative_to(ROOT).parts):
            continue
        relative = path.relative_to(ROOT).as_posix()
        if any(pattern.search(relative) for pattern in BANNED_TEXT):
            leaks.append({"path": relative, "reason": "identity-bearing filename"})
        data = path.read_bytes()
        if IDENTITY_TOKEN.encode("ascii") in data.lower():
            leaks.append({"path": relative, "reason": "identity token in binary/text bytes"})
            continue
        if path.suffix.lower() in TEXT_SUFFIXES or path.name == ".gitignore":
            text = data.decode("utf-8", errors="replace")
            for pattern in BANNED_TEXT:
                match = pattern.search(text)
                if match:
                    line = text.count("\n", 0, match.start()) + 1
                    leaks.append({"path": relative, "line": line, "reason": f"identity pattern: {match.group(0)[:80]}"})
                    break
            for email in EMAIL.findall(text):
                if not any(email.lower().endswith(suffix) for suffix in ALLOWED_EMAIL_SUFFIXES):
                    line = text.count("\n", 0, text.find(email)) + 1
                    leaks.append({"path": relative, "line": line, "reason": f"email address: {email}"})
    report = {"status": "pass" if not leaks else "fail", "leaks": leaks, "scanned_root": "."}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "anonymization_scan.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if not leaks else 1


if __name__ == "__main__":
    raise SystemExit(main())
