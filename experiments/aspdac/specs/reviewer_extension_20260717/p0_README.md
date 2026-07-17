# Reviewer-extension P0 plans and manager

These plans use `aspg-reviewer-p0-plan-1.0` and are consumed by
`experiments/aspdac/scripts/reviewer_extension_p0.py`.

- `p0_smoke.yaml`: six 4x4 x 10^4 candidates (metrics-only and full trace, repeats 0--2).
- `p0_stage_scalability.yaml`: 72 core candidates (4 meshes x 3 packet counts x 2 trace modes x 3 independent repeats).
- `p0_stage_seed_stability.yaml`: six small-run seed checks (seeds 0--2 x 2 trace modes).
- `p0_stage_stretch.yaml`: 24 preregistered 10^7-packet stretch candidates. The manager retains timeout/resource-limit records.
- `p0_specialist_runtime.yaml`: 36 BookSim2 and six SCALE-Sim context candidates. BookSim2 runs through the Ubuntu-24.04 native-staging adapter with GNU `time -v`; SCALE-Sim remains explicitly skipped until a real runtime adapter is supplied.

Candidate IDs are SHA-256-derived from canonical provider, scenario, and parameter JSON. The same candidate keeps the same ID across smoke and full plans. `run` and `smoke` checkpoint after every point and resume terminal candidates by default.

Typical commands:

```text
python experiments/aspdac/scripts/reviewer_extension_p0.py plan --plan experiments/aspdac/specs/reviewer_extension_20260717/p0_stage_scalability.yaml
python experiments/aspdac/scripts/reviewer_extension_p0.py smoke
python experiments/aspdac/scripts/reviewer_extension_p0.py run --plan experiments/aspdac/specs/reviewer_extension_20260717/p0_stage_scalability.yaml
python experiments/aspdac/scripts/reviewer_extension_p0.py analyze --bundle experiments/aspdac/results/reviewer_extension_20260717
```

The runner DLL must already exist; this manager never builds it. Each STAGE candidate runs in an independent child process. The parent manager polls the child working set, enforces timeout/resource limits, and writes all terminal states and reasons to raw JSON plus `manifests/p0_checkpoint.json`; process logs are kept under `logs/`. BookSim2 configurations and execution stay under `/tmp/stage-reviewer-p0-booksim2` rather than `/mnt/f`; exact native accepted-packet denominators come from `pair_stats`, so target packet counts are never presented as exact when latency-mode termination differs.
