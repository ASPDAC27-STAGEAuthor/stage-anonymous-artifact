namespace HardwareSim.Core;

/// <summary>Represents one structured physical route validation issue.</summary>
public sealed record RouteValidationIssue(string Code, string Severity, string RouteId, string Message);

/// <summary>Collects structured physical route validation issues.</summary>
public sealed class RouteValidationReport
{
    /// <summary>Gets the validation issues found for explicit physical routes.</summary>
    public List<RouteValidationIssue> Issues { get; } = [];
    /// <summary>Gets whether any route validation issue is an error.</summary>
    public bool HasErrors => Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Configures explicit physical route validation without inferring missing collection targets.</summary>
public sealed class RouteValidationOptions
{
    /// <summary>Gets or sets whether every logical hardware link must have an explicit route.</summary>
    public bool RequireRoutesForAllLogicalLinks { get; set; }
    /// <summary>Gets required logical collection target ids that must have explicit collection-target routes.</summary>
    public List<string> RequiredCollectionTargetIds { get; set; } = [];
}

/// <summary>Validates explicit physical routes against resolved logical links, placement endpoints, layers, and media.</summary>
public static class PhysicalRouteValidator
{
    /// <summary>Identifies diagonal route segments that are not Manhattan routed.</summary>
    public const string DiagonalSegmentIssueCode = "route_diagonal_segment";
    /// <summary>Identifies route endpoints that do not match resolved placed link endpoints.</summary>
    public const string EndpointMismatchIssueCode = "route_endpoint_mismatch";
    /// <summary>Identifies route points outside the placement bounds.</summary>
    public const string PointOutOfBoundsIssueCode = "route_point_out_of_bounds";
    /// <summary>Identifies consecutive route points that create a zero-length segment.</summary>
    public const string ZeroLengthSegmentIssueCode = "route_zero_length_segment";
    /// <summary>Identifies an invalid structured layer id.</summary>
    public const string InvalidLayerIssueCode = "route_invalid_layer";
    /// <summary>Identifies an invalid routing medium enum value.</summary>
    public const string InvalidMediumIssueCode = "route_invalid_medium";
    /// <summary>Identifies a route whose logical link id cannot be resolved.</summary>
    public const string MissingLogicalLinkIssueCode = "route_missing_logical_link";
    /// <summary>Identifies a required logical collection target with no explicit route.</summary>
    public const string MissingCollectionTargetIssueCode = "route_missing_collection_target";
    /// <summary>Identifies an invalid route target kind enum value.</summary>
    public const string InvalidTargetKindIssueCode = "route_invalid_target_kind";
    /// <summary>Identifies a route with too few explicit path points.</summary>
    public const string PathTooShortIssueCode = "route_path_too_short";

    /// <summary>Validates explicit routes and returns structured issues without mutating the graph.</summary>
    public static RouteValidationReport Validate(
        HardwareGraph graph,
        PhysicalPlacement? placement = null,
        PhysicalRouting? routing = null,
        RouteValidationOptions? options = null)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }
        placement ??= graph.Placement;
        routing ??= graph.Routing ?? new PhysicalRouting();
        options ??= new RouteValidationOptions();
        var report = new RouteValidationReport();
        var routesById = routing.Routes
            .Where(route => !string.IsNullOrWhiteSpace(route.LinkId))
            .GroupBy(route => route.LinkId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        if (options.RequireRoutesForAllLogicalLinks)
        {
            foreach (var link in graph.Links.OrderBy(link => link.Id, StringComparer.Ordinal))
            {
                if (!routesById.TryGetValue(link.Id, out var routes) || routes.All(route => route.TargetKind != PhysicalRouteTargetKind.LogicalLink))
                {
                    Add(report, MissingLogicalLinkIssueCode, link.Id, $"Logical link '{link.Id}' has no explicit physical route.");
                }
            }
        }

        foreach (var targetId in options.RequiredCollectionTargetIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!routesById.TryGetValue(targetId, out var routes) || routes.All(route => route.TargetKind != PhysicalRouteTargetKind.CollectionTarget))
            {
                Add(report, MissingCollectionTargetIssueCode, targetId, $"Logical collection target '{targetId}' has no explicit physical route.");
            }
        }

        foreach (var route in routing.Routes.OrderBy(route => route.LinkId, StringComparer.Ordinal))
        {
            ValidateRouteBasics(route, placement, report);
            if (!Enum.IsDefined(typeof(PhysicalRouteTargetKind), route.TargetKind))
            {
                Add(report, InvalidTargetKindIssueCode, route.LinkId, $"Route '{route.LinkId}' has invalid target kind '{route.TargetKind}'.");
                continue;
            }

            if (route.TargetKind == PhysicalRouteTargetKind.LogicalLink)
            {
                var link = graph.Links.SingleOrDefault(link => string.Equals(link.Id, route.LinkId, StringComparison.OrdinalIgnoreCase));
                if (link is null)
                {
                    Add(report, MissingLogicalLinkIssueCode, route.LinkId, $"Route '{route.LinkId}' does not resolve to a logical hardware link.");
                    continue;
                }

                ValidateResolvedLinkEndpoints(graph, placement, route, link, report);
            }
        }

        return report;
    }

    private static void ValidateRouteBasics(PhysicalRoute route, PhysicalPlacement? placement, RouteValidationReport report)
    {
        if (!Enum.IsDefined(typeof(RoutingMedium), route.Medium))
        {
            Add(report, InvalidMediumIssueCode, route.LinkId, $"Route '{route.LinkId}' has invalid routing medium '{route.Medium}'.");
        }

        if (route.LayerId is null || string.IsNullOrWhiteSpace(route.LayerId.Stack) || route.LayerId.Index < 0)
        {
            Add(report, InvalidLayerIssueCode, route.LinkId, $"Route '{route.LinkId}' has invalid routing layer id.");
        }

        if (route.Path.Count < 2)
        {
            Add(report, PathTooShortIssueCode, route.LinkId, $"Route '{route.LinkId}' must contain at least two explicit path points.");
        }

        for (var index = 0; index < route.Path.Count; index++)
        {
            ValidateBounds(route, index, placement, report);
            if (index == 0)
            {
                continue;
            }

            var previous = route.Path[index - 1];
            var current = route.Path[index];
            var sameX = Same(previous.X, current.X);
            var sameY = Same(previous.Y, current.Y);
            if (sameX && sameY)
            {
                Add(report, ZeroLengthSegmentIssueCode, route.LinkId, $"Route '{route.LinkId}' segment {index - 1}->{index} has zero length.");
            }
            else if (!sameX && !sameY)
            {
                Add(report, DiagonalSegmentIssueCode, route.LinkId, $"Route '{route.LinkId}' segment {index - 1}->{index} is diagonal; explicit routes must be Manhattan segments.");
            }
        }
    }

    private static void ValidateResolvedLinkEndpoints(
        HardwareGraph graph,
        PhysicalPlacement? placement,
        PhysicalRoute route,
        HardwareLink link,
        RouteValidationReport report)
    {
        if (placement is null || route.Path.Count == 0)
        {
            return;
        }

        var source = graph.FindComponent(link.Source.ComponentId);
        var destination = graph.FindComponent(link.Destination.ComponentId);
        if (source is null || destination is null)
        {
            return;
        }

        if (!placement.TryGetPhysicalPosition(source.Id, out var sourcePoint) ||
            !placement.TryGetPhysicalPosition(destination.Id, out var destinationPoint))
        {
            Add(report, EndpointMismatchIssueCode, route.LinkId, $"Route '{route.LinkId}' cannot verify endpoints because one or both logical link endpoints are not placed.");
            return;
        }

        if (!SamePoint(route.Path[0], sourcePoint) || !SamePoint(route.Path[^1], destinationPoint))
        {
            Add(report, EndpointMismatchIssueCode, route.LinkId, $"Route '{route.LinkId}' path endpoints do not match the resolved logical link endpoint placement.");
        }
    }

    private static void ValidateBounds(PhysicalRoute route, int pointIndex, PhysicalPlacement? placement, RouteValidationReport report)
    {
        if (placement is null || placement.Rows <= 0 || placement.Cols <= 0)
        {
            return;
        }

        var point = route.Path[pointIndex];
        var minX = placement.Origin.X;
        var minY = placement.Origin.Y;
        var maxX = placement.Origin.X + placement.Cols * placement.CellWidthMicrometers;
        var maxY = placement.Origin.Y + placement.Rows * placement.CellHeightMicrometers;
        if (point.X < minX - 0.000000001 || point.X > maxX + 0.000000001 ||
            point.Y < minY - 0.000000001 || point.Y > maxY + 0.000000001)
        {
            Add(report, PointOutOfBoundsIssueCode, route.LinkId, $"Route '{route.LinkId}' point {pointIndex} is outside placement bounds.");
        }
    }

    private static void Add(RouteValidationReport report, string code, string routeId, string message) =>
        report.Issues.Add(new RouteValidationIssue(code, "error", routeId, message));

    private static bool SamePoint(PhysicalPoint left, PhysicalPoint right) => Same(left.X, right.X) && Same(left.Y, right.Y);

    private static bool Same(double left, double right) => Math.Abs(left - right) < 0.000000001;
}

