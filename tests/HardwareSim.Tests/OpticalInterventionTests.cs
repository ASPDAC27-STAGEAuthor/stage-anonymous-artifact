using HardwareSim.Core;

internal static class OpticalInterventionTests
{
    public static IReadOnlyList<TestCase> All =>
    [
        new("P10-OPT-INT-001 WDM optical path relieves a constrained electrical link", WdmRelievesConstrainedElectricalLink, "paper")
    ];

    private static void WdmRelievesConstrainedElectricalLink()
    {
        AspdacTransportOptions Options(string mode, int capacity, string transportHash) => new(
            "attention_128_64",
            mode,
            512,
            8,
            capacity,
            4,
            128,
            "workload",
            "mapping",
            "compute",
            "memory",
            "endpoint",
            transportHash);

        var electrical = AspdacTransportRuntime.Run(Options("electrical_contended", 1, "electrical-one-channel"));
        var optical = AspdacTransportRuntime.Run(Options("optical_contended", 8, "optical-eight-wavelength"));
        var repeated = AspdacTransportRuntime.Run(Options("optical_contended", 8, "optical-eight-wavelength"));

        TestAssert.True(optical.TotalCycles < electrical.TotalCycles, "WDM intervention must reduce transport cycles");
        TestAssert.True(optical.MeanPacketLatencyCycles < electrical.MeanPacketLatencyCycles, "WDM intervention must reduce mean packet latency");
        TestAssert.True(electrical.BackpressureEvents > optical.BackpressureEvents, "WDM intervention must reduce backpressure");
        TestAssert.True(optical.TotalTransportEnergyPj > electrical.TotalTransportEnergyPj, "latency improvement must not be misreported as an energy improvement");
        TestAssert.Equal(electrical.WorkloadHash, optical.WorkloadHash, "paired workload hash");
        TestAssert.Equal(electrical.MappingHash, optical.MappingHash, "paired mapping hash");
        TestAssert.Equal(electrical.ComputeHash, optical.ComputeHash, "paired compute hash");
        TestAssert.Equal(electrical.MemoryHash, optical.MemoryHash, "paired memory hash");
        TestAssert.Equal(electrical.EndpointHash, optical.EndpointHash, "paired endpoint hash");
        TestAssert.Equal(optical.CanonicalTraceSha256, repeated.CanonicalTraceSha256, "optical intervention trace repeat");
    }
}