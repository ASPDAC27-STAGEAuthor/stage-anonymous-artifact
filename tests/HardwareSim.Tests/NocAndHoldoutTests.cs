using HardwareSim.Core;

internal static class NocAndHoldoutTests
{
    internal static IReadOnlyList<TestCase> All { get; } =
    [
        new("P10-REV-P1-001 hold-out matrix is the frozen eight-case shape-only contract", HoldoutMatrixIsExact, "paper"),
        new("P10-REV-P1-002 hold-out lowering derives accesses without mapped-count inputs", HoldoutLoweringIsIndependent, "paper"),
        new("P10-REV-P1-003 hold-out trace hashes repeat exactly", HoldoutHashesRepeatExactly, "paper"),
        new("P10-REV-P1-004 NoC matrix covers nine nonredundant contract cases", NocMatrixIsExact, "paper"),
        new("P10-REV-P1-005 NoC single-packet current-next timing matches the oracle", NocSinglePacketTimingIsExact, "paper"),
        new("P10-REV-P1-006 NoC same-output contention advances physical-input round-robin", NocContentionIsExact, "paper"),
        new("P10-REV-P1-007 NoC blocked-release cases remain explicitly unsupported with oracle artifacts", NocBlockedReleaseIsExact, "paper"),
        new("P10-REV-P1-008 NoC atomic admission boundary remains explicit evidence", NocAtomicAdmissionBoundaryIsExact, "paper"),
        new("P10-REV-P1-009 NoC unmodeled pipeline and credit fields are frozen", NocFeatureBoundaryIsExplicit, "paper"),
        new("P10-REV-P1-010 NoC timelines and independent oracles repeat exactly", NocHashesRepeatExactly, "paper")
    ];

    private static void HoldoutMatrixIsExact()
    {
        var cases = AspdacReviewerHoldoutRuntime.Cases;
        TestAssert.Equal(8, cases.Count, "hold-out case count");
        TestAssert.True(cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() == 8, "case ids must be unique");
        TestAssert.True(cases.Select(item => item.CaseId).SequenceEqual(new[]
        {
            "holdout_gemm_096", "holdout_gemm_192", "holdout_gemm_384",
            "holdout_rect_128x256x64", "holdout_rect_256x64x192", "holdout_rect_64x384x128",
            "holdout_attn_qk_s096_d064", "holdout_attn_qk_s192_d064"
        }), "frozen case order");

        var propertyNames = typeof(AspdacReviewerHoldoutInput).GetProperties().Select(property => property.Name).OrderBy(value => value).ToArray();
        TestAssert.True(propertyNames.SequenceEqual(new[] { "CaseId", "K", "M", "N", "PrecisionBits" }),
            "core input exposes only the high-level allowlist");
        TestAssert.True(propertyNames.All(name => !name.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                                                   !name.Contains("External", StringComparison.OrdinalIgnoreCase) &&
                                                   !name.Contains("Schedule", StringComparison.OrdinalIgnoreCase) &&
                                                   !name.Contains("Release", StringComparison.OrdinalIgnoreCase)),
            "mapped-tool fields must not enter the typed input contract");
    }

    private static void HoldoutLoweringIsIndependent()
    {
        var result = AspdacReviewerHoldoutRuntime.Run(AspdacReviewerHoldoutRuntime.GetCase("holdout_gemm_096"));
        TestAssert.Equal(884_736L, result.ExpectedMacs, "96 cubed MAC count");
        TestAssert.Equal(24L, result.RowTiles, "row tiles");
        TestAssert.Equal(24L, result.ColumnTiles, "column tiles");
        TestAssert.Equal(58_752L, result.WavefrontCycles, "sequential tile wavefront");
        TestAssert.Equal(221_184L, result.Accesses.DramIfmapReads, "shape-derived activation reads");
        TestAssert.Equal(221_184L, result.Accesses.DramFilterReads, "shape-derived weight reads");
        TestAssert.Equal(9_216L, result.Accesses.DramOfmapWrites, "shape-derived output writes");
        TestAssert.Equal(884_736L, result.Accesses.SramIfmapReads, "per-MAC array activation reads");
        TestAssert.Equal(884_736L, result.Accesses.SramFilterReads, "per-MAC array weight reads");
        TestAssert.Equal(1_024L, result.PrefetchCycles, "frozen cold-start prefetch");
        TestAssert.Equal(result.WarmCycles + result.PrefetchCycles, result.TotalCycles, "cold-start attribution");
        TestAssert.Equal("Trend", result.TimingEvidenceLabel, "timing comparison boundary");
    }

    private static void HoldoutHashesRepeatExactly()
    {
        foreach (var item in AspdacReviewerHoldoutRuntime.Cases)
        {
            var first = AspdacReviewerHoldoutRuntime.Run(item);
            var second = AspdacReviewerHoldoutRuntime.Run(item);
            TestAssert.Equal(first.InputSha256, second.InputSha256, $"{item.CaseId} input hash");
            TestAssert.Equal(first.CanonicalTraceSha256, second.CanonicalTraceSha256, $"{item.CaseId} trace hash");
            TestAssert.Equal(first.TotalCycles, second.TotalCycles, $"{item.CaseId} total cycles");
        }
    }

    private static void NocMatrixIsExact()
    {
        var cases = AspdacReviewerNocContractRuntime.Cases;
        TestAssert.Equal(9, cases.Count, "NoC case count");
        TestAssert.True(cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() == 9, "NoC ids must be unique");
        TestAssert.True(cases.Select(item => item.PacketBits).Distinct().OrderBy(value => value).SequenceEqual(new[] { 128, 256, 512, 1024 }),
            "packet sizes");
        TestAssert.True(cases.Select(item => item.VirtualChannels).Distinct().OrderBy(value => value).SequenceEqual(new[] { 1, 2, 4 }),
            "VC counts");
        TestAssert.True(cases.Select(item => item.VcDepthFlits).Distinct().OrderBy(value => value).SequenceEqual(new[] { 1, 4, 16 }),
            "VC depths");
        TestAssert.Equal(2, cases.Count(item => item.InputCount == 2), "same-output contention cases");
        TestAssert.Equal(2, cases.Count(item => item.DownstreamReleaseCycle >= 0), "blocked-release cases");
    }

    private static void NocSinglePacketTimingIsExact()
    {
        var result = AspdacReviewerNocContractRuntime.Run(AspdacReviewerNocContractRuntime.GetCase("noc_n01_single_128"));
        var moment = result.ObservedMoments.Single();
        TestAssert.True(result.Completed && result.DeliveredAllPackets && result.OracleMatched, "single packet must complete and match oracle");
        TestAssert.Equal(1L, moment.SourceVisibleCycle, "source visibility");
        TestAssert.Equal(2L, moment.RouterTailArrivalCycle, "router tail arrival");
        TestAssert.Equal(3L, moment.RouterVisibleCycle, "post-tail commit visibility");
        TestAssert.Equal(3L, moment.GrantCycle, "router grant");
        TestAssert.Equal(4L, moment.DeliveryCycle, "sink delivery");
        TestAssert.True(result.Timeline.Any(entry => entry.EventType == nameof(TraceEventType.BufferOccupancy) && entry.ComponentId == "router"),
            "result must project a real generic-engine router occupancy event");
        TestAssert.True(result.Timeline.All(entry => entry.EvidenceLabel == "Exact STAGE trace"),
            "actual timeline must contain only real STAGE trace projections");
    }

    private static void NocContentionIsExact()
    {
        var result = AspdacReviewerNocContractRuntime.Run(AspdacReviewerNocContractRuntime.GetCase("noc_n05_contend_128"));
        TestAssert.True(result.OracleMatched, "contention oracle");
        TestAssert.Equal(3L, result.ObservedMoments[0].GrantCycle, "first input grant");
        TestAssert.Equal(4L, result.ObservedMoments[1].GrantCycle, "second input grant after link release");
        TestAssert.Equal(5L, result.ObservedMoments[1].DeliveryCycle, "second input delivery");

        var grants = result.Timeline.Where(entry => entry.EventType == nameof(TraceEventType.Arbitration)).OrderBy(entry => entry.Cycle).ToArray();
        TestAssert.True(grants.Select(entry => entry.InputPort).SequenceEqual(new[] { "north", "south" }), "physical-input grant order");
    }

    private static void NocBlockedReleaseIsExact()
    {
        var shortPacket = AspdacReviewerNocContractRuntime.Run(AspdacReviewerNocContractRuntime.GetCase("noc_n07_block_release_256"));
        var longPacket = AspdacReviewerNocContractRuntime.Run(AspdacReviewerNocContractRuntime.GetCase("noc_n08_block_tail_1024"));
        TestAssert.Equal("not_supported", shortPacket.Status, "generic engine release-event status");
        TestAssert.Equal("not_supported", longPacket.Status, "generic engine tail-release status");
        TestAssert.True(shortPacket.Timeline.Count == 0 && longPacket.Timeline.Count == 0,
            "unsupported STAGE runs must never expose synthesized events as actual trace");
        TestAssert.Equal(8L, shortPacket.Oracle.Packets.Single().GrantCycle, "256-bit oracle release-cycle grant");
        TestAssert.Equal(10L, shortPacket.Oracle.Packets.Single().DeliveryCycle, "256-bit oracle tail delivery");
        TestAssert.Equal(14L, longPacket.Oracle.Packets.Single().GrantCycle, "1024-bit oracle release-cycle grant");
        TestAssert.Equal(22L, longPacket.Oracle.Packets.Single().DeliveryCycle, "1024-bit oracle tail delivery");
        TestAssert.True(shortPacket.OracleTimeline.Count > 0 && longPacket.OracleTimeline.Count > 0,
            "unsupported STAGE cases retain a separately labeled oracle artifact");
    }

    private static void NocAtomicAdmissionBoundaryIsExact()
    {
        var result = AspdacReviewerNocContractRuntime.Run(AspdacReviewerNocContractRuntime.GetCase("noc_n09_atomic_depth_boundary"));
        TestAssert.Equal("expected_boundary", result.Status, "boundary status");
        TestAssert.True(result.Completed && result.OracleMatched, "negative boundary is a successful contract result");
        TestAssert.True(!result.DeliveredAllPackets, "oversized packet cannot be delivered");
        TestAssert.Equal(-1L, result.ObservedMoments.Single().RouterVisibleCycle, "oversized packet never becomes router-visible");
        TestAssert.True(result.Timeline.Any(entry => entry.EventType == nameof(TraceEventType.Stall) &&
                                                     entry.ComponentId == "router" &&
                                                     entry.Reason.Contains("OutputBufferFull", StringComparison.OrdinalIgnoreCase) &&
                                                     entry.EvidenceLabel == "Exact STAGE trace"),
            "real generic-engine atomic admission boundary event");
    }

    private static void NocFeatureBoundaryIsExplicit()
    {
        var required = new[]
        {
            "route_compute_stage", "route_compute_latency_cycles", "vc_allocation_stage", "vc_allocator",
            "vc_allocation_latency_cycles", "switch_allocation_stage", "switch_allocator",
            "switch_allocation_latency_cycles", "crossbar_traversal_stage", "crossbar_latency_cycles",
            "credit_return_stage", "credit_return_latency_cycles", "credit_count", "credit_available",
            "credit_release_cycle", "wormhole_head_reservation", "per_flit_router_pipeline", "vc_round_robin"
        };
        foreach (var featureId in required)
        {
            var feature = AspdacReviewerNocContractRuntime.FeatureBoundary.Single(item => item.FeatureId == featureId);
            TestAssert.Equal("not_modeled", feature.Status, $"{featureId} status");
            TestAssert.Equal("none", feature.ComparisonPermission, $"{featureId} comparison permission");
        }
        TestAssert.True(AspdacReviewerNocContractRuntime.FeatureBoundary.Where(item => item.Status == "modeled")
            .All(item => item.ComparisonPermission == "contract_only"), "modeled features remain contract-only evidence");
    }

    private static void NocHashesRepeatExactly()
    {
        foreach (var item in AspdacReviewerNocContractRuntime.Cases)
        {
            var first = AspdacReviewerNocContractRuntime.Run(item);
            var second = AspdacReviewerNocContractRuntime.Run(item);
            if (first.Status == "not_supported")
            {
                TestAssert.True(!first.OracleMatched && first.Timeline.Count == 0, $"{item.CaseId} must not synthesize STAGE evidence");
            }
            else
            {
                TestAssert.True(first.OracleMatched && second.OracleMatched, $"{item.CaseId} oracle match");
                TestAssert.Equal(first.CanonicalTimelineSha256, second.CanonicalTimelineSha256, $"{item.CaseId} STAGE trace hash");
            }
            TestAssert.Equal(first.Oracle.CanonicalSha256, second.Oracle.CanonicalSha256, $"{item.CaseId} oracle hash");
            TestAssert.Equal(first.OracleTimelineSha256, second.OracleTimelineSha256, $"{item.CaseId} oracle timeline hash");
        }
    }
}
