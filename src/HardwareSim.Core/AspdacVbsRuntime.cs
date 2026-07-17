using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Defines one frozen V-BS packet-network experiment executed by the STAGE flit runtime.</summary>
public sealed record AspdacVbsOptions(
    string Traffic,
    double InjectionRate,
    int Seed,
    int WarmupCycles,
    int MeasurementCycles,
    int DrainCycles,
    bool RetainEventHash = false);

/// <summary>Defines one canonical one-flit packet offer loaded from a shared CNN CSV trace.</summary>
public sealed record AspdacVbsTracePacket(
    string PacketId,
    long ReleaseCycle,
    int SourceRouter,
    int DestinationRouter,
    int Flits,
    int TrafficClass,
    string LayerId,
    string TensorRole,
    int PayloadBits);

/// <summary>Provides packet-level V-BS results for one externally supplied canonical trace.</summary>
public sealed class AspdacVbsTraceRunResult
{
    /// <summary>Gets the SHA-256 of the exact shared CSV bytes.</summary>
    public string TraceSha256 { get; init; } = "";
    /// <summary>Gets whether every trace packet drained.</summary>
    public bool Completed { get; init; }
    /// <summary>Gets whether the configured drain bound was reached.</summary>
    public bool Timeout { get; init; }
    /// <summary>Gets all simulated cycles.</summary>
    public long TotalCycles { get; init; }
    /// <summary>Gets the inclusive first-release to final-delivery interval.</summary>
    public long NetworkMakespanCycles { get; init; }
    /// <summary>Gets packet offers in the authenticated trace.</summary>
    public long OfferedPackets { get; init; }
    /// <summary>Gets packets admitted to source routers.</summary>
    public long InjectedPackets { get; init; }
    /// <summary>Gets packets delivered to endpoint sinks.</summary>
    public long DeliveredPackets { get; init; }
    /// <summary>Gets packets remaining at timeout.</summary>
    public long UndrainedPackets { get; init; }
    /// <summary>Gets mean release-to-delivery latency.</summary>
    public double PacketLatencyAverage { get; init; }
    /// <summary>Gets nearest-rank p95 release-to-delivery latency.</summary>
    public double PacketLatencyP95 { get; init; }
    /// <summary>Gets delivered packets per makespan cycle.</summary>
    public double ThroughputPacketsPerCycle { get; init; }
    /// <summary>Gets average router input-VC occupancy.</summary>
    public double AverageQueueOccupancyFlits { get; init; }
    /// <summary>Gets peak router input-VC occupancy.</summary>
    public int MaxQueueOccupancyFlits { get; init; }
    /// <summary>Gets cycles containing conflict or backpressure.</summary>
    public long CongestionCycles { get; init; }
    /// <summary>Gets same-output arbitration stalls.</summary>
    public long RouterConflictStalls { get; init; }
    /// <summary>Gets downstream VC backpressure stall events.</summary>
    public long BackpressureEvents { get; init; }
    /// <summary>Gets blocked source-injection attempts.</summary>
    public long InjectionQueueStalls { get; init; }
    /// <summary>Gets the canonical delivery-record hash.</summary>
    public string CanonicalDeliveryTraceHash { get; init; } = "";
    /// <summary>Gets the detailed deterministic runtime-event hash.</summary>
    public string RuntimeEventHash { get; init; } = "";
    /// <summary>Gets stable stall counters by reason.</summary>
    public IReadOnlyDictionary<string, long> StallReasons { get; init; } = new Dictionary<string, long>();
    /// <summary>Gets the bounded-run completion reason.</summary>
    public string CompletionReason { get; init; } = "";
}

/// <summary>Provides tidy metrics and provenance from one V-BS packet-network run.</summary>
public sealed class AspdacVbsRunResult
{
    /// <summary>Gets whether every generated packet drained before the cycle bound.</summary>
    public bool Completed { get; init; }
    /// <summary>Gets whether the cycle bound was reached with undrained traffic.</summary>
    public bool Timeout { get; init; }
    /// <summary>Gets the preregistered per-run instability classification.</summary>
    public bool Unstable { get; init; }
    /// <summary>Gets executed cycle count including drain cycles.</summary>
    public long TotalCycles { get; init; }
    /// <summary>Gets all traffic-generator packet offers.</summary>
    public long OfferedPackets { get; init; }
    /// <summary>Gets packets accepted into local router VCs.</summary>
    public long InjectedPackets { get; init; }
    /// <summary>Gets packets delivered to endpoint sinks.</summary>
    public long DeliveredPackets { get; init; }
    /// <summary>Gets offers inside the measurement window.</summary>
    public long MeasuredOfferedPackets { get; init; }
    /// <summary>Gets delivered packets whose generation occurred inside the measurement window.</summary>
    public long MeasuredDeliveredPackets { get; init; }
    /// <summary>Gets mean offered packets per endpoint per measurement cycle.</summary>
    public double OfferedRateAverage { get; init; }
    /// <summary>Gets mean accepted packets per endpoint per measurement cycle.</summary>
    public double AcceptedRateAverage { get; init; }
    /// <summary>Gets measured accepted packets divided by measured offered packets.</summary>
    public double AcceptedOfferedRatio { get; init; }
    /// <summary>Gets mean end-to-end packet latency in cycles.</summary>
    public double PacketLatencyAverage { get; init; }
    /// <summary>Gets nearest-rank 95th-percentile packet latency in cycles.</summary>
    public double PacketLatencyP95 { get; init; }
    /// <summary>Gets average flit occupancy per router input VC.</summary>
    public double AverageQueueOccupancyFlits { get; init; }
    /// <summary>Gets peak occupancy of any one router input VC.</summary>
    public int MaxQueueOccupancyFlits { get; init; }
    /// <summary>Gets cycles containing at least one conflict or backpressure stall.</summary>
    public long CongestionCycles { get; init; }
    /// <summary>Gets same-output router arbitration stall events.</summary>
    public long RouterConflictStalls { get; init; }
    /// <summary>Gets downstream-VC backpressure stall events.</summary>
    public long BackpressureCycles { get; init; }
    /// <summary>Gets source injection attempts blocked by a full local VC.</summary>
    public long InjectionQueueStalls { get; init; }
    /// <summary>Gets the provider event hash, or a metrics-only marker.</summary>
    public string RuntimeEventHash { get; init; } = "";
    /// <summary>Gets a human-readable completion reason.</summary>
    public string CompletionReason { get; init; } = "";
    /// <summary>Gets stable stall-reason counters.</summary>
    public IReadOnlyDictionary<string, long> StallReasons { get; init; } = new Dictionary<string, long>();
}

/// <summary>
/// Runs the frozen 4x4 V-BS topology with actual stochastic packet injection, one-flit
/// packets, per-input VC queues, deterministic XY routing, and per-output arbitration.
/// </summary>
public static class AspdacVbsRuntime
{
    /// <summary>Gets each mesh dimension size.</summary>
    public const int Dimension = 4;
    /// <summary>Gets the frozen router and endpoint count.</summary>
    public const int RouterCount = 16;
    /// <summary>Gets frozen packet and flit width in bits.</summary>
    public const int PacketBits = 128;
    /// <summary>Gets frozen per-direction link service width in bits per cycle.</summary>
    public const int LinkBitsPerCycle = 128;
    /// <summary>Gets frozen virtual channels per physical input.</summary>
    public const int VirtualChannels = 1;
    /// <summary>Gets frozen flit capacity per virtual channel.</summary>
    public const int FlitsPerVirtualChannel = 16;
    private static readonly string[] InputPorts = ["east", "local", "north", "south", "west"];

    /// <summary>Executes one V-BS run and returns measured packet, queue, and stall metrics.</summary>
    public static AspdacVbsRunResult Run(AspdacVbsOptions options)
    {
        Validate(options);
        var routers = Enumerable.Range(0, RouterCount)
            .Select(_ => new FastRouter(InputPorts, FlitsPerVirtualChannel))
            .ToArray();
        var rng = new StableRandom(unchecked((ulong)(uint)options.Seed) + 0x9e3779b97f4a7c15UL);
        var sourceQueues = Enumerable.Range(0, RouterCount).Select(_ => new Queue<PacketState>()).ToArray();
        var flights = new List<Flight>();
        var packets = new Dictionary<string, PacketState>(StringComparer.Ordinal);
        using var traceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var offeredPackets = 0L;
        var injectedPackets = 0L;
        var deliveredPackets = 0L;
        var measuredOfferedPackets = 0L;
        var measuredDeliveredPackets = 0L;
        var routerConflictStalls = 0L;
        var backpressureCycles = 0L;
        var injectionQueueStalls = 0L;
        var congestionCycles = 0L;
        var queueOccupancySamples = 0L;
        var maxQueueOccupancy = 0;
        var packetIndex = 0L;
        var measuredLatencies = new List<long>();
        var generationEnd = options.WarmupCycles + options.MeasurementCycles;
        var maxCycles = generationEnd + options.DrainCycles;
        var lastCycle = 0L;

        for (var cycle = 0; cycle < maxCycles; cycle++)
        {
            lastCycle = cycle + 1L;
            var cycleDelivered = 0;
            var cycleInjected = 0;
            var cycleConflicts = 0;
            var cycleBackpressure = 0;

            var arrivingFlights = flights.OrderBy(item => item.PacketId, StringComparer.Ordinal).ToList();
            flights = [];
            foreach (var flight in arrivingFlights)
            {
                var packet = packets[flight.PacketId];
                if (flight.DeliverToSink)
                {
                    packet.DeliveredCycle = cycle;
                    deliveredPackets++;
                    cycleDelivered++;
                    if (packet.GeneratedCycle >= options.WarmupCycles && packet.GeneratedCycle < generationEnd)
                    {
                        measuredDeliveredPackets++;
                        measuredLatencies.Add(cycle - packet.GeneratedCycle);
                    }
                    continue;
                }

                packet.CurrentRouter = flight.DestinationRouter;
                if (!routers[packet.CurrentRouter].TryAccept(flight.DestinationInputPort, packet))
                {
                    backpressureCycles++;
                    cycleBackpressure++;
                    flights.Add(flight);
                }
            }

            if (cycle < generationEnd)
            {
                for (var source = 0; source < RouterCount; source++)
                {
                    if (rng.NextDouble() >= options.InjectionRate) continue;
                    var destination = Destination(options.Traffic, source, ref rng);
                    var id = $"vbs-p{packetIndex++:D9}";
                    var packet = new Packet
                    {
                        Id = id,
                        PacketType = PacketType.Activation,
                        Bits = PacketBits,
                        NumElements = 16,
                        BitWidth = 8,
                        Precision = PrecisionKind.INT8,
                        SourceComponentId = $"endpoint-{source:D2}",
                        DestinationComponentId = $"endpoint-{destination:D2}",
                        InjectionCycle = cycle,
                        CreatedCycle = cycle,
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["traffic"] = options.Traffic,
                            ["source_router"] = source.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["destination_router"] = destination.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }
                    };
                    var state = new PacketState(packet, source, destination, cycle);
                    packets[id] = state;
                    sourceQueues[source].Enqueue(state);
                    offeredPackets++;
                    if (cycle >= options.WarmupCycles) measuredOfferedPackets++;
                }
            }

            for (var source = 0; source < RouterCount; source++)
            {
                if (sourceQueues[source].Count == 0) continue;
                var packet = sourceQueues[source].Peek();
                if (!routers[source].TryAccept("local", packet))
                {
                    injectionQueueStalls++;
                    continue;
                }
                sourceQueues[source].Dequeue();
                packet.InjectedCycle = cycle;
                injectedPackets++;
                cycleInjected++;
            }

            for (var routerId = 0; routerId < RouterCount; routerId++)
            {
                var blockedOutputs = BlockedOutputs(routerId, routers);
                var issue = routers[routerId].Issue(routerId, blockedOutputs);
                foreach (var stall in issue.Stalls)
                {
                    if (stall.BlockedByDownstream)
                    {
                        backpressureCycles++;
                        cycleBackpressure++;
                    }
                    else
                    {
                        routerConflictStalls++;
                        cycleConflicts++;
                    }
                }
                foreach (var grant in issue.Grants)
                {
                    var packet = grant.Packet;
                    if (grant.OutputPort == "local")
                    {
                        flights.Add(new Flight(packet.Packet.Id, routerId, "local", true));
                    }
                    else
                    {
                        var nextRouter = Neighbor(routerId, grant.OutputPort);
                        flights.Add(new Flight(packet.Packet.Id, nextRouter, Opposite(grant.OutputPort), false));
                    }
                }
            }

            var occupancy = routers.Sum(router => router.TotalOccupancy);
            queueOccupancySamples += occupancy;
            maxQueueOccupancy = Math.Max(maxQueueOccupancy, routers.Max(router => router.MaxInputOccupancy));
            if (cycleConflicts > 0 || cycleBackpressure > 0) congestionCycles++;
            if (options.RetainEventHash)
            {
                var line = $"{cycle},{cycleInjected},{cycleDelivered},{cycleConflicts},{cycleBackpressure},{occupancy}\n";
                traceHash.AppendData(Encoding.UTF8.GetBytes(line));
            }

            var generationComplete = cycle + 1 >= generationEnd;
            if (generationComplete && flights.Count == 0 && sourceQueues.All(queue => queue.Count == 0) &&
                routers.All(router => router.TotalOccupancy == 0))
            {
                break;
            }
        }

        var undrained = flights.Count + sourceQueues.Sum(queue => queue.Count) + routers.Sum(router => router.TotalOccupancy);
        var timeout = undrained > 0;
        var denominator = (double)(options.MeasurementCycles * RouterCount);
        var offeredRate = measuredOfferedPackets / denominator;
        var acceptedRate = measuredDeliveredPackets / denominator;
        var acceptedOfferedRatio = measuredOfferedPackets == 0 ? 1d : (double)measuredDeliveredPackets / measuredOfferedPackets;
        var averageLatency = measuredLatencies.Count == 0 ? double.NaN : measuredLatencies.Average();
        var p95Latency = Percentile95(measuredLatencies);
        var eventHash = options.RetainEventHash
            ? BitConverter.ToString(traceHash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant()
            : "not_retained_metrics_only";
        var completed = !timeout;
        return new AspdacVbsRunResult
        {
            Completed = completed,
            Timeout = timeout,
            Unstable = timeout || acceptedOfferedRatio < 0.95d,
            TotalCycles = lastCycle,
            OfferedPackets = offeredPackets,
            InjectedPackets = injectedPackets,
            DeliveredPackets = deliveredPackets,
            MeasuredOfferedPackets = measuredOfferedPackets,
            MeasuredDeliveredPackets = measuredDeliveredPackets,
            OfferedRateAverage = offeredRate,
            AcceptedRateAverage = acceptedRate,
            AcceptedOfferedRatio = acceptedOfferedRatio,
            PacketLatencyAverage = averageLatency,
            PacketLatencyP95 = p95Latency,
            AverageQueueOccupancyFlits = lastCycle == 0 ? 0d : (double)queueOccupancySamples / (lastCycle * RouterCount * InputPorts.Length * VirtualChannels),
            MaxQueueOccupancyFlits = maxQueueOccupancy,
            CongestionCycles = congestionCycles,
            RouterConflictStalls = routerConflictStalls,
            BackpressureCycles = backpressureCycles,
            InjectionQueueStalls = injectionQueueStalls,
            RuntimeEventHash = eventHash,
            CompletionReason = completed ? "All generated packets drained." : $"Cycle bound reached with {undrained} flits or packets undrained.",
            StallReasons = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["router_conflict"] = routerConflictStalls,
                ["downstream_vc_full"] = backpressureCycles,
                ["injection_vc_full"] = injectionQueueStalls
            }
        };
    }

    /// <summary>Loads, authenticates, and executes one canonical CNN packet trace on the frozen V-BS network.</summary>
    public static AspdacVbsTraceRunResult RunTraceCsv(string traceCsvPath, string expectedSha256, int drainCycles)
    {
        if (string.IsNullOrWhiteSpace(traceCsvPath)) throw new ArgumentException("Trace CSV path is required.", nameof(traceCsvPath));
        if (string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Expected trace SHA-256 must contain exactly 64 hexadecimal characters.", nameof(expectedSha256));
        if (drainCycles < 0) throw new ArgumentOutOfRangeException(nameof(drainCycles));

        var path = Path.GetFullPath(traceCsvPath);
        var bytes = File.ReadAllBytes(path);
        var traceSha256 = Sha256Hex(bytes);
        if (!string.Equals(traceSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"CNN trace SHA-256 mismatch: expected {expectedSha256.ToLowerInvariant()}, actual {traceSha256}.");

        var rows = ParseTraceCsv(bytes);
        return RunTrace(rows, traceSha256, drainCycles);
    }

    /// <summary>Executes already resolved canonical CNN packet offers on the frozen V-BS network.</summary>
    public static AspdacVbsTraceRunResult RunTrace(
        IReadOnlyList<AspdacVbsTracePacket> trace,
        string traceSha256,
        int drainCycles)
    {
        if (trace is null || trace.Count == 0) throw new ArgumentException("CNN trace must contain at least one packet.", nameof(trace));
        if (drainCycles < 0) throw new ArgumentOutOfRangeException(nameof(drainCycles));
        ValidateTrace(trace);

        var offers = trace.OrderBy(packet => packet.ReleaseCycle).ThenBy(packet => packet.PacketId, StringComparer.Ordinal).ToArray();
        var routers = Enumerable.Range(0, RouterCount).Select(_ => new FastRouter(InputPorts, FlitsPerVirtualChannel)).ToArray();
        var sourceQueues = Enumerable.Range(0, RouterCount).Select(_ => new Queue<PacketState>()).ToArray();
        var flights = new List<Flight>();
        var packets = new Dictionary<string, PacketState>(StringComparer.Ordinal);
        using var runtimeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var deliveryHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var nextOffer = 0;
        var injectedPackets = 0L;
        var deliveredPackets = 0L;
        var routerConflictStalls = 0L;
        var backpressureCycles = 0L;
        var injectionQueueStalls = 0L;
        var congestionCycles = 0L;
        var queueOccupancySamples = 0L;
        var maxQueueOccupancy = 0;
        var latencies = new List<long>(offers.Length);
        var firstReleaseCycle = offers[0].ReleaseCycle;
        var lastReleaseCycle = offers[^1].ReleaseCycle;
        var lastDeliveredCycle = -1L;
        var maxCycles = checked(lastReleaseCycle + 1 + drainCycles);
        var lastCycle = 0L;

        for (var cycle = 0L; cycle < maxCycles; cycle++)
        {
            lastCycle = cycle + 1;
            var cycleDelivered = 0;
            var cycleInjected = 0;
            var cycleConflicts = 0;
            var cycleBackpressure = 0;

            var arrivingFlights = flights.OrderBy(item => item.PacketId, StringComparer.Ordinal).ToList();
            flights = [];
            foreach (var flight in arrivingFlights)
            {
                var packet = packets[flight.PacketId];
                if (flight.DeliverToSink)
                {
                    packet.DeliveredCycle = cycle;
                    deliveredPackets++;
                    cycleDelivered++;
                    lastDeliveredCycle = cycle;
                    var latency = cycle - packet.GeneratedCycle;
                    latencies.Add(latency);
                    AppendHash(runtimeHash, $"deliver,{cycle},{packet.Packet.Id},{latency}\n");
                    AppendHash(deliveryHash, $"{packet.Packet.Id},{packet.SourceRouter},{packet.DestinationRouter},{packet.GeneratedCycle},{cycle},{latency}\n");
                    continue;
                }

                packet.CurrentRouter = flight.DestinationRouter;
                if (!routers[packet.CurrentRouter].TryAccept(flight.DestinationInputPort, packet))
                {
                    backpressureCycles++;
                    cycleBackpressure++;
                    flights.Add(flight);
                    AppendHash(runtimeHash, $"flight_blocked,{cycle},{packet.Packet.Id},{packet.CurrentRouter},{flight.DestinationInputPort}\n");
                }
                else
                {
                    AppendHash(runtimeHash, $"flight_arrive,{cycle},{packet.Packet.Id},{packet.CurrentRouter},{flight.DestinationInputPort}\n");
                }
            }

            while (nextOffer < offers.Length && offers[nextOffer].ReleaseCycle == cycle)
            {
                var offer = offers[nextOffer++];
                var packet = new Packet
                {
                    Id = offer.PacketId,
                    PacketType = PacketType.Activation,
                    Bits = offer.PayloadBits,
                    NumElements = 1,
                    BitWidth = offer.PayloadBits,
                    Precision = PrecisionKind.Any,
                    SourceComponentId = $"endpoint-{offer.SourceRouter:D2}",
                    DestinationComponentId = $"endpoint-{offer.DestinationRouter:D2}",
                    InjectionCycle = cycle,
                    CreatedCycle = cycle,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["traffic"] = "cnn_trace",
                        ["traffic_class"] = offer.TrafficClass.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["layer_id"] = offer.LayerId,
                        ["tensor_role"] = offer.TensorRole,
                        ["source_router"] = offer.SourceRouter.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["destination_router"] = offer.DestinationRouter.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                };
                var state = new PacketState(packet, offer.SourceRouter, offer.DestinationRouter, cycle);
                packets.Add(packet.Id, state);
                sourceQueues[offer.SourceRouter].Enqueue(state);
                AppendHash(runtimeHash, $"release,{cycle},{packet.Id},{offer.SourceRouter},{offer.DestinationRouter},{offer.LayerId},{offer.TensorRole}\n");
            }

            for (var source = 0; source < RouterCount; source++)
            {
                if (sourceQueues[source].Count == 0) continue;
                var packet = sourceQueues[source].Peek();
                if (!routers[source].TryAccept("local", packet))
                {
                    injectionQueueStalls++;
                    AppendHash(runtimeHash, $"inject_blocked,{cycle},{packet.Packet.Id},{source}\n");
                    continue;
                }
                sourceQueues[source].Dequeue();
                packet.InjectedCycle = cycle;
                injectedPackets++;
                cycleInjected++;
                AppendHash(runtimeHash, $"inject,{cycle},{packet.Packet.Id},{source}\n");
            }

            for (var routerId = 0; routerId < RouterCount; routerId++)
            {
                var blockedOutputs = BlockedOutputs(routerId, routers);
                var issue = routers[routerId].Issue(routerId, blockedOutputs);
                foreach (var stall in issue.Stalls)
                {
                    if (stall.BlockedByDownstream)
                    {
                        backpressureCycles++;
                        cycleBackpressure++;
                    }
                    else
                    {
                        routerConflictStalls++;
                        cycleConflicts++;
                    }
                }
                foreach (var grant in issue.Grants)
                {
                    var packet = grant.Packet;
                    if (grant.OutputPort == "local")
                    {
                        flights.Add(new Flight(packet.Packet.Id, routerId, "local", true));
                        AppendHash(runtimeHash, $"issue_sink,{cycle},{packet.Packet.Id},{routerId}\n");
                    }
                    else
                    {
                        var nextRouter = Neighbor(routerId, grant.OutputPort);
                        flights.Add(new Flight(packet.Packet.Id, nextRouter, Opposite(grant.OutputPort), false));
                        AppendHash(runtimeHash, $"issue_link,{cycle},{packet.Packet.Id},{routerId},{grant.OutputPort},{nextRouter}\n");
                    }
                }
            }

            var occupancy = routers.Sum(router => router.TotalOccupancy);
            queueOccupancySamples += occupancy;
            maxQueueOccupancy = Math.Max(maxQueueOccupancy, routers.Max(router => router.MaxInputOccupancy));
            if (cycleConflicts > 0 || cycleBackpressure > 0) congestionCycles++;
            AppendHash(runtimeHash, $"cycle,{cycle},{cycleInjected},{cycleDelivered},{cycleConflicts},{cycleBackpressure},{occupancy}\n");

            var allReleased = nextOffer == offers.Length;
            if (allReleased && flights.Count == 0 && sourceQueues.All(queue => queue.Count == 0) && routers.All(router => router.TotalOccupancy == 0))
                break;
        }

        var undrained = offers.LongLength - deliveredPackets;
        var timeout = undrained > 0;
        var makespan = timeout
            ? Math.Max(0, lastCycle - firstReleaseCycle)
            : Math.Max(0, lastDeliveredCycle - firstReleaseCycle + 1);
        var averageLatency = latencies.Count == 0 ? double.NaN : latencies.Average();
        var p95Latency = Percentile95(latencies);
        return new AspdacVbsTraceRunResult
        {
            TraceSha256 = traceSha256.ToLowerInvariant(),
            Completed = !timeout,
            Timeout = timeout,
            TotalCycles = lastCycle,
            NetworkMakespanCycles = makespan,
            OfferedPackets = offers.LongLength,
            InjectedPackets = injectedPackets,
            DeliveredPackets = deliveredPackets,
            UndrainedPackets = undrained,
            PacketLatencyAverage = averageLatency,
            PacketLatencyP95 = p95Latency,
            ThroughputPacketsPerCycle = makespan == 0 ? 0d : deliveredPackets / (double)makespan,
            AverageQueueOccupancyFlits = lastCycle == 0 ? 0d : (double)queueOccupancySamples / (lastCycle * RouterCount * InputPorts.Length * VirtualChannels),
            MaxQueueOccupancyFlits = maxQueueOccupancy,
            CongestionCycles = congestionCycles,
            RouterConflictStalls = routerConflictStalls,
            BackpressureEvents = backpressureCycles,
            InjectionQueueStalls = injectionQueueStalls,
            CanonicalDeliveryTraceHash = Hex(deliveryHash.GetHashAndReset()),
            RuntimeEventHash = Hex(runtimeHash.GetHashAndReset()),
            CompletionReason = timeout ? $"Drain bound reached with {undrained} packets undrained." : "All canonical trace packets drained.",
            StallReasons = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["router_conflict"] = routerConflictStalls,
                ["downstream_vc_full"] = backpressureCycles,
                ["injection_vc_full"] = injectionQueueStalls
            }
        };
    }

    private static IReadOnlyList<AspdacVbsTracePacket> ParseTraceCsv(byte[] bytes)
    {
        const string expectedHeader = "packet_id,release_cycle,source,destination,flits,traffic_class,layer_id,tensor_role,payload_bits";
        var content = new UTF8Encoding(false, true).GetString(bytes);
        using var reader = new StringReader(content);
        var header = (reader.ReadLine() ?? "").TrimStart('\uFEFF');
        if (!string.Equals(header, expectedHeader, StringComparison.Ordinal))
            throw new InvalidDataException($"CNN trace CSV header must be exactly '{expectedHeader}'.");

        var rows = new List<AspdacVbsTracePacket>();
        var lineNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0) continue;
            var fields = line.Split(',', StringSplitOptions.None);
            if (fields.Length != 9) throw new InvalidDataException($"CNN trace CSV line {lineNumber} must contain exactly nine unquoted fields.");
            try
            {
                rows.Add(new AspdacVbsTracePacket(
                    fields[0],
                    long.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(fields[5], System.Globalization.CultureInfo.InvariantCulture),
                    fields[6],
                    fields[7],
                    int.Parse(fields[8], System.Globalization.CultureInfo.InvariantCulture)));
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"CNN trace CSV line {lineNumber} contains an invalid integer.", exception);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"CNN trace CSV line {lineNumber} contains an out-of-range integer.", exception);
            }
        }
        return rows;
    }

    private static void ValidateTrace(IReadOnlyList<AspdacVbsTracePacket> trace)
    {
        var packetIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceReleaseSlots = new HashSet<(long ReleaseCycle, int SourceRouter)>();
        foreach (var packet in trace)
        {
            if (string.IsNullOrWhiteSpace(packet.PacketId) || !packetIds.Add(packet.PacketId))
                throw new InvalidDataException($"CNN trace packet id '{packet.PacketId}' is empty or duplicated.");
            if (packet.ReleaseCycle < 0) throw new InvalidDataException($"CNN trace packet '{packet.PacketId}' has a negative release cycle.");
            if (packet.SourceRouter < 0 || packet.SourceRouter >= RouterCount || packet.DestinationRouter < 0 || packet.DestinationRouter >= RouterCount)
                throw new InvalidDataException($"CNN trace packet '{packet.PacketId}' endpoint is outside 0..{RouterCount - 1}.");
            if (packet.SourceRouter == packet.DestinationRouter)
                throw new InvalidDataException($"CNN trace packet '{packet.PacketId}' must cross at least one router link.");
            if (packet.Flits != 1 || packet.PayloadBits != PacketBits || packet.TrafficClass != 0)
                throw new InvalidDataException($"CNN trace packet '{packet.PacketId}' must use flits=1, payload_bits={PacketBits}, and traffic_class=0.");
            if (string.IsNullOrWhiteSpace(packet.LayerId) || string.IsNullOrWhiteSpace(packet.TensorRole))
                throw new InvalidDataException($"CNN trace packet '{packet.PacketId}' requires layer_id and tensor_role.");
            if (!sourceReleaseSlots.Add((packet.ReleaseCycle, packet.SourceRouter)))
                throw new InvalidDataException(
                    $"CNN trace endpoint {packet.SourceRouter} offers more than one packet at release cycle {packet.ReleaseCycle}.");
        }
    }

    private static void AppendHash(IncrementalHash hash, string value) => hash.AppendData(Encoding.UTF8.GetBytes(value));

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return Hex(sha256.ComputeHash(bytes));
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    private static HashSet<string> BlockedOutputs(int routerId, IReadOnlyList<FastRouter> routers)
    {
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in new[] { "east", "north", "south", "west" })
        {
            if (!HasNeighbor(routerId, output))
            {
                blocked.Add(output);
                continue;
            }
            var neighbor = Neighbor(routerId, output);
            var input = Opposite(output);
            if (routers[neighbor].InputOccupancy(input) >= FlitsPerVirtualChannel) blocked.Add(output);
        }
        return blocked;
    }

    private static string ResolveOutput(int routerId, int destinationRouter) =>
        RouterRuntime.ResolveOutputPort(
            new Packet { DestinationComponentId = $"endpoint-{destinationRouter:D2}" },
            Position(routerId),
            Position(destinationRouter));

    private static GridPosition Position(int routerId) => new(routerId % Dimension, routerId / Dimension);

    private static bool HasNeighbor(int routerId, string output)
    {
        var position = Position(routerId);
        return output switch
        {
            "east" => position.X + 1 < Dimension,
            "west" => position.X > 0,
            "south" => position.Y + 1 < Dimension,
            "north" => position.Y > 0,
            _ => false
        };
    }

    private static int Neighbor(int routerId, string output)
    {
        if (!HasNeighbor(routerId, output)) throw new InvalidOperationException($"Router {routerId} has no {output} neighbor.");
        return output switch
        {
            "east" => routerId + 1,
            "west" => routerId - 1,
            "south" => routerId + Dimension,
            "north" => routerId - Dimension,
            _ => throw new ArgumentOutOfRangeException(nameof(output), output, "Unknown mesh output.")
        };
    }

    private static string Opposite(string output) => output switch
    {
        "east" => "west",
        "west" => "east",
        "south" => "north",
        "north" => "south",
        _ => throw new ArgumentOutOfRangeException(nameof(output), output, "Unknown mesh output.")
    };

    private static int Destination(string traffic, int source, ref StableRandom rng) => traffic switch
    {
        "uniform" => rng.NextInt(RouterCount),
        "transpose" => (source % Dimension) * Dimension + source / Dimension,
        "bit_complement" => source ^ (RouterCount - 1),
        "hotspot_node5" => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(traffic), traffic, "Unsupported V-BS traffic pattern.")
    };

    private static double Percentile95(List<long> values)
    {
        if (values.Count == 0) return double.NaN;
        values.Sort();
        var index = (int)Math.Ceiling(0.95d * values.Count) - 1;
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }

    private static void Validate(AspdacVbsOptions options)
    {
        if (options.InjectionRate < 0d || options.InjectionRate > 1d) throw new ArgumentOutOfRangeException(nameof(options.InjectionRate));
        if (options.WarmupCycles < 0 || options.MeasurementCycles <= 0 || options.DrainCycles < 0) throw new ArgumentOutOfRangeException(nameof(options));
        var validationRandom = new StableRandom(1);
        _ = Destination(options.Traffic, 0, ref validationRandom);
    }

    private sealed class PacketState
    {
        public PacketState(Packet packet, int sourceRouter, int destinationRouter, long generatedCycle)
        {
            Packet = packet;
            SourceRouter = sourceRouter;
            DestinationRouter = destinationRouter;
            CurrentRouter = sourceRouter;
            GeneratedCycle = generatedCycle;
        }

        public Packet Packet { get; }
        public int SourceRouter { get; }
        public int DestinationRouter { get; }
        public int CurrentRouter { get; set; }
        public long GeneratedCycle { get; }
        public long? InjectedCycle { get; set; }
        public long? DeliveredCycle { get; set; }
    }

    private sealed record Flight(string PacketId, int DestinationRouter, string DestinationInputPort, bool DeliverToSink);

    private sealed class FastRouter
    {
        private readonly SortedDictionary<string, Queue<PacketState>> inputs = new(StringComparer.Ordinal);
        private readonly int depth;

        public FastRouter(IEnumerable<string> inputPorts, int depth)
        {
            this.depth = depth;
            foreach (var port in inputPorts.OrderBy(port => port, StringComparer.Ordinal)) inputs[port] = new Queue<PacketState>();
        }

        public int TotalOccupancy => inputs.Values.Sum(queue => queue.Count);
        public int MaxInputOccupancy => inputs.Values.Max(queue => queue.Count);
        public int InputOccupancy(string inputPort) => inputs[inputPort].Count;

        public bool TryAccept(string inputPort, PacketState packet)
        {
            var queue = inputs[inputPort];
            if (queue.Count >= depth) return false;
            queue.Enqueue(packet);
            return true;
        }

        public FastIssueResult Issue(int routerId, ISet<string> blockedOutputs)
        {
            var result = new FastIssueResult();
            var requests = inputs
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => new FastRequest(pair.Key, pair.Value.Peek(), ResolveOutput(routerId, pair.Value.Peek().DestinationRouter)))
                .OrderBy(request => request.OutputPort, StringComparer.Ordinal)
                .ThenBy(request => request.InputPort, StringComparer.Ordinal)
                .ThenBy(request => request.Packet.Packet.Id, StringComparer.Ordinal)
                .ToList();
            var usedOutputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in requests)
            {
                var blocked = blockedOutputs.Contains(request.OutputPort);
                if (blocked || !usedOutputs.Add(request.OutputPort))
                {
                    result.Stalls.Add(new FastStall(request.OutputPort, blocked));
                    continue;
                }
                inputs[request.InputPort].Dequeue();
                result.Grants.Add(new FastGrant(request.Packet, request.OutputPort));
            }
            return result;
        }
    }

    private sealed class FastIssueResult
    {
        public List<FastGrant> Grants { get; } = [];
        public List<FastStall> Stalls { get; } = [];
    }

    private sealed record FastRequest(string InputPort, PacketState Packet, string OutputPort);
    private sealed record FastGrant(PacketState Packet, string OutputPort);
    private sealed record FastStall(string OutputPort, bool BlockedByDownstream);
    private struct StableRandom
    {
        private ulong state;

        public StableRandom(ulong seed) => state = seed;

        public ulong NextUInt64()
        {
            state += 0x9e3779b97f4a7c15UL;
            var value = state;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }

        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        public int NextInt(int exclusiveMaximum) => (int)(NextUInt64() % (uint)exclusiveMaximum);
    }
}

/// <summary>
/// Stable experiment-facing entry point named by the frozen V-BS-CNN configuration.
/// The implementation delegates to the authenticated V-BS packet runtime.
/// </summary>
public static class AspdacVbsCnnTraceRuntime
{
    /// <summary>Runs one authenticated canonical CNN-shaped packet trace through V-BS.</summary>
    public static AspdacVbsTraceRunResult RunTraceCsv(
        string traceCsvPath,
        string expectedSha256,
        int drainCycles) =>
        AspdacVbsRuntime.RunTraceCsv(traceCsvPath, expectedSha256, drainCycles);
}
