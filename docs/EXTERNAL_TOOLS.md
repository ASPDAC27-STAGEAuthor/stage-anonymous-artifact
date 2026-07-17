# Optional specialist and external reruns

The portable workflow does not vendor third-party simulator source or binaries. It does include the frozen high-level plans, executable adapters, normalized terminal summaries, input templates needed by the supported rerun paths, and hashes that bind those inputs to the paper evidence.

## Two different operations

1. Portable claim replay: <code>python scripts/validate_frozen.py --claim NAME</code>. This recomputes and asserts claims from supplied terminal summaries and requires no external simulator.
2. Fresh external rerun: install the named third-party tool, materialize inputs into a new directory under <code>output/external-rerun/</code>, execute the supplied adapter, and compare the new normalized summary. Never overwrite a frozen result directory.

The artifact does not claim that a frozen replay is a new third-party execution.

The path-free frozen tool identities are recorded in <code>experiments/aspdac/external_inputs/tool_versions.json</code> and are asserted by the Timeloop, SCALE-Sim, Accelergy, and BookSim claim validators.

## Fixed tool and configuration record

| Tool | Frozen identity/configuration | Included inputs and adapters | Claim boundary |
|---|---|---|---|
| BookSim2 | Git commit <code>28f43299f1706a3160ffac721ca461d74eb6e618</code>; binary SHA-256 <code>44b617ec81bcdb7496ee86acab011f5d7d00c0716ae65150a11777a2b84c4cbd</code>; 4x4 mesh; dimension-order routing; one 128-bit flit per packet; one VC of 16 flits; three 1,000-cycle warm-up periods and ten 1,000-cycle measured samples; paired STAGE runs use a 2,000-cycle drain; four traffic patterns; 10 seeds | <code>v_bs.yaml</code>, <code>rq2_booksim_stage.yaml</code>, <code>generate_booksim_configs.py</code>, and a locked ledger for 520 registered plus 10 uniform-extension configurations | Congestion ordering only; absolute saturation is 0.04 versus 0.08 for hotspot and is not called equivalent |
| SCALE-Sim | Commit <code>9f98c4371055a54c75209c2e02b640b897550532</code>; captured compatibility diff SHA-256 <code>ee0f1075ace7dc2b299e9cfaf4bc9f423b7fdfb9747ecff20f542dfa5f3dad1b</code>; 4x4 single-MAC weight-stationary array; 8 words/stream/cycle; seed 40; two repeats | <code>p1_holdout.yaml</code>, 50 prepared candidate inputs, <code>aspdac_4x4_ws.cfg</code>, compatibility diff, <code>run_reviewer_p1_scalesim.py</code>, and independence audit | Cross-tool cycle results use the predefined 10% engineering envelope; scheduling, arbitration, and prefetch internals are not claimed identical |
| Timeloop | Infrastructure commit <code>6e6186f9fe8f9a1f3990f78f57c7224d22cb8cfa</code>; <code>timeloop-model</code> SHA-256 <code>c3eb5d5f5717c701a46e57894a6ac5e24b581c27953cce483c2561913eb5194e</code>; Python 3.12.3; frozen 16-MAC/cycle mapping: M4/N4/K1 spatial and K16 temporal | Five workload decks and five mapping files under <code>external_inputs/timeloop_model/</code>; <code>run_timeloop_model_final.py</code> | Exact only for the shared compute floor and hierarchy-access schedule |
| Accelergy | Version <code>0.4</code> on Timeloop infrastructure commit <code>6e6186f9fe8f9a1f3990f78f57c7224d22cb8cfa</code>; shared 45-nm CACTI/Aladdin reference; locked ERT SHA-256 <code>5f86d648b6e655d254a31dd3a619bd2476adf3dfae81ae7278a34feb2307d9da</code>; version is probed and recorded on every new run | <code>baseline_tools/timeloop_accelergy/</code>, <code>run_accelergy_core_workloads.py</code>, and <code>run_rq3_size_timeloop_accelergy.py</code> | Exact rebound of nine shared ERT actions; not silicon-calibrated energy accuracy |
| PyTorch/CUDA | Python 3.10, torch 1.12.1+cu116, torchvision 0.13.1+cu116, CUDA 11.6 | checkpoint, predictions, compressed MNIST archives, resolved precision config | Functional precision bridge only; not native end-to-end STAGE CNN execution |

## BookSim configuration materialization

The generator emits every configuration used in the registered saturation sweep and the completed uniform extension, then compares all 530 SHA-256 values with the frozen ledger.

~~~bash
python experiments/aspdac/scripts/generate_booksim_configs.py \
  --output output/external-rerun/booksim-configs
~~~

Expected result: <code>BookSim config verification passed: 530/530</code>. The ledger is <code>experiments/aspdac/external_inputs/booksim_config_hashes.csv</code>. It fixes topology, routing function, traffic generator, injection rate, packet/flit size, VC count/depth, seed, warm-up, and sample count. The saturation criterion is stored in <code>rq2_saturation_summary.csv</code>: the first of two consecutive tested rates with any unstable seed or mean accepted/offered below 0.95.

The package intentionally does not include the 530 native stdout logs. Their original file hashes remain in <code>rq2_external_raw_manifest.json</code>; a fresh execution should write a new log/manifest tree under <code>output/external-rerun/</code>.

## Timeloop fresh command

After installing Timeloop, run:

~~~bash
python experiments/aspdac/scripts/run_timeloop_model_final.py \
  --source experiments/aspdac/external_inputs/timeloop_model \
  --output output/external-rerun/timeloop \
  --timeloop-model /path/to/timeloop-model \
  --timeloop-repo /path/to/accelergy-timeloop-infrastructure
~~~

The adapter records the executable command, Timeloop Git revision, input-deck and mapping hashes, stats hashes, return codes, and parsed cycles/accesses. The output directory must be empty, preventing a failed rerun from being masked by stale files.

## SCALE-Sim fresh command

The supplied P1 candidate files make the hold-out adapter auditable. To create a fresh run root and execute SCALE-Sim:

~~~bash
python experiments/aspdac/scripts/reviewer_extension_p1.py prepare \
  --run-root output/external-rerun/p1
python experiments/aspdac/scripts/run_reviewer_p1_scalesim.py \
  --run-root output/external-rerun/p1 \
  --tool-repo /path/to/SCALE-Sim \
  --tool-python /path/to/python
~~~

On Windows, the adapter defaults to WSL Ubuntu 24.04. It refuses a Git revision other than the frozen commit and records the compatibility-diff hash, generated config/topology hashes, native report bundle hash, command, and wall time.

## Accelergy fresh command

After installing the Timeloop/Accelergy stack in the paths documented by the script, run:

~~~bash
python experiments/aspdac/scripts/run_accelergy_core_workloads.py \
  --repo-root . \
  --output output/external-rerun/accelergy
~~~

For the joint size matrix, use <code>run_rq3_size_timeloop_accelergy.py --help</code>. New manifests probe the Timeloop executable hash, Timeloop repository revision, Accelergy package version, Python version, template/mapping hashes, and every generated output hash.

## Raw-output and large-trace policy

The anonymous package includes small inputs and normalized summaries, not third-party source trees, hundreds of native simulator logs, or the 3.92-GiB STAGE JSONL trace. This is deliberate:

- quick review runs only live unit/contract tests, frozen assertions, and plotting;
- medium trace/hash checks can be run without producing a million-packet trace;
- the large trace row is verified with <code>--claim trace</code>;
- fresh large runs must target a new output directory with at least 5 GiB free;
- metrics-only or filtered/windowed tracing is the default recommendation for larger DNN/LLM studies.

The manifests under <code>experiments/aspdac/results/*/manifests/</code> preserve original raw-path hashes as provenance. A manifest entry is not a claim that the large raw file itself is bundled.

## Optional MNIST replay

Decompress the four files under <code>data/mnist/raw/</code> into a torchvision-compatible <code>MNIST/raw/</code> directory and adapt only reviewer-owned dataset/checkpoint paths in a copy of the resolved config. The runner deliberately refuses a CPU-only full replay because the submitted measurement contract used CUDA.

All third-party software and data remain under their upstream terms; see <code>THIRD_PARTY.md</code>.
