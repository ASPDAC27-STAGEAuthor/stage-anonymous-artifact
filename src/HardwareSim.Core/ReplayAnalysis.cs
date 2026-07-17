namespace HardwareSim.Core;

/// <summary>Represents component heatmap entry data exchanged by hardware design and simulation workflows.</summary>
public sealed class ComponentHeatmapEntry
{
    /// <summary>Gets or sets the component id value carried by the enclosing component heatmap entry contract.</summary>
    public string ComponentId { get; set; } = "";
    /// <summary>Gets or sets the traffic bits value carried by the enclosing component heatmap entry contract.</summary>
    public long TrafficBits { get; set; }
    /// <summary>Gets or sets the active cycles value carried by the enclosing component heatmap entry contract.</summary>
    public long ActiveCycles { get; set; }
    /// <summary>Gets or sets the stall cycles value carried by the enclosing component heatmap entry contract.</summary>
    public long StallCycles { get; set; }
    /// <summary>Gets or sets the dominant stall reason value carried by the enclosing component heatmap entry contract.</summary>
    public string DominantStallReason { get; set; } = "";
    /// <summary>Gets or sets the heat value carried by the enclosing component heatmap entry contract.</summary>
    public double Heat { get; set; }
}

/// <summary>Represents link heatmap entry data exchanged by hardware design and simulation workflows.</summary>
public sealed class LinkHeatmapEntry
{
    /// <summary>Gets or sets the link id value carried by the enclosing link heatmap entry contract.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Gets or sets the transferred bits value carried by the enclosing link heatmap entry contract.</summary>
    public long TransferredBits { get; set; }
    /// <summary>Gets or sets the congestion cycles value carried by the enclosing link heatmap entry contract.</summary>
    public long CongestionCycles { get; set; }
    /// <summary>Gets or sets the heat value carried by the enclosing link heatmap entry contract.</summary>
    public double Heat { get; set; }
}

/// <summary>Represents timeline bin data exchanged by hardware design and simulation workflows.</summary>
public sealed class TimelineBin
{
    /// <summary>Gets or sets the start cycle value carried by the enclosing timeline bin contract.</summary>
    public long StartCycle { get; set; }
    /// <summary>Gets or sets the end cycle value carried by the enclosing timeline bin contract.</summary>
    public long EndCycle { get; set; }
    /// <summary>Gets or sets the event count value carried by the enclosing timeline bin contract.</summary>
    public int EventCount { get; set; }
    /// <summary>Gets or sets the event counts by type collection carried by the enclosing timeline bin contract.</summary>
    public Dictionary<TraceEventType, int> EventCountsByType { get; set; } = new();
}

/// <summary>Represents adapter runtime summary entry data exchanged by hardware design and simulation workflows.</summary>
public sealed class AdapterRuntimeSummaryEntry
{
    /// <summary>Gets or sets the component id value carried by the enclosing adapter runtime summary entry contract.</summary>
    public string ComponentId { get; set; } = "";
    /// <summary>Gets or sets the component type value carried by the enclosing adapter runtime summary entry contract.</summary>
    public string ComponentType { get; set; } = "";
    /// <summary>Gets or sets the adapter type value carried by the enclosing adapter runtime summary entry contract.</summary>
    public string AdapterType { get; set; } = "";
    /// <summary>Gets or sets the mismatch field value carried by the enclosing adapter runtime summary entry contract.</summary>
    public string MismatchField { get; set; } = "";
    /// <summary>Gets or sets the source value carried by the enclosing adapter runtime summary entry contract.</summary>
    public string SourceValue { get; set; } = "";
    /// <summary>Gets or sets the destination value carried by the enclosing adapter runtime summary entry contract.</summary>
    public string DestinationValue { get; set; } = "";
    /// <summary>Gets or sets the active cycles value carried by the enclosing adapter runtime summary entry contract.</summary>
    public long ActiveCycles { get; set; }
    /// <summary>Gets or sets the input traffic bits value carried by the enclosing adapter runtime summary entry contract.</summary>
    public long InputTrafficBits { get; set; }
    /// <summary>Gets or sets the output traffic bits value carried by the enclosing adapter runtime summary entry contract.</summary>
    public long OutputTrafficBits { get; set; }
    /// <summary>Gets or sets the energy value carried by the enclosing adapter runtime summary entry contract.</summary>
    public double Energy { get; set; }
    /// <summary>Gets or sets the pass through event count value carried by the enclosing adapter runtime summary entry contract.</summary>
    public int PassThroughEventCount { get; set; }
    /// <summary>Gets or sets the precision conversion event count value carried by the enclosing adapter runtime summary entry contract.</summary>
    public int PrecisionConversionEventCount { get; set; }
}

/// <summary>Represents replay analysis snapshot data exchanged by hardware design and simulation workflows.</summary>
public sealed class ReplayAnalysisSnapshot
{
    /// <summary>Gets or sets the components collection carried by the enclosing replay analysis snapshot contract.</summary>
    public List<ComponentHeatmapEntry> Components { get; set; } = [];
    /// <summary>Gets or sets the links collection carried by the enclosing replay analysis snapshot contract.</summary>
    public List<LinkHeatmapEntry> Links { get; set; } = [];
    /// <summary>Gets or sets the timeline collection carried by the enclosing replay analysis snapshot contract.</summary>
    public List<TimelineBin> Timeline { get; set; } = [];
    /// <summary>Gets or sets the adapter runtime collection carried by the enclosing replay analysis snapshot contract.</summary>
    public List<AdapterRuntimeSummaryEntry> AdapterRuntime { get; set; } = [];
}

/// <summary>Provides replay analysis builder operations for hardware design and simulation workflows.</summary>
public static class ReplayAnalysisBuilder
{
    /// <summary>Derives component and link heatmaps, timeline bins, and adapter summaries from simulation events.</summary>
    public static ReplayAnalysisSnapshot Build(SimulationResult result, int timelineBinSize = 10, HardwareGraph? graph = null)
    {
        timelineBinSize = Math.Max(1, timelineBinSize);
        var snapshot = new ReplayAnalysisSnapshot
        {
            Components = BuildComponentHeatmap(result.Metrics),
            Links = BuildLinkHeatmap(result.Metrics),
            Timeline = BuildTimeline(result.Trace, timelineBinSize),
            AdapterRuntime = BuildAdapterRuntimeSummary(result, graph)
        };

        return snapshot;
    }

    private static List<ComponentHeatmapEntry> BuildComponentHeatmap(SimulationMetrics metrics)
    {
        var entries = metrics.Components.Values
            .Select(c =>
            {
                var dominantStall = c.StallCyclesByReason.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
                return new ComponentHeatmapEntry
                {
                    ComponentId = c.ComponentId,
                    TrafficBits = c.InputTrafficBits + c.OutputTrafficBits,
                    ActiveCycles = c.ActiveCycles,
                    StallCycles = c.StallCycles,
                    DominantStallReason = dominantStall.Value > 0 ? dominantStall.Key.ToString() : "",
                    Heat = c.InputTrafficBits + c.OutputTrafficBits + c.StallCycles
                };
            })
            .ToList();

        Normalize(entries, e => e.Heat, (e, heat) => e.Heat = heat);
        return entries;
    }

    private static List<LinkHeatmapEntry> BuildLinkHeatmap(SimulationMetrics metrics)
    {
        var entries = metrics.Links.Values
            .Select(l => new LinkHeatmapEntry
            {
                LinkId = l.LinkId,
                TransferredBits = l.TotalBitsTransferred,
                CongestionCycles = l.CongestionCycles,
                Heat = l.TotalBitsTransferred + l.CongestionCycles
            })
            .ToList();

        Normalize(entries, e => e.Heat, (e, heat) => e.Heat = heat);
        return entries;
    }

    private static List<TimelineBin> BuildTimeline(SimulationTrace trace, int binSize)
    {
        if (trace.Cycles.Count == 0)
        {
            return [];
        }

        var firstCycle = trace.Cycles.Min(c => c.Cycle);
        var lastCycle = trace.Cycles.Max(c => c.Cycle);
        var bins = new List<TimelineBin>();

        for (var start = firstCycle; start <= lastCycle; start += binSize)
        {
            var end = Math.Min(lastCycle, start + binSize - 1);
            var events = trace.Cycles
                .Where(c => c.Cycle >= start && c.Cycle <= end)
                .SelectMany(c => c.Events)
                .ToList();

            bins.Add(new TimelineBin
            {
                StartCycle = start,
                EndCycle = end,
                EventCount = events.Count,
                EventCountsByType = events
                    .GroupBy(e => e.Type)
                    .ToDictionary(g => g.Key, g => g.Count())
            });
        }

        return bins;
    }

    private static List<AdapterRuntimeSummaryEntry> BuildAdapterRuntimeSummary(SimulationResult result, HardwareGraph? graph)
    {
        var events = result.Trace.Cycles.SelectMany(c => c.Events).ToList();
        var passThroughCounts = events
            .Where(e => e.Type == TraceEventType.Compute &&
                        e.ComponentId is not null &&
                        e.Detail?.StartsWith("adapter_pass_through:", StringComparison.Ordinal) == true)
            .GroupBy(e => e.ComponentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var precisionCounts = events
            .Where(e => e.Type == TraceEventType.Compute &&
                        e.ComponentId is not null &&
                        e.Detail?.StartsWith("precision_conversion:", StringComparison.Ordinal) == true)
            .GroupBy(e => e.ComponentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var candidateIds = new HashSet<string>(passThroughCounts.Keys, StringComparer.OrdinalIgnoreCase);
        candidateIds.UnionWith(precisionCounts.Keys);

        if (graph is not null)
        {
            foreach (var component in graph.Components.Where(IsInsertedAdapter))
            {
                candidateIds.Add(component.Id);
            }
        }

        return candidateIds
            .OrderBy(id => graph?.FindComponent(id)?.Parameters.TryGetValue("chain_index", out var index) == true
                    ? int.Parse(index, System.Globalization.CultureInfo.InvariantCulture)
                    : int.MaxValue)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id =>
            {
                result.Metrics.Components.TryGetValue(id, out var metrics);
                var component = graph?.FindComponent(id);
                var parameters = component?.Parameters ?? new Dictionary<string, string>();

                return new AdapterRuntimeSummaryEntry
                {
                    ComponentId = id,
                    ComponentType = component?.Type.ToString() ?? "",
                    AdapterType = Parameter(parameters, "adapter_type", component?.Type.ToString() ?? ""),
                    MismatchField = Parameter(parameters, "mismatch_field"),
                    SourceValue = Parameter(parameters, "source_value"),
                    DestinationValue = Parameter(parameters, "destination_value"),
                    ActiveCycles = metrics?.ActiveCycles ?? 0,
                    InputTrafficBits = metrics?.InputTrafficBits ?? 0,
                    OutputTrafficBits = metrics?.OutputTrafficBits ?? 0,
                    Energy = metrics?.Energy ?? 0,
                    PassThroughEventCount = passThroughCounts.GetValueOrDefault(id),
                    PrecisionConversionEventCount = precisionCounts.GetValueOrDefault(id)
                };
            })
            .ToList();
    }

    private static bool IsInsertedAdapter(HardwareComponent component) =>
        component.Parameters.ContainsKey("adapter_type") &&
        component.Parameters.TryGetValue("inserted_by", out var insertedBy) &&
        insertedBy.StartsWith("adapter_insertion_", StringComparison.OrdinalIgnoreCase);

    private static string Parameter(IReadOnlyDictionary<string, string> parameters, string key, string fallback = "") =>
        parameters.TryGetValue(key, out var value) ? value : fallback;

    private static void Normalize<T>(IReadOnlyList<T> entries, Func<T, double> getValue, Action<T, double> setValue)
    {
        var max = entries.Count == 0 ? 0 : entries.Max(getValue);
        foreach (var entry in entries)
        {
            setValue(entry, max <= 0 ? 0 : getValue(entry) / max);
        }
    }
}
