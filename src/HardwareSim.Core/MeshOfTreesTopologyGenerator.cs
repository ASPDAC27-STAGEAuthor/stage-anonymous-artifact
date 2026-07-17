using System.Globalization;

namespace HardwareSim.Core;

internal static class MeshOfTreesTopologyGenerator
{
    internal static TopologyBuildResult Generate(
        TopologyPresetRequest request,
        IEnumerable<TopologyBuildIssue> existingIssues)
    {
        var issues = existingIssues.ToList();
        var graph = CreateGraphShell(request);
        var components = new List<TopologyManifestComponent>();
        var links = new List<TopologyManifestLink>();
        var contexts = CreateComponents(request, graph, components);
        CreateClusterLinks(request, graph, contexts, links);
        CreateMeshLinks(request, graph, contexts, links);
        FinalizeComponentPortCounts(graph);
        PlaceComponents(graph, components);
        CreatePhysicalRoutes(request, graph);
        ValidatePhysicalRoutes(graph, issues);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new TopologyBuildResult(graph, null, issues);
        }

        var manifest = TopologyPresetCanonicalizer.CreateManifest(
            request,
            graph,
            components,
            links,
            MeshOfTreesTopologyPresetBuilder.BuilderId,
            MeshOfTreesTopologyPresetBuilder.BuilderVersion,
            MeshOfTreesTopologyPresetBuilder.ProvenanceSource);
        var persistedGraph = TopologyManifestJson.AttachToGraph(graph, manifest);
        var readback = TopologyManifestJson.ReadFromGraph(persistedGraph);
        issues.AddRange(readback.Issues);
        if (!readback.IsSuccess)
        {
            return new TopologyBuildResult(persistedGraph, null, issues);
        }

        return new TopologyBuildResult(persistedGraph, manifest, issues);
    }

    private static HardwareGraph CreateGraphShell(TopologyPresetRequest request)
    {
        var treeDepth = Log2(request.ClusterSize);
        var blockRows = checked(treeDepth * 3 + 3);
        var blockColumns = checked(request.ClusterSize * 2 + 2);
        return new HardwareGraph
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["topology_preset_id"] = request.TopologyId,
                ["topology_builder_id"] = MeshOfTreesTopologyPresetBuilder.BuilderId,
                ["topology_builder_version"] = MeshOfTreesTopologyPresetBuilder.BuilderVersion,
                ["topology_provenance_source"] = MeshOfTreesTopologyPresetBuilder.ProvenanceSource,
                ["topology_tree_fanout"] = "2",
                ["topology_word_bits"] = Invariant(request.WordBits),
                ["physical_route_contract"] = "explicit-per-logical-link"
            },
            Placement = new PhysicalPlacement
            {
                Rows = checked(request.MeshRows * blockRows),
                Cols = checked(request.MeshColumns * blockColumns),
                CellWidthMicrometers = request.PlacementCellSizeMicrometers,
                CellHeightMicrometers = request.PlacementCellSizeMicrometers,
                Origin = new PhysicalPoint(0, 0),
                Layer = "M0",
                FloorplanMetadata =
                {
                    ["layout"] = "mesh-grid-recursive-alternating-axis-tree",
                    ["provenance"] = MeshOfTreesTopologyPresetBuilder.ProvenanceSource
                }
            },
            Routing = new PhysicalRouting()
        };
    }

    private static IReadOnlyList<ClusterContext> CreateComponents(
        TopologyPresetRequest request,
        HardwareGraph graph,
        List<TopologyManifestComponent> manifestComponents)
    {
        var contexts = new List<ClusterContext>();
        var componentRegistry = ComponentTypeRegistry.CreateDefault();
        var treeDepth = Log2(request.ClusterSize);
        var internalNodeCount = request.ClusterSize - 1;
        var blockRows = checked(treeDepth * 3 + 3);
        var blockColumns = checked(request.ClusterSize * 2 + 2);
        for (var meshRow = 0; meshRow < request.MeshRows; meshRow++)
        {
            for (var meshColumn = 0; meshColumn < request.MeshColumns; meshColumn++)
            {
                var clusterIndex = checked(meshRow * request.MeshColumns + meshColumn);
                var context = new ClusterContext(clusterIndex, meshRow, meshColumn, request.ClusterSize);
                contexts.Add(context);
                var baseRow = checked(meshRow * blockRows);
                var baseColumn = checked(meshColumn * blockColumns);
                var meshCoordinate = new TopologyPresetCoordinate(meshRow, meshColumn);
                var meshPlacement = new TopologyPresetCoordinate(baseRow, checked(baseColumn + request.ClusterSize));
                graph.Components.Add(CreateComponent(
                    componentRegistry,
                    context.MeshRouterId,
                    $"Cluster {clusterIndex} Mesh Router",
                    ComponentKind.Router,
                    meshPlacement,
                    TopologyPresetComponentRole.MeshRouter,
                    clusterIndex,
                    treeDepth + 1,
                    request));
                manifestComponents.Add(new TopologyManifestComponent(
                    context.MeshRouterId,
                    TopologyPresetComponentRole.MeshRouter,
                    meshPlacement,
                    meshCoordinate,
                    clusterIndex,
                    treeDepth + 1,
                    null,
                    [context.TreeRouterId(0)],
                    context.ReductionId(0)));

                for (var nodeIndex = 0; nodeIndex < internalNodeCount; nodeIndex++)
                {
                    var depth = HeapDepth(nodeIndex);
                    var level = treeDepth - depth;
                    var nodeAtDepth = nodeIndex - ((1 << depth) - 1);
                    var columnOffset = checked(((2 * nodeAtDepth + 1) * request.ClusterSize) / (1 << depth));
                    var routerCoordinate = new TopologyPresetCoordinate(
                        checked(baseRow + 1 + depth * 3),
                        checked(baseColumn + columnOffset));
                    var reductionCoordinate = new TopologyPresetCoordinate(
                        checked(routerCoordinate.Row + 1),
                        routerCoordinate.Column);
                    var parentRouterId = nodeIndex == 0
                        ? context.MeshRouterId
                        : context.TreeRouterId((nodeIndex - 1) / 2);
                    var parentReductionId = nodeIndex == 0
                        ? context.MeshRouterId
                        : context.ReductionId((nodeIndex - 1) / 2);
                    var childRouterIds = ChildComponentIds(context, nodeIndex, internalNodeCount, reduction: false);
                    var childReductionIds = ChildComponentIds(context, nodeIndex, internalNodeCount, reduction: true);
                    graph.Components.Add(CreateComponent(
                    componentRegistry,
                        context.TreeRouterId(nodeIndex),
                        $"Cluster {clusterIndex} Tree Router {nodeIndex}",
                        ComponentKind.Router,
                        routerCoordinate,
                        TopologyPresetComponentRole.TreeRouter,
                        clusterIndex,
                        level,
                        request));
                    graph.Components.Add(CreateComponent(
                    componentRegistry,
                        context.ReductionId(nodeIndex),
                        $"Cluster {clusterIndex} Reduction {nodeIndex}",
                        ComponentKind.ReductionUnit,
                        reductionCoordinate,
                        TopologyPresetComponentRole.TreeReductionUnit,
                        clusterIndex,
                        level,
                        request));
                    manifestComponents.Add(new TopologyManifestComponent(
                        context.TreeRouterId(nodeIndex),
                        TopologyPresetComponentRole.TreeRouter,
                        routerCoordinate,
                        meshCoordinate,
                        clusterIndex,
                        level,
                        parentRouterId,
                        childRouterIds,
                        context.ReductionId(nodeIndex)));
                    manifestComponents.Add(new TopologyManifestComponent(
                        context.ReductionId(nodeIndex),
                        TopologyPresetComponentRole.TreeReductionUnit,
                        reductionCoordinate,
                        meshCoordinate,
                        clusterIndex,
                        level,
                        parentReductionId,
                        childReductionIds,
                        context.TreeRouterId(nodeIndex)));
                }

                for (var peIndex = 0; peIndex < request.ClusterSize; peIndex++)
                {
                    var leafColumnOffset = checked(((2 * peIndex + 1) * request.ClusterSize) / (1 << treeDepth));
                    var peCoordinate = new TopologyPresetCoordinate(
                        checked(baseRow + 1 + treeDepth * 3),
                        checked(baseColumn + leafColumnOffset));
                    var leafHeapIndex = checked(internalNodeCount + peIndex);
                    var parentNodeIndex = (leafHeapIndex - 1) / 2;
                    graph.Components.Add(CreateComponent(
                    componentRegistry,
                        context.ProcessingElementId(peIndex),
                        $"Cluster {clusterIndex} PE {peIndex}",
                        ComponentKind.ProcessingElement,
                        peCoordinate,
                        TopologyPresetComponentRole.ProcessingElement,
                        clusterIndex,
                        0,
                        request,
                        peIndex));
                    manifestComponents.Add(new TopologyManifestComponent(
                        context.ProcessingElementId(peIndex),
                        TopologyPresetComponentRole.ProcessingElement,
                        peCoordinate,
                        meshCoordinate,
                        clusterIndex,
                        0,
                        context.TreeRouterId(parentNodeIndex),
                        [],
                        context.ReductionId(parentNodeIndex)));
                }

                var clusterIds = manifestComponents
                    .Where(item => item.ClusterIndex == clusterIndex)
                    .Select(item => item.ComponentId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                graph.Groups.Add(new VisualGroup
                {
                    Id = context.GroupId,
                    Name = $"MoT Cluster {clusterIndex}",
                    ComponentIds = clusterIds,
                    Collapsed = false,
                    VisualMetadata =
                    {
                        ["role"] = "processing-cluster",
                        ["mesh_row"] = Invariant(meshRow),
                        ["mesh_column"] = Invariant(meshColumn),
                        ["collapse_semantics"] = VisualGroupDefaults.CollapseSemantics
                    }
                });
            }
        }

        return contexts;
    }

    private static void CreateClusterLinks(
        TopologyPresetRequest request,
        HardwareGraph graph,
        IReadOnlyList<ClusterContext> contexts,
        List<TopologyManifestLink> manifestLinks)
    {
        var treeDepth = Log2(request.ClusterSize);
        var internalNodeCount = request.ClusterSize - 1;
        foreach (var context in contexts)
        {
            var attachmentLevel = treeDepth;
            var attachmentLanes = checked(request.LeafLaneCount * request.ClusterSize);
            var attachmentDistance = request.LeafLinkDistance * Math.Pow(request.TreeDistanceScale, attachmentLevel);
            AddLink(
                graph,
                manifestLinks,
                request,
                $"{context.Prefix}.link.attach.activation",
                context.MeshRouterId,
                "activation-cluster",
                context.TreeRouterId(0),
                "activation-parent",
                TopologyPresetLinkRole.ActivationDistribution,
                TopologyPresetLinkScope.Attachment,
                context.ClusterIndex,
                attachmentLevel,
                attachmentLanes,
                attachmentDistance);
            AddLink(
                graph,
                manifestLinks,
                request,
                $"{context.Prefix}.link.attach.partial-sum",
                context.ReductionId(0),
                "partial-sum-parent",
                context.MeshRouterId,
                "partial-sum-cluster",
                TopologyPresetLinkRole.PartialSumReturn,
                TopologyPresetLinkScope.Attachment,
                context.ClusterIndex,
                attachmentLevel,
                attachmentLanes,
                attachmentDistance);

            for (var childHeapIndex = 1; childHeapIndex < checked(request.ClusterSize * 2 - 1); childHeapIndex++)
            {
                var parentNodeIndex = (childHeapIndex - 1) / 2;
                var childOrdinal = childHeapIndex - (2 * parentNodeIndex + 1);
                var childIsInternal = childHeapIndex < internalNodeCount;
                var childIndex = childIsInternal ? childHeapIndex : childHeapIndex - internalNodeCount;
                var childActivationId = childIsInternal
                    ? context.TreeRouterId(childIndex)
                    : context.ProcessingElementId(childIndex);
                var childReturnId = childIsInternal
                    ? context.ReductionId(childIndex)
                    : context.ProcessingElementId(childIndex);
                var level = childIsInternal ? treeDepth - HeapDepth(childHeapIndex) : 0;
                var subtreeLeaves = 1 << level;
                var laneCount = checked(request.LeafLaneCount * subtreeLeaves);
                var distance = request.LeafLinkDistance * Math.Pow(request.TreeDistanceScale, level);
                var scope = childIsInternal ? TopologyPresetLinkScope.Tree : TopologyPresetLinkScope.Leaf;
                AddLink(
                    graph,
                    manifestLinks,
                    request,
                    $"{context.Prefix}.link.tree.e{childHeapIndex:D4}.activation",
                    context.TreeRouterId(parentNodeIndex),
                    $"activation-child-{childOrdinal}",
                    childActivationId,
                    "activation-parent",
                    TopologyPresetLinkRole.ActivationDistribution,
                    scope,
                    context.ClusterIndex,
                    level,
                    laneCount,
                    distance);
                AddLink(
                    graph,
                    manifestLinks,
                    request,
                    $"{context.Prefix}.link.tree.e{childHeapIndex:D4}.partial-sum",
                    childReturnId,
                    "partial-sum-parent",
                    context.ReductionId(parentNodeIndex),
                    $"partial-sum-child-{childOrdinal}",
                    TopologyPresetLinkRole.PartialSumReturn,
                    scope,
                    context.ClusterIndex,
                    level,
                    laneCount,
                    distance);
            }
        }
    }

    private static void CreateMeshLinks(
        TopologyPresetRequest request,
        HardwareGraph graph,
        IReadOnlyList<ClusterContext> contexts,
        List<TopologyManifestLink> manifestLinks)
    {
        var byCoordinate = contexts.ToDictionary(item => (item.MeshRow, item.MeshColumn));
        var meshLanes = checked(request.LeafLaneCount * request.ClusterSize);
        var meshLevel = checked(Log2(request.ClusterSize) + 1);
        foreach (var context in contexts.OrderBy(item => item.ClusterIndex))
        {
            if (context.MeshColumn + 1 < request.MeshColumns)
            {
                var east = byCoordinate[(context.MeshRow, context.MeshColumn + 1)];
                AddMeshDirection(request, graph, manifestLinks, context, east, "east", "west", meshLevel, meshLanes);
                AddMeshDirection(request, graph, manifestLinks, east, context, "west", "east", meshLevel, meshLanes);
            }

            if (context.MeshRow + 1 < request.MeshRows)
            {
                var south = byCoordinate[(context.MeshRow + 1, context.MeshColumn)];
                AddMeshDirection(request, graph, manifestLinks, context, south, "south", "north", meshLevel, meshLanes);
                AddMeshDirection(request, graph, manifestLinks, south, context, "north", "south", meshLevel, meshLanes);
            }
        }
    }

    private static void AddMeshDirection(
        TopologyPresetRequest request,
        HardwareGraph graph,
        List<TopologyManifestLink> manifestLinks,
        ClusterContext source,
        ClusterContext destination,
        string sourceDirection,
        string destinationDirection,
        int level,
        int laneCount) =>
        AddLink(
            graph,
            manifestLinks,
            request,
            $"mot.link.mesh.r{source.MeshRow:D4}.c{source.MeshColumn:D4}.{sourceDirection}",
            source.MeshRouterId,
            $"mesh-{sourceDirection}-out",
            destination.MeshRouterId,
            $"mesh-{destinationDirection}-in",
            TopologyPresetLinkRole.MeshTransport,
            TopologyPresetLinkScope.Mesh,
            null,
            level,
            laneCount,
            request.MeshHopDistance);

    private static void AddLink(
        HardwareGraph graph,
        List<TopologyManifestLink> manifestLinks,
        TopologyPresetRequest request,
        string linkId,
        string sourceComponentId,
        string sourcePortName,
        string destinationComponentId,
        string destinationPortName,
        TopologyPresetLinkRole role,
        TopologyPresetLinkScope scope,
        int? clusterIndex,
        int level,
        int laneCount,
        double distance)
    {
        var bandwidth = checked(request.WordBits * laneCount);
        var source = graph.FindComponent(sourceComponentId)
            ?? throw new InvalidOperationException($"Generated source component '{sourceComponentId}' is missing.");
        var destination = graph.FindComponent(destinationComponentId)
            ?? throw new InvalidOperationException($"Generated destination component '{destinationComponentId}' is missing.");
        AddPort(source, sourcePortName, PortDirection.Output, bandwidth);
        AddPort(destination, destinationPortName, PortDirection.Input, bandwidth);
        graph.Links.Add(new HardwareLink
        {
            Id = linkId,
            Source = new PortRef(sourceComponentId, sourcePortName),
            Destination = new PortRef(destinationComponentId, destinationPortName),
            BandwidthBitsPerCycle = bandwidth,
            LatencyCycles = 0,
            EnergyPerBit = ComponentDefaults.LinkEnergyPerBitPJ,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["topology_role"] = role.ToString(),
                ["topology_scope"] = scope.ToString(),
                ["topology_level"] = Invariant(level),
                ["lane_count"] = Invariant(laneCount),
                ["word_bits"] = Invariant(request.WordBits),
                ["typed_distance"] = distance.ToString("R", CultureInfo.InvariantCulture),
                ["typed_distance_unit"] = "topology-relative",
                ["physical_route_id"] = linkId,
                ["provenance"] = MeshOfTreesTopologyPresetBuilder.ProvenanceSource
            }
        });
        manifestLinks.Add(new TopologyManifestLink(
            linkId,
            role,
            scope,
            sourceComponentId,
            destinationComponentId,
            clusterIndex,
            level,
            laneCount,
            bandwidth,
            distance));
    }

    private static void AddPort(HardwareComponent component, string portName, PortDirection direction, int bandwidth)
    {
        if (component.FindPort(portName) is not null)
        {
            throw new InvalidOperationException($"Generated component '{component.Id}' contains duplicate port '{portName}'.");
        }

        component.Ports.Add(new HardwarePort
        {
            Name = portName,
            Direction = direction,
            SignalType = SignalType.Digital,
            DataType = HardwareDataType.Packet,
            Precision = PrecisionKind.Any,
            Protocol = PortProtocol.Packet,
            BandwidthBitsPerCycle = bandwidth,
            LatencyCycles = 0,
            ClockDomain = "default",
            Required = true,
            MultiConnect = false
        });
    }

    private static HardwareComponent CreateComponent(
        ComponentTypeRegistry componentRegistry,
        string id,
        string name,
        ComponentKind kind,
        TopologyPresetCoordinate coordinate,
        TopologyPresetComponentRole role,
        int clusterIndex,
        int level,
        TopologyPresetRequest request,
        int? peIndex = null)
    {
        var component = componentRegistry.CreateBuiltIn(
            kind,
            id,
            new GridPosition(coordinate.Column, coordinate.Row),
            name);
        component.Ports.Clear();
        component.Parameters["topology_role"] = role.ToString();
        component.Parameters["cluster_index"] = Invariant(clusterIndex);
        component.Parameters["topology_level"] = Invariant(level);
        component.Parameters["word_bits"] = Invariant(request.WordBits);
        component.Parameters["provenance"] = MeshOfTreesTopologyPresetBuilder.ProvenanceSource;
        if (kind == ComponentKind.Router)
        {
            component.Parameters["router_latency_cycles"] = Invariant(request.RouterLatencyCycles);
        }
        else if (kind == ComponentKind.ReductionUnit)
        {
            component.Parameters["num_inputs"] = "2";
            component.Parameters["accumulate_latency"] = Invariant(request.AdderLatencyCycles);
            component.Parameters["collective_semantics"] = "explicit-grouped-vector-sum-binding-required";
        }
        else if (peIndex.HasValue)
        {
            component.Parameters["pe_index"] = Invariant(peIndex.Value);
        }

        return component;
    }

    private static void FinalizeComponentPortCounts(HardwareGraph graph)
    {
        foreach (var component in graph.Components.Where(item => item.Type == ComponentKind.Router))
        {
            component.Parameters["num_ports"] = Invariant(component.Ports.Count);
        }
    }

    private static void PlaceComponents(HardwareGraph graph, IEnumerable<TopologyManifestComponent> components)
    {
        var placement = graph.Placement ?? throw new InvalidOperationException("Generated topology requires explicit placement.");
        foreach (var component in components.OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            var result = placement.PlaceComponent(
                component.ComponentId,
                component.Coordinate.Row,
                component.Coordinate.Column,
                layer: "M0");
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(string.Join("; ", result.Issues.Select(issue => issue.Message)));
            }
        }
    }

    private static void CreatePhysicalRoutes(TopologyPresetRequest request, HardwareGraph graph)
    {
        var placement = graph.Placement ?? throw new InvalidOperationException("Generated topology requires explicit placement.");
        var routing = graph.Routing ?? throw new InvalidOperationException("Generated topology requires explicit routing.");
        foreach (var link in graph.Links.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!placement.TryGetPhysicalPosition(link.Source.ComponentId, out var source) ||
                !placement.TryGetPhysicalPosition(link.Destination.ComponentId, out var destination))
            {
                throw new InvalidOperationException($"Generated link '{link.Id}' has an unplaced endpoint.");
            }

            var route = ManhattanRoutePlanner.CreateRoute(
                link.Id,
                source,
                destination,
                RoutingLayerId.Metal(3, "signal"),
                RoutingMedium.ElectricalMetal,
                PhysicalRouteTargetKind.LogicalLink,
                request.PlacementCellSizeMicrometers,
                ManhattanRouteAxisOrder.XThenY);
            routing.Routes.Add(route);
            link.PhysicalLength = PhysicalRouteMetrics.Analyze(route.Path).LengthMicrometers;
            link.RouteType = PhysicalRoute.MediumToLegacyRouteType(route.Medium);
        }
    }

    private static void ValidatePhysicalRoutes(HardwareGraph graph, List<TopologyBuildIssue> issues)
    {
        var duplicateRoutes = graph.Routing!.Routes
            .GroupBy(route => route.LinkId, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (graph.Routing.Routes.Count != graph.Links.Count || duplicateRoutes.Length > 0)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidPhysicalRoute,
                ValidationSeverity.Error,
                "$.physicalRouting.routes",
                "Every generated logical link must have exactly one explicit physical RoutePath."));
        }

        var report = PhysicalRouteValidator.Validate(
            graph,
            graph.Placement,
            graph.Routing,
            new RouteValidationOptions { RequireRoutesForAllLogicalLinks = true });
        issues.AddRange(report.Issues.Select(issue => new TopologyBuildIssue(
            TopologyBuildIssueCodes.InvalidPhysicalRoute,
            ValidationSeverity.Error,
            "$.physicalRouting.routes[" + issue.RouteId + "]",
            $"{issue.Code}: {issue.Message}")));
    }

    private static IReadOnlyList<string> ChildComponentIds(
        ClusterContext context,
        int nodeIndex,
        int internalNodeCount,
        bool reduction)
    {
        var result = new List<string>();
        for (var childOrdinal = 0; childOrdinal < 2; childOrdinal++)
        {
            var childHeapIndex = checked(nodeIndex * 2 + 1 + childOrdinal);
            result.Add(childHeapIndex < internalNodeCount
                ? reduction ? context.ReductionId(childHeapIndex) : context.TreeRouterId(childHeapIndex)
                : context.ProcessingElementId(childHeapIndex - internalNodeCount));
        }

        return result;
    }

    private static int HeapDepth(int heapIndex)
    {
        var depth = 0;
        for (var value = heapIndex + 1; value > 1; value >>= 1)
        {
            depth++;
        }

        return depth;
    }

    private static int Log2(int powerOfTwo)
    {
        var result = 0;
        for (var value = powerOfTwo; value > 1; value >>= 1)
        {
            result++;
        }

        return result;
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed class ClusterContext
    {
        internal ClusterContext(int clusterIndex, int meshRow, int meshColumn, int clusterSize)
        {
            ClusterIndex = clusterIndex;
            MeshRow = meshRow;
            MeshColumn = meshColumn;
            ClusterSize = clusterSize;
            Prefix = $"mot.c{clusterIndex:D4}";
        }

        internal int ClusterIndex { get; }
        internal int MeshRow { get; }
        internal int MeshColumn { get; }
        internal int ClusterSize { get; }
        internal string Prefix { get; }
        internal string MeshRouterId => $"{Prefix}.mesh.router";
        internal string GroupId => $"{Prefix}.group";
        internal string TreeRouterId(int nodeIndex) => $"{Prefix}.tree.n{nodeIndex:D4}.router";
        internal string ReductionId(int nodeIndex) => $"{Prefix}.tree.n{nodeIndex:D4}.reduction";
        internal string ProcessingElementId(int peIndex) => $"{Prefix}.pe{peIndex:D4}";
    }
}
