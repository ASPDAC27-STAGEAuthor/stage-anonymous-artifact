# ASP-DAC 实验 Claim 总表与准确复现索引

日期：2026-07-17  
状态：terminal evidence summary  
适用范围：`final_20260716`、`reviewer_extension_20260717`、`trace_visualization_20260717`  

这份文档回答六个问题：实验为什么做、和谁比、比什么、证据有多可信、结果究竟好不好、怎样从冻结配置和原始证据准确复现。它不是营销评分表，也不把不同模拟器的 native cycle、wall-clock 或能耗口径合并成一个“总体准确率”。

## 1. 判定口径

### 1.1 Claim 状态

- `measured`：存在完成的 terminal evidence，并满足该 claim 预先声明的验收条件。
- `trend`：方向、排序或缩放趋势可比较，但抽象、计数器或周期定义不完全相同。
- `not_supported`：现有模型或证据不能支持该句子；不得改写成弱化后的 measured claim。
- `pending`：实验范围内尚无足够数据，或该能力明确未实现。

### 1.2 实验置信度

- `A（高）`：独立 oracle 或 matched schedule；重复 trace byte-identical；或完成的多 seed/多 size 证据全部满足预注册条件。
- `B（中高）`：成对、独立或多点 Trend 证据充分，但不能宣称 numerical/cycle equivalence。
- `C（中）`：确定性的 STAGE 内部 counterfactual、单机 wall-clock，或样本/外部效度有限的描述性结果。
- `D（不足）`：缺字段、未建模、抽象不匹配或测试上界不足，只能保留为 `not_supported/pending`。

置信度表示“这条限定后的 claim 是否可靠”，不是“模拟器与硅是否同样准确”。

### 1.3 指标优异度

- `A（优异）`：对该实验目的而言，全部点达到 Exact 或明确超过预注册验收线。
- `B（良好）`：趋势稳定、误差在注册范围内，或设计干预产生预期效果，但不具备 Exact/硬件校准边界。
- `C（混合）`：部分目标达到，另一些指标无优势、代价较大或口径仍不完整。
- `D（不利）`：same-condition 实测中明确慢、耗能更高或未达到目标。
- `—`：没有合法的数值比较，不能评分。

“优异度”只评当前行的实验目标。例如 Exact action replay 可以是 A，但并不意味着能耗模型已 silicon calibrated。

## 2. 主 Claim Register

| Claim ID / 状态 | 实验目的 | 对比对象 | 核心指标与 terminal 结果 | 实验置信度 | 指标优异度 | 准确复现 |
| --- | --- | --- | --- | --- | --- | --- |
| `C-RQ1-EXACT` measured | 验证图、时序、序列化、背压、光学算术与特殊单元的确定性合同 | STAGE 对独立 analytical/regression oracle；同 case 重复两次 | 9/9 case 通过；每个 case 的 canonical trace bytes 与 hash 重复一致 | A（Exact + repeat） | A | R1 |
| `C-P1-NOC-CONTRACT` measured | 验证真实 STAGE trace 是否实现已公开的 NoC 微基准合同 | STAGE trace projection 对独立 NoC oracle | 7 个 supported case、14 次运行全部 oracle match | A（独立 oracle） | A | R7 |
| `C-P1-CAPACITY-RELEASE` not_supported | 检查确定性的 capacity-release 输入合同 | 2 个 capacity-release case、4 次运行 | 4/4 明确保留为 not supported；不进入 measured 图层 | D | — | R7 |
| `C-RQ2-HOTSPOT` measured/trend | 判断不同 traffic 下最早严重拥塞模式是否一致 | BookSim2 对 STAGE 4×4 mesh | 两者都把 hotspot node 5 排为最早严重拥塞；数值 saturation 不等价 | B（10 seeds，Trend） | B | R2 |
| `C-RQ2-SATURATION-EQUIVALENCE` not_supported | 检验所有 traffic 的 saturation 数值是否一致 | BookSim2 对 STAGE | uniform `0.32` 对 `>0.60`；transpose `0.16` 对 `>0.32`；bit-complement `0.20` 对 `>0.32`；hotspot `0.04` 对 `0.08` | D（定义/结果均不等价） | D | R2 |
| `C-RQ2-UNIFORM-SATURATION` pending | 找到 STAGE uniform 的精确 saturation 点 | STAGE uniform rate sweep | 测到 0.60 仍未达到注册 saturation 条件；只能报告 `>0.60` | D（上界不足） | — | R2 |
| `C-RQ3-TL-CYCLES` measured | 检查 matched 16-MAC compute schedule 和存储访问记账 | Timeloop-model 对 STAGE compute-only | 5/5 workload 的 MAC、cycle floor 与 register/local/global/DRAM access exact；cycle relative error 0 | A（matched schedule） | A | R3 |
| `C-RQ3-SIZE-TL` measured | 检查 compute replay 是否随问题尺寸保持精确 | Timeloop-model 对 STAGE，9 个 GEMM/MLP/attention size | 9/9 size 的 cycles、MAC 和四级 access exact | A（多 size Exact） | A | R3 |
| `C-RQ3-TL-FULLSYSTEM-EQUIVALENCE` not_supported | 判断 Timeloop analytical cycles 能否等同 STAGE full-system cycles | Timeloop compute floor 对 STAGE full-system | 只允许 compute-only exact；memory/NoC 等 excess 是 STAGE-only attribution | D（denominator 不同） | — | R3 |
| `C-RQ3-SS-TREND` measured/trend | 验证 4×4 WS timing 趋势和 bandwidth outlier | SCALE-Sim 对 STAGE matched WS | 5/5 workload 在 10% envelope 内；最大 warm-cycle 差 6.41% | B（独立 scheduler，Trend） | B | R3 |
| `C-RQ3-SIZE-SS` measured/trend | 验证 WS 趋势是否跨尺寸保持 | SCALE-Sim 对 STAGE，9 个 size | 9/9 在预注册 10% envelope 内；最大差 5.4004% | B（多 size Trend） | B | R3 |
| `C-P1-HOLDOUT` trend | 排除共享 schedule 造成的虚假一致 | 独立构造的 SCALE-Sim/STAGE 4×4 WS hold-out | 16 个 paired-ready repeat；最大 native-cycle 差 9.69%，ratio 0.962–1.107 | B（独立 hold-out） | B | R7 |
| `C-ENERGY-ERT` measured | 验证 Accelergy shared-reference action accounting | Accelergy 45-nm CACTI/Aladdin ERT 对 STAGE action replay | 5 workload + 9 size bundle 均有非空 5-table ERT；无 dummy/schema fallback；9 个 one-action check exact | A（Exact accounting） | A | R3 |
| `C-ENERGY-NATIVE-EQUALITY` not_supported/trend | 检查 Timeloop native arithmetic energy 是否等于 shared-ERT rebound | native energy summary 对 shared ERT | 5 workload 的 binding gap 为 3.81%–3.92%；不能称数值相等 | C（稳定但口径不同） | C | R3 |
| `C-MNIST-FUNCTIONAL` measured | 证明选定 LeNet-style 网络本身可达到正常 MNIST accuracy | PyTorch deterministic functional oracle | 10,000 张 test image，accuracy 98.39%；重复 prediction hash 一致 | A（功能 oracle） | A | R4 |
| `C-MNIST-NOC-TRACE` measured | 验证 CNN materialized-im2col 运输输入及重复运行稳定性 | BookSim2 与 STAGE，共享冻结 packet trace | sequential network 18,337 packets byte-identical；12 个 same-seed repeat group 的 delivery/metrics hash exact | A（Exact input + repeat） | A | R4 |
| `C-MNIST-NOC-TREND` measured/trend | 比较网络层通信负载排序，而非绝对周期准确率 | BookSim2 对 STAGE，5 个 isolated layer + sequential trace | isolated layer makespan ranking Spearman rho≈1；两者都识别 FC1 为最大层 | B（Trend） | B | R4 |
| `C-MNIST-STAGE-ENDTOEND` not_supported | 判断 STAGE 是否原生执行完整 CNN 数值链并复现 accuracy | STAGE 数值输出对 PyTorch 逐样本输出 | ReLU、AvgPool、层间完整张量和最终分类核未形成原生 end-to-end 合同 | D | — | R4 |
| `C-RQ4-ORACLE` measured | 验证光损耗、功率和 64b/66b 序列化算术 | STAGE optical calculation 对独立 oracle | 4.11/-4.11/-1.11 dB 与 132-bit、2 service cycles exact；重复两次 | A（独立 oracle） | A | R5 |
| `C-RQ4-CAPACITY` measured | 检查 wavelength capacity 对 contended optical transport 的影响 | capacity 1/2/4/8，same Attention workload/config hashes | cycles 16,385→8,193→4,097→2,049；1→8 降低 87.5%，conflict/backpressure 单调下降 | A（paired runtime） | A | R5 |
| `C-RQ4-OPTICAL-WIN` not_supported | 判断当前 optical transport 是否胜过 matched electrical | Attention electrical 对 optical capacity-8 | 1,025 vs 2,049 cycles；10.49 vs 140.52 nJ；当前 optical 明确更慢且能耗更高 | A（same-condition 负结果） | D | R5 |
| `C-RQ4-BER` pending | 判断光链路是否具有 BER accuracy | optical model 对 device/noise-calibrated BER model | `BER not modeled` | D | — | R5 |
| `C-CODESIGN-MIGRATION` measured | 展示单轴改变引发 bottleneck migration 与平台化 | STAGE baseline 对 PE/link/memory/queue 单轴 sweep | 64→128-bit link 后 NoC→memory；继续加宽不再降 cycles，显示平台化 | C（STAGE 内部） | B | R6 |
| `C-CODESIGN-CAUSAL` measured | 验证 trace 诊断出的 memory stall 是否可被单参数干预消除 | memory ports 1→2，其他 hash 不变 | target memory critical cycles 1,280→0；total cycles 2,560→2,304（-10%）；下一 bottleneck 为 NoC | B（确定性 counterfactual） | A | R6 |
| `C-P2-INTERVENTIONS` measured | 检查预注册 intervention 的预测区间是否命中 | 两组 STAGE baseline/intervention pair | 2/2 accepted；total-cycle change -10.0%、-23.1%；点预测位于预注册区间 | B（预注册、within-model） | A | R7 |
| `C-MOT-MAPPING` measured | 证明相同 workload 的 mapping/mesh geometry 会改变性能与通信 | 同一 M=1,K=N=256 workload 的 24 个 resolved mapping | 24/24 exact、repeat=2 deterministic；cycles 174–1,741，1024-bit packet moves 316–600 | A（确定性多 mapping） | A | R6 |
| `C-PRECISION` measured | 检查精度降低如何改变 packetized traffic 与 bottleneck | FP16/BF16/INT8/INT4，same workload | packetized bits 1,048,576→262,144；packet count 8,192→2,048；conversion cost显式；dominant bottleneck 转为 softmax | B（STAGE 内部） | B | R6 |
| `C-CIM-REPRO` measured/trend | 验证数字/CIM 模板的确定性与 non-ideal 统计可重复性 | digital template 对 literature-backed CIM template | fixed seed hash exact；10 seeds mean RMSE 0.0010097，95% CI [0.0008174,0.0012019] | B（统计可重复，非器件校准） | B | R6 |
| `C-CIM-ENERGY-WIN` pending | 判断 CIM complete energy/op 是否低于 digital PE | digital 对 CIM | `complete_energy_per_operation_pj=unknown`，缺 egress energy；不得把 unknown 当 0 | D | — | R6 |
| `C-P0-STAGE-SCALE` measured | 测量 STAGE wall-clock、内存和 full-trace 代价 | 4/8/16/32 mesh；10k/100k/1M packets；full 对 metrics-only | 78 completed；39 对 delivery hash 一致；full-trace slowdown median 7.37×（2.93×–10.28×） | B（单机实测） | C | R7 |
| `C-P0-SPECIALIST-RUNTIME` trend | 给出外部工具运行时间背景，不做速度排名 | BookSim2 36 rows；SCALE-Sim 6 rows；STAGE context | 仅 descriptive wall-clock context，host/tool invocation 不同 | C | — | R7 |
| `C-TVIZ-HOTSPOT` measured | 把 hotspot 从统计表变成可追溯的空间需求图 | 注册 STAGE rate 0.06/seed 0 offer stream 对冻结 XY 投影 | 12,433/12,433 offers 重放一致；node 5 与入向 link demand 主导；runtime queue/stall 仍单独保留 | A（Exact input projection） | A（可追溯性） | R8 |
| `C-TVIZ-CROSS-TOOL-LATENCY` not_supported | 判断 CNN 的 STAGE/BookSim2 per-packet latency 是否可直接对账 | 共享 18,337-packet input | STAGE terminal bundle 未持久化 per-packet delivery cycle；native cycle model 也不同 | D | — | R8 |
| `C-TVIZ-OCCUPANCY` not_supported | 判断图是否展示跨工具实测 router occupancy | BookSim2 对 STAGE router events | BookSim2 CNN artifact 无 per-router occupancy/stall events；图只展示 shared-input demand 与 BookSim2 backlog | D | — | R8 |
| `C-SILICON-CALIBRATION` pending | 判断能耗/延迟是否为 silicon-calibrated measurement | 所有 energy/timing model 对真实硅测量 | 当前为 shared reference、literature-backed 或 synthetic functional boundary | D | — | R3/R5/R6 |
| `C-UNITY-TRACE-VIEW` pending | 判断新 trace 视图是否已集成 Unity | 外部 PDF/PNG/HTML 对 Unity UI | 本工作明确不修改 Unity；当前只有 external renderer | D | — | R8 |

## 3. 对论文最重要的结论

### 可以写成强结论

1. STAGE 对 9 个 analytical/regression oracle 和 7 个公开 NoC 微基准合同给出 exact、可重复结果。
2. 在冻结的 16-MAC schedule 下，STAGE 对 Timeloop 的 5 个主 workload 和 9 个 size extension 实现 exact cycle/access replay。
3. 独立 SCALE-Sim 对照在主 workload、size extension 和 hold-out 中均保持注册的 10% Trend 边界。
4. Accelergy action accounting 已完成：ERT 非空、无 dummy/schema fallback，one-action replay exact。
5. 共享 CNN transport input 与 repeat hash exact；跨工具层级排序一致，但这不是完整 CNN 数值执行。
6. 光学 capacity sweep 展示 87.5% 的内部改善；同时 same-condition 结果明确表明当前 optical implementation 仍慢于、耗能高于 electrical。
7. STAGE trace 能把 stall 定位到 component/link/packet/reason，并通过单参数 intervention 验证 bottleneck migration。

### 只能写成 Trend 或范围内结论

- BookSim2/STAGE 的 hotspot ordering；
- SCALE-Sim/STAGE 的 warm-cycle 和 hold-out scaling；
- CNN layer makespan ranking；
- literature-backed CIM non-ideal statistics；
- 外部工具与 STAGE 的 wall-clock context。

### 仍不能成立

- “STAGE 数值匹配 BookSim2 的所有 saturation point”；
- “STAGE uniform saturation 已在测试范围内找到”；
- “STAGE full-system cycles 等同 Timeloop analytical cycles”；
- “完整 MNIST CNN 由 STAGE 原生执行并复现 98.39%”；
- “当前 optical transport 优于 electrical”；
- “模型提供 BER accuracy”；
- “CIM complete energy/op 低于 digital”；
- “能耗或延迟已经 silicon calibrated”；
- “外部图展示了 BookSim2 与 STAGE 的共同 router occupancy”；
- “新 trace analysis 已进入 Unity”。

## 4. 准确复现总则

### 4.1 不覆盖已批准 evidence

不要直接把新运行写回 `final_20260716`、`reviewer_extension_20260717` 或 `external_draft_20260716`。先复制对应 plan，将其中唯一的 `bundle_root` 改为新的 F 盘目录，再运行。所有比较都必须保留 candidate ID、resolved config hash、workload/mapping/model hash、seed 和原始输出。

PowerShell 初始化：

```powershell
Set-Location ARTIFACT_ROOT
$ReproRoot = 'experiments/aspdac/results/repro_claims_20260717'
New-Item -ItemType Directory -Force "$ReproRoot/manifests", "$ReproRoot/raw", "$ReproRoot/summary", "$ReproRoot/failures" | Out-Null
```

复制 plan 并安全改写输出根目录的示例：

```powershell
$SourcePlan = 'experiments/aspdac/results/final_20260716/manifests/plan_rq1_exact.yaml'
$ReproPlan = "$ReproRoot/manifests/plan_rq1_exact.yaml"
$yaml = Get-Content -Raw $SourcePlan
$yaml = $yaml.Replace('bundle_root: experiments/aspdac/results/final_20260716', "bundle_root: $($ReproRoot.Replace('\','/'))")
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $ReproPlan), $yaml, [System.Text.UTF8Encoding]::new($false))
python -B experiments/aspdac/scripts/experiment_manager.py $ReproPlan --action run
python -B experiments/aspdac/scripts/experiment_manager.py $ReproPlan --action status
```

`experiment_manager.py` 的 candidate ID 来自 resolved config/axes，而不是文件顺序；重新运行时必须使用原 plan、原 seed 和原工具版本。外部工具环境与 hash 见各 bundle 的 `manifests/` 和 `config/`。

## 5. Recipe 索引

### R0 — 基线与证据完整性

按顺序运行，不并行：

```powershell
dotnet build STAGE.sln -c Release -m:1
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group phase1c-golden --results "$ReproRoot/raw/required-golden.json"
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group phase8a --results "$ReproRoot/raw/phase8a.json"
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --group phase9 --results "$ReproRoot/raw/phase9.json"
dotnet run --project tests/HardwareSim.Tests/HardwareSim.Tests.csproj -c Release --no-build -- --results "$ReproRoot/raw/full-release.json"
```

终端验收：build 0 warning/0 error；Golden 7/7；Phase 8A 154/154；Phase 9 24/24；Full Release 491/491。不得修改 approved Golden hash。

### R1 — Exact/oracle

- Plan：`experiments/aspdac/results/final_20260716/manifests/plan_rq1_exact.yaml`
- Config：`experiments/aspdac/specs/final_configs/s_native.yaml`
- Runner：按 4.1 复制 plan 后执行 `experiment_manager.py ... --action run`
- 验收：每个 case 两次；`passed=true`；同 case 的 `canonical_trace_hash` 和 `canonical_trace_bytes` 完全相同。
- Terminal summary：`experiments/aspdac/results/final_20260716/summary/rq1_exact_cases.csv`、`rq1_repeat_hashes.csv`。

### R2 — BookSim2/STAGE NoC

- STAGE plan：`manifests/plan_rq2_booksim_stage.yaml`，frozen config 为 `experiments/aspdac/specs/final_configs/v_bs.yaml`。
- STAGE 执行：复制 plan、改写 `bundle_root`，再运行 Experiment Manager；保持 traffic/rate/seed 矩阵不变。
- BookSim2 authority：`manifests/rq2_external_raw_manifest.json` 与 `raw/external_booksim_registered/`；uniform extension 使用 `run_booksim_uniform_extension.py --bundle <fresh-root> --rates 0.36 0.40 0.44 0.48 0.52 0.56 0.60`。
- 对齐键：traffic、injection rate、seed、4×4 mesh、128-bit one-flit packet、1 VC、16 flits/VC；不要按 native cycle 强制归一。
- 验收：10 seeds 的 mean/std/95% CI；saturation 使用 summary 中注册的“两连续 rate”条件；timeout/unstable 不得删除。

### R3 — Timeloop、SCALE-Sim 与 Accelergy

- STAGE plan：`manifests/plan_rq3_matched_compute.yaml`、`plan_rq3_energy.yaml`、`plan_rq3_size_stage.yaml`。
- Timeloop：`run_timeloop_model_final.py --source <frozen-source> --output <fresh-output>`；size extension 使用 `run_rq3_size_timeloop_accelergy.py --repo-root <repo> --matrix experiments/aspdac/results/final_20260716/manifests/size_scaling_matrix.json --bundle <fresh-root>`。
- SCALE-Sim size：`run_rq3_size_scalesim.py --matrix experiments/aspdac/results/final_20260716/manifests/size_scaling_matrix.json --frozen-config experiments/aspdac/specs/final_configs/v_ss.yaml --output-root <fresh-root>/raw/size_scaling_scalesim --failure-root <fresh-root>/failures --resume`。
- 对齐键：workload/shape、mapping hash、16-MAC schedule 或 4×4 WS contract、paired config hash。
- 验收：Timeloop compute/access exact；SCALE-Sim 只按 Trend 10% envelope；Accelergy ERT 非空、5 tables、无 dummy/schema fallback、one-action exact。

### R4 — MNIST CNN 与共享 NoC trace

```powershell
python -B experiments/aspdac/scripts/run_mnist_cnn_feasibility.py --data-root experiments/aspdac/data --output "$ReproRoot/raw/mnist_cnn" --epochs 3 --batch-size 128 --test-batch-size 256 --seed 20260716 --resume
python -B experiments/aspdac/scripts/build_mnist_cnn_noc_trace.py --bundle $ReproRoot
python -B experiments/aspdac/scripts/analyze_mnist_cnn_noc.py --bundle $ReproRoot
```

STAGE/BookSim2 transport plans为 `plan_mnist_cnn_noc_booksim_stage_v2.yaml`。验收时分开检查：PyTorch accuracy/prediction hash、packet input hash、repeat delivery/metrics hash、layer ranking。不得把 PyTorch 98.39% 写成 STAGE accuracy。

### R5 — Optical oracle 与 matched transport

- Plan：`manifests/plan_rq4_optical.yaml`；frozen configs 为 `s_native.yaml` 及同 plan 引用的 optical transport profile。
- 执行：复制 plan并改写 `bundle_root`，运行 Experiment Manager。
- 配对 gate：workload/endpoints/mapping/seed/compute/memory hash 相同，只允许 transport/interface 改变。
- 验收：oracle 两次 exact；128-bit payload 编码为 132 bits、2 cycles；capacity 1/2/4/8 单调；BER 字段保持 `BER not modeled`。

### R6 — STAGE-only co-design、precision、CIM 与 MoT

- Co-design/CIM plan：`manifests/plan_stage_codesign.yaml`；执行后检查 paired-axis 之外的 invariant hash 不变。
- MoT 原始 authority：`graph/phase8a-256-dc-sweep-static-weights-mot-inr-v4.csv/.json`；纸面 bundle 注册器为 `package_mot_mapping_for_paper.py`。
- Precision gate：相同 workload hash；logical bits、packetized bits、packet count、conversion cycles/energy 分列。
- CIM gate：Functional exact、seed 42 repeat、seeds 0–9；unknown term 保留字符串 `unknown`，不得填 0。
- 验收：单轴变化；target stall 如预测下降；下一 bottleneck 与预测一致；repeat trace deterministic。

### R7 — Reviewer extension P0/P1/P2

```powershell
python -B experiments/aspdac/scripts/reviewer_extension_p0.py run --plan experiments/aspdac/specs/reviewer_extension_20260717/p0_stage_scalability.yaml --bundle $ReproRoot
python -B experiments/aspdac/scripts/reviewer_extension_p0.py analyze --bundle $ReproRoot
python -B experiments/aspdac/scripts/reviewer_extension_p1.py prepare --run-root $ReproRoot
python -B experiments/aspdac/scripts/reviewer_extension_p1.py claim-next --run-root $ReproRoot --worker repro-0
# 按 claim-next 返回的 candidate payload 调用对应真实 runner/oracle；随后用 record 写入 completed/not_supported。
python -B experiments/aspdac/scripts/reviewer_extension_p1.py summarize --run-root $ReproRoot
```

P2 必须按 `plan → preregister → runner output → observe → analyze → report` 顺序，不能在看到 intervention 结果后改 prediction interval。P1 的 capacity-release 行必须保持 `not_supported`。Reviewer terminal disposition 应为 P0 completed=120，P1 completed=46/not-supported=4，P2 completed=8。

### R8 — 外部 trace visualization 与 claim register

```powershell
python -B experiments/aspdac/scripts/build_trace_visualization_bundle.py --output experiments/aspdac/results/trace_visualization_20260717
python -B -m unittest experiments.aspdac.tests.test_trace_visualization_bundle -v
```

验收：canonical entity map、18,337 packet join、12,433-offer hotspot replay、multi-resolution timeline、causal path、Trend gate、source immutability 和 deterministic PDF/CSV hash 全部通过。该 recipe 不修改 Unity，也不生成缺失的 BookSim2 router occupancy 或 STAGE CNN per-packet delivery。

## 6. 证据入口

- Final claim authority：`experiments/aspdac/results/final_20260716/summary/claim_status.md`
- Reviewer-extension authority：`experiments/aspdac/results/reviewer_extension_20260717/summary/reviewer_claim_matrix.md`
- External trace authority：`experiments/aspdac/results/trace_visualization_20260717/summary/claim_status.md`
- Final manifest：`experiments/aspdac/results/final_20260716/manifests/final_manifest_index.json`
- Reviewer manifest：`experiments/aspdac/results/reviewer_extension_20260717/manifests/reviewer_extension_manifest_index.json`
- Trace visualization manifest：`experiments/aspdac/results/trace_visualization_20260717/manifests/trace_visualization_manifest_index.json`

若本表与 terminal manifest/raw evidence 冲突，以 raw evidence、resolved config、paired hash 和较新的 terminal disposition 为准；不得用本表覆盖原始数据。
