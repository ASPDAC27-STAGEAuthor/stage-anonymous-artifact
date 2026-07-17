namespace HardwareSim.Core;

/// <summary>Represents trace export options data exchanged by hardware design and simulation workflows.</summary>
public sealed class TraceExportOptions
{
    /// <summary>Gets or sets the start cycle value carried by the enclosing trace export options contract.</summary>
    public long StartCycle { get; set; }
    /// <summary>Gets or sets the end cycle value carried by the enclosing trace export options contract.</summary>
    public long? EndCycle { get; set; }
    /// <summary>Gets or sets the event types collection carried by the enclosing trace export options contract.</summary>
    public List<TraceEventType> EventTypes { get; set; } = [];
    /// <summary>Gets or sets whether export omits individual events and writes only aggregate metrics.</summary>
    public bool MetricsOnly { get; set; }
}

/// <summary>Represents trace export summary data exchanged by hardware design and simulation workflows.</summary>
public sealed class TraceExportSummary
{
    /// <summary>Gets or sets the start cycle value carried by the enclosing trace export summary contract.</summary>
    public long StartCycle { get; set; }
    /// <summary>Gets or sets the end cycle value carried by the enclosing trace export summary contract.</summary>
    public long EndCycle { get; set; }
    /// <summary>Gets or sets whether the completed export contained only aggregate metrics.</summary>
    public bool MetricsOnly { get; set; }
    /// <summary>Gets or sets the original cycle count value carried by the enclosing trace export summary contract.</summary>
    public int OriginalCycleCount { get; set; }
    /// <summary>Gets or sets the exported cycle count value carried by the enclosing trace export summary contract.</summary>
    public int ExportedCycleCount { get; set; }
    /// <summary>Gets or sets the original event count value carried by the enclosing trace export summary contract.</summary>
    public int OriginalEventCount { get; set; }
    /// <summary>Gets or sets the exported event count value carried by the enclosing trace export summary contract.</summary>
    public int ExportedEventCount { get; set; }
    /// <summary>Gets or sets the event counts by type collection carried by the enclosing trace export summary contract.</summary>
    public Dictionary<TraceEventType, int> EventCountsByType { get; set; } = new();
}

/// <summary>Represents trace export document data exchanged by hardware design and simulation workflows.</summary>
public sealed class TraceExportDocument
{
    /// <summary>Gets or sets the trace value carried by the enclosing trace export document contract.</summary>
    public SimulationTrace Trace { get; set; } = new();
    /// <summary>Gets or sets the metrics value carried by the enclosing trace export document contract.</summary>
    public SimulationMetrics Metrics { get; set; } = new();
    /// <summary>Gets or sets the bottleneck report value carried by the enclosing trace export document contract.</summary>
    public BottleneckReport BottleneckReport { get; set; } = new();
    /// <summary>Gets or sets the summary value carried by the enclosing trace export document contract.</summary>
    public TraceExportSummary Summary { get; set; } = new();
}

/// <summary>Provides trace exporter operations for hardware design and simulation workflows.</summary>
public static class TraceExporter
{
    /// <summary>Exports the supplied data using the configured stable representation.</summary>
    public static TraceExportDocument Export(SimulationResult result, TraceExportOptions? options = null)
    {
        options ??= new TraceExportOptions();
        var start = Math.Max(0, options.StartCycle);
        var lastCycle = result.Trace.Cycles.Count == 0 ? 0 : result.Trace.Cycles.Max(c => c.Cycle);
        var end = Math.Clamp(options.EndCycle ?? lastCycle, start, lastCycle);
        var allowedEventTypes = options.EventTypes.Count == 0
            ? Enum.GetValues(typeof(TraceEventType)).Cast<TraceEventType>().ToHashSet()
            : options.EventTypes.ToHashSet();

        var originalEventCount = result.Trace.Cycles.Sum(c => c.Events.Count);
        var exportedTrace = new SimulationTrace();
        if (!options.MetricsOnly)
        {
            foreach (var cycle in result.Trace.Cycles.Where(c => c.Cycle >= start && c.Cycle <= end))
            {
                exportedTrace.Cycles.Add(new CycleTraceRecord
                {
                    Cycle = cycle.Cycle,
                    Events = cycle.Events.Where(e => allowedEventTypes.Contains(e.Type)).ToList()
                });
            }
        }

        var exportedEvents = exportedTrace.Cycles.SelectMany(c => c.Events).ToList();
        return new TraceExportDocument
        {
            Trace = exportedTrace,
            Metrics = result.Metrics,
            BottleneckReport = result.BottleneckReport,
            Summary = new TraceExportSummary
            {
                StartCycle = start,
                EndCycle = end,
                MetricsOnly = options.MetricsOnly,
                OriginalCycleCount = result.Trace.Cycles.Count,
                ExportedCycleCount = exportedTrace.Cycles.Count,
                OriginalEventCount = originalEventCount,
                ExportedEventCount = exportedEvents.Count,
                EventCountsByType = exportedEvents
                    .GroupBy(e => e.Type)
                    .ToDictionary(g => g.Key, g => g.Count())
            }
        };
    }
}
