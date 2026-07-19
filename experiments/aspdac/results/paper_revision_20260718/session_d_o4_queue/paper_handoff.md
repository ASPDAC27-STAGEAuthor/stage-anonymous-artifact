# RF-D-O4-QUEUE-V1 paper handoff

## Decision

The heavy-GEMM, traffic-scale-4 O4 result is **not a cycle-timeout failure**. The original engine reaches a drained graph at cycle 1244 after delivering 102/128 logical packets; the missing 26 packets exactly match 26 `P8OpticalInputQueueFull` events. The last successful packet movement is at cycle 1243.

All 26 failures localize to WDM demux inputs: `optical-03-07-demux=6`, `optical-07-11-demux=12`, and `optical-11-15-demux=8`. E/O, O/E, mux, and waveguide queue-full counts are zero. Per-event evidence is in `queue_failure_localization.csv`.

The active semantics are neither lossless backpressure nor retry. `Phase8OpticalRuntime.SampleInput` returns an error when the next-state queue is full, and `CycleSimulationEngine.Deterministic.TryAcceptArrivedPacket` then records `component_kernel_input_error;packet_consumed=true` and returns success. The arrival flits are removed, so extending `MaxCycles` cannot recover them.

A second configuration defect was isolated: the HardwareGraph declares WDM queue depth 16, but the compiled execution contracts in the frozen O4 run have effective input depth 4. The corrected sweep therefore writes depths 16/32/64/128 directly into sidecar compiled contracts and refreshes their contract hashes. `O4-Q16-effective` is not relabeled as the original O4.

O4-Q32, O4-Q64, O4-Q128 completed in both deterministic repeats. These are new queue-depth designs and must be named separately from the original O4.

## Queue-depth sweep

| design | depth | delivered | cycles | queue full | peak occupancy | mean latency | p95 latency | total energy pJ | trace hash |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| O4-Q16-effective | 16 | 128/128 | 1403 | 0 | 7 | 827.6484375 | 1285 | 106832.99200000089 | `93198860ec64ee70314b010f92fb5128e1a2d206d5e485b3130ca385d205ccf6` |
| O4-Q32 | 32 | 128/128 | 1403 | 0 | 3 | 827.6484375 | 1285 | 106832.99200000089 | `5bc1396de7f7114915e23fcd708b7a5b3b8613cddcb27854159aaa06d6e796f0` |
| O4-Q64 | 64 | 128/128 | 1403 | 0 | 3 | 827.6484375 | 1285 | 106832.99200000089 | `5bc1396de7f7114915e23fcd708b7a5b3b8613cddcb27854159aaa06d6e796f0` |
| O4-Q128 | 128 | 128/128 | 1403 | 0 | 3 | 827.6484375 | 1285 | 106832.99200000089 | `5bc1396de7f7114915e23fcd708b7a5b3b8613cddcb27854159aaa06d6e796f0` |

Every configuration used `MaxCycles=60000` and two identical deterministic runs. All repeat-pair hash checks are exact: 7/7.

Energy boundary: the electrical/NoC, SerDes, E/O+O/E, optical-link, and total values are runtime-reported dynamic/static contributions. The exact optical and SerDes queue buffers do not meter read/write energy on this path, so `total_energy_pj` excludes buffer energy and cannot be used as a complete queue-depth energy comparison.

## Minimal production repair proposal (not applied)

1. Make same-cycle capacity checks use reserved/next-state occupancy, not only current-state occupancy.
2. Treat queue capacity exhaustion as `ready=false`: return non-acceptance, retain the arrival flits in `PendingArrivalFlits`, and retry deterministically. Do not emit an error and do not acknowledge packet consumption.
3. Add a focused regression with more simultaneous arrivals than depth, asserting packet conservation, explicit downstream-full stalls, eventual delivery, and exact repeat hashes.

No production runtime file was modified by this task.

## B2 60000-cycle follow-up

Case: `gemm_256_collection-l10-o4`; disposition: `no_progress_from_10000_through_60000`.
- repeat 0: delivered 6/32, total cycles 60000, last packet movement 1886, movement after cycle 10000=False, hash `9b05773f4b7a93ed0f9aa6095554f9e0db3d2f8689aceaead605326c6fa5acc0`.
- repeat 1: delivered 6/32, total cycles 60000, last packet movement 1886, movement after cycle 10000=False, hash `9b05773f4b7a93ed0f9aa6095554f9e0db3d2f8689aceaead605326c6fa5acc0`.

The original B2 results were read only and remain unchanged.

## One-command reproduction

```powershell
powershell -ExecutionPolicy Bypass -File experiments/aspdac/tests/results_first_o4_queue_runner/reproduce.ps1 -OutputRoot experiments/aspdac/results/paper_revision_20260718/session_d_o4_queue_repro
```

Frozen-source queue failure summary: `optical-03-07-demux:6;optical-07-11-demux:12;optical-11-15-demux:8`.
