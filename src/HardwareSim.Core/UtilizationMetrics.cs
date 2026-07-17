namespace HardwareSim.Core;

/// <summary>One component's raw state signals for one committed simulation cycle.</summary>
/// <param name="Cycle">Committed cycle number.</param>
/// <param name="ComponentId">Stable component identifier.</param>
/// <param name="ComponentKind">Component kind used by filtered utilization views.</param>
/// <param name="AreaUm2">Resolved physical area used by area weighting.</param>
/// <param name="IsComputing">Whether the component has active compute or transfer work.</param>
/// <param name="IsWaitingOutput">Whether completed work is blocked waiting for output acceptance.</param>
/// <param name="IsStalled">Whether any other component-local stall condition is active.</param>
public sealed record ComponentActivitySample(
    long Cycle,
    string ComponentId,
    ComponentKind ComponentKind,
    SquareMicrometers AreaUm2,
    bool IsComputing = false,
    bool IsWaitingOutput = false,
    bool IsStalled = false);

/// <summary>Exact mutually exclusive cycle counts for one component.</summary>
public sealed class ComponentUtilizationMetric
{
    internal ComponentUtilizationMetric(
        string componentId,
        ComponentKind componentKind,
        SquareMicrometers areaUm2,
        CycleCount activeCycles,
        CycleCount idleCycles,
        CycleCount stallCycles)
    {
        ComponentId = componentId;
        ComponentKind = componentKind;
        AreaUm2 = areaUm2;
        ActiveCycles = activeCycles;
        IdleCycles = idleCycles;
        StallCycles = stallCycles;
    }

    /// <summary>Gets the stable component identifier.</summary>
    public string ComponentId { get; }

    /// <summary>Gets the component kind.</summary>
    public ComponentKind ComponentKind { get; }

    /// <summary>Gets the resolved physical area.</summary>
    public SquareMicrometers AreaUm2 { get; }

    /// <summary>Gets cycles classified as active.</summary>
    public CycleCount ActiveCycles { get; }

    /// <summary>Gets cycles classified as idle.</summary>
    public CycleCount IdleCycles { get; }

    /// <summary>Gets cycles classified as stalled.</summary>
    public CycleCount StallCycles { get; }

    /// <summary>Gets the exact partition total.</summary>
    public CycleCount TotalCycles => new(ActiveCycles.Value + IdleCycles.Value + StallCycles.Value);

    /// <summary>Gets active cycles divided by the exact partition total.</summary>
    public double Utilization => TotalCycles.Value == 0 ? 0 : (double)ActiveCycles.Value / TotalCycles.Value;
}

/// <summary>Four required system-level utilization views.</summary>
public sealed class GlobalUtilizationMetrics
{
    internal GlobalUtilizationMetrics(double average, double areaWeighted, double peOnly, double routerOnly)
    {
        Average = average;
        AreaWeighted = areaWeighted;
        PeOnly = peOnly;
        RouterOnly = routerOnly;
    }

    /// <summary>Gets the arithmetic mean of component utilizations.</summary>
    public double Average { get; }

    /// <summary>Gets utilization weighted by resolved physical component area.</summary>
    public double AreaWeighted { get; }

    /// <summary>Gets active PE cycles divided by classified PE cycles.</summary>
    public double PeOnly { get; }

    /// <summary>Gets active Router cycles divided by classified Router cycles.</summary>
    public double RouterOnly { get; }
}

/// <summary>Contains component partitions and the four global utilization views.</summary>
public sealed class UtilizationAggregationResult
{
    internal UtilizationAggregationResult(
        IReadOnlyList<ComponentUtilizationMetric> components,
        GlobalUtilizationMetrics global)
    {
        Components = components;
        Global = global;
    }

    /// <summary>Gets component metrics in stable component-id order.</summary>
    public IReadOnlyList<ComponentUtilizationMetric> Components { get; }

    /// <summary>Gets the four global views.</summary>
    public GlobalUtilizationMetrics Global { get; }
}

/// <summary>Classifies raw state signals into one and only one state per component cycle.</summary>
public static class UtilizationAggregation
{
    /// <summary>Aggregates mutually exclusive component states and global utilization views.</summary>
    public static UtilizationAggregationResult Aggregate(IEnumerable<ComponentActivitySample> samples)
    {
        if (samples is null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        var ordered = samples
            .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.Cycle)
            .ToList();
        var seen = new HashSet<(string ComponentId, long Cycle)>();
        foreach (var sample in ordered)
        {
            _ = new CycleCount(sample.Cycle);
            if (!seen.Add((sample.ComponentId, sample.Cycle)))
            {
                throw new InvalidOperationException($"Component '{sample.ComponentId}' has multiple state samples for cycle {sample.Cycle}.");
            }
        }

        var components = new List<ComponentUtilizationMetric>();
        foreach (var group in ordered.GroupBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Any(item => item.ComponentKind != first.ComponentKind || item.AreaUm2 != first.AreaUm2))
            {
                throw new InvalidOperationException($"Component '{first.ComponentId}' changed kind or area within one utilization aggregation.");
            }

            long active = 0;
            long idle = 0;
            long stall = 0;
            foreach (var sample in group)
            {
                switch (Classify(sample))
                {
                    case ComponentActivityState.Active: active++; break;
                    case ComponentActivityState.Idle: idle++; break;
                    case ComponentActivityState.Stall: stall++; break;
                    default: throw new InvalidOperationException("Unknown component activity state.");
                }
            }

            components.Add(new ComponentUtilizationMetric(
                first.ComponentId,
                first.ComponentKind,
                first.AreaUm2,
                new CycleCount(active),
                new CycleCount(idle),
                new CycleCount(stall)));
        }

        var average = components.Count == 0 ? 0 : components.Average(item => item.Utilization);
        var areaDenominator = components.Sum(item => item.AreaUm2.Value);
        var areaWeighted = areaDenominator == 0
            ? 0
            : components.Sum(item => item.Utilization * item.AreaUm2.Value) / areaDenominator;
        var global = new GlobalUtilizationMetrics(
            average,
            areaWeighted,
            CycleWeighted(components, ComponentKind.ProcessingElement),
            CycleWeighted(components, ComponentKind.Router));
        return new UtilizationAggregationResult(components.AsReadOnly(), global);
    }

    private static ComponentActivityState Classify(ComponentActivitySample sample)
    {
        if (sample.IsWaitingOutput || sample.IsStalled)
        {
            return ComponentActivityState.Stall;
        }

        return sample.IsComputing ? ComponentActivityState.Active : ComponentActivityState.Idle;
    }

    private static double CycleWeighted(
        IEnumerable<ComponentUtilizationMetric> components,
        ComponentKind kind)
    {
        var selected = components.Where(item => item.ComponentKind == kind).ToList();
        var total = selected.Sum(item => item.TotalCycles.Value);
        return total == 0 ? 0 : (double)selected.Sum(item => item.ActiveCycles.Value) / total;
    }
}
