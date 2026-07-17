namespace HardwareSim.Core;

/// <summary>Defines replay component visual states rendered by the Unity graph.</summary>
public enum ReplayComponentVisualState
{
    /// <summary>The component is idle at the current replay cycle.</summary>
    Idle,
    /// <summary>The component is actively computing useful work.</summary>
    Computing,
    /// <summary>The component is blocked by a resource or dependency.</summary>
    Stalled,
    /// <summary>The component is waiting for an input packet, flit, or operand.</summary>
    WaitingInput,
    /// <summary>The component has work ready but is waiting for downstream output capacity.</summary>
    WaitingOutput
}

/// <summary>Defines replay link visual states rendered by the Unity graph.</summary>
public enum ReplayLinkVisualState
{
    /// <summary>The link is idle at the current replay cycle.</summary>
    Idle,
    /// <summary>The link is carrying packet or flit traffic.</summary>
    Active,
    /// <summary>The link is associated with congestion or backpressure evidence.</summary>
    Congested
}

/// <summary>Represents one component visual state for replay rendering.</summary>
public sealed class ReplayComponentVisual
{
    /// <summary>Gets or sets the component identifier.</summary>
    public string ComponentId { get; set; } = "";
    /// <summary>Gets or sets the replay visual state.</summary>
    public ReplayComponentVisualState State { get; set; } = ReplayComponentVisualState.Idle;
    /// <summary>Gets or sets buffer occupancy ratio in the inclusive range [0,1].</summary>
    public double OccupancyRatio { get; set; }
    /// <summary>Gets or sets component utilization ratio in the inclusive range [0,1].</summary>
    public double UtilizationRatio { get; set; }
    /// <summary>Gets or sets event evidence that caused the visual state.</summary>
    public string Evidence { get; set; } = "";
}

/// <summary>Represents one link visual state for replay rendering.</summary>
public sealed class ReplayLinkVisual
{
    /// <summary>Gets or sets the link identifier.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Gets or sets the replay visual state.</summary>
    public ReplayLinkVisualState State { get; set; } = ReplayLinkVisualState.Idle;
    /// <summary>Gets or sets packet ids active on this link at the current replay cycle.</summary>
    public List<string> ActivePacketIds { get; set; } = [];
    /// <summary>Gets or sets event evidence that caused the visual state.</summary>
    public string Evidence { get; set; } = "";
}

/// <summary>Represents one animated packet marker for replay rendering.</summary>
public sealed class ReplayPacketVisual
{
    /// <summary>Gets or sets the packet identifier.</summary>
    public string PacketId { get; set; } = "";
    /// <summary>Gets or sets the link identifier carrying the packet.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Gets or sets packet bit count used to scale marker size.</summary>
    public int Bits { get; set; }
    /// <summary>Gets or sets packet precision label used to choose marker color.</summary>
    public string Precision { get; set; } = "unknown";
    /// <summary>Gets or sets deterministic path progress in the inclusive range [0,1].</summary>
    public double Progress { get; set; }
}

/// <summary>Represents a Unity-ready replay visualization snapshot for one cycle.</summary>
public sealed class ReplayVisualizationSnapshot
{
    /// <summary>Gets or sets the replay cycle represented by this visualization snapshot.</summary>
    public long CurrentCycle { get; set; }
    /// <summary>Gets component visual states for this replay cycle.</summary>
    public List<ReplayComponentVisual> Components { get; } = [];
    /// <summary>Gets link visual states for this replay cycle.</summary>
    public List<ReplayLinkVisual> Links { get; } = [];
    /// <summary>Gets animated packet markers for this replay cycle.</summary>
    public List<ReplayPacketVisual> Packets { get; } = [];
}

/// <summary>Builds Unity-ready replay visualization snapshots from replay cycle details and metrics.</summary>
public static class ReplayVisualizationBuilder
{
    /// <summary>Builds an empty visualization snapshot for the supplied cycle.</summary>
    public static ReplayVisualizationSnapshot Empty(long cycle = 0) => new() { CurrentCycle = cycle };

    /// <summary>Builds component, link, packet, occupancy, and utilization visuals for one replay cycle.</summary>
    public static ReplayVisualizationSnapshot Build(ReplayCycleDetails details, SimulationMetrics? metrics = null)
    {
        var snapshot = Empty(details.CurrentCycle);
        var events = details.Events ?? [];
        metrics ??= new SimulationMetrics();

        foreach (var componentId in ComponentIds(events, metrics))
        {
            var componentEvents = events
                .Where(traceEvent => string.Equals(traceEvent.ComponentId, componentId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            metrics.Components.TryGetValue(componentId, out var componentMetrics);
            snapshot.Components.Add(new ReplayComponentVisual
            {
                ComponentId = componentId,
                State = ComponentState(componentEvents),
                OccupancyRatio = OccupancyRatio(componentEvents, componentMetrics),
                UtilizationRatio = componentMetrics?.Utilization ?? 0,
                Evidence = Evidence(componentEvents)
            });
        }

        foreach (var linkId in LinkIds(events, metrics))
        {
            var linkEvents = events
                .Where(traceEvent => string.Equals(traceEvent.LinkId, linkId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            snapshot.Links.Add(new ReplayLinkVisual
            {
                LinkId = linkId,
                State = LinkState(linkEvents),
                ActivePacketIds = linkEvents
                    .Select(traceEvent => traceEvent.PacketId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Evidence = Evidence(linkEvents)
            });
        }

        foreach (var traceEvent in events.Where(IsPacketAnimationEvent))
        {
            snapshot.Packets.Add(new ReplayPacketVisual
            {
                PacketId = traceEvent.PacketId!,
                LinkId = traceEvent.LinkId!,
                Bits = Math.Max(0, traceEvent.Bits),
                Precision = DetailValue(traceEvent.Detail, "precision") ?? DetailValue(traceEvent.Detail, "packet_precision") ?? "unknown",
                Progress = Clamp01(ParseDouble(DetailValue(traceEvent.Detail, "progress")) ?? ((details.CurrentCycle % 16 + 1) / 17.0))
            });
        }

        return snapshot;
    }

    private static IReadOnlyList<string> ComponentIds(IReadOnlyList<TraceEvent> events, SimulationMetrics metrics)
    {
        var ids = new HashSet<string>(metrics.Components.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var componentId in events.Select(traceEvent => traceEvent.ComponentId).Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            ids.Add(componentId!);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> LinkIds(IReadOnlyList<TraceEvent> events, SimulationMetrics metrics)
    {
        var ids = new HashSet<string>(metrics.Links.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var linkId in events.Select(traceEvent => traceEvent.LinkId).Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            ids.Add(linkId!);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ReplayComponentVisualState ComponentState(IReadOnlyList<TraceEvent> events)
    {
        if (events.Any(traceEvent => traceEvent.Type == TraceEventType.Stall))
        {
            return ReplayComponentVisualState.Stalled;
        }

        if (events.Any(traceEvent => traceEvent.Type == TraceEventType.Compute))
        {
            return ReplayComponentVisualState.Computing;
        }

        if (events.Any(traceEvent => string.Equals(DetailValue(traceEvent.Detail, "state"), nameof(ReplayComponentVisualState.WaitingOutput), StringComparison.OrdinalIgnoreCase)))
        {
            return ReplayComponentVisualState.WaitingOutput;
        }

        if (events.Any(traceEvent => string.Equals(DetailValue(traceEvent.Detail, "state"), nameof(ReplayComponentVisualState.WaitingInput), StringComparison.OrdinalIgnoreCase)))
        {
            return ReplayComponentVisualState.WaitingInput;
        }

        return ReplayComponentVisualState.Idle;
    }

    private static ReplayLinkVisualState LinkState(IReadOnlyList<TraceEvent> events)
    {
        if (events.Any(traceEvent => traceEvent.Type == TraceEventType.Stall))
        {
            return ReplayLinkVisualState.Congested;
        }

        if (events.Any(traceEvent => traceEvent.Type is TraceEventType.PacketMove or TraceEventType.LinkTransfer or TraceEventType.FlitIssue or TraceEventType.FlitSerialization or TraceEventType.FlitArrival))
        {
            return ReplayLinkVisualState.Active;
        }

        return ReplayLinkVisualState.Idle;
    }

    private static double OccupancyRatio(IReadOnlyList<TraceEvent> events, ComponentMetrics? metrics)
    {
        var ratios = events.Select(traceEvent =>
            {
                var occupancy = ParseDouble(DetailValue(traceEvent.Detail, "occupancy"));
                var capacity = ParseDouble(DetailValue(traceEvent.Detail, "capacity")) ?? ParseDouble(DetailValue(traceEvent.Detail, "capacity_bits"));
                return occupancy.HasValue && capacity.HasValue && capacity.Value > 0
                    ? Clamp01(occupancy.Value / capacity.Value)
                    : (double?)null;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        if (ratios.Count > 0)
        {
            return ratios.Max();
        }

        if (metrics is not null && metrics.MemoryCapacityBits > 0)
        {
            return Clamp01((double)metrics.OccupancyBits / metrics.MemoryCapacityBits);
        }

        return 0;
    }

    private static bool IsPacketAnimationEvent(TraceEvent traceEvent) =>
        !string.IsNullOrWhiteSpace(traceEvent.PacketId) &&
        !string.IsNullOrWhiteSpace(traceEvent.LinkId) &&
        traceEvent.Type is TraceEventType.PacketMove or TraceEventType.LinkTransfer or TraceEventType.FlitIssue or TraceEventType.FlitSerialization or TraceEventType.FlitArrival;

    private static string Evidence(IReadOnlyList<TraceEvent> events) => string.Join(" | ", events
        .Select(traceEvent => string.IsNullOrWhiteSpace(traceEvent.Detail)
            ? traceEvent.Type.ToString()
            : $"{traceEvent.Type}:{traceEvent.Detail}")
        .Take(4));

    private static string? DetailValue(string? detail, string key)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        foreach (var part in detail.Split(';'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var candidateKey = part.Substring(0, separator).Trim();
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring(separator + 1).Trim();
            }
        }

        return null;
    }

    private static double? ParseDouble(string? value) => double.TryParse(
        value,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture,
        out var parsed)
            ? parsed
            : null;

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));
}
