using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Resolved structural authorities shared by runtime, estimators, JSON, and Unity.</summary>
public sealed class Phase8AMatMulResolvedAuthorityBundle
{
    internal Phase8AMatMulResolvedAuthorityBundle(
        Phase8AMatMulScenarioRequest request,
        int meshRows,
        int meshColumns,
        HardwareGraph topologyGraph,
        TopologyManifest topologyManifest,
        Phase8AMatMulLoweringPlan lowering,
        Phase8ADcLayoutPlan dcLayout,
        Phase8AActivationTreePlan activationTree,
        Phase8AHierarchicalReductionPlan hierarchicalReduction,
        IEnumerable<OperationTileAssignment> assignments,
        Phase8AWeightPlacementPlan weightPlacement,
        MappingCandidate candidate,
        CapabilitySnapshot capabilities,
        string inputArtifactHash,
        string weightArtifactHash,
        string resolvedMappingHash)
    {
        Request = request with { };
        this.topologyGraph = topologyGraph;
        MeshRows = meshRows;
        MeshColumns = meshColumns;
        TopologyManifest = topologyManifest;
        Lowering = lowering;
        DcLayout = dcLayout;
        ActivationTree = activationTree;
        HierarchicalReduction = hierarchicalReduction;
        Assignments = new ReadOnlyCollection<OperationTileAssignment>(assignments.ToArray());
        WeightPlacement = weightPlacement;
        Candidate = candidate;
        Capabilities = capabilities;
        InputArtifactHash = inputArtifactHash;
        WeightArtifactHash = weightArtifactHash;
        ResolvedMappingHash = resolvedMappingHash;
    }

    private readonly HardwareGraph topologyGraph;

    /// <summary>Gets the normalized scenario request.</summary>
    public Phase8AMatMulScenarioRequest Request { get; }
    /// <summary>Gets the resolved root-mesh row count.</summary>
    public int MeshRows { get; }
    /// <summary>Gets the resolved root-mesh column count.</summary>
    public int MeshColumns { get; }
    /// <summary>Gets a defensive copy of the typed topology authority graph.</summary>
    public HardwareGraph TopologyGraph => HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(topologyGraph));
    internal HardwareGraph TopologyGraphAuthority => topologyGraph;
    /// <summary>Gets the typed topology manifest.</summary>
    public TopologyManifest TopologyManifest { get; }
    /// <summary>Gets the canonical tensor-lowering authority.</summary>
    public Phase8AMatMulLoweringPlan Lowering { get; }
    /// <summary>Gets the unique D/C placement and grouping authority.</summary>
    public Phase8ADcLayoutPlan DcLayout { get; }
    /// <summary>Gets the shared-prefix activation-tree authority.</summary>
    public Phase8AActivationTreePlan ActivationTree { get; }
    /// <summary>Gets the local/global reduction and final-assembly authority.</summary>
    public Phase8AHierarchicalReductionPlan HierarchicalReduction { get; }
    /// <summary>Gets operation-tile assignments derived from the resolved layout.</summary>
    public IReadOnlyList<OperationTileAssignment> Assignments { get; }
    /// <summary>Gets weight residency and lifecycle placement.</summary>
    public Phase8AWeightPlacementPlan WeightPlacement { get; }
    /// <summary>Gets the immutable mapping candidate descriptor.</summary>
    public MappingCandidate Candidate { get; }
    /// <summary>Gets the exact PE capability snapshot used for placement.</summary>
    public CapabilitySnapshot Capabilities { get; }
    /// <summary>Gets the deterministic generated-input artifact identity.</summary>
    public string InputArtifactHash { get; }
    /// <summary>Gets the deterministic generated-weight artifact identity.</summary>
    public string WeightArtifactHash { get; }
    /// <summary>Gets the hash joining all resolved mapping authorities.</summary>
    public string ResolvedMappingHash { get; }
}

/// <summary>All-or-nothing structural authority resolution result.</summary>
public sealed class Phase8AMatMulResolvedAuthorityResult
{
    internal Phase8AMatMulResolvedAuthorityResult(
        Phase8AMatMulResolvedAuthorityBundle? bundle,
        IEnumerable<Phase8AMatMulScenarioIssue>? issues)
    {
        Bundle = bundle;
        Issues = new ReadOnlyCollection<Phase8AMatMulScenarioIssue>((issues ?? []).ToArray());
    }

    /// <summary>Gets the complete authority bundle on success.</summary>
    public Phase8AMatMulResolvedAuthorityBundle? Bundle { get; }
    /// <summary>Gets deterministic structured resolution failures.</summary>
    public IReadOnlyList<Phase8AMatMulScenarioIssue> Issues { get; }
    /// <summary>Gets whether a complete authority bundle was resolved.</summary>
    public bool IsSuccess => Bundle is not null && Issues.Count == 0;
}

/// <summary>Resolves the single Phase 8A D/C structural truth without running the cycle engine.</summary>
public static class Phase8AMatMulResolvedAuthorityResolver
{
private const string GeneralizedDcPolicyId = "mot-generalized-dc-first-fit/v1";
    private static readonly object TopologyCacheLock = new();
    private static string cachedTopologyKey = "";
    private static HardwareGraph? cachedTopologyGraph;
    private static TopologyManifest? cachedTopologyManifest;

    /// <summary>Resolves all structural authorities for one normalized request.</summary>
    public static Phase8AMatMulResolvedAuthorityResult Resolve(Phase8AMatMulScenarioRequest request)
    {
        var issues = Validate(request);
        if (issues.Count > 0) return Failure(issues);

        try
        {
            var kTiles = checked(request.K / request.PeRows);
            var nTiles = checked(request.N / request.PeColumns);
            var totalPes = checked(kTiles * nTiles);
            var requiredClusters = CeilingDiv(totalPes, request.ClusterSize);
            var hasExplicitMesh = request.MeshRows > 0 && request.MeshColumns > 0;
            var clusters = hasExplicitMesh ? checked(request.MeshRows * request.MeshColumns) : requiredClusters;
            if (clusters < requiredClusters)
                return Failure([new Phase8AMatMulScenarioIssue("Phase8AScenarioMeshCapacityExceeded", "$.mesh", "The explicit mesh does not provide enough cluster capacity for every operation tile.")]);
            var (meshRows, meshColumns) = hasExplicitMesh
                ? (request.MeshRows, request.MeshColumns)
                : FactorMesh(clusters);
            if (request.AssemblyClusterIndex >= clusters)
                return Failure([new Phase8AMatMulScenarioIssue("Phase8AScenarioAssemblyClusterInvalid", "$.assemblyClusterIndex", "Assembly cluster index is outside the resolved cluster inventory.")]);
            var (topologyGraph, topologyManifest) = ResolveTopology(meshRows, meshColumns, request.ClusterSize);

            var inputArtifactHash = ArtifactHash("X", request);
            var weightArtifactHash = ArtifactHash("W", request);
            var fixedFp32Packets = string.Equals(
                request.TopologyExecutionStrategyId,
                Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1,
                StringComparison.Ordinal);
            var operandPrecision = fixedFp32Packets ? PrecisionKind.FP32 : PrecisionKind.FP8_E4M3;
            var operandBitWidth = fixedFp32Packets
                ? Phase8AMoTInrRuntimeIds.FixedPacketBitWidth
                : 8;
            var seedCapability = PeCapability("seed", request.PeRows, request.PeColumns, operandPrecision, operandBitWidth);
            var lowering = Phase8ATensorLowerer.LowerMatMul(new Phase8AMatMulLoweringRequest(
                "matmul", "X", "W", "Y",
                request.M, request.K, request.N,
                1, request.PeRows, request.PeColumns,
                operandPrecision, operandPrecision, operandPrecision,
                Phase8AMatMulPartitionKind.Hybrid,
                seedCapability));
            if (!lowering.IsSuccess || lowering.Plan is null)
                return Failure(lowering.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            var layoutResult = Phase8ADcLayoutPlanner.Plan(new Phase8ADcLayoutRequest(
                request.M, request.K, request.N, request.WeightRowDivisionSize, request.ClusterSize,
                request.PeRows, request.PeColumns, clusters, "matmul"));
            if (!layoutResult.IsSuccess || layoutResult.Plan is null)
                return Failure(layoutResult.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));
            var layout = layoutResult.Plan;

            var ingressClusters = ResolveActivationIngressClusters(request, meshRows, meshColumns, kTiles);
            var activationResult = Phase8AActivationTreePlanner.Plan(
                layout,
                topologyGraph,
                activationBitWidth: operandBitWidth,
                ingressClusterIndex: ingressClusters[0],
                ingressPolicyId: request.ActivationIngressPolicy,
                ingressClusterByGlobalKTile: ingressClusters);
            if (!activationResult.IsSuccess || activationResult.Plan is null)
                return Failure(activationResult.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));
            var reductionResult = Phase8AHierarchicalReductionPlanner.Plan(layout, topologyGraph, vectorBitWidth: operandBitWidth, assemblyClusterIndex: request.AssemblyClusterIndex);
            if (!reductionResult.IsSuccess || reductionResult.Plan is null)
                return Failure(reductionResult.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            var peCapabilities = topologyManifest.Components
                .Where(component => component.Role == TopologyPresetComponentRole.ProcessingElement)
                .Select(component => PeCapability(component.ComponentId, request.PeRows, request.PeColumns, operandPrecision, operandBitWidth))
                .ToArray();
            var capabilities = new CapabilitySnapshot(
                ComponentExecutionJson.ComputeSha256("phase8a-generated-matmul-capabilities\n" + topologyManifest.CanonicalHash),
                topologyManifest.TopologyGraphHash,
                topologyManifest.PlacementHash,
                topologyManifest.RouteHash,
                ComponentExecutionJson.ComputeSha256("phase8a-generated-matmul-registry-v2"),
                peCapabilities);

            var targetByDcAssignment = activationResult.Plan.Trees.SelectMany(tree => tree.Targets)
                .ToDictionary(target => target.AssignmentId, StringComparer.Ordinal);
            var loweredByRange = lowering.Plan.OperationTiles.ToDictionary(
                tile => (tile.KRange.Offset, tile.KRange.Extent, tile.NRange.Offset, tile.NRange.Extent));
            var assignments = layout.Assignments.Select(dc =>
            {
                if (!targetByDcAssignment.TryGetValue(dc.AssignmentId, out var target) ||
                    !loweredByRange.TryGetValue((dc.KRange.Offset, dc.KRange.Extent, dc.NRange.Offset, dc.NRange.Extent), out var lowered))
                    throw new InvalidOperationException("Resolved D/C assignment does not match lowering or topology endpoint authority: " + dc.AssignmentId);
                if (!string.Equals(dc.ActivationTileId, lowered.ActivationTileId, StringComparison.Ordinal) ||
                    !string.Equals(dc.WeightTileId, lowered.WeightTileId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Resolved D/C tile identities drifted from tensor lowering: " + dc.AssignmentId);
                return lowered.CreateAssignment(lowered.OperationTileId, target.PeComponentId, target.PeComponentId + ".activation");
            }).OrderBy(item => item.AssignmentId, StringComparer.Ordinal).ToArray();

            var artifactUse = new Phase8AWeightArtifactUse(
                "matmul", "W", weightArtifactHash, operandPrecision.ToString(), assignments.Select(item => item.AssignmentId));
            var weightPlacementResult = Phase8AWeightPlacementPlanner.Plan(new Phase8AWeightPlacementRequest(
                capabilities,
                assignments,
                lowering.Plan.OperandTiles.Where(item => item.RoleId == Phase8ATensorRoleIds.Weight),
                [artifactUse]));
            if (!weightPlacementResult.IsSuccess || weightPlacementResult.Plan is null)
                return Failure(weightPlacementResult.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            var normalizedHash = ComponentExecutionJson.ComputeSha256(string.Join("\n",
                lowering.Plan.CanonicalHash,
                layout.CanonicalHash,
                activationResult.Plan.CanonicalHash,
                reductionResult.Plan.CanonicalHash,
                inputArtifactHash,
                weightArtifactHash));
            var profileHash = ComponentExecutionJson.ComputeSha256(string.Join("\n", capabilities.Components
                .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
                .Select(item => item.ComponentId + ":" + item.ProfileHash)));
            var candidate = new MappingCandidate(
                "candidate:dc:" + normalizedHash[..16],
                GeneralizedDcPolicyId,
                normalizedHash,
                [],
                [
                    new MappingScoreItem("dc.activation.link_transfers", activationResult.Plan.Summary.UniqueLinkTransferCount, 1m, activationResult.Plan.Summary.UniqueLinkTransferCount, "packet-hop", "resolved-activation-tree/v1"),
                    new MappingScoreItem("dc.reduction.link_transfers", reductionResult.Plan.Summary.LocalTreeLinkTransferCount + reductionResult.Plan.Summary.GlobalReturnLinkTransferCount + reductionResult.Plan.Summary.GlobalMeshLinkTransferCount, 1m, reductionResult.Plan.Summary.LocalTreeLinkTransferCount + reductionResult.Plan.Summary.GlobalReturnLinkTransferCount + reductionResult.Plan.Summary.GlobalMeshLinkTransferCount, "packet-hop", "resolved-hierarchical-reduction/v1")
                ],
                topologyManifest.TopologyGraphHash,
                topologyManifest.RouteHash,
                profileHash,
                layout.CanonicalHash,
                []);
            var mappingAuthorityHash = ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
            {
                algorithm = "sha256/phase8a-resolved-dc-mapping/v2",
                normalizedHash,
                layout = layout.CanonicalHash,
                activation = activationResult.Plan.CanonicalHash,
                reduction = reductionResult.Plan.CanonicalHash,
                weights = weightPlacementResult.Plan.CanonicalHash,
                assignments = assignments.Select(item => new { item.AssignmentId, item.TargetComponentId, item.KRange, item.NRange })
            }, HardwareGraphJson.Options)));

            return new Phase8AMatMulResolvedAuthorityResult(new Phase8AMatMulResolvedAuthorityBundle(
                request,
                meshRows,
                meshColumns,
                topologyGraph,
                topologyManifest,
                lowering.Plan,
                layout,
                activationResult.Plan,
                reductionResult.Plan,
                assignments,
                weightPlacementResult.Plan,
                candidate,
                capabilities,
                inputArtifactHash,
                weightArtifactHash,
                mappingAuthorityHash), []);
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException or ArgumentException)
        {
            return Failure([new Phase8AMatMulScenarioIssue("Phase8AScenarioPlanningFailed", "$", exception.Message)]);
        }
    }

    private static List<Phase8AMatMulScenarioIssue> Validate(Phase8AMatMulScenarioRequest? request)
    {
        var issues = new List<Phase8AMatMulScenarioIssue>();
        if (request is null)
        {
            issues.Add(new("Phase8AScenarioRequestMissing", "$", "A scenario request is required."));
            return issues;
        }
        if (request.M <= 0 || request.K <= 0 || request.N <= 0 || request.PeRows <= 0 || request.PeColumns <= 0)
            issues.Add(new("Phase8AScenarioShapeInvalid", "$", "M, K, N, and PE dimensions must be positive."));
        if (request.M != 1)
            issues.Add(new("Phase8AScenarioMExtentUnsupported", "$.m", "This scenario generator currently supports M=1 only."));
        if (request.PeRows != 32 || request.PeColumns != 32)
            issues.Add(new("Phase8AScenarioPeShapeUnsupported", "$.pe", "This generator implements the approved 1x32 x 32x32 PE contract."));
        if (request.K % request.PeRows != 0 || request.N % request.PeColumns != 0)
            issues.Add(new("Phase8AScenarioTailUnsupported", "$.shape", "K and N must be exactly divisible by the PE dimensions."));
        if (request.WeightRowDivisionSize <= 0 || request.WeightRowDivisionSize > request.K ||
            request.K % request.WeightRowDivisionSize != 0 || request.WeightRowDivisionSize % request.PeRows != 0)
            issues.Add(new("Phase8AScenarioDivisionInvalid", "$.weightRowDivisionSize", "D must not exceed K, must divide K, and must be divisible by 32."));
        if (request.ClusterSize < 2 || !IsPowerOfTwo(request.ClusterSize))
            issues.Add(new("Phase8AScenarioClusterInvalid", "$.clusterSize", "C must be a power of two and at least two."));
        if (request.MeshRows < 0 || request.MeshColumns < 0 || (request.MeshRows == 0) != (request.MeshColumns == 0))
            issues.Add(new("Phase8AScenarioMeshShapeInvalid", "$.mesh", "MeshRows and MeshColumns must either both be zero for automatic factorization or both be positive."));
        if (request.ActivationIngressPolicy is not (Phase8AActivationIngressPolicies.SingleTopLeft or Phase8AActivationIngressPolicies.LeftColumnStriped))
            issues.Add(new("Phase8AScenarioIngressPolicyInvalid", "$.activationIngressPolicy", "Activation ingress policy must be a supported stable policy id."));
        if (request.TopologyExecutionStrategyId is not (Phase8ATopologyExecutionStrategies.LegacyOverlayV2 or Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1))
            issues.Add(new("Phase8AScenarioExecutionStrategyUnsupported", "$.topologyExecutionStrategyId", "The topology execution strategy id is not registered."));
        if (request.OutputLandingMode is not (
                Phase8AOutputLandingModes.ClusterLocalShardsV1 or
                Phase8AOutputLandingModes.TopologyEgressShardsV1 or
                Phase8AOutputLandingModes.CentralOffsetAssemblyV1))
            issues.Add(new("Phase8AScenarioOutputLandingModeInvalid", "$.outputLandingMode", "The output landing mode must be cluster-local shards, topology-egress shards, or central offset assembly."));
        if (request.TopologyExecutionStrategyId == Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1 &&
            request.ActivationIngressPolicy != Phase8AActivationIngressPolicies.LeftColumnStriped)
            issues.Add(new("Phase8AMoTInrIngressPolicyInvalid", "$.activationIngressPolicy", "The row-replicated MoT-INR strategy requires left-column-striped/v1 ingress."));
        if (request.AssemblyClusterIndex < 0)
            issues.Add(new("Phase8AScenarioAssemblyClusterInvalid", "$.assemblyClusterIndex", "Assembly cluster index must not be negative."));
        if (request.MaxCycles <= 0)
            issues.Add(new("Phase8AScenarioMaxCyclesInvalid", "$.maxCycles", "MaxCycles must be positive."));
        return issues;
    }

    private static ComponentCapabilitySnapshot PeCapability(
        string componentId,
        int rows,
        int columns,
        PrecisionKind precision,
        int bitWidth)
    {
        var weightPortId = componentId + ".weight";
        var capacity = checked((long)rows * columns * bitWidth);
        var precisionId = precision.ToString();
        var profileId = "generated-" + precisionId.ToLowerInvariant() + "-profile";
        return new ComponentCapabilitySnapshot(
            componentId,
            "com.hardware-sim.generated.phase8a.pe.v1",
            $"PE_Array_{rows}x{columns}_{precisionId}_SRAM_Synthetic",
            ComponentExecutionJson.ComputeSha256($"PE_Array_{rows}x{columns}_{precisionId}_SRAM_Synthetic:1.0.0"),
            profileId,
            ComponentExecutionJson.ComputeSha256(profileId),
            "core.digital.vmm",
            ComponentExecutionJson.ComputeSha256("core.digital.vmm:1.1.0"),
            ["matmul"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Phase8ACapabilityShapeKeys.MatMulMaximumM] = "1",
                [Phase8ACapabilityShapeKeys.MatMulMaximumK] = rows.ToString(CultureInfo.InvariantCulture),
                [Phase8ACapabilityShapeKeys.MatMulMaximumN] = columns.ToString(CultureInfo.InvariantCulture)
            },
            [precisionId],
            capacity,
            12,
            1024,
            [
                new CapabilityPortSnapshot(componentId + ".activation", "input", "packet", "digital/default", 8192, "activation", HardwareDataType.Tensor.ToString(), precisionId),
                new CapabilityPortSnapshot(weightPortId, "input", "packet", "digital/default", 1024, "weight", HardwareDataType.Tensor.ToString(), precisionId)
            ],
            "digital/default",
            [new ComponentStorageCapabilitySnapshot(
                "weight-store", "pe-local", [Phase8ATensorRoleIds.Weight], [precisionId],
                capacity, bitWidth, bitWidth, 1, weightPortId, 1024, 1024, 1, 1,
                "write-then-commit-v1", false, true, ComponentExecutionJson.ComputeSha256("generated-weight-store-contract:" + precisionId))]);
    }

    private static (HardwareGraph Graph, TopologyManifest Manifest) ResolveTopology(
        int meshRows,
        int meshColumns,
        int clusterSize)
    {
        var key = string.Join(":", meshRows, meshColumns, clusterSize);
        lock (TopologyCacheLock)
        {
            if (cachedTopologyKey == key && cachedTopologyGraph is not null && cachedTopologyManifest is not null)
                return (cachedTopologyGraph, cachedTopologyManifest);
            var result = new MeshOfTreesTopologyPresetBuilder().Build(new TopologyPresetRequest(
                ReferenceMappingTopologyIds.MeshOfTreesV1,
                meshRows,
                meshColumns,
                clusterSize,
                wordBits: 32,
                leafLaneCount: 8));
            if (!result.IsSuccess || result.TopologyManifest is null)
                throw new InvalidOperationException(string.Join("; ", result.Issues.Select(issue => issue.Code + ": " + issue.Message)));
            cachedTopologyKey = key;
            cachedTopologyGraph = result.HardwareGraph;
            cachedTopologyManifest = result.TopologyManifest;
            return (cachedTopologyGraph, cachedTopologyManifest);
        }
    }

    private static IReadOnlyDictionary<int, int> ResolveActivationIngressClusters(
        Phase8AMatMulScenarioRequest request,
        int meshRows,
        int meshColumns,
        int kTileCount)
    {
        if (request.ActivationIngressPolicy == Phase8AActivationIngressPolicies.SingleTopLeft)
            return Enumerable.Range(0, kTileCount).ToDictionary(index => index, _ => 0);

        return Enumerable.Range(0, kTileCount).ToDictionary(
            globalKTileIndex => globalKTileIndex,
            globalKTileIndex =>
            {
                var row = Math.Min(meshRows - 1, (int)((long)globalKTileIndex * meshRows / kTileCount));
                return checked(row * meshColumns);
            });
    }

    private static string ArtifactHash(string tensorId, Phase8AMatMulScenarioRequest request) =>
        ComponentExecutionJson.ComputeSha256(string.Join("\n",
            "phase8a-generated-values/xorshift32-sparse-activation-ternary-weight/v1",
            tensorId,
            request.M.ToString(CultureInfo.InvariantCulture),
            request.K.ToString(CultureInfo.InvariantCulture),
            request.N.ToString(CultureInfo.InvariantCulture),
            request.Seed.ToString(CultureInfo.InvariantCulture)));

    private static (int Rows, int Columns) FactorMesh(int clusters)
    {
        var rows = (int)Math.Floor(Math.Sqrt(clusters));
        while (rows > 1 && clusters % rows != 0) rows--;
        return (rows, clusters / rows);
    }

    private static int CeilingDiv(int value, int divisor) => checked((value + divisor - 1) / divisor);
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    private static Phase8AMatMulResolvedAuthorityResult Failure(IEnumerable<Phase8AMatMulScenarioIssue> issues) => new(null, issues);
}
