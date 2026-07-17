# ASP-DAC Final Experiment Protocol

Status: frozen for Phase 10 final-data execution on 2026-07-16.

## Evidence levels

- **Exact**: an independently checkable oracle or byte-identical canonical trace hash. Exact cases are executed at least twice.
- **Numerical**: same-condition paired configurations with matching workload, operation, mapping, model, and relevant transport hashes. Report mean, sample standard deviation, and two-sided 95% confidence interval when stochastic.
- **Trend**: definitions differ but the direction of change is still informative. Trend evidence is never presented as numerical agreement.
- **Pending**: the required same-condition measurement has not completed. No projected or estimator-only value may replace it.

## Candidate identity and immutability

Each candidate ID is `c-` followed by the first 16 hexadecimal characters of SHA-256 over canonical JSON containing the experiment name, provider, resolved configuration, axes, and seed. Canonical JSON uses sorted keys and compact separators. Resolved YAML and JSON, their full SHA-256 hashes, the Git commit, host, command, runtime/tool versions, and all input hashes are retained in the final bundle.

The external draft at `results/external_draft_20260716` is read-only evidence. Final runs write only below `results/final_20260716`.

## Execution and recovery

The Experiment Manager expands Cartesian sweep axes from YAML, applies declared constraints, writes a candidate manifest before execution, and invokes a registered provider for each candidate. A completed candidate is resumed only when its candidate ID and resolved-config hash match. Unsupported points are recorded with a skip reason. Exceptions, non-zero exits, and timeouts are retained in `failures/`; they are never silently dropped.

Outputs are separated into immutable raw case JSON, tidy summary CSV, human-readable Markdown progress, manifests, figures, and failures. Figures may consume only manifest-indexed CSVs from the final bundle.

## Frozen matched configurations

- **V-BS**: 4x4 mesh, 16 routers, one endpoint per router, dimension-order routing, one VC, 16 flits per VC, 128-bit packet, 128-bit/cycle links, one-flit packets.
- **V-TL**: 16 total compute units providing 16 MAC/cycle in aggregate. The frozen Timeloop mapping is the reference; S-Native throughput is not substituted.
- **V-SS**: 4x4 single-MAC cells, weight-stationary dataflow, with queue, port, and prefetch semantics recorded explicitly.
- **S-Native**: 16 PEs at 256 MAC/cycle each, 128-bit/cycle links, one memory port, five-cycle memory, two-cycle reduction, eight-cycle softmax.
- **S-CIM**: the Phase 9 literature-backed CIM template paired against the Phase 7C digital PE template under the same workload and comparable mapping.

## Statistical rules

Deterministic cases run at least twice and require byte-identical canonical trace hashes. Stochastic NoC cases use seeds 0-9. Non-ideal CIM reports fixed-seed reproducibility plus multi-seed mean, sample standard deviation, and 95% CI. The CI is `t_(0.975,n-1) * s / sqrt(n)`; for ten seeds the multiplier is 2.262157.

BookSim/STAGE saturation is the first of two consecutive tested offered rates for which either the run is unstable/timeout or mean accepted/offered is below 0.95. If the condition is not met twice within the preregistered extension bound, report `saturation > tested bound`.

## Runtime integrity

RQ2 STAGE values must come from real packet injection through the cycle runtime. Static route pressure, estimator-only outputs, and analytical projections are excluded from runtime columns. RQ3 compute-only and full-system values must identify their runtime provider and decomposition. Optical results state `BER not modeled`. Energy uses the shared 45 nm CACTI/Aladdin reference model and is not silicon-calibrated power.

