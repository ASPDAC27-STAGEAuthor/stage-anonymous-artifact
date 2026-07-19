# RF-E electrical NoC paper handoff

Status: **PASS — sidecar evidence complete; no production-core or paper-TeX modification.**

The previous E128 Attention QK scale-2 result (64 packets, 296 128-bit link transfers, 841.728 pJ) was link-dynamic-only. This sidecar replaces the legacy shared-digital link charge and adds non-zero input-buffer, switch-allocation/route-arbitration, crossbar, clock, router leakage, link leakage, and queue-area terms under one DSENT 45-nm contract.

| Arch | Cycles | Router traversals | Digital transfers | Router dynamic (pJ) | Link dynamic (pJ) | Static/leakage (pJ) | Electrical NoC total (pJ) |
|---|---:|---:|---:|---:|---:|---:|---:|
| E128 | 66 | 232 | 296 | 46637.827 | 224.574 | 799294.925 | 846157.327 |
| O1 | 490 | 232 | 128 | 55266.949 | 31.454 | 5933506.952 | 5988805.356 |
| O2 | 563 | 232 | 128 | 56752.623 | 31.454 | 6817478.396 | 6874262.474 |
| O4 | 731 | 232 | 128 | 60171.709 | 31.454 | 8851823.637 | 8912026.801 |
| H4 | 356 | 200 | 232 | 46292.288 | 151.005 | 4311348.386 | 4357791.679 |

For E128, the sidecar electrical NoC total is **846157.327 pJ**. The large value is not a unit error: the checked runtime configuration requests 5 input ports x 4 VCs x 128 flits/VC of DFFRAM storage per router, and DSENT reports **2.555920 mm2/router** plus **756.714 pJ/router-cycle**. This is a configuration/model result that must be disclosed, not tuned away.

## Conservation and double counting

- Every architecture satisfies `buffer write + buffer read + route/arbitration + crossbar + endpoint link dynamic + mesh link dynamic + clock + router leakage + link leakage = electrical NoC total` with exact Decimal residual 0.
- `legacy_shared_digital_link_energy_removed_pj` is the manifest sum for `endpoint-*` and `electrical-*` links. The rebased provisional transport value subtracts that amount before adding the new NoC total; it never adds the sidecar cost on top of the old shared-digital link charge.
- Legacy non-shared optical-interface terms are retained only in the provisional rebased transport column. They are outside this task and may be replaced by the parallel optical-energy task.

## Claim boundary

This is a reproducible analytic 45-nm Bulk-LVT/1.0-V/1-GHz model, not silicon calibration. Session A shares the 45-nm node and 1-GHz cycle but does not specify voltage, so voltage equivalence is not asserted. Route computation is combined with DSENT's non-zero two-stage switch allocation because DSENT has no separate route-computation event. Optical links/converters/laser/tuning are excluded.

## Reproduce

From repository root:

```powershell
python experiments/aspdac/tests/results_first_electrical_energy_runner/run_electrical_energy.py all
```

The runner always executes analytical one-action contracts before the five-architecture pass and never overwrites Session B inputs.
