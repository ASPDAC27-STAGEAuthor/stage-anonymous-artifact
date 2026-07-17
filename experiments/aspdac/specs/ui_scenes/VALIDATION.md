# Validation record

Graph: aspdac-multichip-electro-optical.hardware.json

- SHA-256: 53839c28a3a6c81c3bc2fbc2f0fd21a38ba1ba3948012f6692e125e89d7170cb
- HardwareGraph 1.0 deserialization: pass
- Contract validation: 0 errors, 0 warnings
- Production compilation: pass, 35 components and 36 links
- Compile diagnostics: 0 errors; 8 expected P8BerNotModeled warnings (four O/E components reported by both validation stages)
- Physical routing: 36/36 links routed; no routing-congestion warning
- Runtime smoke: pass, seed 17, 201 cycles, 5 packets delivered
- Runtime composition: 8 activation packets reduce to one result; 4 weight-path packets pass through the softmax chiplet
- Repeatability: two full-trace runs produced the same SHA-256 5277f7bfc75590e0725e31beae415e64f952ef0d176618cf5adc2795f37d5f02

This record validates the Figure 2 scene itself. It is not an additional paper performance experiment and must not be cited as silicon or BER accuracy.
