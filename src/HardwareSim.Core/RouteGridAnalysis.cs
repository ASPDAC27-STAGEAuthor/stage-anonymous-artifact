namespace HardwareSim.Core;

/// <summary>Represents route grid cell data exchanged by hardware design and simulation workflows.</summary>
public sealed class RouteGridCell
{
    /// <summary>Gets or sets the x value carried by the enclosing route grid cell contract.</summary>
    public int X { get; set; }
    /// <summary>Gets or sets the y value carried by the enclosing route grid cell contract.</summary>
    public int Y { get; set; }
    /// <summary>Gets or sets the layer value carried by the enclosing route grid cell contract.</summary>
    public string Layer { get; set; } = "";
    /// <summary>Gets or sets the occupancy value carried by the enclosing route grid cell contract.</summary>
    public int Occupancy { get; set; }
    /// <summary>Gets or sets the capacity value carried by the enclosing route grid cell contract.</summary>
    public int Capacity { get; set; }
    /// <summary>Gets the over capacity by value carried by the enclosing route grid cell contract.</summary>
    public int OverCapacityBy => Math.Max(0, Occupancy - Capacity);
    /// <summary>Gets whether route occupancy exceeds the cell capacity.</summary>
    public bool IsCongested => Occupancy > Capacity;
    /// <summary>Gets or sets the link ids collection carried by the enclosing route grid cell contract.</summary>
    public List<string> LinkIds { get; set; } = [];
}

/// <summary>Represents route grid congestion report data exchanged by hardware design and simulation workflows.</summary>
public sealed class RouteGridCongestionReport
{
    /// <summary>Gets or sets the report id value carried by the enclosing route grid congestion report contract.</summary>
    public string ReportId { get; set; } = "route_grid_congestion";
    /// <summary>Gets or sets the cell size micrometers value carried by the enclosing route grid congestion report contract.</summary>
    public double CellSizeMicrometers { get; set; }
    /// <summary>Gets or sets the default cell capacity value carried by the enclosing route grid congestion report contract.</summary>
    public int DefaultCellCapacity { get; set; }
    /// <summary>Gets or sets the route count value carried by the enclosing route grid congestion report contract.</summary>
    public int RouteCount { get; set; }
    /// <summary>Gets or sets the cell count value carried by the enclosing route grid congestion report contract.</summary>
    public int CellCount { get; set; }
    /// <summary>Gets or sets the congested cell count value carried by the enclosing route grid congestion report contract.</summary>
    public int CongestedCellCount { get; set; }
    /// <summary>Gets or sets the cells collection carried by the enclosing route grid congestion report contract.</summary>
    public List<RouteGridCell> Cells { get; set; } = [];
}

/// <summary>Defines physical route traversal direction for an edge-level routing resource.</summary>
public enum RouteResourceDirection
{
    /// <summary>The route traverses the edge toward increasing X.</summary>
    East,
    /// <summary>The route traverses the edge toward decreasing X.</summary>
    West,
    /// <summary>The route traverses the edge toward increasing Y.</summary>
    North,
    /// <summary>The route traverses the edge toward decreasing Y.</summary>
    South
}

/// <summary>Identifies the exact route resource used for congestion accounting.</summary>
public sealed record RouteResourceKey(
    int EdgeStartX,
    int EdgeStartY,
    int EdgeEndX,
    int EdgeEndY,
    RouteResourceDirection Direction,
    string Layer,
    RoutingMedium Medium)
{
    /// <summary>Gets the deterministic undirected edge id used in reports.</summary>
    public string EdgeId => $"{EdgeStartX},{EdgeStartY}->{EdgeEndX},{EdgeEndY}";
}

/// <summary>Defines exact routing resource capacity by medium.</summary>
public sealed class RouteResourceCapacityProfile
{
    /// <summary>Gets or sets default capacity used when no medium-specific capacity exists.</summary>
    public int DefaultCapacity { get; set; } = 1;
    /// <summary>Gets or sets electrical metal resource capacity.</summary>
    public int ElectricalMetalCapacity { get; set; } = 1;
    /// <summary>Gets or sets optical waveguide resource capacity.</summary>
    public int OpticalWaveguideCapacity { get; set; } = 1;
    /// <summary>Gets or sets thermal control resource capacity.</summary>
    public int ThermalControlCapacity { get; set; } = 1;

    /// <summary>Creates a profile with all capacities initialized to the supplied default.</summary>
    public static RouteResourceCapacityProfile Uniform(int defaultCapacity)
    {
        var capacity = Math.Max(1, defaultCapacity);
        return new RouteResourceCapacityProfile
        {
            DefaultCapacity = capacity,
            ElectricalMetalCapacity = capacity,
            OpticalWaveguideCapacity = capacity,
            ThermalControlCapacity = capacity
        };
    }

    /// <summary>Gets the capacity for a medium as a positive integer.</summary>
    public int CapacityFor(RoutingMedium medium) => Math.Max(1, medium switch
    {
        RoutingMedium.OpticalWaveguide => OpticalWaveguideCapacity,
        RoutingMedium.ThermalControl => ThermalControlCapacity,
        RoutingMedium.ElectricalMetal => ElectricalMetalCapacity,
        _ => DefaultCapacity
    });

    /// <summary>Gets the deterministic source label for the selected medium capacity.</summary>
    public string CapacitySourceFor(RoutingMedium medium) => medium switch
    {
        RoutingMedium.OpticalWaveguide => "routing_resource_capacity_optical",
        RoutingMedium.ThermalControl => "routing_resource_capacity_thermal",
        RoutingMedium.ElectricalMetal => "routing_resource_capacity_electrical",
        _ => "routing_resource_capacity"
    };
}

/// <summary>Represents exact edge/direction/layer/medium route resource occupancy.</summary>
public sealed class RouteResourceOccupancy
{
    /// <summary>Gets or sets the exact resource key.</summary>
    public RouteResourceKey Key { get; set; } = new(0, 0, 0, 0, RouteResourceDirection.East, "M3", RoutingMedium.ElectricalMetal);
    /// <summary>Gets the deterministic edge id for this resource.</summary>
    public string EdgeId => Key.EdgeId;
    /// <summary>Gets the edge traversal direction.</summary>
    public RouteResourceDirection Direction => Key.Direction;
    /// <summary>Gets the route layer id.</summary>
    public string Layer => Key.Layer;
    /// <summary>Gets the routing medium.</summary>
    public RoutingMedium Medium => Key.Medium;
    /// <summary>Gets or sets distinct link occupancy for this exact resource.</summary>
    public int Occupancy { get; set; }
    /// <summary>Gets or sets resource capacity for this exact resource.</summary>
    public int Capacity { get; set; }
    /// <summary>Gets or sets the capacity source label for this exact resource.</summary>
    public string CapacitySource { get; set; } = "routing_resource_capacity";
    /// <summary>Gets the amount by which occupancy exceeds capacity.</summary>
    public int OverCapacityBy => Math.Max(0, Occupancy - Capacity);
    /// <summary>Gets whether this exact resource is over capacity.</summary>
    public bool IsCongested => Occupancy > Capacity;
    /// <summary>Gets or sets distinct link ids using this resource.</summary>
    public List<string> LinkIds { get; set; } = [];
    /// <summary>Gets or sets link ids beyond the resource capacity in deterministic order.</summary>
    public List<string> OverCapacityLinkIds { get; set; } = [];
    /// <summary>Gets deterministic evidence for warnings, reports, and UI hotspot details.</summary>
    public string Evidence =>
        $"edge={EdgeId};direction={Direction};layer={Layer};medium={Medium};occupancy={Occupancy};capacity={Capacity};capacity_source={CapacitySource};links={LinkList(LinkIds)};over_capacity_links={LinkList(OverCapacityLinkIds)}";

    private static string LinkList(IReadOnlyList<string> links) => links.Count == 0 ? "-" : string.Join(",", links);
}

/// <summary>Represents exact routing resource congestion data. Cell summaries are only UI overview data.</summary>
public sealed class RouteResourceCongestionReport
{
    /// <summary>Gets or sets the report id.</summary>
    public string ReportId { get; set; } = "route_resource_congestion";
    /// <summary>Gets or sets the route grid cell size used to derive edge coordinates.</summary>
    public double CellSizeMicrometers { get; set; }
    /// <summary>Gets or sets default exact resource capacity.</summary>
    public int DefaultResourceCapacity { get; set; }
    /// <summary>Gets or sets analyzed route count.</summary>
    public int RouteCount { get; set; }
    /// <summary>Gets or sets exact resource count.</summary>
    public int ResourceCount { get; set; }
    /// <summary>Gets or sets over-capacity exact resource count.</summary>
    public int CongestedResourceCount { get; set; }
    /// <summary>Gets or sets exact edge/direction/layer/medium resources.</summary>
    public List<RouteResourceOccupancy> Resources { get; set; } = [];
    /// <summary>Gets resources whose exact occupancy is above capacity.</summary>
    public IReadOnlyList<RouteResourceOccupancy> CongestedResources => Resources.Where(resource => resource.IsCongested).ToList();
}

/// <summary>Represents a structured routing congestion mitigation suggestion.</summary>
public sealed record RoutingCongestionSuggestion(string Kind, string Message, string Evidence);

/// <summary>Represents a structured routing congestion warning backed by exact resource evidence.</summary>
public sealed class RoutingCongestionWarning
{
    /// <summary>Gets or sets the stable warning code.</summary>
    public string Code { get; set; } = "routing_congestion";
    /// <summary>Gets or sets the warning severity.</summary>
    public string Severity { get; set; } = "warning";
    /// <summary>Gets or sets the human readable warning message.</summary>
    public string Message { get; set; } = "Routing resource is over capacity.";
    /// <summary>Gets or sets exact resource evidence.</summary>
    public string Evidence { get; set; } = "";
    /// <summary>Gets or sets links using the congested resource.</summary>
    public List<string> LinkIds { get; set; } = [];
    /// <summary>Gets or sets mitigation suggestions tied to the exact evidence.</summary>
    public List<RoutingCongestionSuggestion> Suggestions { get; set; } = [];
}

/// <summary>Analyzes exact edge/direction/layer/medium route resource occupancy.</summary>
public static class RouteResourceAnalyzer
{
    /// <summary>Analyzes route resource occupancy without using cell overlap as the congestion truth.</summary>
    public static RouteResourceCongestionReport Analyze(
        PhysicalRouting routing,
        double cellSizeMicrometers = 100,
        int defaultResourceCapacity = 1,
        RouteResourceCapacityProfile? capacityProfile = null)
    {
        cellSizeMicrometers = Math.Max(1, cellSizeMicrometers);
        capacityProfile ??= RouteResourceCapacityProfile.Uniform(defaultResourceCapacity);
        capacityProfile.DefaultCapacity = Math.Max(1, capacityProfile.DefaultCapacity);
        var linkIdsByResource = new Dictionary<RouteResourceKey, HashSet<string>>();

        foreach (var route in routing.Routes)
        {
            var emittedForRoute = new HashSet<RouteResourceKey>();
            foreach (var key in ResourcesForRoute(route, cellSizeMicrometers))
            {
                if (!emittedForRoute.Add(key))
                {
                    continue;
                }

                if (!linkIdsByResource.TryGetValue(key, out var linkIds))
                {
                    linkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    linkIdsByResource[key] = linkIds;
                }

                linkIds.Add(route.LinkId);
            }
        }

        var resources = linkIdsByResource
            .Select(pair =>
            {
                var linkIds = pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToList();
                var capacity = capacityProfile.CapacityFor(pair.Key.Medium);
                return new RouteResourceOccupancy
                {
                    Key = pair.Key,
                    Occupancy = linkIds.Count,
                    Capacity = capacity,
                    CapacitySource = capacityProfile.CapacitySourceFor(pair.Key.Medium),
                    LinkIds = linkIds,
                    OverCapacityLinkIds = linkIds.Skip(capacity).ToList()
                };
            })
            .OrderBy(resource => resource.Key.EdgeStartX)
            .ThenBy(resource => resource.Key.EdgeStartY)
            .ThenBy(resource => resource.Key.EdgeEndX)
            .ThenBy(resource => resource.Key.EdgeEndY)
            .ThenBy(resource => resource.Key.Direction)
            .ThenBy(resource => resource.Key.Layer, StringComparer.Ordinal)
            .ThenBy(resource => resource.Key.Medium)
            .ToList();

        return new RouteResourceCongestionReport
        {
            CellSizeMicrometers = cellSizeMicrometers,
            DefaultResourceCapacity = capacityProfile.DefaultCapacity,
            RouteCount = routing.Routes.Count,
            ResourceCount = resources.Count,
            CongestedResourceCount = resources.Count(resource => resource.IsCongested),
            Resources = resources
        };
    }

    private static IEnumerable<RouteResourceKey> ResourcesForRoute(PhysicalRoute route, double cellSizeMicrometers)
    {
        if (route.Path.Count < 2)
        {
            yield break;
        }

        var layer = route.LayerId?.ToString() ?? route.Layer;
        for (var index = 1; index < route.Path.Count; index++)
        {
            foreach (var key in ResourcesForSegment(route.Path[index - 1], route.Path[index], layer, route.Medium, cellSizeMicrometers))
            {
                yield return key;
            }
        }
    }

    private static IEnumerable<RouteResourceKey> ResourcesForSegment(
        PhysicalPoint a,
        PhysicalPoint b,
        string layer,
        RoutingMedium medium,
        double cellSizeMicrometers)
    {
        var ax = CellIndex(a.X, cellSizeMicrometers);
        var ay = CellIndex(a.Y, cellSizeMicrometers);
        var bx = CellIndex(b.X, cellSizeMicrometers);
        var by = CellIndex(b.Y, cellSizeMicrometers);
        var xStep = Math.Sign(bx - ax);
        var yStep = Math.Sign(by - ay);
        var x = ax;
        var y = ay;

        while (x != bx)
        {
            var nextX = x + xStep;
            yield return HorizontalKey(x, y, nextX, layer, medium);
            x = nextX;
        }

        while (y != by)
        {
            var nextY = y + yStep;
            yield return VerticalKey(x, y, nextY, layer, medium);
            y = nextY;
        }
    }

    private static RouteResourceKey HorizontalKey(int x, int y, int nextX, string layer, RoutingMedium medium)
    {
        var startX = Math.Min(x, nextX);
        var endX = Math.Max(x, nextX);
        var direction = nextX > x ? RouteResourceDirection.East : RouteResourceDirection.West;
        return new RouteResourceKey(startX, y, endX, y, direction, layer, medium);
    }

    private static RouteResourceKey VerticalKey(int x, int y, int nextY, string layer, RoutingMedium medium)
    {
        var startY = Math.Min(y, nextY);
        var endY = Math.Max(y, nextY);
        var direction = nextY > y ? RouteResourceDirection.North : RouteResourceDirection.South;
        return new RouteResourceKey(x, startY, x, endY, direction, layer, medium);
    }

    private static int CellIndex(double coordinate, double cellSizeMicrometers) =>
        (int)Math.Floor(coordinate / cellSizeMicrometers);
}

/// <summary>Provides route grid analyzer operations for hardware design and simulation workflows.</summary>
public static class RouteGridAnalyzer
{
    /// <summary>Analyzes the supplied metrics and returns structured findings.</summary>
    public static RouteGridCongestionReport Analyze(
        PhysicalRouting routing,
        double cellSizeMicrometers = 100,
        int defaultCellCapacity = 1)
    {
        cellSizeMicrometers = Math.Max(1, cellSizeMicrometers);
        defaultCellCapacity = Math.Max(1, defaultCellCapacity);
        var cellsByKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var metadataByKey = new Dictionary<string, (int X, int Y, string Layer)>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in routing.Routes)
        {
            foreach (var cell in CellsForRoute(route, cellSizeMicrometers))
            {
                var key = $"{cell.Layer}:{cell.X}:{cell.Y}";
                metadataByKey[key] = cell;
                if (!cellsByKey.TryGetValue(key, out var linkIds))
                {
                    linkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    cellsByKey[key] = linkIds;
                }

                linkIds.Add(route.LinkId);
            }
        }

        var cells = cellsByKey
            .Select(kvp =>
            {
                var metadata = metadataByKey[kvp.Key];
                return new RouteGridCell
                {
                    X = metadata.X,
                    Y = metadata.Y,
                    Layer = metadata.Layer,
                    Occupancy = kvp.Value.Count,
                    Capacity = defaultCellCapacity,
                    LinkIds = kvp.Value.OrderBy(id => id, StringComparer.Ordinal).ToList()
                };
            })
            .OrderBy(c => c.Layer, StringComparer.Ordinal)
            .ThenBy(c => c.X)
            .ThenBy(c => c.Y)
            .ToList();

        return new RouteGridCongestionReport
        {
            CellSizeMicrometers = cellSizeMicrometers,
            DefaultCellCapacity = defaultCellCapacity,
            RouteCount = routing.Routes.Count,
            CellCount = cells.Count,
            CongestedCellCount = cells.Count(c => c.IsCongested),
            Cells = cells
        };
    }

    private static IEnumerable<(int X, int Y, string Layer)> CellsForRoute(PhysicalRoute route, double cellSizeMicrometers)
    {
        if (route.Path.Count == 0)
        {
            yield break;
        }

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < route.Path.Count; i++)
        {
            foreach (var cell in CellsForSegment(route.Path[i - 1], route.Path[i], route.Layer, cellSizeMicrometers))
            {
                if (emitted.Add($"{cell.Layer}:{cell.X}:{cell.Y}"))
                {
                    yield return cell;
                }
            }
        }

        if (route.Path.Count == 1)
        {
            var cell = CellForPoint(route.Path[0], route.Layer, cellSizeMicrometers);
            yield return cell;
        }
    }

    private static IEnumerable<(int X, int Y, string Layer)> CellsForSegment(
        PhysicalPoint a,
        PhysicalPoint b,
        string layer,
        double cellSizeMicrometers)
    {
        var ax = CellIndex(a.X, cellSizeMicrometers);
        var ay = CellIndex(a.Y, cellSizeMicrometers);
        var bx = CellIndex(b.X, cellSizeMicrometers);
        var by = CellIndex(b.Y, cellSizeMicrometers);
        var xStep = Math.Sign(bx - ax);
        var yStep = Math.Sign(by - ay);
        var x = ax;
        var y = ay;

        yield return (x, y, layer);
        while (x != bx)
        {
            x += xStep;
            yield return (x, y, layer);
        }

        while (y != by)
        {
            y += yStep;
            yield return (x, y, layer);
        }
    }

    private static (int X, int Y, string Layer) CellForPoint(
        PhysicalPoint point,
        string layer,
        double cellSizeMicrometers) =>
        (CellIndex(point.X, cellSizeMicrometers), CellIndex(point.Y, cellSizeMicrometers), layer);

    private static int CellIndex(double coordinate, double cellSizeMicrometers) =>
        (int)Math.Floor(coordinate / cellSizeMicrometers);
}
