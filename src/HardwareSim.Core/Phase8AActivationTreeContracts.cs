using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Assigns one global activation K tile to a concrete ingress cluster.</summary>
public sealed record Phase8AActivationIngressAssignment(int GlobalKTileIndex, int ClusterIndex);

/// <summary>Stable structured issue codes for cluster-aware Phase 8A activation trees.</summary>
public static class Phase8AActivationTreeIssueCodes
{
    /// <summary>No resolved D/C layout was provided.</summary>
    public const string LayoutMissing = "Phase8AActivationTreeLayoutMissing";
    /// <summary>No typed topology graph was provided.</summary>
    public const string TopologyMissing = "Phase8AActivationTreeTopologyMissing";
    /// <summary>The typed topology manifest is missing or invalid.</summary>
    public const string ManifestInvalid = "Phase8AActivationTreeManifestInvalid";
    /// <summary>The graph changed after its typed topology manifest was frozen.</summary>
    public const string TopologyStale = "Phase8AActivationTreeTopologyStale";
    /// <summary>The topology geometry disagrees with the resolved D/C layout.</summary>
    public const string TopologyMismatch = "Phase8AActivationTreeTopologyMismatch";
    /// <summary>The selected ingress cluster is outside the typed cluster inventory.</summary>
    public const string IngressInvalid = "Phase8AActivationTreeIngressInvalid";
    /// <summary>The activation bit width is not positive.</summary>
    public const string BitWidthInvalid = "Phase8AActivationTreeBitWidthInvalid";
    /// <summary>The typed cluster, mesh-router, or PE inventory is incomplete or ambiguous.</summary>
    public const string InventoryInvalid = "Phase8AActivationTreeInventoryInvalid";
    /// <summary>An exact directed typed link is missing or ambiguous.</summary>
    public const string LinkInvalid = "Phase8AActivationTreeLinkInvalid";
    /// <summary>The resolved activation targets disagree with the D/C assignment authority.</summary>
    public const string TargetMismatch = "Phase8AActivationTreeTargetMismatch";
    /// <summary>The union of exact target routes contains more than one parent for a node.</summary>
    public const string Reconvergence = "Phase8AActivationTreeReconvergence";
    /// <summary>The union of exact target routes contains a directed cycle.</summary>
    public const string Cycle = "Phase8AActivationTreeCycle";
    /// <summary>An exact target cannot be reached from the selected ingress.</summary>
    public const string TargetUnreachable = "Phase8AActivationTreeTargetUnreachable";
    /// <summary>Count or bit arithmetic exceeded the supported range.</summary>
    public const string ArithmeticOverflow = "Phase8AActivationTreeArithmeticOverflow";
}

/// <summary>One deterministic cluster-aware activation-tree planning failure.</summary>
public sealed record Phase8AActivationTreeIssue(string Code, string Location, string Message);

/// <summary>Maps one D/C assignment to its exact typed PE endpoint and complete route.</summary>
public sealed class Phase8AActivationTreeTarget
{
    internal Phase8AActivationTreeTarget(
        string assignmentId,
        int clusterIndex,
        int peOrdinal,
        string peComponentId,
        string routePathId,
        IEnumerable<string> routeLinkIds)
    {
        AssignmentId = assignmentId;
        ClusterIndex = clusterIndex;
        PeOrdinal = peOrdinal;
        PeComponentId = peComponentId;
        RoutePathId = routePathId;
        RouteLinkIds = Freeze(routeLinkIds);
    }

    /// <summary>Gets the resolved D/C assignment identity.</summary>
    public string AssignmentId { get; }
    /// <summary>Gets the typed row-major cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the coordinate-derived semantic PE ordinal.</summary>
    public int PeOrdinal { get; }
    /// <summary>Gets the opaque typed PE component identity.</summary>
    public string PeComponentId { get; }
    /// <summary>Gets the stable complete activation route identity.</summary>
    public string RoutePathId { get; }
    /// <summary>Gets the exact source-to-PE directed links.</summary>
    public IReadOnlyList<string> RouteLinkIds { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>Represents one unique directed link transfer in an activation tree.</summary>
public sealed class Phase8AActivationTreeEdge
{
    internal Phase8AActivationTreeEdge(
        string linkId,
        string sourceComponentId,
        string destinationComponentId,
        TopologyPresetLinkRole role,
        TopologyPresetLinkScope scope,
        int? clusterIndex,
        int depth,
        long bits,
        IEnumerable<string> targetAssignmentIds)
    {
        LinkId = linkId;
        SourceComponentId = sourceComponentId;
        DestinationComponentId = destinationComponentId;
        Role = role;
        Scope = scope;
        ClusterIndex = clusterIndex;
        Depth = depth;
        Bits = bits;
        TargetAssignmentIds = new ReadOnlyCollection<string>(targetAssignmentIds
            .OrderBy(value => value, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the exact typed logical link identity.</summary>
    public string LinkId { get; }
    /// <summary>Gets the exact source component identity.</summary>
    public string SourceComponentId { get; }
    /// <summary>Gets the exact destination component identity.</summary>
    public string DestinationComponentId { get; }
    /// <summary>Gets the typed traffic role.</summary>
    public TopologyPresetLinkRole Role { get; }
    /// <summary>Gets the typed structural scope.</summary>
    public TopologyPresetLinkScope Scope { get; }
    /// <summary>Gets the optional cluster index for cluster-local edges.</summary>
    public int? ClusterIndex { get; }
    /// <summary>Gets the deterministic source-rooted tree depth.</summary>
    public int Depth { get; }
    /// <summary>Gets bits transferred once on this unique link.</summary>
    public long Bits { get; }
    /// <summary>Gets all final assignments sharing this prefix edge.</summary>
    public IReadOnlyList<string> TargetAssignmentIds { get; }
    /// <summary>Gets whether more than one final assignment shares this link transfer.</summary>
    public bool IsSharedPrefix => TargetAssignmentIds.Count > 1;
}

/// <summary>Represents one actual typed component where an activation packet must branch.</summary>
public sealed class Phase8AActivationBranchPoint
{
    internal Phase8AActivationBranchPoint(
        string branchId,
        string componentId,
        TopologyPresetComponentRole componentRole,
        int? clusterIndex,
        int depth,
        string? incomingLinkId,
        IEnumerable<string> outgoingLinkIds,
        long inputBits,
        long outputBits)
    {
        BranchId = branchId;
        ComponentId = componentId;
        ComponentRole = componentRole;
        ClusterIndex = clusterIndex;
        Depth = depth;
        IncomingLinkId = incomingLinkId;
        OutgoingLinkIds = new ReadOnlyCollection<string>(outgoingLinkIds
            .OrderBy(value => value, StringComparer.Ordinal).ToList());
        InputBits = inputBits;
        OutputBits = outputBits;
    }

    /// <summary>Gets the stable activation-tile-specific branch identity.</summary>
    public string BranchId { get; }
    /// <summary>Gets the actual typed branch component identity.</summary>
    public string ComponentId { get; }
    /// <summary>Gets the typed branch component role.</summary>
    public TopologyPresetComponentRole ComponentRole { get; }
    /// <summary>Gets the optional cluster index.</summary>
    public int? ClusterIndex { get; }
    /// <summary>Gets the deterministic source-rooted component depth.</summary>
    public int Depth { get; }
    /// <summary>Gets the unique incoming link, or null when the ingress root branches.</summary>
    public string? IncomingLinkId { get; }
    /// <summary>Gets exact outgoing links receiving branch outputs.</summary>
    public IReadOnlyList<string> OutgoingLinkIds { get; }
    /// <summary>Gets bits in the one parent packet.</summary>
    public long InputBits { get; }
    /// <summary>Gets conserved aggregate bits over all branch outputs.</summary>
    public long OutputBits { get; }
    /// <summary>Gets output packet count at this branch.</summary>
    public int OutputPacketCount => OutgoingLinkIds.Count;
    /// <summary>Gets additional copies beyond forwarding the original packet once.</summary>
    public int AdditionalCloneCount => Math.Max(0, OutgoingLinkIds.Count - 1);
}

/// <summary>Freezes one activation tile's mesh prefix and cluster-local delivery subtree.</summary>
public sealed class Phase8AActivationClusterTree
{
    internal Phase8AActivationClusterTree(
        int clusterIndex,
        string meshRouterComponentId,
        IEnumerable<string> meshPrefixLinkIds,
        IEnumerable<string> clusterLocalLinkIds,
        IEnumerable<string> localBranchIds,
        IEnumerable<Phase8AActivationTreeTarget> targets)
    {
        ClusterIndex = clusterIndex;
        MeshRouterComponentId = meshRouterComponentId;
        MeshPrefixLinkIds = Freeze(meshPrefixLinkIds);
        ClusterLocalLinkIds = Freeze(clusterLocalLinkIds);
        LocalBranchIds = Freeze(localBranchIds);
        Targets = new ReadOnlyCollection<Phase8AActivationTreeTarget>(targets
            .OrderBy(target => target.PeOrdinal).ThenBy(target => target.AssignmentId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Gets the typed row-major cluster index.</summary>
    public int ClusterIndex { get; }
    /// <summary>Gets the cluster-root mesh router identity.</summary>
    public string MeshRouterComponentId { get; }
    /// <summary>Gets the ingress-to-cluster shared mesh prefix.</summary>
    public IReadOnlyList<string> MeshPrefixLinkIds { get; }
    /// <summary>Gets unique attachment/tree/leaf links used inside this cluster.</summary>
    public IReadOnlyList<string> ClusterLocalLinkIds { get; }
    /// <summary>Gets actual cluster-local tree branch identities.</summary>
    public IReadOnlyList<string> LocalBranchIds { get; }
    /// <summary>Gets exact PE targets in semantic ordinal order.</summary>
    public IReadOnlyList<Phase8AActivationTreeTarget> Targets { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.ToList());
}

/// <summary>Contains one activation K tile's complete source-rooted hierarchical tree.</summary>
public sealed class Phase8AActivationTileTree
{
    internal Phase8AActivationTileTree(
        string treeId,
        string activationTileId,
        int globalKTileIndex,
        MappingIndexRange kRange,
        int sourceClusterIndex,
        string sourceComponentId,
        string flowKind,
        long bits,
        IEnumerable<Phase8AActivationTreeTarget> targets,
        IEnumerable<Phase8AActivationTreeEdge> edges,
        IEnumerable<Phase8AActivationBranchPoint> branchPoints,
        IEnumerable<Phase8AActivationClusterTree> clusters)
    {
        TreeId = treeId;
        ActivationTileId = activationTileId;
        GlobalKTileIndex = globalKTileIndex;
        KRange = kRange;
        SourceClusterIndex = sourceClusterIndex;
        SourceComponentId = sourceComponentId;
        FlowKind = flowKind;
        Bits = bits;
        Targets = Freeze(targets, target => target.AssignmentId);
        Edges = Freeze(edges, edge => edge.LinkId);
        BranchPoints = Freeze(branchPoints, branch => branch.BranchId);
        Clusters = new ReadOnlyCollection<Phase8AActivationClusterTree>(clusters
            .OrderBy(cluster => cluster.ClusterIndex).ToList());
    }

    /// <summary>Gets the stable tree identity.</summary>
    public string TreeId { get; }
    /// <summary>Gets the activation tensor-tile identity.</summary>
    public string ActivationTileId { get; }
    /// <summary>Gets the global K tile index.</summary>
    public int GlobalKTileIndex { get; }
    /// <summary>Gets the exact activation K range.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the row-major cluster index containing this tile's ingress router.</summary>
    public int SourceClusterIndex { get; }
    /// <summary>Gets the selected ingress mesh-router identity.</summary>
    public string SourceComponentId { get; }
    /// <summary>Gets Unicast for one target or Multicast for more than one target.</summary>
    public string FlowKind { get; }
    /// <summary>Gets source activation packet bits.</summary>
    public long Bits { get; }
    /// <summary>Gets all exact PE targets.</summary>
    public IReadOnlyList<Phase8AActivationTreeTarget> Targets { get; }
    /// <summary>Gets every unique link transfer exactly once.</summary>
    public IReadOnlyList<Phase8AActivationTreeEdge> Edges { get; }
    /// <summary>Gets every actual branch point.</summary>
    public IReadOnlyList<Phase8AActivationBranchPoint> BranchPoints { get; }
    /// <summary>Gets per-cluster mesh-prefix and local-subtree projections.</summary>
    public IReadOnlyList<Phase8AActivationClusterTree> Clusters { get; }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values, Func<T, string> id) =>
        new ReadOnlyCollection<T>(values.OrderBy(id, StringComparer.Ordinal).ToList());
}

/// <summary>Conservation counters derived from every resolved activation tile tree.</summary>
public sealed record Phase8AActivationTreeSummary(
    int SourceActivationPacketCount,
    int ClusterActivationDeliveryCount,
    int PeTargetDeliveryCount,
    int UniqueLinkTransferCount,
    int MeshLinkTransferCount,
    int ClusterLocalLinkTransferCount,
    int MeshBranchOutputPacketCount,
    int IntraClusterBranchOutputPacketCount,
    int AdditionalCloneCount,
    long SourceBits,
    long ClusterIngressBits,
    long PeDeliveryBits,
    long TransferredBits,
    long BranchInputBits,
    long BranchOutputBits);

/// <summary>Immutable activation-tree plan consumed by later mapping, runtime, estimator, JSON, and Unity adapters.</summary>
public sealed class Phase8AActivationTreePlan
{
    internal Phase8AActivationTreePlan(
        string layoutHash,
        string topologyManifestHash,
        string topologyGraphHash,
        int ingressClusterIndex,
        string ingressPolicyId,
        IEnumerable<Phase8AActivationIngressAssignment> ingressAssignments,
        int activationBitWidth,
        IEnumerable<Phase8AActivationTileTree> trees,
        Phase8AActivationTreeSummary summary,
        string canonicalHash)
    {
        LayoutHash = layoutHash;
        TopologyManifestHash = topologyManifestHash;
        TopologyGraphHash = topologyGraphHash;
        IngressClusterIndex = ingressClusterIndex;
        IngressPolicyId = ingressPolicyId;
        IngressAssignments = new ReadOnlyCollection<Phase8AActivationIngressAssignment>(ingressAssignments
            .OrderBy(item => item.GlobalKTileIndex).ToList());
        ActivationBitWidth = activationBitWidth;
        Trees = new ReadOnlyCollection<Phase8AActivationTileTree>(trees
            .OrderBy(tree => tree.GlobalKTileIndex).ToList());
        Summary = summary;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Identifies the complete activation-tree hash projection.</summary>
    public const string CanonicalHashAlgorithm = "sha256/phase8a-activation-tree/v2";
    /// <summary>Gets the authoritative D/C layout hash.</summary>
    public string LayoutHash { get; }
    /// <summary>Gets the typed topology manifest hash.</summary>
    public string TopologyManifestHash { get; }
    /// <summary>Gets the typed logical topology graph hash.</summary>
    public string TopologyGraphHash { get; }
    /// <summary>Gets the common ingress cluster index, or -1 when activation tiles use multiple ingress clusters.</summary>
    public int IngressClusterIndex { get; }
    /// <summary>Gets the stable policy used to assign activation tiles to ingress clusters.</summary>
    public string IngressPolicyId { get; }
    /// <summary>Gets the exact ingress cluster selected for every global K tile.</summary>
    public IReadOnlyList<Phase8AActivationIngressAssignment> IngressAssignments { get; }
    /// <summary>Gets the concrete activation bit width.</summary>
    public int ActivationBitWidth { get; }
    /// <summary>Gets trees in increasing global K tile order.</summary>
    public IReadOnlyList<Phase8AActivationTileTree> Trees { get; }
    /// <summary>Gets exact packet, link, clone, and bit conservation counters.</summary>
    public Phase8AActivationTreeSummary Summary { get; }
    /// <summary>Gets the deterministic complete plan hash.</summary>
    public string CanonicalHash { get; }
}

/// <summary>All-or-nothing activation-tree planning result.</summary>
public sealed class Phase8AActivationTreeResult
{
    internal Phase8AActivationTreeResult(
        Phase8AActivationTreePlan? plan,
        IEnumerable<Phase8AActivationTreeIssue>? issues)
    {
        Plan = plan;
        Issues = new ReadOnlyCollection<Phase8AActivationTreeIssue>((issues ?? [])
            .OrderBy(issue => issue.Location, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Gets the complete plan on success.</summary>
    public Phase8AActivationTreePlan? Plan { get; }
    /// <summary>Gets deterministic structured failures.</summary>
    public IReadOnlyList<Phase8AActivationTreeIssue> Issues { get; }
    /// <summary>Gets whether a complete plan was produced without issues.</summary>
    public bool IsSuccess => Plan is not null && Issues.Count == 0;
}
