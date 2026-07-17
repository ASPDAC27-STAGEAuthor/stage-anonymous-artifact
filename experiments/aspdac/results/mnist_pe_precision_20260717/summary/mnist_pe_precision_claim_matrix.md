# MNIST PE precision claim matrix

| Claim | Status | Evidence | Boundary |
|---|---|---|---|
| `C-MNIST-PE-ARITH-CONFORMANCE` | `measured/exact` | 96/96 real `CoreDigitalVmmKernel` cases; C# and Python encoded bits exact | Digital PE Conv/FC VMM arithmetic only |
| `C-MNIST-PE-PRECISION-ACCURACY` | `measured/functional-bridge` | 10,000 images × 4 profiles × 2 exact repeats | Bias, ReLU, AvgPool, sequencing, and argmax are the deterministic functional harness |
| `C-MNIST-PE-ACCURACY-COST` | `measured` | 8 accuracy records paired with 88 STAGE layer/network cost records | Compute timing profile does not distinguish accumulator dtype |
| `C-MNIST-STAGE-ENDTOEND` | `not_supported` | No native full-CNN STAGE runtime was introduced | Must remain not supported |

## Measured accuracy

| Profile | Correct | Accuracy | Delta vs FP32 | 95% paired CI | Disagreements |
|---|---:|---:|---:|---:|---:|
| `fp32_a32` | 9839/10000 | 98.39% | +0.00 pp | [+0.00, +0.00] pp | 0 |
| `fp16_a16` | 9840/10000 | 98.40% | +0.01 pp | [+0.00, +0.03] pp | 1 |
| `fp8_a16` | 9828/10000 | 98.28% | -0.11 pp | [-0.20, -0.02] pp | 24 |
| `fp8_a8` | 9735/10000 | 97.35% | -1.04 pp | [-1.29, -0.79] pp | 178 |

## Evidence discipline

- Final R1 matrix: 192/192 terminal candidates plus 4/4 smoke candidates.
- Retained non-final failure records: 5; none is silently removed or admitted into final R1 statistics.
- FP32 bridge reproduces the frozen 98.39% oracle prediction hash exactly.
- FP8/A16 and FP8/A8 share 8-bit transport width; their transport totals match where the frozen system model does not distinguish accumulator dtype.
- No changes were made to `latex/current_overleaf/`.
