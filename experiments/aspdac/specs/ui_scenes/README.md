# ASP-DAC multi-chip electro-optical scene

This is a real HardwareGraph 1.0 project for the final Figure 2 capture.

- Chiplet 0 contains HBM, transmit and receive SerDes, E/O and O/E interfaces, result merge, and output storage.
- Chiplet 1 contains an eight-PE GEMM cluster and reduction.
- Chiplet 2 contains an eight-PE GEMM cluster and softmax.
- The optical interposer carries separate forward and return two-lane WDM paths.
- Every cross-chip path uses explicit Serializer -> E/O -> WDM -> waveguide -> WDM -> O/E -> Deserializer components.
- The scene uses one transceiver abstraction level; it does not duplicate lasers, modulators, or photodetectors.
- Metadata records 128-bit payloads, 64b/66b encoding, 132 encoded bits, two service cycles, 0 dBm source power, -18 dBm receiver sensitivity, and BER not modeled.

Import the JSON, keep all four group labels visible, fit the graph to the canvas, compile, and capture a nonzero trace state if the full scene executes in the editor.
