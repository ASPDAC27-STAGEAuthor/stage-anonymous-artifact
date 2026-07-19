using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Routing functions implemented by the managed STAGE flit/VC runtime.</summary>
public enum AspdacStageNocRouting
{
    /// <summary>Deterministic X-then-Y mesh routing.</summary>
    DimensionOrder,
    /// <summary>Per-packet XY or YX routing with disjoint VC partitions.</summary>
    XyYx,
    /// <summary>Randomized oblivious minimal routing through a generated intermediate.</summary>
    Romm
}

/// <summary>One resolved STAGE packet offer, including provenance that is outside BookSim's network model.</summary>
public sealed record AspdacStageNocPacket(
    string PacketId,
    long ReleaseCycle,
    int SourceRouter,
    int DestinationRouter,
    int Flits,
    string RouteMode = "xy",
    string LayerId = "",
    string TensorRole = "",
    string MappingId = "",
    string SourceComponentId = "",
    string DestinationComponentId = "",
    string RouteResourceId = "");

/// <summary>Configuration for the explicit BookSim-matched STAGE router pipeline.</summary>
public sealed record AspdacStageNocOptions(
    int BufferDepth = 16,
    int VirtualChannels = 1,
    AspdacStageNocRouting Routing = AspdacStageNocRouting.DimensionOrder,
    int Seed = 40,
    bool UseBooksimRouteRandom = false,
    long MaxCycles = 50_000);

/// <summary>Canonical flit-level event emitted by STAGE's managed network runtime.</summary>
public sealed record AspdacStageNocEvent(
    long Cycle,
    string EventType,
    string PacketId,
    int FlitId,
    int Router,
    int InputPort,
    int OutputPort,
    int VirtualChannel,
    int Occupancy,
    string Detail);

/// <summary>Tail-delivery result with STAGE workload and hardware provenance.</summary>
public sealed record AspdacStageNocPacketDelivery(
    string PacketId,
    long ReleaseCycle,
    long InjectionCycle,
    long ArrivalCycle,
    int SourceRouter,
    int DestinationRouter,
    int Flits,
    string LayerId,
    string TensorRole,
    string MappingId,
    string SourceComponentId,
    string DestinationComponentId,
    string RouteResourceId);

/// <summary>One delivered flit.</summary>
public sealed record AspdacStageNocFlitDelivery(
    string PacketId,
    int FlitId,
    int FlitIndex,
    long ArrivalCycle);

/// <summary>Complete deterministic result from the in-process STAGE network engine.</summary>
public sealed class AspdacStageNocRunResult
{
    /// <summary>Gets the stable first-party engine identity.</summary>
    public string EngineIdentity { get; init; } = "stage_managed_flit_vc_runtime";
    /// <summary>Gets the number of external simulator/backend invocations.</summary>
    public int ExternalBackendInvocations { get; init; }
    /// <summary>Gets the final simulated cycle.</summary>
    public long FinalCycle { get; init; }
    /// <summary>Gets the peak occupancy of one router input VC.</summary>
    public int MaxInputVcOccupancy { get; init; }
    /// <summary>Gets the canonical STAGE event-stream hash.</summary>
    public string CanonicalEventHash { get; init; } = "";
    /// <summary>Gets the canonical workload-to-hardware provenance hash.</summary>
    public string StageProvenanceHash { get; init; } = "";
    /// <summary>Gets tail-delivery records.</summary>
    public IReadOnlyList<AspdacStageNocPacketDelivery> PacketDeliveries { get; init; } = Array.Empty<AspdacStageNocPacketDelivery>();
    /// <summary>Gets delivered-flit records.</summary>
    public IReadOnlyList<AspdacStageNocFlitDelivery> FlitDeliveries { get; init; } = Array.Empty<AspdacStageNocFlitDelivery>();
    /// <summary>Gets all canonical pipeline events.</summary>
    public IReadOnlyList<AspdacStageNocEvent> Events { get; init; } = Array.Empty<AspdacStageNocEvent>();
    /// <summary>Gets RNG-generated XY/YX choices keyed by packet id.</summary>
    public IReadOnlyDictionary<string, string> GeneratedRouteModes { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets RNG-generated ROMM intermediates keyed by packet id.</summary>
    public IReadOnlyDictionary<string, int> RommIntermediates { get; init; } = new Dictionary<string, int>();
}

/// <summary>One iteration of BookSim-compatible stochastic drain-horizon resolution.</summary>
public sealed record AspdacStageNocDrainIteration(long Horizon, int GeneratedPackets, long LastMeasuredArrival);

/// <summary>Traffic-manager result containing the converged generation horizon and network run.</summary>
public sealed class AspdacStageNocStochasticResult
{
    /// <summary>Gets the converged traffic-generation horizon.</summary>
    public long GenerationHorizon { get; init; }
    /// <summary>Gets fixed-point drain iterations.</summary>
    public IReadOnlyList<AspdacStageNocDrainIteration> HorizonIterations { get; init; } = Array.Empty<AspdacStageNocDrainIteration>();
    /// <summary>Gets the final STAGE flit/VC run.</summary>
    public AspdacStageNocRunResult Run { get; init; } = new();
}

/// <summary>
/// Pure managed STAGE implementation of the selected BookSim router contract.
/// It does not load, spawn, or call BookSim; BookSim is used only by external validation runners.
/// </summary>
public static class AspdacStageNocRuntime
{
    /// <summary>Gets the fixed mesh dimension.</summary>
    public const int Dimension = 4;
    /// <summary>Gets the fixed router and endpoint count.</summary>
    public const int RouterCount = 16;
    /// <summary>Gets the four directional ports plus the local port.</summary>
    public const int Ports = 5;

    /// <summary>Runs resolved packet offers through the managed STAGE flit/VC pipeline.</summary>
    public static AspdacStageNocRunResult Run(
        AspdacStageNocOptions options,
        IReadOnlyList<AspdacStageNocPacket> packets) =>
        new Simulation(options, packets).Run();

    /// <summary>Runs BookSim's Bernoulli/uniform generation and drain fixed point in STAGE.</summary>
    public static AspdacStageNocStochasticResult RunBooksimUniformTraffic(
        int seed,
        double injectionRate,
        long runningStart = 100,
        long runningEnd = 200)
    {
        if (injectionRate < 0d || injectionRate > 1d) throw new ArgumentOutOfRangeException(nameof(injectionRate));
        if (runningStart < 0 || runningEnd <= runningStart) throw new ArgumentOutOfRangeException(nameof(runningEnd));
        var horizon = runningEnd;
        var iterations = new List<AspdacStageNocDrainIteration>();
        AspdacStageNocRunResult? result = null;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var packets = GenerateBooksimUniformPackets(seed, injectionRate, horizon);
            result = Run(new AspdacStageNocOptions(16, 1, AspdacStageNocRouting.DimensionOrder, seed), packets);
            var measured = result.PacketDeliveries
                .Where(row => row.ReleaseCycle >= runningStart && row.ReleaseCycle < runningEnd)
                .Select(row => row.ArrivalCycle)
                .ToArray();
            var lastMeasured = measured.Length == 0 ? runningEnd : Math.Max(runningEnd, measured.Max());
            iterations.Add(new AspdacStageNocDrainIteration(horizon, result.PacketDeliveries.Count, lastMeasured));
            if (lastMeasured == horizon)
            {
                return new AspdacStageNocStochasticResult
                {
                    GenerationHorizon = horizon,
                    HorizonIterations = iterations,
                    Run = result
                };
            }
            horizon = lastMeasured;
        }
        throw new InvalidOperationException("BookSim-compatible stochastic drain horizon did not converge.");
    }

    private static IReadOnlyList<AspdacStageNocPacket> GenerateBooksimUniformPackets(int seed, double rate, long horizon)
    {
        var integerRandom = new BooksimIntegerRandom(seed);
        var floatingRandom = new BooksimFloatingRandom(seed);
        var packets = new List<AspdacStageNocPacket>();
        var packetId = 0;
        for (var cycle = 0L; cycle < horizon; cycle++)
        {
            for (var source = 0; source < RouterCount; source++)
            {
                if (floatingRandom.Next() >= rate) continue;
                var destination = integerRandom.RandomInt(RouterCount - 1);
                _ = integerRandom.RandomInt(0);
                packets.Add(new AspdacStageNocPacket(
                    $"stoch-{packetId++}",
                    cycle,
                    source,
                    destination,
                    1,
                    LayerId: "traffic-manager",
                    TensorRole: "uniform",
                    MappingId: $"uniform-seed-{seed}",
                    SourceComponentId: $"endpoint-{source:D2}",
                    DestinationComponentId: $"endpoint-{destination:D2}",
                    RouteResourceId: "mesh-4x4"));
            }
        }
        return packets;
    }

    private sealed class Simulation
    {
        private readonly AspdacStageNocOptions options;
        private readonly AspdacStageNocPacket[] packets;
        private readonly Router[] routers;
        private readonly Dictionary<long, List<Arrival>> arrivals = new();
        private readonly Dictionary<long, List<Crossbar>> crossbars = new();
        private readonly Dictionary<long, List<Upstream>> credits = new();
        private readonly Dictionary<long, List<Envelope>> endpointArrivals = new();
        private readonly List<AspdacStageNocEvent> events = new();
        private readonly List<AspdacStageNocPacketDelivery> packetDeliveries = new();
        private readonly List<AspdacStageNocFlitDelivery> flitDeliveries = new();
        private readonly Dictionary<string, Flit[]> flitsByPacket = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> generatedRouteModes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> rommIntermediates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> rommPhases = new(StringComparer.Ordinal);
        private readonly BooksimIntegerRandom? routeRandom;
        private int maxOccupancy;

        public Simulation(AspdacStageNocOptions options, IReadOnlyList<AspdacStageNocPacket> packets)
        {
            Validate(options, packets);
            this.options = options;
            this.packets = packets
                .OrderBy(packet => packet.ReleaseCycle)
                .ThenBy(packet => packet.SourceRouter)
                .ThenBy(packet => packet.PacketId, StringComparer.Ordinal)
                .ToArray();
            routers = Enumerable.Range(0, RouterCount)
                .Select(routerId => new Router(routerId, options.BufferDepth, options.VirtualChannels))
                .ToArray();
            var nextFlitId = 0;
            foreach (var packet in this.packets)
            {
                flitsByPacket.Add(packet.PacketId, Enumerable.Range(0, packet.Flits)
                    .Select(index => new Flit(nextFlitId + index, packet, index))
                    .ToArray());
                nextFlitId += packet.Flits;
            }
            if ((options.Routing == AspdacStageNocRouting.XyYx && options.UseBooksimRouteRandom) ||
                options.Routing == AspdacStageNocRouting.Romm)
            {
                routeRandom = new BooksimIntegerRandom(options.Seed);
                foreach (var packet in this.packets) _ = routeRandom.RandomInt(0);
            }
        }

        public AspdacStageNocRunResult Run()
        {
            var releaseSchedule = packets
                .GroupBy(packet => packet.ReleaseCycle)
                .ToDictionary(group => group.Key, group => group
                    .OrderBy(packet => packet.SourceRouter)
                    .ThenBy(packet => packet.PacketId, StringComparer.Ordinal)
                    .ToArray());
            var pending = Enumerable.Range(0, RouterCount).Select(_ => new Queue<AspdacStageNocPacket>()).ToArray();
            var active = new SourceActive?[RouterCount];
            var sourceCredits = new int[RouterCount, options.VirtualChannels];
            for (var source = 0; source < RouterCount; source++)
                for (var vc = 0; vc < options.VirtualChannels; vc++)
                    sourceCredits[source, vc] = options.BufferDepth;

            var finalCycle = 0L;
            for (var cycle = 0L; cycle <= options.MaxCycles; cycle++)
            {
                finalCycle = cycle;
                if (releaseSchedule.TryGetValue(cycle, out var released))
                {
                    foreach (var packet in released)
                    {
                        pending[packet.SourceRouter].Enqueue(packet);
                        Event(cycle, "release", packet, detail: $"source={packet.SourceRouter}");
                    }
                }
                ProcessCredits(cycle, sourceCredits);
                Inject(cycle, pending, active, sourceCredits);
                ProcessEndpointArrivals(cycle);
                ProcessArrivals(cycle);
                ProcessCrossbars(cycle);
                ProcessDeferredRoutes(cycle);
                ProcessVcAllocation(cycle);
                ProcessSwitchAllocation(cycle);
                if (packetDeliveries.Count == packets.Length) break;
                if (cycle == options.MaxCycles) throw new InvalidOperationException("STAGE flit/VC runtime did not drain.");
            }

            var sortedEvents = events
                .OrderBy(row => row.Cycle)
                .ThenBy(row => row.Router)
                .ThenBy(row => row.EventType, StringComparer.Ordinal)
                .ThenBy(row => row.FlitId)
                .ToArray();
            return new AspdacStageNocRunResult
            {
                ExternalBackendInvocations = 0,
                FinalCycle = finalCycle,
                MaxInputVcOccupancy = maxOccupancy,
                CanonicalEventHash = HashEvents(sortedEvents),
                StageProvenanceHash = HashProvenance(packets),
                PacketDeliveries = packetDeliveries
                    .OrderBy(row => row.ArrivalCycle)
                    .ThenBy(row => row.PacketId, StringComparer.Ordinal)
                    .ToArray(),
                FlitDeliveries = flitDeliveries
                    .OrderBy(row => row.ArrivalCycle)
                    .ThenBy(row => row.FlitId)
                    .ToArray(),
                Events = sortedEvents,
                GeneratedRouteModes = new Dictionary<string, string>(generatedRouteModes, StringComparer.Ordinal),
                RommIntermediates = new Dictionary<string, int>(rommIntermediates, StringComparer.Ordinal)
            };
        }

        private void ProcessCredits(long cycle, int[,] sourceCredits)
        {
            foreach (var target in Take(credits, cycle))
            {
                if (target.Kind == UpstreamKind.Source)
                {
                    sourceCredits[target.Owner, target.VirtualChannel]++;
                    Event(cycle, "credit_return", virtualChannel: target.VirtualChannel, detail: $"source={target.Owner}");
                }
                else
                {
                    var router = routers[target.Owner];
                    router.OutputCredits[target.OutputPort, target.VirtualChannel]++;
                    Event(cycle, "credit_return", router: target.Owner, outputPort: target.OutputPort,
                        virtualChannel: target.VirtualChannel,
                        detail: $"credits={router.OutputCredits[target.OutputPort, target.VirtualChannel]}");
                }
            }
        }

        private void Inject(long cycle, Queue<AspdacStageNocPacket>[] pending, SourceActive?[] active, int[,] sourceCredits)
        {
            for (var source = 0; source < RouterCount; source++)
            {
                if (active[source] is null && pending[source].Count > 0)
                {
                    var packet = pending[source].Dequeue();
                    active[source] = new SourceActive(packet, 0, InjectionVc(packet));
                }
                var state = active[source];
                if (state is null) continue;
                if (sourceCredits[source, state.VirtualChannel] == 0)
                {
                    Event(cycle, "injection_stall", state.Packet, virtualChannel: state.VirtualChannel,
                        detail: "source_input_credit=0");
                    continue;
                }
                var flit = flitsByPacket[state.Packet.PacketId][state.Index];
                sourceCredits[source, state.VirtualChannel]--;
                var envelope = new Envelope(
                    flit,
                    cycle - state.Index,
                    state.VirtualChannel,
                    new Upstream(UpstreamKind.Source, source, -1, state.VirtualChannel));
                Add(arrivals, cycle + 2, new Arrival(source, 4, envelope));
                Event(cycle, "injection", state.Packet, flit.Id, source, 4, virtualChannel: state.VirtualChannel);
                active[source] = flit.IsTail
                    ? null
                    : new SourceActive(state.Packet, state.Index + 1, state.VirtualChannel);
            }
        }

        private void ProcessEndpointArrivals(long cycle)
        {
            foreach (var envelope in Take(endpointArrivals, cycle))
            {
                var flit = envelope.Flit;
                flitDeliveries.Add(new AspdacStageNocFlitDelivery(
                    flit.Packet.PacketId, flit.Id, flit.Index, cycle));
                Event(cycle, "ejection", flit.Packet, flit.Id, flit.Packet.DestinationRouter,
                    virtualChannel: envelope.InputVirtualChannel);
                if (!flit.IsTail) continue;
                var packet = flit.Packet;
                packetDeliveries.Add(new AspdacStageNocPacketDelivery(
                    packet.PacketId,
                    packet.ReleaseCycle,
                    envelope.InjectionCycle,
                    cycle,
                    packet.SourceRouter,
                    packet.DestinationRouter,
                    packet.Flits,
                    packet.LayerId,
                    packet.TensorRole,
                    packet.MappingId,
                    packet.SourceComponentId,
                    packet.DestinationComponentId,
                    packet.RouteResourceId));
            }
        }

        private void ProcessArrivals(long cycle)
        {
            foreach (var arrival in Take(arrivals, cycle)
                         .OrderBy(row => row.Router)
                         .ThenBy(row => row.InputPort)
                         .ThenBy(row => row.Envelope.InputVirtualChannel))
            {
                var inputVc = routers[arrival.Router].Inputs[arrival.InputPort, arrival.Envelope.InputVirtualChannel];
                inputVc.Buffer.Enqueue(arrival.Envelope);
                maxOccupancy = Math.Max(maxOccupancy, inputVc.Buffer.Count);
                if (inputVc.Buffer.Count > options.BufferDepth)
                    throw new InvalidOperationException("STAGE input VC overflowed despite credit flow control.");
                Event(cycle, "arrival", arrival.Envelope.Flit.Packet, arrival.Envelope.Flit.Id,
                    arrival.Router, arrival.InputPort, virtualChannel: arrival.Envelope.InputVirtualChannel,
                    occupancy: inputVc.Buffer.Count);
                if (inputVc.Buffer.Count == 1 && inputVc.State == InputVcState.Idle)
                {
                    if (!arrival.Envelope.Flit.IsHead) throw new InvalidOperationException("Body flit arrived at an idle VC.");
                    RouteNow(cycle, arrival.Router, arrival.InputPort, arrival.Envelope.InputVirtualChannel);
                }
            }
        }

        private void ProcessCrossbars(long cycle)
        {
            foreach (var traversal in Take(crossbars, cycle)
                         .OrderBy(row => row.Router)
                         .ThenBy(row => row.OutputPort)
                         .ThenBy(row => row.InputPort)
                         .ThenBy(row => row.OutputVirtualChannel))
            {
                var router = routers[traversal.Router];
                var flit = traversal.Envelope.Flit;
                if (flit.IsTail) router.OutputBusy[traversal.OutputPort, traversal.OutputVirtualChannel] = false;
                router.OutputCredits[traversal.OutputPort, traversal.OutputVirtualChannel]--;
                if (router.OutputCredits[traversal.OutputPort, traversal.OutputVirtualChannel] < 0)
                    throw new InvalidOperationException("STAGE observed a negative downstream credit.");
                Add(credits, cycle + 1, traversal.Envelope.Upstream);
                Event(cycle, "send", flit.Packet, flit.Id, traversal.Router, traversal.InputPort,
                    traversal.OutputPort, traversal.OutputVirtualChannel, detail: $"tail={flit.IsTail}");
                var downstream = new Envelope(
                    flit,
                    traversal.Envelope.InjectionCycle,
                    traversal.OutputVirtualChannel,
                    new Upstream(UpstreamKind.Router, traversal.Router, traversal.OutputPort, traversal.OutputVirtualChannel));
                if (traversal.OutputPort == 4)
                {
                    Add(endpointArrivals, cycle + 2, downstream);
                    Add(credits, cycle + 4,
                        new Upstream(UpstreamKind.Router, traversal.Router, traversal.OutputPort, traversal.OutputVirtualChannel));
                }
                else
                {
                    var (nextRouter, nextInput) = NextHop(traversal.Router, traversal.OutputPort);
                    Add(arrivals, cycle + 2, new Arrival(nextRouter, nextInput, downstream));
                }
            }
        }

        private void ProcessDeferredRoutes(long cycle)
        {
            foreach (var router in routers)
            {
                for (var inputPort = 0; inputPort < Ports; inputPort++)
                {
                    for (var inputVcIndex = 0; inputVcIndex < options.VirtualChannels; inputVcIndex++)
                    {
                        var inputVc = router.Inputs[inputPort, inputVcIndex];
                        if (inputVc.State == InputVcState.Routing && inputVc.ReadyCycle <= cycle)
                            RouteNow(cycle, router.Id, inputPort, inputVcIndex);
                    }
                }
            }
        }

        private void RouteNow(long cycle, int routerId, int inputPort, int inputVcIndex)
        {
            var inputVc = routers[routerId].Inputs[inputPort, inputVcIndex];
            if (inputVc.Buffer.Count == 0 || !inputVc.Buffer.Peek().Flit.IsHead)
                throw new InvalidOperationException("STAGE routing requires a head flit.");
            var packet = inputVc.Buffer.Peek().Flit.Packet;
            inputVc.OutputPort = Route(routerId, packet);
            inputVc.AllowedOutputVirtualChannels = AllowedVirtualChannels(routerId, packet);
            inputVc.OutputVirtualChannel = -1;
            inputVc.State = InputVcState.VirtualChannelAllocation;
            inputVc.ReadyCycle = cycle + 1;
            Event(cycle, "route", packet, inputVc.Buffer.Peek().Flit.Id, routerId, inputPort,
                inputVc.OutputPort, inputVcIndex,
                detail: $"allowed=[{string.Join(',', inputVc.AllowedOutputVirtualChannels)}];mode={EffectiveRouteMode(packet, routerId)}");
        }

        private void ProcessVcAllocation(long cycle)
        {
            var virtualChannels = options.VirtualChannels;
            var expanded = Ports * virtualChannels;
            foreach (var router in routers)
            {
                var requestsByOutput = new Dictionary<int, List<int>>();
                var requestLookup = new Dictionary<(int Input, int Output), (int Port, int Vc)>();
                for (var inputPort = 0; inputPort < Ports; inputPort++)
                {
                    for (var inputVcIndex = 0; inputVcIndex < virtualChannels; inputVcIndex++)
                    {
                        var inputVc = router.Inputs[inputPort, inputVcIndex];
                        if (inputVc.State != InputVcState.VirtualChannelAllocation || inputVc.ReadyCycle > cycle) continue;
                        var expandedInput = inputPort * virtualChannels + inputVcIndex;
                        foreach (var outputVc in inputVc.AllowedOutputVirtualChannels)
                        {
                            if (router.OutputBusy[inputVc.OutputPort, outputVc]) continue;
                            var expandedOutput = inputVc.OutputPort * virtualChannels + outputVc;
                            Add(requestsByOutput, expandedOutput, expandedInput);
                            requestLookup[(expandedInput, expandedOutput)] = (inputPort, inputVcIndex);
                        }
                    }
                }

                var grantsByInput = new Dictionary<int, List<int>>();
                foreach (var pair in requestsByOutput.OrderBy(pair => pair.Key))
                {
                    var winner = RoundRobin(pair.Value, router.VirtualChannelOutputRoundRobin[pair.Key], expanded);
                    Add(grantsByInput, winner, pair.Key);
                }
                foreach (var pair in grantsByInput.OrderBy(pair => pair.Key))
                {
                    var acceptedOutput = RoundRobin(pair.Value, router.VirtualChannelInputRoundRobin[pair.Key], expanded);
                    var (inputPort, inputVcIndex) = requestLookup[(pair.Key, acceptedOutput)];
                    var outputPort = acceptedOutput / virtualChannels;
                    var outputVc = acceptedOutput % virtualChannels;
                    var inputVc = router.Inputs[inputPort, inputVcIndex];
                    inputVc.State = InputVcState.Active;
                    inputVc.ReadyCycle = cycle + 1;
                    inputVc.OutputPort = outputPort;
                    inputVc.OutputVirtualChannel = outputVc;
                    router.OutputBusy[outputPort, outputVc] = true;
                    router.VirtualChannelOutputRoundRobin[acceptedOutput] = (pair.Key + 1) % expanded;
                    router.VirtualChannelInputRoundRobin[pair.Key] = (acceptedOutput + 1) % expanded;
                    Event(cycle, "vc_grant", inputVc.Buffer.Peek().Flit.Packet,
                        inputVc.Buffer.Peek().Flit.Id, router.Id, inputPort, outputPort, inputVcIndex,
                        detail: $"output_vc={outputVc}");
                }
            }
        }

        private void ProcessSwitchAllocation(long cycle)
        {
            var virtualChannels = options.VirtualChannels;
            foreach (var router in routers)
            {
                var requestsByOutput = new Dictionary<int, List<(int InputPort, int InputVc)>>();
                for (var inputPort = 0; inputPort < Ports; inputPort++)
                {
                    var candidates = new List<int>();
                    for (var inputVcIndex = 0; inputVcIndex < virtualChannels; inputVcIndex++)
                    {
                        var inputVc = router.Inputs[inputPort, inputVcIndex];
                        if (inputVc.State == InputVcState.Active &&
                            inputVc.ReadyCycle <= cycle &&
                            inputVc.Buffer.Count > 0 &&
                            router.OutputCredits[inputVc.OutputPort, inputVc.OutputVirtualChannel] > 0)
                        {
                            candidates.Add(inputVcIndex);
                        }
                    }
                    if (candidates.Count == 0) continue;
                    var selectedVc = RoundRobin(candidates, router.InputVirtualChannelRoundRobin[inputPort], virtualChannels);
                    var selected = router.Inputs[inputPort, selectedVc];
                    Add(requestsByOutput, selected.OutputPort, (inputPort, selectedVc));
                }

                foreach (var pair in requestsByOutput.OrderBy(pair => pair.Key))
                {
                    var physicalInputs = pair.Value.Select(candidate => candidate.InputPort);
                    var winningInput = RoundRobin(physicalInputs, router.SwitchOutputRoundRobin[pair.Key], Ports);
                    var winningVc = pair.Value.First(candidate => candidate.InputPort == winningInput).InputVc;
                    var inputVc = router.Inputs[winningInput, winningVc];
                    var envelope = inputVc.Buffer.Dequeue();
                    router.InputVirtualChannelRoundRobin[winningInput] = (winningVc + 1) % virtualChannels;
                    router.SwitchOutputRoundRobin[pair.Key] = (winningInput + 1) % Ports;
                    Add(crossbars, cycle + 1,
                        new Crossbar(router.Id, winningInput, pair.Key, inputVc.OutputVirtualChannel, envelope));
                    Event(cycle, "switch_grant", envelope.Flit.Packet, envelope.Flit.Id, router.Id,
                        winningInput, pair.Key, winningVc, detail: $"output_vc={inputVc.OutputVirtualChannel}");
                    if (envelope.Flit.IsTail)
                    {
                        if (inputVc.Buffer.Count > 0)
                        {
                            if (!inputVc.Buffer.Peek().Flit.IsHead)
                                throw new InvalidOperationException("A tail flit was followed by a non-head flit.");
                            inputVc.State = InputVcState.Routing;
                            inputVc.ReadyCycle = cycle + 1;
                        }
                        else
                        {
                            inputVc.State = InputVcState.Idle;
                        }
                        inputVc.OutputPort = -1;
                        inputVc.OutputVirtualChannel = -1;
                        inputVc.AllowedOutputVirtualChannels = Array.Empty<int>();
                    }
                    else
                    {
                        inputVc.State = InputVcState.Active;
                        inputVc.ReadyCycle = cycle + 1;
                    }
                }
            }
        }

        private int InjectionVc(AspdacStageNocPacket packet)
        {
            if (options.Routing == AspdacStageNocRouting.XyYx &&
                !options.UseBooksimRouteRandom &&
                string.Equals(packet.RouteMode, "yx", StringComparison.Ordinal))
            {
                return options.VirtualChannels / 2;
            }
            return 0;
        }

        private int Route(int routerId, AspdacStageNocPacket packet)
        {
            if (options.Routing == AspdacStageNocRouting.Romm)
            {
                var phase = RommPhase(routerId, packet);
                if (routerId == packet.DestinationRouter) return 4;
                var target = phase == 0 ? rommIntermediates[packet.PacketId] : packet.DestinationRouter;
                return DimensionOrderPort(routerId, target);
            }
            if (routerId == packet.DestinationRouter) return 4;
            var mode = EffectiveRouteMode(packet, routerId);
            var x = routerId % Dimension;
            var y = routerId / Dimension;
            var destinationX = packet.DestinationRouter % Dimension;
            var destinationY = packet.DestinationRouter / Dimension;
            if (options.Routing == AspdacStageNocRouting.XyYx &&
                string.Equals(mode, "yx", StringComparison.Ordinal) &&
                y != destinationY)
            {
                return y < destinationY ? 2 : 3;
            }
            if (x != destinationX) return x < destinationX ? 0 : 1;
            return y < destinationY ? 2 : 3;
        }

        private int[] AllowedVirtualChannels(int routerId, AspdacStageNocPacket packet)
        {
            if (routerId == packet.DestinationRouter ||
                options.Routing == AspdacStageNocRouting.DimensionOrder)
            {
                return Enumerable.Range(0, options.VirtualChannels).ToArray();
            }
            var half = options.VirtualChannels / 2;
            if (options.Routing == AspdacStageNocRouting.Romm)
            {
                return RommPhase(routerId, packet) == 0
                    ? Enumerable.Range(0, half).ToArray()
                    : Enumerable.Range(half, options.VirtualChannels - half).ToArray();
            }
            return string.Equals(EffectiveRouteMode(packet, routerId), "yx", StringComparison.Ordinal)
                ? Enumerable.Range(half, options.VirtualChannels - half).ToArray()
                : Enumerable.Range(0, half).ToArray();
        }

        private string EffectiveRouteMode(AspdacStageNocPacket packet, int routerId)
        {
            if (options.Routing != AspdacStageNocRouting.XyYx) return packet.RouteMode;
            if (!options.UseBooksimRouteRandom) return packet.RouteMode;
            if (!generatedRouteModes.ContainsKey(packet.PacketId) && routerId == packet.SourceRouter)
            {
                generatedRouteModes[packet.PacketId] = routeRandom!.RandomInt(1) > 0 ? "xy" : "yx";
            }
            return generatedRouteModes.TryGetValue(packet.PacketId, out var mode) ? mode : packet.RouteMode;
        }

        private int RommPhase(int routerId, AspdacStageNocPacket packet)
        {
            var intermediate = EnsureRommIntermediate(packet);
            if (rommPhases[packet.PacketId] == 0 && routerId == intermediate) rommPhases[packet.PacketId] = 1;
            return rommPhases[packet.PacketId];
        }

        private int EnsureRommIntermediate(AspdacStageNocPacket packet)
        {
            if (rommIntermediates.TryGetValue(packet.PacketId, out var existing)) return existing;
            var source = packet.SourceRouter;
            var destination = packet.DestinationRouter;
            var intermediate = 0;
            var offset = 1;
            for (var dimension = 0; dimension < 2; dimension++)
            {
                var distance = destination % Dimension - source % Dimension;
                var coordinate = distance > 0
                    ? source % Dimension + routeRandom!.RandomInt(distance)
                    : destination % Dimension + routeRandom!.RandomInt(-distance);
                intermediate += offset * coordinate;
                offset *= Dimension;
                source /= Dimension;
                destination /= Dimension;
            }
            rommIntermediates.Add(packet.PacketId, intermediate);
            rommPhases.Add(packet.PacketId, 0);
            return intermediate;
        }

        private static int DimensionOrderPort(int routerId, int destination)
        {
            var x = routerId % Dimension;
            var y = routerId / Dimension;
            var destinationX = destination % Dimension;
            var destinationY = destination / Dimension;
            if (x != destinationX) return x < destinationX ? 0 : 1;
            return y < destinationY ? 2 : 3;
        }

        private static (int Router, int InputPort) NextHop(int routerId, int outputPort) => outputPort switch
        {
            0 => (routerId + 1, 1),
            1 => (routerId - 1, 0),
            2 => (routerId + Dimension, 3),
            3 => (routerId - Dimension, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(outputPort))
        };

        private void Event(
            long cycle,
            string eventType,
            AspdacStageNocPacket? packet = null,
            int flitId = -1,
            int router = -1,
            int inputPort = -1,
            int outputPort = -1,
            int virtualChannel = -1,
            int occupancy = -1,
            string detail = "")
        {
            events.Add(new AspdacStageNocEvent(
                cycle,
                eventType,
                packet?.PacketId ?? "",
                flitId,
                router,
                inputPort,
                outputPort,
                virtualChannel,
                occupancy,
                detail));
        }

        private static int RoundRobin(IEnumerable<int> candidates, int cursor, int size)
        {
            var available = candidates.ToHashSet();
            for (var offset = 0; offset < size; offset++)
            {
                var candidate = (cursor + offset) % size;
                if (available.Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("Round-robin selection had no candidates.");
        }

        private static void Validate(AspdacStageNocOptions options, IReadOnlyList<AspdacStageNocPacket> packets)
        {
            if (options.BufferDepth <= 0 || options.VirtualChannels <= 0 || options.MaxCycles <= 0)
                throw new ArgumentOutOfRangeException(nameof(options));
            if ((options.Routing == AspdacStageNocRouting.XyYx || options.Routing == AspdacStageNocRouting.Romm) &&
                (options.VirtualChannels < 2 || options.VirtualChannels % 2 != 0))
                throw new ArgumentException("XY/YX and ROMM require a positive even VC count of at least two.", nameof(options));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var packet in packets)
            {
                if (string.IsNullOrWhiteSpace(packet.PacketId) || !ids.Add(packet.PacketId))
                    throw new ArgumentException("STAGE NoC packet ids must be non-empty and unique.", nameof(packets));
                if (packet.ReleaseCycle < 0 || packet.SourceRouter is < 0 or >= RouterCount ||
                    packet.DestinationRouter is < 0 or >= RouterCount || packet.Flits <= 0)
                    throw new ArgumentException($"Packet '{packet.PacketId}' has an invalid release, endpoint, or flit count.", nameof(packets));
                if (!string.Equals(packet.RouteMode, "xy", StringComparison.Ordinal) &&
                    !string.Equals(packet.RouteMode, "yx", StringComparison.Ordinal))
                    throw new ArgumentException($"Packet '{packet.PacketId}' has an unsupported route mode.", nameof(packets));
            }
        }

        private static string HashEvents(IEnumerable<AspdacStageNocEvent> rows)
        {
            var canonical = string.Join((char)10, rows.Select(row =>
                $"{row.Cycle},{row.EventType},{row.PacketId},{row.FlitId},{row.Router},{row.InputPort},{row.OutputPort},{row.VirtualChannel},{row.Occupancy},{row.Detail}"));
            return Sha256Hex(canonical);
        }

        private static string HashProvenance(IEnumerable<AspdacStageNocPacket> rows)
        {
            var canonical = string.Join((char)10, rows
                .OrderBy(row => row.PacketId, StringComparer.Ordinal)
                .Select(row =>
                    $"{row.PacketId},{row.LayerId},{row.TensorRole},{row.MappingId},{row.SourceComponentId},{row.DestinationComponentId},{row.RouteResourceId}"));
            return Sha256Hex(canonical);
        }

        private static string Sha256Hex(string canonical)
        {
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static IReadOnlyList<T> Take<T>(Dictionary<long, List<T>> schedule, long cycle)
        {
            if (!schedule.Remove(cycle, out var rows)) return Array.Empty<T>();
            return rows;
        }

        private static void Add<T>(Dictionary<long, List<T>> schedule, long cycle, T row)
        {
            if (!schedule.TryGetValue(cycle, out var rows))
            {
                rows = new List<T>();
                schedule.Add(cycle, rows);
            }
            rows.Add(row);
        }

        private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> rows, TKey key, TValue value)
            where TKey : notnull
        {
            if (!rows.TryGetValue(key, out var values))
            {
                values = new List<TValue>();
                rows.Add(key, values);
            }
            values.Add(value);
        }

        private sealed record SourceActive(AspdacStageNocPacket Packet, int Index, int VirtualChannel);
        private sealed record Flit(int Id, AspdacStageNocPacket Packet, int Index)
        {
            public bool IsHead => Index == 0;
            public bool IsTail => Index == Packet.Flits - 1;
        }
        private enum UpstreamKind { Source, Router }
        private sealed record Upstream(UpstreamKind Kind, int Owner, int OutputPort, int VirtualChannel);
        private sealed record Envelope(Flit Flit, long InjectionCycle, int InputVirtualChannel, Upstream Upstream);
        private sealed record Arrival(int Router, int InputPort, Envelope Envelope);
        private sealed record Crossbar(int Router, int InputPort, int OutputPort, int OutputVirtualChannel, Envelope Envelope);
        private enum InputVcState { Idle, Routing, VirtualChannelAllocation, Active }

        private sealed class InputVc
        {
            public Queue<Envelope> Buffer { get; } = new();
            public InputVcState State { get; set; }
            public long ReadyCycle { get; set; }
            public int OutputPort { get; set; } = -1;
            public int OutputVirtualChannel { get; set; } = -1;
            public int[] AllowedOutputVirtualChannels { get; set; } = Array.Empty<int>();
        }

        private sealed class Router
        {
            public Router(int id, int depth, int virtualChannels)
            {
                Id = id;
                Inputs = new InputVc[Ports, virtualChannels];
                for (var port = 0; port < Ports; port++)
                    for (var vc = 0; vc < virtualChannels; vc++)
                        Inputs[port, vc] = new InputVc();
                OutputBusy = new bool[Ports, virtualChannels];
                OutputCredits = new int[Ports, virtualChannels];
                for (var port = 0; port < Ports; port++)
                    for (var vc = 0; vc < virtualChannels; vc++)
                        OutputCredits[port, vc] = depth;
                var expanded = Ports * virtualChannels;
                VirtualChannelOutputRoundRobin = new int[expanded];
                VirtualChannelInputRoundRobin = new int[expanded];
                SwitchOutputRoundRobin = new int[Ports];
                InputVirtualChannelRoundRobin = new int[Ports];
            }

            public int Id { get; }
            public InputVc[,] Inputs { get; }
            public bool[,] OutputBusy { get; }
            public int[,] OutputCredits { get; }
            public int[] VirtualChannelOutputRoundRobin { get; }
            public int[] VirtualChannelInputRoundRobin { get; }
            public int[] SwitchOutputRoundRobin { get; }
            public int[] InputVirtualChannelRoundRobin { get; }
        }
    }

    private sealed class BooksimIntegerRandom
    {
        private const int Kk = 100;
        private const int Ll = 37;
        private const int Quality = 1009;
        private const int Tt = 70;
        private const int Modulus = 1 << 30;
        private readonly int[] state = new int[Kk];
        private int[] buffer = System.Array.Empty<int>();
        private int pointer = -1;

        public BooksimIntegerRandom(int seed) => Start(seed);

        public int RandomInt(int maximum)
        {
            if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            return Next() % (maximum + 1);
        }

        private static int ModDiff(int left, int right) => unchecked(left - right) & (Modulus - 1);

        private int[] Array(int count)
        {
            var values = new int[count];
            System.Array.Copy(state, values, Kk);
            for (var index = Kk; index < count; index++)
                values[index] = ModDiff(values[index - Kk], values[index - Ll]);
            var cursor = count;
            for (var index = 0; index < Ll; index++)
            {
                state[index] = ModDiff(values[cursor - Kk], values[cursor - Ll]);
                cursor++;
            }
            for (var index = Ll; index < Kk; index++)
            {
                state[index] = ModDiff(values[cursor - Kk], state[index - Ll]);
                cursor++;
            }
            return values;
        }

        private void Start(int seed)
        {
            var work = new int[Kk + Kk - 1];
            var shift = ((long)seed + 2) & (Modulus - 2);
            for (var index = 0; index < Kk; index++)
            {
                work[index] = (int)shift;
                shift <<= 1;
                if (shift >= Modulus) shift -= Modulus - 2;
            }
            work[1]++;
            var selector = seed & (Modulus - 1);
            var turns = Tt - 1;
            while (turns != 0)
            {
                for (var index = Kk - 1; index > 0; index--)
                {
                    work[index + index] = work[index];
                    work[index + index - 1] = 0;
                }
                for (var index = Kk + Kk - 2; index >= Kk; index--)
                {
                    work[index - (Kk - Ll)] = ModDiff(work[index - (Kk - Ll)], work[index]);
                    work[index - Kk] = ModDiff(work[index - Kk], work[index]);
                }
                if ((selector & 1) != 0)
                {
                    for (var index = Kk; index > 0; index--) work[index] = work[index - 1];
                    work[0] = work[Kk];
                    work[Ll] = ModDiff(work[Ll], work[Kk]);
                }
                if (selector != 0) selector >>= 1;
                else turns--;
            }
            for (var index = 0; index < Ll; index++) state[index + Kk - Ll] = work[index];
            for (var index = Ll; index < Kk; index++) state[index - Ll] = work[index];
            for (var warmup = 0; warmup < 10; warmup++) _ = Array(Kk + Kk - 1);
            buffer = System.Array.Empty<int>();
            pointer = -1;
        }

        private int Next()
        {
            if (pointer >= 0 && pointer < buffer.Length && buffer[pointer] >= 0)
                return buffer[pointer++];
            buffer = Array(Quality);
            buffer[Kk] = -1;
            pointer = 1;
            return buffer[0];
        }
    }

    private sealed class BooksimFloatingRandom
    {
        private const int Kk = 100;
        private const int Ll = 37;
        private const int Quality = 1009;
        private const int Tt = 70;
        private readonly double[] state = new double[Kk];
        private double[] buffer = System.Array.Empty<double>();
        private int pointer = -1;

        public BooksimFloatingRandom(int seed) => Start(seed);

        public double Next()
        {
            if (pointer >= 0 && pointer < buffer.Length && buffer[pointer] >= 0d)
                return buffer[pointer++];
            buffer = Array(Quality);
            buffer[Kk] = -1d;
            pointer = 1;
            return buffer[0];
        }

        private static double ModSum(double left, double right)
        {
            var total = left + right;
            return total - (long)total;
        }

        private double[] Array(int count)
        {
            var values = new double[count];
            System.Array.Copy(state, values, Kk);
            for (var index = Kk; index < count; index++)
                values[index] = ModSum(values[index - Kk], values[index - Ll]);
            var cursor = count;
            for (var index = 0; index < Ll; index++)
            {
                state[index] = ModSum(values[cursor - Kk], values[cursor - Ll]);
                cursor++;
            }
            for (var index = Ll; index < Kk; index++)
            {
                state[index] = ModSum(values[cursor - Kk], state[index - Ll]);
                cursor++;
            }
            return values;
        }

        private void Start(int seed)
        {
            var work = new double[Kk + Kk - 1];
            var ulp = (1d / (1 << 30)) / (1 << 22);
            var shift = 2d * ulp * ((seed & 0x3fffffff) + 2);
            for (var index = 0; index < Kk; index++)
            {
                work[index] = shift;
                shift += shift;
                if (shift >= 1d) shift -= 1d - 2d * ulp;
            }
            work[1] += ulp;
            var selector = seed & 0x3fffffff;
            var turns = Tt - 1;
            while (turns != 0)
            {
                for (var index = Kk - 1; index > 0; index--)
                {
                    work[index + index] = work[index];
                    work[index + index - 1] = 0d;
                }
                for (var index = Kk + Kk - 2; index >= Kk; index--)
                {
                    work[index - (Kk - Ll)] = ModSum(work[index - (Kk - Ll)], work[index]);
                    work[index - Kk] = ModSum(work[index - Kk], work[index]);
                }
                if ((selector & 1) != 0)
                {
                    for (var index = Kk; index > 0; index--) work[index] = work[index - 1];
                    work[0] = work[Kk];
                    work[Ll] = ModSum(work[Ll], work[Kk]);
                }
                if (selector != 0) selector >>= 1;
                else turns--;
            }
            for (var index = 0; index < Ll; index++) state[index + Kk - Ll] = work[index];
            for (var index = Ll; index < Kk; index++) state[index - Ll] = work[index];
            for (var warmup = 0; warmup < 10; warmup++) _ = Array(Kk + Kk - 1);
            buffer = System.Array.Empty<double>();
            pointer = -1;
        }
    }
}
