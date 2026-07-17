namespace HardwareSim.Core;

/// <summary>Provides the cycle simulation engine service for hardware design and simulation workflows.</summary>
public sealed partial class CycleSimulationEngine
{
    private readonly ComponentRuntimeKernelRegistrySnapshot? runtimeKernelRegistry;

    /// <summary>Creates an engine with an optional frozen registry for components carrying execution contracts.</summary>
    public CycleSimulationEngine(ComponentRuntimeKernelRegistrySnapshot? runtimeKernelRegistry = null)
    {
        this.runtimeKernelRegistry = runtimeKernelRegistry;
    }
    private sealed record FlitEnvelope(Packet Packet, Flit Flit);
    private sealed record FlitFlight(Packet Packet, Flit Flit, SimLinkDef Link, long ArrivalCycle);
    private enum RouterInputPort { North = 0, South = 1, East = 2, West = 3, Local = 4 }
    private sealed class ProcessingItem
    {
        public ProcessingItem(
            Packet packet,
            long readyCycle,
            MemoryOperationType memoryOperation = MemoryOperationType.None,
            string memoryBankId = "")
        {
            Packet = packet;
            ReadyCycle = readyCycle;
            MemoryOperation = memoryOperation;
            MemoryBankId = memoryBankId;
        }

        public Packet Packet { get; }
        public long ReadyCycle { get; }
        public MemoryOperationType MemoryOperation { get; }
        public string MemoryBankId { get; }
        public bool MemoryWriteApplied { get; set; }
        public bool MemoryOperationCompleted { get; set; }
        public bool RuntimeApplied { get; set; }
    }
    private sealed class RouterState
    {
        public RouterState(int virtualChannelCount = ComponentDefaults.RouterVirtualChannels)
        {
            VirtualChannelCount = Math.Max(1, virtualChannelCount);
            InputVcs = Enum
                .GetValues(typeof(RouterInputPort))
                .Cast<RouterInputPort>()
                .ToDictionary(
                    port => port,
                    _ => Enumerable.Range(0, VirtualChannelCount)
                        .Select(_ => new Queue<FlitEnvelope>())
                        .ToList());
        }

        public int VirtualChannelCount { get; }
        public Dictionary<RouterInputPort, List<Queue<FlitEnvelope>>> InputVcs { get; }
        public int RoundRobinCursor { get; set; }
    }

    private sealed class ReductionUnitState
    {
        public List<Packet> Inputs { get; } = [];
    }

    private sealed class ScheduledPacketRelease
    {
        public ScheduledOperation Operation { get; init; } = new();
        public int RemainingPackets { get; set; }
    }

    /// <summary>Runs the enclosing simulation engine with the supplied graph and options, returning deterministic trace and metrics data.</summary>
    public SimulationResult Run(
        HardwareSimulationGraph graph,
        SimulationOptions? options = null,
        ProjectDirtyState? dirtyState = null)
    {
        options ??= new SimulationOptions();
        var executionModeIssue = ValidateExecutionMode(options);
        return executionModeIssue is not null
            ? FailureFromIssue(executionModeIssue)
            : RunDeterministicInternal(graph, schedule: null, options, dirtyState, runtimeKernelRegistry);
    }

    /// <summary>Runs an executable simulation graph after validating packet/flit consistency.</summary>
    public SimulationResult Run(
        ExecutableSimulationGraph executable,
        SimulationOptions? options = null,
        ProjectDirtyState? dirtyState = null)
    {
        if (executable is null)
        {
            throw new ArgumentNullException(nameof(executable));
        }

        options ??= new SimulationOptions();
        var executionModeIssue = ValidateExecutionMode(options);
        if (executionModeIssue is not null)
        {
            return FailureFromIssue(executionModeIssue);
        }

        var validationIssue = ValidateExecutableFlits(executable);
        if (validationIssue is not null)
        {
            return FailureFromIssue(validationIssue);
        }

        return RunExecutableDeterministic(executable, options, dirtyState);
    }

    /// <summary>Runs the enclosing simulation engine with the supplied graph and options, returning deterministic trace and metrics data.</summary>
    public SimulationResult Run(
        HardwareSimulationGraph graph,
        WorkloadSchedule schedule,
        SimulationOptions? options = null,
        ProjectDirtyState? dirtyState = null)
    {
        options ??= new SimulationOptions();
        var executionModeIssue = ValidateExecutionMode(options);
        return executionModeIssue is not null
            ? FailureFromIssue(executionModeIssue)
            : RunDeterministicInternal(graph, schedule, options, dirtyState, runtimeKernelRegistry);
    }

    private static SimulationIssue? ValidateExecutionMode(SimulationOptions options)
    {
        return options.ExecutionMode == SimulationExecutionMode.CycleAccurate
            ? null
            : new SimulationIssue(
                "UnsupportedExecutionMode",
                "error",
                0,
                "simulation",
                null,
                null,
                null,
                $"Simulation execution mode '{options.ExecutionMode}' is not implemented; use {SimulationExecutionMode.CycleAccurate}.");
    }

    private static SimulationResult FailureFromIssue(SimulationIssue issue) => new()
    {
        Completed = false,
        CompletionReason = issue.Message,
        Issues = [issue],
        Trace = new SimulationTrace
        {
            Cycles =
            [
                new CycleTraceRecord
                {
                    Cycle = issue.Cycle,
                    Events =
                    [
                        new TraceEvent(
                            TraceEventType.Error,
                            PacketId: issue.PacketId,
                            ComponentId: issue.ComponentId,
                            Detail: $"code={issue.Code};severity={issue.Severity};{issue.Message}")
                    ]
                }
            ]
        }
    };

    private static SimulationIssue? ValidateExecutableFlits(ExecutableSimulationGraph executable)
    {
        var exactOperandIssue = ValidateExecutableExactOperands(executable);
        if (exactOperandIssue is not null) return exactOperandIssue;

        if (!Enum.IsDefined(typeof(PacketizationMode), executable.PacketizationMode))
        {
            return new SimulationIssue(
                "UnsupportedPacketizationMode",
                "error",
                0,
                "executable",
                null,
                null,
                null,
                $"Packetization mode '{executable.PacketizationMode}' is not supported.");
        }

        if (executable.PacketizationMode != PacketizationMode.FlitLevelMode && executable.InitialFlits.Count == 0)
        {
            return null;
        }

        var catalog = new PacketCatalog();
        foreach (var packet in executable.InitialPackets.OrderBy(packet => packet.Id, StringComparer.Ordinal))
        {
            catalog.Add(packet);
        }
        var assembler = new PacketAssembler(catalog);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flit in executable.InitialFlits.OrderBy(flit => flit.PacketId, StringComparer.Ordinal).ThenBy(flit => flit.FlitIndex))
        {
            var result = assembler.Accept(flit);
            if (!result.IsSuccess)
            {
                return new SimulationIssue("FlitSequenceError", "error", 0, flit.Metadata.GetValueOrDefault("component_id", "executable"), flit.PacketId, null, null, result.Message);
            }
            if (result.IsComplete)
            {
                completed.Add(result.PacketId);
            }
        }

        foreach (var packet in executable.InitialPackets)
        {
            if (!completed.Contains(packet.Id))
            {
                return new SimulationIssue("FlitSequenceError", "error", 0, packet.SourceComponentId, packet.Id, null, null, $"Packet '{packet.Id}' did not have a complete flit sequence.");
            }
        }
        return null;
    }
    private static Packet CreateReductionOutput(
        SimComponentDef reduction,
        IReadOnlyList<Packet> inputs,
        long cycle,
        out double sum)
    {
        sum = inputs.Sum(PacketNumericValue);
        var first = inputs[0];
        return new Packet
        {
            Id = $"{reduction.Id}_psum_{first.Id}",
            Bits = inputs.Max(packet => packet.Bits),
            PacketType = PacketType.Activation,
            RequestId = string.IsNullOrWhiteSpace(first.RequestId) ? $"{reduction.Id}_psum" : $"{reduction.Id}_{first.RequestId}_psum",
            MemoryOperation = MemoryOperationType.None,
            SourceComponentId = reduction.Id,
            DestinationComponentId = first.DestinationComponentId,
            CurrentComponentId = reduction.Id,
            CreatedCycle = cycle,
            VisitedComponents = first.VisitedComponents.Concat([reduction.Id]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Values = [sum]
        };
    }

    private static bool TryEnqueueRouterInputFlits(
        HardwareSimulationGraph graph,
        SimComponentDef router,
        SimLinkDef incomingLink,
        Packet packet,
        IReadOnlyList<Flit> flits,
        RouterState state,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        var inputPort = ClassifyRouterInputPort(graph, router, incomingLink);
        var legacyBufferDepth = RouterLegacyBufferDepth(router);
        var bufferDepth = RouterVcDepthFlits(router, Math.Max(1, flits.Count), legacyBufferDepth);
        var staged = new List<(int VirtualChannel, Flit Flit)>();
        foreach (var flit in flits.OrderBy(flit => flit.FlitIndex))
        {
            var virtualChannel = SelectRouterVirtualChannel(packet, flit, state.VirtualChannelCount);
            if (virtualChannel < 0 || virtualChannel >= state.VirtualChannelCount)
            {
                RecordStall(metrics, router.Id, StallReason.OutputBufferFull);
                record.Events.Add(new(
                    TraceEventType.Stall,
                    PacketId: packet.Id,
                    ComponentId: router.Id,
                    LinkId: incomingLink.Id,
                    Source: incomingLink.Source.ComponentId,
                    Destination: router.Id,
                    Bits: packet.Bits,
                    Detail: $"VirtualChannelError;stall_reason={StallReason.OutputBufferFull};buffer=router_input;input_port={RouterPortName(inputPort)};virtual_channel={flit.VirtualChannel}"));
                return false;
            }

            var queue = state.InputVcs[inputPort][virtualChannel];
            var stagedForVc = staged.Count(item => item.VirtualChannel == virtualChannel);
            if (queue.Count + stagedForVc >= bufferDepth)
            {
                RecordStall(metrics, router.Id, StallReason.OutputBufferFull);
                metrics.Components[router.Id].FlitsStalled++;
                record.Events.Add(new(
                    TraceEventType.Stall,
                    PacketId: packet.Id,
                    ComponentId: router.Id,
                    LinkId: incomingLink.Id,
                    Source: incomingLink.Source.ComponentId,
                    Destination: router.Id,
                    Bits: packet.Bits,
                    Detail: $"{StallReason.OutputBufferFull};stall_reason={StallReason.OutputBufferFull};buffer=router_input;input_port={RouterPortName(inputPort)};buffer_depth={legacyBufferDepth}"));
                return false;
            }

            staged.Add((virtualChannel, flit));
        }

        foreach (var item in staged)
        {
            state.InputVcs[inputPort][item.VirtualChannel].Enqueue(new FlitEnvelope(ClonePacket(packet), CloneFlit(item.Flit)));
            metrics.Components[router.Id].FlitsAccepted++;
        }

        var occupancy = state.InputVcs[inputPort].Sum(queue => queue.Count);
        metrics.Components[router.Id].MaxQueueLength = Math.Max(metrics.Components[router.Id].MaxQueueLength, occupancy);
        record.Events.Add(new(
            TraceEventType.BufferOccupancy,
            PacketId: packet.Id,
            ComponentId: router.Id,
            LinkId: incomingLink.Id,
            Bits: packet.Bits,
            Detail: $"buffer=router_input;input_port={RouterPortName(inputPort)};occupancy={occupancy};buffer_depth={legacyBufferDepth}"));
        return true;
    }

    private static RouterInputPort ClassifyRouterInputPort(
        HardwareSimulationGraph graph,
        SimComponentDef router,
        SimLinkDef incomingLink)
    {
        var portName = incomingLink.Destination.PortName;
        if (portName.Contains("north", StringComparison.OrdinalIgnoreCase)) return RouterInputPort.North;
        if (portName.Contains("south", StringComparison.OrdinalIgnoreCase)) return RouterInputPort.South;
        if (portName.Contains("east", StringComparison.OrdinalIgnoreCase)) return RouterInputPort.East;
        if (portName.Contains("west", StringComparison.OrdinalIgnoreCase)) return RouterInputPort.West;
        if (portName.Contains("local", StringComparison.OrdinalIgnoreCase)) return RouterInputPort.Local;

        var source = graph.FindComponent(incomingLink.Source.ComponentId);
        if (source is null)
        {
            return RouterInputPort.Local;
        }

        var dx = source.Position.X - router.Position.X;
        var dy = source.Position.Y - router.Position.Y;
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return dx < 0 ? RouterInputPort.West : RouterInputPort.East;
        }

        if (dy < 0) return RouterInputPort.North;
        if (dy > 0) return RouterInputPort.South;
        if (dx < 0) return RouterInputPort.West;
        if (dx > 0) return RouterInputPort.East;
        return RouterInputPort.Local;
    }

    private static int RouterVirtualChannels(SimComponentDef router) =>
        Math.Max(1, router.GetIntParameter("virtual_channels", ComponentDefaults.RouterVirtualChannels));

    private static int RouterLegacyBufferDepth(SimComponentDef router) =>
        Math.Max(1, router.GetIntParameter("buffer_depth", router.GetIntParameter("queue_capacity", 16)));

    private static int RouterVcDepthFlits(SimComponentDef router, int flitsPerPacket = 1, int? legacyBufferDepth = null)
    {
        if (router.Parameters.TryGetValue("vc_depth_flits", out var rawDepth) &&
            int.TryParse(rawDepth, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explicitDepth))
        {
            return Math.Max(1, explicitDepth);
        }

        return Math.Max(1, (legacyBufferDepth ?? RouterLegacyBufferDepth(router)) * Math.Max(1, flitsPerPacket));
    }

    private static int SelectRouterVirtualChannel(Packet packet, Flit flit, int virtualChannelCount)
    {
        if (!string.IsNullOrWhiteSpace(flit.VirtualChannel))
        {
            var raw = flit.VirtualChannel.StartsWith("vc", StringComparison.OrdinalIgnoreCase)
                ? flit.VirtualChannel[2..]
                : flit.VirtualChannel;
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explicitVc) ? explicitVc : -1;
        }

        return packet.PacketType is PacketType.Control or PacketType.Config or PacketType.Status or PacketType.MemoryReadResponse
            ? Math.Min(1, virtualChannelCount - 1)
            : 0;
    }

    private static string RouterPortName(RouterInputPort port) => port.ToString().ToLowerInvariant();

    private static bool RouterStatesEmpty(IReadOnlyDictionary<string, RouterState> routerStates) =>
        routerStates.Values.All(RouterStateEmpty);

    private static bool RouterStateEmpty(RouterState state) =>
        state.InputVcs.Values.All(queues => queues.All(queue => queue.Count == 0));

    private static bool ReductionStatesEmpty(IReadOnlyDictionary<string, ReductionUnitState> reductionStates) =>
        reductionStates.Values.All(state => state.Inputs.Count == 0);

    private static bool TryFindBusyOpticalChannel(
        SimLinkDef nextLink,
        Packet packet,
        IReadOnlyList<FlitFlight> inFlight,
        out string busyLinkId,
        out string channel)
    {
        busyLinkId = "";
        channel = "";
        if (nextLink.OpticalProfile is not null && packet.OpticalState is not null)
        {
            var requestedResources = OpticalWavelengthResourceIds(nextLink, packet.OpticalState);
            channel = packet.OpticalState.ChannelId + "@" +
                packet.OpticalState.Wavelength.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "nm";
            var typedBusy = inFlight.FirstOrDefault(flight =>
                flight.Link.OpticalProfile is not null &&
                flight.Packet.OpticalState is not null &&
                requestedResources.Overlaps(OpticalWavelengthResourceIds(flight.Link, flight.Packet.OpticalState)));
            if (typedBusy is null) return false;
            busyLinkId = typedBusy.Link.Id;
            return true;
        }

        if (!string.Equals(nextLink.RouteType, "optical", StringComparison.OrdinalIgnoreCase) ||
            !nextLink.Parameters.TryGetValue("optical_channel", out var nextChannel) ||
            string.IsNullOrWhiteSpace(nextChannel))
        {
            return false;
        }

        channel = nextChannel;
        var busy = inFlight.FirstOrDefault(t =>
            string.Equals(t.Link.RouteType, "optical", StringComparison.OrdinalIgnoreCase) &&
            t.Link.Parameters.TryGetValue("optical_channel", out var busyChannel) &&
            string.Equals(busyChannel, nextChannel, StringComparison.OrdinalIgnoreCase));
        if (busy is null)
        {
            return false;
        }

        busyLinkId = busy.Link.Id;
        return true;
    }

    private static HashSet<string> OpticalWavelengthResourceIds(SimLinkDef link, OpticalPacketState state)
    {
        const double cellSizeMicrometers = 100.0;
        var route = link.OpticalProfile?.Route
            ?? throw new InvalidOperationException("Typed wavelength resources require an optical route profile.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        var wavelength = state.Wavelength.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 1; index < route.Path.Count; index++)
        {
            var a = route.Path[index - 1];
            var b = route.Path[index];
            var ax = (int)Math.Floor(a.X / cellSizeMicrometers);
            var ay = (int)Math.Floor(a.Y / cellSizeMicrometers);
            var bx = (int)Math.Floor(b.X / cellSizeMicrometers);
            var by = (int)Math.Floor(b.Y / cellSizeMicrometers);
            var xStep = Math.Sign(bx - ax);
            var yStep = Math.Sign(by - ay);
            var x = ax;
            var y = ay;
            while (x != bx)
            {
                var nextX = x + xStep;
                var direction = xStep > 0 ? RouteResourceDirection.East : RouteResourceDirection.West;
                result.Add(OpticalWavelengthEdgeId(x, y, nextX, y, direction, route, state.ChannelId, wavelength));
                x = nextX;
            }
            while (y != by)
            {
                var nextY = y + yStep;
                var direction = yStep > 0 ? RouteResourceDirection.North : RouteResourceDirection.South;
                result.Add(OpticalWavelengthEdgeId(x, y, x, nextY, direction, route, state.ChannelId, wavelength));
                y = nextY;
            }
        }

        if (result.Count == 0)
        {
            result.Add("route=" + route.RouteHash + "|layer=" + route.LayerId + "|medium=" + route.Medium +
                "|channel=" + state.ChannelId + "|wavelength_nm=" + wavelength);
        }
        return result;
    }

    private static string OpticalWavelengthEdgeId(
        int ax,
        int ay,
        int bx,
        int by,
        RouteResourceDirection direction,
        OpticalRouteSnapshot route,
        string channel,
        string wavelength)
    {
        var startX = Math.Min(ax, bx);
        var endX = Math.Max(ax, bx);
        var startY = Math.Min(ay, by);
        var endY = Math.Max(ay, by);
        return startX.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
            startY.ToString(System.Globalization.CultureInfo.InvariantCulture) + "->" +
            endX.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
            endY.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "|direction=" + direction + "|layer=" + route.LayerId + "|medium=" + route.Medium +
            "|channel=" + channel + "|wavelength_nm=" + wavelength;
    }

    private static Packet SelectPacketForSend(
        SimComponentDef component,
        Queue<Packet> queue,
        SimLinkDef nextLink,
        Dictionary<string, int> routerArbitrationCursors,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        if (component.Type != ComponentKind.Router || queue.Count <= 1)
        {
            return queue.Dequeue();
        }

        var packets = queue.ToList();
        var policy = component.Parameters.TryGetValue("arbitration_policy", out var rawPolicy)
            ? rawPolicy
            : "round_robin";
        var selectedIndex = string.Equals(policy, "round_robin", StringComparison.OrdinalIgnoreCase)
            ? Math.Abs(routerArbitrationCursors.GetValueOrDefault(component.Id, 0)) % packets.Count
            : 0;
        var selected = packets[selectedIndex];

        queue.Clear();
        for (var i = 0; i < packets.Count; i++)
        {
            if (i == selectedIndex)
            {
                continue;
            }

            queue.Enqueue(packets[i]);
            RecordStall(metrics, component.Id, StallReason.RouterConflict);
            metrics.Links[nextLink.Id].CongestionCycles++;
            record.Events.Add(new(
                TraceEventType.Stall,
                PacketId: packets[i].Id,
                ComponentId: component.Id,
                LinkId: nextLink.Id,
                Bits: packets[i].Bits,
                Detail: $"{StallReason.RouterConflict}:policy={policy};winner={selected.Id}"));
        }

        routerArbitrationCursors[component.Id] = selectedIndex >= packets.Count - 1 ? 0 : selectedIndex + 1;
        return selected;
    }

    private static bool TryEnqueueOutputPacket(
        HardwareSimulationGraph graph,
        string componentId,
        Packet packet,
        Dictionary<string, Queue<Packet>> outputQueues,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        string? linkId = null)
    {
        var component = graph.FindComponent(componentId)!;
        var queue = outputQueues[componentId];
        if (queue.Count >= QueueCapacity(component))
        {
            RecordStall(metrics, componentId, StallReason.OutputBufferFull);
            record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: componentId, LinkId: linkId,
                Bits: packet.Bits, Detail: $"{StallReason.OutputBufferFull};stall_reason={StallReason.OutputBufferFull};queue_capacity={QueueCapacity(component)}"));
            return false;
        }

        queue.Enqueue(packet);
        metrics.Components[componentId].MaxQueueLength = Math.Max(metrics.Components[componentId].MaxQueueLength, queue.Count);
        return true;
    }

    private static void RecordStall(SimulationMetrics metrics, string componentId, StallReason reason)
    {
        var componentMetrics = metrics.Components[componentId];
        componentMetrics.StallCycles++;
        componentMetrics.StallCyclesByReason[reason] = componentMetrics.StallCyclesByReason.GetValueOrDefault(reason) + 1;
    }

    private static void AcceptPacketAtDestination(
        Packet packet,
        SimLinkDef link,
        SimComponentDef destination,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        packet.CurrentComponentId = destination.Id;
        packet.DestinationPort = link.Destination.PortName;
        packet.VisitedComponents.Add(destination.Id);
        metrics.Components[destination.Id].InputTrafficBits += packet.Bits;
        record.Events.Add(new(TraceEventType.PacketMove, PacketId: packet.Id, ComponentId: destination.Id,
            LinkId: link.Id, Source: link.Source.ComponentId, Destination: destination.Id, Bits: packet.Bits,
            Detail: PacketTraceIdentity.TraceDetailOrNull(packet)));
    }

    private static SimLinkDef? FindNextLink(
        HardwareSimulationGraph graph,
        string startComponentId,
        string sinkId,
        Dictionary<string, int> routeChoiceCursors,
        IReadOnlyList<FlitFlight> inFlight,
        SimulationMetrics metrics,
        out string routingDetail,
        string? requiredSourcePort = null)
    {
        routingDetail = "";
        var component = graph.FindComponent(startComponentId);
        var outgoingLinks = graph.Links
            .Where(l =>
                string.Equals(l.Source.ComponentId, startComponentId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(requiredSourcePort) || string.Equals(l.Source.PortName, requiredSourcePort, StringComparison.Ordinal)))
            .OrderBy(l => l.Source.PortName, StringComparer.Ordinal)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .ToList();
        if (component is not null && component.Parameters.TryGetValue("routing_policy", out var policy) &&
            !string.Equals(policy, "xy", StringComparison.OrdinalIgnoreCase))
        {
            var routableLinks = outgoingLinks
                .Where(l => CanReachSink(graph, l.Destination.ComponentId, sinkId))
                .ToList();

            if (routableLinks.Count > 1 && string.Equals(policy, "round_robin", StringComparison.OrdinalIgnoreCase))
            {
                var selectedIndex = Math.Abs(routeChoiceCursors.GetValueOrDefault(startComponentId, 0)) % routableLinks.Count;
                routeChoiceCursors[startComponentId] = selectedIndex >= routableLinks.Count - 1 ? 0 : selectedIndex + 1;
                routingDetail = $"routing=round_robin;selected={routableLinks[selectedIndex].Id}";
                return routableLinks[selectedIndex];
            }

            if (routableLinks.Count > 1 && string.Equals(policy, "adaptive_least_busy", StringComparison.OrdinalIgnoreCase))
            {
                var selected = routableLinks
                    .Select((l, index) => new
                    {
                        Link = l,
                        Index = index,
                        InFlight = inFlight.Count(t => t.Link.Id == l.Id),
                        Congestion = metrics.Links.TryGetValue(l.Id, out var linkMetrics) ? linkMetrics.CongestionCycles : 0
                    })
                    .OrderBy(x => x.InFlight)
                    .ThenBy(x => x.Congestion)
                    .ThenBy(x => x.Index)
                    .First();
                routingDetail = $"routing=adaptive_least_busy;selected={selected.Link.Id};in_flight={selected.InFlight};congestion={selected.Congestion}";
                return selected.Link;
            }
        }

        if (component is not null && TryFindXyNextLink(graph, component, sinkId, outgoingLinks, out var xyLink, out routingDetail))
        {
            return xyLink;
        }

        if (outgoingLinks.Count == 1)
        {
            routingDetail = $"routing=direct;selected={outgoingLinks[0].Id}";
            return outgoingLinks[0];
        }

        return null;
    }

    private static bool TryFindXyNextLink(
        HardwareSimulationGraph graph,
        SimComponentDef component,
        string sinkId,
        IReadOnlyList<SimLinkDef> outgoingLinks,
        out SimLinkDef? selectedLink,
        out string routingDetail)
    {
        selectedLink = null;
        routingDetail = "";
        if (outgoingLinks.Count == 0)
        {
            return false;
        }

        var sink = graph.FindComponent(sinkId);
        if (sink is null)
        {
            return false;
        }

        var current = component.Position;
        var target = sink.Position;
        if (current.X != target.X)
        {
            var direction = target.X > current.X ? "east" : "west";
            var sign = target.X > current.X ? 1 : -1;
            selectedLink = ChooseXyCandidate(graph, outgoingLinks, current, target, candidate =>
            {
                var destination = graph.FindComponent(candidate.Destination.ComponentId);
                return destination is not null && Math.Sign(destination.Position.X - current.X) == sign;
            });
            if (selectedLink is not null)
            {
                routingDetail = $"routing=xy;axis=x;direction={direction};selected={selectedLink.Id};from={current.X},{current.Y};target={target.X},{target.Y}";
                return true;
            }
        }

        if (current.Y != target.Y)
        {
            var direction = target.Y > current.Y ? "south" : "north";
            var sign = target.Y > current.Y ? 1 : -1;
            selectedLink = ChooseXyCandidate(graph, outgoingLinks, current, target, candidate =>
            {
                var destination = graph.FindComponent(candidate.Destination.ComponentId);
                return destination is not null && Math.Sign(destination.Position.Y - current.Y) == sign;
            });
            if (selectedLink is not null)
            {
                routingDetail = $"routing=xy;axis=y;direction={direction};selected={selectedLink.Id};from={current.X},{current.Y};target={target.X},{target.Y}";
                return true;
            }
        }

        selectedLink = outgoingLinks
            .Where(link => string.Equals(link.Destination.ComponentId, sinkId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(link => link.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (selectedLink is not null)
        {
            routingDetail = $"routing=xy;axis=local;direction=local;selected={selectedLink.Id};from={current.X},{current.Y};target={target.X},{target.Y}";
            return true;
        }

        if (outgoingLinks.Count == 1)
        {
            selectedLink = outgoingLinks[0];
            routingDetail = $"routing=xy;axis=direct;direction=direct;selected={selectedLink.Id};from={current.X},{current.Y};target={target.X},{target.Y}";
            return true;
        }

        return false;
    }

    private static SimLinkDef? ChooseXyCandidate(
        HardwareSimulationGraph graph,
        IEnumerable<SimLinkDef> outgoingLinks,
        GridPosition current,
        GridPosition target,
        Func<SimLinkDef, bool> predicate) =>
        outgoingLinks
            .Where(predicate)
            .Select(link => new
            {
                Link = link,
                Destination = graph.FindComponent(link.Destination.ComponentId)
            })
            .Where(item => item.Destination is not null)
            .OrderBy(item => Math.Abs(target.X - item.Destination!.Position.X) + Math.Abs(target.Y - item.Destination.Position.Y))
            .ThenBy(item => Math.Abs(item.Destination!.Position.X - current.X) + Math.Abs(item.Destination.Position.Y - current.Y))
            .ThenBy(item => item.Link.Source.PortName, StringComparer.Ordinal)
            .ThenBy(item => item.Link.Id, StringComparer.Ordinal)
            .Select(item => item.Link)
            .FirstOrDefault();

    private static bool CanReachSink(HardwareSimulationGraph graph, string startComponentId, string sinkId)
    {
        if (string.Equals(startComponentId, sinkId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startComponentId };
        queue.Enqueue(startComponentId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var link in graph.Links
                         .Where(l => string.Equals(l.Source.ComponentId, current, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(l => l.Source.PortName, StringComparer.Ordinal)
                         .ThenBy(l => l.Id, StringComparer.Ordinal))
            {
                var next = link.Destination.ComponentId;
                if (string.Equals(next, sinkId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static MemoryOperationType DetermineMemoryOperation(SimComponentDef memory, Packet packet)
    {
        if (packet.MemoryOperation != MemoryOperationType.None)
        {
            return packet.MemoryOperation;
        }

        if (packet.PacketType == PacketType.MemoryWriteRequest)
        {
            return MemoryOperationType.Write;
        }

        if (packet.PacketType == PacketType.MemoryReadRequest || packet.PacketType == PacketType.Activation)
        {
            return MemoryOperationType.Read;
        }

        return ParseMemoryOperation(memory.Parameters.GetValueOrDefault("default_memory_operation", "read"));
    }

    private static void EnsureMemoryRequestMetadata(Packet packet, MemoryOperationType operation)
    {
        if (string.IsNullOrWhiteSpace(packet.RequestId))
        {
            packet.RequestId = $"{packet.Id}_REQ";
        }

        if (packet.MemoryOperation == MemoryOperationType.None)
        {
            packet.MemoryOperation = operation;
        }
    }

    private static int MemoryLatency(SimComponentDef memory, MemoryOperationType operation)
    {
        return operation == MemoryOperationType.Write
            ? Math.Max(0, memory.GetIntParameter("write_latency", memory.GetIntParameter("write_latency_cycles", ComponentDefaults.MemoryWriteLatency)))
            : Math.Max(0, memory.GetIntParameter("read_latency", memory.GetIntParameter("read_latency_cycles", memory.GetIntParameter("memory_latency_cycles", ComponentDefaults.MemoryReadLatency))));
    }

    private static long MemoryCapacityBits(SimComponentDef memory) =>
        Math.Max(1, memory.GetIntParameter("capacity_bits", memory.GetIntParameter("memory_capacity_bits", (int)ComponentDefaults.MemoryCapacityBits)));

    private static PacketType ParsePacketType(string raw) =>
        Enum.TryParse<PacketType>(raw, ignoreCase: true, out var packetType) ? packetType : PacketType.Activation;

    private static MemoryOperationType ParseMemoryOperation(string raw)
    {
        if (Enum.TryParse<MemoryOperationType>(raw, ignoreCase: true, out var operation))
        {
            return operation;
        }

        return raw.Equals("read", StringComparison.OrdinalIgnoreCase) ? MemoryOperationType.Read :
            raw.Equals("write", StringComparison.OrdinalIgnoreCase) ? MemoryOperationType.Write :
            MemoryOperationType.None;
    }

    private static string ProcessingStartDetail(SimComponentDef component, Packet packet, long cycle, int latency) =>
        component.Type switch
        {
            ComponentKind.SoftmaxUnit => $"state=WaitingInput->Computing;compute_latency={latency};processing_until={cycle + latency};input={FormatVector(PacketInputValues(packet))}",
            _ => $"processing until cycle {cycle + latency}"
        };

    private static string ProcessingCompleteDetail(SimComponentDef component, Packet packet) =>
        component.Type switch
        {
            ComponentKind.ReductionUnit => $"state=WaitingOutput->WaitingInput;reduction_sum={FormatVector(packet.Values)}",
            ComponentKind.SoftmaxUnit => $"state=WaitingOutput->WaitingInput;softmax={FormatVector(packet.Values)}",
            _ => "processing complete"
        };

    private static int ProcessingLatency(SimComponentDef component) => component.Type switch
    {
        ComponentKind.ProcessingElement => ProcessingElementLatency(component),
        ComponentKind.ReductionUnit => ReductionLatency(component),
        ComponentKind.SoftmaxUnit => SoftmaxLatency(component),
        ComponentKind.Memory => MemoryLatency(component, MemoryOperationType.Read),
        ComponentKind.Quantizer or ComponentKind.Dequantizer or ComponentKind.PrecisionConverter => PrecisionConversionLatency(component),
        ComponentKind.Adapter => AdapterRuntimeMetadata.LatencyCycles(component),
        _ => component.GetIntParameter(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, 0)
    };

    private static int PrecisionConversionLatency(SimComponentDef component)
    {
        if (component.Parameters.TryGetValue("conversion_latency_cycles", out var rawLatency) &&
            int.TryParse(rawLatency, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explicitLatency))
        {
            return Math.Max(0, explicitLatency);
        }

        var sourcePrecision = ParsePrecision(component.Parameters.GetValueOrDefault("source_precision", PrecisionKind.INT8.ToString()));
        var targetPrecision = ParsePrecision(component.Parameters.GetValueOrDefault("target_precision", PrecisionKind.INT8.ToString()));
        return sourcePrecision == PrecisionKind.FP32 && targetPrecision == PrecisionKind.INT4 ? 2 : 1;
    }
    private static int ProcessingElementLatency(SimComponentDef component)
    {
        var totalMacs = component.GetIntParameter("total_macs", 0);
        var macPerCycle = component.GetIntParameter("mac_per_cycle", ComponentDefaults.ProcessingElementMacPerCycle);
        if (totalMacs > 0 && macPerCycle > 0)
        {
            return Math.Max(1, (int)Math.Ceiling((double)totalMacs / macPerCycle));
        }

        return component.GetIntParameter("compute_latency_cycles", 2);
    }

    private static int ReductionNumInputs(SimComponentDef component) =>
        Math.Max(1, component.GetIntParameter("num_inputs", ComponentDefaults.ReductionUnitNumInputs));

    private static int ReductionLatency(SimComponentDef component) =>
        Math.Max(0, component.GetIntParameter(
            "accumulate_latency",
            component.GetIntParameter("reduction_latency_cycles", ComponentDefaults.ReductionUnitAccumulateLatency)));

    private static int SoftmaxLatency(SimComponentDef component) =>
        Math.Max(0, component.GetIntParameter(
            "compute_latency",
            component.GetIntParameter("softmax_latency_cycles", ComponentDefaults.SoftmaxUnitComputeLatency)));

    private static void ApplyComponentRuntimeIfNeeded(
        SimComponentDef component,
        Packet packet,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        if (component.Type == ComponentKind.ProcessingElement && component.Parameters.ContainsKey(ComponentTemplateRuntimeKeys.CompiledProfileHash))
        {
            ApplyComponentTemplateRuntime(component, packet, record, metrics);
        }

        if (component.Type == ComponentKind.SoftmaxUnit)
        {
            ApplySoftmaxRuntime(component, packet, record);
            return;
        }

        if (component.Type is ComponentKind.Quantizer or ComponentKind.Dequantizer or ComponentKind.PrecisionConverter)
        {
            ApplyPrecisionConversion(component, packet, record, metrics);
            return;
        }

        if (!string.IsNullOrWhiteSpace(component.TypeId))
        {
            ApplyPluginRuntime(component, packet, record, metrics);
            return;
        }

        if (!AdapterRuntimeMetadata.IsAdapterRuntimeComponent(component))
        {
            return;
        }

        var energy = packet.Bits * AdapterRuntimeMetadata.EnergyPicojoulesPerBit(component);
        metrics.Components[component.Id].Energy += energy;
        if (AdapterRuntimeMetadata.ContributesToConversionEnergy(component))
        {
            metrics.Global.ConversionEnergy += energy;
        }

        metrics.Global.TotalEnergy += energy;
        if (AdapterRuntimeMetadata.ContributesToOpticalEnergy(component))
        {
            metrics.Global.OpticalEnergy += energy;
        }

        var adapterType = component.Parameters.GetValueOrDefault("adapter_type", component.Type.ToString());
        record.Events.Add(new(
            TraceEventType.Compute,
            PacketId: packet.Id,
            ComponentId: component.Id,
            Bits: packet.Bits,
            Detail: $"adapter_pass_through:{adapterType};bits={packet.Bits};energy_pj={energy:0.###}"));
    }

    private static void ApplyComponentTemplateRuntime(
        SimComponentDef component,
        Packet packet,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        var breakdown = ParseTemplateEnergyBreakdown(component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.EnergyBreakdownPicojoules, ""));
        var breakdownTotal = breakdown.Values.Sum();
        var declaredTotal = component.GetDoubleParameter(ComponentTemplateRuntimeKeys.EnergyTotalPicojoules, breakdownTotal);
        var energy = declaredTotal > 0 ? declaredTotal : breakdownTotal;
        var componentMetrics = metrics.Components[component.Id];
        if (energy > 0)
        {
            componentMetrics.Energy += energy;
            componentMetrics.EnergyBreakdown.Dynamic += new Picojoules(energy);
            metrics.Global.TotalEnergy += energy;
            metrics.Global.ComputeEnergy += energy;
            metrics.Global.EnergyByCategory.Compute += new Picojoules(energy);
        }

        if (breakdown.Count > 0)
        {
            componentMetrics.InternalEnergyBreakdown ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in breakdown.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                componentMetrics.InternalEnergyBreakdown[pair.Key] = componentMetrics.InternalEnergyBreakdown.GetValueOrDefault(pair.Key) + pair.Value;
            }
        }

        var templateId = component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.TemplateId, "");
        var templateVersion = component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.TemplateVersion, "");
        var profileHash = component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.CompiledProfileHash, "");
        var descriptors = component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.TraceDescriptors, "");
        var drilldown = component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.InternalDrilldownStages, "");
        record.Events.Add(new(
            TraceEventType.Compute,
            PacketId: packet.Id,
            ComponentId: component.Id,
            Bits: packet.Bits,
            Detail: $"component_template_runtime;template_id={templateId};template_version={templateVersion};profile_hash={profileHash};shell_summary=pe_shell_summary;operation_latency={component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.OperationLatency, "")};pipeline_latency={component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.PipelineLatency, "")};issue_interval={component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.IssueInterval, "")};response_target_policy={component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.DefaultResponseTargetPolicy, "")};energy_pj={FormatDouble(energy)};breakdown_total_pj={FormatDouble(breakdownTotal)};trace={descriptors}"));

        if (!string.IsNullOrWhiteSpace(drilldown))
        {
            record.Events.Add(new(
                TraceEventType.Compute,
                PacketId: packet.Id,
                ComponentId: component.Id,
                Bits: packet.Bits,
                Detail: $"component_template_drilldown;profile_hash={profileHash};stages={drilldown};energy_breakdown={component.Parameters.GetValueOrDefault(ComponentTemplateRuntimeKeys.EnergyBreakdownPicojoules, "")}"));
        }
    }

    private static Dictionary<string, double> ParseTemplateEnergyBreakdown(string raw)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw.Split(['|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = item.IndexOf(':');
            if (index <= 0 || index >= item.Length - 1)
            {
                continue;
            }

            var key = item[..index];
            var valueText = item[(index + 1)..];
            if (double.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static void ApplyPluginRuntime(
        SimComponentDef component,
        Packet packet,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        var energy = component.GetDoubleParameter(ComponentPluginRuntimeKeys.EnergyPicojoulesPerPacket, 0) +
            packet.Bits * component.GetDoubleParameter(ComponentPluginRuntimeKeys.EnergyPicojoulesPerBit, 0);
        var category = PluginEnergyCategory(component);
        var componentMetrics = metrics.Components[component.Id];
        componentMetrics.Energy += energy;
        if (energy > 0)
        {
            componentMetrics.EnergyBreakdown.Dynamic += new Picojoules(energy);
            metrics.Global.TotalEnergy += energy;
            metrics.Global.EnergyByCategory[category] = metrics.Global.EnergyByCategory[category] + new Picojoules(energy);
            switch (category)
            {
                case EnergyCategory.Compute: metrics.Global.ComputeEnergy += energy; break;
                case EnergyCategory.NoC: metrics.Global.NoCEnergy += energy; break;
                case EnergyCategory.Conversion: metrics.Global.ConversionEnergy += energy; break;
                case EnergyCategory.Optical: metrics.Global.OpticalEnergy += energy; break;
            }

            if (AdapterRuntimeMetadata.ContributesToConversionEnergy(component) && category != EnergyCategory.Conversion)
            {
                metrics.Global.ConversionEnergy += energy;
            }

            if (AdapterRuntimeMetadata.ContributesToOpticalEnergy(component) && category != EnergyCategory.Optical)
            {
                metrics.Global.OpticalEnergy += energy;
            }
        }

        record.Events.Add(new(
            TraceEventType.Compute,
            PacketId: packet.Id,
            ComponentId: component.Id,
            Bits: packet.Bits,
            Detail: $"plugin_runtime:type_id={component.TypeId};latency={component.GetIntParameter(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, 0)};energy_category={category};energy_pj={energy:0.###};trace={component.Parameters.GetValueOrDefault(ComponentPluginRuntimeKeys.TraceDescriptors, "")};metrics={component.Parameters.GetValueOrDefault(ComponentPluginRuntimeKeys.MetricDescriptors, "")}"));

        if (AdapterRuntimeMetadata.IsAdapterRuntimeComponent(component))
        {
            var adapterType = component.Parameters.GetValueOrDefault("adapter_type", component.Name);
            record.Events.Add(new(
                TraceEventType.Compute,
                PacketId: packet.Id,
                ComponentId: component.Id,
                Bits: packet.Bits,
                Detail: $"adapter_pass_through:{adapterType};bits={packet.Bits};energy_pj={energy:0.###}"));
        }
    }

    private static EnergyCategory PluginEnergyCategory(SimComponentDef component) =>
        component.Parameters.TryGetValue(ComponentPluginRuntimeKeys.EnergyCategory, out var rawCategory) &&
        Enum.TryParse<EnergyCategory>(rawCategory, ignoreCase: true, out var category)
            ? category
            : EnergyCategory.NoC;
    private static void ApplyPrecisionConversion(
        SimComponentDef component,
        Packet packet,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        var sourcePrecision = ParsePrecision(component.Parameters.GetValueOrDefault("source_precision", PrecisionKind.INT8.ToString()));
        var targetPrecision = ParsePrecision(component.Parameters.GetValueOrDefault("target_precision", PrecisionKind.INT8.ToString()));
        var sourceBits = Math.Max(1, PrecisionModel.BitsPerElement(sourcePrecision));
        var targetBits = Math.Max(1, PrecisionModel.BitsPerElement(targetPrecision));
        var originalBits = packet.Bits;
        var numericDetail = ApplyPrecisionNumericSemantics(component, packet, targetPrecision, targetBits, record);
        var numericSuffix = string.IsNullOrWhiteSpace(numericDetail) ? "" : numericDetail + ";";
        var elementCount = Math.Max(1, (int)Math.Ceiling(originalBits / (double)sourceBits));
        packet.NumElements = elementCount;
        packet.BitWidth = targetBits;
        packet.Precision = targetPrecision;
        packet.Bits = Math.Max(1, elementCount * targetBits);

        var energy = originalBits * component.GetDoubleParameter("conversion_energy_pj_per_bit", 0.01);
        var componentMetrics = metrics.Components[component.Id];
        componentMetrics.Energy += energy;
        componentMetrics.EnergyBreakdown.Conversion += new Picojoules(energy);
        metrics.Global.ConversionEnergy += energy;
        metrics.Global.TotalEnergy += energy;
        metrics.Global.EnergyByCategory.Conversion += new Picojoules(energy);
        packet.Metadata["precision_conversion"] = $"{sourcePrecision}->{targetPrecision}";
        packet.Metadata["bit_width"] = targetBits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        record.Events.Add(new(
            TraceEventType.Compute,
            PacketId: packet.Id,
            ComponentId: component.Id,
            Bits: packet.Bits,
            Detail: $"precision_conversion:{sourcePrecision}->{targetPrecision};precision={packet.Precision};bit_width={packet.BitWidth};bits={originalBits}->{packet.Bits};total_bits={packet.TotalBits};{numericSuffix}energy_pj={energy:0.###}"));
    }

    private static string ApplyPrecisionNumericSemantics(
        SimComponentDef component,
        Packet packet,
        PrecisionKind targetPrecision,
        int targetBits,
        CycleTraceRecord record)
    {
        if (component.Type == ComponentKind.Quantizer)
        {
            var scale = QuantizationScale(component);
            var zeroPoint = QuantizationZeroPoint(component);
            var rounding = QuantizationRounding(component);
            var signed = QuantizationSigned(component);
            var (min, max) = QuantizationRange(targetBits, signed);
            var saturationCount = 0;
            var quantized = new List<double>();
            foreach (var value in PacketInputValues(packet))
            {
                var rounded = RoundQuantized(value / scale + zeroPoint, rounding);
                var clamped = Math.Min(max, Math.Max(min, rounded));
                if (Math.Abs(clamped - rounded) > 0.000000001)
                {
                    saturationCount++;
                }

                quantized.Add(clamped);
            }

            packet.Values = quantized;
            packet.Metadata["quantization_scale"] = FormatDouble(scale);
            packet.Metadata["quantization_zero_point"] = FormatDouble(zeroPoint);
            packet.Metadata["quantization_rounding"] = rounding;
            packet.Metadata["quantization_signed"] = signed.ToString().ToLowerInvariant();
            if (saturationCount > 0)
            {
                record.Events.Add(new(
                    TraceEventType.Warning,
                    PacketId: packet.Id,
                    ComponentId: component.Id,
                    Bits: packet.Bits,
                    Detail: $"quantization_saturation;saturation_count={saturationCount};clamp=[{min},{max}];target_precision={targetPrecision}"));
            }

            return $"scale={FormatDouble(scale)};zero_point={FormatDouble(zeroPoint)};rounding={rounding};signed={signed.ToString().ToLowerInvariant()};clamp=[{min},{max}];saturation_count={saturationCount};quantized_values={FormatVector(packet.Values)};";
        }

        if (component.Type == ComponentKind.Dequantizer)
        {
            var scale = QuantizationScale(component);
            var zeroPoint = QuantizationZeroPoint(component);
            var dequantized = PacketInputValues(packet)
                .Select(value => (value - zeroPoint) * scale)
                .ToList();
            packet.Values = dequantized;
            packet.Metadata["dequantization_scale"] = FormatDouble(scale);
            packet.Metadata["dequantization_zero_point"] = FormatDouble(zeroPoint);
            return $"scale={FormatDouble(scale)};zero_point={FormatDouble(zeroPoint)};dequantized_values={FormatVector(packet.Values)};";
        }

        return "";
    }

    private static double QuantizationScale(SimComponentDef component)
    {
        var raw = component.Parameters.GetValueOrDefault("scale", component.Parameters.GetValueOrDefault("quantization_scale", "1"));
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale) && scale > 0
            ? scale
            : 1d;
    }

    private static double QuantizationZeroPoint(SimComponentDef component)
    {
        var raw = component.Parameters.GetValueOrDefault("zero_point", component.Parameters.GetValueOrDefault("quantization_zero_point", "0"));
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var zeroPoint)
            ? zeroPoint
            : 0d;
    }

    private static string QuantizationRounding(SimComponentDef component) =>
        component.Parameters.GetValueOrDefault("rounding", "nearest_away_from_zero").Trim().ToLowerInvariant() switch
        {
            "nearest_even" => "nearest_even",
            "floor" => "floor",
            "ceil" => "ceil",
            "truncate" => "truncate",
            "nearest" => "nearest_away_from_zero",
            _ => "nearest_away_from_zero"
        };

    private static bool QuantizationSigned(SimComponentDef component)
    {
        var raw = component.Parameters.GetValueOrDefault("signed", component.Parameters.GetValueOrDefault("signedness", "signed"));
        return !(raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw.Equals("unsigned", StringComparison.OrdinalIgnoreCase));
    }

    private static (long Min, long Max) QuantizationRange(int bits, bool signed)
    {
        bits = Math.Clamp(bits, 1, 32);
        return signed
            ? (-(1L << (bits - 1)), (1L << (bits - 1)) - 1)
            : (0, (1L << bits) - 1);
    }

    private static double RoundQuantized(double value, string rounding) => rounding switch
    {
        "nearest_even" => Math.Round(value, MidpointRounding.ToEven),
        "floor" => Math.Floor(value),
        "ceil" => Math.Ceiling(value),
        "truncate" => Math.Truncate(value),
        _ => Math.Round(value, MidpointRounding.AwayFromZero)
    };
    private static void ApplySoftmaxRuntime(
        SimComponentDef component,
        Packet packet,
        CycleTraceRecord record)
    {
        var values = PacketInputValues(packet);
        var max = values.Max();
        var exp = values.Select(value => Math.Exp(value - max)).ToList();
        var sum = exp.Sum();
        packet.Values = exp.Select(value => value / sum).ToList();
        record.Events.Add(new(
            TraceEventType.Compute,
            PacketId: packet.Id,
            ComponentId: component.Id,
            Bits: packet.Bits,
            Detail: $"state=Computing->WaitingOutput;softmax={FormatVector(packet.Values)}"));
    }

    private static List<double> PacketValues(SimComponentDef source, int packetIndex)
    {
        if (source.Parameters.TryGetValue("payload_values", out var vector))
        {
            return ParseVector(vector);
        }

        if (!source.Parameters.TryGetValue("payload_value", out var scalar) ||
            !double.TryParse(scalar, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return [];
        }

        if (source.Parameters.TryGetValue("payload_stride", out var strideRaw) &&
            double.TryParse(strideRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var stride))
        {
            value += packetIndex * stride;
        }

        return [value];
    }

    private static List<double> ParseVector(string raw) =>
        raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => double.Parse(item.Trim(), System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    private static IReadOnlyList<double> PacketInputValues(Packet packet) =>
        packet.Values.Count == 0 ? [0d] : packet.Values;

    private static double PacketNumericValue(Packet packet) =>
        packet.Values.Count == 0 ? 1d : packet.Values.Sum();

    private static string FormatVector(IReadOnlyList<double> values) =>
        $"[{string.Join(",", values.Select(FormatDouble))}]";

    private static string FormatDouble(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static PrecisionKind ParsePrecision(string raw) =>
        Enum.TryParse<PrecisionKind>(raw, ignoreCase: true, out var precision) ? precision : PrecisionKind.INT8;

    private static int QueueCapacity(SimComponentDef component) =>
        Math.Max(1, component.GetIntParameter("queue_capacity", 4));

    private static bool TryReserveMemoryAccess(
        SimComponentDef component,
        Packet packet,
        Dictionary<string, int> memoryAccessesThisCycle,
        SimulationMetrics metrics,
        out string bankId)
    {
        var bankCount = Math.Max(1, component.GetIntParameter("memory_banks", ComponentDefaults.MemoryBanks));
        var bankIndex = SelectMemoryBank(component, packet, bankCount);
        bankId = bankCount > 1 ? $"bank{bankIndex}" : "";
        var reservationKey = bankCount > 1 ? $"{component.Id}:{bankId}" : component.Id;
        var capacity = Math.Max(1, component.GetIntParameter("max_concurrent_accesses",
            component.GetIntParameter("memory_ports", ComponentDefaults.MemoryPorts)));
        var used = memoryAccessesThisCycle.GetValueOrDefault(reservationKey);
        if (used >= capacity)
        {
            return false;
        }

        memoryAccessesThisCycle[reservationKey] = used + 1;
        if (!string.IsNullOrWhiteSpace(bankId))
        {
            metrics.Components[component.Id].MemoryBankAccesses[bankId] =
                metrics.Components[component.Id].MemoryBankAccesses.GetValueOrDefault(bankId) + 1;
        }

        return true;
    }

    private static int SelectMemoryBank(SimComponentDef memory, Packet packet, int bankCount)
    {
        if (bankCount <= 1)
        {
            return 0;
        }

        if (!packet.MemoryAddress.HasValue)
        {
            return 0;
        }

        var lineSize = Math.Max(1, memory.GetIntParameter("line_size_bits", ComponentDefaults.MemoryLineSizeBits));
        return (int)(Math.Abs(packet.MemoryAddress.Value / lineSize) % bankCount);
    }

    private static void AccountIdleCycles(
        HardwareSimulationGraph graph,
        Dictionary<string, Queue<Packet>> outputQueues,
        Dictionary<string, RouterState> routerStates,
        Dictionary<string, ReductionUnitState> reductionStates,
        Dictionary<string, List<ProcessingItem>> processing,
        List<FlitFlight> inFlight,
        SimulationMetrics metrics,
        long cycles = 1)
    {
        if (cycles <= 0)
        {
            return;
        }

        foreach (var component in graph.Components)
        {
            var activeInFlight = inFlight.Any(t => t.Link.Source.ComponentId == component.Id || t.Link.Destination.ComponentId == component.Id);
            var routerQueueActive = routerStates.TryGetValue(component.Id, out var routerState) && !RouterStateEmpty(routerState);
            var reductionInputActive = reductionStates.TryGetValue(component.Id, out var reductionState) && reductionState.Inputs.Count > 0;
            if (outputQueues[component.Id].Count == 0 && processing[component.Id].Count == 0 && !routerQueueActive && !reductionInputActive && !activeInFlight)
            {
                metrics.Components[component.Id].IdleCycles += cycles;
            }
        }
    }

    private static void InitializeMetrics(HardwareSimulationGraph graph, SimulationMetrics metrics)
    {
        foreach (var component in graph.Components)
        {
            var componentMetrics = new ComponentMetrics { ComponentId = component.Id };
            if (component.Type == ComponentKind.Memory)
            {
                componentMetrics.MemoryCapacityBits = MemoryCapacityBits(component);
            }

            metrics.Components[component.Id] = componentMetrics;
        }

        foreach (var link in graph.Links)
        {
            metrics.Links[link.Id] = new LinkMetrics { LinkId = link.Id };
        }
    }
}
