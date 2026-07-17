# Canonical trace and expected-hash contract

This document defines what the artifact means by a byte-identical canonical trace. The implementation authority is <code>src/HardwareSim.Core/CanonicalTraceHash.cs</code>; the tables below are locked expectations, not hashes recomputed during documentation generation.

## Canonical serialization

The hash input is a compact JSON document encoded as UTF-8 and passed directly to SHA-256. No byte-order mark or trailing newline is appended to the hash input.

The document uses this fixed top-level order: <code>algorithm</code>, <code>trace_schema_version</code>, <code>seed</code>, <code>config</code>, and <code>trace</code>. Configuration keys are sorted with ordinal comparison. Cycle records are sorted numerically. Event records retain deterministic committed emission order and use the fixed field order <code>type</code>, <code>bits</code>, then the optional fields <code>packet_id</code>, <code>component_id</code>, <code>link_id</code>, <code>source</code>, <code>destination</code>, and <code>detail</code>. Empty optional strings are omitted.

The stable configuration filter removes timestamps, created/updated/generated times, absolute/output paths, working directories, rooted path values, and timestamp-valued strings. SHA-256 digests use lowercase hexadecimal and invariant-culture formatting.

## Cross-platform protections

- The canonical hash input has no platform-native newline operation, so CRLF versus LF cannot affect it.
- Rooted/absolute paths and timestamp-like configuration values are excluded.
- Configuration ordering is ordinal rather than locale-dependent.
- Numeric JSON values are emitted by <code>System.Text.Json</code>; hash formatting itself is invariant-culture.
- Cycle order and committed event order are deterministic; no dictionary enumeration order is used for configuration.
- The seed and trace schema version are explicit hash inputs.
- The artifact targets .NET 8 on Windows, Linux, and macOS. Cross-platform identity is the serialization contract; reviewers should still report the OS, architecture, and exact <code>dotnet --info</code> output with any mismatch.

Generated report files use LF endings. This is separate from the compact canonical JSON hash input, which has no appended line ending.

## Seven live golden hashes

Running the <code>golden</code> test group regenerates each trace twice, checks byte identity, and compares the digest below.

| Golden case | Expected SHA-256 |
|---|---|
| Single Link Latency | <code>ea5d8d34ae5bb553ca1f1d6fb813ad4d5b47016c42a56760054f21578c572f7f</code> |
| Buffer Backpressure | <code>5ec3b01df3e3df5b060a4c254c324488f32a4e7bb853ee4898c0085b721eeab3</code> |
| Router Round-Robin | <code>7d95c56d35b6051b6bb997f1bb75c4d7331eae4af12432462fd134b0661e4440</code> |
| PE Compute | <code>1c4ba6ffcb00444d7409ae2a724d9938d42a8058eb72aeeac76d7ef3d37fb116</code> |
| Memory Read | <code>a785341081d4343a23cade6575177df572aa530c9c7e4c884476f4cd7a93372f</code> |
| 2x2 Mesh | <code>99eb4144b50a7fd2f029ffc279098faedc0f1cf0a419863601815dfe7d7362f0</code> |
| Workload Injection | <code>e8fd4ab1bd6506b46d8c4343fec15d5a170a550ee99d9e295144622fd311bd89</code> |

## Nine analytical contract hashes

The claim validator locks these hashes and verifies two byte-identical repeats per case.

| Analytical contract | Expected SHA-256 |
|---|---|
| backpressure | <code>b721530d43bf2ee66d3560b26e7092ba655a1e8b3683c2dc69941645d328c079</code> |
| compile_non_mutation | <code>2d77093f637dc2e14e76f5aaa548ff004a16a94d873c1651d7a38808c553ff8e</code> |
| current_next_visibility | <code>0e4db9fd07357145c9a7a589c005b79d612b4bf54a7be97e0270063d461dca36</code> |
| graph_round_trip | <code>e6d253c4f48a312e81c06c1fe001cd6a2df01b1e20393f3b23e55d4281065cef</code> |
| optical_loss_power_margin | <code>3549e5320b9e87ff088e36dadaead0a7ee530a1fa5840442d255120730d9d77f</code> |
| packet_serialization_132b | <code>b67b89ae6faade90c9bf972b22361e883cf94ba02bfcb4d7632ce183e0ce9245</code> |
| reduction | <code>77a7e00b1f934108990e8b32dd78275f63dda18719b5ed8cf7f668741858130c</code> |
| softmax | <code>d6b3637ede22b6f598dd5507f965b23b6f837781f67112ea4d1c3e44f8b5ea81</code> |
| wavelength_arbitration | <code>50620c37578e981bd53c80209baf26f380f2b345458c1f512b762f6324071321</code> |

## Seven supported NoC contract hashes

Each case has two identical repeats. STAGE and oracle serialize different projections, so their hashes are expected to differ from each other while their cycle contract must match.

| NoC case | STAGE trace SHA-256 | Oracle timeline SHA-256 |
|---|---|---|
| noc_n01_single_128 | <code>39b5935d8ef3f5b80bc28ba38fcca659ea4686ea990fca041e720b311214b625</code> | <code>eb27dd9911b609414d5d4be795575d742237ca9dbd623481e29ef338b30a3cf0</code> |
| noc_n02_single_256_vc1 | <code>775c258695f69a6bf801e0d5365ee21f77838a8c1e3a786127ae785e2505aeaf</code> | <code>aa559a872cae7d6f7a513b86c42a3eb04e4b1c9d344f92cbc648f4e2aaa0142b</code> |
| noc_n03_single_512_vc3 | <code>b444ceb9860f3675c23347ba7c30c7542c6e9bbe5f367f320e1aac0cf0f3d6aa</code> | <code>80667b8b8db1f0d84a2a784d9615d9841e18310e2a51d1f4a80eb3fd49065041</code> |
| noc_n04_single_1024 | <code>76745208393878939427057e208d749a3b04802a75e2ec44007b580d19a64b88</code> | <code>cf97ce053ddc0539965cb41f764d848e44bf957773f033fc6566ff7ff277c1d8</code> |
| noc_n05_contend_128 | <code>df0418aaf50ba81b05d8343818400f1ba33159a63016566aa84148eb8f6067ff</code> | <code>fbbeb638ee913896a62eb40d75d09881ddb260c3f7c9dfffc231b72560860b21</code> |
| noc_n06_contend_512 | <code>d031287b738b199add6a75685993b871ad028c8065c7d38df842a5862d66b2dc</code> | <code>823dd9817b95c9eda5c28c5ccb2a2b175db545ab12896e3ec4f313e8d38240a1</code> |
| noc_n09_atomic_depth_boundary | <code>ef32c764fe3dc95b2332b9cbd16b3a890ec6df02f1132c333d0a8144d652b8e3</code> | <code>19151ad92ba991bc2ac4721f26211f93e6ecc637e0503d29599c20107d0dcb74</code> |

N07 and N08 are explicitly unsupported blocked-release cases and are not counted as passing supported contracts.

## Failure localization

To compare two exported canonical JSON files:

~~~bash
python scripts/compare_hashes.py expected.json actual.json
~~~

The command prints both SHA-256 values, the first JSON path whose values differ when possible, and the first differing byte offset with local byte context. It exits 0 only for byte-identical files.
