using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

/// <summary>Defines the supported component kind values used by hardware simulation contracts.</summary>
public enum ComponentKind
{
    /// <summary>Selects the processing element value for the component kind contract.</summary>
    ProcessingElement,
    /// <summary>Selects the router value for the component kind contract.</summary>
    Router,
    /// <summary>Selects the buffer value for the component kind contract.</summary>
    Buffer,
    /// <summary>Selects the memory value for the component kind contract.</summary>
    Memory,
    /// <summary>Selects the link endpoint value for the component kind contract.</summary>
    LinkEndpoint,
    /// <summary>Selects the reduction unit value for the component kind contract.</summary>
    ReductionUnit,
    /// <summary>Selects the softmax unit value for the component kind contract.</summary>
    SoftmaxUnit,
    /// <summary>Selects the workload source value for the component kind contract.</summary>
    WorkloadSource,
    /// <summary>Selects the workload sink value for the component kind contract.</summary>
    WorkloadSink,
    /// <summary>Selects the adapter value for the component kind contract.</summary>
    Adapter,
    /// <summary>Selects the precision converter value for the component kind contract.</summary>
    PrecisionConverter,
    /// <summary>Selects the quantizer value for the component kind contract.</summary>
    Quantizer,
    /// <summary>Selects the dequantizer value for the component kind contract.</summary>
    Dequantizer,
    /// <summary>Selects the optical link value for the component kind contract.</summary>
    OpticalLink,
    /// <summary>Selects the laser value for the component kind contract.</summary>
    Laser,
    /// <summary>Selects the mrr router value for the component kind contract.</summary>
    MrrRouter,
    /// <summary>Selects the mzi switch value for the component kind contract.</summary>
    MziSwitch,
    /// <summary>Selects the splitter value for the component kind contract.</summary>
    Splitter,
    /// <summary>Selects the combiner value for the component kind contract.</summary>
    Combiner,
    /// <summary>Selects the photodetector value for the component kind contract.</summary>
    Photodetector,
    /// <summary>Selects the modulator value for the component kind contract.</summary>
    Modulator,
    /// <summary>Selects the wdm mux value for the component kind contract.</summary>
    WdmMux,
    /// <summary>Selects the wdm demux value for the component kind contract.</summary>
    WdmDemux,
    /// <summary>Selects the eo converter value for the component kind contract.</summary>
    EoConverter,
    /// <summary>Selects the oe converter value for the component kind contract.</summary>
    OeConverter,
    /// <summary>Selects the re ram crossbar value for the component kind contract.</summary>
    ReRamCrossbar,
    /// <summary>Selects the fe fet crossbar value for the component kind contract.</summary>
    FeFetCrossbar,
    /// <summary>Selects the adc value for the component kind contract.</summary>
    Adc,
    /// <summary>Selects the dac value for the component kind contract.</summary>
    Dac,
    /// <summary>Selects the analog accumulator value for the component kind contract.</summary>
    AnalogAccumulator,
    /// <summary>Selects the sense amplifier value for the component kind contract.</summary>
    SenseAmplifier,
    /// <summary>Selects the write driver value for the component kind contract.</summary>
    WriteDriver,
    /// <summary>Selects the macro value for the component kind contract.</summary>
    Macro,
    /// <summary>Selects the custom value for the component kind contract.</summary>
    Custom
}

/// <summary>Defines the supported port direction values used by hardware simulation contracts.</summary>
public enum PortDirection
{
    /// <summary>Accepts data arriving at a component.</summary>
    Input,
    /// <summary>Emits data produced by a component.</summary>
    Output,
    /// <summary>Allows the port to act as either a source or destination.</summary>
    Bidirectional
}
/// <summary>Defines the supported signal type values used by hardware simulation contracts.</summary>
public enum SignalType
{
    /// <summary>Carries digitally encoded values.</summary>
    Digital,
    /// <summary>Carries continuous analog values.</summary>
    Analog,
    /// <summary>Carries optically encoded values.</summary>
    Optical,
    /// <summary>Carries control-plane state or commands.</summary>
    Control,
    /// <summary>Carries explicit memory or array address information.</summary>
    Address,
    /// <summary>Carries clock distribution timing signals.</summary>
    Clock,
    /// <summary>Carries power delivery signals.</summary>
    Power
}
/// <summary>Defines the supported hardware data type values used by hardware simulation contracts.</summary>
public enum HardwareDataType
{
    /// <summary>Represents multidimensional tensor data.</summary>
    Tensor,
    /// <summary>Represents a routed packet.</summary>
    Packet,
    /// <summary>Represents a single scalar value.</summary>
    Scalar,
    /// <summary>Represents configuration data.</summary>
    Config,
    /// <summary>Represents status or telemetry data.</summary>
    Status
}
/// <summary>Defines the supported precision kind values used by hardware simulation contracts.</summary>
public enum PrecisionKind
{
    /// <summary>Accepts any precision supported by the connected component.</summary>
    Any,
    /// <summary>Uses IEEE single-precision floating point.</summary>
    FP32,
    /// <summary>Uses IEEE half-precision floating point.</summary>
    FP16,
    /// <summary>Uses bfloat16 floating point.</summary>
    BF16,
    /// <summary>Uses TensorFloat-32 arithmetic precision.</summary>
    TF32,
    /// <summary>Uses 8-bit E4M3 floating point.</summary>
    FP8_E4M3,
    /// <summary>Uses 8-bit E5M2 floating point.</summary>
    FP8_E5M2,
    /// <summary>Uses signed 32-bit integer precision.</summary>
    INT32,
    /// <summary>Uses signed 16-bit integer precision.</summary>
    INT16,
    /// <summary>Uses signed 8-bit integer precision.</summary>
    INT8,
    /// <summary>Uses signed 4-bit integer precision.</summary>
    INT4,
    /// <summary>Uses signed 2-bit integer precision.</summary>
    INT2,
    /// <summary>Uses one-bit binary precision.</summary>
    Binary,
    /// <summary>Uses an analog value domain rather than a digital packet encoding.</summary>
    Analog
}
/// <summary>Defines the supported port protocol values used by hardware simulation contracts.</summary>
public enum PortProtocol
{
    /// <summary>Transfers an ordered stream without per-item requests.</summary>
    Streaming,
    /// <summary>Pairs each request with a corresponding response.</summary>
    RequestResponse,
    /// <summary>Uses address-based memory transactions.</summary>
    MemoryMapped,
    /// <summary>Transfers independently routed packets.</summary>
    Packet
}

/// <summary>Represents grid position data exchanged by hardware design and simulation workflows.</summary>
/// <param name="X">Provides the x value carried by this contract.</param>
/// <param name="Y">Provides the y value carried by this contract.</param>
public sealed record GridPosition(int X, int Y);
/// <summary>Represents port ref data exchanged by hardware design and simulation workflows.</summary>
/// <param name="ComponentId">Provides the component id value carried by this contract.</param>
/// <param name="PortName">Provides the port name value carried by this contract.</param>
public sealed record PortRef(string ComponentId, string PortName);

/// <summary>Represents hardware port data exchanged by hardware design and simulation workflows.</summary>
public sealed class HardwarePort
{
    /// <summary>Gets or sets the name value carried by the enclosing hardware port contract.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the direction value carried by the enclosing hardware port contract.</summary>
    public PortDirection Direction { get; set; }
    /// <summary>Gets or sets the signal type value carried by the enclosing hardware port contract.</summary>
    public SignalType SignalType { get; set; } = SignalType.Digital;
    /// <summary>Gets or sets the data type value carried by the enclosing hardware port contract.</summary>
    public HardwareDataType DataType { get; set; } = HardwareDataType.Packet;
    /// <summary>Gets or sets the precision value carried by the enclosing hardware port contract.</summary>
    public PrecisionKind Precision { get; set; } = PrecisionKind.Any;
    /// <summary>Gets or sets the protocol value carried by the enclosing hardware port contract.</summary>
    public PortProtocol Protocol { get; set; } = PortProtocol.Packet;
    /// <summary>Gets or sets the bandwidth bits per cycle value carried by the enclosing hardware port contract.</summary>
    public int BandwidthBitsPerCycle { get; set; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    /// <summary>Gets or sets the latency cycles value carried by the enclosing hardware port contract.</summary>
    public int LatencyCycles { get; set; }
    /// <summary>Gets or sets the clock domain value carried by the enclosing hardware port contract.</summary>
    public string ClockDomain { get; set; } = "default";
    /// <summary>Gets or sets whether compilation requires this port to be connected.</summary>
    public bool Required { get; set; }
    /// <summary>Gets or sets whether this port may participate in more than one link.</summary>
    public bool MultiConnect { get; set; }
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Represents hardware component data exchanged by hardware design and simulation workflows.</summary>
public sealed class HardwareComponent
{
    /// <summary>Gets or sets the id value carried by the enclosing hardware component contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the name value carried by the enclosing hardware component contract.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the type value carried by the enclosing hardware component contract.</summary>
    public ComponentKind Type { get; set; } = ComponentKind.Custom;
    /// <summary>Gets or sets the stable plugin type id when the component is not identified by a legacy built-in kind.</summary>
    [JsonPropertyName("type_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string TypeId { get; set; } = "";
    /// <summary>Gets or sets the optional ComponentTemplate instance reference used by Phase 7C compilation.</summary>
    [JsonPropertyName("template_ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ComponentTemplateInstanceRef? TemplateRef { get; set; }
    /// <summary>Gets or sets the position value carried by the enclosing hardware component contract.</summary>
    public GridPosition Position { get; set; } = new(0, 0);
    /// <summary>Gets or sets the ports collection carried by the enclosing hardware component contract.</summary>
    public List<HardwarePort> Ports { get; set; } = [];
    /// <summary>Gets or sets the parameters collection carried by the enclosing hardware component contract.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the model ref value carried by the enclosing hardware component contract.</summary>
    [JsonPropertyName("model_ref")]
    public string? ModelRef { get; set; }

    /// <summary>Gets or sets the latency model value carried by the enclosing hardware component contract.</summary>
    [JsonPropertyName("latency_model")]
    public string? LatencyModel { get; set; }

    /// <summary>Gets or sets the energy model value carried by the enclosing hardware component contract.</summary>
    [JsonPropertyName("energy_model")]
    public string? EnergyModel { get; set; }

    /// <summary>Gets or sets the area model value carried by the enclosing hardware component contract.</summary>
    [JsonPropertyName("area_model")]
    public string? AreaModel { get; set; }
    /// <summary>Gets or sets the visual style collection carried by the enclosing hardware component contract.</summary>
    public Dictionary<string, string> VisualStyle { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the internal state collection carried by the enclosing hardware component contract.</summary>
    public Dictionary<string, string> InternalState { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Finds port when it exists.</summary>
    public HardwarePort? FindPort(string portName) =>
        Ports.FirstOrDefault(p => string.Equals(p.Name, portName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns int parameter from the current contract.</summary>
    public int GetIntParameter(string name, int defaultValue)
    {
        return Parameters.TryGetValue(name, out var raw) && int.TryParse(raw, out var value)
            ? value
            : defaultValue;
    }

    /// <summary>Returns double parameter from the current contract.</summary>
    public double GetDoubleParameter(string name, double defaultValue)
    {
        return Parameters.TryGetValue(name, out var raw) && double.TryParse(raw, out var value)
            ? value
            : defaultValue;
    }
}

/// <summary>Defines the single Core source for built-in component parameter defaults.</summary>
public static class ComponentDefaults
{
    /// <summary>Default global flit size in bits.</summary>
    public const int GlobalFlitSizeBits = 128;
    /// <summary>Default PE MAC throughput in MAC operations per cycle.</summary>
    public const int ProcessingElementMacPerCycle = 256;
    /// <summary>Default router port count.</summary>
    public const int RouterNumPorts = 5;
    /// <summary>Default router virtual channel count per physical input.</summary>
    public const int RouterVirtualChannels = 2;
    /// <summary>Default router virtual channel depth in flits.</summary>
    public const int RouterVcDepthFlits = 4;
    /// <summary>Default router routing policy.</summary>
    public const string RouterRoutingPolicy = "xy";
    /// <summary>Default router crossbar issue model.</summary>
    public const string RouterCrossbarIssueModel = nameof(CrossbarIssueModel.PerOutputIssue);
    /// <summary>Default buffer capacity in bits.</summary>
    public const int BufferCapacityBits = 65_536;
    /// <summary>Default buffer read bandwidth in bits per cycle.</summary>
    public const int BufferReadBandwidthBitsPerCycle = 128;
    /// <summary>Default buffer write bandwidth in bits per cycle.</summary>
    public const int BufferWriteBandwidthBitsPerCycle = 128;
    /// <summary>Default buffer read latency in cycles.</summary>
    public const int BufferReadLatencyCycles = 0;
    /// <summary>Default buffer write latency in cycles.</summary>
    public const int BufferWriteLatencyCycles = 0;
    /// <summary>Default memory capacity in bits.</summary>
    public const long MemoryCapacityBits = 67_108_864;
    /// <summary>Default memory port count.</summary>
    public const int MemoryPorts = 1;
    /// <summary>Default memory bank count.</summary>
    public const int MemoryBanks = 1;
    /// <summary>Default per-bank port count.</summary>
    public const int MemoryBankPorts = 1;
    /// <summary>Default memory line size in bits.</summary>
    public const int MemoryLineSizeBits = 128;
    /// <summary>Default memory read bandwidth in bits per cycle.</summary>
    public const int MemoryReadBandwidthBitsPerCycle = 128;
    /// <summary>Default memory write bandwidth in bits per cycle.</summary>
    public const int MemoryWriteBandwidthBitsPerCycle = 128;
    /// <summary>Default memory read latency in cycles.</summary>
    public const int MemoryReadLatency = 5;
    /// <summary>Default memory write latency in cycles.</summary>
    public const int MemoryWriteLatency = 1;
    /// <summary>Default link bandwidth in bits per cycle.</summary>
    public const int LinkBandwidthBitsPerCycle = 128;
    /// <summary>Default link base latency in cycles.</summary>
    public const int LinkBaseLatencyCycles = 1;
    /// <summary>Default link energy per bit in picojoules.</summary>
    public const double LinkEnergyPerBitPJ = 0.01;
    /// <summary>Default processing-element physical area in square micrometers.</summary>
    public const double ProcessingElementAreaUm2 = 50_000;
    /// <summary>Default router physical area in square micrometers.</summary>
    public const double RouterAreaUm2 = 30_000;
    /// <summary>Default buffer physical area in square micrometers.</summary>
    public const double BufferAreaUm2 = 10_000;
    /// <summary>Default memory physical area in square micrometers.</summary>
    public const double MemoryAreaUm2 = 200_000;
    /// <summary>Default reduction input count.</summary>
    public const int ReductionUnitNumInputs = 4;
    /// <summary>Default reduction accumulation latency in cycles.</summary>
    public const int ReductionUnitAccumulateLatency = 2;
    /// <summary>Default softmax compute latency in cycles.</summary>
    public const int SoftmaxUnitComputeLatency = 8;

    /// <summary>Returns the built-in defaults for the supplied component kind.</summary>
    public static Dictionary<string, string> For(ComponentKind kind) => kind switch
    {
        ComponentKind.ProcessingElement => new(StringComparer.OrdinalIgnoreCase)
        {
            ["mac_per_cycle"] = ProcessingElementMacPerCycle.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        ComponentKind.Router => new(StringComparer.OrdinalIgnoreCase)
        {
            ["num_ports"] = RouterNumPorts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["virtual_channels"] = RouterVirtualChannels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["vc_depth_flits"] = RouterVcDepthFlits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["routing_policy"] = RouterRoutingPolicy,
            ["crossbar_issue_model"] = RouterCrossbarIssueModel
        },
        ComponentKind.Buffer => new(StringComparer.OrdinalIgnoreCase)
        {
            ["capacity_bits"] = BufferCapacityBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["read_bandwidth_bits_per_cycle"] = BufferReadBandwidthBitsPerCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["write_bandwidth_bits_per_cycle"] = BufferWriteBandwidthBitsPerCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["read_latency_cycles"] = BufferReadLatencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["write_latency_cycles"] = BufferWriteLatencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        ComponentKind.Memory => new(StringComparer.OrdinalIgnoreCase)
        {
            ["capacity_bits"] = MemoryCapacityBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["memory_ports"] = MemoryPorts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["memory_banks"] = MemoryBanks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bank_ports"] = MemoryBankPorts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["line_size_bits"] = MemoryLineSizeBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["read_bandwidth_bits_per_cycle"] = MemoryReadBandwidthBitsPerCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["write_bandwidth_bits_per_cycle"] = MemoryWriteBandwidthBitsPerCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["read_latency"] = MemoryReadLatency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["read_latency_cycles"] = MemoryReadLatency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["write_latency_cycles"] = MemoryWriteLatency.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        ComponentKind.ReductionUnit => new(StringComparer.OrdinalIgnoreCase)
        {
            ["num_inputs"] = ReductionUnitNumInputs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["accumulate_latency"] = ReductionUnitAccumulateLatency.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        ComponentKind.SoftmaxUnit => new(StringComparer.OrdinalIgnoreCase)
        {
            ["compute_latency"] = SoftmaxUnitComputeLatency.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        _ => new(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>Returns one default parameter value, or a fallback when none exists.</summary>
    public static string Get(ComponentKind kind, string key, string fallback = "") =>
        For(kind).TryGetValue(key, out var value) ? value : fallback;

    /// <summary>Returns the typed default physical area for a system-level physical component.</summary>
    public static SquareMicrometers DefaultArea(ComponentKind kind) => new(kind switch
    {
        ComponentKind.ProcessingElement => ProcessingElementAreaUm2,
        ComponentKind.Router => RouterAreaUm2,
        ComponentKind.Buffer => BufferAreaUm2,
        ComponentKind.Memory => MemoryAreaUm2,
        _ => 0
    });

    /// <summary>Fills missing built-in defaults without replacing explicit component parameters.</summary>
    public static void ApplyTo(HardwareComponent component)
    {
        foreach (var pair in For(component.Type))
        {
            component.Parameters.TryAdd(pair.Key, pair.Value);
        }
    }
}

/// <summary>Represents hardware link data exchanged by hardware design and simulation workflows.</summary>
public sealed class HardwareLink
{
    /// <summary>Gets or sets the id value carried by the enclosing hardware link contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the source value carried by the enclosing hardware link contract.</summary>
    public PortRef Source { get; set; } = new("", "");
    /// <summary>Gets or sets the destination value carried by the enclosing hardware link contract.</summary>
    public PortRef Destination { get; set; } = new("", "");
    /// <summary>Gets or sets the model ref value carried by the enclosing hardware link contract.</summary>
    [JsonPropertyName("model_ref")]
    public string? ModelRef { get; set; }
    /// <summary>Gets or sets the bandwidth bits per cycle value carried by the enclosing hardware link contract.</summary>
    public int BandwidthBitsPerCycle { get; set; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    /// <summary>Gets or sets the latency cycles value carried by the enclosing hardware link contract.</summary>
    public int LatencyCycles { get; set; } = ComponentDefaults.LinkBaseLatencyCycles;
    /// <summary>Gets or sets the energy per bit value carried by the enclosing hardware link contract.</summary>
    public double EnergyPerBit { get; set; } = ComponentDefaults.LinkEnergyPerBitPJ;
    /// <summary>Gets or sets the physical length value carried by the enclosing hardware link contract.</summary>
    public double PhysicalLength { get; set; }
    /// <summary>Gets or sets the route type value carried by the enclosing hardware link contract.</summary>
    public string RouteType { get; set; } = "logical";
    /// <summary>Gets or sets the parameters collection carried by the enclosing hardware link contract.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Represents visual group data exchanged by hardware design and simulation workflows.</summary>
public sealed class VisualGroup
{
    /// <summary>Gets or sets the id value carried by the enclosing visual group contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the name value carried by the enclosing visual group contract.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the component ids collection carried by the enclosing visual group contract.</summary>
    public List<string> ComponentIds { get; set; } = [];
    /// <summary>Gets or sets whether editors render the group in its collapsed form.</summary>
    public bool Collapsed { get; set; }
    /// <summary>Gets or sets visual metadata such as color, bounds, or editor hints.</summary>
    public Dictionary<string, string> VisualMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Represents macro component data exchanged by hardware design and simulation workflows.</summary>
public sealed class MacroComponent
{
    /// <summary>Defines the canonical current macro schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the schema version for this macro definition.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the id value carried by the enclosing macro component contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the name value carried by the enclosing macro component contract.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the internal components collection carried by the enclosing macro component contract.</summary>
    public List<HardwareComponent> InternalComponents { get; set; } = [];
    /// <summary>Gets or sets the internal links collection carried by the enclosing macro component contract.</summary>
    public List<HardwareLink> InternalLinks { get; set; } = [];
    /// <summary>Gets or sets visual groups captured inside the macro for lossless editor collapse and expand.</summary>
    public List<VisualGroup> InternalGroups { get; set; } = [];
    /// <summary>Gets or sets the external port mappings collection carried by the enclosing macro component contract.</summary>
    public Dictionary<string, PortRef> ExternalPortMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Represents hardware graph data exchanged by hardware design and simulation workflows.</summary>
public sealed class HardwareGraph
{
    /// <summary>Defines the canonical current schema version value used by the enclosing hardware graph contract.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Gets or sets the schema version value carried by the enclosing hardware graph contract.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Gets or sets the components collection carried by the enclosing hardware graph contract.</summary>
    public List<HardwareComponent> Components { get; set; } = [];
    /// <summary>Gets or sets the links collection carried by the enclosing hardware graph contract.</summary>
    public List<HardwareLink> Links { get; set; } = [];
    /// <summary>Gets or sets the groups collection carried by the enclosing hardware graph contract.</summary>
    public List<VisualGroup> Groups { get; set; } = [];
    /// <summary>Gets or sets the macros collection carried by the enclosing hardware graph contract.</summary>
    public List<MacroComponent> Macros { get; set; } = [];
    /// <summary>Gets or sets the parameters collection carried by the enclosing hardware graph contract.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the placement value carried by the enclosing hardware graph contract.</summary>
    public PhysicalPlacement? Placement { get; set; }
    /// <summary>Gets or sets the routing value carried by the enclosing hardware graph contract.</summary>
    public PhysicalRouting? Routing { get; set; }
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Finds a component by case-insensitive identifier.</summary>
    public HardwareComponent? FindComponent(string componentId) =>
        Components.FirstOrDefault(c => string.Equals(c.Id, componentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolves a component and port reference against this graph.</summary>
    public HardwarePort? FindPort(PortRef portRef) => FindComponent(portRef.ComponentId)?.FindPort(portRef.PortName);
}

/// <summary>Serializes and validates the stable HardwareGraph 1.0 JSON contract.</summary>
public static class HardwareGraphJson
{
    /// <summary>Gets the shared serializer settings for stable field names and enum values.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Serializes the supplied contract to its stable JSON representation.</summary>
    public static string Serialize(HardwareGraph graph)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }
        return JsonSerializer.Serialize(graph, Options);
    }

    /// <summary>Attempts to deserialize JSON and returns structured issues instead of an expected parse exception.</summary>
    public static HardwareGraphDeserializationResult TryDeserialize(string json)
    {
        var import = HardwareGraphSchemaMigrator.ImportToCurrent(json);
        return import.IsSuccess && import.Graph is not null
            ? HardwareGraphDeserializationResult.Success(import.Graph)
            : HardwareGraphDeserializationResult.Failure(import.Issues.ToArray());
    }

    /// <summary>Deserializes a stable JSON representation into the corresponding contract.</summary>
    public static HardwareGraph Deserialize(string json)
    {
        var result = TryDeserialize(json);
        if (result.Graph is not null)
        {
            return result.Graph;
        }

        throw new HardwareGraphSerializationException(result.Issues);
    }

    internal static HardwareGraphDeserializationResult DeserializeCurrentSchemaJson(string json)
    {
        try
        {
            var graph = JsonSerializer.Deserialize<HardwareGraph>(json, Options);
            if (graph is null)
            {
                return HardwareGraphDeserializationResult.Failure(
                    new HardwareGraphSerializationIssue(
                        "InvalidJson",
                        "error",
                        "$",
                        "HardwareGraph JSON did not contain an object."));
            }

            var versionIssue = ValidateCurrentSchemaVersion(graph.SchemaVersion);
            if (versionIssue is not null)
            {
                return HardwareGraphDeserializationResult.Failure(versionIssue);
            }

            NormalizeGraph(graph);
            return HardwareGraphDeserializationResult.Success(graph);
        }
        catch (JsonException exception)
        {
            return HardwareGraphDeserializationResult.Failure(
                new HardwareGraphSerializationIssue(
                    "InvalidJson",
                    "error",
                    exception.Path ?? "$",
                    exception.Message));
        }
    }

    internal static void NormalizeGraph(HardwareGraph graph)
    {
        graph.SchemaVersion = string.IsNullOrWhiteSpace(graph.SchemaVersion)
            ? HardwareGraph.CurrentSchemaVersion
            : graph.SchemaVersion;
        graph.Components ??= [];
        graph.Links ??= [];
        graph.Groups ??= [];
        graph.Macros ??= [];
        graph.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        graph.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        graph.Placement?.Normalize();
        graph.Routing?.Normalize();
        foreach (var component in graph.Components)
        {
            NormalizeComponent(component);
        }

        foreach (var link in graph.Links)
        {
            NormalizeLink(link);
        }

        foreach (var group in graph.Groups)
        {
            group.ComponentIds ??= [];
            group.VisualMetadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            group.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        foreach (var macro in graph.Macros)
        {
            macro.InternalComponents ??= [];
            macro.InternalLinks ??= [];
            macro.InternalGroups ??= [];
            macro.ExternalPortMappings ??= new Dictionary<string, PortRef>(StringComparer.OrdinalIgnoreCase);
            macro.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            macro.SchemaVersion = string.IsNullOrWhiteSpace(macro.SchemaVersion)
                ? MacroComponent.CurrentSchemaVersion
                : macro.SchemaVersion;
            foreach (var component in macro.InternalComponents)
            {
                NormalizeComponent(component);
            }

            foreach (var link in macro.InternalLinks)
            {
                NormalizeLink(link);
            }

            foreach (var group in macro.InternalGroups)
            {
                group.ComponentIds ??= [];
                group.VisualMetadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                group.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
        }
    }

    internal static HardwareGraphSerializationIssue? ValidateCurrentSchemaVersion(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return new HardwareGraphSerializationIssue(
                "MissingSchemaVersion",
                "error",
                "$.schema_version",
                "HardwareGraph schema_version is required.");
        }

        var majorText = schemaVersion.Split('.', 2)[0];
        if (!int.TryParse(majorText, out var major))
        {
            return new HardwareGraphSerializationIssue(
                "InvalidSchemaVersion",
                "error",
                "$.schema_version",
                $"HardwareGraph schema_version '{schemaVersion}' is not valid.");
        }

        if (major != 1)
        {
            return new HardwareGraphSerializationIssue(
                "UnsupportedSchemaVersion",
                "error",
                "$.schema_version",
                $"HardwareGraph schema major version '{major}' is not supported; supported major version is 1.");
        }

        if (!string.Equals(schemaVersion, HardwareGraph.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            return new HardwareGraphSerializationIssue(
                "UnsupportedSchemaVersion",
                "error",
                "$.schema_version",
                $"HardwareGraph schema version '{schemaVersion}' is not supported by the current reader; supported version is {HardwareGraph.CurrentSchemaVersion}.");
        }

        return null;
    }

    private static void NormalizeComponent(HardwareComponent component)
    {
        component.Ports ??= [];
        component.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        component.TypeId ??= "";
        if (component.TemplateRef is not null)
        {
            component.TemplateRef.ParameterOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        component.VisualStyle ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        component.InternalState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        component.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var port in component.Ports)
        {
            port.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private static void NormalizeLink(HardwareLink link)
    {
        link.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        link.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}

/// <summary>Represents hardware graph serialization issue data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Code">Provides the code value carried by this contract.</param>
/// <param name="Severity">Provides the severity value carried by this contract.</param>
/// <param name="Location">Provides the location value carried by this contract.</param>
/// <param name="Message">Provides the message value carried by this contract.</param>
/// <param name="Suggestion">Provides the suggestion value carried by this contract.</param>
public sealed record HardwareGraphSerializationIssue(
    string Code,
    string Severity,
    string Location,
    string Message,
    string? Suggestion = null);

/// <summary>Contains either a normalized HardwareGraph or structured deserialization issues.</summary>
public sealed class HardwareGraphDeserializationResult
{
    private HardwareGraphDeserializationResult(
        HardwareGraph? graph,
        IReadOnlyList<HardwareGraphSerializationIssue> issues)
    {
        Graph = graph;
        Issues = issues;
    }

    /// <summary>Gets whether JSON parsing and schema validation succeeded.</summary>
    public bool IsSuccess => Graph is not null && Issues.Count == 0;
    /// <summary>Gets the graph value carried by the enclosing hardware graph deserialization result contract.</summary>
    public HardwareGraph? Graph { get; }
    /// <summary>Gets parse or schema issues; the collection is empty after success.</summary>
    public IReadOnlyList<HardwareGraphSerializationIssue> Issues { get; }

    /// <summary>Creates a successful result containing the normalized graph.</summary>
    public static HardwareGraphDeserializationResult Success(HardwareGraph graph) => new(graph, []);

    /// <summary>Creates a failed result containing structured issues and no graph.</summary>
    public static HardwareGraphDeserializationResult Failure(params HardwareGraphSerializationIssue[] issues) =>
        new(null, issues);
}

/// <summary>Represents hardware graph serialization exception data exchanged by hardware design and simulation workflows.</summary>
public sealed class HardwareGraphSerializationException : Exception
{
    /// <summary>Initializes a new hardware graph serialization exception instance from the supplied state.</summary>
    public HardwareGraphSerializationException(IReadOnlyList<HardwareGraphSerializationIssue> issues)
        : base(string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code} at {issue.Location}: {issue.Message}")))
    {
        Issues = issues;
    }

    /// <summary>Gets the structured issues represented by this exception.</summary>
    public IReadOnlyList<HardwareGraphSerializationIssue> Issues { get; }
}
