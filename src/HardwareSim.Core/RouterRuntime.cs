namespace HardwareSim.Core;

/// <summary>Defines deterministic router runtime settings for Phase 3C flit arbitration.</summary>
public sealed class RouterRuntimeConfig
{
    /// <summary>Gets or sets virtual channels per physical input port.</summary>
    public int VirtualChannels { get; set; } = ComponentDefaults.RouterVirtualChannels;
    /// <summary>Gets or sets each virtual channel FIFO depth in flits.</summary>
    public int VcDepthFlits { get; set; } = ComponentDefaults.RouterVcDepthFlits;
    /// <summary>Gets or sets the crossbar issue model.</summary>
    public CrossbarIssueModel CrossbarIssueModel { get; set; } = CrossbarIssueModel.PerOutputIssue;
    /// <summary>Gets or sets the default output lane count for FullCrossbar issue.</summary>
    public int OutputLaneCount { get; set; } = 1;
    /// <summary>Gets or sets the default output bit budget for FullCrossbar issue.</summary>
    public int OutputBandwidthBitsPerCycle { get; set; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
}

/// <summary>Represents the result of attempting to accept one flit into a router VC.</summary>
public sealed class RouterAcceptResult
{
    private RouterAcceptResult(bool accepted, string code, string message, string inputPort, int virtualChannel, string flitId)
    {
        Accepted = accepted;
        Code = code;
        Message = message;
        InputPort = inputPort;
        VirtualChannel = virtualChannel;
        FlitId = flitId;
    }

    /// <summary>Gets whether the flit was enqueued.</summary>
    public bool Accepted { get; }
    /// <summary>Gets the stable result or error code.</summary>
    public string Code { get; }
    /// <summary>Gets a human-readable result message.</summary>
    public string Message { get; }
    /// <summary>Gets the physical input port.</summary>
    public string InputPort { get; }
    /// <summary>Gets the selected virtual channel index.</summary>
    public int VirtualChannel { get; }
    /// <summary>Gets the flit id.</summary>
    public string FlitId { get; }

    /// <summary>Creates an accepted result.</summary>
    public static RouterAcceptResult Success(string inputPort, int virtualChannel, string flitId) => new(true, "Accepted", "Flit accepted into router VC.", inputPort, virtualChannel, flitId);
    /// <summary>Creates a failed result.</summary>
    public static RouterAcceptResult Failure(string code, string message, string inputPort, int virtualChannel, string flitId) => new(false, code, message, inputPort, virtualChannel, flitId);
}

/// <summary>Represents one router crossbar grant.</summary>
public sealed record RouterGrant(string InputPort, int VirtualChannel, string OutputPort, string FlitId, string PacketId, int Bits);

/// <summary>Represents one router stall without dequeuing the flit.</summary>
public sealed record RouterStall(string InputPort, int VirtualChannel, string OutputPort, string FlitId, string Reason);

/// <summary>Represents the deterministic grants and stalls from one router issue cycle.</summary>
public sealed class RouterIssueResult
{
    /// <summary>Gets granted flits in deterministic issue order.</summary>
    public List<RouterGrant> Grants { get; } = [];
    /// <summary>Gets stalled flits in deterministic order.</summary>
    public List<RouterStall> Stalls { get; } = [];
    /// <summary>Gets per input/VC occupancy after the issue cycle.</summary>
    public Dictionary<string, int> OccupancyByInputVc { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets arbitration, stall, and virtual-channel state trace events for this issue cycle.</summary>
    public List<TraceEvent> TraceEvents { get; } = [];
}

/// <summary>Provides a deterministic flit-level router runtime with input-port x VC queues.</summary>
public sealed class RouterRuntime
{
    private readonly RouterRuntimeConfig config;
    private readonly SortedDictionary<string, List<Queue<Flit>>> inputQueues = new(StringComparer.Ordinal);

    /// <summary>Initializes a router runtime with the supplied physical input port ids.</summary>
    public RouterRuntime(IEnumerable<string> inputPorts, RouterRuntimeConfig? config = null)
    {
        this.config = config ?? new RouterRuntimeConfig();
        if (this.config.VirtualChannels <= 0 || this.config.VcDepthFlits <= 0 || this.config.OutputLaneCount <= 0 || this.config.OutputBandwidthBitsPerCycle <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Router VC counts, depths, lanes, and bandwidth must be positive.");
        }

        foreach (var port in inputPorts.OrderBy(port => port, StringComparer.Ordinal))
        {
            inputQueues[port] = Enumerable.Range(0, this.config.VirtualChannels).Select(_ => new Queue<Flit>()).ToList();
        }
    }

    /// <summary>Gets per input/VC occupancy without mutating router state.</summary>
    public Dictionary<string, int> OccupancyByInputVc => SnapshotOccupancy();

    /// <summary>Attempts to enqueue a flit into the selected virtual channel.</summary>
    public RouterAcceptResult TryAccept(string inputPort, Flit flit, Packet? packet = null)
    {
        if (!inputQueues.TryGetValue(inputPort, out var vcs))
        {
            return RouterAcceptResult.Failure("VirtualChannelError", $"Unknown input port '{inputPort}'.", inputPort, -1, flit.Id);
        }

        var vc = SelectVirtualChannel(flit, packet);
        if (vc < 0 || vc >= config.VirtualChannels)
        {
            return RouterAcceptResult.Failure("VirtualChannelError", $"Virtual channel '{flit.VirtualChannel}' is outside configured range.", inputPort, vc, flit.Id);
        }
        if (vcs[vc].Count >= config.VcDepthFlits)
        {
            return RouterAcceptResult.Failure("OutputBufferFull", "Router virtual channel is full; upstream must hold the flit.", inputPort, vc, flit.Id);
        }

        vcs[vc].Enqueue(CloneFlit(flit));
        return RouterAcceptResult.Success(inputPort, vc, flit.Id);
    }

    /// <summary>Issues one deterministic crossbar cycle according to the configured issue model.</summary>
    public RouterIssueResult Issue(IReadOnlyDictionary<string, int>? outputLaneCapacity = null, IReadOnlyDictionary<string, int>? outputBitBudget = null)
    {
        var result = new RouterIssueResult();
        var requests = BuildRequests();
        var usedInputs = new HashSet<string>(StringComparer.Ordinal);
        var usedOutputs = new HashSet<string>(StringComparer.Ordinal);
        var lanes = new Dictionary<string, int>(StringComparer.Ordinal);
        var bits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            var output = request.OutputPort;
            var laneLimit = outputLaneCapacity?.GetValueOrDefault(output, config.OutputLaneCount) ?? config.OutputLaneCount;
            var bitLimit = outputBitBudget?.GetValueOrDefault(output, config.OutputBandwidthBitsPerCycle) ?? config.OutputBandwidthBitsPerCycle;
            var currentLanes = lanes.GetValueOrDefault(output);
            var currentBits = bits.GetValueOrDefault(output);
            var canGrant = config.CrossbarIssueModel switch
            {
                CrossbarIssueModel.SingleIssue => result.Grants.Count == 0 && currentLanes < 1 && request.Flit.PayloadBits <= bitLimit,
                CrossbarIssueModel.PerOutputIssue => !usedInputs.Contains(request.InputPort) && !usedOutputs.Contains(output) && request.Flit.PayloadBits <= bitLimit,
                CrossbarIssueModel.FullCrossbar => currentLanes < laneLimit && currentBits + request.Flit.PayloadBits <= bitLimit,
                _ => false
            };

            if (!canGrant)
            {
                var stallCode = TransportSemanticsContract.RouterConflictStallCode;
                result.Stalls.Add(new RouterStall(request.InputPort, request.VirtualChannel, output, request.Flit.Id, stallCode));
                result.TraceEvents.Add(new TraceEvent(
                    TraceEventType.Stall,
                    PacketId: request.Flit.PacketId,
                    ComponentId: "router",
                    Bits: request.Flit.PayloadBits,
                    Detail: $"crossbar_issue_model={config.CrossbarIssueModel};stall_code={stallCode};stall_reason={stallCode};input_port={request.InputPort};virtual_channel=vc{request.VirtualChannel};output_port={output};flit_id={request.Flit.Id}",
                    FlitId: request.Flit.Id,
                    FlitIndex: request.Flit.FlitIndex,
                    TotalFlits: request.Flit.TotalFlits,
                    VirtualChannel: $"vc{request.VirtualChannel}",
                    InputPort: request.InputPort,
                    OutputPort: output,
                    StallReason: stallCode));
                continue;
            }

            inputQueues[request.InputPort][request.VirtualChannel].Dequeue();
            result.Grants.Add(new RouterGrant(request.InputPort, request.VirtualChannel, output, request.Flit.Id, request.Flit.PacketId, request.Flit.PayloadBits));
            result.TraceEvents.Add(new TraceEvent(
                TraceEventType.Arbitration,
                PacketId: request.Flit.PacketId,
                ComponentId: "router",
                Bits: request.Flit.PayloadBits,
                Detail: $"crossbar_issue_model={config.CrossbarIssueModel};grant=true;input_port={request.InputPort};virtual_channel=vc{request.VirtualChannel};output_port={output};flit_id={request.Flit.Id}",
                FlitId: request.Flit.Id,
                FlitIndex: request.Flit.FlitIndex,
                TotalFlits: request.Flit.TotalFlits,
                VirtualChannel: $"vc{request.VirtualChannel}",
                InputPort: request.InputPort,
                OutputPort: output));
            usedInputs.Add(request.InputPort);
            usedOutputs.Add(output);
            lanes[output] = currentLanes + 1;
            bits[output] = currentBits + request.Flit.PayloadBits;
        }

        foreach (var pair in SnapshotOccupancy())
        {
            result.OccupancyByInputVc[pair.Key] = pair.Value;
            var parts = pair.Key.Split(':', 2);
            result.TraceEvents.Add(new TraceEvent(
                TraceEventType.VirtualChannel,
                ComponentId: "router",
                Detail: $"crossbar_issue_model={config.CrossbarIssueModel};input_vc={pair.Key};occupancy={pair.Value}",
                VirtualChannel: parts.Length == 2 ? parts[1] : pair.Key,
                InputPort: parts[0]));
        }
        return result;
    }

    /// <summary>Resolves a head flit's output port using routing table priority and deterministic XY fallback.</summary>
    public static string ResolveOutputPort(
        Packet packet,
        GridPosition routerPosition,
        GridPosition destinationPosition,
        IReadOnlyDictionary<string, string>? routingTable = null)
    {
        if (routingTable is not null && routingTable.TryGetValue(packet.DestinationComponentId, out var tablePort) && !string.IsNullOrWhiteSpace(tablePort))
        {
            return tablePort;
        }
        if (destinationPosition.X != routerPosition.X)
        {
            return destinationPosition.X > routerPosition.X ? "east" : "west";
        }
        if (destinationPosition.Y != routerPosition.Y)
        {
            return destinationPosition.Y > routerPosition.Y ? "south" : "north";
        }
        return "local";
    }

    private IReadOnlyList<RouterRequest> BuildRequests()
    {
        var requests = new List<RouterRequest>();
        foreach (var input in inputQueues)
        {
            for (var vc = 0; vc < input.Value.Count; vc++)
            {
                if (input.Value[vc].Count == 0)
                {
                    continue;
                }
                var flit = input.Value[vc].Peek();
                var output = string.IsNullOrWhiteSpace(flit.RouteMetadata) ? "local" : flit.RouteMetadata;
                requests.Add(new RouterRequest(input.Key, vc, output, flit));
            }
        }

        return requests
            .OrderBy(request => request.OutputPort, StringComparer.Ordinal)
            .ThenBy(request => request.InputPort, StringComparer.Ordinal)
            .ThenBy(request => request.VirtualChannel)
            .ThenBy(request => request.Flit.Id, StringComparer.Ordinal)
            .ToList();
    }

    private Dictionary<string, int> SnapshotOccupancy()
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var input in inputQueues)
        {
            for (var vc = 0; vc < input.Value.Count; vc++)
            {
                snapshot[$"{input.Key}:vc{vc}"] = input.Value[vc].Count;
            }
        }
        return snapshot;
    }

    private int SelectVirtualChannel(Flit flit, Packet? packet)
    {
        if (!string.IsNullOrWhiteSpace(flit.VirtualChannel))
        {
            var raw = flit.VirtualChannel.StartsWith("vc", StringComparison.OrdinalIgnoreCase)
                ? flit.VirtualChannel[2..]
                : flit.VirtualChannel;
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explicitVc) ? explicitVc : -1;
        }

        var packetType = packet?.PacketType ?? PacketType.Activation;
        return packetType is PacketType.Control or PacketType.Config or PacketType.Status or PacketType.MemoryReadResponse ? Math.Min(1, config.VirtualChannels - 1) : 0;
    }

    private static Flit CloneFlit(Flit flit) => new()
    {
        Id = flit.Id,
        PacketId = flit.PacketId,
        FlitIndex = flit.FlitIndex,
        TotalFlits = flit.TotalFlits,
        PayloadBits = flit.PayloadBits,
        IsHead = flit.IsHead,
        IsTail = flit.IsTail,
        VirtualChannel = flit.VirtualChannel,
        RouteMetadata = flit.RouteMetadata,
        CreationCycle = flit.CreationCycle,
        Metadata = new Dictionary<string, string>(flit.Metadata, StringComparer.OrdinalIgnoreCase)
    };

    private sealed record RouterRequest(string InputPort, int VirtualChannel, string OutputPort, Flit Flit);
}
