using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

/// <summary>Defines an immutable compiled component consumed by simulation engines.</summary>
public sealed class SimComponentDef
{
    /// <summary>Gets or sets the id value carried by the enclosing sim component def contract.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the name value carried by the enclosing sim component def contract.</summary>
    public string Name { get; init; } = "";
    /// <summary>Gets or sets the type value carried by the enclosing sim component def contract.</summary>
    public ComponentKind Type { get; init; } = ComponentKind.Custom;
    /// <summary>Gets or sets the stable plugin type id when the compiled component is not identified by a legacy built-in kind.</summary>
    [JsonPropertyName("type_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string TypeId { get; init; } = "";
    /// <summary>Gets or sets the position value carried by the enclosing sim component def contract.</summary>
    public GridPosition Position { get; init; } = new(0, 0);
    /// <summary>Gets or sets the port ids collection carried by the enclosing sim component def contract.</summary>
    public IReadOnlyList<string> PortIds { get; init; } = [];
    /// <summary>Gets or sets the parameters collection carried by the enclosing sim component def contract.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    /// <summary>Gets or sets the model ref value carried by the enclosing sim component def contract.</summary>
    [JsonPropertyName("model_ref")]
    public string? ModelRef { get; init; }
    /// <summary>Gets or sets the latency model id value carried by the enclosing sim component def contract.</summary>
    public string? LatencyModelId { get; init; }
    /// <summary>Gets or sets the energy model id value carried by the enclosing sim component def contract.</summary>
    public string? EnergyModelId { get; init; }
    /// <summary>Gets or sets the area model id value carried by the enclosing sim component def contract.</summary>
    public string? AreaModelId { get; init; }
    /// <summary>Gets or sets the exact compiled component execution contract when this component uses a registered kernel.</summary>
    [JsonPropertyName("execution_contract")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompiledComponentExecutionContract? ExecutionContract { get; init; }

    /// <summary>Returns int parameter from the current contract.</summary>
    public int GetIntParameter(string name, int defaultValue) =>
        Parameters.TryGetValue(name, out var raw) && int.TryParse(raw, out var value) ? value : defaultValue;

    /// <summary>Returns double parameter from the current contract.</summary>
    public double GetDoubleParameter(string name, double defaultValue) =>
        Parameters.TryGetValue(name, out var raw) &&
        double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
}

/// <summary>Defines an immutable compiled port with canonical ownership and transport attributes.</summary>
public sealed class SimPortDef
{
    /// <summary>Gets or sets the id value carried by the enclosing sim port def contract.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the component id value carried by the enclosing sim port def contract.</summary>
    public string ComponentId { get; init; } = "";
    /// <summary>Gets or sets the name value carried by the enclosing sim port def contract.</summary>
    public string Name { get; init; } = "";
    /// <summary>Gets or sets the direction value carried by the enclosing sim port def contract.</summary>
    public PortDirection Direction { get; init; }
    /// <summary>Gets or sets the signal type value carried by the enclosing sim port def contract.</summary>
    public SignalType SignalType { get; init; } = SignalType.Digital;
    /// <summary>Gets or sets the data type value carried by the enclosing sim port def contract.</summary>
    public HardwareDataType DataType { get; init; } = HardwareDataType.Packet;
    /// <summary>Gets or sets the precision value carried by the enclosing sim port def contract.</summary>
    public PrecisionKind Precision { get; init; } = PrecisionKind.Any;
    /// <summary>Gets or sets the protocol value carried by the enclosing sim port def contract.</summary>
    public PortProtocol Protocol { get; init; } = PortProtocol.Packet;
    /// <summary>Gets or sets the bandwidth bits per cycle value carried by the enclosing sim port def contract.</summary>
    public int BandwidthBitsPerCycle { get; init; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    /// <summary>Gets or sets the latency cycles value carried by the enclosing sim port def contract.</summary>
    public int LatencyCycles { get; init; }
    /// <summary>Gets or sets the clock domain value carried by the enclosing sim port def contract.</summary>
    public string ClockDomain { get; init; } = "default";
    /// <summary>Gets or sets whether the compiled port must have a connection.</summary>
    public bool Required { get; init; }
    /// <summary>Gets or sets whether the compiled port accepts multiple links.</summary>
    public bool MultiConnect { get; init; }
}

/// <summary>Defines an immutable compiled link between canonical simulation ports.</summary>
public sealed class SimLinkDef
{
    /// <summary>Gets or sets the id value carried by the enclosing sim link def contract.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the source port id value carried by the enclosing sim link def contract.</summary>
    public string SourcePortId { get; init; } = "";
    /// <summary>Gets or sets the destination port id value carried by the enclosing sim link def contract.</summary>
    public string DestinationPortId { get; init; } = "";
    /// <summary>Gets or sets the source value carried by the enclosing sim link def contract.</summary>
    public PortRef Source { get; init; } = new("", "");
    /// <summary>Gets or sets the destination value carried by the enclosing sim link def contract.</summary>
    public PortRef Destination { get; init; } = new("", "");
    /// <summary>Gets or sets the model ref value carried by the enclosing sim link def contract.</summary>
    [JsonPropertyName("model_ref")]
    public string? ModelRef { get; init; }
    /// <summary>Gets or sets the bandwidth bits per cycle value carried by the enclosing sim link def contract.</summary>
    public int BandwidthBitsPerCycle { get; init; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    /// <summary>Gets or sets the latency cycles value carried by the enclosing sim link def contract.</summary>
    public int LatencyCycles { get; init; } = ComponentDefaults.LinkBaseLatencyCycles;
    /// <summary>Gets or sets the energy per bit pj value carried by the enclosing sim link def contract.</summary>
    public double EnergyPerBitPJ { get; init; } = ComponentDefaults.LinkEnergyPerBitPJ;
    /// <summary>Gets or sets the physical length um value carried by the enclosing sim link def contract.</summary>
    public double PhysicalLengthUm { get; init; }
    /// <summary>Gets or sets the route type value carried by the enclosing sim link def contract.</summary>
    public string RouteType { get; init; } = "logical";
    /// <summary>Gets or sets the optional immutable Phase 8 route-derived optical geometry snapshot.</summary>
    [JsonPropertyName("optical_route")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpticalRouteSnapshot? OpticalRoute { get; init; }
    /// <summary>Gets or sets the optional typed Phase 8 optical loss/runtime profile.</summary>
    [JsonPropertyName("optical_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpticalLinkRuntimeProfile? OpticalProfile { get; init; }
    /// <summary>Gets or sets the parameters collection carried by the enclosing sim link def contract.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>Gets the energy per bit value carried by the enclosing sim link def contract.</summary>
    [JsonIgnore]
    public double EnergyPerBit => EnergyPerBitPJ;
}

/// <summary>Represents routing table data exchanged by hardware design and simulation workflows.</summary>
public sealed class RoutingTable
{
    /// <summary>Gets or sets the router id value carried by the enclosing routing table contract.</summary>
    public string RouterId { get; init; } = "";
    /// <summary>Gets or sets the destination to port id collection carried by the enclosing routing table contract.</summary>
    public IReadOnlyDictionary<string, string> DestinationToPortId { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

/// <summary>Represents energy model data exchanged by hardware design and simulation workflows.</summary>
public sealed class EnergyModel
{
    /// <summary>Gets or sets the id value carried by the enclosing energy model contract.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the energy per operation pj value carried by the enclosing energy model contract.</summary>
    public double EnergyPerOperationPJ { get; init; }
    /// <summary>Gets or sets the energy per bit pj value carried by the enclosing energy model contract.</summary>
    public double EnergyPerBitPJ { get; init; }
    /// <summary>Gets or sets the source value carried by the enclosing energy model contract.</summary>
    public string Source { get; init; } = "hardware_graph";
}

/// <summary>Represents latency model data exchanged by hardware design and simulation workflows.</summary>
public sealed class LatencyModel
{
    /// <summary>Gets or sets the id value carried by the enclosing latency model contract.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the latency cycles value carried by the enclosing latency model contract.</summary>
    public int LatencyCycles { get; init; }
    /// <summary>Gets or sets the source value carried by the enclosing latency model contract.</summary>
    public string Source { get; init; } = "hardware_graph";
}

/// <summary>Represents area model data exchanged by hardware design and simulation workflows.</summary>
public sealed class AreaModel
{
    /// <summary>Gets or sets the id value carried by the enclosing area model contract.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the area um2 value carried by the enclosing area model contract.</summary>
    public double AreaUm2 { get; init; }
    /// <summary>Gets or sets the source value carried by the enclosing area model contract.</summary>
    public string Source { get; init; } = "hardware_graph";
}

/// <summary>Represents simulation config data exchanged by hardware design and simulation workflows.</summary>
public sealed class SimulationConfig
{
    /// <summary>Gets or sets the clock value carried by the enclosing simulation config contract.</summary>
    public ClockConfig Clock { get; init; } = new();
    /// <summary>Gets or sets the max cycles value carried by the enclosing simulation config contract.</summary>
    public int MaxCycles { get; init; } = 10_000;
    /// <summary>Gets or sets the global flit size in bits.</summary>
    public int FlitSizeBits { get; init; } = ComponentDefaults.GlobalFlitSizeBits;
    /// <summary>Gets or sets the frozen transport semantics contract used by this simulation graph.</summary>
    public TransportSemanticsSnapshot TransportSemantics { get; init; } = TransportSemanticsContract.CreateDefault();
}

/// <summary>Represents trace config data exchanged by hardware design and simulation workflows.</summary>
public sealed class TraceConfig
{
    /// <summary>Gets or sets whether the simulation records trace events.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets or sets the level value carried by the enclosing trace config contract.</summary>
    public string Level { get; init; } = "events";
}

/// <summary>Represents simulation graph provenance data exchanged by hardware design and simulation workflows.</summary>
public sealed class SimulationGraphProvenance
{
    /// <summary>Gets or sets the source schema version value carried by the enclosing simulation graph provenance contract.</summary>
    public string SourceSchemaVersion { get; init; } = HardwareGraph.CurrentSchemaVersion;
    /// <summary>Gets or sets the source graph hash value carried by the enclosing simulation graph provenance contract.</summary>
    public string SourceGraphHash { get; init; } = "";
    /// <summary>Gets or sets the compiler version value carried by the enclosing simulation graph provenance contract.</summary>
    public string CompilerVersion { get; init; } = "1.0";
    /// <summary>Gets or sets the frozen component runtime kernel registry snapshot hash.</summary>
    public string ComponentRuntimeKernelRegistryHash { get; init; } = "";
}

/// <summary>Provides the immutable, hardware-only input accepted by simulation engines.</summary>
public sealed class HardwareSimulationGraph
{
    /// <summary>Defines the canonical current schema version value used by the enclosing hardware simulation graph contract.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Gets or sets the schema version value carried by the enclosing hardware simulation graph contract.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the components collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyList<SimComponentDef> Components { get; init; } = [];
    /// <summary>Gets or sets the ports collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyList<SimPortDef> Ports { get; init; } = [];
    /// <summary>Gets or sets the links collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyList<SimLinkDef> Links { get; init; } = [];
    /// <summary>Gets or sets the routing tables collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyDictionary<string, RoutingTable> RoutingTables { get; init; } =
        new ReadOnlyDictionary<string, RoutingTable>(new Dictionary<string, RoutingTable>());
    /// <summary>Gets or sets the energy models collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyDictionary<string, EnergyModel> EnergyModels { get; init; } =
        new ReadOnlyDictionary<string, EnergyModel>(new Dictionary<string, EnergyModel>());
    /// <summary>Gets or sets the latency models collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyDictionary<string, LatencyModel> LatencyModels { get; init; } =
        new ReadOnlyDictionary<string, LatencyModel>(new Dictionary<string, LatencyModel>());
    /// <summary>Gets or sets the area models collection carried by the enclosing hardware simulation graph contract.</summary>
    public IReadOnlyDictionary<string, AreaModel> AreaModels { get; init; } =
        new ReadOnlyDictionary<string, AreaModel>(new Dictionary<string, AreaModel>());
    /// <summary>Gets or sets Phase 7 physical model binding snapshots by deterministic id.</summary>
    public IReadOnlyDictionary<string, ModelBindingSnapshot> ModelBindingSnapshots { get; init; } =
        new ReadOnlyDictionary<string, ModelBindingSnapshot>(new Dictionary<string, ModelBindingSnapshot>());
    /// <summary>Gets or sets Phase 7 characterized profile snapshots by deterministic id.</summary>
    public IReadOnlyDictionary<string, CharacterizedProfileSnapshot> CharacterizedProfiles { get; init; } =
        new ReadOnlyDictionary<string, CharacterizedProfileSnapshot>(new Dictionary<string, CharacterizedProfileSnapshot>());
    /// <summary>Gets or sets compiled Phase 7C component profiles by top-level component id.</summary>
    public IReadOnlyDictionary<string, CompiledComponentProfile> CompiledComponentProfiles { get; init; } =
        new ReadOnlyDictionary<string, CompiledComponentProfile>(new Dictionary<string, CompiledComponentProfile>());
    /// <summary>Gets or sets the simulation config value carried by the enclosing hardware simulation graph contract.</summary>
    public SimulationConfig SimulationConfig { get; init; } = new();
    /// <summary>Gets or sets the trace config value carried by the enclosing hardware simulation graph contract.</summary>
    public TraceConfig TraceConfig { get; init; } = new();
    /// <summary>Gets or sets the provenance value carried by the enclosing hardware simulation graph contract.</summary>
    public SimulationGraphProvenance Provenance { get; init; } = new();

    /// <summary>Finds a compiled component by case-insensitive identifier.</summary>
    public SimComponentDef? FindComponent(string componentId) =>
        Components.FirstOrDefault(component => string.Equals(component.Id, componentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a compiled port by canonical identifier.</summary>
    public SimPortDef? FindPort(string portId) =>
        Ports.FirstOrDefault(port => string.Equals(port.Id, portId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Records workload and mapping provenance for an executable simulation graph.</summary>
public sealed class WorkloadMappingProvenance
{
    /// <summary>Gets or sets the workload schema version.</summary>
    public string WorkloadSchemaVersion { get; init; } = WorkloadGraph.CurrentSchemaVersion;
    /// <summary>Gets or sets the workload graph hash.</summary>
    public string WorkloadHash { get; init; } = "";
    /// <summary>Gets or sets the mapping schema version.</summary>
    public string MappingSchemaVersion { get; init; } = WorkloadMapping.CurrentSchemaVersion;
    /// <summary>Gets or sets the mapping hash.</summary>
    public string MappingHash { get; init; } = "";
    /// <summary>Gets or sets an optional provenance note.</summary>
    public string Note { get; init; } = "";
}

/// <summary>Selects the compatibility-safe source of phase-0 operands for an executable graph.</summary>
public enum ExecutableInitialPacketExecutionMode
{
    /// <summary>Preserves the approved schedule-derived executable runtime path.</summary>
    LegacySchedule,
    /// <summary>Injects cloned InitialPackets as the sole phase-0 operand truth.</summary>
    ExactOperands
}

/// <summary>Provides the executable simulation graph that combines compiled hardware with workload and transport inputs.</summary>
public sealed class ExecutableSimulationGraph
{
    /// <summary>Defines the current executable simulation graph schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the executable graph schema version.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the compiled hardware-only simulation graph.</summary>
    public HardwareSimulationGraph HardwareGraph { get; init; } = new();
    /// <summary>Gets or sets the workload schedule.</summary>
    public WorkloadSchedule Schedule { get; init; } = new();
    /// <summary>Gets or sets the opt-in phase-0 InitialPackets execution mode; legacy schedule behavior is the default.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("initial_packet_execution_mode")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ExecutableInitialPacketExecutionMode InitialPacketExecutionMode { get; init; } = ExecutableInitialPacketExecutionMode.LegacySchedule;
    /// <summary>Gets or sets initial logical messages.</summary>
    public IReadOnlyList<Message> InitialMessages { get; init; } = [];
    /// <summary>Gets or sets initial packets derived from messages and schedule.</summary>
    public IReadOnlyList<Packet> InitialPackets { get; init; } = [];
    /// <summary>Gets or sets explicit flits reserved for flit-level mode.</summary>
    public IReadOnlyList<Flit> InitialFlits { get; init; } = [];
    /// <summary>Gets or sets tensor tiles referenced by messages and packets.</summary>
    public IReadOnlyList<TensorTile> TensorTiles { get; init; } = [];
    /// <summary>Gets or sets executable storage map snapshots indexed by storage component id.</summary>
    public Dictionary<string, StorageMapSnapshot> StorageMaps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the mapping used by executable compilation.</summary>
    public WorkloadMapping Mapping { get; init; } = new();
    /// <summary>Gets or sets workload and mapping provenance hashes.</summary>
    public WorkloadMappingProvenance WorkloadMappingProvenance { get; init; } = new();
    /// <summary>Gets or sets the packetization mode requested by the executable graph.</summary>
    public PacketizationMode PacketizationMode { get; init; } = PacketizationMode.CoarsePacketMode;
    /// <summary>Gets or sets the frozen transport semantics contract used by this executable graph.</summary>
    public TransportSemanticsSnapshot TransportSemantics { get; init; } = TransportSemanticsContract.CreateDefault();
}