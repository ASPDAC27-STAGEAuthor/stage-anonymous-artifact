using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Stable execution modes for generated Phase 8A MatMul scenarios.</summary>
public static class Phase8AMatMulExecutionModes
{
    /// <summary>Runs the exact cycle engine and records runtime metrics and trace.</summary>
    public const string FullCycle = "full-cycle";
    /// <summary>Runs the exact cycle engine with PE weights present in initial kernel state and no runtime weight injection.</summary>
    public const string FullCycleWeightsPreplaced = "full-cycle-weights-preplaced";
    /// <summary>Runs the exact cycle engine while retaining metrics and final packets instead of per-cycle trace records.</summary>
    public const string MetricsOnlyCycle = "metrics-only-cycle";
    /// <summary>Counts resolved authorities without claiming cycle execution.</summary>
    public const string Analytical = "analytical";
}

/// <summary>Stable activation ingress policies for generated Phase 8A MatMul scenarios.</summary>
public static class Phase8AActivationIngressPolicies
{
    /// <summary>Injects every activation tile through cluster zero.</summary>
    public const string SingleTopLeft = "single-top-left/v1";
    /// <summary>Partitions consecutive K tiles across rows and injects each partition through that row's leftmost cluster.</summary>
    public const string LeftColumnStriped = "left-column-striped/v1";
}

/// <summary>Requests one deterministic executable M=1 MatMul scenario for the Phase 8A MoT subset.</summary>
public sealed record Phase8AMatMulScenarioRequest(
    int K = 256,
    int N = 256,
    int WeightRowDivisionSize = 64,
    int ClusterSize = 16,
    int Seed = 807,
    int PeRows = 32,
    int PeColumns = 32,
    int MaxCycles = 20_000,
    int M = 1,
    bool WeightsPreplaced = false,
    int MeshRows = 0,
    int MeshColumns = 0,
    string ActivationIngressPolicy = Phase8AActivationIngressPolicies.LeftColumnStriped,
    int AssemblyClusterIndex = 0,
    string TopologyExecutionStrategyId = Phase8ATopologyExecutionStrategies.LegacyOverlayV2,
    string OutputLandingMode = Phase8AOutputLandingModes.CentralOffsetAssemblyV1);

/// <summary>One structured scenario-generation failure.</summary>
public sealed record Phase8AMatMulScenarioIssue(string Code, string Location, string Message);

/// <summary>Data-flow evidence derived from the same resolved D/C authorities consumed by runtime.</summary>
public sealed record Phase8AMatMulDataflowEvidence(
    int ActivationPacketCount,
    int WeightPacketCount,
    int OperationTileCount,
    int PartialSumPacketCount,
    int ReducedOutputPacketCount,
    int ActivationRouterHopCount,
    int PartialSumReductionHopCount,
    int PartialSumMeshHopCount,
    string TraceHash,
    string ExecutionMode,
    int ClusterActivationDeliveryCount,
    int ActivationBranchCloneCount,
    int LocalReducedPacketCount,
    int MeshPartialPacketCount,
    int FinalOutputPacketCount,
    int LocalAddOperationCount,
    int MeshAddOperationCount,
    long ActivationLinkTransferBits,
    long PartialSumReturnBits,
    long PartialSumMeshBits,
    long AssemblyTransferBits,
    string LayoutHash,
    string ActivationTreeHash,
    string ReductionPlanHash,
    long PhysicalPacketMovesMeasured,
    long PhysicalTransferredBitsMeasured,
    long PhysicalFlitLinkTransfersMeasured,
    long InternalCollectiveOperations,
    long LegacyRawPacketMoves,
    double PhysicalBitDistanceMeasured = 0);

/// <summary>Complete in-memory output of one generated and executed MatMul scenario.</summary>
public sealed class Phase8AMatMulScenarioBundle
{
    internal Phase8AMatMulScenarioBundle(
        Phase8AMatMulScenarioRequest request,
        int meshRows,
        int meshColumns,
        HardwareGraph topologyAuthorityGraph,
        HardwareGraph hardwareGraph,
        WorkloadMappingV2 mapping,
        Phase8ADcLayoutPlan dcLayout,
        Phase8AActivationTreePlan activationTree,
        Phase8AHierarchicalReductionPlan hierarchicalReduction,
        Phase8AWeightPlacementPlan weightPlacement,
        IReadOnlyList<double> input,
        IReadOnlyList<double> weights,
        IReadOnlyList<double> expectedOutput,
        IReadOnlyList<double> actualOutput,
        SimulationResult simulation,
        Phase8AMatMulDataflowEvidence dataflow,
        string resolvedMappingHash,
        string loweringHash,
        string operandPlanHash)
    {
        Request = request;
        MeshRows = meshRows;
        MeshColumns = meshColumns;
        TopologyAuthorityGraph = topologyAuthorityGraph;
        HardwareGraph = hardwareGraph;
        Mapping = mapping;
        DcLayout = dcLayout;
        ActivationTree = activationTree;
        HierarchicalReduction = hierarchicalReduction;
        WeightPlacement = weightPlacement;
        Input = Array.AsReadOnly(input.ToArray());
        Weights = Array.AsReadOnly(weights.ToArray());
        ExpectedOutput = Array.AsReadOnly(expectedOutput.ToArray());
        ActualOutput = Array.AsReadOnly(actualOutput.ToArray());
        Simulation = simulation;
        Dataflow = dataflow;
        ResolvedMappingHash = resolvedMappingHash;
        LoweringHash = loweringHash;
        OperandPlanHash = operandPlanHash;
    }

    /// <summary>Gets the normalized generation request.</summary>
    public Phase8AMatMulScenarioRequest Request { get; }
    /// <summary>Gets the resolved root-mesh row count.</summary>
    public int MeshRows { get; }
    /// <summary>Gets the resolved root-mesh column count.</summary>
    public int MeshColumns { get; }
    /// <summary>Gets the immutable typed topology used by all resolved authorities.</summary>
    public HardwareGraph TopologyAuthorityGraph { get; }
    /// <summary>Gets the Unity-loadable executable hardware graph.</summary>
    public HardwareGraph HardwareGraph { get; }
    /// <summary>Gets the verified WorkloadMapping 2.0 document.</summary>
    public WorkloadMappingV2 Mapping { get; }
    /// <summary>Gets the unique D/C placement and contributor-group authority.</summary>
    public Phase8ADcLayoutPlan DcLayout { get; }
    /// <summary>Gets the exact shared-prefix activation-tree authority.</summary>
    public Phase8AActivationTreePlan ActivationTree { get; }
    /// <summary>Gets the exact local/global reduction and assembly authority.</summary>
    public Phase8AHierarchicalReductionPlan HierarchicalReduction { get; }
    /// <summary>Gets the immutable weight residency and lifecycle authority.</summary>
    public Phase8AWeightPlacementPlan WeightPlacement { get; }
    /// <summary>Gets the deterministic X[1,K] values.</summary>
    public IReadOnlyList<double> Input { get; }
    /// <summary>Gets the deterministic row-major W[K,N] values.</summary>
    public IReadOnlyList<double> Weights { get; }
    /// <summary>Gets the tiled digital-reference result.</summary>
    public IReadOnlyList<double> ExpectedOutput { get; }
    /// <summary>Gets the result reconstructed from runtime-delivered Y.</summary>
    public IReadOnlyList<double> ActualOutput { get; }
    /// <summary>Gets the completed cycle-simulation result.</summary>
    public SimulationResult Simulation { get; }
    /// <summary>Gets route, packet, and authority conservation evidence.</summary>
    public Phase8AMatMulDataflowEvidence Dataflow { get; }
    /// <summary>Gets the generalized D/C mapping authority hash.</summary>
    public string ResolvedMappingHash { get; }
    /// <summary>Gets the resolved mapping hash through the original compatibility property.</summary>
    public string ReferenceMappingHash => ResolvedMappingHash;
    /// <summary>Gets the tensor-lowering canonical hash.</summary>
    public string LoweringHash { get; }
    /// <summary>Gets the exact external-operand compilation hash.</summary>
    public string OperandPlanHash { get; }
    /// <summary>Gets whether every expected value exactly matches runtime output.</summary>
    public bool IsExact => ExpectedOutput.SequenceEqual(ActualOutput);
}

/// <summary>Structured all-or-nothing scenario generation result.</summary>
public sealed class Phase8AMatMulScenarioGenerationResult
{
    internal Phase8AMatMulScenarioGenerationResult(
        Phase8AMatMulScenarioBundle? bundle,
        IEnumerable<Phase8AMatMulScenarioIssue>? issues)
    {
        Bundle = bundle;
        Issues = new ReadOnlyCollection<Phase8AMatMulScenarioIssue>((issues ?? []).ToList());
    }

    /// <summary>Gets the complete scenario on success.</summary>
    public Phase8AMatMulScenarioBundle? Bundle { get; }
    /// <summary>Gets deterministic structured failures.</summary>
    public IReadOnlyList<Phase8AMatMulScenarioIssue> Issues { get; }
    /// <summary>Gets whether a complete scenario was produced without issues.</summary>
    public bool IsSuccess => Bundle is not null && Issues.Count == 0;
}
