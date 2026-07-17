using System.Security.Cryptography;
using System.Text;
using HardwareSim.Core;

internal static class PaperExperimentTests
{
    internal static IReadOnlyList<TestCase> All { get; } =
    [
        new("P10-EXP-001 V-BS frozen topology and transport constants are exact", FrozenVbsContractIsExact, "paper"),
        new("P10-EXP-002 V-BS runtime is deterministic for the same candidate", VbsRuntimeRepeatsExactly, "paper"),
        new("P10-EXP-003 V-BS hotspot runtime records real congestion", VbsHotspotRecordsCongestion, "paper"),
        new("P10-EXP-004 V-TL cycle replay preserves the exact 16-MAC floor", VtlCycleReplayIsExact, "paper"),
        new("P10-EXP-005 V-SS cycle service exposes the MLP bandwidth bottleneck", VssCycleServiceIsBandwidthBound, "paper"),
        new("P10-EXP-006 RQ4 optical oracle preserves the exact hand calculation", OpticalOracleIsExact, "paper"),
        new("P10-EXP-007 RQ4 matched transport preserves paired hashes and capacity monotonicity", MatchedTransportCapacityIsMonotonic, "paper"),
        new("P10-EXP-008 co-design intervention moves the diagnosed memory bottleneck", CodesignInterventionMovesBottleneck, "paper"),
        new("P10-EXP-009 precision runtime preserves logical bits and exposes conversion", PrecisionRuntimePreservesBitAccounting, "paper"),
        new("P10-EXP-010 MNIST CNN lowering preserves exact work and deterministic service", MnistCnnLayerRuntimeIsExact, "paper"),
        new("P10-EXP-011 CNN packet trace is authenticated and deterministic on V-BS", CnnPacketTraceRuntimeIsExact, "paper"),
        new("P10-MP-COST-001 MNIST PE profiles preserve packet and converter accounting", MnistPeProfileCostAccountingIsExact, "paper"),
        new("P10-MP-KERNEL-001 MNIST PE FP8 kernel matches encoded-bit reference", MnistPeFp8KernelConformanceIsExact, "paper")
    ];

    private static void FrozenVbsContractIsExact()
    {
        TestAssert.Equal(4, AspdacVbsRuntime.Dimension, "mesh dimension");
        TestAssert.Equal(16, AspdacVbsRuntime.RouterCount, "router and endpoint count");
        TestAssert.Equal(128, AspdacVbsRuntime.PacketBits, "one packet is one 128-bit flit");
        TestAssert.Equal(128, AspdacVbsRuntime.LinkBitsPerCycle, "link width");
        TestAssert.Equal(1, AspdacVbsRuntime.VirtualChannels, "virtual channel count");
        TestAssert.Equal(16, AspdacVbsRuntime.FlitsPerVirtualChannel, "flits per VC");
        TestAssert.Equal(2048, AspdacVbsRuntime.PacketBits * AspdacVbsRuntime.FlitsPerVirtualChannel, "matched VC capacity bits");
    }

    private static void VbsRuntimeRepeatsExactly()
    {
        var options = new AspdacVbsOptions("uniform", 0.08, 7, 20, 120, 80, RetainEventHash: true);
        var first = AspdacVbsRuntime.Run(options);
        var second = AspdacVbsRuntime.Run(options);

        TestAssert.Equal(first.RuntimeEventHash, second.RuntimeEventHash, "event audit hash");
        TestAssert.Equal(first.OfferedPackets, second.OfferedPackets, "offered packet count");
        TestAssert.Equal(first.DeliveredPackets, second.DeliveredPackets, "delivered packet count");
        TestAssert.Near(first.PacketLatencyAverage, second.PacketLatencyAverage, 0d, "mean latency");
        TestAssert.True(first.Completed && second.Completed, "bounded smoke run should drain");
    }

    private static void VbsHotspotRecordsCongestion()
    {
        var result = AspdacVbsRuntime.Run(new AspdacVbsOptions("hotspot_node5", 0.20, 3, 20, 160, 200, RetainEventHash: false));

        TestAssert.True(result.OfferedPackets > 0, "traffic generator must offer packets");
        TestAssert.True(result.RouterConflictStalls > 0 || result.BackpressureCycles > 0, "hotspot must exercise router contention or backpressure");
        TestAssert.True(result.CongestionCycles > 0, "hotspot must record congestion cycles");
        TestAssert.True(result.StallReasons.ContainsKey("router_conflict"), "stable stall reason must be present");
    }
    private static void VtlCycleReplayIsExact()
    {
        var workload = new AspdacMatchedWorkload("gemm_256", 256, 256, 256, 16_777_216, 34_471_936, 53_608_448, 11_534_336, 6_291_456);
        var computeOnly = AspdacMatchedRuntime.RunTimeloopMatched(workload, fullSystem: false);
        var fullSystem = AspdacMatchedRuntime.RunTimeloopMatched(workload, fullSystem: true);

        TestAssert.Equal(1_048_576L, computeOnly.TotalCycles, "exact 16-MAC floor");
        TestAssert.Equal(workload.ExpectedMacs, computeOnly.CompletedMacs, "completed MAC count");
        TestAssert.Equal(fullSystem.ComputeCycles + fullSystem.MemoryCycles + fullSystem.NocCycles + fullSystem.SerializationCycles, fullSystem.TotalCycles, "full-system attribution");
        TestAssert.True(fullSystem.TotalCycles >= computeOnly.TotalCycles, "full-system cycles cannot beat the compute floor");
    }

    private static void VssCycleServiceIsBandwidthBound()
    {
        var workload = new AspdacMatchedWorkload(
            "mlp_l1", 128, 256, 512, 16_777_216,
            SramIfmapReads: 4_194_304,
            SramFilterReads: 131_072,
            SramOfmapWrites: 4_194_304,
            DramIfmapReads: 8_388_608,
            DramFilterReads: 22_138_880,
            DramOfmapWrites: 4_194_311);
        var warm = AspdacMatchedRuntime.RunScaleSimMatched(workload, coldStart: false);
        var cold = AspdacMatchedRuntime.RunScaleSimMatched(workload, coldStart: true);

        TestAssert.Equal(2_767_360L, warm.TotalCycles, "filter stream bound");
        TestAssert.True(warm.MemoryStallCycles > 0, "MLP layer 1 must be memory bound");
        TestAssert.Equal(1_024L, cold.PrefetchCycles, "two sequential 8-KiB input prefetches");
        TestAssert.Equal(warm.TotalCycles + cold.PrefetchCycles, cold.TotalCycles, "cold-start separation");
    }

    private static void OpticalOracleIsExact()
    {
        var first = AspdacTransportRuntime.RunOracle();
        var second = AspdacTransportRuntime.RunOracle();

        TestAssert.Near(4.11, first.RouteLossDb, 1e-12, "route loss");
        TestAssert.Near(-4.11, first.ReceivedPowerDbm, 1e-12, "received power");
        TestAssert.Near(-1.11, first.ExactCaseMarginDb, 1e-12, "exact-case margin");
        TestAssert.Near(13.89, first.MatchedSystemMarginDb, 1e-12, "matched-system margin");
        TestAssert.Equal(132, first.EncodedBits, "64b/66b encoded bits");
        TestAssert.Equal(2, first.ServiceCycles, "128-bit service cycles");
        TestAssert.Equal(first.CanonicalTraceSha256, second.CanonicalTraceSha256, "oracle trace hash");
    }

    private static void MatchedTransportCapacityIsMonotonic()
    {
        AspdacTransportOptions Options(int capacity) => new(
            "attention_128_64",
            "optical_contended",
            256,
            16,
            capacity,
            4,
            128,
            "workload",
            "mapping",
            "compute",
            "memory",
            "endpoint",
            $"optical-{capacity}");

        var capacityOne = AspdacTransportRuntime.Run(Options(1));
        var capacityEight = AspdacTransportRuntime.Run(Options(8));
        var repeated = AspdacTransportRuntime.Run(Options(8));

        TestAssert.True(capacityEight.TotalCycles <= capacityOne.TotalCycles, "more channels cannot increase cycles");
        TestAssert.True(capacityEight.ConflictCount <= capacityOne.ConflictCount, "more channels cannot increase conflicts");
        TestAssert.True(capacityOne.BackpressureEvents > 0, "contended transport must expose backpressure");
        TestAssert.Equal(capacityEight.CanonicalTraceSha256, repeated.CanonicalTraceSha256, "runtime trace hash");
        TestAssert.Equal(capacityOne.WorkloadHash, capacityEight.WorkloadHash, "paired workload hash");
        TestAssert.Equal(capacityOne.MappingHash, capacityEight.MappingHash, "paired mapping hash");
        TestAssert.Equal("BER not modeled", capacityEight.BerStatus, "BER scope");
    }

    private static void CodesignInterventionMovesBottleneck()
    {
        AspdacCodesignOptions Options(int memoryPorts) => new(
            "attention_128_64", 2_097_152, 8_192, 256, 256, 128, memoryPorts, 4,
            "graph", "workload", "mapping", "model");
        var baseline = AspdacCodesignRuntime.Run(Options(1));
        var intervention = AspdacCodesignRuntime.Run(Options(2));

        TestAssert.Equal("memory", baseline.DominantBottleneck, "baseline bottleneck");
        TestAssert.Equal("noc", intervention.DominantBottleneck, "next bottleneck");
        TestAssert.True(intervention.MemoryCriticalCycles < baseline.MemoryCriticalCycles, "target memory stall must fall");
        TestAssert.True(intervention.TotalCycles < baseline.TotalCycles, "intervention must reduce cycles");
        TestAssert.Equal(baseline.WorkloadHash, intervention.WorkloadHash, "paired workload hash");
    }

    private static void PrecisionRuntimePreservesBitAccounting()
    {
        var baseline = new AspdacCodesignOptions(
            "attention_128_64", 2_097_152, 8_192, 256, 256, 128, 1, 4,
            "graph", "workload", "mapping", "model");
        var fp16 = AspdacCodesignRuntime.RunPrecision(new AspdacPrecisionOptions("FP16", 16, 0, 65_536, baseline));
        var int8 = AspdacCodesignRuntime.RunPrecision(new AspdacPrecisionOptions("INT8", 8, 2, 65_536, baseline));

        TestAssert.Equal(1_048_576L, fp16.LogicalBits, "FP16 logical bits");
        TestAssert.Equal(524_288L, int8.LogicalBits, "INT8 logical bits");
        TestAssert.Equal(fp16.PacketCount / 2, int8.PacketCount, "INT8 packet count");
        TestAssert.True(int8.ConversionCycles > 0 && int8.ConversionEnergyPj > 0, "INT8 conversion must be explicit");
        TestAssert.Equal(fp16.MappingHash, int8.MappingHash, "mapping hash");
    }
    private static void MnistCnnLayerRuntimeIsExact()
    {
        AspdacCnnLayerOptions Options() => new(
            "conv1", "conv2d_im2col", 576, 6, 25, 1, 32, 4_320, true,
            "model", "dataset", "prediction", "lowering");
        var first = AspdacCnnRuntime.Run(Options());
        var repeated = AspdacCnnRuntime.Run(Options());

        TestAssert.Equal(86_400L, first.CompletedMacs, "exact Conv1 MAC count");
        TestAssert.Equal(14_400L, first.InputElements, "lowered input elements");
        TestAssert.Equal(150L, first.WeightElements, "weight elements");
        TestAssert.Equal(3_456L, first.OutputElements, "output elements");
        TestAssert.Equal(first.ComputeCycles + first.MemoryCycles + first.NocCycles + first.PostOpCycles, first.TotalCycles, "cycle attribution");
        TestAssert.Equal("memory", first.DominantService, "S-Native layer bottleneck");
        TestAssert.Equal(first.CanonicalTraceHash, repeated.CanonicalTraceHash, "cycle trace repeat hash");
    }

    private static void MnistPeProfileCostAccountingIsExact()
    {
        AspdacCnnLayerOptions Options(int bits, int passes, string profile, string hash) => new(
            "conv1", "conv2d_im2col", 576, 6, 25, 10_000, bits, 4_320, true,
            "model", "dataset", "prediction", "lowering", passes, profile, hash);
        var fp32 = AspdacCnnRuntime.Run(Options(32, 0, "fp32_a32", "fp32-hash"));
        var fp8 = AspdacCnnRuntime.Run(Options(8, 2, "fp8_a8", "fp8-hash"));
        var repeated = AspdacCnnRuntime.Run(Options(8, 2, "fp8_a8", "fp8-hash"));

        TestAssert.Equal(fp32.CompletedMacs, fp8.CompletedMacs, "arithmetic profiles preserve CNN work");
        TestAssert.Equal(fp32.ComputeCycles, fp8.ComputeCycles, "frozen compute timing does not distinguish dtype");
        TestAssert.True(fp8.LogicalBits < fp32.LogicalBits, "FP8 reduces logical traffic");
        TestAssert.True(fp8.PacketizedBits % AspdacCnnRuntime.LinkBitsPerCycle == 0, "packetized traffic aligns to 128 bits");
        TestAssert.Equal(fp8.PacketizedBits - fp8.LogicalBits, fp8.PaddingBits, "padding attribution");
        TestAssert.Equal(fp8.PacketizedBits / AspdacCnnRuntime.LinkBitsPerCycle, fp8.PacketCount, "packet count");
        TestAssert.True(fp8.ConversionCycles > 0 && fp8.ConversionEnergyPj > 0, "FP8 conversion is explicit");
        TestAssert.Equal(fp8.CanonicalTraceHash, repeated.CanonicalTraceHash, "profile cost trace repeat hash");
    }

    private static void MnistPeFp8KernelConformanceIsExact()
    {
        var template = ComponentTemplateJson.Clone(ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic());
        template.StorageLayouts.Single(layout => layout.Id == "weight_store_0").Rows = 4096;
        template.CompiledProfile = null;
        template.Provenance.CompileHash = "";
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["array_rows"] = "10",
            ["array_cols"] = "7",
            ["input_dtype"] = "fp8",
            ["weight_dtype"] = "fp8",
            ["accumulate_dtype"] = "fp8",
            ["output_dtype"] = "fp8",
            ["macs_per_cycle"] = "70",
            ["weight_write_bandwidth_bits_per_cycle"] = "1048576"
        };
        var first = new ComponentKernelTestRunner().Run(
            template, ComponentTypeRegistry.CreateDefault(), overrides, seed: 7405);
        var repeated = new ComponentKernelTestRunner().Run(
            template, ComponentTypeRegistry.CreateDefault(), overrides, seed: 7405);

        TestAssert.True(first.IsSuccess && repeated.IsSuccess, "real CoreDigitalVmmKernel conformance run");
        TestAssert.Equal(first.ExpectedOutputHash, first.ActualOutputHash, "kernel/reference encoded output");
        TestAssert.Equal(first.ActualOutputHash, repeated.ActualOutputHash, "encoded output repeat hash");
        TestAssert.Equal(first.TraceHash, repeated.TraceHash, "cycle trace repeat hash");
    }
    private static void CnnPacketTraceRuntimeIsExact()
    {
        const string csv =
            "packet_id,release_cycle,source,destination,flits,traffic_class,layer_id,tensor_role,payload_bits\n" +
            "cnn-p0000,0,0,15,1,0,conv1,input,128\n" +
            "cnn-p0001,0,4,15,1,0,conv1,weight,128\n" +
            "cnn-p0002,1,8,3,1,0,conv1,output,128\n" +
            "cnn-p0003,2,12,1,1,0,conv2,input,128\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = Path.Combine(Path.GetTempPath(), $"hardware-sim-cnn-trace-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllBytes(path, bytes);
            var first = AspdacVbsRuntime.RunTraceCsv(path, expectedSha256, drainCycles: 100);
            var repeated = AspdacVbsRuntime.RunTraceCsv(path, expectedSha256, drainCycles: 100);

            TestAssert.True(first.Completed && repeated.Completed, "canonical trace must drain");
            TestAssert.True(!first.Timeout && first.UndrainedPackets == 0, "canonical trace must not time out");
            TestAssert.Equal(4L, first.OfferedPackets, "exact packet count");
            TestAssert.Equal(first.OfferedPackets, first.InjectedPackets, "all packets injected");
            TestAssert.Equal(first.OfferedPackets, first.DeliveredPackets, "all packets delivered");
            TestAssert.Equal(expectedSha256, first.TraceSha256, "shared CSV authentication hash");
            TestAssert.Equal(first.CanonicalDeliveryTraceHash, repeated.CanonicalDeliveryTraceHash, "exact delivery trace hash");
            TestAssert.Equal(first.RuntimeEventHash, repeated.RuntimeEventHash, "exact runtime event hash");
            TestAssert.Near(first.PacketLatencyAverage, repeated.PacketLatencyAverage, 0d, "mean packet latency");
            TestAssert.True(first.NetworkMakespanCycles > 0, "network makespan must be measured");

            AspdacVbsTraceRunResult RunSingleHopOracle(string packetId, int destination) =>
                AspdacVbsRuntime.RunTrace(
                    [new AspdacVbsTracePacket(packetId, 0, 0, destination, 1, 0, "oracle", "input", 128)],
                    new string('a', 64),
                    drainCycles: 32);

            var adjacent = RunSingleHopOracle("adjacent", 1);
            TestAssert.Equal(3L, adjacent.TotalCycles, "adjacent packet total simulated cycles");
            TestAssert.Equal(3L, adjacent.NetworkMakespanCycles, "adjacent packet inclusive makespan");
            TestAssert.Near(2d, adjacent.PacketLatencyAverage, 0d, "adjacent release-to-delivery latency");
            TestAssert.Near(2d, adjacent.PacketLatencyP95, 0d, "adjacent release-to-delivery p95");

            var multiHop = RunSingleHopOracle("multi-hop", 15);
            TestAssert.Equal(8L, multiHop.TotalCycles, "six-link packet total simulated cycles");
            TestAssert.Equal(8L, multiHop.NetworkMakespanCycles, "six-link packet inclusive makespan");
            TestAssert.Near(7d, multiHop.PacketLatencyAverage, 0d, "six-link release-to-delivery latency");
            TestAssert.Near(7d, multiHop.PacketLatencyP95, 0d, "six-link release-to-delivery p95");

            var sameSourceSameCycle = new[]
            {
                new AspdacVbsTracePacket("collision-a", 0, 0, 1, 1, 0, "oracle", "input", 128),
                new AspdacVbsTracePacket("collision-b", 0, 0, 2, 1, 0, "oracle", "weight", 128)
            };
            TestAssert.Throws<InvalidDataException>(
                () => AspdacVbsRuntime.RunTrace(sameSourceSameCycle, new string('b', 64), drainCycles: 32),
                "the canonical trace contract must reject multiple offers from one source in one cycle");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
