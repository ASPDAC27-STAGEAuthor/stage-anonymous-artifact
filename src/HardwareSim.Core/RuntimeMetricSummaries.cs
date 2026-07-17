namespace HardwareSim.Core;

/// <summary>Exact average and peak buffer occupancy over sampled cycles.</summary>
public sealed class BufferOccupancySummary
{
    internal BufferOccupancySummary(
        string componentId,
        double averageOccupancyBits,
        BitCount peakOccupancyBits,
        CycleCount totalCycles)
    {
        ComponentId = componentId;
        AverageOccupancyBits = averageOccupancyBits;
        PeakOccupancyBits = peakOccupancyBits;
        TotalCycles = totalCycles;
    }

    /// <summary>Gets the stable buffer component identifier.</summary>
    public string ComponentId { get; }

    /// <summary>Gets arithmetic mean occupancy in bits.</summary>
    public double AverageOccupancyBits { get; }

    /// <summary>Gets the maximum sampled occupancy in bits.</summary>
    public BitCount PeakOccupancyBits { get; }

    /// <summary>Gets the number of sampled cycles.</summary>
    public CycleCount TotalCycles { get; }
}

/// <summary>Aggregates raw per-cycle buffer occupancy without trusting derived peak fields.</summary>
public static class BufferOccupancyAggregation
{
    /// <summary>Computes exact average and peak occupancy from one record per cycle.</summary>
    public static BufferOccupancySummary Aggregate(
        string componentId,
        IEnumerable<BufferCycleRecord> records)
    {
        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        var ordered = records.OrderBy(item => item.Cycle).ToList();
        var seenCycles = new HashSet<long>();
        foreach (var record in ordered)
        {
            _ = new CycleCount(record.Cycle);
            _ = new BitCount(record.OccupancyBits);
            if (!seenCycles.Add(record.Cycle))
            {
                throw new InvalidOperationException($"Buffer '{componentId}' has multiple occupancy samples for cycle {record.Cycle}.");
            }
        }

        var average = ordered.Count == 0 ? 0 : ordered.Average(item => item.OccupancyBits);
        var peak = ordered.Count == 0 ? 0 : ordered.Max(item => item.OccupancyBits);
        return new BufferOccupancySummary(componentId, average, new BitCount(peak), new CycleCount(ordered.Count));
    }
}

/// <summary>One link's emitted bits for one committed cycle.</summary>
/// <param name="Cycle">Committed cycle number.</param>
/// <param name="BitsSent">Actual serializer bits emitted during the cycle.</param>
public sealed record LinkCycleSample(long Cycle, BitCount BitsSent);

/// <summary>Exact active and total cycle counts for one link.</summary>
public sealed class LinkCycleSummary
{
    internal LinkCycleSummary(string linkId, CycleCount activeCycles, CycleCount totalCycles)
    {
        LinkId = linkId;
        ActiveCycles = activeCycles;
        TotalCycles = totalCycles;
    }

    /// <summary>Gets the stable link identifier.</summary>
    public string LinkId { get; }

    /// <summary>Gets cycles with at least one emitted bit.</summary>
    public CycleCount ActiveCycles { get; }

    /// <summary>Gets all sampled cycles.</summary>
    public CycleCount TotalCycles { get; }

    /// <summary>Gets active cycles divided by total cycles.</summary>
    public double Utilization => TotalCycles.Value == 0 ? 0 : (double)ActiveCycles.Value / TotalCycles.Value;
}

/// <summary>Aggregates one raw serializer sample per link cycle.</summary>
public static class LinkCycleAggregation
{
    /// <summary>Computes active and total cycles without counting multiple events in one cycle.</summary>
    public static LinkCycleSummary Aggregate(string linkId, IEnumerable<LinkCycleSample> samples)
    {
        if (samples is null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        var ordered = samples.OrderBy(item => item.Cycle).ToList();
        var seenCycles = new HashSet<long>();
        foreach (var sample in ordered)
        {
            _ = new CycleCount(sample.Cycle);
            if (!seenCycles.Add(sample.Cycle))
            {
                throw new InvalidOperationException($"Link '{linkId}' has multiple cycle samples for cycle {sample.Cycle}.");
            }
        }

        var active = ordered.Count(item => item.BitsSent.Value > 0);
        return new LinkCycleSummary(linkId, new CycleCount(active), new CycleCount(ordered.Count));
    }
}
