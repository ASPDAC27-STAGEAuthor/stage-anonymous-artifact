using System.Globalization;

namespace HardwareSim.Core;

/// <summary>Builds an ordinary PE-attached flat two-dimensional mesh from registered Router and PE components.</summary>
public sealed class Flat2DMeshTopologyPresetBuilder : ITopologyPresetBuilder
{
    /// <summary>Defines the stable ordinary flat two-dimensional mesh preset identity.</summary>
    public const string TopologyPresetId = "com.hardware-sim.topology.flat-2d-mesh.v1";

    /// <summary>Defines the stable builder implementation identity.</summary>
    public const string BuilderId = "com.hardware-sim.builder.flat-2d-mesh";

    /// <summary>Defines the builder contract version recorded in topology provenance.</summary>
    public const string BuilderVersion = "1.0.0";

    private const string ProvenanceSource = "phase8a-core-flat-2d-mesh";
    private readonly ComponentTypeRegistry _componentRegistry;

    /// <summary>Initializes the builder with the default registered component types.</summary>
    public Flat2DMeshTopologyPresetBuilder()
        : this(ComponentTypeRegistry.CreateDefault())
    {
    }

    /// <summary>Initializes the builder with an explicit registered component-type source.</summary>
    /// <param name="componentRegistry">Registry used to create ordinary Router and ProcessingElement components.</param>
    public Flat2DMeshTopologyPresetBuilder(ComponentTypeRegistry componentRegistry)
    {
        _componentRegistry = componentRegistry ?? throw new ArgumentNullException(nameof(componentRegistry));
    }

    /// <inheritdoc />
    public string TopologyId => TopologyPresetId;

    /// <inheritdoc />
    public TopologyBuildResult Build(TopologyPresetRequest request)
    {
        var issues = ValidateRequest(request);
        if (issues.Count > 0)
        {
            return Failure(issues);
        }

        var graph = new HardwareGraph
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["topology_preset_id"] = TopologyPresetId,
                ["topology_builder_id"] = BuilderId,
                ["topology_builder_version"] = BuilderVersion,
                ["topology_profile"] = "ordinary-flat-2d-mesh",
                ["cluster_size"] = "1"
            },
            Placement = new PhysicalPlacement
            {
                Rows = checked(request.MeshRows * 2),
                Cols = checked(request.MeshColumns * 2),
                CellWidthMicrometers = request.PlacementCellSizeMicrometers,
                CellHeightMicrometers = request.PlacementCellSizeMicrometers,
                Origin = new PhysicalPoint(0, 0),
                Layer = "M0",
                FloorplanMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["topology_preset_id"] = TopologyPresetId,
                    ["layout_policy"] = "router-even-cell-pe-diagonal-cell-v1"
                }
            },
            Routing = new PhysicalRouting()
        };
        var manifestComponents = new List<TopologyManifestComponent>();
        var manifestLinks = new List<TopologyManifestLink>();

        try
        {
            CreateComponentsAndGroups(request, graph, manifestComponents);
            CreateAttachmentLinks(request, graph, manifestLinks);
            CreateMeshLinks(request, graph, manifestLinks);
            CreatePhysicalRoutes(graph);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return Failure(
            [
                new TopologyBuildIssue(
                    exception is OverflowException ? TopologyBuildIssueCodes.ArithmeticOverflow : TopologyBuildIssueCodes.InvalidManifest,
                    ValidationSeverity.Error,
                    "$",
                    exception.Message)
            ]);
        }

        var placementReport = graph.Placement!.Validate(graph.Components);
        if (placementReport.HasErrors || placementReport.UnplacedComponentIds.Count > 0)
        {
            return Failure(
            [
                new TopologyBuildIssue(
                    TopologyBuildIssueCodes.InvalidPhysicalRoute,
                    ValidationSeverity.Error,
                    "$.placement",
                    "Flat mesh generation did not produce one valid explicit placement for every component.")
            ]);
        }

        var routeValidation = PhysicalRouteValidator.Validate(
            graph,
            graph.Placement,
            graph.Routing,
            new RouteValidationOptions { RequireRoutesForAllLogicalLinks = true });
        if (graph.Routing!.Routes.Count != graph.Links.Count ||
            graph.Routing.Routes.Select(route => route.LinkId).Distinct(StringComparer.Ordinal).Count() != graph.Links.Count ||
            routeValidation.HasErrors)
        {
            return Failure(
            [
                new TopologyBuildIssue(
                    TopologyBuildIssueCodes.InvalidPhysicalRoute,
                    ValidationSeverity.Error,
                    "$.routing.routes",
                    "Flat mesh generation requires exactly one validator-clean explicit physical route per logical link: " +
                    string.Join("; ", routeValidation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")))
            ]);
        }

        var manifest = TopologyPresetCanonicalizer.CreateManifest(
            request,
            graph,
            manifestComponents,
            manifestLinks,
            BuilderId,
            BuilderVersion,
            ProvenanceSource);
        var persistedGraph = TopologyManifestJson.AttachToGraph(graph, manifest);
        return new TopologyBuildResult(persistedGraph, manifest, []);
    }

    private static List<TopologyBuildIssue> ValidateRequest(TopologyPresetRequest? request)
    {
        var issues = new List<TopologyBuildIssue>();
        if (request is null)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.MissingRequest,
                ValidationSeverity.Error,
                "$",
                "A topology preset request is required."));
            return issues;
        }

        if (!string.Equals(request.TopologyId, TopologyPresetId, StringComparison.Ordinal))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.UnsupportedTopology,
                ValidationSeverity.Error,
                "$.topologyId",
                $"Flat2DMeshTopologyPresetBuilder accepts only topology id '{TopologyPresetId}'."));
        }

        if (request.MeshRows <= 0 || request.MeshColumns <= 0)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidMeshSize,
                ValidationSeverity.Error,
                "$.meshRows",
                "Flat mesh rows and columns must both be positive."));
        }

        if (request.ClusterSize != 1)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidClusterSize,
                ValidationSeverity.Error,
                "$.clusterSize",
                "The ordinary flat mesh canonical profile requires exactly one PE per router and cluster_size=1."));
        }

        if (request.WordBits <= 0 || request.LeafLaneCount <= 0)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidBandwidthConfiguration,
                ValidationSeverity.Error,
                "$.wordBits",
                "Flat mesh word width and lane count must both be positive."));
        }

        if (!IsPositiveFinite(request.LeafLinkDistance) ||
            !IsPositiveFinite(request.TreeDistanceScale) ||
            !IsPositiveFinite(request.MeshHopDistance) ||
            !IsPositiveFinite(request.PlacementCellSizeMicrometers))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidDistanceConfiguration,
                ValidationSeverity.Error,
                "$.leafLinkDistance",
                "Flat mesh logical distances, shared scale, and placement cell size must be positive finite values."));
        }

        if (request.RouterLatencyCycles < 0 || request.AdderLatencyCycles < 0)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidLatencyConfiguration,
                ValidationSeverity.Error,
                "$.routerLatencyCycles",
                "Shared topology latency values must be non-negative."));
        }

        try
        {
            var clusterCount = checked((long)request.MeshRows * request.MeshColumns);
            var adjacency = checked((long)request.MeshRows * Math.Max(0, request.MeshColumns - 1) +
                                    (long)request.MeshColumns * Math.Max(0, request.MeshRows - 1));
            var componentCount = checked(clusterCount * 2);
            var linkCount = checked((clusterCount + adjacency) * 2);
            _ = checked(request.WordBits * request.LeafLaneCount);
            _ = checked(request.MeshRows * 2);
            _ = checked(request.MeshColumns * 2);
            if (componentCount > int.MaxValue || linkCount > int.MaxValue)
            {
                throw new OverflowException("Generated flat mesh inventory exceeds supported collection bounds.");
            }
        }
        catch (OverflowException exception)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.ArithmeticOverflow,
                ValidationSeverity.Error,
                "$",
                exception.Message));
        }

        return issues;
    }

    private void CreateComponentsAndGroups(
        TopologyPresetRequest request,
        HardwareGraph graph,
        ICollection<TopologyManifestComponent> manifestComponents)
    {
        for (var row = 0; row < request.MeshRows; row++)
        {
            for (var column = 0; column < request.MeshColumns; column++)
            {
                var clusterIndex = checked(row * request.MeshColumns + column);
                var routerId = RouterId(row, column);
                var peId = ProcessingElementId(row, column);
                var routerGridPosition = new GridPosition(checked(column * 2), checked(row * 2));
                var peGridPosition = new GridPosition(checked(column * 2 + 1), checked(row * 2 + 1));
                var router = _componentRegistry.CreateBuiltIn(ComponentKind.Router, routerId, routerGridPosition, $"Flat Mesh Router ({row},{column})");
                var pe = _componentRegistry.CreateBuiltIn(ComponentKind.ProcessingElement, peId, peGridPosition, $"Flat Mesh PE ({row},{column})");
                ConfigureComponent(router, "mesh_router", row, column, clusterIndex);
                ConfigureComponent(pe, "processing_element", row, column, clusterIndex);
                router.Parameters["router_latency_cycles"] = request.RouterLatencyCycles.ToString(CultureInfo.InvariantCulture);
                graph.Components.Add(router);
                graph.Components.Add(pe);

                PlaceOrThrow(graph.Placement!, routerId, checked(row * 2), checked(column * 2));
                PlaceOrThrow(graph.Placement!, peId, checked(row * 2 + 1), checked(column * 2 + 1));
                graph.Groups.Add(new VisualGroup
                {
                    Id = GroupId(row, column),
                    Name = $"Flat Mesh Tile ({row},{column})",
                    ComponentIds = [routerId, peId],
                    Collapsed = false,
                    VisualMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["topology_role"] = "flat_mesh_tile",
                        ["mesh_row"] = row.ToString(CultureInfo.InvariantCulture),
                        ["mesh_column"] = column.ToString(CultureInfo.InvariantCulture)
                    }
                });

                var meshCoordinate = new TopologyPresetCoordinate(row, column);
                manifestComponents.Add(new TopologyManifestComponent(
                    routerId,
                    TopologyPresetComponentRole.MeshRouter,
                    new TopologyPresetCoordinate(checked(row * 2), checked(column * 2)),
                    meshCoordinate,
                    clusterIndex,
                    1,
                    null,
                    [peId],
                    peId));
                manifestComponents.Add(new TopologyManifestComponent(
                    peId,
                    TopologyPresetComponentRole.ProcessingElement,
                    new TopologyPresetCoordinate(checked(row * 2 + 1), checked(column * 2 + 1)),
                    meshCoordinate,
                    clusterIndex,
                    0,
                    routerId,
                    [],
                    routerId));
            }
        }
    }

    private static void CreateAttachmentLinks(
        TopologyPresetRequest request,
        HardwareGraph graph,
        ICollection<TopologyManifestLink> manifestLinks)
    {
        var bandwidth = checked(request.WordBits * request.LeafLaneCount);
        for (var row = 0; row < request.MeshRows; row++)
        {
            for (var column = 0; column < request.MeshColumns; column++)
            {
                var clusterIndex = checked(row * request.MeshColumns + column);
                var routerId = RouterId(row, column);
                var peId = ProcessingElementId(row, column);
                AddDirectedLink(
                    graph,
                    manifestLinks,
                    AttachmentLinkId(row, column, peToRouter: true),
                    peId,
                    "out",
                    routerId,
                    "in",
                    TopologyPresetLinkRole.PartialSumReturn,
                    TopologyPresetLinkScope.Attachment,
                    clusterIndex,
                    0,
                    request.LeafLaneCount,
                    bandwidth,
                    request.LeafLinkDistance);
                AddDirectedLink(
                    graph,
                    manifestLinks,
                    AttachmentLinkId(row, column, peToRouter: false),
                    routerId,
                    "out",
                    peId,
                    "in",
                    TopologyPresetLinkRole.ActivationDistribution,
                    TopologyPresetLinkScope.Attachment,
                    clusterIndex,
                    0,
                    request.LeafLaneCount,
                    bandwidth,
                    request.LeafLinkDistance);
            }
        }
    }

    private static void CreateMeshLinks(
        TopologyPresetRequest request,
        HardwareGraph graph,
        ICollection<TopologyManifestLink> manifestLinks)
    {
        var bandwidth = checked(request.WordBits * request.LeafLaneCount);
        for (var row = 0; row < request.MeshRows; row++)
        {
            for (var column = 0; column < request.MeshColumns; column++)
            {
                if (column + 1 < request.MeshColumns)
                {
                    AddMeshPair(request, graph, manifestLinks, row, column, row, column + 1, bandwidth);
                }

                if (row + 1 < request.MeshRows)
                {
                    AddMeshPair(request, graph, manifestLinks, row, column, row + 1, column, bandwidth);
                }
            }
        }
    }

    private static void AddMeshPair(
        TopologyPresetRequest request,
        HardwareGraph graph,
        ICollection<TopologyManifestLink> manifestLinks,
        int firstRow,
        int firstColumn,
        int secondRow,
        int secondColumn,
        int bandwidth)
    {
        AddDirectedLink(
            graph,
            manifestLinks,
            MeshLinkId(firstRow, firstColumn, secondRow, secondColumn),
            RouterId(firstRow, firstColumn),
            "out",
            RouterId(secondRow, secondColumn),
            "in",
            TopologyPresetLinkRole.MeshTransport,
            TopologyPresetLinkScope.Mesh,
            null,
            1,
            request.LeafLaneCount,
            bandwidth,
            request.MeshHopDistance);
        AddDirectedLink(
            graph,
            manifestLinks,
            MeshLinkId(secondRow, secondColumn, firstRow, firstColumn),
            RouterId(secondRow, secondColumn),
            "out",
            RouterId(firstRow, firstColumn),
            "in",
            TopologyPresetLinkRole.MeshTransport,
            TopologyPresetLinkScope.Mesh,
            null,
            1,
            request.LeafLaneCount,
            bandwidth,
            request.MeshHopDistance);
    }

    private static void AddDirectedLink(
        HardwareGraph graph,
        ICollection<TopologyManifestLink> manifestLinks,
        string linkId,
        string sourceComponentId,
        string sourcePort,
        string destinationComponentId,
        string destinationPort,
        TopologyPresetLinkRole role,
        TopologyPresetLinkScope scope,
        int? clusterIndex,
        int level,
        int laneCount,
        int bandwidth,
        double distance)
    {
        graph.Links.Add(new HardwareLink
        {
            Id = linkId,
            Source = new PortRef(sourceComponentId, sourcePort),
            Destination = new PortRef(destinationComponentId, destinationPort),
            BandwidthBitsPerCycle = bandwidth,
            LatencyCycles = ComponentDefaults.LinkBaseLatencyCycles,
            EnergyPerBit = ComponentDefaults.LinkEnergyPerBitPJ,
            PhysicalLength = distance,
            RouteType = "electrical",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["topology_role"] = role.ToString(),
                ["topology_scope"] = scope.ToString(),
                ["topology_level"] = level.ToString(CultureInfo.InvariantCulture),
                ["lane_count"] = laneCount.ToString(CultureInfo.InvariantCulture),
                ["topology_distance"] = distance.ToString("R", CultureInfo.InvariantCulture)
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

    private static void CreatePhysicalRoutes(HardwareGraph graph)
    {
        foreach (var link in graph.Links.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            if (!graph.Placement!.TryGetPhysicalPosition(link.Source.ComponentId, out var source) ||
                !graph.Placement.TryGetPhysicalPosition(link.Destination.ComponentId, out var destination))
            {
                throw new InvalidOperationException($"Logical link '{link.Id}' cannot be routed without explicit endpoint placement.");
            }

            var path = new List<PhysicalPoint> { source };
            var bend = new PhysicalPoint(destination.X, source.Y);
            if (!SamePoint(source, bend) && !SamePoint(bend, destination))
            {
                path.Add(bend);
            }

            if (!SamePoint(path[^1], destination))
            {
                path.Add(destination);
            }

            if (path.Count < 2)
            {
                throw new InvalidOperationException($"Logical link '{link.Id}' generated a zero-length physical route.");
            }

            graph.Routing!.Routes.Add(new PhysicalRoute
            {
                LinkId = link.Id,
                TargetKind = PhysicalRouteTargetKind.LogicalLink,
                Medium = RoutingMedium.ElectricalMetal,
                LayerId = RoutingLayerId.Metal(3, "flat-mesh-signal"),
                PathUnit = PhysicalRoutePointUnit.Micrometers,
                Path = path
            });
        }
    }

    private static void ConfigureComponent(HardwareComponent component, string role, int row, int column, int clusterIndex)
    {
        component.Parameters["topology_preset_id"] = TopologyPresetId;
        component.Parameters["topology_role"] = role;
        component.Parameters["mesh_row"] = row.ToString(CultureInfo.InvariantCulture);
        component.Parameters["mesh_column"] = column.ToString(CultureInfo.InvariantCulture);
        component.Parameters["cluster_index"] = clusterIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static void PlaceOrThrow(PhysicalPlacement placement, string componentId, int row, int column)
    {
        var result = placement.PlaceComponent(componentId, row, column, layer: "M0");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        }
    }

    private static TopologyBuildResult Failure(IEnumerable<TopologyBuildIssue> issues) =>
        new(new HardwareGraph(), null, issues);

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static bool SamePoint(PhysicalPoint left, PhysicalPoint right) => left.X == right.X && left.Y == right.Y;

    private static string RouterId(int row, int column) => $"flat-mesh-router-r{row}-c{column}";

    private static string ProcessingElementId(int row, int column) => $"flat-mesh-pe-r{row}-c{column}";

    private static string GroupId(int row, int column) => $"flat-mesh-group-r{row}-c{column}";

    private static string AttachmentLinkId(int row, int column, bool peToRouter) => peToRouter
        ? $"flat-mesh-link-attachment-pe-to-router-r{row}-c{column}"
        : $"flat-mesh-link-attachment-router-to-pe-r{row}-c{column}";

    private static string MeshLinkId(int sourceRow, int sourceColumn, int destinationRow, int destinationColumn) =>
        $"flat-mesh-link-r{sourceRow}-c{sourceColumn}-to-r{destinationRow}-c{destinationColumn}";
}
