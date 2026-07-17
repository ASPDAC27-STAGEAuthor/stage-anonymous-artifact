namespace HardwareSim.Core;

/// <summary>Architecture-level packet and bit counts for row-replicated full-input delivery and hierarchical result return.</summary>
public sealed class Phase8ARowReplicatedTrafficProjection
{
    internal Phase8ARowReplicatedTrafficProjection(
        int inputBoundaryPacketMoves,
        int inputMeshPacketMoves,
        int inputTreePacketMoves,
        long inputBoundaryBits,
        long inputMeshBits,
        long inputTreeBits,
        int clusterLocalReturnPacketMoves,
        int centralAssemblyReturnPacketMoves,
        long clusterLocalReturnBits,
        long centralAssemblyReturnBits)
    {
        InputBoundaryPacketMoves = inputBoundaryPacketMoves;
        InputMeshPacketMoves = inputMeshPacketMoves;
        InputTreePacketMoves = inputTreePacketMoves;
        InputBoundaryBits = inputBoundaryBits;
        InputMeshBits = inputMeshBits;
        InputTreeBits = inputTreeBits;
        ClusterLocalReturnPacketMoves = clusterLocalReturnPacketMoves;
        CentralAssemblyReturnPacketMoves = centralAssemblyReturnPacketMoves;
        ClusterLocalReturnBits = clusterLocalReturnBits;
        CentralAssemblyReturnBits = centralAssemblyReturnBits;
    }

    /// <summary>Gets one complete-X injection at every left-column mesh row.</summary>
    public int InputBoundaryPacketMoves { get; }
    /// <summary>Gets row-wise mesh transfers needed to expose complete X to every cluster column.</summary>
    public int InputMeshPacketMoves { get; }
    /// <summary>Gets unique cluster-tree transfers after complete X is partitioned toward used PE leaves.</summary>
    public int InputTreePacketMoves { get; }
    /// <summary>Gets complete-X bits injected through left-column boundary links.</summary>
    public long InputBoundaryBits { get; }
    /// <summary>Gets complete-X bits transferred across row-wise mesh links.</summary>
    public long InputMeshBits { get; }
    /// <summary>Gets partitioned activation bits transferred across unique cluster-tree links.</summary>
    public long InputTreeBits { get; }
    /// <summary>Gets the total row-replicated input packet moves.</summary>
    public int InputPacketMoves => checked(InputBoundaryPacketMoves + InputMeshPacketMoves + InputTreePacketMoves);
    /// <summary>Gets the total row-replicated input transferred bits.</summary>
    public long InputTransferredBits => checked(InputBoundaryBits + InputMeshBits + InputTreeBits);
    /// <summary>Gets local tree reduction plus one local shard egress per cluster-local result.</summary>
    public int ClusterLocalReturnPacketMoves { get; }
    /// <summary>Gets local tree, global reduction, central assembly transport, and one final boundary output.</summary>
    public int CentralAssemblyReturnPacketMoves { get; }
    /// <summary>Gets local tree reduction plus local shard egress bits.</summary>
    public long ClusterLocalReturnBits { get; }
    /// <summary>Gets local tree, global reduction, central assembly transport, and final boundary output bits.</summary>
    public long CentralAssemblyReturnBits { get; }
    /// <summary>Gets input plus cluster-local distributed-output packet moves.</summary>
    public int ClusterLocalTotalPacketMoves => checked(InputPacketMoves + ClusterLocalReturnPacketMoves);
    /// <summary>Gets input plus central-assembly packet moves.</summary>
    public int CentralAssemblyTotalPacketMoves => checked(InputPacketMoves + CentralAssemblyReturnPacketMoves);
    /// <summary>Gets input plus cluster-local distributed-output transferred bits.</summary>
    public long ClusterLocalTotalTransferredBits => checked(InputTransferredBits + ClusterLocalReturnBits);
    /// <summary>Gets input plus central-assembly transferred bits.</summary>
    public long CentralAssemblyTotalTransferredBits => checked(InputTransferredBits + CentralAssemblyReturnBits);
}

/// <summary>One Mesh-row cluster's exact fixed activation-chunk demand.</summary>
public sealed record Phase8ARowClusterChunkDemand(
    int MeshRow,
    int MeshColumn,
    IReadOnlyList<int> GlobalKTileIndices);

/// <summary>Boundary and horizontal-Mesh traffic for mapping-aware fixed activation packets.</summary>
public sealed record Phase8AFixedRowInputProjection(
    int BoundaryPacketMoves,
    int MeshPacketMoves,
    long BoundaryBits,
    long MeshBits)
{
    /// <summary>Gets boundary plus horizontal fixed-packet link transfers.</summary>
    public int TotalPacketMoves => checked(BoundaryPacketMoves + MeshPacketMoves);
    /// <summary>Gets boundary plus horizontal transferred bits.</summary>
    public long TotalBits => checked(BoundaryBits + MeshBits);
}
/// <summary>Projects logical architecture traffic without counting runtime-only kernel overlay links.</summary>
public static class Phase8ARowReplicatedTrafficProjector
{
    /// <summary>Projects the row demand union at the boundary and the suffix union on every horizontal edge.</summary>
    public static Phase8AFixedRowInputProjection ProjectFixedRowInput(
        IEnumerable<Phase8ARowClusterChunkDemand> demands)
    {
        var frozen = (demands ?? throw new ArgumentNullException(nameof(demands))).ToArray();
        if (frozen.Any(demand => demand.MeshRow < 0 || demand.MeshColumn < 0 ||
                                demand.GlobalKTileIndices is null ||
                                demand.GlobalKTileIndices.Any(index => index < 0)))
            throw new ArgumentException("Row chunk demands require non-negative coordinates and K-tile indices.", nameof(demands));
        if (frozen.GroupBy(demand => (demand.MeshRow, demand.MeshColumn)).Any(group => group.Count() != 1))
            throw new ArgumentException("Each Mesh row/column coordinate may appear at most once.", nameof(demands));

        var boundaryMoves = 0;
        var meshMoves = 0;
        foreach (var row in frozen.GroupBy(demand => demand.MeshRow))
        {
            var tiles = row.SelectMany(demand => demand.GlobalKTileIndices).Distinct().ToArray();
            boundaryMoves = checked(boundaryMoves + tiles.Length);
            foreach (var tile in tiles)
                meshMoves = checked(meshMoves + row.Where(demand => demand.GlobalKTileIndices.Contains(tile))
                    .Max(demand => demand.MeshColumn));
        }
        return new Phase8AFixedRowInputProjection(
            boundaryMoves,
            meshMoves,
            checked((long)boundaryMoves * Phase8AMoTInrRuntimeIds.FixedPacketBits),
            checked((long)meshMoves * Phase8AMoTInrRuntimeIds.FixedPacketBits));
    }
    /// <summary>Projects full-X row replication, cluster-tree partition, hierarchical reduction, and result-return traffic.</summary>
    public static Phase8ARowReplicatedTrafficProjection Project(Phase8AMatMulScenarioBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        var fixedPacketStrategy = string.Equals(
            bundle.Request.TopologyExecutionStrategyId,
            Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1,
            StringComparison.Ordinal);
        int inputBoundaryPacketMoves;
        int inputMeshPacketMoves;
        int inputTreePacketMoves;
        long inputBoundaryBits;
        long inputMeshBits;
        long inputTreeBits;
        if (fixedPacketStrategy)
        {
            var manifest = TopologyManifestJson.ReadFromGraph(bundle.TopologyAuthorityGraph).Manifest
                ?? throw new InvalidOperationException("The scenario topology authority has no readable manifest.");
            var coordinateByCluster = manifest.Components
                .Where(component => component.Role == TopologyPresetComponentRole.MeshRouter)
                .ToDictionary(component => component.ClusterIndex!.Value, component => component.MeshCoordinate!);
            var rowInput = ProjectFixedRowInput(bundle.DcLayout.Assignments
                .GroupBy(assignment => assignment.ClusterIndex)
                .Select(group => new Phase8ARowClusterChunkDemand(
                    coordinateByCluster[group.Key].Row,
                    coordinateByCluster[group.Key].Column,
                    group.Select(assignment => assignment.GlobalKTileIndex).Distinct().OrderBy(index => index).ToArray())));
            inputBoundaryPacketMoves = rowInput.BoundaryPacketMoves;
            inputMeshPacketMoves = rowInput.MeshPacketMoves;
            var localEdges = bundle.ActivationTree.Trees
                .SelectMany(tree => tree.Edges)
                .Where(edge => edge.ClusterIndex.HasValue &&
                               edge.Scope is TopologyPresetLinkScope.Leaf or TopologyPresetLinkScope.Tree)
                .ToArray();
            inputTreePacketMoves = localEdges.Length;
            inputBoundaryBits = rowInput.BoundaryBits;
            inputMeshBits = rowInput.MeshBits;
            inputTreeBits = checked((long)inputTreePacketMoves * Phase8AMoTInrRuntimeIds.FixedPacketBits);
        }
        else
        {
            var assignments = bundle.DcLayout.Assignments.ToDictionary(item => item.AssignmentId, StringComparer.Ordinal);
            var localEdgeGroups = bundle.ActivationTree.Trees
                .SelectMany(tree => tree.Edges)
                .Where(edge => edge.ClusterIndex.HasValue && edge.Scope is TopologyPresetLinkScope.Leaf or TopologyPresetLinkScope.Tree)
                .GroupBy(edge => edge.LinkId, StringComparer.Ordinal)
                .ToArray();
            inputTreeBits = localEdgeGroups.Sum(group => checked(
                CoveredExtent(group.SelectMany(edge => edge.TargetAssignmentIds)
                    .Distinct(StringComparer.Ordinal)
                    .Select(id => assignments.TryGetValue(id, out var assignment)
                        ? assignment.KRange
                        : throw new InvalidOperationException("Activation edge references an unknown D/C assignment: " + id))) *
                bundle.ActivationTree.ActivationBitWidth));
            inputBoundaryPacketMoves = bundle.MeshRows;
            inputMeshPacketMoves = checked(bundle.MeshRows * Math.Max(0, bundle.MeshColumns - 1));
            inputTreePacketMoves = localEdgeGroups.Length;
            inputBoundaryBits = checked((long)inputBoundaryPacketMoves * bundle.Request.K * bundle.ActivationTree.ActivationBitWidth);
            inputMeshBits = checked((long)inputMeshPacketMoves * bundle.Request.K * bundle.ActivationTree.ActivationBitWidth);
        }
        var reduction = bundle.HierarchicalReduction.Summary;
        var clusterLocalReturnPacketMoves = checked(reduction.LocalTreeLinkTransferCount + reduction.LocalResultCount);
        var clusterLocalReturnBits = checked(reduction.LocalTreeTransferredBits + reduction.LocalResultBits);
        var (centralAssemblyReturnPacketMoves, centralAssemblyReturnBits) =
            string.Equals(
                bundle.Request.TopologyExecutionStrategyId,
                Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1,
                StringComparison.Ordinal)
                ? ProjectMeshReductionForest(bundle, reduction)
                : (
                    checked(
                        reduction.LocalTreeLinkTransferCount +
                        reduction.GlobalReturnLinkTransferCount +
                        reduction.GlobalMeshLinkTransferCount +
                        reduction.AssemblyReturnLinkTransferCount +
                        reduction.AssemblyMeshLinkTransferCount +
                        1),
                    checked(
                        reduction.LocalTreeTransferredBits +
                        reduction.GlobalTransferredBits +
                        reduction.AssemblyTransferredBits +
                        reduction.FinalOutputBits));
        return new Phase8ARowReplicatedTrafficProjection(
            inputBoundaryPacketMoves,
            inputMeshPacketMoves,
            inputTreePacketMoves,
            inputBoundaryBits,
            inputMeshBits,
            inputTreeBits,
            clusterLocalReturnPacketMoves,
            centralAssemblyReturnPacketMoves,
            clusterLocalReturnBits,
            centralAssemblyReturnBits);
    }

    private static (int PacketMoves, long TransferredBits) ProjectMeshReductionForest(
        Phase8AMatMulScenarioBundle bundle,
        Phase8AHierarchicalReductionSummary reduction)
    {
        var manifest = TopologyManifestJson.ReadFromGraph(bundle.TopologyAuthorityGraph).Manifest
            ?? throw new InvalidOperationException("The scenario topology authority has no readable manifest.");
        var forest = Phase8AMeshReductionForestPlanner.Build(
            bundle.Request,
            manifest,
            bundle.HierarchicalReduction);
        var centralLanding = string.Equals(
            bundle.Request.OutputLandingMode,
            Phase8AOutputLandingModes.CentralOffsetAssemblyV1,
            StringComparison.Ordinal);
        var packetMoves = reduction.LocalTreeLinkTransferCount;
        var transferredBits = reduction.LocalTreeTransferredBits;
        foreach (var group in forest.Groups)
        {
            var vectorBits = checked((long)group.NRange.Extent * bundle.HierarchicalReduction.VectorBitWidth);
            var localReturnMoves = group.Stages.Count > 0
                ? group.Leaves.Sum(leaf => leaf.ReturnRouteLinkIds.Count)
                : bundle.HierarchicalReduction.FinalAssembly.Shards
                    .Single(shard => shard.NShardIndex == group.NShardIndex)
                    .ReturnRouteLinkIds.Count;
            packetMoves = checked(packetMoves + localReturnMoves);
            transferredBits = checked(transferredBits + vectorBits * localReturnMoves);
            foreach (var stage in group.Stages)
            {
                var meshMoves = stage.Inputs.Sum(input => input.MeshRouteLinkIds.Count);
                packetMoves = checked(packetMoves + meshMoves);
                transferredBits = checked(transferredBits + vectorBits * meshMoves);
            }
            if (centralLanding)
            {
                packetMoves = checked(packetMoves + group.RootToAssemblyMeshRouteLinkIds.Count);
                transferredBits = checked(transferredBits + vectorBits * group.RootToAssemblyMeshRouteLinkIds.Count);
            }
        }
        if (centralLanding)
        {
            var finalFixedPackets = checked((int)((reduction.FinalOutputBits +
                Phase8AMoTInrRuntimeIds.FixedPacketBits - 1L) / Phase8AMoTInrRuntimeIds.FixedPacketBits));
            packetMoves = checked(packetMoves + finalFixedPackets);
            transferredBits = checked(transferredBits + reduction.FinalOutputBits);
        }
        return (packetMoves, transferredBits);
    }

    private static long CoveredExtent(IEnumerable<MappingIndexRange> ranges)
    {
        var ordered = ranges.OrderBy(range => range.Offset).ThenBy(range => range.Extent).ToArray();
        if (ordered.Length == 0) return 0;
        var covered = 0L;
        var start = ordered[0].Offset;
        var end = checked(start + ordered[0].Extent);
        foreach (var range in ordered.Skip(1))
        {
            var nextEnd = checked(range.Offset + range.Extent);
            if (range.Offset > end)
            {
                covered = checked(covered + end - start);
                start = range.Offset;
                end = nextEnd;
            }
            else if (nextEnd > end)
            {
                end = nextEnd;
            }
        }
        return checked(covered + end - start);
    }
}
