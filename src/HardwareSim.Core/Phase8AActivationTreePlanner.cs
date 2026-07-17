using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Resolves D/C activation targets into exact cluster-aware typed MoT trees.</summary>
public static class Phase8AActivationTreePlanner
{
    /// <summary>Builds an immutable activation tree plan without mutating the layout or topology graph.</summary>
    public static Phase8AActivationTreeResult Plan(
        Phase8ADcLayoutPlan? layout,
        HardwareGraph? topologyGraph,
        int activationBitWidth = 8,
        int ingressClusterIndex = 0,
        string ingressPolicyId = Phase8AActivationIngressPolicies.SingleTopLeft,
        IReadOnlyDictionary<int, int>? ingressClusterByGlobalKTile = null)
    {
        var issues = new List<Phase8AActivationTreeIssue>();
        if (layout is null)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.LayoutMissing, "$.layout", "A resolved D/C layout plan is required."));
        if (topologyGraph is null)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TopologyMissing, "$.topology", "A typed topology graph is required."));
        if (activationBitWidth <= 0)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.BitWidthInvalid, "$.activationBitWidth", "Activation bit width must be positive."));
        if (issues.Count > 0) return Failure(issues);

        TopologyManifestReadResult read;
        try
        {
            read = TopologyManifestJson.ReadFromGraph(topologyGraph!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return Failure(Issue(
                Phase8AActivationTreeIssueCodes.ManifestInvalid,
                "$.topology.topology_manifest",
                $"The typed topology manifest could not be read: {exception.Message}"));
        }

        var readErrors = read.Issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToArray();
        if (read.Manifest is null || readErrors.Length > 0)
        {
            var detail = string.Join("; ", readErrors.Select(issue => $"{issue.Code}: {issue.Message}"));
            return Failure(Issue(
                Phase8AActivationTreeIssueCodes.ManifestInvalid,
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
                Phase8AActivationTreeIssueCodes.TopologyStale,
                "$.topology" + issue.Location.TrimStart('$'),
                $"{issue.Code}: {issue.Message}")));
        }

        var manifest = read.Manifest;
        var clusterCount = layout!.Summary.ClusterCount;
        if (!string.Equals(manifest.Request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal))
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TopologyMismatch, "$.topology.request.topologyId", "The D/C activation tree requires the typed Mesh-of-Trees v1 topology."));
        if (manifest.Request.ClusterSize != layout.Request.ClusterSize)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TopologyMismatch, "$.topology.request.clusterSize", "Topology cluster size must equal the resolved D/C cluster size."));
        if (manifest.Request.ClusterCount != clusterCount)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TopologyMismatch, "$.topology.request.mesh", "Topology mesh capacity must equal the resolved D/C cluster count."));
        if (ingressClusterByGlobalKTile is null && (ingressClusterIndex < 0 || ingressClusterIndex >= clusterCount))
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.IngressInvalid, "$.ingressClusterIndex", "Ingress cluster index is outside the resolved cluster inventory."));
        if (string.IsNullOrWhiteSpace(ingressPolicyId))
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.IngressInvalid, "$.ingressPolicyId", "Ingress policy id is required."));
        ValidateLayoutAuthority(layout, issues);
        if (issues.Count > 0) return Failure(issues);

        var kTileIndices = layout.Assignments.Select(item => item.GlobalKTileIndex).Distinct().OrderBy(value => value).ToArray();
        var ingressAssignments = kTileIndices.Select(globalKTileIndex => new Phase8AActivationIngressAssignment(
            globalKTileIndex,
            ingressClusterByGlobalKTile is null
                ? ingressClusterIndex
                : ingressClusterByGlobalKTile.TryGetValue(globalKTileIndex, out var selected) ? selected : -1)).ToArray();
        if (ingressAssignments.Any(item => item.ClusterIndex < 0 || item.ClusterIndex >= clusterCount) ||
            ingressClusterByGlobalKTile is not null &&
            (ingressClusterByGlobalKTile.Count != ingressAssignments.Length ||
             ingressClusterByGlobalKTile.Keys.Any(key => !kTileIndices.Contains(key))))
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.IngressInvalid, "$.ingressAssignments", "Ingress assignments must cover every global K tile exactly once with an in-range cluster index."));
            return Failure(issues);
        }
        var commonIngress = ingressAssignments.Select(item => item.ClusterIndex).Distinct().Take(2).Count() == 1
            ? ingressAssignments[0].ClusterIndex
            : -1;
        var ingressByTile = ingressAssignments.ToDictionary(item => item.GlobalKTileIndex, item => item.ClusterIndex);

        var inventory = BuildInventory(layout, manifest, issues);
        if (inventory is null || issues.Count > 0) return Failure(issues);

        try
        {
            var trees = new List<Phase8AActivationTileTree>();
            foreach (var assignments in layout.Assignments
                         .GroupBy(item => item.GlobalKTileIndex)
                         .OrderBy(group => group.Key))
            {
                var tree = BuildTree(assignments.ToArray(), layout, inventory, activationBitWidth, ingressByTile[assignments.Key], issues);
                if (tree is null) return Failure(issues);
                trees.Add(tree);
            }

            var summary = Summarize(trees);
            var hash = ComputeHash(layout, manifest, ingressPolicyId, ingressAssignments, activationBitWidth, trees, summary);
            return new Phase8AActivationTreeResult(new Phase8AActivationTreePlan(
                layout.CanonicalHash,
                manifest.CanonicalHash,
                manifest.TopologyGraphHash,
                commonIngress,
                ingressPolicyId,
                ingressAssignments,
                activationBitWidth,
                trees,
                summary,
                hash), []);
        }
        catch (OverflowException)
        {
            return Failure(Issue(
                Phase8AActivationTreeIssueCodes.ArithmeticOverflow,
                "$",
                "Activation packet, link, branch, or conservation arithmetic exceeded the supported range."));
        }
    }

    private static void ValidateLayoutAuthority(Phase8ADcLayoutPlan layout, List<Phase8AActivationTreeIssue> issues)
    {
        var assignmentById = layout.Assignments
            .GroupBy(item => item.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (assignmentById.Any(pair => pair.Value.Length != 1))
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, "$.layout.assignments", "D/C assignment identities must be unique."));
            return;
        }

        var deliveryTargets = layout.ActivationDeliveries.SelectMany(delivery => delivery.TargetAssignmentIds).ToArray();
        if (deliveryTargets.Length != layout.Assignments.Count ||
            deliveryTargets.Distinct(StringComparer.Ordinal).Count() != deliveryTargets.Length ||
            deliveryTargets.Any(id => !assignmentById.ContainsKey(id)))
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, "$.layout.activationDeliveries", "Activation deliveries must cover every D/C assignment exactly once."));
        }

        foreach (var delivery in layout.ActivationDeliveries)
        {
            if (delivery.TargetAssignmentIds.Count != delivery.TargetPeOrdinals.Count)
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, $"$.layout.activationDeliveries[{delivery.DeliveryId}]", "Assignment and PE target counts disagree."));
                continue;
            }

            for (var index = 0; index < delivery.TargetAssignmentIds.Count; index++)
            {
                if (!assignmentById.TryGetValue(delivery.TargetAssignmentIds[index], out var candidates) ||
                    candidates.Length != 1 ||
                    candidates[0].GlobalKTileIndex != delivery.GlobalKTileIndex ||
                    candidates[0].ClusterIndex != delivery.ClusterIndex ||
                    candidates[0].PeOrdinal != delivery.TargetPeOrdinals[index])
                {
                    issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, $"$.layout.activationDeliveries[{delivery.DeliveryId}]", "A delivery target disagrees with its authoritative D/C assignment."));
                    break;
                }
            }
        }
    }

    private static Inventory? BuildInventory(
        Phase8ADcLayoutPlan layout,
        TopologyManifest manifest,
        List<Phase8AActivationTreeIssue> issues)
    {
        var duplicateComponents = manifest.Components.GroupBy(item => item.ComponentId, StringComparer.Ordinal)
            .Where(group => group.Count() != 1).Select(group => group.Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var duplicateLinks = manifest.Links.GroupBy(item => item.LinkId, StringComparer.Ordinal)
            .Where(group => group.Count() != 1).Select(group => group.Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (duplicateComponents.Length > 0)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.InventoryInvalid, "$.topology.components", $"Duplicate component identities: {string.Join(", ", duplicateComponents)}."));
        if (duplicateLinks.Length > 0)
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.LinkInvalid, "$.topology.links", $"Duplicate link identities: {string.Join(", ", duplicateLinks)}."));
        if (issues.Count > 0) return null;

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
            if (mesh.Length != 1 || mesh[0].MeshCoordinate is null)
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.InventoryInvalid, $"$.topology.clusters[{cluster}]", "Each cluster must have exactly one mesh router with an explicit mesh coordinate."));
                continue;
            }

            var coordinate = mesh[0].MeshCoordinate!;
            var expectedCluster = checked(coordinate.Row * manifest.Request.MeshColumns + coordinate.Column);
            if (coordinate.Row < 0 || coordinate.Row >= manifest.Request.MeshRows ||
                coordinate.Column < 0 || coordinate.Column >= manifest.Request.MeshColumns || expectedCluster != cluster ||
                !meshByCoordinate.TryAdd((coordinate.Row, coordinate.Column), mesh[0]))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.InventoryInvalid, $"$.topology.clusters[{cluster}].meshCoordinate", "Mesh coordinates must be unique, in bounds, and row-major consistent with cluster indices."));
                continue;
            }
            meshByCluster[cluster] = mesh[0];

            var peItems = manifest.Components
                .Where(item => item.Role == TopologyPresetComponentRole.ProcessingElement && item.ClusterIndex == cluster)
                .OrderBy(item => item.Coordinate.Row)
                .ThenBy(item => item.Coordinate.Column)
                .ThenBy(item => item.ComponentId, StringComparer.Ordinal)
                .ToArray();
            if (peItems.Length != layout.Request.ClusterSize ||
                peItems.Select(item => (item.Coordinate.Row, item.Coordinate.Column)).Distinct().Count() != peItems.Length)
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.InventoryInvalid, $"$.topology.clusters[{cluster}].processingElements", "Each cluster must contain exactly C processing elements at unique coordinates."));
                continue;
            }
            peByCluster[cluster] = peItems.Select((component, ordinal) => (component, ordinal))
                .ToDictionary(item => item.ordinal, item => item.component);
        }

        if (meshByCluster.Count != layout.Summary.ClusterCount ||
            peByCluster.Count != layout.Summary.ClusterCount ||
            meshByCoordinate.Count != layout.Summary.ClusterCount)
        {
            return null;
        }

        return new Inventory(manifest, components, links, exactLinks, meshByCluster, peByCluster, meshByCoordinate);
    }

    private static Phase8AActivationTileTree? BuildTree(
        IReadOnlyList<Phase8ADcPeAssignment> assignments,
        Phase8ADcLayoutPlan layout,
        Inventory inventory,
        int activationBitWidth,
        int ingressClusterIndex,
        List<Phase8AActivationTreeIssue> issues)
    {
        var first = assignments.OrderBy(item => item.AssignmentId, StringComparer.Ordinal).First();
        if (assignments.Any(item => item.ActivationTileId != first.ActivationTileId || item.KRange != first.KRange))
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, $"$.layout.kTiles[{first.GlobalKTileIndex}]", "One global K tile must identify one activation tile and exact K range."));
            return null;
        }

        var bits = checked(first.KRange.Extent * activationBitWidth);
        var source = inventory.MeshByCluster[ingressClusterIndex];
        var meshPrefixes = new Dictionary<int, IReadOnlyList<TopologyManifestLink>>();
        var targets = new List<Phase8AActivationTreeTarget>();
        var targetRoutes = new Dictionary<string, IReadOnlyList<TopologyManifestLink>>(StringComparer.Ordinal);
        foreach (var assignment in assignments.OrderBy(item => item.AssignmentId, StringComparer.Ordinal))
        {
            if (!inventory.PeByCluster.TryGetValue(assignment.ClusterIndex, out var byOrdinal) ||
                !byOrdinal.TryGetValue(assignment.PeOrdinal, out var pe))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, $"$.layout.assignments[{assignment.AssignmentId}]", "The D/C PE ordinal has no coordinate-derived typed PE endpoint."));
                return null;
            }

            if (!meshPrefixes.TryGetValue(assignment.ClusterIndex, out var meshPrefix))
            {
                meshPrefix = ResolveMeshPrefix(source, inventory.MeshByCluster[assignment.ClusterIndex], inventory, issues);
                if (issues.Count > 0) return null;
                meshPrefixes[assignment.ClusterIndex] = meshPrefix;
            }
            var local = ResolveLocalRoute(pe, inventory.MeshByCluster[assignment.ClusterIndex], inventory, issues);
            if (issues.Count > 0) return null;
            var complete = meshPrefix.Concat(local).ToArray();
            if (complete.Select(item => item.LinkId).Distinct(StringComparer.Ordinal).Count() != complete.Length)
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.Cycle, $"$.trees[{first.GlobalKTileIndex}].targets[{assignment.AssignmentId}]", "The exact source-to-PE route repeats a directed link."));
                return null;
            }
            targetRoutes[assignment.AssignmentId] = complete;
            targets.Add(new Phase8AActivationTreeTarget(
                assignment.AssignmentId,
                assignment.ClusterIndex,
                assignment.PeOrdinal,
                pe.ComponentId,
                $"activation-path:k{first.GlobalKTileIndex:D4}:{assignment.AssignmentId}",
                complete.Select(item => item.LinkId)));
        }

        var edgeTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            foreach (var link in targetRoutes[target.AssignmentId])
            {
                if (!edgeTargets.TryGetValue(link.LinkId, out var set)) edgeTargets[link.LinkId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(target.AssignmentId);
            }
        }

        var usedLinks = edgeTargets.Keys.Select(id => inventory.Links[id]).ToArray();
        if (!TryResolveTreeDepths(source.ComponentId, usedLinks, targets, out var depths, out var treeIssue))
        {
            issues.Add(treeIssue!);
            return null;
        }

        var edges = usedLinks.Select(link => new Phase8AActivationTreeEdge(
            link.LinkId,
            link.SourceComponentId,
            link.DestinationComponentId,
            link.Role,
            link.Scope,
            link.ClusterIndex,
            depths[link.DestinationComponentId],
            bits,
            edgeTargets[link.LinkId])).ToArray();
        var incoming = usedLinks.ToDictionary(link => link.DestinationComponentId, link => link.LinkId, StringComparer.Ordinal);
        var outgoing = usedLinks.GroupBy(link => link.SourceComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(link => link.LinkId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var branches = new List<Phase8AActivationBranchPoint>();
        foreach (var pair in outgoing.Where(pair => pair.Value.Length > 1).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var component = inventory.Components[pair.Key];
            if (component.Role is not (TopologyPresetComponentRole.MeshRouter or TopologyPresetComponentRole.TreeRouter))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.InventoryInvalid, $"$.topology.components[{component.ComponentId}]", "Only typed mesh or tree routers may branch activation traffic."));
                return null;
            }
            branches.Add(new Phase8AActivationBranchPoint(
                $"activation-branch:k{first.GlobalKTileIndex:D4}:{component.ComponentId}",
                component.ComponentId,
                component.Role,
                component.ClusterIndex,
                depths[component.ComponentId],
                incoming.GetValueOrDefault(component.ComponentId),
                pair.Value.Select(link => link.LinkId),
                bits,
                checked(bits * pair.Value.Length)));
        }

        if (branches.Sum(branch => branch.AdditionalCloneCount) != targets.Count - 1)
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, $"$.trees[{first.GlobalKTileIndex}]", "Branch clones do not conserve one source packet into the exact PE target count."));
            return null;
        }

        var clusters = targets.GroupBy(target => target.ClusterIndex).OrderBy(group => group.Key).Select(group =>
        {
            var localLinkIds = group.SelectMany(target => targetRoutes[target.AssignmentId])
                .Where(link => link.Role == TopologyPresetLinkRole.ActivationDistribution)
                .Select(link => link.LinkId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var localBranchIds = branches.Where(branch => branch.ComponentRole == TopologyPresetComponentRole.TreeRouter && branch.ClusterIndex == group.Key)
                .Select(branch => branch.BranchId).ToArray();
            return new Phase8AActivationClusterTree(
                group.Key,
                inventory.MeshByCluster[group.Key].ComponentId,
                meshPrefixes[group.Key].Select(link => link.LinkId),
                localLinkIds,
                localBranchIds,
                group);
        }).ToArray();

        var expectedDeliveries = layout.ActivationDeliveries.Where(delivery => delivery.GlobalKTileIndex == first.GlobalKTileIndex).ToArray();
        if (clusters.Length != expectedDeliveries.Length ||
            !clusters.SelectMany(cluster => cluster.Targets).Select(target => target.AssignmentId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedDeliveries.SelectMany(delivery => delivery.TargetAssignmentIds)))
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetMismatch, $"$.trees[{first.GlobalKTileIndex}]", "Resolved cluster trees disagree with D/C activation delivery authority."));
            return null;
        }

        return new Phase8AActivationTileTree(
            $"activation-tree:k{first.GlobalKTileIndex:D4}",
            first.ActivationTileId,
            first.GlobalKTileIndex,
            first.KRange,
            ingressClusterIndex,
            source.ComponentId,
            targets.Count == 1 ? Phase8ACommunicationFlowKinds.Unicast : Phase8ACommunicationFlowKinds.Multicast,
            bits,
            targets,
            edges,
            branches,
            clusters);
    }

    private static IReadOnlyList<TopologyManifestLink> ResolveMeshPrefix(
        TopologyManifestComponent source,
        TopologyManifestComponent target,
        Inventory inventory,
        List<Phase8AActivationTreeIssue> issues)
    {
        var current = source;
        var currentCoordinate = source.MeshCoordinate!;
        var targetCoordinate = target.MeshCoordinate!;
        var links = new List<TopologyManifestLink>();

        while (currentCoordinate.Column != targetCoordinate.Column)
        {
            var nextCoordinate = (currentCoordinate.Row, currentCoordinate.Column + Math.Sign(targetCoordinate.Column - currentCoordinate.Column));
            if (!inventory.MeshByCoordinate.TryGetValue(nextCoordinate, out var next))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetUnreachable, $"$.topology.mesh[{nextCoordinate.Row},{nextCoordinate.Item2}]", "Column-first mesh routing encountered a missing mesh router."));
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
            var nextCoordinate = (currentCoordinate.Row + Math.Sign(targetCoordinate.Row - currentCoordinate.Row), currentCoordinate.Column);
            if (!inventory.MeshByCoordinate.TryGetValue(nextCoordinate, out var next))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetUnreachable, $"$.topology.mesh[{nextCoordinate.Item1},{nextCoordinate.Column}]", "Column-first mesh routing encountered a missing mesh router."));
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

    private static IReadOnlyList<TopologyManifestLink> ResolveLocalRoute(
        TopologyManifestComponent pe,
        TopologyManifestComponent expectedMesh,
        Inventory inventory,
        List<Phase8AActivationTreeIssue> issues)
    {
        var reverseComponents = new List<TopologyManifestComponent> { pe };
        var seen = new HashSet<string>(StringComparer.Ordinal) { pe.ComponentId };
        var current = pe;
        while (current.Role != TopologyPresetComponentRole.MeshRouter)
        {
            if (current.ParentComponentId is null || !inventory.Components.TryGetValue(current.ParentComponentId, out var parent))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetUnreachable, $"$.topology.components[{current.ComponentId}].parentComponentId", "The PE hierarchy has no complete path to its mesh router."));
                return [];
            }
            if (!seen.Add(parent.ComponentId))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.Cycle, $"$.topology.components[{parent.ComponentId}]", "The typed activation hierarchy contains a parent cycle."));
                return [];
            }
            if (parent.ClusterIndex != pe.ClusterIndex || parent.Role is not (TopologyPresetComponentRole.TreeRouter or TopologyPresetComponentRole.MeshRouter))
            {
                issues.Add(Issue(Phase8AActivationTreeIssueCodes.InventoryInvalid, $"$.topology.components[{parent.ComponentId}]", "An activation parent must be a tree or mesh router in the same cluster."));
                return [];
            }
            reverseComponents.Add(parent);
            current = parent;
        }
        if (!string.Equals(current.ComponentId, expectedMesh.ComponentId, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.TargetUnreachable, $"$.topology.components[{pe.ComponentId}]", "The PE hierarchy terminates at the wrong cluster mesh router."));
            return [];
        }

        reverseComponents.Reverse();
        var links = new List<TopologyManifestLink>();
        for (var index = 0; index < reverseComponents.Count - 1; index++)
        {
            var link = ExactLink(reverseComponents[index].ComponentId, reverseComponents[index + 1].ComponentId, TopologyPresetLinkRole.ActivationDistribution, inventory, issues);
            if (link is null) return [];
            links.Add(link);
        }
        return links;
    }

    private static TopologyManifestLink? ExactLink(
        string source,
        string destination,
        TopologyPresetLinkRole role,
        Inventory inventory,
        List<Phase8AActivationTreeIssue> issues)
    {
        if (!inventory.ExactLinks.TryGetValue((source, destination, role), out var links) || links.Length != 1)
        {
            issues.Add(Issue(Phase8AActivationTreeIssueCodes.LinkInvalid, "$.topology.links", $"Expected exactly one {role} link from '{source}' to '{destination}'."));
            return null;
        }
        return links[0];
    }

    private static bool TryResolveTreeDepths(
        string sourceComponentId,
        IReadOnlyList<TopologyManifestLink> links,
        IReadOnlyList<Phase8AActivationTreeTarget> targets,
        out IReadOnlyDictionary<string, int> depths,
        out Phase8AActivationTreeIssue? issue)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (parent.TryGetValue(link.DestinationComponentId, out var existing) && !string.Equals(existing, link.SourceComponentId, StringComparison.Ordinal))
            {
                depths = new Dictionary<string, int>();
                issue = Issue(Phase8AActivationTreeIssueCodes.Reconvergence, "$.trees", $"Component '{link.DestinationComponentId}' has more than one activation parent.");
                return false;
            }
            parent[link.DestinationComponentId] = link.SourceComponentId;
        }

        var outgoing = links.GroupBy(link => link.SourceComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal) { [sourceComponentId] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(sourceComponentId);
        while (queue.Count > 0)
        {
            var component = queue.Dequeue();
            foreach (var link in outgoing.GetValueOrDefault(component) ?? [])
            {
                if (resolved.ContainsKey(link.DestinationComponentId))
                {
                    depths = resolved;
                    issue = Issue(Phase8AActivationTreeIssueCodes.Cycle, "$.trees", $"Activation tree contains a cycle through '{link.DestinationComponentId}'.");
                    return false;
                }
                resolved[link.DestinationComponentId] = checked(resolved[component] + 1);
                queue.Enqueue(link.DestinationComponentId);
            }
        }

        if (links.Any(link => !resolved.ContainsKey(link.SourceComponentId) || !resolved.ContainsKey(link.DestinationComponentId)) ||
            targets.Any(target => !resolved.ContainsKey(target.PeComponentId)))
        {
            depths = resolved;
            issue = Issue(Phase8AActivationTreeIssueCodes.TargetUnreachable, "$.trees", "An activation edge or PE target is not reachable from the selected ingress.");
            return false;
        }
        depths = resolved;
        issue = null;
        return true;
    }

    private static Phase8AActivationTreeSummary Summarize(IReadOnlyList<Phase8AActivationTileTree> trees) => new(
        trees.Count,
        trees.Sum(tree => tree.Clusters.Count),
        trees.Sum(tree => tree.Targets.Count),
        trees.Sum(tree => tree.Edges.Count),
        trees.Sum(tree => tree.Edges.Count(edge => edge.Role == TopologyPresetLinkRole.MeshTransport)),
        trees.Sum(tree => tree.Edges.Count(edge => edge.Role == TopologyPresetLinkRole.ActivationDistribution)),
        trees.Sum(tree => tree.BranchPoints.Where(branch => branch.ComponentRole == TopologyPresetComponentRole.MeshRouter).Sum(branch => branch.OutputPacketCount)),
        trees.Sum(tree => tree.BranchPoints.Where(branch => branch.ComponentRole == TopologyPresetComponentRole.TreeRouter).Sum(branch => branch.OutputPacketCount)),
        trees.Sum(tree => tree.BranchPoints.Sum(branch => branch.AdditionalCloneCount)),
        trees.Sum(tree => tree.Bits),
        trees.Sum(tree => checked(tree.Bits * tree.Clusters.Count)),
        trees.Sum(tree => checked(tree.Bits * tree.Targets.Count)),
        trees.Sum(tree => checked(tree.Bits * tree.Edges.Count)),
        trees.Sum(tree => tree.BranchPoints.Sum(branch => branch.InputBits)),
        trees.Sum(tree => tree.BranchPoints.Sum(branch => branch.OutputBits)));

    private static string ComputeHash(
        Phase8ADcLayoutPlan layout,
        TopologyManifest manifest,
        string ingressPolicyId,
        IReadOnlyList<Phase8AActivationIngressAssignment> ingressAssignments,
        int activationBitWidth,
        IReadOnlyList<Phase8AActivationTileTree> trees,
        Phase8AActivationTreeSummary summary)
    {
        var json = JsonSerializer.Serialize(new
        {
            algorithm = Phase8AActivationTreePlan.CanonicalHashAlgorithm,
            layoutHash = layout.CanonicalHash,
            topologyManifestHash = manifest.CanonicalHash,
            topologyGraphHash = manifest.TopologyGraphHash,
            ingressPolicyId,
            ingressAssignments,
            activationBitWidth,
            trees,
            summary
        }, HardwareGraphJson.Options);
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(json));
    }

    private static Phase8AActivationTreeIssue Issue(string code, string location, string message) => new(code, location, message);
    private static Phase8AActivationTreeResult Failure(params Phase8AActivationTreeIssue[] issues) => new(null, issues);
    private static Phase8AActivationTreeResult Failure(IEnumerable<Phase8AActivationTreeIssue> issues) => new(null, issues);

    private sealed record Inventory(
        TopologyManifest Manifest,
        IReadOnlyDictionary<string, TopologyManifestComponent> Components,
        IReadOnlyDictionary<string, TopologyManifestLink> Links,
        IReadOnlyDictionary<(string Source, string Destination, TopologyPresetLinkRole Role), TopologyManifestLink[]> ExactLinks,
        IReadOnlyDictionary<int, TopologyManifestComponent> MeshByCluster,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, TopologyManifestComponent>> PeByCluster,
        IReadOnlyDictionary<(int Row, int Column), TopologyManifestComponent> MeshByCoordinate);
}
