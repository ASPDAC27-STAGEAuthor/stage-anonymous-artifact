namespace HardwareSim.Core;

/// <summary>Defines axis order used by deterministic Manhattan route generation.</summary>
public enum ManhattanRouteAxisOrder
{
    /// <summary>Route horizontal segments before vertical segments.</summary>
    XThenY,
    /// <summary>Route vertical segments before horizontal segments.</summary>
    YThenX
}

/// <summary>Builds deterministic explicit physical route paths from already-resolved physical endpoints.</summary>
public static class ManhattanRoutePlanner
{
    /// <summary>Builds a Manhattan route path between two physical points using the supplied step size.</summary>
    public static IReadOnlyList<PhysicalPoint> GeneratePath(
        PhysicalPoint start,
        PhysicalPoint end,
        double stepMicrometers = 100,
        ManhattanRouteAxisOrder axisOrder = ManhattanRouteAxisOrder.XThenY)
    {
        if (stepMicrometers <= 0 || double.IsNaN(stepMicrometers) || double.IsInfinity(stepMicrometers))
        {
            throw new ArgumentOutOfRangeException(nameof(stepMicrometers), "Route step size must be a finite positive distance.");
        }

        var points = new List<PhysicalPoint> { start };
        if (axisOrder == ManhattanRouteAxisOrder.XThenY)
        {
            AddAxisPoints(points, end.X, isX: true, stepMicrometers);
            AddAxisPoints(points, end.Y, isX: false, stepMicrometers);
        }
        else
        {
            AddAxisPoints(points, end.Y, isX: false, stepMicrometers);
            AddAxisPoints(points, end.X, isX: true, stepMicrometers);
        }

        AddIfChanged(points, end);
        return points;
    }

    /// <summary>Creates a physical route bound to a resolved logical target and generated Manhattan path.</summary>
    public static PhysicalRoute CreateRoute(
        string linkId,
        PhysicalPoint start,
        PhysicalPoint end,
        RoutingLayerId? layerId = null,
        RoutingMedium medium = RoutingMedium.ElectricalMetal,
        PhysicalRouteTargetKind targetKind = PhysicalRouteTargetKind.LogicalLink,
        double stepMicrometers = 100,
        ManhattanRouteAxisOrder axisOrder = ManhattanRouteAxisOrder.XThenY) => new()
    {
        LinkId = linkId,
        TargetKind = targetKind,
        Medium = medium,
        LayerId = layerId ?? RoutingLayerId.Metal(3),
        PathUnit = PhysicalRoutePointUnit.Micrometers,
        Path = GeneratePath(start, end, stepMicrometers, axisOrder).ToList()
    };

    private static void AddAxisPoints(List<PhysicalPoint> points, double target, bool isX, double stepMicrometers)
    {
        var current = points[^1];
        var currentValue = isX ? current.X : current.Y;
        var delta = target - currentValue;
        if (Math.Abs(delta) < 0.000000001)
        {
            return;
        }

        var direction = Math.Sign(delta);
        var remaining = Math.Abs(delta);
        while (remaining > stepMicrometers + 0.000000001)
        {
            currentValue += direction * stepMicrometers;
            AddIfChanged(points, isX ? new PhysicalPoint(currentValue, current.Y) : new PhysicalPoint(current.X, currentValue));
            remaining -= stepMicrometers;
        }

        AddIfChanged(points, isX ? new PhysicalPoint(target, current.Y) : new PhysicalPoint(current.X, target));
    }

    private static void AddIfChanged(List<PhysicalPoint> points, PhysicalPoint point)
    {
        var last = points[^1];
        if (Math.Abs(last.X - point.X) < 0.000000001 && Math.Abs(last.Y - point.Y) < 0.000000001)
        {
            return;
        }

        points.Add(point);
    }
}
