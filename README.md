# STAGE anonymous review artifact

This package contains the implementation, experiment contracts, frozen terminal evidence, tests, and plotting code for the submitted STAGE paper. It is intentionally history-free and identity-free. The default workflow builds the simulator, executes the exact-cycle golden and paper-claim tests, validates the frozen measurements, and regenerates the four result figures without requiring Unity, CUDA, or any external comparison tool.

## Quick start

### Windows PowerShell

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\run_all.ps1
```

### Linux or macOS

```bash
bash scripts/run_all.sh
```

The first run creates `.venv`, installs the pinned Python plotting dependencies, restores/builds the .NET projects, runs the tests, validates the data, and writes figures and reports under `output/`. Internet access is needed only for the first Python/.NET dependency restore. If the dependencies are already available, use `-SkipBootstrap` on PowerShell or `--skip-bootstrap` on Bash.

### Docker

```bash
docker build -t stage-artifact .
docker run --rm -v "${PWD}/output:/artifact/output" stage-artifact
```

The Docker image uses the .NET 8 SDK and a Python virtual environment. The default container command runs the same validation path as the host scripts.

## Claim-to-command matrix

Run commands from the artifact root. Times are conservative estimates for a normal CPU workstation after dependencies are installed; the first dependency restore can add 5--15 minutes. Commands labeled frozen replay assert supplied terminal evidence and do not silently invoke third-party simulators.

| Paper claim | Artifact command | Expected result | Typical time |
|---|---|---|---|
| Complete portable review path | Windows: <code>.\scripts\run_all.ps1</code><br>Linux/macOS: <code>bash scripts/run_all.sh</code> | Build succeeds; 7/7 golden and 25/25 paper tests pass; every frozen claim passes; eight Figure 3--6 files are generated | 1--3 min cached |
| Nine analytical contracts | <code>python scripts/validate_frozen.py --claim analytical</code> | 9/9 pass; 18 repeated runs are byte-identical; all nine expected SHA-256 values match | &lt;5 s |
| Seven live exact-cycle golden traces | <code>dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release -- --group golden</code> | 7/7 pass; each trace is regenerated twice and matches its locked SHA-256 | &lt;1 min |
| Seven supported NoC cycle contracts | <code>python scripts/validate_frozen.py --claim noc</code> | 14/14 runs across 7 cases pass the independent oracle and locked-hash checks; N07/N08 remain explicitly unsupported | &lt;5 s |
| Byte-identical repeated traces | <code>python scripts/validate_frozen.py --claim determinism</code> | Two runs for each of 9 analytical cases have identical canonical bytes and SHA-256 | &lt;5 s |
| Timeloop matched compute/accesses | <code>python scripts/validate_frozen.py --claim timeloop</code> | 5/5 compute-cycle cases and 20/20 hierarchy-access rows match exactly | &lt;5 s |
| SCALE-Sim hold-outs | <code>python scripts/validate_frozen.py --claim scalesim</code> | 16/16 paired runs pass; maximum relative cycle difference is 9.691109% within the predefined 10% engineering envelope | &lt;5 s |
| Accelergy shared ERT actions | <code>python scripts/validate_frozen.py --claim accelergy</code> | 9/9 action energies match exactly; ERT SHA-256 matches | &lt;5 s |
| BookSim congestion ordering | <code>python scripts/validate_frozen.py --claim booksim</code> | Both tools identify <code>hotspot_node5</code> first; 0.04 versus 0.08 is reported without an equivalence claim | &lt;5 s |
| BookSim matched input set | <code>python experiments/aspdac/scripts/generate_booksim_configs.py --output output/external-rerun/booksim-configs</code> | 530/530 generated configurations match their locked SHA-256 ledger | &lt;5 s |
| Controlled bottleneck attribution | <code>python scripts/validate_frozen.py --claim attribution</code> | Two accepted interventions produce the trace-connected NoC -> memory -> NoC diagnosis | &lt;5 s |
| Mapping/topology DSE | <code>python scripts/validate_frozen.py --claim mapping</code> | 24 candidates pass; cycle range 174--1741 and packet-move range 316--600 are recovered | &lt;5 s |
| MNIST precision study | <code>python scripts/validate_frozen.py --claim precision</code> | Four accuracy/traffic profiles and checkpoint/prediction hashes match | &lt;5 s |
| Electrical/optical intervention | <code>python scripts/validate_frozen.py --claim optical</code> | Effective payload is 4x; cycles are 8193 versus 2049 | &lt;5 s |
| Full-trace overhead record | <code>python scripts/validate_frozen.py --claim trace</code> | The three-repeat medians, 46,852,084 events, 3.92-GiB raw size, and compressed size match; no large trace is generated | &lt;5 s |
| Paper Figures 3--6 | Windows: <code>.\scripts\run_figures.ps1</code><br>Linux/macOS: <code>bash scripts/run_figures.sh</code> | Four PDF and four PNG files plus geometry/hash verification | &lt;1 min |

The selector reports are written to <code>output/claim_&lt;name&gt;.json</code>. The complete run writes <code>output/frozen_validation.json</code>. Expected trace hashes and mismatch localization are documented in <code>docs/CANONICAL_TRACES.md</code>. Frozen external-tool commits, versions, and binary/diff hashes are recorded without local paths in <code>experiments/aspdac/external_inputs/tool_versions.json</code>. Fresh BookSim, SCALE-Sim, Timeloop, and Accelergy reruns are intentionally separate; see <code>docs/EXTERNAL_TOOLS.md</code>.

## Expected result

A successful run ends with `Artifact validation passed` and produces:

- `output/run_summary.json`: overall status;
- `output/environment.json`: non-identifying environment inventory;
- `output/frozen_validation.json`: recomputed paper metrics and contract checks;
- `output/anonymization_scan.json`: identity-leak scan;
- `output/figure_verification.json`: generated figure hashes and geometry;
- `output/figures/`: regenerated Figures 3--6 in PDF and PNG form;
- `output/logs/`: per-step logs and exit markers; on Windows, .NET output is also streamed directly to the console.

The default path is designed to finish on a normal CPU workstation. The initial package installation and .NET restore usually dominate wall time.

## What the one-click workflow executes

1. Checks Python, .NET, pinned Python packages, disk space, and optional external tools.
2. Scans the static artifact for personal paths, host names, emails, and nested Git history.
3. Verifies every static file against `metadata/artifact_manifest.json`.
4. Restores and builds `STAGE.sln` sequentially.
5. Runs `golden`, the seven exact-cycle regression cases.
6. Runs `paper`, covering the paper experiment contracts, NoC oracle cases, hold-out shapes, optical intervention, precision accounting, and CIM output-stage accounting.
7. Recomputes the paper-facing validation values from the frozen CSV/JSON evidence.
8. Regenerates the four result figures and verifies their geometry against the submission references.

Build and regression commands are intentionally sequential because parallel .NET builds can contend on `obj/` files and give misleading failures.

## Package layout

| Path | Contents |
|---|---|
| `src/HardwareSim.Core/` | Typed graph, compiler, cycle engine, component models, event stream, metrics, lineage, NoC, optical, precision, and CIM logic. |
| `tools/HardwareSim.AspdacRunner/` | Non-Unity experiment runner used by the paper workflow. |
| `tests/HardwareSim.Tests/` | Curated 32-case paper-validation harness; internal development regressions are intentionally excluded. |
| `experiments/aspdac/specs/` | Frozen experiment plans, sweeps, schemas, mappings, and reviewer-extension contracts. |
| `experiments/aspdac/external_inputs/` | Locked BookSim configuration ledger and Timeloop workload/mapping decks for fresh external reruns. |
| `experiments/aspdac/scripts/` | Experiment managers, analysis scripts, external-tool adapters, and paper plotting code. |
| `experiments/aspdac/results/*/summary/` | Frozen terminal summaries used for tables, claims, and figures. |
| `experiments/aspdac/results/*/manifests/` | Selected plans, audits, output manifests, preregistration record, and provenance metadata. |
| `data/characterization/` | Literature-backed component characterization catalog used by the CIM contract. |
| `data/mnist/` | Compressed MNIST archives, frozen checkpoint, predictions, and functional summary. |
| `expected/figures/` | Submission-side visual references for figure-geometry checks. |
| `scripts/` | Portable environment, test, data-validation, plotting, integrity, and anonymization entry points. |
| `docs/` | Claim map, canonical-trace/hash contract, data boundary, external-tool instructions, and anonymization notes. |
| `LICENSE`, `NOTICE`, `THIRD_PARTY.md` | Apache-2.0 terms for STAGE-owned material and third-party scope notices. |

## Required environment

Portable default path:

- 64-bit Windows, Linux, or macOS;
- .NET SDK 8.0 or newer capable of targeting `net8.0`;
- Python 3.10 or newer;
- approximately 2 GiB free disk space for the environment and build outputs;
- no GPU and no Unity installation required.

Pinned Python packages are listed in `requirements.txt`. The .NET projects have one NuGet dependency (`System.Text.Json 8.0.5`) only for the `netstandard2.1` target of the core library.

The archived measurement host reported in the paper used Windows, .NET 8, an Intel Core i9-12900KF, and 64 GiB RAM. Those details describe the performance measurement, not a functional requirement.

## Fast individual commands

Build and run the `golden` and `paper` test groups:

```powershell
.\scripts\run_tests.ps1
```

Regenerate only the figures after installing `requirements.txt`:

```powershell
.\scripts\run_figures.ps1
```

Validate all frozen measurements, or one named paper claim:

~~~powershell
.\.venv\Scripts\python.exe scripts\validate_frozen.py
.\.venv\Scripts\python.exe scripts\validate_frozen.py --claim booksim
~~~

Bash equivalents are `scripts/run_tests.sh` and `scripts/run_figures.sh`.

## Evidence tiers and claim boundary

The package deliberately separates three levels of reproducibility:

- **Portable replay:** build, deterministic simulator contracts, frozen-data validation, and figure regeneration. This is the default path and is fully contained here.
- **Native STAGE rerun:** selected plans can be rerun with `HardwareSim.AspdacRunner` and the managers under `experiments/aspdac/scripts/`. Fresh results must go to a new output directory; frozen directories are read-only evidence.
- **Specialist/external rerun:** BookSim2, SCALE-Sim, Timeloop, Accelergy, ZigZag, and the full CUDA MNIST precision replay require separately installed third-party tools. Their configurations and terminal summaries are included; their source trees are not redistributed.

The external comparisons do not claim cycle equivalence across mismatched abstractions. Each result is labeled as exact, trend, numerical, or unsupported in the supplied summaries and claim registers.

## Why the 3.92-GiB trace is not included

The largest measured case was a 32x32 mesh with one million packets. Metrics-only execution retained the committed metric reductions without serializing every event. Full tracing wrote 46.9 million per-hop JSONL events: 3.92 GiB raw and 366 MiB compressed. Shipping that trace would exceed ordinary Git hosting limits and add no information needed to reproduce the paper figures, so this package includes the canonical hashes, event count, performance row, reduced spatial/temporal summaries, and trace-generation code instead.

For larger DNN or LLM-scale studies, use metrics-only mode by default and enable filtered/windowed tracing for selected packet IDs, components, phases, or time windows. Persistent full-event tracing should be streamed to chunked compressed files or object storage, never buffered in memory or committed to Git.

## MNIST precision replay

The default path checks the checkpoint/prediction hashes and validates the four frozen precision profiles. A full numerical replay is optional because the archived runner requires an NVIDIA GPU and the historical PyTorch/CUDA stack. See `requirements-mnist.txt` and `docs/EXTERNAL_TOOLS.md`. The four compressed MNIST archives are included so the test set and training provenance can be reconstructed without adding the duplicated uncompressed files.

## License

Original STAGE source code, scripts, documentation, and author-produced artifact metadata are released under the Apache License 2.0; see <code>LICENSE</code> and <code>NOTICE</code>. Third-party software and data are not relicensed and are listed in <code>THIRD_PARTY.md</code>. The anonymous copyright label <code>STAGE Authors</code> does not disclose author identity.

## Integrity and safe modification

`metadata/artifact_manifest.json` records the SHA-256 and size of every static package file. Generated paths (`.venv`, `bin`, `obj`, `output`, and caches) are excluded. Verify it with:

```bash
python scripts/build_manifest.py --verify
```

After an intentional static-file change, rebuild it with `python scripts/build_manifest.py`, review the diff, rerun the anonymization scan, and create a fresh history-free archive. Do not modify the frozen result directories in place.

## Troubleshooting

- `dotnet` not found: install the .NET 8 SDK, not only the runtime.
- Python package import failure: rerun without `SkipBootstrap`, or install `requirements.txt` in the active environment.
- Figure geometry mismatch: confirm the pinned package versions and remove only the generated `output/matplotlib` cache before rerunning.
- External tool missing: this does not block the default workflow; it is reported as optional in `output/environment.json`.
- Low disk space: the default package is small, but a fresh full trace may require more than 5 GiB for one case.

## Create the upload archive

After an intentional review and a successful one-click run, create a deterministic history-free ZIP plus SHA-256 sidecar:

```bash
python scripts/package_artifact.py
```

The archive is written beside this directory as `STAGE_ASPDAC27_Anonymous.zip`. Generated environments, build products, logs, caches, and nested Git state are excluded automatically.

For a concise reviewer checklist, start with `docs/ARTIFACT_EVALUATION.md`.
