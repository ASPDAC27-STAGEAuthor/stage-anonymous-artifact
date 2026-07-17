using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Stable issue codes for hierarchical D/C reduction planning.</summary>
public static class Phase8AHierarchicalReductionIssueCodes
{
    /// <summary>No resolved D/C layout was provided.</summary>
    public const string LayoutMissing = "Phase8AHierarchicalReductionLayoutMissing";
    /// <summary>No typed topology graph was provided.</summary>
    public const string TopologyMissing = "Phase8AHierarchicalReductionTopologyMissing";
    /// <summary>The typed topology manifest is missing or invalid.</summary>
    public const string ManifestInvalid = "Phase8AHierarchicalReductionManifestInvalid";
    /// <summary>The topology graph changed after its manifest was frozen.</summary>
    public const string TopologyStale = "Phase8AHierarchicalReductionTopologyStale";
    /// <summary>The topology geometry disagrees with the D/C layout.</summary>
    public const string TopologyMismatch = "Phase8AHierarchicalReductionTopologyMismatch";
    /// <summary>The selected final assembly cluster is invalid.</summary>
    public const string AssemblyClusterInvalid = "Phase8AHierarchicalReductionAssemblyClusterInvalid";
    /// <summary>The partial-vector element bit width is not positive.</summary>
    public const string VectorBitWidthInvalid = "Phase8AHierarchicalReductionVectorBitWidthInvalid";
    /// <summary>The typed PE, reduction, or mesh inventory is incomplete.</summary>
    public const string InventoryInvalid = "Phase8AHierarchicalReductionInventoryInvalid";
    /// <summary>An exact directed typed return or mesh link is missing or ambiguous.</summary>
    public const string LinkInvalid = "Phase8AHierarchicalReductionLinkInvalid";
    /// <summary>Layout contributor and group authorities disagree.</summary>
    public const string GroupMismatch = "Phase8AHierarchicalReductionGroupMismatch";
    /// <summary>A cluster-local reduction subtree is malformed.</summary>
    public const string TreeInvalid = "Phase8AHierarchicalReductionTreeInvalid";
    /// <summary>A local result cannot reach its required mesh endpoint.</summary>
    public const string RouteUnreachable = "Phase8AHierarchicalReductionRouteUnreachable";
    /// <summary>Final N-shard assembly contains invalid coverage.</summary>
    public const string AssemblyInvalid = "Phase8AHierarchicalReductionAssemblyInvalid";
    /// <summary>Count, range, or bit arithmetic exceeded the supported range.</summary>
    public const string ArithmeticOverflow = "Phase8AHierarchicalReductionArithmeticOverflow";
}

/// <summary>Stable execution modes used by local and global reduction groups.</summary>
public static class Phase8AHierarchicalReductionModes
{
    /// <summary>One contributor is forwarded without a numerical add.</summary>
    public const string Bypass = "bypass";
    /// <summary>Two or more ordered vectors use the registered grouped-vector Sum contract.</summary>
    public const string GroupedVectorSum = "grouped-vector-sum";
}

/// <summary>Stable output-location kinds used by hierarchical reduction plans.</summary>
public static class Phase8AHierarchicalReductionTargetKinds
{
    /// <summary>The result remains at its sole processing element.</summary>
    public const string ProcessingElement = "processing-element";
    /// <summary>The result is produced by a real typed cluster-tree ReductionUnit.</summary>
    public const string TreeReductionUnit = "tree-reduction-unit";
    /// <summary>A later integration package must instantiate a registered grouped-vector collector.</summary>
    public const string DedicatedGroupedVectorCollector = "dedicated-grouped-vector-collector";
}

/// <summary>One deterministic hierarchical reduction planning failure.</summary>
public sealed record Phase8AHierarchicalReductionIssue(string Code, string Location, string Message);

/// <summary>Maps one PE partial to its ordered local reduction route.</summary>
public sealed class Phase8ALocalReductionContributor
{
    internal Phase8ALocalReductionContributor(
        string assignmentId,
        string partialResultId,
        string peComponentId,
        int peOrdinal,
        MappingIndexRange kRange,
        IEnumerable<string> routeLinkIds)
    {
        AssignmentId = assignmentId;
        PartialResultId = partialResultId;
        PeComponentId = peComponentId;
        PeOrdinal = peOrdinal;
        KRange = kRange;
        RouteLinkIds = Freeze(routeLinkIds);
    }

    /// <summary>Gets the authoritative D/C assignment identity.</summary>
    public string AssignmentId { get; }
    /// <summary>Gets the PE-produced partial-result identity.</summary>
    public string PartialResultId { get; }
    /// <summary>Gets the exact typed PE component identity.</summary>
    public string PeComponentId { get; }
    /// <summary>Gets the coordinate-derived PE ordinal.</summary>
    public int PeOrdinal { get; }
    /// <summary>Gets the exact K coverage contributed by the PE.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the PE-to-local-target PartialSumReturn route.</summary>
    public IReadOnlyList<string> RouteLinkIds { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>One unique packet transfer in a cluster-local reduction subtree.</summary>
public sealed class Phase8ALocalReductionTreeEdge
{
    internal Phase8ALocalReductionTreeEdge(
        string linkId,
        string sourceComponentId,
        string destinationComponentId,
        int clusterIndex,
        int level,
        long bits,
        IEnumerable<string> contributorAssignmentIds)
    {
        LinkId = linkId;
        SourceComponentId = sourceComponentId;
        DestinationComponentId = destinationComponentId;
        ClusterIndex = clusterIndex;
        Level = level;
        Bits = bits;
        ContributorAssignmentIds = new ReadOnlyCollection<string>(contributorAssignmentIds
            .OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the exact typed link identity.</summary>
    public string LinkId { get; }
    /// <summary>Gets the exact source component identity.</summary>
    public string SourceComponentId { get; }
    /// <summary>Gets the exact destination ReductionUnit identity.</summary>
    public string DestinationComponentId { get; }
    /// <summary>Gets the row-major cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the typed tree level of the link.</summary>
    public int Level { get; }
    /// <summary>Gets the vector bits transferred once on this edge.</summary>
    public long Bits { get; }
    /// <summary>Gets all PE contributions already represented by this packet.</summary>
    public IReadOnlyList<string> ContributorAssignmentIds { get; }
}

/// <summary>One real numerical grouped-vector add at a typed TreeReductionUnit.</summary>
public sealed class Phase8ALocalReductionStage
{
    internal Phase8ALocalReductionStage(
        string stageId,
        int stageOrder,
        string targetReductionComponentId,
        IEnumerable<string> inputResultIds,
        string outputResultId,
        IEnumerable<string> contributorAssignmentIds,
        MappingIndexRange kRange,
        MappingIndexRange nRange)
    {
        StageId = stageId;
        StageOrder = stageOrder;
        TargetReductionComponentId = targetReductionComponentId;
        InputResultIds = Freeze(inputResultIds);
        OutputResultId = outputResultId;
        ContributorAssignmentIds = Freeze(contributorAssignmentIds);
        KRange = kRange;
        NRange = nRange;
    }

    /// <summary>Gets the stable local stage identity.</summary>
    public string StageId { get; }
    /// <summary>Gets the bottom-up deterministic stage order.</summary>
    public int StageOrder { get; }
    /// <summary>Gets the real typed TreeReductionUnit executing this add.</summary>
    public string TargetReductionComponentId { get; }
    /// <summary>Gets exactly two immediate result inputs in K order.</summary>
    public IReadOnlyList<string> InputResultIds { get; }
    /// <summary>Gets the output result identity.</summary>
    public string OutputResultId { get; }
    /// <summary>Gets all ordered PE assignments accumulated into the output.</summary>
    public IReadOnlyList<string> ContributorAssignmentIds { get; }
    /// <summary>Gets the accumulated K coverage.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the invariant output N range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the registered numerical operation required by this stage.</summary>
    public string OperationKind => Phase8AGroupedVectorSumContract.SumOperation;
    /// <summary>Gets the exact scalar add count represented by this binary stage.</summary>
    public int AddOperationCount => InputResultIds.Count - 1;

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>One cluster-local reduction group resolved from D/C layout authority.</summary>
public sealed class Phase8ALocalReductionPlan
{
    internal Phase8ALocalReductionPlan(
        string groupId,
        string groupKey,
        int divisionIndex,
        int nShardIndex,
        int fragmentIndex,
        int clusterIndex,
        MappingIndexRange kRange,
        MappingIndexRange nRange,
        long vectorBits,
        string mode,
        string targetKind,
        string targetComponentId,
        string outputResultId,
        IEnumerable<Phase8ALocalReductionContributor> contributors,
        IEnumerable<Phase8ALocalReductionTreeEdge> treeEdges,
        IEnumerable<string> forwardingReductionComponentIds,
        IEnumerable<Phase8ALocalReductionStage> stages)
    {
        GroupId = groupId;
        GroupKey = groupKey;
        DivisionIndex = divisionIndex;
        NShardIndex = nShardIndex;
        FragmentIndex = fragmentIndex;
        ClusterIndex = clusterIndex;
        KRange = kRange;
        NRange = nRange;
        VectorBits = vectorBits;
        Mode = mode;
        TargetKind = targetKind;
        TargetComponentId = targetComponentId;
        OutputResultId = outputResultId;
        Contributors = new ReadOnlyCollection<Phase8ALocalReductionContributor>(contributors
            .OrderBy(item => item.KRange.Offset).ThenBy(item => item.AssignmentId, StringComparer.Ordinal).ToList());
        TreeEdges = new ReadOnlyCollection<Phase8ALocalReductionTreeEdge>(treeEdges
            .OrderBy(item => item.LinkId, StringComparer.Ordinal).ToList());
        ForwardingReductionComponentIds = new ReadOnlyCollection<string>(forwardingReductionComponentIds
            .OrderBy(id => id, StringComparer.Ordinal).ToList());
        Stages = new ReadOnlyCollection<Phase8ALocalReductionStage>(stages
            .OrderBy(item => item.StageOrder).ThenBy(item => item.StageId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the authoritative local group identity.</summary>
    public string GroupId { get; }
    /// <summary>Gets the operation, N-range, division, fragment, and cluster group key.</summary>
    public string GroupKey { get; }
    /// <summary>Gets the D division index.</summary>
    public int DivisionIndex { get; }
    /// <summary>Gets the N-shard index.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets the fragment index within its block.</summary>
    public int FragmentIndex { get; }
    /// <summary>Gets the row-major cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets exact local K coverage.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the invariant output N range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets bits in every partial vector.</summary>
    public long VectorBits { get; }
    /// <summary>Gets bypass or grouped-vector-sum mode.</summary>
    public string Mode { get; }
    /// <summary>Gets whether the result remains at a PE or a real TreeReductionUnit.</summary>
    public string TargetKind { get; }
    /// <summary>Gets the exact output component identity.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the local result identity consumed by the global group.</summary>
    public string OutputResultId { get; }
    /// <summary>Gets ordered exact PE contributors.</summary>
    public IReadOnlyList<Phase8ALocalReductionContributor> Contributors { get; }
    /// <summary>Gets unique return-tree packet transfers.</summary>
    public IReadOnlyList<Phase8ALocalReductionTreeEdge> TreeEdges { get; }
    /// <summary>Gets typed ReductionUnits that forward one active child without a numerical add.</summary>
    public IReadOnlyList<string> ForwardingReductionComponentIds { get; }
    /// <summary>Gets only real two-input numerical add stages.</summary>
    public IReadOnlyList<Phase8ALocalReductionStage> Stages { get; }
    /// <summary>Gets the exact local scalar add count.</summary>
    public int AddOperationCount => Stages.Sum(stage => stage.AddOperationCount);
}

/// <summary>Maps one local result to a mesh-level grouped-vector reduction.</summary>
public sealed class Phase8AGlobalReductionContributor
{
    internal Phase8AGlobalReductionContributor(
        string localGroupId,
        string localResultId,
        string sourceComponentId,
        int clusterIndex,
        MappingIndexRange kRange,
        IEnumerable<string> returnRouteLinkIds,
        IEnumerable<string> meshRouteLinkIds)
    {
        LocalGroupId = localGroupId;
        LocalResultId = localResultId;
        SourceComponentId = sourceComponentId;
        ClusterIndex = clusterIndex;
        KRange = kRange;
        ReturnRouteLinkIds = Freeze(returnRouteLinkIds);
        MeshRouteLinkIds = Freeze(meshRouteLinkIds);
    }

    /// <summary>Gets the local group identity.</summary>
    public string LocalGroupId { get; }
    /// <summary>Gets the local result identity.</summary>
    public string LocalResultId { get; }
    /// <summary>Gets the actual PE or TreeReductionUnit source component.</summary>
    public string SourceComponentId { get; }
    /// <summary>Gets the source cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets exact K coverage.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the exact source-to-cluster-mesh PartialSumReturn path.</summary>
    public IReadOnlyList<string> ReturnRouteLinkIds { get; }
    /// <summary>Gets the exact cluster-mesh-to-collector-mesh path.</summary>
    public IReadOnlyList<string> MeshRouteLinkIds { get; }
    /// <summary>Gets the complete typed topology portion of the contributor route.</summary>
    public IReadOnlyList<string> RouteLinkIds => new ReadOnlyCollection<string>(ReturnRouteLinkIds.Concat(MeshRouteLinkIds).ToList());

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>One N-range global group combining local results after mesh transport.</summary>
public sealed class Phase8AGlobalReductionPlan
{
    internal Phase8AGlobalReductionPlan(
        string groupId,
        string groupKey,
        int nShardIndex,
        MappingIndexRange nRange,
        long vectorBits,
        string mode,
        int collectionClusterIndex,
        string collectionMeshRouterComponentId,
        string? collectorRequirementId,
        string outputLocationKind,
        string outputLocationId,
        string outputResultId,
        IEnumerable<Phase8AGlobalReductionContributor> contributors)
    {
        GroupId = groupId;
        GroupKey = groupKey;
        NShardIndex = nShardIndex;
        NRange = nRange;
        VectorBits = vectorBits;
        Mode = mode;
        CollectionClusterIndex = collectionClusterIndex;
        CollectionMeshRouterComponentId = collectionMeshRouterComponentId;
        CollectorRequirementId = collectorRequirementId;
        OutputLocationKind = outputLocationKind;
        OutputLocationId = outputLocationId;
        OutputResultId = outputResultId;
        Contributors = new ReadOnlyCollection<Phase8AGlobalReductionContributor>(contributors
            .OrderBy(item => item.KRange.Offset).ThenBy(item => item.LocalGroupId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the authoritative global group identity.</summary>
    public string GroupId { get; }
    /// <summary>Gets the operation and N-range group key.</summary>
    public string GroupKey { get; }
    /// <summary>Gets the N-shard index.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets exact output N coverage.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets bits in every local or global vector.</summary>
    public long VectorBits { get; }
    /// <summary>Gets bypass or grouped-vector-sum mode.</summary>
    public string Mode { get; }
    /// <summary>Gets the deterministic collection cluster.</summary>
    public int CollectionClusterIndex { get; }
    /// <summary>Gets the exact mesh router where contributors arrive.</summary>
    public string CollectionMeshRouterComponentId { get; }
    /// <summary>Gets the dedicated registered collector requirement, or null for bypass.</summary>
    public string? CollectorRequirementId { get; }
    /// <summary>Gets the required registered collector type, or null for bypass.</summary>
    public string? CollectorTypeId => CollectorRequirementId is null ? null : Phase8AGroupedVectorSumContract.TypeId;
    /// <summary>Gets the output location kind.</summary>
    public string OutputLocationKind { get; }
    /// <summary>Gets an actual component id for bypass or requirement id for grouped Sum.</summary>
    public string OutputLocationId { get; }
    /// <summary>Gets the final N-shard result identity.</summary>
    public string OutputResultId { get; }
    /// <summary>Gets local results in stable K order.</summary>
    public IReadOnlyList<Phase8AGlobalReductionContributor> Contributors { get; }
    /// <summary>Gets the registered numerical operation required when not bypassed.</summary>
    public string? OperationKind => Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum
        ? Phase8AGroupedVectorSumContract.SumOperation
        : null;
    /// <summary>Gets exact mesh-level scalar add count.</summary>
    public int AddOperationCount => Math.Max(0, Contributors.Count - 1);
}

/// <summary>One final N shard routed to the dedicated offset-aware assembly requirement.</summary>
public sealed class Phase8AFinalAssemblyShardPlan
{
    internal Phase8AFinalAssemblyShardPlan(
        string shardId,
        int nShardIndex,
        MappingIndexRange nRange,
        string sourceResultId,
        string sourceLocationKind,
        string sourceLocationId,
        string sourceMeshRouterComponentId,
        IEnumerable<string> returnRouteLinkIds,
        IEnumerable<string> meshRouteLinkIds)
    {
        ShardId = shardId;
        NShardIndex = nShardIndex;
        NRange = nRange;
        SourceResultId = sourceResultId;
        SourceLocationKind = sourceLocationKind;
        SourceLocationId = sourceLocationId;
        SourceMeshRouterComponentId = sourceMeshRouterComponentId;
        ReturnRouteLinkIds = Freeze(returnRouteLinkIds);
        MeshRouteLinkIds = Freeze(meshRouteLinkIds);
    }

    /// <summary>Gets the stable assembly shard identity.</summary>
    public string ShardId { get; }
    /// <summary>Gets the N-shard index.</summary>
    public int NShardIndex { get; }
    /// <summary>Gets the exact final output offset and extent.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the global reduction output identity.</summary>
    public string SourceResultId { get; }
    /// <summary>Gets the actual or dedicated source-location kind.</summary>
    public string SourceLocationKind { get; }
    /// <summary>Gets the actual component or dedicated collector requirement identity.</summary>
    public string SourceLocationId { get; }
    /// <summary>Gets the mesh router where this result enters assembly transport.</summary>
    public string SourceMeshRouterComponentId { get; }
    /// <summary>Gets local PartialSumReturn links needed before mesh transport.</summary>
    public IReadOnlyList<string> ReturnRouteLinkIds { get; }
    /// <summary>Gets mesh links toward the assembly mesh router.</summary>
    public IReadOnlyList<string> MeshRouteLinkIds { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>Freezes gap-free offset-aware final output assembly.</summary>
public sealed class Phase8AFinalAssemblyPlan
{
    internal Phase8AFinalAssemblyPlan(
        string assemblyRequirementId,
        int assemblyClusterIndex,
        string assemblyMeshRouterComponentId,
        long outputExtent,
        IEnumerable<Phase8AFinalAssemblyShardPlan> shards)
    {
        AssemblyRequirementId = assemblyRequirementId;
        AssemblyClusterIndex = assemblyClusterIndex;
        AssemblyMeshRouterComponentId = assemblyMeshRouterComponentId;
        OutputExtent = outputExtent;
        Shards = new ReadOnlyCollection<Phase8AFinalAssemblyShardPlan>(shards
            .OrderBy(item => item.NRange.Offset).ThenBy(item => item.ShardId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the dedicated registered tensor-assembly requirement identity.</summary>
    public string AssemblyRequirementId { get; }
    /// <summary>Gets the deterministic assembly cluster.</summary>
    public int AssemblyClusterIndex { get; }
    /// <summary>Gets the mesh router where final shards arrive.</summary>
    public string AssemblyMeshRouterComponentId { get; }
    /// <summary>Gets the exact final output extent.</summary>
    public long OutputExtent { get; }
    /// <summary>Gets final shards in exact offset order.</summary>
    public IReadOnlyList<Phase8AFinalAssemblyShardPlan> Shards { get; }
    /// <summary>Gets the required registered tensor-assembly type.</summary>
    public string AssemblyTypeId => Phase8ATensorAssemblyContract.TypeId;
    /// <summary>Gets the registered offset-aware assembly operation.</summary>
    public string OperationKind => Phase8ATensorAssemblyContract.ConcatOperation;
}

/// <summary>Exact hierarchical reduction, routing, and assembly conservation counters.</summary>
public sealed record Phase8AHierarchicalReductionSummary(
    int PePartialCount,
    int LocalGroupCount,
    int LocalBypassGroupCount,
    int LocalReducedGroupCount,
    int LocalReductionStageCount,
    int LocalForwardingNodeCount,
    int LocalAddOperationCount,
    int LocalTreeLinkTransferCount,
    int LocalResultCount,
    int GlobalGroupCount,
    int GlobalBypassGroupCount,
    int GlobalReducedGroupCount,
    int GlobalContributorPacketCount,
    int GlobalAddOperationCount,
    int GlobalReturnLinkTransferCount,
    int GlobalMeshLinkTransferCount,
    int FinalAssemblyShardCount,
    int AssemblyReturnLinkTransferCount,
    int AssemblyMeshLinkTransferCount,
    long PePartialBits,
    long LocalTreeTransferredBits,
    long LocalResultBits,
    long GlobalTransferredBits,
    long FinalOutputBits,
    long AssemblyTransferredBits);

/// <summary>Immutable hierarchical reduction authority consumed by later runtime integration.</summary>
public sealed class Phase8AHierarchicalReductionPlan
{
    internal Phase8AHierarchicalReductionPlan(
        string layoutHash,
        string topologyManifestHash,
        string topologyGraphHash,
        int vectorBitWidth,
        IEnumerable<Phase8ALocalReductionPlan> localGroups,
        IEnumerable<Phase8AGlobalReductionPlan> globalGroups,
        Phase8AFinalAssemblyPlan finalAssembly,
        Phase8AHierarchicalReductionSummary summary,
        string canonicalHash)
    {
        LayoutHash = layoutHash;
        TopologyManifestHash = topologyManifestHash;
        TopologyGraphHash = topologyGraphHash;
        VectorBitWidth = vectorBitWidth;
        LocalGroups = new ReadOnlyCollection<Phase8ALocalReductionPlan>(localGroups
            .OrderBy(item => item.NRange.Offset).ThenBy(item => item.KRange.Offset).ThenBy(item => item.GroupId, StringComparer.Ordinal).ToList());
        GlobalGroups = new ReadOnlyCollection<Phase8AGlobalReductionPlan>(globalGroups
            .OrderBy(item => item.NRange.Offset).ThenBy(item => item.GroupId, StringComparer.Ordinal).ToList());
        FinalAssembly = finalAssembly;
        Summary = summary;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Identifies the complete hierarchical reduction hash projection.</summary>
    public const string CanonicalHashAlgorithm = "sha256/phase8a-hierarchical-reduction/v1";
    /// <summary>Gets the authoritative D/C layout hash.</summary>
    public string LayoutHash { get; }
    /// <summary>Gets the typed topology manifest hash.</summary>
    public string TopologyManifestHash { get; }
    /// <summary>Gets the typed topology graph hash.</summary>
    public string TopologyGraphHash { get; }
    /// <summary>Gets the concrete partial-vector element bit width.</summary>
    public int VectorBitWidth { get; }
    /// <summary>Gets every cluster-local group.</summary>
    public IReadOnlyList<Phase8ALocalReductionPlan> LocalGroups { get; }
    /// <summary>Gets every N-range global group.</summary>
    public IReadOnlyList<Phase8AGlobalReductionPlan> GlobalGroups { get; }
    /// <summary>Gets exact offset-aware final assembly.</summary>
    public Phase8AFinalAssemblyPlan FinalAssembly { get; }
    /// <summary>Gets exact conservation counters.</summary>
    public Phase8AHierarchicalReductionSummary Summary { get; }
    /// <summary>Gets the deterministic complete plan hash.</summary>
    public string CanonicalHash { get; }
}

/// <summary>All-or-nothing hierarchical reduction planning result.</summary>
public sealed class Phase8AHierarchicalReductionResult
{
    internal Phase8AHierarchicalReductionResult(
        Phase8AHierarchicalReductionPlan? plan,
        IEnumerable<Phase8AHierarchicalReductionIssue>? issues)
    {
        Plan = plan;
        Issues = new ReadOnlyCollection<Phase8AHierarchicalReductionIssue>((issues ?? [])
            .OrderBy(issue => issue.Location, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the complete plan on success.</summary>
    public Phase8AHierarchicalReductionPlan? Plan { get; }
    /// <summary>Gets deterministic structured failures.</summary>
    public IReadOnlyList<Phase8AHierarchicalReductionIssue> Issues { get; }
    /// <summary>Gets whether a complete plan was produced without issues.</summary>
    public bool IsSuccess => Plan is not null && Issues.Count == 0;
}
