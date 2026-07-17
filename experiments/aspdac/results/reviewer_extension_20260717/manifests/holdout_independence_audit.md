# P1 Hold-out Independence Audit

Generated UTC: `2026-07-16T19:37:58.6923611Z`

## Scope and outcome

- Scope: the eight frozen 4x4 weight-stationary hold-out workloads, each run at repeats 0 and 1 through both STAGE and SCALE-Sim.
- Pair gate: `16/16` repeat-level STAGE/SCALE-Sim pairs are `ready`; `0` are blocked.
- This artifact audits input independence, repeat stability, provenance, and evidence boundaries. It does **not** establish or generalize simulator accuracy.

## STAGE source audit

- Audited source: `src/HardwareSim.Core/AspdacReviewerHoldoutRuntime.cs`.
- Audited source SHA-256: `10a60f6af0a2b252815c77397d444525335a8c2057454542cf570281c4a8e882`.
- Forbidden-token hits: `0` (`passed=true`).
- The forbidden list covered SCALE-Sim/Timeloop references, external/reference paths, raw paths, schedule CSVs, and access-count CSVs.
- The audit reported `16` stable STAGE hold-out candidate IDs.
- Machine-readable evidence: `manifests/p1_source_audit.json`.

The zero-hit result applies to the audited hold-out runtime source above. It is not a claim that every file in the repository was exhaustively audited.

## Allowed and prohibited inputs

Allowed shared high-level contract:

- Workload identity and dimensions: `case_id`, `M`, `N`, `K`.
- Precision/repetition controls: `precision_bits=16`, `repeat`, `seed=40`.
- Frozen architecture/mapping contract: 4x4 array, one MAC per PE per cycle, weight-stationary dataflow, frozen public memory/interface configuration.

Prohibited cross-tool inputs:

- SCALE-Sim- or Timeloop-generated schedules, cycle counts, utilization values, access counts, bandwidth results, or report CSVs as STAGE inputs.
- STAGE-generated schedules, packet release cycles, traces, counts, or result JSON/CSV values as SCALE-Sim inputs.
- Any external/reference raw path used as a hidden lowering input.

The SCALE-Sim adapter validates an exact allowlist of `case_id`, `M`, `N`, `K`, `precision_bits`, `repeat`, and `seed`, materializes its own topology/config from those fields, and does not read `raw/stage`.

## Repeat stability

- STAGE: all `8/8` workload pairs have two repeats and exactly one `canonical_trace_sha256` per workload; unstable pairs: `0`.
- SCALE-Sim: all `8/8` workload pairs have two repeats and exactly one `canonical_metrics_sha256` per workload; unstable pairs: `0`.
- Repeat stability is a within-tool determinism result. It is not an accuracy score.

## SCALE-Sim provenance

- Repository: `/opt/stage-baselines/tools/SCALE-Sim`.
- Git commit: `9f98c4371055a54c75209c2e02b640b897550532` (matches the frozen expected commit).
- Python: `/opt/stage-baselines/venv/bin/python`.
- Known local compatibility modifications:
  - `scalesim/memory/double_buffered_scratchpad_mem.py`
  - `scalesim/memory/read_buffer.py`
- Hash of the captured compatibility diff over those two files: `ee0f1075ace7dc2b299e9cfaf4bc9f423b7fdfb9747ecff20f542dfa5f3dad1b`.
- The tool working tree is therefore not pristine; every SCALE-Sim raw result records the commit, status, compatibility-diff hash, command, and wall time.

## Comparison boundary

- **Exact:** frozen workload shape, 4x4 weight-stationary tool configuration, per-tool raw provenance, and within-tool repeat hashes.
- **Trend:** cross-tool total/warm cycles, utilization, and bandwidth because native scheduling, arbitration, prefetch, and trace-window definitions differ.
- **Numerical only under a shared counter definition:** SRAM/DRAM access counts. If the counter semantics are not demonstrably identical, they remain Trend evidence.
- No wall-clock, cycle, utilization, access, or bandwidth difference in this bundle is presented as a general accuracy ranking.

## Evidence paths

- Candidate/checkpoint/bundle: `manifests/p1_candidates.json`, `manifests/p1_checkpoint.json`, `manifests/p1_bundle_manifest.json`.
- Source audit: `manifests/p1_source_audit.json`.
- STAGE raw: `raw/stage/<case>/r{0,1}/result.json`.
- SCALE-Sim raw: `raw/scalesim/<case>/r{0,1}/result.json` plus each candidate's four native reports.
- Pair gate: `summary/holdout_pair_status.csv`.
- Timing comparison: `summary/holdout_scalesim_stage_timing.csv`.
- Access comparison: `summary/holdout_scalesim_stage_accesses.csv`.
