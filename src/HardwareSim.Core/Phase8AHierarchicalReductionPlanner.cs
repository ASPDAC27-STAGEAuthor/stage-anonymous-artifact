using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Resolves D/C partials into exact local trees, mesh collection, and final assembly.</summary>
public static class Phase8AHierarchicalReductionPlanner
{
    /// <summary>Builds an immutable hierarchical reduction plan without mutating its authorities.</summary>
    public static Phase8AHierarchicalReductionResult Plan(
        Phase8ADcLayoutPlan? layout,
        HardwareGraph? topologyGraph,
        int vectorBitWidth = 8,
        int assemblyClusterIndex = 0)
    {
        var issues = new List<Phase8AHierarchicalReductionIssue>();
        if (layout is null)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.LayoutMissing, "$.layout", "A resolved D/C layout plan is required."));
        if (topologyGraph is null)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TopologyMissing, "$.topology", "A typed topology graph is required."));
        if (vectorBitWidth <= 0)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.VectorBitWidthInvalid, "$.vectorBitWidth", "Partial-vector element bit width must be positive."));
        if (issues.Count > 0) return Failure(issues);

        TopologyManifestReadResult read;
        try
        {
            read = TopologyManifestJson.ReadFromGraph(topologyGraph!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return Failure(Issue(
                Phase8AHierarchicalReductionIssueCodes.ManifestInvalid,
                "$.topology.topology_manifest",
                $"The typed topology manifest could not be read: {exception.Message}"));
        }

        var readErrors = read.Issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToArray();
        if (read.Manifest is null || readErrors.Length > 0)
        {
            var detail = string.Join("; ", readErrors.Select(issue => $"{issue.Code}: {issue.Message}"));
            return Failure(Issue(
                Phase8AHierarchicalReductionIssueCodes.ManifestInvalid,
                "$.topology.topology_manifest",
                string.IsNullOrEmpty(detail) ? "The typed topology manifest is missing." : detail));
        }

        var stale = read.Issues.Where(issue => issue.Code is
            TopologyBuildIssueCodes.TopologyGraphChanged or
            TopologyBuildIssueCodes.PlacementChanged or
            TopologyBuildIssueCodes.RouteChanged).ToArray();
        if (stale.Length > 0)
        {
            return Failure(stale.Select(issue => Issue(
                Phase8AHierarchicalReductionIssueCodes.TopologyStale,
                "$.topology" + issue.Location.TrimStart('$'),
                $"{issue.Code}: {issue.Message}")));
        }

        var manifest = read.Manifest;
        var clusterCount = layout!.Summary.ClusterCount;
        if (!string.Equals(manifest.Request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TopologyMismatch, "$.topology.request.topologyId", "Hierarchical D/C reduction requires the typed Mesh-of-Trees v1 topology."));
        if (manifest.Request.ClusterSize != layout.Request.ClusterSize)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TopologyMismatch, "$.topology.request.clusterSize", "Topology cluster size must equal the resolved D/C cluster size."));
        if (manifest.Request.ClusterCount != clusterCount)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TopologyMismatch, "$.topology.request.mesh", "Topology mesh capacity must equal the resolved D/C cluster count."));
        if (assemblyClusterIndex < 0 || assemblyClusterIndex >= clusterCount)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.AssemblyClusterInvalid, "$.assemblyClusterIndex", "Assembly cluster index is outside the resolved cluster inventory."));
        ValidateLayoutAuthority(layout, issues);
        if (issues.Count > 0) return Failure(issues);

        try
        {
            var inventory = BuildInventory(layout, manifest, issues);
            if (inventory is null || issues.Count > 0) return Failure(issues);

            var localGroups = new List<Phase8ALocalReductionPlan>();
            foreach (var local in layout.LocalReductionGroups
                         .OrderBy(item => item.NRange.Offset)
                         .ThenBy(item => item.KRange.Offset)
                         .ThenBy(item => item.GroupId, StringComparer.Ordinal))
            {
                var resolved = BuildLocalGroup(local, layout, inventory, vectorBitWidth, issues);
                if (resolved is null) return Failure(issues);
                localGroups.Add(resolved);
            }

            var localById = localGroups.ToDictionary(item => item.GroupId, StringComparer.Ordinal);
            var globalGroups = new List<Phase8AGlobalReductionPlan>();
            foreach (var global in layout.MeshReductionGroups.OrderBy(item => item.NRange.Offset))
            {
                var resolved = BuildGlobalGroup(global, layout, localById, inventory, issues);
                if (resolved is null) return Failure(issues);
                globalGroups.Add(resolved);
            }

            var assembly = BuildAssembly(layout, globalGroups, localById, inventory, assemblyClusterIndex, issues);
            if (assembly is null) return Failure(issues);
            var summary = Summarize(localGroups, globalGroups, assembly);
            ValidateConservation(layout, localGroups, globalGroups, assembly, summary, issues);
            if (issues.Count > 0) return Failure(issues);

            var hash = ComputeHash(layout, manifest, vectorBitWidth, localGroups, globalGroups, assembly, summary);
            return new Phase8AHierarchicalReductionResult(new Phase8AHierarchicalReductionPlan(
                layout.CanonicalHash,
                manifest.CanonicalHash,
                manifest.TopologyGraphHash,
                vectorBitWidth,
                localGroups,
                globalGroups,
                assembly,
                summary,
                hash), []);
        }
        catch (OverflowException)
        {
            return Failure(Issue(
                Phase8AHierarchicalReductionIssueCodes.ArithmeticOverflow,
                "$",
                "Reduction group, range, route, or bit arithmetic exceeded the supported range."));
        }
    }

    private static void ValidateLayoutAuthority(
        Phase8ADcLayoutPlan layout,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var assignmentIds = layout.Assignments.Select(item => item.AssignmentId).ToArray();
        if (assignmentIds.Distinct(StringComparer.Ordinal).Count() != assignmentIds.Length)
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, "$.layout.assignments", "D/C assignment identities must be unique."));
            return;
        }
        var assignmentSet = assignmentIds.ToHashSet(StringComparer.Ordinal);
        var localContributorIds = layout.LocalReductionGroups.SelectMany(item => item.ContributorAssignmentIds).ToArray();
        if (localContributorIds.Length != assignmentIds.Length ||
            localContributorIds.Distinct(StringComparer.Ordinal).Count() != localContributorIds.Length ||
            !assignmentSet.SetEquals(localContributorIds))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, "$.layout.localReductionGroups", "Every PE partial must enter exactly one local reduction group."));

        var localIds = layout.LocalReductionGroups.Select(item => item.GroupId).ToArray();
        var meshContributorIds = layout.MeshReductionGroups.SelectMany(item => item.ContributorLocalGroupIds).ToArray();
        if (localIds.Distinct(StringComparer.Ordinal).Count() != localIds.Length ||
            meshContributorIds.Length != localIds.Length ||
            meshContributorIds.Distinct(StringComparer.Ordinal).Count() != meshContributorIds.Length ||
            !localIds.ToHashSet(StringComparer.Ordinal).SetEquals(meshContributorIds))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, "$.layout.meshReductionGroups", "Every local result must enter exactly one same-N global group."));

        var meshIds = layout.MeshReductionGroups.Select(item => item.GroupId).ToArray();
        var assemblyMeshIds = layout.FinalAssemblyShards.Select(item => item.MeshReductionGroupId).ToArray();
        if (meshIds.Distinct(StringComparer.Ordinal).Count() != meshIds.Length ||
            assemblyMeshIds.Length != meshIds.Length ||
            assemblyMeshIds.Distinct(StringComparer.Ordinal).Count() != assemblyMeshIds.Length ||
            !meshIds.ToHashSet(StringComparer.Ordinal).SetEquals(assemblyMeshIds))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.AssemblyInvalid, "$.layout.finalAssemblyShards", "Every global N result must enter final assembly exactly once."));

        long nextN = 0;
        foreach (var shard in layout.FinalAssemblyShards.OrderBy(item => item.NRange.Offset))
        {
            if (shard.NRange.Offset != nextN || shard.NRange.Extent <= 0)
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.AssemblyInvalid, "$.layout.finalAssemblyShards", "Final N shards must be positive, gap-free, and non-overlapping."));
                return;
            }
            nextN = checked(shard.NRange.Offset + shard.NRange.Extent);
        }
        if (nextN != layout.Request.N)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.AssemblyInvalid, "$.layout.finalAssemblyShards", "Final N shards do not cover the exact output extent."));
    }

    private static Inventory? BuildInventory(
        Phase8ADcLayoutPlan layout,
        TopologyManifest manifest,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        if (manifest.Components.GroupBy(item => item.ComponentId, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            manifest.Links.GroupBy(item => item.LinkId, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, "$.topology", "Typed topology component and link identities must be unique."));
            return null;
        }

        var components = manifest.Components.ToDictionary(item => item.ComponentId, StringComparer.Ordinal);
        var links = manifest.Links.ToDictionary(item => item.LinkId, StringComparer.Ordinal);
        var exactLinks = manifest.Links
            .GroupBy(item => (item.SourceComponentId, item.DestinationComponentId, item.Role))
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.LinkId, StringComparer.Ordinal).ToArray());
        var meshByCluster = new Dictionary<int, TopologyManifestComponent>();
        var peByCluster = new Dictionary<int, IReadOnlyDictionary<int, TopologyManifestComponent>>();
        var meshByCoordinate = new Dictionary<(int Row, int Column), TopologyManifestComponent>();

        for (var cluster = 0; cluster < layout.Summary.ClusterCount; cluster++)
        {
            var mesh = manifest.Components.Where(item => item.Role == TopologyPresetComponentRole.MeshRouter && item.ClusterIndex == cluster).ToArray();
            var reductions = manifest.Components.Where(item => item.Role == TopologyPresetComponentRole.TreeReductionUnit && item.ClusterIndex == cluster).ToArray();
            var pes = manifest.Components
                .Where(item => item.Role == TopologyPresetComponentRole.ProcessingElement && item.ClusterIndex == cluster)
                .OrderBy(item => item.Coordinate.Row)
                .ThenBy(item => item.Coordinate.Column)
                .ThenBy(item => item.ComponentId, StringComparer.Ordinal)
                .ToArray();
            if (mesh.Length != 1 || mesh[0].MeshCoordinate is null ||
                reductions.Length != layout.Request.ClusterSize - 1 ||
                pes.Length != layout.Request.ClusterSize ||
                pes.Select(item => (item.Coordinate.Row, item.Coordinate.Column)).Distinct().Count() != pes.Length)
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.topology.clusters[{cluster}]", "Each cluster must expose one mesh router, C unique-coordinate PEs, and C-1 typed ReductionUnits."));
                continue;
            }

            var coordinate = mesh[0].MeshCoordinate!;
            var expectedCluster = (long)coordinate.Row * manifest.Request.MeshColumns + coordinate.Column;
            if (coordinate.Row < 0 || coordinate.Row >= manifest.Request.MeshRows ||
                coordinate.Column < 0 || coordinate.Column >= manifest.Request.MeshColumns ||
                expectedCluster != cluster || !meshByCoordinate.TryAdd((coordinate.Row, coordinate.Column), mesh[0]))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.topology.clusters[{cluster}].meshCoordinate", "Mesh coordinates must be unique, in bounds, and row-major consistent."));
                continue;
            }

            var hierarchyValid = pes.All(pe => pe.AttachmentComponentId is not null &&
                                                    components.TryGetValue(pe.AttachmentComponentId, out var attached) &&
                                                    attached.Role == TopologyPresetComponentRole.TreeReductionUnit &&
                                                    attached.ClusterIndex == cluster) &&
                                 reductions.All(reduction => reduction.ParentComponentId is not null &&
                                     components.TryGetValue(reduction.ParentComponentId, out var parent) &&
                                     parent.ClusterIndex == cluster &&
                                     parent.Role is TopologyPresetComponentRole.TreeReductionUnit or TopologyPresetComponentRole.MeshRouter);
            if (!hierarchyValid)
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.topology.clusters[{cluster}].reductionHierarchy", "PE attachments and ReductionUnit parents must form a typed same-cluster return hierarchy."));
                continue;
            }

            meshByCluster[cluster] = mesh[0];
            peByCluster[cluster] = pes.Select((component, ordinal) => (component, ordinal))
                .ToDictionary(item => item.ordinal, item => item.component);
        }

        if (meshByCluster.Count != layout.Summary.ClusterCount ||
            peByCluster.Count != layout.Summary.ClusterCount ||
            meshByCoordinate.Count != layout.Summary.ClusterCount)
            return null;

        return new Inventory(manifest, components, links, exactLinks, meshByCluster, peByCluster, meshByCoordinate);
    }

    private static Phase8ALocalReductionPlan? BuildLocalGroup(
        Phase8ADcLocalReductionGroup group,
        Phase8ADcLayoutPlan layout,
        Inventory inventory,
        int vectorBitWidth,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var assignmentsById = layout.Assignments.ToDictionary(item => item.AssignmentId, StringComparer.Ordinal);
        var assignments = group.ContributorAssignmentIds.Select(id => assignmentsById[id])
            .OrderBy(item => item.KRange.Offset).ThenBy(item => item.AssignmentId, StringComparer.Ordinal).ToArray();
        if (assignments.Length == 0 || assignments.Any(item => item.ClusterIndex != group.ClusterIndex ||
                                                               item.NShardIndex != group.NShardIndex ||
                                                               item.NRange != group.NRange) ||
            !IsGapFree(assignments.Select(item => item.KRange), group.KRange.Offset, checked(group.KRange.Offset + group.KRange.Extent)))
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, $"$.layout.localReductionGroups[{group.GroupId}]", "Local contributors must be non-empty, same-cluster, same-N, and gap-free in K order."));
            return null;
        }

        var peByAssignment = new Dictionary<string, TopologyManifestComponent>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            if (!inventory.PeByCluster[group.ClusterIndex].TryGetValue(assignment.PeOrdinal, out var pe))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.layout.assignments[{assignment.AssignmentId}]", "The coordinate-derived PE ordinal is missing from its typed cluster."));
                return null;
            }
            peByAssignment[assignment.AssignmentId] = pe;
        }

        var vectorBits = checked(group.NRange.Extent * vectorBitWidth);
        if (assignments.Length == 1)
        {
            var assignment = assignments[0];
            var pe = peByAssignment[assignment.AssignmentId];
            return new Phase8ALocalReductionPlan(
                group.GroupId,
                group.GroupKey,
                group.DivisionIndex,
                group.NShardIndex,
                group.FragmentIndex,
                group.ClusterIndex,
                group.KRange,
                group.NRange,
                vectorBits,
                Phase8AHierarchicalReductionModes.Bypass,
                Phase8AHierarchicalReductionTargetKinds.ProcessingElement,
                pe.ComponentId,
                assignment.PartialResultId,
                [new Phase8ALocalReductionContributor(assignment.AssignmentId, assignment.PartialResultId, pe.ComponentId, assignment.PeOrdinal, assignment.KRange, [])],
                [],
                [],
                []);
        }

        var target = FindLowestCommonReduction(peByAssignment.Values, inventory, issues, group.GroupId);
        if (target is null) return null;
        var contributorRoutes = new Dictionary<string, IReadOnlyList<TopologyManifestLink>>(StringComparer.Ordinal);
        var edgeContributors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var route = ResolveReturnRouteToTarget(peByAssignment[assignment.AssignmentId], target, inventory, issues);
            if (issues.Count > 0) return null;
            contributorRoutes[assignment.AssignmentId] = route;
            foreach (var link in route)
            {
                if (!edgeContributors.TryGetValue(link.LinkId, out var set)) edgeContributors[link.LinkId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(assignment.AssignmentId);
            }
        }

        var usedLinks = edgeContributors.Keys.Select(id => inventory.Links[id]).ToArray();
        var incoming = usedLinks.GroupBy(link => link.DestinationComponentId, StringComparer.Ordinal)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.OrderBy(link => link.SourceComponentId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        if (incoming.Any(pair => pair.Value.Length is < 1 or > 2))
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.localGroups[{group.GroupId}]", "A binary reduction node must receive one forwarded packet or two add inputs."));
            return null;
        }

        var forwardingReductionComponentIds = incoming.Where(pair => pair.Value.Length == 1)
            .Select(pair => pair.Key).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var assignmentByPe = assignments.ToDictionary(item => peByAssignment[item.AssignmentId].ComponentId, StringComparer.Ordinal);
        var stages = new List<Phase8ALocalReductionStage>();
        var resolution = ResolveNode(target.ComponentId, group, assignmentByPe, incoming, inventory, stages, issues);
        if (resolution is null || issues.Count > 0) return null;
        if (stages.Count != assignments.Length - 1 || resolution.ContributorAssignmentIds.Count != assignments.Length)
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.localGroups[{group.GroupId}]", "The local binary subtree does not conserve contributors into exactly contributor-count minus one adds."));
            return null;
        }

        var contributors = assignments.Select(assignment => new Phase8ALocalReductionContributor(
            assignment.AssignmentId,
            assignment.PartialResultId,
            peByAssignment[assignment.AssignmentId].ComponentId,
            assignment.PeOrdinal,
            assignment.KRange,
            contributorRoutes[assignment.AssignmentId].Select(link => link.LinkId))).ToArray();
        var edges = usedLinks.Select(link => new Phase8ALocalReductionTreeEdge(
            link.LinkId,
            link.SourceComponentId,
            link.DestinationComponentId,
            group.ClusterIndex,
            link.Level,
            vectorBits,
            edgeContributors[link.LinkId])).ToArray();
        return new Phase8ALocalReductionPlan(
            group.GroupId,
            group.GroupKey,
            group.DivisionIndex,
            group.NShardIndex,
            group.FragmentIndex,
            group.ClusterIndex,
            group.KRange,
            group.NRange,
            vectorBits,
            Phase8AHierarchicalReductionModes.GroupedVectorSum,
            Phase8AHierarchicalReductionTargetKinds.TreeReductionUnit,
            target.ComponentId,
            resolution.ResultId,
            contributors,
            edges,
            forwardingReductionComponentIds,
            stages);
    }

    private static TopologyManifestComponent? FindLowestCommonReduction(
        IEnumerable<TopologyManifestComponent> pes,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues,
        string groupId)
    {
        var ancestorLists = pes.Select(pe => ReductionAncestors(pe, inventory, issues)).ToArray();
        if (issues.Count > 0 || ancestorLists.Any(list => list.Count == 0)) return null;
        var common = ancestorLists[0].FirstOrDefault(candidate => ancestorLists.Skip(1).All(list => list.Any(item => item.ComponentId == candidate.ComponentId)));
        if (common is null)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.localGroups[{groupId}]", "Local PE contributors have no common typed TreeReductionUnit ancestor."));
        return common;
    }

    private static IReadOnlyList<TopologyManifestComponent> ReductionAncestors(
        TopologyManifestComponent pe,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var result = new List<TopologyManifestComponent>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { pe.ComponentId };
        if (pe.AttachmentComponentId is null || !inventory.Components.TryGetValue(pe.AttachmentComponentId, out var current))
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.topology.components[{pe.ComponentId}]", "PE has no typed reduction attachment."));
            return result;
        }
        while (current.Role == TopologyPresetComponentRole.TreeReductionUnit)
        {
            if (!seen.Add(current.ComponentId))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.topology.components[{current.ComponentId}]", "Reduction hierarchy contains a cycle."));
                return [];
            }
            result.Add(current);
            if (current.ParentComponentId is null || !inventory.Components.TryGetValue(current.ParentComponentId, out current))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.topology.components[{result[^1].ComponentId}]", "Reduction hierarchy has no parent path to its mesh router."));
                return [];
            }
        }
        if (current.Role != TopologyPresetComponentRole.MeshRouter || current.ClusterIndex != pe.ClusterIndex)
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.InventoryInvalid, $"$.topology.components[{pe.ComponentId}]", "Reduction hierarchy terminates at the wrong mesh router."));
            return [];
        }
        return result;
    }

    private static IReadOnlyList<TopologyManifestLink> ResolveReturnRouteToTarget(
        TopologyManifestComponent source,
        TopologyManifestComponent target,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var result = new List<TopologyManifestLink>();
        var current = source;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.ComponentId };
        while (current.ComponentId != target.ComponentId)
        {
            var nextId = current.Role switch
            {
                TopologyPresetComponentRole.ProcessingElement => current.AttachmentComponentId,
                TopologyPresetComponentRole.TreeReductionUnit => current.ParentComponentId,
                _ => null
            };
            if (nextId is null || !inventory.Components.TryGetValue(nextId, out var next) ||
                next.Role != TopologyPresetComponentRole.TreeReductionUnit || !seen.Add(next.ComponentId))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.RouteUnreachable, $"$.topology.components[{current.ComponentId}]", $"No acyclic typed return route reaches local target '{target.ComponentId}'."));
                return [];
            }
            var link = ExactLink(current.ComponentId, next.ComponentId, TopologyPresetLinkRole.PartialSumReturn, inventory, issues);
            if (link is null) return [];
            result.Add(link);
            current = next;
        }
        return result;
    }

    private static NodeResolution? ResolveNode(
        string componentId,
        Phase8ADcLocalReductionGroup group,
        IReadOnlyDictionary<string, Phase8ADcPeAssignment> assignmentByPe,
        IReadOnlyDictionary<string, TopologyManifestLink[]> incoming,
        Inventory inventory,
        List<Phase8ALocalReductionStage> stages,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        if (!incoming.TryGetValue(componentId, out var inputs) || inputs.Length is < 1 or > 2)
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.localGroups[{group.GroupId}].components[{componentId}]", "Reduction target must have one forwarded input or two numerical inputs."));
            return null;
        }

        var resolvedInputs = new List<NodeResolution>();
        foreach (var input in inputs)
        {
            var source = inventory.Components[input.SourceComponentId];
            if (source.Role == TopologyPresetComponentRole.ProcessingElement)
            {
                if (!assignmentByPe.TryGetValue(source.ComponentId, out var assignment))
                {
                    issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.localGroups[{group.GroupId}]", "A subtree PE is not an authoritative local contributor."));
                    return null;
                }
                resolvedInputs.Add(new NodeResolution(assignment.PartialResultId, [assignment.AssignmentId], assignment.KRange, -1));
            }
            else if (source.Role == TopologyPresetComponentRole.TreeReductionUnit)
            {
                var child = ResolveNode(source.ComponentId, group, assignmentByPe, incoming, inventory, stages, issues);
                if (child is null) return null;
                resolvedInputs.Add(child);
            }
            else
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, $"$.localGroups[{group.GroupId}]", "Only PE partials or child ReductionUnit results may enter a local reduction stage."));
                return null;
            }
        }

        resolvedInputs = resolvedInputs.OrderBy(item => item.KRange.Offset).ThenBy(item => item.ResultId, StringComparer.Ordinal).ToList();
        if (resolvedInputs.Count == 1) return resolvedInputs[0];
        var contributorIds = resolvedInputs.SelectMany(item => item.ContributorAssignmentIds)
            .OrderBy(id => assignmentByPe.Values.Single(item => item.AssignmentId == id).KRange.Offset)
            .ThenBy(id => id, StringComparer.Ordinal).ToArray();
        var minK = resolvedInputs.Min(item => item.KRange.Offset);
        var maxK = resolvedInputs.Max(item => checked(item.KRange.Offset + item.KRange.Extent));
        var order = resolvedInputs.Max(item => item.StageOrder) + 1;
        var stageId = $"local-stage:{group.GroupId}:{componentId}";
        var outputId = stageId + ":output";
        stages.Add(new Phase8ALocalReductionStage(
            stageId,
            order,
            componentId,
            resolvedInputs.Select(item => item.ResultId),
            outputId,
            contributorIds,
            new MappingIndexRange(minK, checked(maxK - minK)),
            group.NRange));
        return new NodeResolution(outputId, contributorIds, new MappingIndexRange(minK, checked(maxK - minK)), order);
    }

    private static Phase8AGlobalReductionPlan? BuildGlobalGroup(
        Phase8ADcMeshReductionGroup group,
        Phase8ADcLayoutPlan layout,
        IReadOnlyDictionary<string, Phase8ALocalReductionPlan> localById,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var locals = group.ContributorLocalGroupIds.Select(id => localById[id])
            .OrderBy(item => item.KRange.Offset).ThenBy(item => item.GroupId, StringComparer.Ordinal).ToArray();
        if (locals.Length == 0 || locals.Any(item => item.NShardIndex != group.NShardIndex || item.NRange != group.NRange) ||
            !IsGapFree(locals.Select(item => item.KRange), 0, layout.Request.K))
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, $"$.layout.meshReductionGroups[{group.GroupId}]", "Global contributors must be non-empty, same-N, and cover K exactly once in stable order."));
            return null;
        }

        var collectionCluster = locals[0].ClusterIndex;
        var collectionMesh = inventory.MeshByCluster[collectionCluster];
        var contributors = new List<Phase8AGlobalReductionContributor>();
        foreach (var local in locals)
        {
            IReadOnlyList<TopologyManifestLink> returnRoute = [];
            IReadOnlyList<TopologyManifestLink> meshRoute = [];
            if (locals.Length > 1)
            {
                returnRoute = ResolveEgress(local.TargetComponentId, inventory.MeshByCluster[local.ClusterIndex], inventory, issues);
                if (issues.Count > 0) return null;
                meshRoute = ResolveMeshRoute(inventory.MeshByCluster[local.ClusterIndex], collectionMesh, inventory, issues);
                if (issues.Count > 0) return null;
            }
            contributors.Add(new Phase8AGlobalReductionContributor(
                local.GroupId,
                local.OutputResultId,
                local.TargetComponentId,
                local.ClusterIndex,
                local.KRange,
                returnRoute.Select(link => link.LinkId),
                meshRoute.Select(link => link.LinkId)));
        }

        var reduced = locals.Length > 1;
        var collectorId = reduced ? $"collector:global:n{group.NShardIndex:D4}" : null;
        return new Phase8AGlobalReductionPlan(
            group.GroupId,
            group.GroupKey,
            group.NShardIndex,
            group.NRange,
            locals[0].VectorBits,
            reduced ? Phase8AHierarchicalReductionModes.GroupedVectorSum : Phase8AHierarchicalReductionModes.Bypass,
            collectionCluster,
            collectionMesh.ComponentId,
            collectorId,
            reduced ? Phase8AHierarchicalReductionTargetKinds.DedicatedGroupedVectorCollector : locals[0].TargetKind,
            reduced ? collectorId! : locals[0].TargetComponentId,
            $"global-result:n{group.NShardIndex:D4}",
            contributors);
    }

    private static Phase8AFinalAssemblyPlan? BuildAssembly(
        Phase8ADcLayoutPlan layout,
        IReadOnlyList<Phase8AGlobalReductionPlan> globals,
        IReadOnlyDictionary<string, Phase8ALocalReductionPlan> localById,
        Inventory inventory,
        int assemblyClusterIndex,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var globalById = globals.ToDictionary(item => item.GroupId, StringComparer.Ordinal);
        var assemblyMesh = inventory.MeshByCluster[assemblyClusterIndex];
        var shards = new List<Phase8AFinalAssemblyShardPlan>();
        foreach (var authority in layout.FinalAssemblyShards.OrderBy(item => item.NRange.Offset))
        {
            if (!globalById.TryGetValue(authority.MeshReductionGroupId, out var global) || global.NRange != authority.NRange)
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.AssemblyInvalid, $"$.layout.finalAssemblyShards[{authority.AssemblyShardId}]", "Assembly shard does not match its global reduction output."));
                return null;
            }

            IReadOnlyList<TopologyManifestLink> returnRoute;
            TopologyManifestComponent sourceMesh;
            if (global.Mode == Phase8AHierarchicalReductionModes.Bypass)
            {
                var local = localById[global.Contributors.Single().LocalGroupId];
                sourceMesh = inventory.MeshByCluster[local.ClusterIndex];
                returnRoute = ResolveEgress(local.TargetComponentId, sourceMesh, inventory, issues);
                if (issues.Count > 0) return null;
            }
            else
            {
                sourceMesh = inventory.MeshByCluster[global.CollectionClusterIndex];
                returnRoute = [];
            }
            var meshRoute = ResolveMeshRoute(sourceMesh, assemblyMesh, inventory, issues);
            if (issues.Count > 0) return null;
            shards.Add(new Phase8AFinalAssemblyShardPlan(
                authority.AssemblyShardId,
                authority.NShardIndex,
                authority.NRange,
                global.OutputResultId,
                global.OutputLocationKind,
                global.OutputLocationId,
                sourceMesh.ComponentId,
                returnRoute.Select(link => link.LinkId),
                meshRoute.Select(link => link.LinkId)));
        }

        return new Phase8AFinalAssemblyPlan(
            "collector:final-offset-assembly",
            assemblyClusterIndex,
            assemblyMesh.ComponentId,
            layout.Request.N,
            shards);
    }

    private static IReadOnlyList<TopologyManifestLink> ResolveEgress(
        string sourceComponentId,
        TopologyManifestComponent expectedMesh,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var links = new List<TopologyManifestLink>();
        var current = inventory.Components[sourceComponentId];
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.ComponentId };
        while (current.Role != TopologyPresetComponentRole.MeshRouter)
        {
            var nextId = current.Role switch
            {
                TopologyPresetComponentRole.ProcessingElement => current.AttachmentComponentId,
                TopologyPresetComponentRole.TreeReductionUnit => current.ParentComponentId,
                _ => null
            };
            if (nextId is null || !inventory.Components.TryGetValue(nextId, out var next) || !seen.Add(next.ComponentId) ||
                next.Role is not (TopologyPresetComponentRole.TreeReductionUnit or TopologyPresetComponentRole.MeshRouter))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.RouteUnreachable, $"$.topology.components[{current.ComponentId}]", "Local result has no acyclic typed return route to its cluster mesh router."));
                return [];
            }
            var link = ExactLink(current.ComponentId, next.ComponentId, TopologyPresetLinkRole.PartialSumReturn, inventory, issues);
            if (link is null) return [];
            links.Add(link);
            current = next;
        }
        if (current.ComponentId != expectedMesh.ComponentId)
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.RouteUnreachable, $"$.topology.components[{sourceComponentId}]", "Local result return hierarchy terminates at the wrong cluster mesh router."));
            return [];
        }
        return links;
    }

    private static IReadOnlyList<TopologyManifestLink> ResolveMeshRoute(
        TopologyManifestComponent source,
        TopologyManifestComponent target,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        var current = source;
        var currentCoordinate = source.MeshCoordinate!;
        var targetCoordinate = target.MeshCoordinate!;
        var links = new List<TopologyManifestLink>();
        while (currentCoordinate.Column != targetCoordinate.Column)
        {
            var coordinate = (currentCoordinate.Row, currentCoordinate.Column + Math.Sign(targetCoordinate.Column - currentCoordinate.Column));
            if (!inventory.MeshByCoordinate.TryGetValue(coordinate, out var next))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.RouteUnreachable, "$.topology.mesh", "Column-first mesh routing encountered a missing router."));
                return [];
            }
            var link = ExactLink(current.ComponentId, next.ComponentId, TopologyPresetLinkRole.MeshTransport, inventory, issues);
            if (link is null) return [];
            links.Add(link);
            current = next;
            currentCoordinate = next.MeshCoordinate!;
        }
        while (currentCoordinate.Row != targetCoordinate.Row)
        {
            var coordinate = (currentCoordinate.Row + Math.Sign(targetCoordinate.Row - currentCoordinate.Row), currentCoordinate.Column);
            if (!inventory.MeshByCoordinate.TryGetValue(coordinate, out var next))
            {
                issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.RouteUnreachable, "$.topology.mesh", "Column-first mesh routing encountered a missing router."));
                return [];
            }
            var link = ExactLink(current.ComponentId, next.ComponentId, TopologyPresetLinkRole.MeshTransport, inventory, issues);
            if (link is null) return [];
            links.Add(link);
            current = next;
            currentCoordinate = next.MeshCoordinate!;
        }
        return links;
    }

    private static TopologyManifestLink? ExactLink(
        string source,
        string destination,
        TopologyPresetLinkRole role,
        Inventory inventory,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        if (!inventory.ExactLinks.TryGetValue((source, destination, role), out var links) || links.Length != 1)
        {
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.LinkInvalid, "$.topology.links", $"Expected exactly one {role} link from '{source}' to '{destination}'."));
            return null;
        }
        return links[0];
    }

    private static Phase8AHierarchicalReductionSummary Summarize(
        IReadOnlyList<Phase8ALocalReductionPlan> locals,
        IReadOnlyList<Phase8AGlobalReductionPlan> globals,
        Phase8AFinalAssemblyPlan assembly) => new(
        locals.Sum(group => group.Contributors.Count),
        locals.Count,
        locals.Count(group => group.Mode == Phase8AHierarchicalReductionModes.Bypass),
        locals.Count(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum),
        locals.Sum(group => group.Stages.Count),
        locals.Sum(group => group.ForwardingReductionComponentIds.Count),
        locals.Sum(group => group.AddOperationCount),
        locals.Sum(group => group.TreeEdges.Count),
        locals.Count,
        globals.Count,
        globals.Count(group => group.Mode == Phase8AHierarchicalReductionModes.Bypass),
        globals.Count(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum),
        globals.Sum(group => group.Contributors.Count),
        globals.Sum(group => group.AddOperationCount),
        globals.Sum(group => group.Contributors.Sum(contributor => contributor.ReturnRouteLinkIds.Count)),
        globals.Sum(group => group.Contributors.Sum(contributor => contributor.MeshRouteLinkIds.Count)),
        assembly.Shards.Count,
        assembly.Shards.Sum(shard => shard.ReturnRouteLinkIds.Count),
        assembly.Shards.Sum(shard => shard.MeshRouteLinkIds.Count),
        locals.Sum(group => checked(group.VectorBits * group.Contributors.Count)),
        locals.Sum(group => checked(group.VectorBits * group.TreeEdges.Count)),
        locals.Sum(group => group.VectorBits),
        globals.Sum(group => checked(group.VectorBits * group.Contributors.Sum(contributor => contributor.RouteLinkIds.Count))),
        globals.Sum(group => group.VectorBits),
        assembly.Shards.Sum(shard => checked(globals.Single(group => group.NShardIndex == shard.NShardIndex).VectorBits *
                                             (shard.ReturnRouteLinkIds.Count + shard.MeshRouteLinkIds.Count))));

    private static void ValidateConservation(
        Phase8ADcLayoutPlan layout,
        IReadOnlyList<Phase8ALocalReductionPlan> locals,
        IReadOnlyList<Phase8AGlobalReductionPlan> globals,
        Phase8AFinalAssemblyPlan assembly,
        Phase8AHierarchicalReductionSummary summary,
        List<Phase8AHierarchicalReductionIssue> issues)
    {
        if (summary.PePartialCount != layout.Assignments.Count ||
            summary.LocalResultCount != layout.LocalReductionGroups.Count ||
            summary.GlobalContributorPacketCount != layout.LocalReductionGroups.Count ||
            summary.FinalAssemblyShardCount != layout.FinalAssemblyShards.Count)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, "$.summary", "PE partials, local results, global contributors, and final shards do not conserve exactly."));
        if (summary.LocalAddOperationCount != layout.Summary.LocalAddOperationCount ||
            summary.GlobalAddOperationCount != layout.Summary.MeshAddOperationCount ||
            summary.LocalAddOperationCount + summary.GlobalAddOperationCount != summary.PePartialCount - summary.FinalAssemblyShardCount)
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, "$.summary.addOperations", "Local plus global add counts disagree with D/C layout conservation."));
        if (locals.Any(group => group.Mode == Phase8AHierarchicalReductionModes.Bypass &&
                                (group.Contributors.Count != 1 || group.Stages.Count != 0 || group.TreeEdges.Count != 0)) ||
            locals.Any(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum &&
                                (group.Contributors.Count < 2 || group.Stages.Count != group.Contributors.Count - 1)))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.TreeInvalid, "$.localGroups", "Local bypass and numerical-stage cardinalities are inconsistent."));
        if (globals.Any(group => group.Mode == Phase8AHierarchicalReductionModes.Bypass &&
                                 (group.Contributors.Count != 1 || group.CollectorRequirementId is not null)) ||
            globals.Any(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum &&
                                 (group.Contributors.Count < 2 || group.CollectorRequirementId is null)))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.GroupMismatch, "$.globalGroups", "Global bypass and dedicated collector requirements are inconsistent."));
        if (!IsGapFree(assembly.Shards.Select(shard => shard.NRange), 0, assembly.OutputExtent))
            issues.Add(Issue(Phase8AHierarchicalReductionIssueCodes.AssemblyInvalid, "$.finalAssembly", "Final assembly shards must cover the output exactly once."));
    }

    private static bool IsGapFree(IEnumerable<MappingIndexRange> ranges, long expectedStart, long expectedEnd)
    {
        var next = expectedStart;
        foreach (var range in ranges.OrderBy(item => item.Offset))
        {
            if (range.Extent <= 0 || range.Offset != next) return false;
            next = checked(range.Offset + range.Extent);
        }
        return next == expectedEnd;
    }

    private static string ComputeHash(
        Phase8ADcLayoutPlan layout,
        TopologyManifest manifest,
        int vectorBitWidth,
        IReadOnlyList<Phase8ALocalReductionPlan> locals,
        IReadOnlyList<Phase8AGlobalReductionPlan> globals,
        Phase8AFinalAssemblyPlan assembly,
        Phase8AHierarchicalReductionSummary summary)
    {
        var json = JsonSerializer.Serialize(new
        {
            algorithm = Phase8AHierarchicalReductionPlan.CanonicalHashAlgorithm,
            layoutHash = layout.CanonicalHash,
            topologyManifestHash = manifest.CanonicalHash,
            topologyGraphHash = manifest.TopologyGraphHash,
            vectorBitWidth,
            localGroups = locals,
            globalGroups = globals,
            finalAssembly = assembly,
            summary
        }, HardwareGraphJson.Options);
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(json));
    }

    private static Phase8AHierarchicalReductionIssue Issue(string code, string location, string message) => new(code, location, message);
    private static Phase8AHierarchicalReductionResult Failure(params Phase8AHierarchicalReductionIssue[] issues) => new(null, issues);
    private static Phase8AHierarchicalReductionResult Failure(IEnumerable<Phase8AHierarchicalReductionIssue> issues) => new(null, issues);

    private sealed record NodeResolution(
        string ResultId,
        IReadOnlyList<string> ContributorAssignmentIds,
        MappingIndexRange KRange,
        int StageOrder);

    private sealed record Inventory(
        TopologyManifest Manifest,
        IReadOnlyDictionary<string, TopologyManifestComponent> Components,
        IReadOnlyDictionary<string, TopologyManifestLink> Links,
        IReadOnlyDictionary<(string Source, string Destination, TopologyPresetLinkRole Role), TopologyManifestLink[]> ExactLinks,
        IReadOnlyDictionary<int, TopologyManifestComponent> MeshByCluster,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, TopologyManifestComponent>> PeByCluster,
        IReadOnlyDictionary<(int Row, int Column), TopologyManifestComponent> MeshByCoordinate);
}
