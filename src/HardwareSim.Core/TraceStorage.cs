using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

/// <summary>Defines how much trace detail is persisted for replay and analysis.</summary>
public enum TraceLevel
{
    /// <summary>Persists only metrics, bottleneck report, config, and hashes; no cycle trace is written.</summary>
    SummaryOnly,
    /// <summary>Persists key events needed for timeline inspection without full per-cycle phase detail.</summary>
    EventTrace,
    /// <summary>Persists every cycle, phase audit, and event needed to reconstruct replay state.</summary>
    FullCycleTrace,
    /// <summary>Persists only events associated with explicitly selected packets, components, or links.</summary>
    DebugSelected
}

/// <summary>Describes the explicit objects included by a debug-selected trace.</summary>
public sealed class TraceDebugSelection
{
    /// <summary>Gets or sets the selected component identifiers.</summary>
    public List<string> ComponentIds { get; set; } = [];
    /// <summary>Gets or sets the selected packet identifiers.</summary>
    public List<string> PacketIds { get; set; } = [];
    /// <summary>Gets or sets the selected link identifiers.</summary>
    public List<string> LinkIds { get; set; } = [];
    /// <summary>Gets whether at least one explicit object is selected.</summary>
    [JsonIgnore]
    public bool HasExplicitSelection => ComponentIds.Count > 0 || PacketIds.Count > 0 || LinkIds.Count > 0;
}

/// <summary>Configures persisted trace storage generation.</summary>
public sealed class TraceStorageOptions
{
    /// <summary>Gets or sets the persisted trace detail level.</summary>
    public TraceLevel TraceLevel { get; set; } = TraceLevel.FullCycleTrace;
    /// <summary>Gets or sets the deterministic seed recorded with the persisted trace hash.</summary>
    public int Seed { get; set; }
    /// <summary>Gets or sets stable configuration values recorded with the persisted trace hash.</summary>
    public Dictionary<string, string> Config { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets optional event type filters for event and debug trace projections.</summary>
    public List<TraceEventType> EventTypes { get; set; } = [];
    /// <summary>Gets or sets explicit debug object selection.</summary>
    public TraceDebugSelection DebugSelection { get; set; } = new();
}

/// <summary>Represents one byte-offset entry in a persisted cycle index.</summary>
public sealed class TraceCycleIndexEntry
{
    /// <summary>Gets or sets the trace cycle number.</summary>
    public long Cycle { get; set; }
    /// <summary>Gets or sets the UTF-8 byte offset of the cycle JSON record in the cycles file.</summary>
    public long OffsetBytes { get; set; }
    /// <summary>Gets or sets the UTF-8 byte length of the cycle JSON record, excluding the newline separator.</summary>
    public int LengthBytes { get; set; }
    /// <summary>Gets or sets the number of events stored for this cycle.</summary>
    public int EventCount { get; set; }
    /// <summary>Gets or sets the number of phase audit entries stored for this cycle.</summary>
    public int PhaseCount { get; set; }
}

/// <summary>Describes a persisted trace store and its stable replay contract.</summary>
public sealed class PersistedTraceManifest
{
    /// <summary>Gets or sets the persisted trace store schema version.</summary>
    public string SchemaVersion { get; set; } = PersistedTraceStore.SchemaVersion;
    /// <summary>Gets or sets the underlying simulation trace schema version.</summary>
    public string TraceSchemaVersion { get; set; } = CanonicalTraceHasher.TraceSchemaVersion;
    /// <summary>Gets or sets the persisted trace detail level.</summary>
    public TraceLevel TraceLevel { get; set; }
    /// <summary>Gets or sets the random access strategy used by this store.</summary>
    public string AccessPattern { get; set; } = PersistedTraceStore.CycleOffsetAccessPattern;
    /// <summary>Gets or sets the relative cycle JSONL file path, or null for summary-only stores.</summary>
    public string? CyclesFile { get; set; }
    /// <summary>Gets or sets the relative cycle index file path, or null for summary-only stores.</summary>
    public string? CycleIndexFile { get; set; }
    /// <summary>Gets or sets the number of source cycle records before projection.</summary>
    public int SourceCycleCount { get; set; }
    /// <summary>Gets or sets the number of source trace events before projection.</summary>
    public int SourceEventCount { get; set; }
    /// <summary>Gets or sets the total cycles reported by metrics.</summary>
    public long SourceTotalCycles { get; set; }
    /// <summary>Gets or sets the number of stored cycle records after projection.</summary>
    public int StoredCycleCount { get; set; }
    /// <summary>Gets or sets the number of stored trace events after projection.</summary>
    public int StoredEventCount { get; set; }
    /// <summary>Gets or sets the first stored cycle, if any cycle trace was written.</summary>
    public long? FirstCycle { get; set; }
    /// <summary>Gets or sets the last stored cycle, if any cycle trace was written.</summary>
    public long? LastCycle { get; set; }
    /// <summary>Gets or sets the deterministic seed recorded in the canonical persisted hash.</summary>
    public int Seed { get; set; }
    /// <summary>Gets or sets stable configuration values recorded in the canonical persisted hash.</summary>
    public SortedDictionary<string, string> Config { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the explicit debug selection recorded with this store.</summary>
    public TraceDebugSelection DebugSelection { get; set; } = new();
    /// <summary>Gets or sets stored event counts by type.</summary>
    public Dictionary<TraceEventType, int> EventCountsByType { get; set; } = new();
    /// <summary>Gets or sets the metrics persisted with this trace store.</summary>
    public SimulationMetrics Metrics { get; set; } = new();
    /// <summary>Gets or sets the bottleneck report persisted with this trace store.</summary>
    public BottleneckReport BottleneckReport { get; set; } = new();
    /// <summary>Gets or sets the canonical persisted trace hash algorithm.</summary>
    public string CanonicalHashAlgorithm { get; set; } = PersistedTraceStore.HashAlgorithm;
    /// <summary>Gets or sets the canonical persisted trace hash.</summary>
    public string CanonicalHash { get; set; } = "";
}

/// <summary>Reads a persisted trace store by cycle offset without loading all cycle records.</summary>
public sealed class PersistedTraceStoreReader : IDisposable
{
    private readonly string? cyclesPath;
    private readonly FileStream? stream;
    private readonly IReadOnlyDictionary<long, TraceCycleIndexEntry> indexByCycle;

    internal PersistedTraceStoreReader(string directory, PersistedTraceManifest manifest, IReadOnlyList<TraceCycleIndexEntry> index)
    {
        Manifest = manifest;
        Index = index;
        indexByCycle = index.ToDictionary(entry => entry.Cycle);
        if (!string.IsNullOrWhiteSpace(manifest.CyclesFile))
        {
            cyclesPath = Path.Combine(directory, manifest.CyclesFile!);
            stream = new FileStream(cyclesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    /// <summary>Gets the persisted trace manifest.</summary>
    public PersistedTraceManifest Manifest { get; }
    /// <summary>Gets the cycle byte-offset index entries loaded by the reader.</summary>
    public IReadOnlyList<TraceCycleIndexEntry> Index { get; }
    /// <summary>Gets whether this reader uses a byte-offset cycle index.</summary>
    public bool UsesCycleOffsetIndex => Manifest.AccessPattern == PersistedTraceStore.CycleOffsetAccessPattern && Index.Count > 0;
    /// <summary>Gets the number of resident cycle records loaded by the reader.</summary>
    public int ResidentCycleRecordCount => 0;

    /// <summary>Reads one cycle by seeking directly to its indexed byte offset.</summary>
    public CycleTraceRecord? ReadCycle(long cycle)
    {
        if (stream is null || !indexByCycle.TryGetValue(cycle, out var entry))
        {
            return null;
        }

        var buffer = new byte[entry.LengthBytes];
        stream.Seek(entry.OffsetBytes, SeekOrigin.Begin);
        ReadExact(stream, buffer);
        return JsonSerializer.Deserialize<CycleTraceRecord>(buffer, HardwareGraphJson.Options);
    }

    /// <summary>Reads the filtered events for one indexed cycle.</summary>
    public IReadOnlyList<TraceEvent> ReadCycleEvents(long cycle) => ReadCycle(cycle)?.Events ?? [];

    /// <summary>Releases the underlying cycle file stream.</summary>
    public void Dispose() => stream?.Dispose();

    private static void ReadExact(Stream source, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = source.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of persisted trace cycle file.");
            }

            offset += read;
        }
    }
}

/// <summary>Writes and opens stable persisted trace stores for replay and analysis.</summary>
public static class PersistedTraceStore
{
    /// <summary>Defines the persisted trace storage schema version.</summary>
    public const string SchemaVersion = "trace-store-1.0";
    /// <summary>Defines the canonical persisted trace hash algorithm.</summary>
    public const string HashAlgorithm = "SHA-256";
    /// <summary>Defines the manifest file name.</summary>
    public const string ManifestFileName = "trace-manifest.json";
    /// <summary>Defines the cycle JSONL file name.</summary>
    public const string CyclesFileName = "cycles.jsonl";
    /// <summary>Defines the cycle index file name.</summary>
    public const string CycleIndexFileName = "cycle-index.json";
    /// <summary>Defines the manifest access pattern for cycle-indexed trace files.</summary>
    public const string CycleOffsetAccessPattern = "cycle-byte-offset-index";
    /// <summary>Defines the manifest access pattern for summary-only stores.</summary>
    public const string SummaryOnlyAccessPattern = "summary-only";

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly HashSet<TraceEventType> EventTraceTypes =
    [
        TraceEventType.PacketInjection,
        TraceEventType.PacketMove,
        TraceEventType.Stall,
        TraceEventType.Arbitration,
        TraceEventType.Compute,
        TraceEventType.OperationStart,
        TraceEventType.OperationComplete,
        TraceEventType.LinkTransfer,
        TraceEventType.FlitIssue,
        TraceEventType.FlitArrival,
        TraceEventType.MemoryRequest,
        TraceEventType.Warning,
        TraceEventType.Error
    ];

    /// <summary>Writes a persisted trace store into the supplied directory.</summary>
    public static PersistedTraceManifest Write(SimulationResult result, string directory, TraceStorageOptions? options = null)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Trace store directory is required.", nameof(directory));
        }

        options ??= new TraceStorageOptions();
        Directory.CreateDirectory(directory);
        RemoveKnownStoreFiles(directory);

        var projection = ProjectTrace(result.Trace, options);
        var storedEvents = projection.Cycles.SelectMany(cycle => cycle.Events).ToList();
        var manifest = new PersistedTraceManifest
        {
            TraceLevel = options.TraceLevel,
            AccessPattern = options.TraceLevel == TraceLevel.SummaryOnly ? SummaryOnlyAccessPattern : CycleOffsetAccessPattern,
            SourceCycleCount = result.Trace.Cycles.Count,
            SourceEventCount = result.Trace.Cycles.Sum(cycle => cycle.Events.Count),
            SourceTotalCycles = result.Metrics.Global.TotalCycles,
            StoredCycleCount = projection.Cycles.Count,
            StoredEventCount = storedEvents.Count,
            FirstCycle = projection.Cycles.Count == 0 ? null : projection.Cycles.Min(cycle => cycle.Cycle),
            LastCycle = projection.Cycles.Count == 0 ? null : projection.Cycles.Max(cycle => cycle.Cycle),
            Seed = options.Seed,
            Config = StableConfig(options.Config),
            DebugSelection = StableSelection(options.DebugSelection),
            Metrics = result.Metrics,
            BottleneckReport = result.BottleneckReport
        };

        foreach (var group in storedEvents.GroupBy(item => item.Type).OrderBy(group => group.Key.ToString(), StringComparer.Ordinal))
        {
            manifest.EventCountsByType[group.Key] = group.Count();
        }

        if (options.TraceLevel != TraceLevel.SummaryOnly)
        {
            var index = WriteCycleRecords(directory, projection);
            manifest.CyclesFile = CyclesFileName;
            manifest.CycleIndexFile = CycleIndexFileName;
            WriteJson(Path.Combine(directory, CycleIndexFileName), index);
        }

        manifest.CanonicalHash = ComputeCanonicalHash(manifest, projection);
        WriteJson(Path.Combine(directory, ManifestFileName), manifest);
        return manifest;
    }

    /// <summary>Opens a persisted trace store for indexed cycle reads.</summary>
    public static PersistedTraceStoreReader Open(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Trace store directory is required.", nameof(directory));
        }

        var manifestPath = Path.Combine(directory, ManifestFileName);
        var manifest = JsonSerializer.Deserialize<PersistedTraceManifest>(File.ReadAllText(manifestPath), HardwareGraphJson.Options)
            ?? throw new InvalidOperationException("Persisted trace manifest could not be deserialized.");
        var index = new List<TraceCycleIndexEntry>();
        if (!string.IsNullOrWhiteSpace(manifest.CycleIndexFile))
        {
            var indexPath = Path.Combine(directory, manifest.CycleIndexFile!);
            index = JsonSerializer.Deserialize<List<TraceCycleIndexEntry>>(File.ReadAllText(indexPath), HardwareGraphJson.Options) ?? [];
        }

        return new PersistedTraceStoreReader(directory, manifest, index);
    }

    /// <summary>Projects an in-memory trace to the requested persisted trace level.</summary>
    public static SimulationTrace ProjectTrace(SimulationTrace trace, TraceStorageOptions options)
    {
        if (trace is null)
        {
            throw new ArgumentNullException(nameof(trace));
        }

        if (options.TraceLevel == TraceLevel.SummaryOnly)
        {
            return new SimulationTrace();
        }

        var projected = new SimulationTrace();
        var selectedComponents = StableSet(options.DebugSelection.ComponentIds);
        var selectedPackets = StableSet(options.DebugSelection.PacketIds);
        var selectedLinks = StableSet(options.DebugSelection.LinkIds);
        var enabledTypes = options.EventTypes.Count == 0
            ? null
            : options.EventTypes.ToHashSet();

        foreach (var cycle in trace.Cycles.OrderBy(cycle => cycle.Cycle))
        {
            var events = cycle.Events
                .Where(traceEvent => EventPassesLevel(traceEvent, options.TraceLevel, enabledTypes, selectedComponents, selectedPackets, selectedLinks))
                .Select(CloneEvent)
                .ToList();
            if (options.TraceLevel == TraceLevel.DebugSelected && events.Count == 0)
            {
                continue;
            }

            projected.Cycles.Add(new CycleTraceRecord
            {
                Cycle = cycle.Cycle,
                Phases = options.TraceLevel == TraceLevel.FullCycleTrace
                    ? cycle.Phases.Select(phase => new CyclePhaseTrace(phase.Index, phase.Name)).ToList()
                    : [],
                Events = events
            });
        }

        return projected;
    }

    private static bool EventPassesLevel(
        TraceEvent traceEvent,
        TraceLevel level,
        HashSet<TraceEventType>? enabledTypes,
        HashSet<string> selectedComponents,
        HashSet<string> selectedPackets,
        HashSet<string> selectedLinks)
    {
        if (level == TraceLevel.FullCycleTrace)
        {
            return true;
        }

        if (enabledTypes is not null && !enabledTypes.Contains(traceEvent.Type))
        {
            return false;
        }

        if (level == TraceLevel.EventTrace)
        {
            return EventTraceTypes.Contains(traceEvent.Type);
        }

        if (level == TraceLevel.DebugSelected)
        {
            return (traceEvent.ComponentId is not null && selectedComponents.Contains(traceEvent.ComponentId)) ||
                   (traceEvent.PacketId is not null && selectedPackets.Contains(traceEvent.PacketId)) ||
                   (traceEvent.LinkId is not null && selectedLinks.Contains(traceEvent.LinkId));
        }

        return false;
    }

    private static List<TraceCycleIndexEntry> WriteCycleRecords(string directory, SimulationTrace trace)
    {
        var index = new List<TraceCycleIndexEntry>();
        var path = Path.Combine(directory, CyclesFileName);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        long offset = 0;
        foreach (var cycle in trace.Cycles.OrderBy(cycle => cycle.Cycle))
        {
            var json = JsonSerializer.Serialize(cycle, CompactJsonOptions);
            var recordBytes = Utf8NoBom.GetBytes(json);
            var newlineBytes = Utf8NoBom.GetBytes("\n");
            stream.Write(recordBytes, 0, recordBytes.Length);
            stream.Write(newlineBytes, 0, newlineBytes.Length);
            index.Add(new TraceCycleIndexEntry
            {
                Cycle = cycle.Cycle,
                OffsetBytes = offset,
                LengthBytes = recordBytes.Length,
                EventCount = cycle.Events.Count,
                PhaseCount = cycle.Phases.Count
            });
            offset += recordBytes.Length + newlineBytes.Length;
        }

        return index;
    }

    private static void RemoveKnownStoreFiles(string directory)
    {
        foreach (var file in new[] { ManifestFileName, CyclesFileName, CycleIndexFileName })
        {
            var path = Path.Combine(directory, file);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, HardwareGraphJson.Options);
        File.WriteAllText(path, json, Utf8NoBom);
    }

    private static TraceEvent CloneEvent(TraceEvent traceEvent) => traceEvent with { Provenance = traceEvent.Provenance };

    private static SortedDictionary<string, string> StableConfig(IReadOnlyDictionary<string, string> config)
    {
        var stable = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in config.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            stable[pair.Key] = pair.Value ?? "";
        }

        return stable;
    }

    private static TraceDebugSelection StableSelection(TraceDebugSelection selection) => new()
    {
        ComponentIds = StableList(selection.ComponentIds),
        PacketIds = StableList(selection.PacketIds),
        LinkIds = StableList(selection.LinkIds)
    };

    private static List<string> StableList(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static HashSet<string> StableSet(IEnumerable<string> values) => new(StableList(values), StringComparer.OrdinalIgnoreCase);

    private static string ComputeCanonicalHash(PersistedTraceManifest manifest, SimulationTrace projection)
    {
        var hashDocument = new PersistedTraceHashDocument
        {
            SchemaVersion = manifest.SchemaVersion,
            TraceSchemaVersion = manifest.TraceSchemaVersion,
            TraceLevel = manifest.TraceLevel,
            AccessPattern = manifest.AccessPattern,
            SourceCycleCount = manifest.SourceCycleCount,
            SourceEventCount = manifest.SourceEventCount,
            SourceTotalCycles = manifest.SourceTotalCycles,
            StoredCycleCount = manifest.StoredCycleCount,
            StoredEventCount = manifest.StoredEventCount,
            FirstCycle = manifest.FirstCycle,
            LastCycle = manifest.LastCycle,
            Seed = manifest.Seed,
            Config = manifest.Config,
            DebugSelection = manifest.DebugSelection,
            EventCountsByType = manifest.EventCountsByType
                .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            Metrics = manifest.Metrics,
            BottleneckReport = manifest.BottleneckReport,
            Cycles = manifest.TraceLevel == TraceLevel.SummaryOnly ? null : projection.Cycles
        };
        var canonical = JsonSerializer.Serialize(hashDocument, CompactJsonOptions);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Utf8NoBom.GetBytes(canonical));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed class PersistedTraceHashDocument
    {
        public string SchemaVersion { get; set; } = "";
        public string TraceSchemaVersion { get; set; } = "";
        public TraceLevel TraceLevel { get; set; }
        public string AccessPattern { get; set; } = "";
        public int SourceCycleCount { get; set; }
        public int SourceEventCount { get; set; }
        public long SourceTotalCycles { get; set; }
        public int StoredCycleCount { get; set; }
        public int StoredEventCount { get; set; }
        public long? FirstCycle { get; set; }
        public long? LastCycle { get; set; }
        public int Seed { get; set; }
        public SortedDictionary<string, string> Config { get; set; } = new(StringComparer.Ordinal);
        public TraceDebugSelection DebugSelection { get; set; } = new();
        public Dictionary<TraceEventType, int> EventCountsByType { get; set; } = new();
        public SimulationMetrics Metrics { get; set; } = new();
        public BottleneckReport BottleneckReport { get; set; } = new();
        public List<CycleTraceRecord>? Cycles { get; set; }
    }
}
