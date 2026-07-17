using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Stable structured issue codes for the Phase 8A D/C layout planner.</summary>
public static class Phase8ADcLayoutIssueCodes
{
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string RequestMissing = "Phase8ADcLayoutRequestMissing";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string OperationIdInvalid = "Phase8ADcLayoutOperationIdInvalid";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string ShapeInvalid = "Phase8ADcLayoutShapeInvalid";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string UnsupportedMExtent = "Phase8ADcLayoutMExtentUnsupported";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string ComputeTileUnsupported = "Phase8ADcLayoutComputeTileUnsupported";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string TailUnsupported = "Phase8ADcLayoutTailUnsupported";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string DivisionInvalid = "Phase8ADcLayoutDivisionInvalid";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string DivisionExceedsK = "Phase8ADcLayoutDivisionExceedsK";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string ClusterInvalid = "Phase8ADcLayoutClusterInvalid";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string ClusterCountInvalid = "Phase8ADcLayoutClusterCountInvalid";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string CapacityExceeded = "Phase8ADcLayoutCapacityExceeded";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string ArithmeticOverflow = "Phase8ADcLayoutArithmeticOverflow";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string LayoutTooLarge = "Phase8ADcLayoutTooLarge";
    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string InternalInvariant = "Phase8ADcLayoutInternalInvariant";
}

/// <summary>Requests one canonical M=1, 32x32-PE D/C layout.</summary>
public sealed record Phase8ADcLayoutRequest(
    int M = 1,
    int K = 256,
    int N = 256,
    int WeightRowDivisionSize = 256,
    int ClusterSize = 8,
    int PeRows = 32,
    int PeColumns = 32,
    int? ClusterCount = null,
    string OperationId = "matmul");

/// <summary>One deterministic planner failure.</summary>
public sealed record Phase8ADcLayoutIssue(string Code, string Location, string Message);

/// <summary>One K-axis division before it is split into N blocks.</summary>
public sealed class Phase8ADcDivision
{
    internal Phase8ADcDivision(
        string divisionId,
        int divisionIndex,
        MappingIndexRange kRange,
        int firstKTileIndex,
        int kTileCount,
        IEnumerable<string> blockIds)
    {
        DivisionId = divisionId;
        DivisionIndex = divisionIndex;
        ValidKRange = kRange;
        PaddedKRange = kRange;
        FirstKTileIndex = firstKTileIndex;
        KTileCount = kTileCount;
        BlockIds = Freeze(blockIds);
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string DivisionId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the valid unpadded K range.</summary>
    public MappingIndexRange ValidKRange { get; }
    /// <summary>Gets the physical padded K range; canonical mode requires it to equal the valid range.</summary>
    public MappingIndexRange PaddedKRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int FirstKTileIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int KTileCount { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> BlockIds { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>One valid output-column shard assembled at its exact tensor offset.</summary>
public sealed record Phase8ADcNShard(
    string NShardId,
    int NShardIndex,
    MappingIndexRange NRange,
    MappingIndexRange PaddedNRange);

/// <summary>One D-by-PE-columns weight block before cluster fragmentation.</summary>
public sealed class Phase8ADcBlock
{
    internal Phase8ADcBlock(
        string blockId,
        int divisionIndex,
        int nShardIndex,
        MappingIndexRange kRange,
        MappingIndexRange nRange,
        IEnumerable<string> fragmentIds)
    {
        BlockId = blockId;
        DivisionIndex = divisionIndex;
        NShardIndex = nShardIndex;
        KRange = kRange;
        NRange = nRange;
        FragmentIds = new ReadOnlyCollection<string>(fragmentIds.ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string BlockId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> FragmentIds { get; }
}

/// <summary>One contiguous piece of a D-by-PE-columns block resident in one cluster.</summary>
public sealed class Phase8ADcClusterFragment
{
    internal Phase8ADcClusterFragment(
        string fragmentId,
        string blockId,
        int divisionIndex,
        int nShardIndex,
        int fragmentIndex,
        int clusterIndex,
        int peStartOrdinal,
        int peCount,
        int kTileOffsetWithinDivision,
        MappingIndexRange kRange,
        string localReductionGroupId,
        IEnumerable<string> assignmentIds)
    {
        FragmentId = fragmentId;
        BlockId = blockId;
        DivisionIndex = divisionIndex;
        NShardIndex = nShardIndex;
        FragmentIndex = fragmentIndex;
        ClusterIndex = clusterIndex;
        PeStartOrdinal = peStartOrdinal;
        PeCount = peCount;
        KTileOffsetWithinDivision = kTileOffsetWithinDivision;
        KRange = kRange;
        LocalReductionGroupId = localReductionGroupId;
        AssignmentIds = new ReadOnlyCollection<string>(assignmentIds.ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string FragmentId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string BlockId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int FragmentIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int PeStartOrdinal { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int PeCount { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int KTileOffsetWithinDivision { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string LocalReductionGroupId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> AssignmentIds { get; }
}

/// <summary>Binds one 32x32 weight tile and one activation tile to one PE.</summary>
public sealed record Phase8ADcPeAssignment(
    string AssignmentId,
    string OperationTileId,
    string BlockId,
    string FragmentId,
    string ActivationTileId,
    string WeightTileId,
    string PartialResultId,
    int DivisionIndex,
    int NShardIndex,
    int GlobalKTileIndex,
    int KTileIndexWithinDivision,
    MappingIndexRange KRange,
    MappingIndexRange NRange,
    int ClusterIndex,
    int PeOrdinal);

/// <summary>Lists the exact PEs in one cluster that consume one activation K tile.</summary>
public sealed class Phase8ADcActivationClusterDelivery
{
    internal Phase8ADcActivationClusterDelivery(
        string deliveryId,
        string activationTileId,
        int globalKTileIndex,
        MappingIndexRange kRange,
        int clusterIndex,
        IEnumerable<string> targetAssignmentIds,
        IEnumerable<int> targetPeOrdinals)
    {
        DeliveryId = deliveryId;
        ActivationTileId = activationTileId;
        GlobalKTileIndex = globalKTileIndex;
        KRange = kRange;
        ClusterIndex = clusterIndex;
        TargetAssignmentIds = new ReadOnlyCollection<string>(targetAssignmentIds.ToList());
        TargetPeOrdinals = new ReadOnlyCollection<int>(targetPeOrdinals.ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string DeliveryId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string ActivationTileId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int GlobalKTileIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> TargetAssignmentIds { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<int> TargetPeOrdinals { get; }
}

/// <summary>One cluster-local reduction group; contributors are ordered by K offset.</summary>
public sealed class Phase8ADcLocalReductionGroup
{
    internal Phase8ADcLocalReductionGroup(
        string groupId,
        string groupKey,
        int divisionIndex,
        int nShardIndex,
        int fragmentIndex,
        int clusterIndex,
        MappingIndexRange kRange,
        MappingIndexRange nRange,
        IEnumerable<string> contributorAssignmentIds)
    {
        GroupId = groupId;
        GroupKey = groupKey;
        DivisionIndex = divisionIndex;
        NShardIndex = nShardIndex;
        FragmentIndex = fragmentIndex;
        ClusterIndex = clusterIndex;
        KRange = kRange;
        NRange = nRange;
        ContributorAssignmentIds = new ReadOnlyCollection<string>(contributorAssignmentIds.ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string GroupId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string GroupKey { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int FragmentIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> ContributorAssignmentIds { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int AddOperationCount => Math.Max(0, ContributorAssignmentIds.Count - 1);
}

/// <summary>One mesh-level group combining only local results for the same N range.</summary>
public sealed class Phase8ADcMeshReductionGroup
{
    internal Phase8ADcMeshReductionGroup(
        string groupId,
        string groupKey,
        int nShardIndex,
        MappingIndexRange nRange,
        IEnumerable<string> contributorLocalGroupIds)
    {
        GroupId = groupId;
        GroupKey = groupKey;
        NShardIndex = nShardIndex;
        NRange = nRange;
        ContributorLocalGroupIds = new ReadOnlyCollection<string>(contributorLocalGroupIds.ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string GroupId { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string GroupKey { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> ContributorLocalGroupIds { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int AddOperationCount => Math.Max(0, ContributorLocalGroupIds.Count - 1);
}

/// <summary>One final N shard written at an exact output offset.</summary>
public sealed record Phase8ADcFinalAssemblyShard(
    string AssemblyShardId,
    int NShardIndex,
    MappingIndexRange NRange,
    string MeshReductionGroupId);

/// <summary>Freezes final PE occupancy, including idle slots.</summary>
public sealed class Phase8ADcClusterOccupancy
{
    internal Phase8ADcClusterOccupancy(int clusterIndex, string utilizationBitset, IEnumerable<string> assignmentIds)
    {
        ClusterIndex = clusterIndex;
        UtilizationBitset = utilizationBitset;
        AssignmentIds = new ReadOnlyCollection<string>(assignmentIds.ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string UtilizationBitset { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<string> AssignmentIds { get; }
}

/// <summary>Conservation counters derived from the resolved D/C plan.</summary>
public sealed record Phase8ADcLayoutSummary(
    int DivisionCount,
    int KTileCount,
    int NShardCount,
    int ClusterCount,
    int TotalPeCapacity,
    int UsedPeCount,
    int IdlePeCount,
    int PePartialCount,
    int LocalGroupCount,
    int LocalAddOperationCount,
    int MeshGroupCount,
    int MeshAddOperationCount,
    int FinalOutputShardCount);

/// <summary>Immutable single source of truth for Phase 8A D/C layout and contributor grouping.</summary>
public sealed class Phase8ADcLayoutPlan
{
    internal Phase8ADcLayoutPlan(
        Phase8ADcLayoutRequest request,
        IEnumerable<Phase8ADcDivision> divisions,
        IEnumerable<Phase8ADcNShard> nShards,
        IEnumerable<Phase8ADcBlock> blocks,
        IEnumerable<Phase8ADcClusterFragment> fragments,
        IEnumerable<Phase8ADcPeAssignment> assignments,
        IEnumerable<Phase8ADcActivationClusterDelivery> activationDeliveries,
        IEnumerable<Phase8ADcLocalReductionGroup> localReductionGroups,
        IEnumerable<Phase8ADcMeshReductionGroup> meshReductionGroups,
        IEnumerable<Phase8ADcFinalAssemblyShard> finalAssemblyShards,
        IEnumerable<Phase8ADcClusterOccupancy> clusterOccupancies,
        Phase8ADcLayoutSummary summary,
        string canonicalHash)
    {
        Request = request with { };
        Divisions = Freeze(divisions);
        NShards = Freeze(nShards);
        Blocks = Freeze(blocks);
        Fragments = Freeze(fragments);
        Assignments = Freeze(assignments);
        ActivationDeliveries = Freeze(activationDeliveries);
        LocalReductionGroups = Freeze(localReductionGroups);
        MeshReductionGroups = Freeze(meshReductionGroups);
        FinalAssemblyShards = Freeze(finalAssemblyShards);
        ClusterOccupancies = Freeze(clusterOccupancies);
        Summary = summary;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Identifies one stable D/C layout contract value.</summary>
    public const string CanonicalHashAlgorithm = "sha256/phase8a-dc-layout/v1";

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public Phase8ADcLayoutRequest Request { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcDivision> Divisions { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcNShard> NShards { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcBlock> Blocks { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcClusterFragment> Fragments { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcPeAssignment> Assignments { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcActivationClusterDelivery> ActivationDeliveries { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcLocalReductionGroup> LocalReductionGroups { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcMeshReductionGroup> MeshReductionGroups { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcFinalAssemblyShard> FinalAssemblyShards { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcClusterOccupancy> ClusterOccupancies { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public Phase8ADcLayoutSummary Summary { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public string CanonicalHash { get; }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToList());
}

/// <summary>All-or-nothing D/C layout planning result.</summary>
public sealed class Phase8ADcLayoutResult
{
    internal Phase8ADcLayoutResult(Phase8ADcLayoutPlan? plan, IEnumerable<Phase8ADcLayoutIssue>? issues)
    {
        Plan = plan;
        Issues = new ReadOnlyCollection<Phase8ADcLayoutIssue>((issues ?? [])
            .OrderBy(issue => issue.Location, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public Phase8ADcLayoutPlan? Plan { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public IReadOnlyList<Phase8ADcLayoutIssue> Issues { get; }
    /// <summary>Gets the immutable D/C layout contract value.</summary>
    public bool IsSuccess => Plan is not null && Issues.Count == 0;
}
