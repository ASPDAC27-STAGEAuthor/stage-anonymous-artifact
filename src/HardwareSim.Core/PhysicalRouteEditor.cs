namespace HardwareSim.Core;

/// <summary>Provides the physical route editor service for hardware design and simulation workflows.</summary>
public sealed class PhysicalRouteEditor
{
    /// <summary>Initializes a new physical route editor instance from the supplied state.</summary>
    public PhysicalRouteEditor(PhysicalRouting? routing = null)
    {
        Routing = routing ?? new PhysicalRouting();
    }

    /// <summary>Gets the routing value carried by the enclosing physical route editor contract.</summary>
    public PhysicalRouting Routing { get; }

    /// <summary>Creates route from legacy layer and route type inputs.</summary>
    public PhysicalRoute CreateRoute(
        string linkId,
        IEnumerable<PhysicalPoint> path,
        string layer = "M3",
        string routeType = "electrical") =>
        CreateStructuredRoute(linkId, path, RoutingLayerId.Parse(layer), PhysicalRoute.ParseMedium(routeType));

    /// <summary>Creates route from structured medium and layer inputs.</summary>
    public PhysicalRoute CreateStructuredRoute(
        string linkId,
        IEnumerable<PhysicalPoint> path,
        RoutingLayerId? layerId = null,
        RoutingMedium medium = RoutingMedium.ElectricalMetal,
        PhysicalRouteTargetKind targetKind = PhysicalRouteTargetKind.LogicalLink)
    {
        if (Routing.FindRoute(linkId) is not null)
        {
            throw new InvalidOperationException($"Route for link '{linkId}' already exists.");
        }

        var route = new PhysicalRoute
        {
            LinkId = linkId,
            TargetKind = targetKind,
            LayerId = layerId ?? RoutingLayerId.Metal(3),
            Medium = medium,
            PathUnit = PhysicalRoutePointUnit.Micrometers,
            Path = path.ToList()
        };
        Routing.Routes.Add(route);
        return route;
    }

    /// <summary>Removes route from the current model.</summary>
    public void DeleteRoute(string linkId)
    {
        var route = RequiredRoute(linkId);
        Routing.Routes.Remove(route);
    }

    /// <summary>Appends a waypoint to the named link route.</summary>
    public void AppendPoint(string linkId, PhysicalPoint point)
    {
        RequiredRoute(linkId).Path.Add(point);
    }

    /// <summary>Inserts a waypoint at the requested route index.</summary>
    public void InsertPoint(string linkId, int index, PhysicalPoint point)
    {
        var route = RequiredRoute(linkId);
        if (index < 0 || index > route.Path.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        route.Path.Insert(index, point);
    }

    /// <summary>Replaces the waypoint at the requested route index.</summary>
    public void MovePoint(string linkId, int index, PhysicalPoint point)
    {
        var route = RequiredRoute(linkId);
        if (index < 0 || index >= route.Path.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        route.Path[index] = point;
    }

    /// <summary>Removes point from the current model.</summary>
    public void DeletePoint(string linkId, int index)
    {
        var route = RequiredRoute(linkId);
        if (index < 0 || index >= route.Path.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        route.Path.RemoveAt(index);
    }

    /// <summary>Analyzes congestion and returns structured findings.</summary>
    public RouteGridCongestionReport AnalyzeCongestion(double cellSizeMicrometers = 100, int defaultCellCapacity = 1) =>
        RouteGridAnalyzer.Analyze(Routing, cellSizeMicrometers, defaultCellCapacity);

    private PhysicalRoute RequiredRoute(string linkId) =>
        Routing.FindRoute(linkId) ?? throw new InvalidOperationException($"Route for link '{linkId}' does not exist.");
}
