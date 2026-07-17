namespace HardwareSim.Core;

/// <summary>Represents deterministic metrics derived from an explicit physical route path.</summary>
/// <param name="LengthMicrometers">Provides route length in micrometers.</param>
/// <param name="BendCount">Provides the number of direction changes along the route path.</param>
public sealed record PhysicalRouteMetricResult(double LengthMicrometers, int BendCount);

/// <summary>Calculates length and bend metrics from explicit physical route paths.</summary>
public static class PhysicalRouteMetrics
{
    /// <summary>Calculates route length and bend count from explicit route path points.</summary>
    public static PhysicalRouteMetricResult Analyze(IReadOnlyList<PhysicalPoint> path) =>
        new(CalculateLengthMicrometers(path), CountBends(path));

    /// <summary>Calculates exact Manhattan length in micrometers from the supplied route path.</summary>
    public static double CalculateLengthMicrometers(IReadOnlyList<PhysicalPoint> path)
    {
        if (path.Count < 2)
        {
            return 0;
        }

        var total = 0.0;
        for (var index = 1; index < path.Count; index++)
        {
            total += Math.Abs(path[index].X - path[index - 1].X) + Math.Abs(path[index].Y - path[index - 1].Y);
        }

        return total;
    }

    /// <summary>Counts direction changes in the supplied route path.</summary>
    public static int CountBends(IReadOnlyList<PhysicalPoint> path)
    {
        var bends = 0;
        (int X, int Y)? previousDirection = null;
        for (var index = 1; index < path.Count; index++)
        {
            var direction = Direction(path[index - 1], path[index]);
            if (direction is null)
            {
                continue;
            }

            if (previousDirection is not null && previousDirection.Value != direction.Value)
            {
                bends++;
            }

            previousDirection = direction;
        }

        return bends;
    }

    private static (int X, int Y)? Direction(PhysicalPoint previous, PhysicalPoint current)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        if (Math.Abs(dx) < 0.000000001 && Math.Abs(dy) < 0.000000001)
        {
            return null;
        }

        return (Math.Sign(dx), Math.Sign(dy));
    }
}
