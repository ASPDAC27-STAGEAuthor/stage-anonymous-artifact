# Claim-to-artifact map

The README contains the reviewer-facing command matrix. This file records the exact evidence authority behind each command. Frozen replay means the command recomputes and asserts values from supplied terminal CSV/JSON evidence; live means the simulator or plotting code is executed in the reviewer's environment.

| Paper evidence | Command | Expected acceptance condition | Evidence authority | Mode / typical time |
|---|---|---|---|---|
| Nine analytical contracts | <code>python scripts/validate_frozen.py --claim analytical</code> | 9/9 cases, 18 repeats, byte identity, and locked SHA-256 values | <code>rq1_exact_cases.csv</code>; <code>docs/CANONICAL_TRACES.md</code> | Frozen replay / &lt;5 s |
| Seven exact-cycle golden traces | <code>dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release -- --group golden</code> | 7/7 live tests pass twice against locked hashes | <code>ExactCycleGoldenTests.cs</code>; <code>CanonicalTraceHash.cs</code> | Live / &lt;1 min |
| Seven supported NoC cycle contracts | <code>python scripts/validate_frozen.py --claim noc</code> | 14/14 repeated runs, 7 supported cases, independent-oracle match, locked STAGE/oracle hashes | <code>p1_noc.yaml</code>; <code>noc_contract_microbench.csv</code>; cycle/oracle timelines | Frozen replay / &lt;5 s |
| Deterministic canonical serialization | <code>python scripts/validate_frozen.py --claim determinism</code> | Two byte-identical runs for all nine analytical cases | <code>CanonicalTraceHash.cs</code>; <code>rq1_exact_cases.csv</code> | Frozen replay / &lt;5 s |
| Timeloop compute/access matching | <code>python scripts/validate_frozen.py --claim timeloop</code> | 5/5 compute cycles and 20/20 access rows exact | <code>rq3_timeloop_stage_cycles.csv</code>; <code>rq3_timeloop_stage_accesses.csv</code>; <code>tool_versions.json</code> | Frozen replay / &lt;5 s |
| Submitted 47-configuration match | <code>python scripts/validate_frozen.py --claim matched47</code> | 47/47 matched compute-cycle pairs and 47/47 shared-ERT energy pairs pass | <code>session_c_matched_47bars/matched_47bar_values.csv</code> | Frozen replay / &lt;5 s |
| SCALE-Sim hold-out | <code>python scripts/validate_frozen.py --claim scalesim</code> | 16/16 pairs; maximum 9.691109% within predefined 10% engineering envelope | <code>p1_holdout.yaml</code>; independence audit; paired timing CSV; <code>tool_versions.json</code> | Frozen replay / &lt;5 s |
| Accelergy ERT actions | <code>python scripts/validate_frozen.py --claim accelergy</code> | 9/9 shared action values exact and ERT hash locked | <code>rq3_energy_microbench.csv</code>; included 45-nm template; <code>tool_versions.json</code> | Frozen replay / &lt;5 s |
| BookSim2 selected-contract timing | <code>python scripts/validate_frozen.py --claim booksim_timing</code> | Four contracts, nine event categories, eleven repeat hashes, and seven production cases exact | <code>session_j_booksim_production_paper/</code> plus <code>AspdacStageNocRuntime.cs</code> and six live tests | Frozen replay plus live STAGE tests / &lt;5 s |
| Additional BookSim congestion order | <code>python scripts/validate_frozen.py --claim booksim</code> | <code>hotspot_node5</code> first in both. The 0.04 versus 0.08 saturation values remain non-equivalent | <code>v_bs.yaml</code>; 530-config hash ledger; saturation summary; <code>tool_versions.json</code> | Frozen replay / &lt;5 s |
| Controlled bottleneck attribution | <code>python scripts/validate_frozen.py --claim attribution</code> | Both intervention gates pass and the trace-connected sequence is NoC -> memory -> NoC | preregistration; intervention analysis; <code>trace_guided_interventions.csv</code> | Frozen replay / &lt;5 s |
| Mapping/topology DSE | <code>python scripts/validate_frozen.py --claim mapping</code> | 24 exact deterministic candidates and reported ranges match | <code>stage_mot_mapping.csv</code> | Frozen replay / &lt;5 s |
| MNIST precision | <code>python scripts/validate_frozen.py --claim precision</code> | Four accuracy/traffic profiles and checkpoint/prediction hashes match | paired precision CSV; frozen checkpoint and predictions | Frozen replay / &lt;5 s |
| Optical intervention | <code>python scripts/validate_frozen.py --claim optical</code> | 4x payload and 8193-to-2049 cycle result | <code>optical_intervention.csv</code> | Frozen replay / &lt;5 s |
| Trace overhead/storage | <code>python scripts/validate_frozen.py --claim trace</code> | Three-repeat medians, 46,852,084 events, raw/compressed bytes match | <code>stage_scalability.csv</code> | Frozen replay / &lt;5 s |
| Submitted Figures 2--5 | <code>scripts/run_figures.ps1</code> or <code>scripts/run_figures.sh</code> | Four PDF and four PNG outputs are generated. Every format used by the paper matches its submission reference byte for byte | submitted plotting code plus frozen terminal summaries | Live plotting / &lt;1 min |

## Interpretation rules

- A zero exit code is the acceptance signal; the JSON report under <code>output/</code> records the asserted values.
- The NoC count is 14 runs because each of seven supported cases is repeated twice. N07/N08 are boundary cases explicitly marked unsupported, not silently dropped failures.
- Timeloop exactness is limited to the shared mapping schedule and access definitions.
- SCALE-Sim remains trend/engineering-envelope evidence across different native scheduling abstractions.
- The production STAGE network matches native BookSim2 on the selected tested 4x4 mesh contract only. The separate saturation sweep supports congestion ordering and does not claim cycle or absolute-saturation equivalence.
- Attribution is deterministic, within-model controlled comparison; it is not general causal identification.
- Full-trace generation is optional. The default path verifies the measurement record without writing a 3.92-GiB file.

Fresh external-tool setup, versions, input materialization, and output boundaries are documented in <code>docs/EXTERNAL_TOOLS.md</code>.
