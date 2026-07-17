using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Defines structured hard failures emitted by the Phase 8A reference first-fit mapper.</summary>
public static class ReferenceFirstFitIssueCodes
{
    /// <summary>The selected mapping policy is outside the reference first-fit subset.</summary>
    public const string UnsupportedPolicy = "ReferenceFirstFitPolicyUnsupported";
    /// <summary>The selected topology is not the typed Mesh-of-Trees preset.</summary>
    public const string UnsupportedTopology = "ReferenceFirstFitTopologyUnsupported";
    /// <summary>The normalized reference request disagrees with the persisted topology request.</summary>
    public const string RequestTopologyMismatch = "ReferenceFirstFitRequestTopologyMismatch";
    /// <summary>The reference policy subset received an M extent other than one.</summary>
    public const string UnsupportedMExtent = "ReferenceFirstFitMExtentUnsupported";
    /// <summary>The capability snapshot was frozen against a different logical topology hash.</summary>
    public const string CapabilityTopologyHashMismatch = "ReferenceFirstFitCapabilityTopologyHashMismatch";
    /// <summary>The capability snapshot was frozen against a different physical placement hash.</summary>
    public const string CapabilityPlacementHashMismatch = "ReferenceFirstFitCapabilityPlacementHashMismatch";
    /// <summary>The capability snapshot was frozen against a different physical route hash.</summary>
    public const string CapabilityRouteHashMismatch = "ReferenceFirstFitCapabilityRouteHashMismatch";
    /// <summary>The typed manifest does not describe a complete canonical PE inventory.</summary>
    public const string InvalidProcessingElementManifest = "ReferenceFirstFitProcessingElementManifestInvalid";
    /// <summary>Two PEs share one semantic coordinate inside the same typed cluster.</summary>
    public const string DuplicateProcessingElementCoordinate = "ReferenceFirstFitProcessingElementCoordinateDuplicate";
    /// <summary>The lowering plan does not exactly match the normalized request geometry or compute tile.</summary>
    public const string LoweringMismatch = "ReferenceFirstFitLoweringMismatch";
    /// <summary>An explicit weight artifact use is incomplete, inconsistent, or duplicated.</summary>
    public const string ArtifactUseInvalid = "ReferenceFirstFitArtifactUseInvalid";
    /// <summary>A typed manifest PE has no corresponding frozen compiled capability.</summary>
    public const string CapabilityMissing = "ReferenceFirstFitCapabilityMissing";
    /// <summary>A typed manifest PE does not have exactly one frozen compiled capability.</summary>
    public const string CapabilityInventoryInvalid = "ReferenceFirstFitCapabilityInventoryInvalid";
    /// <summary>A selected PE lacks an explicitly declared operation, precision, storage, or preload capability.</summary>
    public const string CapabilityUnsupported = "ReferenceFirstFitCapabilityUnsupported";
    /// <summary>The row-division PE demand cannot form a legal reference-compatible segment.</summary>
    public const string SegmentInvalid = "ReferenceFirstFitSegmentInvalid";
    /// <summary>No cluster has enough contiguous free PE slots for a complete block.</summary>
    public const string CapacityExceeded = "ReferenceFirstFitCapacityExceeded";
    /// <summary>A complete deterministic cluster pass made no allocation progress.</summary>
    public const string NoProgress = "ReferenceFirstFitNoProgress";
    /// <summary>Checked allocation or canonicalization arithmetic exceeded the supported range.</summary>
    public const string ArithmeticOverflow = "ReferenceFirstFitArithmeticOverflow";
}

/// <summary>Freezes every input consumed by one reference-compatible first-fit mapping attempt.</summary>
public sealed class ReferenceFirstFitMappingRequest
{
    private readonly string _hardwareGraphJson;

    /// <summary>Creates a defensive snapshot of every mapping input, including the mutable hardware graph.</summary>
    /// <param name="referenceRequest">Validated normalized workload, topology, compute, division, policy, and seed input.</param>
    /// <param name="hardwareGraph">Hardware graph carrying the persisted typed topology manifest.</param>
    /// <param name="loweringPlan">Immutable MatMul tile and collective lowering plan.</param>
    /// <param name="capabilitySnapshot">Frozen compiled capability snapshot bound to topology, placement, and route hashes.</param>
    /// <param name="artifactUses">Explicit weight artifact uses covering every lowered operation tile.</param>
    public ReferenceFirstFitMappingRequest(
        ReferenceMappingRequest referenceRequest,
        HardwareGraph hardwareGraph,
        Phase8AMatMulLoweringPlan loweringPlan,
        CapabilitySnapshot capabilitySnapshot,
        IEnumerable<Phase8AWeightArtifactUse>? artifactUses)
    {
        ReferenceRequest = referenceRequest ?? throw new ArgumentNullException(nameof(referenceRequest));
        _hardwareGraphJson = HardwareGraphJson.Serialize(hardwareGraph ?? throw new ArgumentNullException(nameof(hardwareGraph)));
        LoweringPlan = loweringPlan ?? throw new ArgumentNullException(nameof(loweringPlan));
        CapabilitySnapshot = capabilitySnapshot ?? throw new ArgumentNullException(nameof(capabilitySnapshot));
        ArtifactUses = new ReadOnlyCollection<Phase8AWeightArtifactUse>((artifactUses ?? [])
            .OrderBy(item => item.OperationId, StringComparer.Ordinal)
            .ThenBy(item => item.TensorId, StringComparer.Ordinal)
            .ThenBy(item => item.ArtifactHash, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Gets the normalized reference mapping request.</summary>
    public ReferenceMappingRequest ReferenceRequest { get; }
    /// <summary>Gets a defensive hardware graph copy.</summary>
    public HardwareGraph HardwareGraph => HardwareGraphJson.Deserialize(_hardwareGraphJson);
    /// <summary>Gets the immutable lowering plan.</summary>
    public Phase8AMatMulLoweringPlan LoweringPlan { get; }
    /// <summary>Gets the frozen compiled capability snapshot.</summary>
    public CapabilitySnapshot CapabilitySnapshot { get; }
    /// <summary>Gets explicit artifact uses in deterministic canonical order.</summary>
    public IReadOnlyList<Phase8AWeightArtifactUse> ArtifactUses { get; }

    internal HardwareGraph CloneHardwareGraph() =>
        JsonSerializer.Deserialize<HardwareGraph>(_hardwareGraphJson, HardwareGraphJson.Options)
        ?? throw new InvalidOperationException("Frozen HardwareGraph JSON did not contain an object.");
}

/// <summary>Identifies one PE only through typed manifest hierarchy and coordinates.</summary>
/// <param name="ClusterIndex">Typed row-major cluster index.</param>
/// <param name="PeOrdinal">PE ordinal derived from coordinate row and column order within the cluster.</param>
/// <param name="CoordinateRow">Typed manifest coordinate row.</param>
/// <param name="CoordinateColumn">Typed manifest coordinate column.</param>
/// <param name="ComponentId">Opaque execution binding retained only after semantic ordering.</param>
public sealed record ReferenceFirstFitPeSlot(
    int ClusterIndex,
    int PeOrdinal,
    int CoordinateRow,
    int CoordinateColumn,
    string ComponentId);

/// <summary>Records one N block's exact contiguous PE reservation.</summary>
public sealed class ReferenceFirstFitBlockAllocation
{
    internal ReferenceFirstFitBlockAllocation(
        int divisionIndex,
        int nBlockIndex,
        MappingIndexRange kRange,
        MappingIndexRange nRange,
        long paddedNExtent,
        int clusterIndex,
        int segmentOrdinal,
        string utilizationBitsetAfter,
        IEnumerable<ReferenceFirstFitPeSlot> slots)
    {
        DivisionIndex = divisionIndex;
        NBlockIndex = nBlockIndex;
        KRange = kRange;
        NRange = nRange;
        PaddedNExtent = paddedNExtent;
        ClusterIndex = clusterIndex;
        SegmentOrdinal = segmentOrdinal;
        UtilizationBitsetAfter = utilizationBitsetAfter;
        PeSlots = new ReadOnlyCollection<ReferenceFirstFitPeSlot>(slots.OrderBy(item => item.PeOrdinal).ToList());
    }

    /// <summary>Gets the containing K-division index.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the zero-based output-column block index.</summary>
    public int NBlockIndex { get; }
    /// <summary>Gets the complete K range represented by this row division.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the valid, unpadded output-column range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the physical padded output-column extent.</summary>
    public long PaddedNExtent { get; }
    /// <summary>Gets the typed cluster index selected by first fit.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the zero-based segment containing the contiguous reservation.</summary>
    public int SegmentOrdinal { get; }
    /// <summary>Gets cluster utilization immediately after this complete block commit.</summary>
    public string UtilizationBitsetAfter { get; }
    /// <summary>Gets contiguous PE slots in semantic ordinal order.</summary>
    public IReadOnlyList<ReferenceFirstFitPeSlot> PeSlots { get; }
}

/// <summary>Records one K division and the all-or-nothing blocks allocated for it.</summary>
public sealed class ReferenceFirstFitDivisionAllocation
{
    internal ReferenceFirstFitDivisionAllocation(
        int divisionIndex,
        MappingIndexRange kRange,
        IEnumerable<ReferenceFirstFitBlockAllocation> blocks,
        IEnumerable<ReferenceFirstFitClusterUtilization> clusterUtilizationAfter)
    {
        DivisionIndex = divisionIndex;
        KRange = kRange;
        Blocks = new ReadOnlyCollection<ReferenceFirstFitBlockAllocation>(blocks.OrderBy(item => item.NBlockIndex).ToList());
        ClusterUtilizationAfter = new ReadOnlyCollection<ReferenceFirstFitClusterUtilization>(clusterUtilizationAfter.OrderBy(item => item.ClusterIndex).ToList());
    }

    /// <summary>Gets the zero-based K-division index.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the complete K range covered by the division.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets N-block reservations in increasing output-column order.</summary>
    public IReadOnlyList<ReferenceFirstFitBlockAllocation> Blocks { get; }
    /// <summary>Gets every cluster's utilization after the division has committed completely.</summary>
    public IReadOnlyList<ReferenceFirstFitClusterUtilization> ClusterUtilizationAfter { get; }
}

/// <summary>Freezes one cluster's PE utilization after a complete division.</summary>
/// <param name="ClusterIndex">Typed cluster index.</param>
/// <param name="UtilizationBitset">PE occupancy in semantic ordinal order.</param>
public sealed record ReferenceFirstFitClusterUtilization(int ClusterIndex, string UtilizationBitset);

/// <summary>Describes branch-aware activation distribution derived only from typed manifest roles.</summary>
public sealed class ReferenceFirstFitBranchIntent
{
    internal ReferenceFirstFitBranchIntent(
        string intentId,
        int divisionIndex,
        int nBlockIndex,
        int clusterIndex,
        string ingressComponentId,
        string branchRootComponentId,
        IEnumerable<string> operationTileIds,
        IEnumerable<ReferenceFirstFitPeSlot> destinationSlots)
    {
        IntentId = intentId;
        DivisionIndex = divisionIndex;
        NBlockIndex = nBlockIndex;
        ClusterIndex = clusterIndex;
        IngressComponentId = ingressComponentId;
        BranchRootComponentId = branchRootComponentId;
        OperationTileIds = new ReadOnlyCollection<string>(operationTileIds.OrderBy(item => item, StringComparer.Ordinal).ToList());
        DestinationSlots = new ReadOnlyCollection<ReferenceFirstFitPeSlot>(destinationSlots.OrderBy(item => item.PeOrdinal).ToList());
    }

    /// <summary>Gets the stable typed intent identity.</summary>
    public string IntentId { get; }
    /// <summary>Gets the K-division index.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the output-column block index.</summary>
    public int NBlockIndex { get; }
    /// <summary>Gets the selected typed cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the component having the manifest MeshRouter role for this cluster.</summary>
    public string IngressComponentId { get; }
    /// <summary>Gets the unique highest-level component having the manifest TreeRouter role.</summary>
    public string BranchRootComponentId { get; }
    /// <summary>Gets the lowered operations whose activation reaches this branch.</summary>
    public IReadOnlyList<string> OperationTileIds { get; }
    /// <summary>Gets branch destinations in semantic PE order.</summary>
    public IReadOnlyList<ReferenceFirstFitPeSlot> DestinationSlots { get; }
}

/// <summary>Describes deterministic partial-result reduction and landing for one allocated block.</summary>
public sealed class ReferenceFirstFitResultIntent
{
    internal ReferenceFirstFitResultIntent(
        string intentId,
        int divisionIndex,
        int nBlockIndex,
        int clusterIndex,
        string reductionRootComponentId,
        string outputComponentId,
        ReferenceFirstFitPeSlot landingSlot,
        ReferenceFirstFitPeSlot referenceBackCodeSlot,
        IEnumerable<string> contributorOperationTileIds)
    {
        IntentId = intentId;
        DivisionIndex = divisionIndex;
        NBlockIndex = nBlockIndex;
        ClusterIndex = clusterIndex;
        ReductionRootComponentId = reductionRootComponentId;
        OutputComponentId = outputComponentId;
        LandingSlot = landingSlot;
        ReferenceBackCodeSlot = referenceBackCodeSlot;
        ContributorOperationTileIds = new ReadOnlyCollection<string>(contributorOperationTileIds.OrderBy(item => item, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the stable typed intent identity.</summary>
    public string IntentId { get; }
    /// <summary>Gets the K-division index.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the output-column block index.</summary>
    public int NBlockIndex { get; }
    /// <summary>Gets the selected typed cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the unique highest-level component having the TreeReductionUnit role.</summary>
    public string ReductionRootComponentId { get; }
    /// <summary>Gets the typed MeshRouter that receives the cluster result.</summary>
    public string OutputComponentId { get; }
    /// <summary>Gets the current block's lowest-ordinal PE used as its result landing.</summary>
    public ReferenceFirstFitPeSlot LandingSlot { get; }
    /// <summary>Gets the Python-reference PE back-code target, which may reuse the cluster's first allocation root.</summary>
    public ReferenceFirstFitPeSlot ReferenceBackCodeSlot { get; }
    /// <summary>Gets contributing lowered operation tiles in deterministic order.</summary>
    public IReadOnlyList<string> ContributorOperationTileIds { get; }
}

/// <summary>Explicitly binds one mapped operation tile to its exact weight artifact and PE.</summary>
/// <param name="OperationTileId">Lowered operation tile consuming the weight.</param>
/// <param name="WeightTileId">Exact lowered weight tile.</param>
/// <param name="ArtifactHash">Lowercase SHA-256 identity of the source artifact.</param>
/// <param name="TargetComponentId">Opaque target PE binding.</param>
/// <param name="ClusterIndex">Typed target cluster index.</param>
/// <param name="PeOrdinal">Semantic PE ordinal within the cluster.</param>
public sealed record ReferenceFirstFitWeightBindingIntent(
    string OperationTileId,
    string WeightTileId,
    string ArtifactHash,
    string TargetComponentId,
    int ClusterIndex,
    int PeOrdinal);

/// <summary>Contains a complete immutable reference first-fit candidate.</summary>
public sealed class ReferenceFirstFitMappingPlan
{
    internal ReferenceFirstFitMappingPlan(
        IEnumerable<ReferenceFirstFitPeSlot> peSlots,
        IEnumerable<ReferenceFirstFitDivisionAllocation> divisions,
        IEnumerable<OperationTileAssignment> assignments,
        IEnumerable<ReferenceFirstFitWeightBindingIntent> weightBindings,
        IEnumerable<ReferenceFirstFitBranchIntent> branchIntents,
        IEnumerable<ReferenceFirstFitResultIntent> resultIntents,
        Phase8AWeightPlacementPlan weightPlacementPlan,
        MappingCandidate candidate,
        string semanticAllocationHash,
        string canonicalHash)
    {
        PeSlots = new ReadOnlyCollection<ReferenceFirstFitPeSlot>(peSlots.OrderBy(item => item.ClusterIndex).ThenBy(item => item.PeOrdinal).ToList());
        Divisions = new ReadOnlyCollection<ReferenceFirstFitDivisionAllocation>(divisions.OrderBy(item => item.DivisionIndex).ToList());
        Assignments = new ReadOnlyCollection<OperationTileAssignment>(assignments.OrderBy(item => item.AssignmentId, StringComparer.Ordinal).ToList());
        WeightBindings = new ReadOnlyCollection<ReferenceFirstFitWeightBindingIntent>(weightBindings.OrderBy(item => item.OperationTileId, StringComparer.Ordinal).ToList());
        BranchIntents = new ReadOnlyCollection<ReferenceFirstFitBranchIntent>(branchIntents.OrderBy(item => item.DivisionIndex).ThenBy(item => item.NBlockIndex).ToList());
        ResultIntents = new ReadOnlyCollection<ReferenceFirstFitResultIntent>(resultIntents.OrderBy(item => item.DivisionIndex).ThenBy(item => item.NBlockIndex).ToList());
        WeightPlacementPlan = weightPlacementPlan ?? throw new ArgumentNullException(nameof(weightPlacementPlan));
        Candidate = candidate;
        SemanticAllocationHash = semanticAllocationHash;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Identifies the complete plan hash projection, including opaque execution bindings.</summary>
    public const string CanonicalHashAlgorithm = "sha256/reference-first-fit-mapping/v1";
    /// <summary>Identifies the rename-independent semantic allocation projection.</summary>
    public const string SemanticAllocationHashAlgorithm = "sha256/reference-first-fit-semantic-allocation/v1";

    /// <summary>Gets all available PE slots in typed semantic order.</summary>
    public IReadOnlyList<ReferenceFirstFitPeSlot> PeSlots { get; }
    /// <summary>Gets committed K divisions in increasing K-offset order.</summary>
    public IReadOnlyList<ReferenceFirstFitDivisionAllocation> Divisions { get; }
    /// <summary>Gets Mapping 2.0-ready operation assignments.</summary>
    public IReadOnlyList<OperationTileAssignment> Assignments { get; }
    /// <summary>Gets explicit artifact-to-weight-tile-to-PE bindings for the placement composition layer.</summary>
    public IReadOnlyList<ReferenceFirstFitWeightBindingIntent> WeightBindings { get; }
    /// <summary>Gets typed activation branch intents derived from manifest roles.</summary>
    public IReadOnlyList<ReferenceFirstFitBranchIntent> BranchIntents { get; }
    /// <summary>Gets typed result reduction and landing intents derived from manifest roles.</summary>
    public IReadOnlyList<ReferenceFirstFitResultIntent> ResultIntents { get; }
    /// <summary>Gets authoritative immutable StorageMap reservations and preload, write, commit, or reuse lifecycle.</summary>
    public Phase8AWeightPlacementPlan WeightPlacementPlan { get; }
    /// <summary>Gets the Mapping 2.0 candidate descriptor and deterministic tie-break.</summary>
    public MappingCandidate Candidate { get; }
    /// <summary>Gets the hash of coordinates, ranges, ordinals, and occupancy without opaque component identities.</summary>
    public string SemanticAllocationHash { get; }
    /// <summary>Gets the complete plan hash including frozen topology, capability, assignment, and artifact bindings.</summary>
    public string CanonicalHash { get; }
}

/// <summary>Returns either one complete immutable plan or deterministic structured hard failures.</summary>
public sealed class ReferenceFirstFitMappingResult
{
    internal ReferenceFirstFitMappingResult(ReferenceFirstFitMappingPlan? plan, IEnumerable<WorkloadMappingV2Issue> issues)
    {
        Plan = plan;
        Issues = new ReadOnlyCollection<WorkloadMappingV2Issue>(issues
            .OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RelatedId, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Gets the complete plan when no hard failure occurred.</summary>
    public ReferenceFirstFitMappingPlan? Plan { get; }
    /// <summary>Gets deterministic structured hard failures.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    /// <summary>Gets whether a complete all-or-nothing plan was produced.</summary>
    public bool IsSuccess => Plan is not null && Issues.All(item => item.Severity != ValidationSeverity.Error);
}

/// <summary>Implements the deterministic contiguous segment first-fit subset of the Python MoT reference mapper.</summary>
public static class ReferenceFirstFitMapper
{
    /// <summary>Maps a frozen request without consulting component ids, names, parameters, or ComponentKind for hierarchy.</summary>
    public static ReferenceFirstFitMappingResult Map(ReferenceFirstFitMappingRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        try
        {
            var validationIssues = ValidateReferenceRequest(request.ReferenceRequest);
            if (validationIssues.Count > 0) return Failure(validationIssues);
            if (!string.Equals(request.ReferenceRequest.PolicyId, ReferenceMappingPolicyIds.MotReferenceFirstFitV1, StringComparison.Ordinal))
                return Failure(Error(ReferenceFirstFitIssueCodes.UnsupportedPolicy, "$.referenceRequest.policy_id",
                    "The reference first-fit mapper accepts only mot-reference-first-fit-v1."));
            if (!string.Equals(request.ReferenceRequest.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal))
                return Failure(Error(ReferenceFirstFitIssueCodes.UnsupportedTopology, "$.referenceRequest.topology_id",
                    "The reference first-fit subset accepts only the Mesh-of-Trees topology."));

            var graph = request.CloneHardwareGraph();
            var read = TopologyManifestJson.ReadFromGraph(graph);
            if (read.Manifest is null || read.Issues.Count > 0)
            {
                return Failure(read.Issues.Select(issue => new WorkloadMappingV2Issue(
                    issue.Code,
                    ValidationSeverity.Error,
                    issue.Location,
                    issue.Message)));
            }
            var manifest = read.Manifest;
            var inputIssues = ValidateFrozenInputs(request, manifest);
            if (inputIssues.Count > 0) return Failure(inputIssues);

            var slotResult = DiscoverSemanticSlots(manifest);
            if (slotResult.Issues.Count > 0) return Failure(slotResult.Issues);
            var slots = slotResult.Slots;
            var capabilityInventory = DiscoverPeCapabilities(request.CapabilitySnapshot, slots);
            if (capabilityInventory.Issues.Count > 0) return Failure(capabilityInventory.Issues);
            var clusterSize = request.ReferenceRequest.ClusterSize;
            var pePerBlock = checked(request.ReferenceRequest.RowDivisionSize / request.ReferenceRequest.ComputeSize.Rows);
            var segmentSize = NextPowerOfTwo(pePerBlock);
            if (pePerBlock <= 0 || segmentSize <= 0 || segmentSize > clusterSize || clusterSize % segmentSize != 0)
                return Failure(Error(ReferenceFirstFitIssueCodes.SegmentInvalid, "$.referenceRequest.row_division_size",
                    "The reference-compatible subset requires a positive power-of-two segment no larger than, and exactly dividing, cluster_size."));

            var byCluster = slots.GroupBy(item => item.ClusterIndex).ToDictionary(group => group.Key, group => group.OrderBy(item => item.PeOrdinal).ToArray());
            var utilization = byCluster.Keys.OrderBy(item => item).ToDictionary(item => item, _ => new bool[clusterSize]);
            var operation = request.ReferenceRequest.Workload.Operations[0];
            var divisionCount = checked(operation.Geometry.K / request.ReferenceRequest.RowDivisionSize);
            var nBlockCount = CeilingDiv(operation.Geometry.N, request.ReferenceRequest.ComputeSize.Columns);
            var divisions = new List<ReferenceFirstFitDivisionAllocation>();
            var allBlocks = new Dictionary<(int Division, int NBlock), ReferenceFirstFitBlockAllocation>();

            for (var divisionIndex = 0; divisionIndex < divisionCount; divisionIndex++)
            {
                var divisionBlocks = new List<ReferenceFirstFitBlockAllocation>();
                var kRange = new MappingIndexRange(
                    checked((long)divisionIndex * request.ReferenceRequest.RowDivisionSize),
                    request.ReferenceRequest.RowDivisionSize);
                for (var nBlockIndex = 0; nBlockIndex < nBlockCount; nBlockIndex++)
                {
                    var nOffset = checked((long)nBlockIndex * request.ReferenceRequest.ComputeSize.Columns);
                    var nRange = new MappingIndexRange(nOffset, Math.Min(request.ReferenceRequest.ComputeSize.Columns, operation.Geometry.N - nOffset));
                    ReferenceFirstFitBlockAllocation? allocated = null;
                    foreach (var clusterIndex in utilization.Keys.OrderBy(item => item))
                    {
                        var trial = (bool[])utilization[clusterIndex].Clone();
                        if (!TryReserveSegment(trial, pePerBlock, segmentSize, out var ordinals, out var segmentOrdinal)) continue;
                        utilization[clusterIndex] = trial;
                        allocated = new ReferenceFirstFitBlockAllocation(
                            divisionIndex,
                            nBlockIndex,
                            kRange,
                            nRange,
                            request.ReferenceRequest.ComputeSize.Columns,
                            clusterIndex,
                            segmentOrdinal,
                            Bitset(trial),
                            ordinals.Select(ordinal => byCluster[clusterIndex][ordinal]));
                        break;
                    }
                    if (allocated is null)
                    {
                        return Failure(
                            Error(ReferenceFirstFitIssueCodes.CapacityExceeded, "$.allocation",
                                $"No cluster can reserve {pePerBlock} contiguous PE slots for division {divisionIndex}, N block {nBlockIndex}."),
                            Error(ReferenceFirstFitIssueCodes.NoProgress, "$.allocation",
                                "First-fit visited every semantic cluster without a complete temporary reservation; no partial state was committed."));
                    }
                    divisionBlocks.Add(allocated);
                    allBlocks[(divisionIndex, nBlockIndex)] = allocated;
                }
                divisions.Add(new ReferenceFirstFitDivisionAllocation(
                    divisionIndex,
                    kRange,
                    divisionBlocks,
                    utilization.OrderBy(item => item.Key).Select(item => new ReferenceFirstFitClusterUtilization(item.Key, Bitset(item.Value)))));
            }

            var bindingResult = BindAssignments(request, operation, capabilityInventory.Capabilities, allBlocks, manifest);
            if (bindingResult.Issues.Count > 0) return Failure(bindingResult.Issues);

            var weightPlacementResult = Phase8AWeightPlacementPlanner.Plan(new Phase8AWeightPlacementRequest(
                request.CapabilitySnapshot,
                bindingResult.Assignments,
                request.LoweringPlan.OperandTiles.Where(item => item.RoleId == Phase8ATensorRoleIds.Weight),
                request.ArtifactUses));
            if (!weightPlacementResult.IsSuccess) return Failure(weightPlacementResult.Issues);
            var weightPlacementPlan = weightPlacementResult.Plan!;

            var semanticHash = Hash(new
            {
                algorithm = ReferenceFirstFitMappingPlan.SemanticAllocationHashAlgorithm,
                normalizedInput = request.ReferenceRequest.ComputeCanonicalHash().Hash,
                slots = slots.Select(slot => new { slot.ClusterIndex, slot.PeOrdinal, slot.CoordinateRow, slot.CoordinateColumn }),
                divisions = divisions.Select(division => new
                {
                    division.DivisionIndex,
                    division.KRange,
                    blocks = division.Blocks.Select(block => new
                    {
                        block.NBlockIndex,
                        block.NRange,
                        block.PaddedNExtent,
                        block.ClusterIndex,
                        block.SegmentOrdinal,
                        ordinals = block.PeSlots.Select(slot => slot.PeOrdinal),
                        block.UtilizationBitsetAfter
                    }),
                    utilization = division.ClusterUtilizationAfter
                })
            });
            var profileHash = Hash(new
            {
                algorithm = "sha256/reference-first-fit-profiles/v1",
                profiles = slots.Select(slot => new
                {
                    slot.ClusterIndex,
                    slot.PeOrdinal,
                    capability = capabilityInventory.Capabilities[slot.ComponentId].ProfileHash
                })
            });
            var policyConfigHash = Hash(new
            {
                algorithm = "sha256/reference-first-fit-policy-config/v1",
                request = request.ReferenceRequest.ComputeCanonicalHash().Hash,
                lowering = request.LoweringPlan.CanonicalHash,
                artifacts = request.ArtifactUses.Select(use => new { use.OperationId, use.TensorId, use.ArtifactHash, use.PrecisionId, use.ConsumerOperationTileIds })
            });
            var used = utilization.Values.Sum(bits => bits.Count(value => value));
            var total = checked(utilization.Count * clusterSize);
            var candidate = new MappingCandidate(
                "mot-reference-first-fit:" + semanticHash[..16],
                ReferenceMappingPolicyIds.MotReferenceFirstFitV1,
                policyConfigHash,
                [],
                [
                    new MappingScoreItem("allocated-pe-slots", used, 1m, used, "slots", ReferenceMappingPolicyIds.MotReferenceFirstFitV1),
                    new MappingScoreItem("pe-utilization", total == 0 ? 0m : (decimal)used / total, 1m, total == 0 ? 0m : (decimal)used / total, "ratio", ReferenceMappingPolicyIds.MotReferenceFirstFitV1)
                ],
                manifest.TopologyGraphHash,
                manifest.RouteHash,
                profileHash,
                semanticHash,
                []);
            var canonicalHash = Hash(new
            {
                algorithm = ReferenceFirstFitMappingPlan.CanonicalHashAlgorithm,
                semanticHash,
                topology = manifest.CanonicalHash,
                capability = request.CapabilitySnapshot.SnapshotId,
                assignments = bindingResult.Assignments,
                bindings = bindingResult.WeightBindings,
                branches = bindingResult.BranchIntents,
                results = bindingResult.ResultIntents,
                weightPlacement = weightPlacementPlan.CanonicalHash,
                candidate
            });
            return new ReferenceFirstFitMappingResult(new ReferenceFirstFitMappingPlan(
                slots,
                divisions,
                bindingResult.Assignments,
                bindingResult.WeightBindings,
                bindingResult.BranchIntents,
                bindingResult.ResultIntents,
                weightPlacementPlan,
                candidate,
                semanticHash,
                canonicalHash), []);
        }
        catch (OverflowException)
        {
            return Failure(Error(ReferenceFirstFitIssueCodes.ArithmeticOverflow, "$.allocation",
                "Reference first-fit count, range, or hash projection arithmetic exceeded the supported range."));
        }
    }

    private static List<WorkloadMappingV2Issue> ValidateReferenceRequest(ReferenceMappingRequest request) => request.Validate().Issues
        .Where(issue => issue.Severity == ValidationSeverity.Error)
        .Select(issue => new WorkloadMappingV2Issue(issue.Code, issue.Severity, issue.Location, issue.Message, issue.RelatedId))
        .ToList();

    private static List<WorkloadMappingV2Issue> ValidateFrozenInputs(ReferenceFirstFitMappingRequest request, TopologyManifest manifest)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        var reference = request.ReferenceRequest;
        if (!string.Equals(manifest.Request.TopologyId, reference.TopologyId, StringComparison.Ordinal) ||
            manifest.Request.MeshRows != reference.MeshSize.Rows ||
            manifest.Request.MeshColumns != reference.MeshSize.Columns ||
            manifest.Request.ClusterSize != reference.ClusterSize)
            issues.Add(Error(ReferenceFirstFitIssueCodes.RequestTopologyMismatch, "$.referenceRequest",
                "Reference topology id, mesh size, and cluster size must exactly match the persisted typed topology manifest."));
        if (!string.Equals(request.CapabilitySnapshot.HardwareGraphHash, manifest.TopologyGraphHash, StringComparison.Ordinal))
            issues.Add(Error(ReferenceFirstFitIssueCodes.CapabilityTopologyHashMismatch, "$.capabilitySnapshot.hardwareGraphHash",
                "Capability topology hash differs from the current typed topology manifest."));
        if (!string.Equals(request.CapabilitySnapshot.PlacementHash, manifest.PlacementHash, StringComparison.Ordinal))
            issues.Add(Error(ReferenceFirstFitIssueCodes.CapabilityPlacementHashMismatch, "$.capabilitySnapshot.placementHash",
                "Capability placement hash differs from the current typed topology manifest."));
        if (!string.Equals(request.CapabilitySnapshot.RouteHash, manifest.RouteHash, StringComparison.Ordinal))
            issues.Add(Error(ReferenceFirstFitIssueCodes.CapabilityRouteHashMismatch, "$.capabilitySnapshot.routeHash",
                "Capability route hash differs from the current typed topology manifest."));

        var operation = reference.Workload.Operations.Count == 1 ? reference.Workload.Operations[0] : null;
        if (operation is null || !string.Equals(operation.OperationId, request.LoweringPlan.OperationId, StringComparison.Ordinal))
            issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.operationId",
                "The reference first-fit subset maps exactly one workload operation and requires the lowering plan to identify it."));
        else if (operation.Geometry.M != 1)
            issues.Add(Error(ReferenceFirstFitIssueCodes.UnsupportedMExtent, "$.referenceRequest.workload.operations.geometry.m",
                "mot-reference-first-fit-v1 supports exactly M=1; wider batch or row extents require a different mapping policy.", operation.OperationId));
        if (issues.Count > 0 || operation is null) return issues;

        var tensorIds = operation.Tensors.Select(item => item.TensorId).ToHashSet(StringComparer.Ordinal);
        var precisionId = operation.Precision.ToString();
        var operandGroups = request.LoweringPlan.OperandTiles.GroupBy(item => item.TileId, StringComparer.Ordinal).ToArray();
        var outputGroups = request.LoweringPlan.OutputTiles.GroupBy(item => item.TileId, StringComparer.Ordinal).ToArray();
        var operationGroups = request.LoweringPlan.OperationTiles.GroupBy(item => item.OperationTileId, StringComparer.Ordinal).ToArray();
        if (operandGroups.Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1) ||
            outputGroups.Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1) ||
            operationGroups.Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1))
        {
            issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan",
                "Lowered operand, output, and operation tile identities must be non-empty and unique before indexing."));
            return issues;
        }
        var operands = operandGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var outputs = outputGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var weightTiles = operands.Values.Where(item => item.RoleId == Phase8ATensorRoleIds.Weight)
            .ToDictionary(item => item.TileId, StringComparer.Ordinal);
        if (operationGroups.Length == 0 || operands.Count == 0 || outputs.Count == 0)
            issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan",
                "Reference first-fit requires concrete operation, operand, and output tiles."));

        foreach (var operand in operands.Values)
        {
            if (!tensorIds.Contains(operand.TensorId) || !string.Equals(operand.PrecisionId, precisionId, StringComparison.Ordinal))
                issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.operandTiles",
                    "Every lowered operand must retain a normalized tensor identity and the normalized concrete precision.", operand.TileId));
        }
        foreach (var output in outputs.Values)
        {
            if (!tensorIds.Contains(output.TensorId) || !string.Equals(output.PrecisionId, precisionId, StringComparison.Ordinal) ||
                output.ValidShape.Dimensions.Any(dimension => dimension <= 0) || output.PaddedShape.Dimensions.Any(dimension => dimension <= 0))
                issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.outputTiles",
                    "Every lowered output must retain normalized tensor/precision identity and positive valid and padded shapes.", output.TileId));
        }

        if (request.LoweringPlan.CollectiveIntents.Any(item =>
                !string.Equals(item.PrecisionId, precisionId, StringComparison.Ordinal) ||
                item.MRange.Offset < 0 || item.MRange.Extent <= 0 ||
                item.NRange.Offset < 0 || item.NRange.Extent <= 0))
            issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.collectiveIntents",
                "Collective intents must preserve normalized precision and positive output ranges."));

        foreach (var tile in operationGroups.Select(group => group.First()))
        {
            if (!string.Equals(tile.OperationId, operation.OperationId, StringComparison.Ordinal) ||
                tile.MRange != new MappingIndexRange(0, 1) ||
                tile.KRange.Offset < 0 || tile.KRange.Extent != reference.ComputeSize.Rows ||
                tile.KRange.Offset % reference.ComputeSize.Rows != 0 ||
                tile.NRange.Offset < 0 || tile.NRange.Extent <= 0 || tile.NRange.Extent > reference.ComputeSize.Columns ||
                tile.NRange.Offset % reference.ComputeSize.Columns != 0 ||
                !DimensionsEqual(tile.ValidShape, 1, tile.NRange.Extent) ||
                !DimensionsEqual(tile.PaddedShape, 1, reference.ComputeSize.Rows, reference.ComputeSize.Columns) ||
                !operands.TryGetValue(tile.ActivationTileId, out var activation) ||
                !operands.TryGetValue(tile.WeightTileId, out var weight) ||
                !outputs.TryGetValue(tile.OutputTileId, out var output) ||
                !ValidActivationTile(activation, tile, precisionId, reference.ComputeSize.Rows) ||
                !ValidWeightTile(weight, tile, precisionId, reference.ComputeSize.Rows, reference.ComputeSize.Columns) ||
                !ValidPartialOutputTile(output, tile, precisionId, reference.ComputeSize.Columns))
                issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.operationTiles",
                    "Each issue must exactly bind normalized activation/weight/partial tensors with M=1 and compute-size valid/padded shapes.", tile.OperationTileId));
        }

        var tiles = operationGroups.Select(group => group.First()).ToArray();
        var maxK = tiles.Length == 0 ? 0 : tiles.Max(item => checked(item.KRange.Offset + item.KRange.Extent));
        var maxN = tiles.Length == 0 ? 0 : tiles.Max(item => checked(item.NRange.Offset + item.NRange.Extent));
        var maxM = tiles.Length == 0 ? 0 : tiles.Max(item => checked(item.MRange.Offset + item.MRange.Extent));
        if (maxM != operation.Geometry.M || maxK != operation.Geometry.K || maxN != operation.Geometry.N)
            issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.operationTiles",
                "Lowered M/K/N ranges must cover the complete normalized operation geometry."));
        issues.AddRange(ValidateArtifactUses(request, operation, weightTiles));
        return issues;
    }

    private static bool ValidActivationTile(
        Phase8ALoweredOperandTile? operand,
        Phase8ALoweredOperationTile operation,
        string precisionId,
        long computeRows) => operand is not null &&
        string.Equals(operand.RoleId, Phase8ATensorRoleIds.Activation, StringComparison.Ordinal) &&
        string.Equals(operand.PrecisionId, precisionId, StringComparison.Ordinal) &&
        operand.MRange == operation.MRange && operand.KRange == operation.KRange && operand.NRange == new MappingIndexRange(0, 0) &&
        DimensionsEqual(operand.ValidShape, 1, operation.KRange.Extent) &&
        DimensionsEqual(operand.PaddedShape, 1, computeRows);

    private static bool ValidWeightTile(
        Phase8ALoweredOperandTile? operand,
        Phase8ALoweredOperationTile operation,
        string precisionId,
        long computeRows,
        long computeColumns) => operand is not null &&
        string.Equals(operand.RoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal) &&
        string.Equals(operand.PrecisionId, precisionId, StringComparison.Ordinal) &&
        operand.MRange == new MappingIndexRange(0, 0) && operand.KRange == operation.KRange && operand.NRange == operation.NRange &&
        DimensionsEqual(operand.ValidShape, operation.KRange.Extent, operation.NRange.Extent) &&
        DimensionsEqual(operand.PaddedShape, computeRows, computeColumns);

    private static bool ValidPartialOutputTile(
        Phase8ALoweredOutputTile? output,
        Phase8ALoweredOperationTile operation,
        string precisionId,
        long computeColumns) => output is not null &&
        string.Equals(output.RoleId, Phase8ATensorRoleIds.PartialOutput, StringComparison.Ordinal) &&
        string.Equals(output.PrecisionId, precisionId, StringComparison.Ordinal) &&
        output.MRange == operation.MRange && output.NRange == operation.NRange &&
        DimensionsEqual(output.ValidShape, 1, operation.NRange.Extent) &&
        DimensionsEqual(output.PaddedShape, 1, computeColumns);

    private static bool DimensionsEqual(MappingShape shape, params long[] expected) =>
        shape.Dimensions.SequenceEqual(expected);
    private static IEnumerable<WorkloadMappingV2Issue> ValidateArtifactUses(
        ReferenceFirstFitMappingRequest request,
        ReferenceMappingWorkloadOperation operation,
        IReadOnlyDictionary<string, Phase8ALoweredOperandTile> weightTiles)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        var operationIds = request.LoweringPlan.OperationTiles.Select(item => item.OperationTileId).ToHashSet(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var use in request.ArtifactUses)
        {
            if (!string.Equals(use.OperationId, operation.OperationId, StringComparison.Ordinal) ||
                !IsLowerSha256(use.ArtifactHash) || string.IsNullOrWhiteSpace(use.TensorId) || string.IsNullOrWhiteSpace(use.PrecisionId) ||
                use.ConsumerOperationTileIds.Count == 0)
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.ArtifactUseInvalid, "$.artifactUses",
                    "Each explicit use requires the mapped operation, tensor, concrete precision, lowercase SHA-256 artifact, and consumers.", use.TensorId));
                continue;
            }
            var tensor = operation.Tensors.FirstOrDefault(item => item.TensorId == use.TensorId);
            var resolvedArtifactHash = tensor is null
                ? null
                : Phase8AWeightResidencyKey.ResolveArtifactHash(tensor);
            if (resolvedArtifactHash is null ||
                !string.Equals(resolvedArtifactHash, use.ArtifactHash, StringComparison.Ordinal))
                issues.Add(Error(ReferenceFirstFitIssueCodes.ArtifactUseInvalid, "$.artifactUses",
                    "Explicit weight use must resolve the normalized workload tensor and exact artifact hash.", use.TensorId));
            foreach (var consumerId in use.ConsumerOperationTileIds)
            {
                var tile = request.LoweringPlan.OperationTiles.FirstOrDefault(item => item.OperationTileId == consumerId);
                if (tile is null || !operationIds.Contains(consumerId) || !covered.Add(consumerId) ||
                    !weightTiles.TryGetValue(tile.WeightTileId, out var weight) ||
                    !string.Equals(weight.TensorId, use.TensorId, StringComparison.Ordinal) ||
                    !string.Equals(weight.PrecisionId, use.PrecisionId, StringComparison.Ordinal))
                    issues.Add(Error(ReferenceFirstFitIssueCodes.ArtifactUseInvalid, "$.artifactUses.consumerOperationTileIds",
                        "Every lowered operation tile must be covered exactly once by its matching typed weight artifact use.", consumerId));
            }
        }
        if (!covered.SetEquals(operationIds))
            issues.Add(Error(ReferenceFirstFitIssueCodes.ArtifactUseInvalid, "$.artifactUses.consumerOperationTileIds",
                "Explicit weight artifact uses must cover every lowered operation tile exactly once."));
        return issues;
    }

    private static SlotDiscoveryResult DiscoverSemanticSlots(TopologyManifest manifest)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        var pes = manifest.Components.Where(item => item.Role == TopologyPresetComponentRole.ProcessingElement).ToArray();
        var expectedClusterCount = checked((int)manifest.Request.ClusterCount);
        var actualClusterIndexes = pes.Where(item => item.ClusterIndex.HasValue)
            .Select(item => item.ClusterIndex!.Value)
            .Distinct()
            .OrderBy(item => item)
            .ToArray();
        if (pes.Any(item => item.ClusterIndex is null || item.ClusterIndex < 0) ||
            pes.GroupBy(item => item.ClusterIndex).Any(group => group.Count() != manifest.Request.ClusterSize) ||
            !actualClusterIndexes.SequenceEqual(Enumerable.Range(0, expectedClusterCount)))
            issues.Add(Error(ReferenceFirstFitIssueCodes.InvalidProcessingElementManifest, "$.topologyManifest.components",
                "Canonical clusters must be exactly the contiguous range 0..cluster_count-1 and each expose exactly cluster_size typed processing elements."));
        if (pes.GroupBy(item => (item.ClusterIndex, item.Coordinate.Row, item.Coordinate.Column)).Any(group => group.Count() > 1))
            issues.Add(Error(ReferenceFirstFitIssueCodes.DuplicateProcessingElementCoordinate, "$.topologyManifest.components",
                "Processing-element coordinates must be unique within each typed cluster."));
        if (issues.Count > 0) return new SlotDiscoveryResult([], issues);
        var slots = pes
            .OrderBy(item => item.ClusterIndex)
            .ThenBy(item => item.Coordinate.Row)
            .ThenBy(item => item.Coordinate.Column)
            .GroupBy(item => item.ClusterIndex!.Value)
            .SelectMany(group => group.Select((item, ordinal) => new ReferenceFirstFitPeSlot(
                group.Key, ordinal, item.Coordinate.Row, item.Coordinate.Column, item.ComponentId)))
            .ToList();
        return new SlotDiscoveryResult(slots, []);
    }

    private static CapabilityInventoryResult DiscoverPeCapabilities(
        CapabilitySnapshot snapshot,
        IReadOnlyList<ReferenceFirstFitPeSlot> slots)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        var groups = snapshot.Components.GroupBy(item => item.ComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var capabilities = new Dictionary<string, ComponentCapabilitySnapshot>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (!groups.TryGetValue(slot.ComponentId, out var matches) || matches.Length == 0)
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.CapabilityMissing, "$.capabilitySnapshot.components",
                    "Every typed manifest PE requires exactly one frozen compiled capability.", slot.ComponentId));
                continue;
            }
            if (matches.Length != 1)
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.CapabilityInventoryInvalid, "$.capabilitySnapshot.components",
                    "A typed manifest PE resolves more than one frozen compiled capability; ambiguous inventory is forbidden.", slot.ComponentId));
                continue;
            }
            capabilities.Add(slot.ComponentId, matches[0]);
        }
        return issues.Count > 0
            ? new CapabilityInventoryResult(new ReadOnlyDictionary<string, ComponentCapabilitySnapshot>(new Dictionary<string, ComponentCapabilitySnapshot>()), issues)
            : new CapabilityInventoryResult(new ReadOnlyDictionary<string, ComponentCapabilitySnapshot>(capabilities), []);
    }
    private static BindingResult BindAssignments(
        ReferenceFirstFitMappingRequest request,
        ReferenceMappingWorkloadOperation operation,
        IReadOnlyDictionary<string, ComponentCapabilitySnapshot> capabilities,
        IReadOnlyDictionary<(int Division, int NBlock), ReferenceFirstFitBlockAllocation> blocks,
        TopologyManifest manifest)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        var useGroups = request.ArtifactUses
            .SelectMany(use => use.ConsumerOperationTileIds.Select(id => (Id: id, Use: use)))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var weightGroups = request.LoweringPlan.OperandTiles
            .Where(item => item.RoleId == Phase8ATensorRoleIds.Weight)
            .GroupBy(item => item.TileId, StringComparer.Ordinal)
            .ToArray();
        if (useGroups.Any(group => group.Count() != 1) || weightGroups.Any(group => group.Count() != 1))
            return new BindingResult([], [], [], [],
            [
                Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan",
                    "Validated artifact consumers and lowered weight tile identities must remain unique before binding.")
            ]);
        var uses = useGroups.ToDictionary(group => group.Key, group => group.First().Use, StringComparer.Ordinal);
        var weightTiles = weightGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var assignments = new List<OperationTileAssignment>();
        var bindings = new List<ReferenceFirstFitWeightBindingIntent>();
        foreach (var tile in request.LoweringPlan.OperationTiles
                     .OrderBy(item => item.KRange.Offset)
                     .ThenBy(item => item.NRange.Offset)
                     .ThenBy(item => item.MRange.Offset)
                     .ThenBy(item => item.OperationTileId, StringComparer.Ordinal))
        {
            var divisionIndex = checked((int)(tile.KRange.Offset / request.ReferenceRequest.RowDivisionSize));
            var nBlockIndex = checked((int)(tile.NRange.Offset / request.ReferenceRequest.ComputeSize.Columns));
            if (!blocks.TryGetValue((divisionIndex, nBlockIndex), out var block))
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.operationTiles",
                    "Operation tile does not resolve a previously completed reference allocation block.", tile.OperationTileId));
                continue;
            }
            var localK = checked((int)((tile.KRange.Offset - block.KRange.Offset) / request.ReferenceRequest.ComputeSize.Rows));
            if (localK < 0 || localK >= block.PeSlots.Count)
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.LoweringMismatch, "$.loweringPlan.operationTiles",
                    "Operation tile K offset does not resolve one PE inside its row division.", tile.OperationTileId));
                continue;
            }
            var slot = block.PeSlots[localK];
            if (!capabilities.TryGetValue(slot.ComponentId, out var capability) ||
                !weightTiles.TryGetValue(tile.WeightTileId, out var weight) ||
                !uses.TryGetValue(tile.OperationTileId, out var use))
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.CapabilityInventoryInvalid, "$.capabilitySnapshot.components",
                    "Validated PE capability, weight tile, or artifact binding became unresolved before assignment.", slot.ComponentId));
                continue;
            }
            if (!TrySelectTargetPort(capability, operation, weight, request.ReferenceRequest, out var preloadPortId, out var capabilityIssue))
            {
                issues.Add(capabilityIssue!);
                continue;
            }
            assignments.Add(tile.CreateAssignment(tile.OperationTileId, slot.ComponentId, preloadPortId));
            bindings.Add(new ReferenceFirstFitWeightBindingIntent(
                tile.OperationTileId,
                tile.WeightTileId,
                use.ArtifactHash,
                slot.ComponentId,
                slot.ClusterIndex,
                slot.PeOrdinal));
        }
        if (issues.Count > 0) return new BindingResult([], [], [], [], issues);

        var branches = new List<ReferenceFirstFitBranchIntent>();
        var results = new List<ReferenceFirstFitResultIntent>();
        var referenceBackCodeRoots = blocks.Values
            .GroupBy(block => block.ClusterIndex)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var firstDivision = group.Min(block => block.DivisionIndex);
                    var roots = group
                        .Where(block => block.DivisionIndex == firstDivision)
                        .OrderBy(block => block.NBlockIndex)
                        .Select(block => block.PeSlots[0])
                        .ToArray();
                    return (FirstDivision: firstDivision, Roots: roots);
                });
        foreach (var pair in blocks.OrderBy(item => item.Key.Division).ThenBy(item => item.Key.NBlock))
        {
            var block = pair.Value;
            var components = manifest.Components.Where(item => item.ClusterIndex == block.ClusterIndex).ToArray();
            var ingress = UniqueRole(components, TopologyPresetComponentRole.MeshRouter);
            var branchRoot = UniqueHighestRole(components, TopologyPresetComponentRole.TreeRouter);
            var reductionRoot = UniqueHighestRole(components, TopologyPresetComponentRole.TreeReductionUnit);
            if (ingress is null || branchRoot is null || reductionRoot is null)
            {
                issues.Add(Error(ReferenceFirstFitIssueCodes.InvalidProcessingElementManifest, "$.topologyManifest.components",
                    "Every allocated cluster requires one typed mesh ingress, tree branch root, and tree reduction root.", block.ClusterIndex.ToString(CultureInfo.InvariantCulture)));
                continue;
            }
            var contributors = request.LoweringPlan.OperationTiles
                .Where(tile => tile.KRange.Offset >= block.KRange.Offset && tile.KRange.Offset < block.KRange.Offset + block.KRange.Extent && tile.NRange == block.NRange)
                .Select(tile => tile.OperationTileId)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToList();
            var backCodeState = referenceBackCodeRoots[block.ClusterIndex];
            var referenceBackCodeSlot = block.DivisionIndex == backCodeState.FirstDivision
                ? block.PeSlots[0]
                : backCodeState.Roots[block.PeSlots[0].PeOrdinal % backCodeState.Roots.Length];
            var baseId = $"{request.LoweringPlan.OperationId}:d{block.DivisionIndex}:n{block.NBlockIndex}:c{block.ClusterIndex}";
            branches.Add(new ReferenceFirstFitBranchIntent(baseId + ":branch", block.DivisionIndex, block.NBlockIndex,
                block.ClusterIndex, ingress.ComponentId, branchRoot.ComponentId, contributors, block.PeSlots));
            results.Add(new ReferenceFirstFitResultIntent(baseId + ":result", block.DivisionIndex, block.NBlockIndex,
                block.ClusterIndex, reductionRoot.ComponentId, ingress.ComponentId, block.PeSlots[0], referenceBackCodeSlot, contributors));
        }
        return new BindingResult(assignments, bindings, branches, results, issues);
    }

    private static bool TrySelectTargetPort(
        ComponentCapabilitySnapshot capability,
        ReferenceMappingWorkloadOperation operation,
        Phase8ALoweredOperandTile weight,
        ReferenceMappingRequest reference,
        out string preloadPortId,
        out WorkloadMappingV2Issue? issue)
    {
        preloadPortId = "";
        issue = null;
        var precisionId = operation.Precision.ToString();
        var rawBits = RawWeightBits(weight);
        var shapeCompatible = ShapeLimitAtLeast(capability, Phase8ACapabilityShapeKeys.MatMulMaximumM, operation.Geometry.M) &&
                              ShapeLimitAtLeast(capability, Phase8ACapabilityShapeKeys.MatMulMaximumK, reference.ComputeSize.Rows) &&
                              ShapeLimitAtLeast(capability, Phase8ACapabilityShapeKeys.MatMulMaximumN, reference.ComputeSize.Columns);
        var componentCompatible = capability.OperationKindIds.Contains(operation.OperationTypeId, StringComparer.Ordinal) &&
                                  capability.PrecisionIds.Contains(precisionId, StringComparer.Ordinal) &&
                                  string.Equals(weight.PrecisionId, precisionId, StringComparison.Ordinal) &&
                                  shapeCompatible && capability.CapacityBits > 0 && capability.LatencyCycles >= 0 &&
                                  capability.BandwidthBitsPerCycle > 0 && !string.IsNullOrWhiteSpace(capability.DomainId) &&
                                  !string.Equals(capability.DomainId, "mixed", StringComparison.Ordinal);
        if (!componentCompatible || rawBits is null)
        {
            issue = Error(ReferenceFirstFitIssueCodes.CapabilityUnsupported, "$.capabilitySnapshot.components",
                "Mapped PE must explicitly cover operation, precision, M/K/N shape, positive capacity/bandwidth, latency, and one concrete domain.", capability.ComponentId);
            return false;
        }

        var eligible = capability.StorageCapabilities
            .Select(storage => new
            {
                Storage = storage,
                Ports = capability.Ports.Where(port => string.Equals(port.PortId, storage.PreloadPortId, StringComparison.Ordinal)).ToArray()
            })
            .Where(item => item.Ports.Length == 1 &&
                           string.Equals(item.Ports[0].DirectionId, "input", StringComparison.Ordinal) &&
                           !string.IsNullOrWhiteSpace(item.Ports[0].ProtocolId) &&
                           string.Equals(item.Ports[0].DomainId, capability.DomainId, StringComparison.Ordinal) &&
                           item.Ports[0].BandwidthBitsPerCycle > 0 &&
                           capability.BandwidthBitsPerCycle >= item.Ports[0].BandwidthBitsPerCycle &&
                           item.Storage.SupportedOperandRoleIds.Contains(Phase8ATensorRoleIds.Weight, StringComparer.Ordinal) &&
                           item.Storage.SupportedPrecisionIds.Contains(precisionId, StringComparer.Ordinal) &&
                           item.Storage.CapacityBits > 0 && item.Storage.AlignmentBits > 0 &&
                           item.Storage.AllocationGranularityBits > 0 && item.Storage.ResidentSlots > 0 &&
                           item.Storage.ReadBandwidthBitsPerCycle > 0 && item.Storage.WriteBandwidthBitsPerCycle > 0 &&
                           item.Storage.ReadLatencyCycles >= 0 && item.Storage.WriteLatencyCycles >= 0 &&
                           !string.IsNullOrWhiteSpace(item.Storage.CommitModeId) &&
                           !string.IsNullOrWhiteSpace(item.Storage.SourceContractHash))
            .OrderBy(item => item.Storage.StorageLevelId, StringComparer.Ordinal)
            .ThenBy(item => item.Storage.ResourceId, StringComparer.Ordinal)
            .ThenBy(item => item.Storage.SourceContractHash, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length == 0)
        {
            issue = Error(ReferenceFirstFitIssueCodes.CapabilityUnsupported, "$.capabilitySnapshot.components.storageCapabilities",
                "Mapped PE requires a declared input port/domain/bandwidth and weight storage with exact role/precision/capacity/alignment/granularity/slots/read-write/commit/source contract.", capability.ComponentId);
            return false;
        }
        preloadPortId = eligible[0].Storage.PreloadPortId;
        return true;
    }

    private static bool ShapeLimitAtLeast(ComponentCapabilitySnapshot capability, string key, long required) =>
        capability.ShapeContracts.TryGetValue(key, out var raw) &&
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var available) &&
        available >= required;

    private static long? RawWeightBits(Phase8ALoweredOperandTile weight)
    {
        if (!Enum.TryParse<PrecisionKind>(weight.PrecisionId, false, out var precision) ||
            !PrecisionModel.TryGetDigitalBitWidth(precision, out var bitWidth)) return null;
        try
        {
            var elements = 1L;
            foreach (var dimension in weight.PaddedShape.Dimensions)
            {
                if (dimension <= 0) return null;
                elements = checked(elements * dimension);
            }
            return checked(elements * bitWidth);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
    private static TopologyManifestComponent? UniqueRole(IEnumerable<TopologyManifestComponent> components, TopologyPresetComponentRole role)
    {
        var matches = components.Where(item => item.Role == role).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static TopologyManifestComponent? UniqueHighestRole(IEnumerable<TopologyManifestComponent> components, TopologyPresetComponentRole role)
    {
        var matches = components.Where(item => item.Role == role).ToArray();
        if (matches.Length == 0) return null;
        var level = matches.Max(item => item.Level);
        var highest = matches.Where(item => item.Level == level).ToArray();
        return highest.Length == 1 ? highest[0] : null;
    }

    private static bool TryReserveSegment(bool[] trial, int count, int segmentSize, out int[] ordinals, out int segmentOrdinal)
    {
        ordinals = [];
        segmentOrdinal = -1;
        if (count <= 0 || segmentSize <= 0 || count > segmentSize) return false;
        for (var segmentStart = 0; segmentStart < trial.Length; segmentStart += segmentSize)
        {
            var segmentEnd = Math.Min(trial.Length, segmentStart + segmentSize);
            for (var start = segmentStart; start + count <= segmentEnd; start++)
            {
                var free = true;
                for (var index = start; index < start + count; index++) free &= !trial[index];
                if (!free) continue;
                ordinals = Enumerable.Range(start, count).ToArray();
                foreach (var ordinal in ordinals) trial[ordinal] = true;
                segmentOrdinal = segmentStart / segmentSize;
                return true;
            }
        }
        return false;
    }

    private static int NextPowerOfTwo(int value)
    {
        if (value <= 0) return 0;
        var result = 1;
        while (result < value) result = checked(result * 2);
        return result;
    }

    private static int CeilingDiv(int value, int divisor) => checked(1 + (value - 1) / divisor);
    private static string Bitset(IEnumerable<bool> values) => new(values.Select(value => value ? '1' : '0').ToArray());
    private static bool IsLowerSha256(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(object value) => ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(value, HardwareGraphJson.Options)));
    private static WorkloadMappingV2Issue Error(string code, string location, string message, string? relatedId = null) => new(code, ValidationSeverity.Error, location, message, relatedId);
    private static ReferenceFirstFitMappingResult Failure(params WorkloadMappingV2Issue[] issues) => new(null, issues);
    private static ReferenceFirstFitMappingResult Failure(IEnumerable<WorkloadMappingV2Issue> issues) => new(null, issues);

    private sealed record SlotDiscoveryResult(IReadOnlyList<ReferenceFirstFitPeSlot> Slots, IReadOnlyList<WorkloadMappingV2Issue> Issues);
    private sealed record CapabilityInventoryResult(
        IReadOnlyDictionary<string, ComponentCapabilitySnapshot> Capabilities,
        IReadOnlyList<WorkloadMappingV2Issue> Issues);
    private sealed record BindingResult(
        IReadOnlyList<OperationTileAssignment> Assignments,
        IReadOnlyList<ReferenceFirstFitWeightBindingIntent> WeightBindings,
        IReadOnlyList<ReferenceFirstFitBranchIntent> BranchIntents,
        IReadOnlyList<ReferenceFirstFitResultIntent> ResultIntents,
        IReadOnlyList<WorkloadMappingV2Issue> Issues);
}
