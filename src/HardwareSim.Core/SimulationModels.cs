namespace HardwareSim.Core;

/// <summary>Defines the supported trace event type values used by hardware simulation contracts.</summary>
public enum TraceEventType
{
    /// <summary>Selects the packet injection value for the trace event type contract.</summary>
    PacketInjection,
    /// <summary>Selects the packet move value for the trace event type contract.</summary>
    PacketMove,
    /// <summary>Selects the stall value for the trace event type contract.</summary>
    Stall,
    /// <summary>Selects the router arbitration value for the trace event type contract.</summary>
    Arbitration,
    /// <summary>Selects the compute value for the trace event type contract.</summary>
    Compute,
    /// <summary>Selects the operation start value for the trace event type contract.</summary>
    OperationStart,
    /// <summary>Selects the operation complete value for the trace event type contract.</summary>
    OperationComplete,
    /// <summary>Selects the buffer occupancy value for the trace event type contract.</summary>
    BufferOccupancy,
    /// <summary>Selects the link transfer value for the trace event type contract.</summary>
    LinkTransfer,
    /// <summary>Selects the flit issue value for the flit-level trace contract.</summary>
    FlitIssue,
    /// <summary>Selects the flit serialization progress value for the flit-level trace contract.</summary>
    FlitSerialization,
    /// <summary>Selects the flit arrival value for the flit-level trace contract.</summary>
    FlitArrival,
    /// <summary>Selects the virtual channel state value for the flit-level router trace contract.</summary>
    VirtualChannel,
    /// <summary>Selects the memory request lifecycle value for the flit-level memory trace contract.</summary>
    MemoryRequest,
    /// <summary>Selects the energy value for the trace event type contract.</summary>
    Energy,
    /// <summary>Selects the warning value for the trace event type contract.</summary>
    Warning,
    /// <summary>Selects the error value for the trace event type contract.</summary>
    Error
}


/// <summary>Defines the fixed deterministic cycle kernel phases executed in order for every cycle.</summary>
public enum CycleKernelPhase
{
    /// <summary>Phase 0 records external workload injection into next state.</summary>
    ExternalEventInjection = 0,
    /// <summary>Phase 1 samples ready link arrivals from current state.</summary>
    InputSampling = 1,
    /// <summary>Phase 2 checks component readiness against current state.</summary>
    ReadyCheck = 2,
    /// <summary>Phase 3 performs deterministic resource arbitration and scheduling.</summary>
    ArbitrationScheduling = 3,
    /// <summary>Phase 4 advances compute items that were ready in current state.</summary>
    ComputeProgressUpdate = 4,
    /// <summary>Phase 5 issues link transfers from current output buffers.</summary>
    LinkTransferIssue = 5,
    /// <summary>Phase 6 records buffer and memory updates staged for commit.</summary>
    BufferMemoryUpdate = 6,
    /// <summary>Phase 7 commits next state into current state.</summary>
    StateCommit = 7,
    /// <summary>Phase 8 accumulates metrics after state commit.</summary>
    MetricsAccumulation = 8,
    /// <summary>Phase 9 records the completed cycle trace.</summary>
    TraceRecord = 9
}

/// <summary>Describes the minimal Tick/Commit contract required by deterministic cycle components.</summary>
public interface ICycleComponent
{
    /// <summary>Gets the stable component identifier used for deterministic traversal.</summary>
    string ComponentId { get; }
    /// <summary>Ticks the component for a single fixed kernel phase.</summary>
    void Tick(CycleKernelPhase phase);
    /// <summary>Commits staged next-state changes after all phases have run.</summary>
    void Commit();
}
/// <summary>Defines the supported component runtime state values used by hardware simulation contracts.</summary>
public enum ComponentRuntimeState
{
    /// <summary>The component has no work in progress.</summary>
    Idle,
    /// <summary>The component is advancing useful work.</summary>
    Active,
    /// <summary>The component cannot advance because a required resource is unavailable.</summary>
    Stalled
}

/// <summary>Defines the committed runtime state values for Phase 3C processing elements.</summary>
public enum ProcessingElementRuntimeState
{
    /// <summary>The PE is idle and waiting for a complete input packet.</summary>
    WaitingInput,
    /// <summary>The PE is consuming MAC cycles for its current packet.</summary>
    Computing,
    /// <summary>The PE has computed a result and is waiting for downstream capacity.</summary>
    WaitingOutput
}

/// <summary>Defines router crossbar issue models used by the Phase 3C flit runtime.</summary>
public enum CrossbarIssueModel
{
    /// <summary>The entire router may accept at most one flit per cycle.</summary>
    SingleIssue,
    /// <summary>Each physical input and each physical output may accept at most one grant per cycle.</summary>
    PerOutputIssue,
    /// <summary>The router performs a stable maximum matching subject to lane and bit budgets.</summary>
    FullCrossbar
}
/// <summary>Defines the supported stall reason values used by hardware simulation contracts.</summary>
public enum StallReason
{
    /// <summary>Selects the input buffer empty value for the stall reason contract.</summary>
    InputBufferEmpty,
    /// <summary>Selects the output buffer full value for the stall reason contract.</summary>
    OutputBufferFull,
    /// <summary>Selects the link busy value for the stall reason contract.</summary>
    LinkBusy,
    /// <summary>Selects the router conflict value for the stall reason contract.</summary>
    RouterConflict,
    /// <summary>Selects the memory busy value for the stall reason contract.</summary>
    MemoryBusy,
    /// <summary>Selects the dependency not ready value for the stall reason contract.</summary>
    DependencyNotReady,
    /// <summary>Selects the precision converter busy value for the stall reason contract.</summary>
    PrecisionConverterBusy,
    /// <summary>Selects the optical channel unavailable value for the stall reason contract.</summary>
    OpticalChannelUnavailable,
    /// <summary>Selects the no route value for the stall reason contract.</summary>
    NoRoute
}

/// <summary>Defines coarse packet payload types used by workload-aware simulation contracts.</summary>
public enum PacketType
{
    /// <summary>Represents activation data moving through the simulated graph.</summary>
    Activation,
    /// <summary>Represents weight tensor data.</summary>
    Weight,
    /// <summary>Represents partial sum tensor data.</summary>
    PartialSum,
    /// <summary>Represents attention score tensor data.</summary>
    AttentionScore,
    /// <summary>Represents normalized softmax result tensor data.</summary>
    SoftmaxResult,
    /// <summary>Represents control-plane data.</summary>
    Control,
    /// <summary>Represents configuration data.</summary>
    Config,
    /// <summary>Represents status or telemetry data.</summary>
    Status,
    /// <summary>Represents an address-bearing memory read request.</summary>
    MemoryReadRequest,
    /// <summary>Represents a memory read response.</summary>
    MemoryReadResponse,
    /// <summary>Represents an address-bearing memory write request.</summary>
    MemoryWriteRequest,
    /// <summary>Represents optical-domain signal metadata.</summary>
    OpticalSignal,
    /// <summary>Represents analog-domain signal metadata.</summary>
    AnalogSignal
}
/// <summary>Defines memory operation types attached to packet-level memory requests.</summary>
public enum MemoryOperationType
{
    /// <summary>Indicates that the packet is not a memory operation.</summary>
    None,
    /// <summary>Indicates a memory read operation.</summary>
    Read,
    /// <summary>Indicates a memory write operation.</summary>
    Write
}

/// <summary>Represents an explicit bit-addressed memory transaction in the Phase 3C runtime contract.</summary>
public sealed class MemoryRequest
{
    /// <summary>Gets or sets the stable request identifier.</summary>
    public string RequestId { get; set; } = "";
    /// <summary>Gets or sets the memory operation.</summary>
    public MemoryOperationType Operation { get; set; } = MemoryOperationType.None;
    /// <summary>Gets or sets the storage component identifier targeted by the request.</summary>
    public string StorageId { get; set; } = "";
    /// <summary>Gets or sets the bit address targeted by the request.</summary>
    public long BitAddress { get; set; }
    /// <summary>Gets or sets the request size in bits.</summary>
    public long SizeBits { get; set; }
    /// <summary>Gets or sets the logical packet id that produced this request.</summary>
    public string PacketId { get; set; } = "";
    /// <summary>Gets or sets the tensor tile id associated with this request.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the component that should receive the read response or status packet.</summary>
    public string ResponseDestinationComponentId { get; set; } = "";
    /// <summary>Gets or sets additional deterministic request metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Defines whether executable graph traffic is represented as whole packets or explicit flits.</summary>
public enum PacketizationMode
{
    /// <summary>Uses one coarse packet per transfer and computes serialization from packet bits and bandwidth.</summary>
    CoarsePacketMode,
    /// <summary>Represents packets as explicit flits; this mode is reserved and not run by Phase 3A.</summary>
    FlitLevelMode
}

/// <summary>Represents a logical tile transfer before packet-level transport decomposition.</summary>
public sealed class Message
{
    /// <summary>Gets or sets the message identifier.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the source component id.</summary>
    public string SourceComponentId { get; set; } = "";
    /// <summary>Gets or sets the destination component id.</summary>
    public string DestinationComponentId { get; set; } = "";
    /// <summary>Gets or sets the workload operation id that produced or consumes the message.</summary>
    public string WorkloadOpId { get; set; } = "";
    /// <summary>Gets or sets the tensor id carried by the message.</summary>
    public string TensorId { get; set; } = "";
    /// <summary>Gets or sets the tile id carried by the message.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the packet ids generated from this message.</summary>
    public List<string> PacketIds { get; set; } = [];
    /// <summary>Gets or sets transfer metadata for the message.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Represents a tensor tile and its workload provenance.</summary>
public sealed class TensorTile
{
    /// <summary>Gets or sets the tensor identifier.</summary>
    public string TensorId { get; set; } = "";
    /// <summary>Gets or sets the tile identifier.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the tensor display name.</summary>
    public string TensorName { get; set; } = "";
    /// <summary>Gets or sets the full tensor shape.</summary>
    public List<int> GlobalShape { get; set; } = [];
    /// <summary>Gets or sets the tile shape.</summary>
    public List<int> TileShape { get; set; } = [];
    /// <summary>Gets or sets the tile offset within the global tensor.</summary>
    public List<int> TileOffset { get; set; } = [];
    /// <summary>Gets or sets the tile precision.</summary>
    public PrecisionKind Precision { get; set; } = PrecisionKind.INT8;
    /// <summary>Gets or sets the producer operation id.</summary>
    public string ProducerOpId { get; set; } = "";
    /// <summary>Gets or sets the consumer operation ids.</summary>
    public List<string> ConsumerOpIds { get; set; } = [];
    /// <summary>Gets or sets a storage component or hierarchy hint.</summary>
    public string StorageLocation { get; set; } = "";
    /// <summary>Gets or sets the explicit storage component id after placement.</summary>
    public string StorageId { get; set; } = "";
    /// <summary>Gets or sets the allocated base bit address after placement.</summary>
    public long? BaseAddressBits { get; set; }
    /// <summary>Gets or sets the allocated size in bits after placement.</summary>
    public long SizeBits { get; set; }
    /// <summary>Gets or sets an optional address hint for memory placement.</summary>
    public string AddressHint { get; set; } = "";
    /// <summary>Gets the number of elements in the tile.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long NumElements => TileShape.Count == 0 ? 0 : TileShape.Aggregate(1L, (acc, dim) => acc * Math.Max(0, dim));
    /// <summary>Gets the digital total bit count for the tile.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long TotalBits => PrecisionModel.TryGetDigitalBitWidth(Precision, out var bitWidth) ? NumElements * bitWidth : 0;
}


/// <summary>Describes one tensor tile validation diagnostic.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Location">JSON-style location of the invalid tile field.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="RelatedId">Optional tile or operation identifier.</param>
public sealed record TensorTileValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null);

/// <summary>Contains tensor tile validation diagnostics.</summary>
public sealed class TensorTileValidationResult
{
    /// <summary>Gets tensor tile validation issues.</summary>
    public List<TensorTileValidationIssue> Issues { get; } = [];
    /// <summary>Gets whether validation has no errors.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Validates tensor tile shape bounds and workload operation references.</summary>
public static class TensorTileValidator
{
    /// <summary>Validates one tensor tile against a workload graph.</summary>
    public static TensorTileValidationResult Validate(TensorTile tile, WorkloadGraph workload)
    {
        var result = new TensorTileValidationResult();
        if (tile.GlobalShape.Count != tile.TileShape.Count || tile.GlobalShape.Count != tile.TileOffset.Count)
        {
            Add(result, "TensorTileRankMismatch", "$.tileShape", $"Tile '{tile.TileId}' shape, offset, and global rank must match.", tile.TileId);
            return result;
        }

        for (var index = 0; index < tile.GlobalShape.Count; index++)
        {
            if (tile.TileShape[index] <= 0 || tile.TileOffset[index] < 0 || tile.TileOffset[index] + tile.TileShape[index] > tile.GlobalShape[index])
            {
                Add(result, "TensorTileBoundsError", $"$.tileOffset[{index}]", $"Tile '{tile.TileId}' is outside tensor bounds at dimension {index}.", tile.TileId);
            }
        }

        var opIds = workload.Ops.Select(operation => operation.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!opIds.Contains(tile.ProducerOpId))
        {
            Add(result, "TensorTileProducerReferenceError", "$.producerOpId", $"Tile '{tile.TileId}' references missing producer operation '{tile.ProducerOpId}'.", tile.ProducerOpId);
        }

        foreach (var consumer in tile.ConsumerOpIds)
        {
            if (!opIds.Contains(consumer))
            {
                Add(result, "TensorTileConsumerReferenceError", "$.consumerOpIds", $"Tile '{tile.TileId}' references missing consumer operation '{consumer}'.", consumer);
            }
        }

        return result;
    }

    private static void Add(TensorTileValidationResult result, string code, string location, string message, string? relatedId) =>
        result.Issues.Add(new TensorTileValidationIssue(code, ValidationSeverity.Error, location, message, relatedId));
}
/// <summary>Represents packet data exchanged by hardware design and simulation workflows.</summary>
public sealed class Packet
{
    /// <summary>Gets or sets the id value carried by the enclosing packet contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the packet type value carried by the enclosing packet contract.</summary>
    public PacketType PacketType { get; set; } = PacketType.Activation;
    /// <summary>Gets or sets the legacy packet type alias.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public PacketType Type
    {
        get => PacketType;
        set => PacketType = value;
    }
    /// <summary>Gets or sets the number of elements represented by this packet.</summary>
    public int NumElements { get; set; } = 16;
    /// <summary>Gets or sets the digital bit width for each element.</summary>
    public int BitWidth { get; set; } = 8;
    /// <summary>Gets or sets the bits value carried by the enclosing packet contract.</summary>
    public int Bits { get; set; } = 128;
    /// <summary>Gets or sets the total bits value carried by the enclosing packet contract.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int TotalBits
    {
        get => Bits;
        set
        {
            Bits = value;
            if (BitWidth > 0)
            {
                NumElements = Math.Max(1, value / BitWidth);
            }
        }
    }
    /// <summary>Gets or sets the precision of the packet payload.</summary>
    public PrecisionKind Precision { get; set; } = PrecisionKind.INT8;
    /// <summary>Gets or sets the source component id value carried by the enclosing packet contract.</summary>
    public string SourceComponentId { get; set; } = "";
    /// <summary>Gets or sets the destination component id value carried by the enclosing packet contract.</summary>
    public string DestinationComponentId { get; set; } = "";
    /// <summary>Gets or sets the source port name.</summary>
    public string SourcePort { get; set; } = "";
    /// <summary>Gets or sets the destination port name.</summary>
    public string DestinationPort { get; set; } = "";
    /// <summary>Gets or sets the workload operation id provenance.</summary>
    public string WorkloadOpId { get; set; } = "";
    /// <summary>Gets or sets the tensor id provenance.</summary>
    public string TensorId { get; set; } = "";
    /// <summary>Gets or sets the tile id provenance.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the route path used or requested by this packet.</summary>
    public List<string> RoutePath { get; set; } = [];
    /// <summary>Gets or sets dependency packet or message ids.</summary>
    public List<string> DependencyIds { get; set; } = [];
    /// <summary>Gets or sets packet metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the typed signal domain currently carried by this packet.</summary>
    public PacketSignalDomain SignalDomain { get; set; } = PacketSignalDomain.Digital;
    /// <summary>Gets or sets typed optical carrier state while the packet is in the optical domain.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public OpticalPacketState? OpticalState { get; set; }
    /// <summary>Gets or sets the request id value for memory transactions.</summary>
    public string RequestId { get; set; } = "";
    /// <summary>Gets or sets the memory operation value for memory transactions.</summary>
    public MemoryOperationType MemoryOperation { get; set; } = MemoryOperationType.None;
    /// <summary>Gets or sets the memory address value for bank selection and request tracing.</summary>
    public long? MemoryAddress { get; set; }
    /// <summary>Gets or sets the current component id value carried by the enclosing packet contract.</summary>
    public string CurrentComponentId { get; set; } = "";
    /// <summary>Gets or sets the created cycle value carried by the enclosing packet contract.</summary>
    public long CreatedCycle { get; set; }
    /// <summary>Gets or sets the injection cycle for workload-aware packet injection.</summary>
    public long InjectionCycle { get; set; }
    /// <summary>Gets or sets the delivered cycle value carried by the enclosing packet contract.</summary>
    public long? DeliveredCycle { get; set; }
    /// <summary>Gets or sets the arrival cycle for workload-aware packet delivery.</summary>
    public long? ArrivalCycle { get; set; }
    /// <summary>Gets or sets the visited components collection carried by the enclosing packet contract.</summary>
    public List<string> VisitedComponents { get; set; } = [];
    /// <summary>Gets or sets scalar or tensor payload values carried by the packet.</summary>
    public List<double> Values { get; set; } = [];
}

/// <summary>Represents a flit belonging to a packet.</summary>
public sealed class Flit
{
    /// <summary>Gets or sets the flit identifier.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the packet id containing this flit.</summary>
    public string PacketId { get; set; } = "";
    /// <summary>Gets or sets the zero-based flit index inside the packet.</summary>
    public int FlitIndex { get; set; }
    /// <summary>Gets or sets the total flit count for the packet.</summary>
    public int TotalFlits { get; set; }
    /// <summary>Gets or sets the payload bits carried by this flit.</summary>
    public int PayloadBits { get; set; }
    /// <summary>Gets or sets the legacy bits alias.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Bits
    {
        get => PayloadBits;
        set => PayloadBits = value;
    }
    /// <summary>Gets whether this is the first flit of the packet.</summary>
    public bool IsHead { get; set; }
    /// <summary>Gets whether this is the last flit of the packet.</summary>
    public bool IsTail { get; set; }
    /// <summary>Gets or sets the virtual channel id.</summary>
    public string VirtualChannel { get; set; } = "";
    /// <summary>Gets or sets route metadata for this flit.</summary>
    public string RouteMetadata { get; set; } = "";
    /// <summary>Gets or sets the creation cycle.</summary>
    public long CreationCycle { get; set; }
    /// <summary>Gets or sets flit metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
/// <summary>Represents trace event data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Type">Provides the type value carried by this contract.</param>
/// <param name="PacketId">Provides the packet id value carried by this contract.</param>
/// <param name="ComponentId">Provides the component id value carried by this contract.</param>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="Source">Provides the source value carried by this contract.</param>
/// <param name="Destination">Provides the destination value carried by this contract.</param>
/// <param name="Bits">Provides the bits value carried by this contract.</param>
/// <param name="Detail">Provides the detail value carried by this contract.</param>
/// <param name="FlitId">Provides the flit id value carried by the flit-level trace contract.</param>
/// <param name="FlitIndex">Provides the flit index value carried by the flit-level trace contract.</param>
/// <param name="TotalFlits">Provides the total flit count value carried by the flit-level trace contract.</param>
/// <param name="VirtualChannel">Provides the virtual channel value carried by the flit-level trace contract.</param>
/// <param name="InputPort">Provides the input port value carried by the flit-level trace contract.</param>
/// <param name="OutputPort">Provides the output port value carried by the flit-level trace contract.</param>
/// <param name="Route">Provides the route value carried by the flit-level trace contract.</param>
/// <param name="ArrivalCycle">Provides the arrival cycle value carried by the flit-level trace contract.</param>
/// <param name="StallReason">Provides the stall reason value carried by the flit-level trace contract.</param>
public sealed record TraceEvent(
    TraceEventType Type,
    string? PacketId = null,
    string? ComponentId = null,
    string? LinkId = null,
    string? Source = null,
    string? Destination = null,
    int Bits = 0,
    string? Detail = null,
    string? FlitId = null,
    int? FlitIndex = null,
    int? TotalFlits = null,
    string? VirtualChannel = null,
    string? InputPort = null,
    string? OutputPort = null,
    string? Route = null,
    long? ArrivalCycle = null,
    string? StallReason = null)
{
    /// <summary>Gets structured workload, packet, stall, and energy provenance when this is a critical event.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TraceProvenance? Provenance { get; init; }
}


/// <summary>Represents one audited phase entry in a cycle trace record.</summary>
/// <param name="Index">Provides the zero-based phase index.</param>
/// <param name="Name">Provides the stable phase name.</param>
public sealed record CyclePhaseTrace(int Index, string Name);
/// <summary>Represents cycle trace record data exchanged by hardware design and simulation workflows.</summary>
public sealed class CycleTraceRecord
{
    /// <summary>Gets or sets the cycle value carried by the enclosing cycle trace record contract.</summary>
    public long Cycle { get; set; }
    /// <summary>Gets or sets the audited phase order executed by the deterministic cycle kernel.</summary>
    public List<CyclePhaseTrace> Phases { get; set; } = [];
    /// <summary>Gets or sets the events collection carried by the enclosing cycle trace record contract.</summary>
    public List<TraceEvent> Events { get; set; } = [];
}

/// <summary>Represents simulation trace data exchanged by hardware design and simulation workflows.</summary>
public sealed class SimulationTrace
{
    /// <summary>Gets or sets the cycles collection carried by the enclosing simulation trace contract.</summary>
    public List<CycleTraceRecord> Cycles { get; set; } = [];
}

/// <summary>Represents component metrics data exchanged by hardware design and simulation workflows.</summary>
public sealed class ComponentMetrics
{
    /// <summary>Gets or sets the component id value carried by the enclosing component metrics contract.</summary>
    public string ComponentId { get; set; } = "";
    /// <summary>Gets or sets the active cycles value carried by the enclosing component metrics contract.</summary>
    public long ActiveCycles { get; set; }
    /// <summary>Gets or sets the idle cycles value carried by the enclosing component metrics contract.</summary>
    public long IdleCycles { get; set; }
    /// <summary>Gets or sets the stall cycles value carried by the enclosing component metrics contract.</summary>
    public long StallCycles { get; set; }
    /// <summary>Gets or sets the stall cycles by reason collection carried by the enclosing component metrics contract.</summary>
    public Dictionary<StallReason, long> StallCyclesByReason { get; set; } = new();
    /// <summary>Gets or sets the memory bank accesses collection carried by the enclosing component metrics contract.</summary>
    public Dictionary<string, long> MemoryBankAccesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the memory bank busy cycles collection carried by the enclosing component metrics contract.</summary>
    public Dictionary<string, long> MemoryBankBusyCycles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the input traffic bits value carried by the enclosing component metrics contract.</summary>
    public long InputTrafficBits { get; set; }
    /// <summary>Gets or sets the output traffic bits value carried by the enclosing component metrics contract.</summary>
    public long OutputTrafficBits { get; set; }
    /// <summary>Gets or sets the max queue length value carried by the enclosing component metrics contract.</summary>
    public int MaxQueueLength { get; set; }
    /// <summary>Gets or sets the memory used bits value carried by the enclosing component metrics contract.</summary>
    public long MemoryUsedBits { get; set; }
    /// <summary>Gets or sets the memory capacity bits value carried by the enclosing component metrics contract.</summary>
    public long MemoryCapacityBits { get; set; }
    /// <summary>Gets or sets the rejected memory write count value carried by the enclosing component metrics contract.</summary>
    public long MemoryRejectedWrites { get; set; }
    /// <summary>Gets or sets the current bit occupancy for bit-capacity components such as buffers and memories.</summary>
    public long OccupancyBits { get; set; }
    /// <summary>Gets or sets the average bit occupancy observed over sampled cycles.</summary>
    public double AverageOccupancyBits { get; set; }
    /// <summary>Gets or sets the peak bit occupancy observed during the run.</summary>
    public long PeakOccupancyBits { get; set; }
    /// <summary>Gets or sets the number of bits serviced by read paths.</summary>
    public long ReadBitsServiced { get; set; }
    /// <summary>Gets or sets the number of bits serviced by write paths.</summary>
    public long WriteBitsServiced { get; set; }
    /// <summary>Gets or sets the number of flits accepted by the component.</summary>
    public long FlitsAccepted { get; set; }
    /// <summary>Gets or sets the number of flits stalled by component-local flow control.</summary>
    public long FlitsStalled { get; set; }
    /// <summary>Gets or sets the number of memory requests issued by the component.</summary>
    public long MemoryRequestsIssued { get; set; }
    /// <summary>Gets or sets the number of memory requests completed by the component.</summary>
    public long MemoryRequestsCompleted { get; set; }
    /// <summary>Gets or sets the energy value carried by the enclosing component metrics contract.</summary>
    public double Energy { get; set; }
    /// <summary>Gets or sets the strongly typed energy breakdown.</summary>
    public EnergyBreakdown EnergyBreakdown { get; set; } = new();
    /// <summary>Gets or sets optional internal component energy breakdown by compiled template stage.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, double>? InternalEnergyBreakdown { get; set; }
    /// <summary>Gets or sets unit-bearing generic metrics emitted by exact component kernels.</summary>
    public Dictionary<string, NamedMetricAggregate> NamedMetrics { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the strongly typed physical area.</summary>
    public SquareMicrometers AreaUm2 { get; set; }
    /// <summary>Gets the strongly typed total classified cycle count.</summary>
    public CycleCount TotalCycleCount => new(ActiveCycles + IdleCycles + StallCycles);
    /// <summary>Gets the utilization value carried by the enclosing component metrics contract.</summary>
    public double Utilization => ActiveCycles + IdleCycles + StallCycles == 0
        ? 0
        : (double)ActiveCycles / (ActiveCycles + IdleCycles + StallCycles);
}

/// <summary>Represents link metrics data exchanged by hardware design and simulation workflows.</summary>
public sealed class LinkMetrics
{
    /// <summary>Gets or sets the link id value carried by the enclosing link metrics contract.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Gets or sets the number of packets transferred across this link.</summary>
    public long PacketsTransferred { get; set; }
    /// <summary>Gets or sets the total bits transferred value carried by the enclosing link metrics contract.</summary>
    public long TotalBitsTransferred { get; set; }
    /// <summary>Gets or sets the busy cycles value carried by the enclosing link metrics contract.</summary>
    public long BusyCycles { get; set; }
    /// <summary>Gets or sets the sampled total cycles value carried by the enclosing link metrics contract.</summary>
    public long TotalCycles { get; set; }
    /// <summary>Gets or sets the congestion cycles value carried by the enclosing link metrics contract.</summary>
    public long CongestionCycles { get; set; }
    /// <summary>Gets or sets the number of flits transferred across this link.</summary>
    public long FlitsTransferred { get; set; }
    /// <summary>Gets or sets the number of cycles where serialization was blocked by downstream backpressure.</summary>
    public long BackpressureCycles { get; set; }
    /// <summary>Gets or sets the number of bits emitted by the serializer.</summary>
    public long SerializationBitsSent { get; set; }
    /// <summary>Gets or sets the energy value carried by the enclosing link metrics contract.</summary>
    public double Energy { get; set; }
    /// <summary>Gets or sets the strongly typed energy breakdown.</summary>
    public EnergyBreakdown EnergyBreakdown { get; set; } = new();
    /// <summary>Gets the strongly typed active cycle count.</summary>
    public CycleCount ActiveCycleCount => new(BusyCycles);
    /// <summary>Gets the strongly typed sampled total cycle count.</summary>
    public CycleCount TotalCycleCount => new(TotalCycles);
    /// <summary>Returns the fraction of total cycles during which the link was busy.</summary>
    public double Utilization(long totalCycles) => totalCycles <= 0 ? 0 : (double)BusyCycles / totalCycles;
}

/// <summary>Represents global metrics data exchanged by hardware design and simulation workflows.</summary>
public sealed class GlobalMetrics
{
    /// <summary>Gets or sets the total cycles value carried by the enclosing global metrics contract.</summary>
    public long TotalCycles { get; set; }
    /// <summary>Gets or sets the packets injected value carried by the enclosing global metrics contract.</summary>
    public long PacketsInjected { get; set; }
    /// <summary>Gets or sets the packets delivered value carried by the enclosing global metrics contract.</summary>
    public long PacketsDelivered { get; set; }
    /// <summary>Gets or sets the flits injected value carried by the enclosing global metrics contract.</summary>
    public long FlitsInjected { get; set; }
    /// <summary>Gets or sets the flits delivered value carried by the enclosing global metrics contract.</summary>
    public long FlitsDelivered { get; set; }
    /// <summary>Gets or sets the total energy value carried by the enclosing global metrics contract.</summary>
    public double TotalEnergy { get; set; }
    /// <summary>Gets or sets the compute energy value carried by the enclosing global metrics contract.</summary>
    public double ComputeEnergy { get; set; }
    /// <summary>Gets or sets the no c energy value carried by the enclosing global metrics contract.</summary>
    public double NoCEnergy { get; set; }
    /// <summary>Gets or sets the conversion energy value carried by the enclosing global metrics contract.</summary>
    public double ConversionEnergy { get; set; }
    /// <summary>Gets or sets the optical energy value carried by the enclosing global metrics contract.</summary>
    public double OpticalEnergy { get; set; }
    /// <summary>Gets or sets the strongly typed energy categories.</summary>
    public EnergyCategoryBreakdown EnergyByCategory { get; set; } = new();
    /// <summary>Gets or sets component-qualified generic exact-kernel metrics.</summary>
    public Dictionary<string, NamedMetricAggregate> NamedMetrics { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the strongly typed total physical area.</summary>
    public SquareMicrometers TotalAreaUm2 { get; set; }
    /// <summary>Gets or sets the arithmetic mean of component utilizations.</summary>
    public double AverageUtilization { get; set; }
    /// <summary>Gets or sets utilization weighted by resolved physical component area.</summary>
    public double AreaWeightedUtilization { get; set; }
    /// <summary>Gets or sets active PE cycles divided by classified PE cycles.</summary>
    public double PeOnlyUtilization { get; set; }
    /// <summary>Gets or sets active Router cycles divided by classified Router cycles.</summary>
    public double RouterOnlyUtilization { get; set; }
    /// <summary>Gets the strongly typed total cycle count.</summary>
    public CycleCount TotalCycleCount => new(TotalCycles);
    /// <summary>Gets the average throughput packets per cycle value carried by the enclosing global metrics contract.</summary>
    public double AverageThroughputPacketsPerCycle => TotalCycles <= 0 ? 0 : (double)PacketsDelivered / TotalCycles;
}

/// <summary>Represents simulation metrics data exchanged by hardware design and simulation workflows.</summary>
public sealed class SimulationMetrics
{
    /// <summary>Gets or sets the global value carried by the enclosing simulation metrics contract.</summary>
    public GlobalMetrics Global { get; set; } = new();
    /// <summary>Gets or sets the components collection carried by the enclosing simulation metrics contract.</summary>
    public Dictionary<string, ComponentMetrics> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the links collection carried by the enclosing simulation metrics contract.</summary>
    public Dictionary<string, LinkMetrics> Links { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Represents a structured simulation diagnostic emitted during runtime.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Issue severity such as warning or error.</param>
/// <param name="Cycle">Cycle at which the issue was detected.</param>
/// <param name="ComponentId">Component associated with the issue.</param>
/// <param name="PacketId">Packet associated with the issue.</param>
/// <param name="RequestId">Memory request id associated with the issue.</param>
/// <param name="OperationType">Memory operation type associated with the issue.</param>
/// <param name="Message">Human-readable issue explanation.</param>
public sealed record SimulationIssue(
    string Code,
    string Severity,
    long Cycle,
    string ComponentId,
    string? PacketId,
    string? RequestId,
    string? OperationType,
    string Message);

/// <summary>Represents bottleneck report data exchanged by hardware design and simulation workflows.</summary>
public sealed class BottleneckReport
{
    /// <summary>Gets or sets the bottleneck analyzer schema version.</summary>
    public string AnalyzerSchemaVersion { get; set; } = "bottleneck-analyzer-1.0";
    /// <summary>Gets or sets the main bottleneck value carried by the enclosing bottleneck report contract.</summary>
    public string MainBottleneck { get; set; } = "No bottleneck detected.";
    /// <summary>Gets or sets the cause value carried by the enclosing bottleneck report contract.</summary>
    public string Cause { get; set; } = "Simulation completed without congestion evidence.";
    /// <summary>Gets or sets the suggested fix value carried by the enclosing bottleneck report contract.</summary>
    public string SuggestedFix { get; set; } = "No action required for this run.";
    /// <summary>Gets or sets the Top-3 structured bottleneck findings.</summary>
    public List<BottleneckFinding> TopFindings { get; set; } = [];
    /// <summary>Gets or sets the suggestions collection carried by the enclosing bottleneck report contract.</summary>
    public List<BottleneckSuggestion> Suggestions { get; set; } = [];
}

/// <summary>Represents one structured bottleneck finding emitted by the Phase 4 analyzer.</summary>
public sealed class BottleneckFinding
{
    /// <summary>Gets or sets the supported bottleneck type.</summary>
    public string Type { get; set; } = "";
    /// <summary>Gets or sets the component, link, or global location.</summary>
    public string Location { get; set; } = "";
    /// <summary>Gets or sets the concrete metric evidence for the finding.</summary>
    public string Evidence { get; set; } = "";
    /// <summary>Gets or sets the estimated bottleneck impact.</summary>
    public string Impact { get; set; } = "";
    /// <summary>Gets or sets confidence in the finding on the closed interval [0,1].</summary>
    public double Confidence { get; set; }
    /// <summary>Gets or sets the inferred cause backed by the evidence.</summary>
    public string Cause { get; set; } = "";
    /// <summary>Gets or sets the evidence-referenced suggested action.</summary>
    public string Suggestion { get; set; } = "";
}
/// <summary>Represents bottleneck suggestion data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Code">Provides the code value carried by this contract.</param>
/// <param name="TargetId">Provides the target id value carried by this contract.</param>
/// <param name="Evidence">Provides the evidence value carried by this contract.</param>
/// <param name="Recommendation">Provides the recommendation value carried by this contract.</param>
public sealed record BottleneckSuggestion(string Code, string TargetId, string Evidence, string Recommendation);

/// <summary>Represents simulation options data exchanged by hardware design and simulation workflows.</summary>
public sealed class SimulationOptions
{
    /// <summary>Gets or sets the max cycles value carried by the enclosing simulation options contract.</summary>
    public int MaxCycles { get; set; } = 1000;
    /// <summary>Gets or sets the default packet count value carried by the enclosing simulation options contract.</summary>
    public int DefaultPacketCount { get; set; } = 16;
    /// <summary>Gets or sets the default inject count value carried by the enclosing simulation options contract.</summary>
    public int DefaultInjectCount { get; set; } = 16;
    /// <summary>Gets or sets the default inject interval value carried by the enclosing simulation options contract.</summary>
    public int DefaultInjectInterval { get; set; } = 10;
    /// <summary>Gets or sets the default packet bits value carried by the enclosing simulation options contract.</summary>
    public int DefaultPacketBits { get; set; } = 128;
    /// <summary>Gets or sets the execution mode value carried by the enclosing simulation options contract.</summary>
    public SimulationExecutionMode ExecutionMode { get; set; } = SimulationExecutionMode.CycleAccurate;
    /// <summary>Gets or sets the deterministic seed recorded with cycle-kernel hash contracts.</summary>
    public int DeterministicSeed { get; set; } = 42;
    /// <summary>Gets or sets whether the cycle kernel retains full per-cycle trace records or only metrics.</summary>
    public SimulationCycleTraceMode CycleTraceMode { get; set; } = SimulationCycleTraceMode.Full;
}

/// <summary>Defines the supported simulation execution mode values used by hardware simulation contracts.</summary>
public enum SimulationExecutionMode
{
    /// <summary>Selects the cycle accurate value for the simulation execution mode contract.</summary>
    CycleAccurate,
    /// <summary>Selects the sparse event driven prototype value for the simulation execution mode contract.</summary>
    SparseEventDrivenPrototype
}

/// <summary>Defines trace-retention choices for the deterministic cycle kernel.</summary>
public enum SimulationCycleTraceMode
{
    /// <summary>Retains every cycle record and emits the canonical full-trace hash.</summary>
    Full,
    /// <summary>Retains runtime metrics and final packets without storing per-cycle records or claiming a full-trace hash.</summary>
    MetricsOnly
}

/// <summary>Represents simulation result data exchanged by hardware design and simulation workflows.</summary>
public sealed class SimulationResult
{
    /// <summary>Gets or sets the trace value carried by the enclosing simulation result contract.</summary>
    public SimulationTrace Trace { get; set; } = new();
    /// <summary>Gets or sets the metrics value carried by the enclosing simulation result contract.</summary>
    public SimulationMetrics Metrics { get; set; } = new();
    /// <summary>Gets or sets the bottleneck report value carried by the enclosing simulation result contract.</summary>
    public BottleneckReport BottleneckReport { get; set; } = new();
    /// <summary>Gets or sets structured runtime diagnostics such as capacity overflow errors.</summary>
    public List<SimulationIssue> Issues { get; set; } = [];
    /// <summary>Gets deterministic deep snapshots of packets accepted by workload sinks.</summary>
    public IReadOnlyList<Packet> DeliveredPackets { get; set; } = [];
    /// <summary>Gets or sets the canonical full-trace hash emitted by the deterministic cycle kernel.</summary>
    public CanonicalTraceHash? TraceHash { get; set; }
    /// <summary>Gets or sets the trace-retention mode used for this simulation result.</summary>
    public SimulationCycleTraceMode CycleTraceMode { get; set; } = SimulationCycleTraceMode.Full;
    /// <summary>Gets or sets whether the simulation reached a normal completion state.</summary>
    public bool Completed { get; set; }
    /// <summary>Gets or sets the completion reason value carried by the enclosing simulation result contract.</summary>
    public string CompletionReason { get; set; } = "";
}
