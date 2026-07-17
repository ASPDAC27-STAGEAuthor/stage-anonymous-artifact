using System.Collections.ObjectModel;
using System.Text;

namespace HardwareSim.Core;

internal sealed record Phase8AMeshReductionLeaf(
    string LocalGroupId,
    string LocalResultId,
    string SourceComponentId,
    int ClusterIndex,
    string SourceMeshRouterComponentId,
    MappingIndexRange KRange,
    IReadOnlyList<string> ReturnRouteLinkIds);

internal sealed record Phase8AMeshReductionStageInput(
    string ResultId,
    string SourceMeshRouterComponentId,
    IReadOnlyList<string> MeshRouteLinkIds);

internal sealed record Phase8AMeshReductionStage(
    string StageId,
    string GroupKey,
    int Level,
    int PairIndex,
    string TargetMeshRouterComponentId,
    IReadOnlyList<Phase8AMeshReductionStageInput> Inputs,
    string OutputResultId,
    MappingIndexRange KRange,
    MappingIndexRange NRange);

internal sealed record Phase8AMeshReductionGroup(
    string GlobalGroupId,
    string GlobalGroupKey,
    int NShardIndex,
    MappingIndexRange NRange,
    IReadOnlyList<Phase8AMeshReductionLeaf> Leaves,
    IReadOnlyList<Phase8AMeshReductionStage> Stages,
    string RootResultId,
    string RootMeshRouterComponentId,
    IReadOnlyList<string> RootToAssemblyMeshRouteLinkIds);

internal sealed record Phase8AMeshReductionForest(
    IReadOnlyList<Phase8AMeshReductionGroup> Groups,
    IReadOnlyList<string> EgressMeshRouterComponentIds,
    string CanonicalHash);

internal static class Phase8AMeshReductionForestPlanner
{
    public static Phase8AMeshReductionForest Build(Phase8AMatMulScenarioPlan plan)
        => Build(plan.Request, plan.TopologyManifest, plan.HierarchicalReduction);

    public static Phase8AMeshReductionForest Build(
        Phase8AMatMulScenarioRequest request,
        TopologyManifest topologyManifest,
        Phase8AHierarchicalReductionPlan hierarchicalReduction)
    {
        var meshByCluster = topologyManifest.Components
            .Where(component => component.Role == TopologyPresetComponentRole.MeshRouter)
            .ToDictionary(component => component.ClusterIndex!.Value, component => component, EqualityComparer<int>.Default);
        var meshIds = meshByCluster.Values.Select(component => component.ComponentId).ToHashSet(StringComparer.Ordinal);
        var adjacency = topologyManifest.Links
            .Where(link => link.Role == TopologyPresetLinkRole.MeshTransport &&
                           meshIds.Contains(link.SourceComponentId) &&
                           meshIds.Contains(link.DestinationComponentId))
            .GroupBy(link => link.SourceComponentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(link => link.LinkId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var assemblyMeshId = hierarchicalReduction.FinalAssembly.AssemblyMeshRouterComponentId;
        var groups = new List<Phase8AMeshReductionGroup>();

        foreach (var global in hierarchicalReduction.GlobalGroups
                     .OrderBy(group => group.NRange.Offset)
                     .ThenBy(group => group.GroupId, StringComparer.Ordinal))
        {
            var leaves = global.Contributors
                .OrderBy(contributor => contributor.KRange.Offset)
                .ThenBy(contributor => contributor.LocalGroupId, StringComparer.Ordinal)
                .Select(contributor => new Phase8AMeshReductionLeaf(
                    contributor.LocalGroupId,
                    contributor.LocalResultId,
                    contributor.SourceComponentId,
                    contributor.ClusterIndex,
                    meshByCluster[contributor.ClusterIndex].ComponentId,
                    contributor.KRange,
                    new ReadOnlyCollection<string>(contributor.ReturnRouteLinkIds.ToList())))
                .ToArray();
            if (leaves.Length == 0)
                throw new InvalidOperationException($"Mesh reduction group '{global.GroupId}' has no contributors.");

            var nodes = leaves.Select(leaf => new PendingNode(
                leaf.LocalResultId,
                leaf.SourceMeshRouterComponentId,
                leaf.KRange)).ToList();
            var stages = new List<Phase8AMeshReductionStage>();
            var level = 0;
            while (nodes.Count > 1)
            {
                var next = new List<PendingNode>();
                for (var index = 0; index < nodes.Count; index += 2)
                {
                    if (index + 1 >= nodes.Count)
                    {
                        next.Add(nodes[index]);
                        continue;
                    }

                    var left = nodes[index];
                    var right = nodes[index + 1];
                    if (checked(left.KRange.Offset + left.KRange.Extent) != right.KRange.Offset)
                        throw new InvalidOperationException($"Mesh reduction group '{global.GroupId}' has non-contiguous K contributors.");

                    var targetMeshId = right.MeshRouterComponentId;
                    var pairIndex = index / 2;
                    var stageId = $"mesh-stage:n{global.NShardIndex:D4}:l{level:D2}:p{pairIndex:D4}";
                    var kRange = new MappingIndexRange(
                        left.KRange.Offset,
                        checked(left.KRange.Extent + right.KRange.Extent));
                    var isRoot = kRange.Offset == 0 && kRange.Extent == request.K;
                    var outputResultId = isRoot
                        ? global.OutputResultId
                        : stageId + ":output";
                    var inputs = new[]
                    {
                        new Phase8AMeshReductionStageInput(
                            left.ResultId,
                            left.MeshRouterComponentId,
                            ResolveMeshRoute(left.MeshRouterComponentId, targetMeshId, adjacency)),
                        new Phase8AMeshReductionStageInput(
                            right.ResultId,
                            right.MeshRouterComponentId,
                            ResolveMeshRoute(right.MeshRouterComponentId, targetMeshId, adjacency))
                    };
                    stages.Add(new Phase8AMeshReductionStage(
                        stageId,
                        global.GroupKey + ":" + stageId,
                        level,
                        pairIndex,
                        targetMeshId,
                        new ReadOnlyCollection<Phase8AMeshReductionStageInput>(inputs),
                        outputResultId,
                        kRange,
                        global.NRange));
                    next.Add(new PendingNode(outputResultId, targetMeshId, kRange));
                }
                nodes = next;
                level++;
            }

            var root = nodes.Single();
            if (root.KRange.Offset != 0 || root.KRange.Extent != request.K)
                throw new InvalidOperationException($"Mesh reduction group '{global.GroupId}' does not cover K exactly once.");
            groups.Add(new Phase8AMeshReductionGroup(
                global.GroupId,
                global.GroupKey,
                global.NShardIndex,
                global.NRange,
                new ReadOnlyCollection<Phase8AMeshReductionLeaf>(leaves),
                new ReadOnlyCollection<Phase8AMeshReductionStage>(stages),
                stages.Count == 0 ? global.OutputResultId : root.ResultId,
                root.MeshRouterComponentId,
                ResolveMeshRoute(root.MeshRouterComponentId, assemblyMeshId, adjacency)));
        }

        var frozenGroups = new ReadOnlyCollection<Phase8AMeshReductionGroup>(groups);
        var egress = new ReadOnlyCollection<string>(groups
            .Select(group => group.RootMeshRouterComponentId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList());
        return new Phase8AMeshReductionForest(
            frozenGroups,
            egress,
            ComputeHash(frozenGroups, egress));
    }

    private static IReadOnlyList<string> ResolveMeshRoute(
        string sourceComponentId,
        string destinationComponentId,
        IReadOnlyDictionary<string, TopologyManifestLink[]> adjacency)
    {
        if (string.Equals(sourceComponentId, destinationComponentId, StringComparison.Ordinal)) return [];
        var queue = new Queue<string>();
        var previous = new Dictionary<string, (string ComponentId, string LinkId)>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { sourceComponentId };
        queue.Enqueue(sourceComponentId);
        while (queue.Count > 0 && !seen.Contains(destinationComponentId))
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var links)) continue;
            foreach (var link in links)
            {
                if (!seen.Add(link.DestinationComponentId)) continue;
                previous[link.DestinationComponentId] = (current, link.LinkId);
                queue.Enqueue(link.DestinationComponentId);
            }
        }
        if (!seen.Contains(destinationComponentId))
            throw new InvalidOperationException($"No typed Mesh route exists from '{sourceComponentId}' to '{destinationComponentId}'.");

        var route = new List<string>();
        var cursor = destinationComponentId;
        while (!string.Equals(cursor, sourceComponentId, StringComparison.Ordinal))
        {
            var step = previous[cursor];
            route.Add(step.LinkId);
            cursor = step.ComponentId;
        }
        route.Reverse();
        return new ReadOnlyCollection<string>(route);
    }

    private static string ComputeHash(
        IReadOnlyList<Phase8AMeshReductionGroup> groups,
        IReadOnlyList<string> egress)
    {
        var text = new StringBuilder("phase8a-mot-inr-mesh-reduction-forest-v1\n");
        foreach (var group in groups)
        {
            text.Append("group|").Append(group.GlobalGroupId).Append('|').Append(group.GlobalGroupKey).Append('|')
                .Append(group.NRange.Offset).Append(':').Append(group.NRange.Extent).Append('|')
                .Append(group.RootResultId).Append('|').Append(group.RootMeshRouterComponentId).Append('\n');
            foreach (var leaf in group.Leaves)
                text.Append("leaf|").Append(leaf.LocalGroupId).Append('|').Append(leaf.LocalResultId).Append('|')
                    .Append(leaf.ClusterIndex).Append('|').Append(leaf.SourceMeshRouterComponentId).Append('|')
                    .Append(leaf.KRange.Offset).Append(':').Append(leaf.KRange.Extent).Append('|')
                    .AppendJoin(',', leaf.ReturnRouteLinkIds).Append('\n');
            foreach (var stage in group.Stages)
            {
                text.Append("stage|").Append(stage.StageId).Append('|').Append(stage.GroupKey).Append('|')
                    .Append(stage.TargetMeshRouterComponentId).Append('|').Append(stage.OutputResultId).Append('|')
                    .Append(stage.KRange.Offset).Append(':').Append(stage.KRange.Extent).Append('\n');
                foreach (var input in stage.Inputs)
                    text.Append("input|").Append(input.ResultId).Append('|').Append(input.SourceMeshRouterComponentId).Append('|')
                        .AppendJoin(',', input.MeshRouteLinkIds).Append('\n');
            }
            text.Append("assembly-route|").AppendJoin(',', group.RootToAssemblyMeshRouteLinkIds).Append('\n');
        }
        text.Append("egress|").AppendJoin(',', egress).Append('\n');
        return ComponentExecutionJson.ComputeSha256(text.ToString());
    }

    private sealed record PendingNode(
        string ResultId,
        string MeshRouterComponentId,
        MappingIndexRange KRange);
}
