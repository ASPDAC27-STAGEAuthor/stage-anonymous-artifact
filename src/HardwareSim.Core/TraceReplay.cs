namespace HardwareSim.Core;

/// <summary>Defines the supported replay state values used by hardware simulation contracts.</summary>
public enum ReplayState
{
    /// <summary>Replay time remains fixed until explicitly advanced.</summary>
    Paused,
    /// <summary>Replay time advances according to the configured playback speed.</summary>
    Playing
}

/// <summary>Represents one packet path event with its cycle coordinate.</summary>
/// <param name="Cycle">Provides the cycle containing the packet event.</param>
/// <param name="Event">Provides the packet event recorded at the cycle.</param>
public sealed record ReplayPacketPathStep(long Cycle, TraceEvent Event);

/// <summary>Represents current replay cycle details after active filters are applied.</summary>
/// <param name="CurrentCycle">Provides the current replay cycle.</param>
/// <param name="LastCycle">Provides the last replay cycle available in the trace.</param>
/// <param name="State">Provides the replay controller state.</param>
/// <param name="HasCycleRecord">Indicates whether the current cycle exists in the persisted or in-memory trace.</param>
/// <param name="RawEventCount">Provides the unfiltered event count for the current cycle.</param>
/// <param name="Events">Provides filtered current-cycle events.</param>
/// <param name="PacketIds">Provides packet ids present in filtered events.</param>
/// <param name="ComponentIds">Provides component ids present in filtered events.</param>
/// <param name="LinkIds">Provides link ids present in filtered events.</param>
/// <param name="SourceKind">Identifies whether replay was loaded from memory or persisted trace storage.</param>
/// <param name="TraceLevel">Provides the persisted trace level when replay was loaded from a trace store.</param>
public sealed record ReplayCycleDetails(
    long CurrentCycle,
    long LastCycle,
    ReplayState State,
    bool HasCycleRecord,
    int RawEventCount,
    IReadOnlyList<TraceEvent> Events,
    IReadOnlyList<string> PacketIds,
    IReadOnlyList<string> ComponentIds,
    IReadOnlyList<string> LinkIds,
    string SourceKind,
    TraceLevel? TraceLevel);

/// <summary>Represents trace replay controller data exchanged by hardware design and simulation workflows.</summary>
public sealed class TraceReplayController : IDisposable
{
    private readonly PersistedTraceStoreReader? store;
    private readonly Dictionary<long, CycleTraceRecord> cyclesByCycle;
    private readonly List<long> cycleOrder;
    private readonly HashSet<TraceEventType> enabledEventTypes = [];
    private readonly HashSet<string> packetFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> componentFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly long lastCycle;
    private Dictionary<string, string>? logicalIdsByPhysical;
    private Dictionary<string, HashSet<string>>? physicalIdsByLogical;

    /// <summary>Initializes a new trace replay controller instance from an in-memory trace.</summary>
    public TraceReplayController(SimulationTrace trace)
    {
        if (trace is null)
        {
            throw new ArgumentNullException(nameof(trace));
        }

        cyclesByCycle = trace.Cycles
            .GroupBy(cycle => cycle.Cycle)
            .ToDictionary(group => group.Key, group => group.Last());
        cycleOrder = cyclesByCycle.Keys.OrderBy(cycle => cycle).ToList();
        lastCycle = cycleOrder.Count == 0 ? 0 : cycleOrder[^1];
        EnableAllEventTypes();
    }

    /// <summary>Initializes a new trace replay controller instance from persisted trace storage.</summary>
    public TraceReplayController(PersistedTraceStoreReader traceStore)
    {
        store = traceStore ?? throw new ArgumentNullException(nameof(traceStore));
        cyclesByCycle = new Dictionary<long, CycleTraceRecord>();
        cycleOrder = store.Index.Select(entry => entry.Cycle).OrderBy(cycle => cycle).ToList();
        lastCycle = store.Manifest.LastCycle ?? 0;
        EnableAllEventTypes();
    }

    /// <summary>Gets the state value carried by the enclosing trace replay controller contract.</summary>
    public ReplayState State { get; private set; } = ReplayState.Paused;
    /// <summary>Gets the current cycle value carried by the enclosing trace replay controller contract.</summary>
    public long CurrentCycle { get; private set; }
    /// <summary>Gets the selected packet id value carried by the compatibility single-packet filter.</summary>
    public string? SelectedPacketId { get; private set; }
    /// <summary>Gets the selected component id value carried by the compatibility single-component filter.</summary>
    public string? SelectedComponentId { get; private set; }
    /// <summary>Gets the last cycle value carried by the enclosing trace replay controller contract.</summary>
    public long LastCycle => lastCycle;
    /// <summary>Gets whether replay was loaded from persisted trace storage rather than a live simulation result.</summary>
    public bool LoadedFromPersistentTrace => store is not null;
    /// <summary>Gets a stable label for the replay data source.</summary>
    public string SourceKind => LoadedFromPersistentTrace ? "persisted-trace-store" : "in-memory-trace";
    /// <summary>Gets the persisted manifest when replay was loaded from a trace store.</summary>
    public PersistedTraceManifest? Manifest => store?.Manifest;
    /// <summary>Gets active packet filters.</summary>
    public IReadOnlyList<string> PacketFilters => packetFilters.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    /// <summary>Gets active component filters.</summary>
    public IReadOnlyList<string> ComponentFilters => componentFilters.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Loads a replay controller from a persisted trace store directory.</summary>
    public static TraceReplayController LoadFromStore(string directory) => new(PersistedTraceStore.Open(directory));

    /// <summary>Advances the replay controller into its playing state.</summary>
    public void Play()
    {
        if (LastCycle <= 0)
        {
            State = ReplayState.Paused;
            return;
        }

        if (CurrentCycle >= LastCycle)
        {
            CurrentCycle = 0;
        }

        State = ReplayState.Playing;
    }
    /// <summary>Stops automatic replay advancement at the current cycle.</summary>
    public void Pause() => State = ReplayState.Paused;

    /// <summary>Moves forward by at least one cycle without passing the end of the trace.</summary>
    public void StepForward(int cycles = 1)
    {
        CurrentCycle = Math.Min(LastCycle, CurrentCycle + Math.Max(1, cycles));
    }

    /// <summary>Moves backward by at least one cycle without passing cycle zero.</summary>
    public void StepBackward(int cycles = 1)
    {
        CurrentCycle = Math.Max(0, CurrentCycle - Math.Max(1, cycles));
    }

    /// <summary>Moves directly to a cycle clamped to the available trace range.</summary>
    public void JumpTo(long cycle)
    {
        CurrentCycle = Math.Clamp(cycle, 0, LastCycle);
    }

    /// <summary>Advances by the requested multiplier while respecting the trace boundary.</summary>
    public void FastForward(int multiplier)
    {
        StepForward(Math.Max(1, multiplier));
    }

    /// <summary>Advances only when the controller is currently playing.</summary>
    public void AdvanceIfPlaying(int cycles = 1) => TryAdvanceIfPlaying(cycles);

    /// <summary>Advances only when playing and reports whether the replay cursor changed.</summary>
    public bool TryAdvanceIfPlaying(int cycles = 1)
    {
        if (State != ReplayState.Playing)
        {
            return false;
        }

        var before = CurrentCycle;
        StepForward(cycles);
        if (CurrentCycle >= LastCycle)
        {
            State = ReplayState.Paused;
        }

        return CurrentCycle != before;
    }

    /// <summary>Sets the packet used to filter replay views, or clears the filter.</summary>
    public void SelectPacket(string? packetId)
    {
        SelectedPacketId = string.IsNullOrWhiteSpace(packetId) ? null : packetId;
        SetPacketFilter(SelectedPacketId is null ? [] : [SelectedPacketId]);
    }

    /// <summary>
    /// Selects the complete logical packet family containing the supplied physical packet id.
    /// Older traces without identity provenance fall back to exact packet selection.
    /// </summary>
    public void SelectLogicalPacket(string? packetId)
    {
        SelectedPacketId = string.IsNullOrWhiteSpace(packetId) ? null : packetId;
        packetFilters.Clear();
        if (SelectedPacketId is null)
        {
            return;
        }

        foreach (var id in LogicalPacketFamilyIds(SelectedPacketId))
        {
            packetFilters.Add(id);
        }
    }

    /// <summary>Sets the component used to filter replay views, or clears the filter.</summary>
    public void SelectComponent(string? componentId)
    {
        SelectedComponentId = string.IsNullOrWhiteSpace(componentId) ? null : componentId;
        SetComponentFilter(SelectedComponentId is null ? [] : [SelectedComponentId]);
    }

    /// <summary>Replaces the active packet filter set.</summary>
    public void SetPacketFilter(IEnumerable<string> packetIds)
    {
        packetFilters.Clear();
        foreach (var id in StableIds(packetIds))
        {
            packetFilters.Add(id);
        }

        SelectedPacketId = packetFilters.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Replaces the active component filter set.</summary>
    public void SetComponentFilter(IEnumerable<string> componentIds)
    {
        componentFilters.Clear();
        foreach (var id in StableIds(componentIds))
        {
            componentFilters.Add(id);
        }

        SelectedComponentId = componentFilters.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Clears packet and component filters while preserving event-type visibility settings.</summary>
    public void ClearObjectFilters()
    {
        packetFilters.Clear();
        componentFilters.Clear();
        SelectedPacketId = null;
        SelectedComponentId = null;
    }

    /// <summary>Updates event type enabled using the supplied value.</summary>
    public void SetEventTypeEnabled(TraceEventType eventType, bool enabled)
    {
        if (enabled)
        {
            enabledEventTypes.Add(eventType);
        }
        else
        {
            enabledEventTypes.Remove(eventType);
        }
    }

    /// <summary>Returns enabled events at the current cycle that match active packet and component filters.</summary>
    public IReadOnlyList<TraceEvent> CurrentEvents() => CurrentCycleDetails().Events;

    /// <summary>Returns current cycle replay details including filtered events and object ids.</summary>
    public ReplayCycleDetails CurrentCycleDetails()
    {
        var cycle = ReadCycle(CurrentCycle);
        var rawEvents = cycle?.Events ?? [];
        var filteredEvents = rawEvents.Where(EventPassesFilters).ToList();
        return new ReplayCycleDetails(
            CurrentCycle,
            LastCycle,
            State,
            cycle is not null,
            rawEvents.Count,
            filteredEvents,
            DistinctIds(filteredEvents.Select(traceEvent => traceEvent.PacketId)),
            DistinctIds(filteredEvents.Select(traceEvent => traceEvent.ComponentId)),
            DistinctIds(filteredEvents.Select(traceEvent => traceEvent.LinkId)),
            SourceKind,
            Manifest?.TraceLevel);
    }

    /// <summary>Returns every packet-move trace event associated with a packet in chronological order.</summary>
    public IReadOnlyList<TraceEvent> PacketPath(string packetId) => PacketPathWithCycles(packetId)
        .Select(step => step.Event)
        .ToList();

    /// <summary>Returns every packet-move trace event associated with a packet and its cycle coordinate.</summary>
    public IReadOnlyList<ReplayPacketPathStep> PacketPathWithCycles(string packetId)
    {
        if (string.IsNullOrWhiteSpace(packetId))
        {
            return [];
        }

        var path = new List<ReplayPacketPathStep>();
        foreach (var cycle in cycleOrder)
        {
            var record = ReadCycle(cycle);
            if (record is null)
            {
                continue;
            }

            path.AddRange(record.Events
                .Where(traceEvent => traceEvent.Type == TraceEventType.PacketMove && string.Equals(traceEvent.PacketId, packetId, StringComparison.OrdinalIgnoreCase))
                .Select(traceEvent => new ReplayPacketPathStep(record.Cycle, traceEvent)));
        }

        return path;
    }

    /// <summary>
    /// Returns the chronological packet-move path for every physical packet in one logical
    /// transformation family while preserving the exact-path compatibility APIs above.
    /// </summary>
    public IReadOnlyList<ReplayPacketPathStep> LogicalPacketPathWithCycles(string packetId)
    {
        if (string.IsNullOrWhiteSpace(packetId))
        {
            return [];
        }

        var familyIds = LogicalPacketFamilyIds(packetId);
        var path = new List<ReplayPacketPathStep>();
        foreach (var cycle in cycleOrder)
        {
            var record = ReadCycle(cycle);
            if (record is null)
            {
                continue;
            }

            path.AddRange(record.Events
                .Where(traceEvent => traceEvent.Type == TraceEventType.PacketMove &&
                    traceEvent.PacketId is not null &&
                    familyIds.Contains(traceEvent.PacketId))
                .Select(traceEvent => new ReplayPacketPathStep(record.Cycle, traceEvent)));
        }

        return path;
    }

    /// <summary>Releases a persisted trace reader owned by this replay controller.</summary>
    public void Dispose() => store?.Dispose();

    private CycleTraceRecord? ReadCycle(long cycle)
    {
        if (store is not null)
        {
            return store.ReadCycle(cycle);
        }

        return cyclesByCycle.TryGetValue(cycle, out var record) ? record : null;
    }

    private bool EventPassesFilters(TraceEvent traceEvent)
    {
        if (!enabledEventTypes.Contains(traceEvent.Type))
        {
            return false;
        }

        if (packetFilters.Count > 0 && (traceEvent.PacketId is null || !packetFilters.Contains(traceEvent.PacketId)))
        {
            return false;
        }

        if (componentFilters.Count > 0 && (traceEvent.ComponentId is null || !componentFilters.Contains(traceEvent.ComponentId)))
        {
            return false;
        }

        return true;
    }

    private HashSet<string> LogicalPacketFamilyIds(string packetId)
    {
        EnsureLogicalIdentityIndex();
        var logicalId = logicalIdsByPhysical!.GetValueOrDefault(packetId, packetId);
        var familyIds = physicalIdsByLogical!.TryGetValue(logicalId, out var indexedFamily)
            ? new HashSet<string>(indexedFamily, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        familyIds.Add(logicalId);
        familyIds.Add(packetId);
        return familyIds;
    }

    private void EnsureLogicalIdentityIndex()
    {
        if (logicalIdsByPhysical is not null && physicalIdsByLogical is not null)
        {
            return;
        }

        var observedPacketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitLogicalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var traceEvent in ReadAllEvents())
        {
            if (string.IsNullOrWhiteSpace(traceEvent.PacketId)) continue;
            observedPacketIds.Add(traceEvent.PacketId);
            var eventLogicalId = PacketTraceIdentity.DetailValue(
                traceEvent.Detail,
                PacketTraceIdentity.LogicalPacketIdKey);
            if (!string.IsNullOrWhiteSpace(eventLogicalId))
            {
                explicitLogicalIds[traceEvent.PacketId] = eventLogicalId;
            }
        }

        logicalIdsByPhysical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        physicalIdsByLogical = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var physicalId in observedPacketIds)
        {
            var logicalId = explicitLogicalIds.GetValueOrDefault(physicalId, physicalId);
            logicalIdsByPhysical[physicalId] = logicalId;
            if (!physicalIdsByLogical.TryGetValue(logicalId, out var family))
            {
                physicalIdsByLogical[logicalId] = family = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            family.Add(physicalId);
            family.Add(logicalId);
        }
    }

    private IEnumerable<TraceEvent> ReadAllEvents()
    {
        foreach (var cycle in cycleOrder)
        {
            var record = ReadCycle(cycle);
            if (record is null)
            {
                continue;
            }

            foreach (var traceEvent in record.Events)
            {
                yield return traceEvent;
            }
        }
    }

    private void EnableAllEventTypes()
    {
        foreach (TraceEventType eventType in Enum.GetValues(typeof(TraceEventType)))
        {
            enabledEventTypes.Add(eventType);
        }
    }

    private static IReadOnlyList<string> StableIds(IEnumerable<string> ids) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static IReadOnlyList<string> DistinctIds(IEnumerable<string?> ids) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
