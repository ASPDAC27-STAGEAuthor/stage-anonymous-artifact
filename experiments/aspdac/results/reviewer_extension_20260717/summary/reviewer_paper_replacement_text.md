# Reviewer-extension paper replacement text

Scope rule: these are replacement blocks for Methods, Results, Limitations, and figure captions only. **Do not modify the Introduction.**

## Methods — reviewer-extension evidence protocol

We separate reviewer-extension evidence into four explicit states: measured, trend, not supported, and pending. Measured rows require completed source records and the phase-specific acceptance predicate. Trend rows are descriptive cross-tool observations whose abstractions or counter definitions are not equivalent. Not-supported and pending rows are excluded from measured plots and numerical claims. All figures in this bundle are regenerated from the published CSV summaries; no values are transcribed or fabricated.

## Results — STAGE scalability and trace cost

The scalability sweep contains 78 completed STAGE records spanning mesh dimensions [4, 8, 16, 32], packet counts [10000, 100000, 1000000], and trace modes ['full', 'metrics_only']. Across 39 completed full-trace/metrics-only pairs, trace-enabled simulation slowdown was median 7.37x (range 2.93x–10.28x); delivery hashes agreed in 39/39 pairs. These are host-specific runtime and memory observations, not a specialist-tool speed comparison.

## Results — modeled NoC contract

Across 14 completed runs covering 7 modeled NoC microbench cases, the real STAGE trace projection matched the independent oracle contract in every included run. 4 capacity-release runs across 2 cases were excluded as not supported because the generic engine exposes no public deterministic capacity-release event input. The result establishes agreement only for the stated modeled contract; it does not establish router-pipeline, credit-flow, wormhole, BookSim, or hardware equivalence.

## Results — cross-tool holdout

The published holdout summary currently contains 16 paired-ready row(s) and 0 blocked status row(s). For the ready subset, SCALE-Sim/STAGE total-cycle ratio 0.962–1.107. These observations are trend-only because STAGE and SCALE-Sim use different abstractions and access-counter definitions; they are not evidence of timing or access-count accuracy.

## Results — preregistered interventions

The preregistered intervention analysis contains 2 accepted within-model pairs, with measured total-cycle changes of -10.0%, -23.1%. Registered point predictions fell inside their preregistered intervals for the accepted rows. This is deterministic within-model counterfactual evidence, not general causal identification, hardware accuracy, or cross-tool validation.

## Limitations

The NoC evidence covers only the explicitly modeled contract and omits route-compute, VC-allocation, switch-allocation, crossbar, credit-return, wormhole-reservation, and per-flit router-pipeline semantics. Capacity-release cases remain not supported. External specialist runtime records are absent unless explicitly present in `specialist_tool_runtime_context.csv`. The holdout subset remains trend-only. Fig. 2 remains pending until a fresh visible multi-chip UI capture is reviewed. None of these results support silicon accuracy or universal model validity.

## Figure captions

**Reviewer scalability figure.** Host-specific STAGE scaling and trace-cost measurements from completed P0 records; trace-enabled and metrics-only delivery hashes are checked pairwise.

**Reviewer NoC contract figure.** Modeled cases only. Panel (a) compares delivered cycles from real STAGE traces with the independent oracle; panel (b) reports run-level oracle match rates. Not-supported capacity-release cases are excluded from measured layers.

**Reviewer holdout figure.** Trend-only paired STAGE/SCALE-Sim observations from rows marked `paired_ready=true`; pending and not-supported rows are excluded. Omit this caption and figure when no paired-ready row exists.

**Reviewer intervention figure.** Preregistered deterministic within-model counterfactuals with accepted prediction intervals and bottleneck-set transitions; no general causal or hardware-accuracy claim is made.

**Fig. 2.** Pending. Replace only after a fresh visible multi-chip UI capture has been visually reviewed and indexed.
