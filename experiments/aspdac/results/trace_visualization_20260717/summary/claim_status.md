# External Trace Visualization Claim Status

| Claim | Status | Evidence | Boundary |
| --- | --- | --- | --- |
| The CNN transport input is identical across STAGE and BookSim2. | measured | 18,337 packet IDs and the registered input hash are joined. | Exact input contract, not CNN numerical inference. |
| The registered STAGE hotspot case concentrates traffic at node 5. | measured | The stable 0.06-rate, seed-0 offer stream reproduces all 12,433 recorded offers; node 5 and its inbound links dominate the XY projection. | Link counts are Exact input projection; queue/stall values remain aggregate STAGE runtime metrics. |
| STAGE exposes packet-to-stall causality. | measured | The supported contention case records RouterConflict followed by LinkBusy before delivery. | Exact only for the registered supported microbench. |
| STAGE and SCALE-Sim follow the same 4x4 WS timing trend. | trend | 16 independent repeat-level pairs; maximum native-cycle difference 9.69%. | Independent scheduling and prefetch semantics; no cycle-exact claim. |
| STAGE and BookSim2 have equivalent absolute CNN packet latency. | not supported | The same input is used, but the native cycle models differ and STAGE per-packet delivery was not persisted. | Do not report an accuracy percentage from their absolute cycle difference. |
| The figures show observed cross-tool router occupancy. | not supported | BookSim2 router-level occupancy/stall events are absent from the terminal CNN artifact. | Shared-input route demand and native delivery backlog are shown separately. |
| Unity provides the new trace views. | pending | This goal deliberately leaves Unity unchanged. | External PDF/PNG/HTML only. |

No pending or unsupported field is substituted with zero or inferred runtime data.
