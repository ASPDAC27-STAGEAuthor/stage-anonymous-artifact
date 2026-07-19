using HardwareSim.Core;

internal static class Phase10BooksimMatchedNocTests
{
    internal static IReadOnlyList<TestCase> All { get; } =
    [
        new("P10-NOC-BS-001 managed STAGE multi-flit pipeline matches frozen native cycles", MultiFlitPipelineMatchesNativeCycles, "paper"),
        new("P10-NOC-BS-002 managed STAGE multi-VC arbitration matches frozen native order", MultiVcArbitrationMatchesNativeOrder, "paper"),
        new("P10-NOC-BS-003 managed STAGE XY/YX choices match BookSim Knuth RNG", XyYxRandomChoicesMatchNative, "paper"),
        new("P10-NOC-BS-004 managed STAGE ROMM intermediates match BookSim Knuth RNG", RommIntermediatesMatchNative, "paper"),
        new("P10-NOC-BS-005 managed STAGE traffic manager matches stochastic fixed point", StochasticTrafficManagerMatchesNative, "paper"),
        new("P10-NOC-BS-006 managed STAGE runtime preserves workload and hardware provenance", StageProvenanceIsFirstParty, "paper")
    ];

    private static void MultiFlitPipelineMatchesNativeCycles()
    {
        var result = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 1),
            [new AspdacStageNocPacket("mf-d1-a", 0, 0, 3, 4)]);
        var delivery = result.PacketDeliveries.Single();
        TestAssert.Equal(0L, delivery.InjectionCycle, "head injection cycle");
        TestAssert.Equal(25L, delivery.ArrivalCycle, "tail arrival cycle");
        TestAssert.Equal(4, result.FlitDeliveries.Count, "flit delivery count");
        TestAssert.Equal(16, result.Events.Count(row => row.EventType == "send"), "router send count");
        TestAssert.True(result.Events.Any(row => row.EventType == "vc_grant"), "VC allocation is explicit");
        TestAssert.True(result.Events.Any(row => row.EventType == "credit_return"), "credit return is explicit");
    }

    private static void MultiVcArbitrationMatchesNativeOrder()
    {
        AspdacStageNocPacket[] Packets(string prefix) =>
        [
            new($"{prefix}-a", 0, 0, 3, 4),
            new($"{prefix}-b", 5, 1, 3, 3)
        ];
        var singleVc = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 1),
            Packets("mf-d2"));
        var dualVc = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 2),
            Packets("mv-d2"));
        var single = singleVc.PacketDeliveries.ToDictionary(row => row.PacketId, row => row.ArrivalCycle);
        var dual = dualVc.PacketDeliveries.ToDictionary(row => row.PacketId, row => row.ArrivalCycle);
        TestAssert.Equal(25L, single["mf-d2-a"], "single-VC packet A");
        TestAssert.Equal(30L, single["mf-d2-b"], "single-VC packet B");
        TestAssert.Equal(27L, dual["mv-d2-b"], "dual-VC packet B");
        TestAssert.Equal(28L, dual["mv-d2-a"], "dual-VC packet A");
        TestAssert.Equal("mv-d2-b", dualVc.PacketDeliveries[0].PacketId, "dual-VC delivery order");
    }

    private static void XyYxRandomChoicesMatchNative()
    {
        var single = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 2, AspdacStageNocRouting.XyYx, 40, true),
            [new AspdacStageNocPacket("route-a", 0, 0, 6, 4)]);
        var cross = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 2, AspdacStageNocRouting.XyYx, 40, true),
            [
                new AspdacStageNocPacket("route-cross-a", 0, 0, 6, 4),
                new AspdacStageNocPacket("route-cross-b", 0, 9, 3, 4)
            ]);
        TestAssert.Equal("xy", single.GeneratedRouteModes["route-a"], "single route choice");
        TestAssert.Equal("yx", cross.GeneratedRouteModes["route-cross-a"], "cross route A choice");
        TestAssert.Equal("yx", cross.GeneratedRouteModes["route-cross-b"], "cross route B choice");
        TestAssert.Equal(25L, single.PacketDeliveries.Single().ArrivalCycle, "single route arrival");
        TestAssert.Equal(30L, cross.PacketDeliveries.Single(row => row.PacketId == "route-cross-b").ArrivalCycle, "cross route B arrival");
    }

    private static void RommIntermediatesMatchNative()
    {
        var single = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 2, AspdacStageNocRouting.Romm, 40),
            [new AspdacStageNocPacket("romm-a", 0, 0, 15, 4)]);
        var cross = AspdacStageNocRuntime.Run(
            new AspdacStageNocOptions(16, 2, AspdacStageNocRouting.Romm, 40),
            [
                new AspdacStageNocPacket("romm-cross-a", 0, 0, 15, 4),
                new AspdacStageNocPacket("romm-cross-b", 0, 12, 3, 4)
            ]);
        TestAssert.Equal(11, single.RommIntermediates["romm-a"], "single ROMM intermediate");
        TestAssert.Equal(2, cross.RommIntermediates["romm-cross-a"], "cross ROMM A intermediate");
        TestAssert.Equal(14, cross.RommIntermediates["romm-cross-b"], "cross ROMM B intermediate");
        TestAssert.True(single.PacketDeliveries.All(row => row.ArrivalCycle == 40), "single ROMM arrival");
        TestAssert.True(cross.PacketDeliveries.All(row => row.ArrivalCycle == 40), "cross ROMM arrivals");
    }

    private static void StochasticTrafficManagerMatchesNative()
    {
        var first = AspdacStageNocRuntime.RunBooksimUniformTraffic(40, 0.02);
        var repeated = AspdacStageNocRuntime.RunBooksimUniformTraffic(40, 0.02);
        TestAssert.Equal(218L, first.GenerationHorizon, "drain generation horizon");
        TestAssert.Equal(69, first.Run.PacketDeliveries.Count, "generated packet count");
        TestAssert.Equal(69, first.Run.Events.Count(row => row.EventType == "injection"), "flit injection count");
        TestAssert.Equal(255, first.Run.Events.Count(row => row.EventType == "send"), "router send count");
        TestAssert.Equal(69, first.Run.Events.Count(row => row.EventType == "ejection"), "flit ejection count");
        TestAssert.Equal(first.Run.CanonicalEventHash, repeated.Run.CanonicalEventHash, "stochastic repeat hash");
    }

    private static void StageProvenanceIsFirstParty()
    {
        var packet = new AspdacStageNocPacket(
            "stage-native",
            0,
            0,
            15,
            4,
            LayerId: "conv2",
            TensorRole: "activation",
            MappingId: "mapping-mnist-r3",
            SourceComponentId: "pe-array-03",
            DestinationComponentId: "reduce-00",
            RouteResourceId: "WG1:east:r0-r1");
        var first = AspdacStageNocRuntime.Run(new AspdacStageNocOptions(16, 1), [packet]);
        var repeated = AspdacStageNocRuntime.Run(new AspdacStageNocOptions(16, 1), [packet]);
        var delivery = first.PacketDeliveries.Single();
        TestAssert.Equal("stage_managed_flit_vc_runtime", first.EngineIdentity, "first-party engine identity");
        TestAssert.Equal(0, first.ExternalBackendInvocations, "external backend count");
        TestAssert.Equal("conv2", delivery.LayerId, "layer provenance");
        TestAssert.Equal("activation", delivery.TensorRole, "tensor-role provenance");
        TestAssert.Equal("mapping-mnist-r3", delivery.MappingId, "mapping provenance");
        TestAssert.Equal("pe-array-03", delivery.SourceComponentId, "source component provenance");
        TestAssert.Equal("reduce-00", delivery.DestinationComponentId, "destination component provenance");
        TestAssert.Equal("WG1:east:r0-r1", delivery.RouteResourceId, "physical route-resource provenance");
        TestAssert.Equal(first.StageProvenanceHash, repeated.StageProvenanceHash, "provenance hash");
        TestAssert.Equal(first.CanonicalEventHash, repeated.CanonicalEventHash, "event hash");
    }
}
