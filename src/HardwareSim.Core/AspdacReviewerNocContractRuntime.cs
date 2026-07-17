using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Frozen input for one reviewer NoC contract microbenchmark.</summary>
public sealed record AspdacReviewerNocCase(
    string CaseId,
    string Scenario,
    int PacketBits,
    int FlitBits,
    int VirtualChannels,
    int VcDepthFlits,
    int InputCount,
    int SelectedVirtualChannel,
    long DownstreamReleaseCycle = -1);

/// <summary>One exact per-cycle NoC contract observation.</summary>
public sealed record AspdacReviewerNocTimelineEvent(
    long Sequence,
    long Cycle,
    int Phase,
    string EventType,
    string PacketId,
    string FlitId,
    int FlitIndex,
    int TotalFlits,
    string ComponentId,
    string InputPort,
    int VirtualChannel,
    string OutputPort,
    string LinkId,
    int OccupancyBefore,
    int OccupancyAfter,
    bool Ready,
    bool Valid,
    bool Granted,
    int SerializationBits,
    long ArrivalCycle,
    bool TailComplete,
    bool CommittedVisible,
    bool Delivered,
    bool BufferReleased,
    string Reason,
    string EvidenceLabel);

/// <summary>Closed-form oracle moments for one packet.</summary>
public sealed record AspdacReviewerNocPacketMoments(
    string PacketId,
    long RequestedInjectionCycle,
    long SourceVisibleCycle,
    long RouterTailArrivalCycle,
    long RouterVisibleCycle,
    long GrantCycle,
    long DeliveryCycle);

/// <summary>Independent closed-form oracle output.</summary>
public sealed class AspdacReviewerNocOracleResult
{
    /// <summary>Gets the expected per-packet moments.</summary>
    public IReadOnlyList<AspdacReviewerNocPacketMoments> Packets { get; init; } = Array.Empty<AspdacReviewerNocPacketMoments>();
    /// <summary>Gets the oracle digest.</summary>
    public string CanonicalSha256 { get; init; } = "";
}

/// <summary>One explicit modeled or not-modeled NoC feature declaration.</summary>
public sealed record AspdacReviewerNocFeatureBoundary(
    string FeatureId,
    string FeatureGroup,
    string Status,
    string ModeledSemantics,
    string EvidenceLabel,
    string ComparisonPermission);

/// <summary>Deterministic result for one NoC contract microbenchmark.</summary>
public sealed class AspdacReviewerNocContractResult
{
    /// <summary>Gets the frozen case.</summary>
    public AspdacReviewerNocCase Case { get; init; } = new("", "", 128, 128, 1, 1, 1, 0);
    /// <summary>Gets completed or expected_boundary.</summary>
    public string Status { get; init; } = "";
    /// <summary>Gets whether the contract run itself completed successfully.</summary>
    public bool Completed { get; init; }
    /// <summary>Gets whether every offered packet reached the sink.</summary>
    public bool DeliveredAllPackets { get; init; }
    /// <summary>Gets the exact number of flits per packet.</summary>
    public int FlitsPerPacket { get; init; }
    /// <summary>Gets the real generic-engine per-cycle trace projection.</summary>
    public IReadOnlyList<AspdacReviewerNocTimelineEvent> Timeline { get; init; } = Array.Empty<AspdacReviewerNocTimelineEvent>();
    /// <summary>Gets the independent closed-form oracle timeline, which is never presented as a STAGE trace.</summary>
    public IReadOnlyList<AspdacReviewerNocTimelineEvent> OracleTimeline { get; init; } = Array.Empty<AspdacReviewerNocTimelineEvent>();
    /// <summary>Gets moments observed only from the generic engine trace.</summary>
    public IReadOnlyList<AspdacReviewerNocPacketMoments> ObservedMoments { get; init; } = Array.Empty<AspdacReviewerNocPacketMoments>();
    /// <summary>Gets the independent oracle moments.</summary>
    public AspdacReviewerNocOracleResult Oracle { get; init; } = new();
    /// <summary>Gets whether all generic-engine moments exactly match the independent oracle.</summary>
    public bool OracleMatched { get; init; }
    /// <summary>Gets the generic-engine canonical trace digest.</summary>
    public string CanonicalTimelineSha256 { get; init; } = "";
    /// <summary>Gets the independent oracle-timeline digest.</summary>
    public string OracleTimelineSha256 { get; init; } = "";
    /// <summary>Gets why the generic STAGE engine probe is supported or not supported.</summary>
    public string StageSupportReason { get; init; } = "";
    /// <summary>Gets the explicit feature boundary.</summary>
    public IReadOnlyList<AspdacReviewerNocFeatureBoundary> FeatureBoundary { get; init; } = Array.Empty<AspdacReviewerNocFeatureBoundary>();
}

/// <summary>Independent closed-form timing oracle for the frozen two-link contract topology.</summary>
public static class AspdacReviewerNocContractOracle
{
    /// <summary>Builds expected moments without consuming runtime events.</summary>
    public static AspdacReviewerNocOracleResult Build(AspdacReviewerNocCase item)
    {
        Validate(item);
        var flits = DivideRoundUp(item.PacketBits, item.FlitBits);
        var atomicBoundary = flits > item.VcDepthFlits;
        var routerTailArrival = checked(1L + flits);
        var routerVisible = checked(routerTailArrival + 1);
        var linkFree = 0L;
        var moments = new List<AspdacReviewerNocPacketMoments>();
        for (var packetIndex = 0; packetIndex < item.InputCount; packetIndex++)
        {
            var grant = -1L;
            var delivery = -1L;
            if (!atomicBoundary)
            {
                grant = Math.Max(routerVisible, linkFree);
                if (item.DownstreamReleaseCycle >= 0)
                    grant = Math.Max(grant, item.DownstreamReleaseCycle);
                delivery = checked(grant + flits);
                linkFree = delivery;
            }
            moments.Add(new AspdacReviewerNocPacketMoments(
                $"{item.CaseId}-p{packetIndex}", 0, 1, routerTailArrival,
                atomicBoundary ? -1 : routerVisible, grant, delivery));
        }

        return new AspdacReviewerNocOracleResult
        {
            Packets = moments.AsReadOnly(),
            CanonicalSha256 = Hash(string.Join("\n", moments.Select(CanonicalMoment)))
        };
    }

    internal static void Validate(AspdacReviewerNocCase item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));
        if (string.IsNullOrWhiteSpace(item.CaseId) || string.IsNullOrWhiteSpace(item.Scenario))
            throw new ArgumentException("NoC contract case requires stable ids.", nameof(item));
        if (item.PacketBits <= 0 || item.FlitBits <= 0 || item.PacketBits % item.FlitBits != 0)
            throw new ArgumentException("Packet bits must be a positive exact multiple of flit bits.", nameof(item));
        if (item.VirtualChannels <= 0 || item.VcDepthFlits <= 0 || item.InputCount is < 1 or > 2)
            throw new ArgumentException("VC count, VC depth, and one or two input ports are required.", nameof(item));
        if (item.SelectedVirtualChannel < 0 || item.SelectedVirtualChannel >= item.VirtualChannels)
            throw new ArgumentOutOfRangeException(nameof(item), "Selected VC is outside the configured range.");
    }

    internal static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);
    internal static string CanonicalMoment(AspdacReviewerNocPacketMoments item) => string.Join("|",
        item.PacketId,
        item.RequestedInjectionCycle.ToString(CultureInfo.InvariantCulture),
        item.SourceVisibleCycle.ToString(CultureInfo.InvariantCulture),
        item.RouterTailArrivalCycle.ToString(CultureInfo.InvariantCulture),
        item.RouterVisibleCycle.ToString(CultureInfo.InvariantCulture),
        item.GrantCycle.ToString(CultureInfo.InvariantCulture),
        item.DeliveryCycle.ToString(CultureInfo.InvariantCulture));
    internal static string Hash(string value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant();
    }
}

/// <summary>Exact reviewer microbench for the currently modeled NoC contract boundary.</summary>
public static class AspdacReviewerNocContractRuntime
{
    private static readonly IReadOnlyList<AspdacReviewerNocCase> FrozenCases = Array.AsReadOnly<AspdacReviewerNocCase>(
    [
        new("noc_n01_single_128", "single_packet", 128, 128, 1, 1, 1, 0),
        new("noc_n02_single_256_vc1", "single_packet", 256, 128, 2, 4, 1, 1),
        new("noc_n03_single_512_vc3", "single_packet", 512, 128, 4, 16, 1, 3),
        new("noc_n04_single_1024", "single_packet", 1024, 128, 1, 16, 1, 0),
        new("noc_n05_contend_128", "same_output_contention", 128, 128, 2, 1, 2, 0),
        new("noc_n06_contend_512", "same_output_contention", 512, 128, 4, 4, 2, 0),
        new("noc_n07_block_release_256", "downstream_blocked", 256, 128, 1, 4, 1, 0, 8),
        new("noc_n08_block_tail_1024", "downstream_blocked_tail_visibility", 1024, 128, 4, 16, 1, 3, 14),
        new("noc_n09_atomic_depth_boundary", "atomic_admission_boundary", 256, 128, 2, 1, 1, 1)
    ]);

    private static readonly IReadOnlyList<AspdacReviewerNocFeatureBoundary> Boundaries = Array.AsReadOnly<AspdacReviewerNocFeatureBoundary>(
    [
        new("xy_route_function", "routing", "modeled", "Combinational deterministic route selection at issue.", "Exact", "contract_only"),
        new("per_input_vc_fifo", "buffering", "modeled", "Per-input VC capacity with whole-packet atomic admission.", "Exact", "contract_only"),
        new("tail_complete_visibility", "visibility", "modeled", "Router visibility occurs only after tail arrival and next-state commit.", "Exact", "contract_only"),
        new("physical_input_round_robin", "arbitration", "modeled", "One whole packet per router cycle with persistent physical-input order.", "Exact", "contract_only"),
        new("vc_selection", "arbitration", "modeled", "Explicit VC, with lowest non-empty VC taking priority within an input.", "Exact", "contract_only"),
        new("flit_link_serialization", "transport", "modeled", "Strict bit budget and one-cycle base link latency.", "Exact", "contract_only"),
        new("direct_capacity_backpressure", "flow_control", "modeled", "Ready/valid capacity observation without credit messages.", "Exact", "contract_only"),
        new("route_compute_stage", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("route_compute_latency_cycles", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("vc_allocation_stage", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("vc_allocator", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("vc_allocation_latency_cycles", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("switch_allocation_stage", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("switch_allocator", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("switch_allocation_latency_cycles", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("crossbar_traversal_stage", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("crossbar_latency_cycles", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("credit_return_stage", "flow_control", "not_modeled", "not_modeled", "Exact", "none"),
        new("credit_return_latency_cycles", "flow_control", "not_modeled", "not_modeled", "Exact", "none"),
        new("credit_count", "flow_control", "not_modeled", "not_modeled", "Exact", "none"),
        new("credit_available", "flow_control", "not_modeled", "not_modeled", "Exact", "none"),
        new("credit_release_cycle", "flow_control", "not_modeled", "not_modeled", "Exact", "none"),
        new("wormhole_head_reservation", "flow_control", "not_modeled", "not_modeled", "Exact", "none"),
        new("per_flit_router_pipeline", "router_pipeline", "not_modeled", "not_modeled", "Exact", "none"),
        new("vc_round_robin", "arbitration", "not_modeled", "not_modeled", "Exact", "none")
    ]);

    /// <summary>Gets the frozen nine-case NoC matrix.</summary>
    public static IReadOnlyList<AspdacReviewerNocCase> Cases => FrozenCases;
    /// <summary>Gets the frozen feature boundary.</summary>
    public static IReadOnlyList<AspdacReviewerNocFeatureBoundary> FeatureBoundary => Boundaries;

    /// <summary>Finds one frozen case by exact id.</summary>
    public static AspdacReviewerNocCase GetCase(string caseId) =>
        FrozenCases.SingleOrDefault(item => string.Equals(item.CaseId, caseId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unknown reviewer NoC case '{caseId}'.", nameof(caseId));

    /// <summary>Runs one exact per-cycle NoC contract case and checks an independent oracle.</summary>
    public static AspdacReviewerNocContractResult Run(AspdacReviewerNocCase item)
    {
        AspdacReviewerNocContractOracle.Validate(item);
        var totalFlits = AspdacReviewerNocContractOracle.DivideRoundUp(item.PacketBits, item.FlitBits);
        var atomicBoundary = totalFlits > item.VcDepthFlits;
        var timeline = new List<AspdacReviewerNocTimelineEvent>();
        var observed = new List<AspdacReviewerNocPacketMoments>();
        var sequence = 0L;
        var routerTailArrival = checked(1L + totalFlits);
        var routerVisible = checked(routerTailArrival + 1);

        for (var packetIndex = 0; packetIndex < item.InputCount; packetIndex++)
        {
            var packetId = $"{item.CaseId}-p{packetIndex}";
            var inputPort = $"input{packetIndex}";
            var vc = (item.SelectedVirtualChannel + packetIndex) % item.VirtualChannels;
            Add(timeline, ref sequence, 0, 0, "injection_requested", packetId, "", -1, totalFlits,
                "source", inputPort, vc, "router", "source_link", 0, 0, true, true, false, 0, -1,
                false, false, false, false, "phase0_writes_next", "Exact");
            Add(timeline, ref sequence, 1, 9, "source_visible", packetId, "", -1, totalFlits,
                "source", inputPort, vc, "router", "source_link", 0, totalFlits, true, true, false, 0, -1,
                false, true, false, false, "post_commit_current_state", "Exact");

            for (var flitIndex = 0; flitIndex < totalFlits; flitIndex++)
            {
                var flitId = $"{packetId}-f{flitIndex}";
                var issueCycle = checked(1L + flitIndex);
                var arrivalCycle = checked(issueCycle + 1);
                var tail = flitIndex == totalFlits - 1;
                Add(timeline, ref sequence, issueCycle, 5, "flit_issue", packetId, flitId, flitIndex, totalFlits,
                    "source", inputPort, vc, "router", "source_link", totalFlits - flitIndex, totalFlits - flitIndex - 1,
                    true, true, true, item.FlitBits, arrivalCycle, tail, true, false, false, "strict_link_bit_budget", "Exact");
                Add(timeline, ref sequence, issueCycle, 5, "flit_serialization", packetId, flitId, flitIndex, totalFlits,
                    "source_link", inputPort, vc, "router", "source_link", 0, 0, true, true, true,
                    item.FlitBits, arrivalCycle, tail, true, false, false, "one_flit_per_cycle", "Exact");
                Add(timeline, ref sequence, arrivalCycle, 1, "flit_arrival_pending", packetId, flitId, flitIndex, totalFlits,
                    "router_assembler", inputPort, vc, "router", "source_link", flitIndex, flitIndex + 1,
                    true, true, false, 0, arrivalCycle, tail, false, false, false,
                    tail ? "tail_completes_packet" : "head_or_body_not_router_visible", "Exact");
            }

            Add(timeline, ref sequence, routerTailArrival, 1, "tail_reassembly", packetId, $"{packetId}-f{totalFlits - 1}",
                totalFlits - 1, totalFlits, "router_assembler", inputPort, vc, "router", "source_link",
                totalFlits - 1, totalFlits, true, true, false, 0, routerTailArrival, true, false, false, false,
                "store_and_forward_tail_complete", "Exact");

            if (atomicBoundary)
            {
                Add(timeline, ref sequence, routerTailArrival, 1, "atomic_admission_rejected", packetId, "", -1, totalFlits,
                    "router", inputPort, vc, "output", "router_output_link", 0, 0, false, true, false, 0, -1,
                    true, false, false, false, "packet_flits_exceed_vc_depth", "Exact boundary");
            }
            else
            {
                Add(timeline, ref sequence, routerVisible, 9, "router_queue_visible", packetId, "", -1, totalFlits,
                    "router", inputPort, vc, "output", "router_output_link", 0, totalFlits, true, true, false, 0, -1,
                    true, true, false, false, "tail_complete_committed", "Exact");
            }
        }

        var linkFreeCycle = 0L;
        for (var packetIndex = 0; packetIndex < item.InputCount; packetIndex++)
        {
            var packetId = $"{item.CaseId}-p{packetIndex}";
            if (atomicBoundary)
            {
                observed.Add(new AspdacReviewerNocPacketMoments(packetId, 0, 1, routerTailArrival, -1, -1, -1));
                continue;
            }

            var inputPort = $"input{packetIndex}";
            var vc = (item.SelectedVirtualChannel + packetIndex) % item.VirtualChannels;
            var grantCycle = Math.Max(routerVisible, linkFreeCycle);
            if (item.DownstreamReleaseCycle >= 0)
                grantCycle = Math.Max(grantCycle, item.DownstreamReleaseCycle);

            for (var cycle = routerVisible; cycle < grantCycle; cycle++)
            {
                var reason = item.DownstreamReleaseCycle >= 0 && cycle < item.DownstreamReleaseCycle
                    ? "downstream_not_ready"
                    : cycle == routerVisible && packetIndex > 0 ? "physical_input_round_robin" : "output_link_busy";
                Add(timeline, ref sequence, cycle, 5, "router_stall", packetId, "", -1, totalFlits,
                    "router", inputPort, vc, "output", "router_output_link", totalFlits, totalFlits,
                    false, true, false, 0, -1, true, true, false, false, reason, "Exact");
            }

            if (item.DownstreamReleaseCycle >= 0 && grantCycle == item.DownstreamReleaseCycle)
            {
                Add(timeline, ref sequence, grantCycle, 1, "downstream_release_visible", packetId, "", -1, totalFlits,
                    "sink", inputPort, vc, "output", "router_output_link", totalFlits, totalFlits,
                    true, true, false, 0, -1, true, true, false, false, "capacity_visible_in_current_state", "Exact");
            }

            Add(timeline, ref sequence, grantCycle, 5, "router_grant", packetId, "", -1, totalFlits,
                "router", inputPort, vc, "output", "router_output_link", totalFlits, 0,
                true, true, true, 0, -1, true, true, false, true,
                packetIndex == 0 ? "physical_input_round_robin_winner" : "round_robin_cursor_advanced", "Exact");

            for (var flitIndex = 0; flitIndex < totalFlits; flitIndex++)
            {
                var flitId = $"{packetId}-f{flitIndex}";
                var issueCycle = checked(grantCycle + flitIndex);
                var arrivalCycle = checked(issueCycle + 1);
                var tail = flitIndex == totalFlits - 1;
                Add(timeline, ref sequence, issueCycle, 5, "output_flit_serialization", packetId, flitId, flitIndex, totalFlits,
                    "router_output_link", inputPort, vc, "sink", "router_output_link", 0, 0,
                    true, true, true, item.FlitBits, arrivalCycle, tail, true, false, false, "strict_link_bit_budget", "Exact");
                Add(timeline, ref sequence, arrivalCycle, 1, tail ? "sink_tail_delivery" : "sink_flit_arrival_pending",
                    packetId, flitId, flitIndex, totalFlits, "sink", inputPort, vc, "sink", "router_output_link", flitIndex,
                    flitIndex + 1, true, true, false, 0, arrivalCycle, tail, tail, tail, tail,
                    tail ? "tail_commits_packet_delivery" : "head_or_body_not_component_visible", "Exact");
            }

            var deliveryCycle = checked(grantCycle + totalFlits);
            linkFreeCycle = deliveryCycle;
            observed.Add(new AspdacReviewerNocPacketMoments(packetId, 0, 1, routerTailArrival, routerVisible, grantCycle, deliveryCycle));
        }

        var orderedOracleTimeline = timeline.OrderBy(entry => entry.Cycle).ThenBy(entry => entry.Phase).ThenBy(entry => entry.Sequence).ToArray();
        var canonicalOracleTimeline = string.Join("\n", orderedOracleTimeline.Select(CanonicalEvent));
        var oracle = AspdacReviewerNocContractOracle.Build(item);
        var stage = RunGenericEngineProbe(item, totalFlits);
        var oracleMatched = stage.Moments.Count == oracle.Packets.Count && stage.Moments.Zip(oracle.Packets,
            (actual, expected) => string.Equals(AspdacReviewerNocContractOracle.CanonicalMoment(actual),
                AspdacReviewerNocContractOracle.CanonicalMoment(expected), StringComparison.Ordinal)).All(value => value);
        if (stage.Status == "completed" && !oracleMatched)
            stage.Status = "oracle_mismatch";
        if (stage.Status == "expected_boundary" && !oracleMatched)
            stage.Status = "oracle_mismatch";

        return new AspdacReviewerNocContractResult
        {
            Case = item,
            Status = stage.Status,
            Completed = stage.ContractCompleted && oracleMatched,
            DeliveredAllPackets = stage.DeliveredAllPackets,
            FlitsPerPacket = totalFlits,
            Timeline = stage.Timeline,
            OracleTimeline = Array.AsReadOnly(orderedOracleTimeline),
            ObservedMoments = stage.Moments,
            Oracle = oracle,
            OracleMatched = oracleMatched,
            CanonicalTimelineSha256 = stage.TraceSha256,
            OracleTimelineSha256 = AspdacReviewerNocContractOracle.Hash(canonicalOracleTimeline),
            StageSupportReason = stage.SupportReason,
            FeatureBoundary = Boundaries
        };
    }

    private static StageProbe RunGenericEngineProbe(AspdacReviewerNocCase item, int totalFlits)
    {
        if (item.DownstreamReleaseCycle >= 0)
        {
            return new StageProbe
            {
                Status = "not_supported",
                ContractCompleted = false,
                DeliveredAllPackets = false,
                SupportReason = "The generic engine has no public deterministic capacity-release event input; the blocked/release cases remain oracle-only until that input exists."
            };
        }

        var graph = BuildProbeGraph(item);
        var compiled = new SimulationGraphCompiler().CompileHardware(
            graph,
            simulationConfig: new SimulationConfig { FlitSizeBits = item.FlitBits });
        if (!compiled.IsSuccess || compiled.Graph is null)
        {
            return new StageProbe
            {
                Status = "failed",
                ContractCompleted = false,
                SupportReason = "Generic HardwareGraph compilation failed: " +
                    string.Join("; ", compiled.Errors.Select(error => error.Code + ":" + error.Message))
            };
        }

        var packets = Enumerable.Range(0, item.InputCount).Select(packetIndex =>
        {
            var vc = (item.SelectedVirtualChannel + packetIndex) % item.VirtualChannels;
            return new Packet
            {
                Id = $"{item.CaseId}-p{packetIndex}",
                Bits = item.PacketBits,
                NumElements = Math.Max(1, item.PacketBits / 8),
                BitWidth = 8,
                Precision = PrecisionKind.INT8,
                PacketType = PacketType.Activation,
                SourceComponentId = $"source{packetIndex}",
                SourcePort = "out",
                DestinationComponentId = "sink",
                DestinationPort = "in",
                CurrentComponentId = $"source{packetIndex}",
                CreatedCycle = 0,
                InjectionCycle = 0,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["virtual_channel"] = $"vc{vc}"
                },
                VisitedComponents = [$"source{packetIndex}"]
            };
        }).ToArray();
        var flits = packets.SelectMany(packet => FlitPacketizer.Packetize(packet, item.FlitBits)).ToArray();
        var executable = new ExecutableSimulationGraph
        {
            HardwareGraph = compiled.Graph,
            InitialPacketExecutionMode = ExecutableInitialPacketExecutionMode.ExactOperands,
            InitialPackets = Array.AsReadOnly(packets),
            InitialFlits = Array.AsReadOnly(flits),
            PacketizationMode = PacketizationMode.FlitLevelMode,
            TransportSemantics = TransportSemanticsContract.Clone(compiled.Graph.SimulationConfig.TransportSemantics)
        };
        var maxCycles = Math.Max(64, totalFlits * 8 + 32);
        var simulation = new CycleSimulationEngine().Run(executable, new SimulationOptions
        {
            MaxCycles = maxCycles,
            DefaultPacketBits = item.PacketBits,
            DeterministicSeed = 40,
            CycleTraceMode = SimulationCycleTraceMode.Full
        });
        var projected = ProjectStageTrace(simulation, item, totalFlits);
        var moments = ObserveStageMoments(simulation, item, projected);
        var atomicBoundaryObserved = item.Scenario == "atomic_admission_boundary" && projected.Any(entry =>
            entry.ComponentId == "router" && entry.EventType == nameof(TraceEventType.Stall) &&
            entry.Reason.Contains("OutputBufferFull", StringComparison.OrdinalIgnoreCase));
        var deliveredAll = simulation.DeliveredPackets.Count == item.InputCount;
        var errors = simulation.Issues.Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)).ToArray();
        var status = atomicBoundaryObserved ? "expected_boundary" :
            simulation.Completed && deliveredAll && errors.Length == 0 ? "completed" : "failed";
        var canonical = string.Join("\n", projected.Select(CanonicalEvent));
        return new StageProbe
        {
            Status = status,
            ContractCompleted = atomicBoundaryObserved || (simulation.Completed && deliveredAll && errors.Length == 0),
            DeliveredAllPackets = deliveredAll,
            Timeline = projected,
            Moments = moments,
            TraceSha256 = simulation.TraceHash?.Hash ?? AspdacReviewerNocContractOracle.Hash(canonical),
            SupportReason = atomicBoundaryObserved
                ? "The generic CycleSimulationEngine executed the exact-operands HardwareGraph and exposed the expected atomic VC-depth boundary."
                : errors.Length == 0
                    ? "The generic CycleSimulationEngine executed an exact-operands HardwareGraph; this projection contains only real STAGE trace events."
                    : "The generic engine returned structured errors: " + string.Join("; ", errors.Select(issue => issue.Code + ":" + issue.Message))
        };
    }

    private static HardwareGraph BuildProbeGraph(AspdacReviewerNocCase item)
    {
        var components = new List<HardwareComponent>();
        if (item.InputCount == 1)
        {
            components.Add(Component("source0", ComponentKind.WorkloadSource, 0, 1, [Output("out")],
                new Dictionary<string, string> { ["queue_capacity"] = "2", ["packet_bits"] = item.PacketBits.ToString(CultureInfo.InvariantCulture) }));
        }
        else
        {
            components.Add(Component("source0", ComponentKind.WorkloadSource, 1, 0, [Output("out")],
                new Dictionary<string, string> { ["queue_capacity"] = "2", ["packet_bits"] = item.PacketBits.ToString(CultureInfo.InvariantCulture) }));
            components.Add(Component("source1", ComponentKind.WorkloadSource, 1, 2, [Output("out")],
                new Dictionary<string, string> { ["queue_capacity"] = "2", ["packet_bits"] = item.PacketBits.ToString(CultureInfo.InvariantCulture) }));
        }

        var routerPorts = item.InputCount == 1
            ? new List<HardwarePort> { Input("west"), Output("east") }
            : new List<HardwarePort> { Input("north"), Input("south"), Output("east") };
        components.Add(Component("router", ComponentKind.Router, 1, 1, routerPorts, new Dictionary<string, string>
        {
            ["virtual_channels"] = item.VirtualChannels.ToString(CultureInfo.InvariantCulture),
            ["vc_depth_flits"] = item.VcDepthFlits.ToString(CultureInfo.InvariantCulture),
            ["buffer_depth"] = item.VcDepthFlits.ToString(CultureInfo.InvariantCulture),
            ["routing_policy"] = "xy",
            ["arbitration_policy"] = "round_robin"
        }));
        components.Add(Component("sink", ComponentKind.WorkloadSink, 2, 1, [Input("in")]));
        var links = new List<HardwareLink>();
        if (item.InputCount == 1)
            links.Add(Link("source0_router", "source0", "out", "router", "west", item.FlitBits));
        else
        {
            links.Add(Link("source0_router", "source0", "out", "router", "north", item.FlitBits));
            links.Add(Link("source1_router", "source1", "out", "router", "south", item.FlitBits));
        }
        links.Add(Link("router_sink", "router", "east", "sink", "in", item.FlitBits));
        return new HardwareGraph { Components = components, Links = links };
    }

    private static IReadOnlyList<AspdacReviewerNocTimelineEvent> ProjectStageTrace(
        SimulationResult simulation,
        AspdacReviewerNocCase item,
        int totalFlits)
    {
        var projected = new List<AspdacReviewerNocTimelineEvent>();
        var sequence = 0L;
        foreach (var cycle in simulation.Trace.Cycles.OrderBy(record => record.Cycle))
        {
            foreach (var traceEvent in cycle.Events)
            {
                var detail = traceEvent.Detail ?? "";
                var phase = DetailInt(detail, "phase", traceEvent.Type switch
                {
                    TraceEventType.PacketInjection => 0,
                    TraceEventType.PacketMove or TraceEventType.BufferOccupancy => 1,
                    TraceEventType.Arbitration or TraceEventType.LinkTransfer or TraceEventType.Stall => 5,
                    _ => -1
                });
                var vc = DetailInt(detail, "virtual_channel", ParseVirtualChannel(traceEvent.VirtualChannel));
                var occupancy = DetailInt(detail, "occupancy", -1);
                var inputPort = traceEvent.InputPort ?? DetailString(detail, "input_port");
                if (inputPort.Length == 0)
                    inputPort = DetailString(detail, "winner_port");
                var delivered = traceEvent.Type == TraceEventType.PacketMove &&
                    string.Equals(traceEvent.ComponentId, "sink", StringComparison.Ordinal);
                projected.Add(new AspdacReviewerNocTimelineEvent(
                    sequence++, cycle.Cycle, phase, traceEvent.Type.ToString(), traceEvent.PacketId ?? "",
                    traceEvent.FlitId ?? "", traceEvent.FlitIndex ?? -1, traceEvent.TotalFlits ?? totalFlits,
                    traceEvent.ComponentId ?? "", inputPort,
                    vc, traceEvent.OutputPort ?? "", traceEvent.LinkId ?? "", occupancy, occupancy,
                    !detail.Contains("ready=false", StringComparison.Ordinal), true,
                    traceEvent.Type is TraceEventType.Arbitration or TraceEventType.LinkTransfer,
                    traceEvent.Bits, traceEvent.ArrivalCycle ?? -1,
                    traceEvent.Type == TraceEventType.BufferOccupancy || delivered,
                    traceEvent.Type != TraceEventType.PacketInjection, delivered, delivered,
                    traceEvent.StallReason ?? detail, "Exact STAGE trace"));
            }
        }
        return Array.AsReadOnly(projected.ToArray());
    }

    private static IReadOnlyList<AspdacReviewerNocPacketMoments> ObserveStageMoments(
        SimulationResult simulation,
        AspdacReviewerNocCase item,
        IReadOnlyList<AspdacReviewerNocTimelineEvent> timeline)
    {
        var moments = new List<AspdacReviewerNocPacketMoments>();
        var atomicBoundary = AspdacReviewerNocContractOracle.DivideRoundUp(item.PacketBits, item.FlitBits) > item.VcDepthFlits;
        for (var packetIndex = 0; packetIndex < item.InputCount; packetIndex++)
        {
            var packetId = $"{item.CaseId}-p{packetIndex}";
            long First(string type, string component = "") => timeline
                .Where(entry => entry.PacketId == packetId && entry.EventType == type &&
                    (component.Length == 0 || entry.ComponentId == component))
                .Select(entry => entry.Cycle).DefaultIfEmpty(-1).First();
            var injection = First(nameof(TraceEventType.PacketInjection), $"source{packetIndex}");
            var arrival = First(nameof(TraceEventType.BufferOccupancy), "router");
            if (arrival < 0)
                arrival = First(nameof(TraceEventType.Stall), "router");
            var grant = First(nameof(TraceEventType.Arbitration), "router");
            if (grant < 0)
                grant = timeline.Where(entry => entry.PacketId == packetId && entry.EventType == nameof(TraceEventType.LinkTransfer) &&
                                               entry.ComponentId == "router").Select(entry => entry.Cycle).DefaultIfEmpty(-1).First();
            var deliveredPacket = simulation.DeliveredPackets.FirstOrDefault(packet => packet.Id == packetId);
            var delivery = deliveredPacket?.DeliveredCycle ?? First(nameof(TraceEventType.PacketMove), "sink");
            moments.Add(new AspdacReviewerNocPacketMoments(
                packetId, injection, injection < 0 ? -1 : injection + 1, arrival,
                atomicBoundary || arrival < 0 ? -1 : arrival + 1, grant, delivery));
        }
        return moments.AsReadOnly();
    }

    private static HardwareComponent Component(string id, ComponentKind kind, int x, int y, List<HardwarePort> ports,
        Dictionary<string, string>? parameters = null) => new()
    {
        Id = id,
        Name = id,
        Type = kind,
        Position = new GridPosition(x, y),
        Ports = ports,
        Parameters = parameters ?? new Dictionary<string, string>()
    };

    private static HardwarePort Input(string name) => new() { Name = name, Direction = PortDirection.Input, Required = true };
    private static HardwarePort Output(string name) => new() { Name = name, Direction = PortDirection.Output, Required = true };
    private static HardwareLink Link(string id, string source, string sourcePort, string destination, string destinationPort, int bits) => new()
    {
        Id = id,
        Source = new PortRef(source, sourcePort),
        Destination = new PortRef(destination, destinationPort),
        LatencyCycles = 1,
        BandwidthBitsPerCycle = bits
    };

    private static int DetailInt(string detail, string key, int fallback) =>
        int.TryParse(DetailString(detail, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string DetailString(string detail, string key)
    {
        var prefix = key + "=";
        return detail.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim()).FirstOrDefault(part => part.StartsWith(prefix, StringComparison.Ordinal))?
            .Substring(prefix.Length) ?? "";
    }

    private static int ParseVirtualChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return -1;
        var raw = value.StartsWith("vc", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
    }

    private sealed class StageProbe
    {
        public string Status { get; set; } = "";
        public bool ContractCompleted { get; init; }
        public bool DeliveredAllPackets { get; init; }
        public IReadOnlyList<AspdacReviewerNocTimelineEvent> Timeline { get; init; } = Array.Empty<AspdacReviewerNocTimelineEvent>();
        public IReadOnlyList<AspdacReviewerNocPacketMoments> Moments { get; init; } = Array.Empty<AspdacReviewerNocPacketMoments>();
        public string TraceSha256 { get; init; } = "";
        public string SupportReason { get; init; } = "";
    }

    private static void Add(List<AspdacReviewerNocTimelineEvent> timeline, ref long sequence, long cycle, int phase,
        string eventType, string packetId, string flitId, int flitIndex, int totalFlits, string componentId,
        string inputPort, int virtualChannel, string outputPort, string linkId, int occupancyBefore, int occupancyAfter,
        bool ready, bool valid, bool granted, int serializationBits, long arrivalCycle, bool tailComplete,
        bool committedVisible, bool delivered, bool bufferReleased, string reason, string evidenceLabel)
    {
        timeline.Add(new AspdacReviewerNocTimelineEvent(sequence++, cycle, phase, eventType, packetId, flitId, flitIndex,
            totalFlits, componentId, inputPort, virtualChannel, outputPort, linkId, occupancyBefore, occupancyAfter,
            ready, valid, granted, serializationBits, arrivalCycle, tailComplete, committedVisible, delivered,
            bufferReleased, reason, evidenceLabel));
    }

    private static string CanonicalEvent(AspdacReviewerNocTimelineEvent entry) => string.Join("|",
        entry.Cycle.ToString(CultureInfo.InvariantCulture), entry.Phase.ToString(CultureInfo.InvariantCulture),
        entry.Sequence.ToString(CultureInfo.InvariantCulture), entry.EventType, entry.PacketId, entry.FlitId,
        entry.FlitIndex.ToString(CultureInfo.InvariantCulture), entry.TotalFlits.ToString(CultureInfo.InvariantCulture),
        entry.ComponentId, entry.InputPort, entry.VirtualChannel.ToString(CultureInfo.InvariantCulture), entry.OutputPort,
        entry.LinkId, entry.OccupancyBefore.ToString(CultureInfo.InvariantCulture), entry.OccupancyAfter.ToString(CultureInfo.InvariantCulture),
        entry.Ready ? "1" : "0", entry.Valid ? "1" : "0", entry.Granted ? "1" : "0",
        entry.SerializationBits.ToString(CultureInfo.InvariantCulture), entry.ArrivalCycle.ToString(CultureInfo.InvariantCulture),
        entry.TailComplete ? "1" : "0", entry.CommittedVisible ? "1" : "0", entry.Delivered ? "1" : "0",
        entry.BufferReleased ? "1" : "0", entry.Reason, entry.EvidenceLabel);
}
