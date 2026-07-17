using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Selects whether the reviewer scaling runtime retains provenance events.</summary>
public enum AspdacReviewerTraceMode
{
    /// <summary>Counts events and retains aggregate hashes without creating a trace payload.</summary>
    MetricsOnly,
    /// <summary>Streams every provenance event to JSONL and produces a compressed copy.</summary>
    FullProvenance
}

/// <summary>Defines one parameterized reviewer scaling run on a square flat mesh.</summary>
public sealed record AspdacReviewerScalingOptions(
    int MeshDimension,
    long PacketCount,
    int Seed,
    AspdacReviewerTraceMode TraceMode,
    string? TraceDirectory = null,
    double InjectionRate = 0.02d,
    int InputBufferDepth = 16,
    long? MaxCycles = null,
    bool CompressTrace = true);

/// <summary>Contains measured stage durations from one reviewer scaling run.</summary>
public sealed class AspdacReviewerScalingTimings
{
    /// <summary>Gets the flat-mesh topology build duration.</summary>
    public double GraphBuildSeconds { get; init; }
    /// <summary>Gets the explicit hardware-graph validation duration.</summary>
    public double GraphValidationSeconds { get; init; }
    /// <summary>Gets the hardware simulation-graph compilation duration.</summary>
    public double CompileSeconds { get; init; }
    /// <summary>Gets simulation wall time including inline provenance persistence.</summary>
    public double SimulationWallSeconds { get; init; }
    /// <summary>Gets simulation time after subtracting measured inline provenance writes.</summary>
    public double SimulationCoreSeconds { get; init; }
    /// <summary>Gets measured time spent serializing, hashing, flushing, and closing raw trace events.</summary>
    public double TracePersistSeconds { get; init; }
    /// <summary>Gets measured gzip and compressed-file hashing time.</summary>
    public double TraceCompressionSeconds { get; init; }
    /// <summary>Gets total in-process scenario duration, excluding process startup.</summary>
    public double ScenarioWallSeconds { get; init; }
}

/// <summary>Provides parameterized flat-mesh scaling metrics and trace provenance.</summary>
public sealed class AspdacReviewerScalingResult
{
    /// <summary>Gets whether the exact packet target drained.</summary>
    public bool Completed { get; init; }
    /// <summary>Gets whether the configured maximum cycle bound was reached.</summary>
    public bool Timeout { get; init; }
    /// <summary>Gets the completion or timeout explanation.</summary>
    public string CompletionReason { get; init; } = "";
    /// <summary>Gets the configured square mesh dimension.</summary>
    public int MeshDimension { get; init; }
    /// <summary>Gets the compiled component count.</summary>
    public int CompiledComponentCount { get; init; }
    /// <summary>Gets the compiled logical-link count.</summary>
    public int CompiledLinkCount { get; init; }
    /// <summary>Gets the canonical topology-manifest hash.</summary>
    public string TopologyCanonicalHash { get; init; } = "";
    /// <summary>Gets the compiler source-graph hash binding graph parameters and structure.</summary>
    public string CompiledSourceGraphHash { get; init; } = "";
    /// <summary>Gets the requested exact packet count.</summary>
    public long RequestedPackets { get; init; }
    /// <summary>Gets generated packets admitted to source-local router inputs.</summary>
    public long InjectedPackets { get; init; }
    /// <summary>Gets packets delivered at destination routers.</summary>
    public long CompletedPackets { get; init; }
    /// <summary>Gets executed packet-cycle simulation cycles.</summary>
    public long SimulatedCycles { get; init; }
    /// <summary>Gets logical provenance events counted in both trace modes.</summary>
    public long EventCount { get; init; }
    /// <summary>Gets same-output arbitration conflict events.</summary>
    public long RouterConflictEvents { get; init; }
    /// <summary>Gets downstream input-buffer backpressure events.</summary>
    public long BackpressureEvents { get; init; }
    /// <summary>Gets source injection deferrals caused by full local inputs.</summary>
    public long InjectionBlockedEvents { get; init; }
    /// <summary>Gets the mode-independent canonical delivery-record hash.</summary>
    public string CanonicalDeliveryHash { get; init; } = "";
    /// <summary>Gets the raw full-provenance trace SHA-256, or null in metrics-only mode.</summary>
    public string? RawTraceSha256 { get; init; }
    /// <summary>Gets the compressed full-provenance trace SHA-256, or null when absent.</summary>
    public string? CompressedTraceSha256 { get; init; }
    /// <summary>Gets the raw full-provenance trace size in bytes.</summary>
    public long RawTraceBytes { get; init; }
    /// <summary>Gets the compressed full-provenance trace size in bytes.</summary>
    public long CompressedTraceBytes { get; init; }
    /// <summary>Gets the raw trace path, or null in metrics-only mode.</summary>
    public string? RawTracePath { get; init; }
    /// <summary>Gets the compressed trace path, or null when compression is disabled.</summary>
    public string? CompressedTracePath { get; init; }
    /// <summary>Gets the OS-reported process high-water mark for advisory in-process diagnostics; parent-process polling is authoritative evidence.</summary>
    public long PeakWorkingSetBytes { get; init; }
    /// <summary>Gets the peak sampled managed heap size.</summary>
    public long PeakManagedBytes { get; init; }
    /// <summary>Gets the number of graph-validator diagnostics.</summary>
    public int GraphValidationIssueCount { get; init; }
    /// <summary>Gets measured in-process stage durations.</summary>
    public AspdacReviewerScalingTimings Timings { get; init; } = new();
}

/// <summary>
/// Builds, validates, compiles, and simulates parameterized flat meshes for ASP-DAC
/// reviewer scalability evidence. Packets are one-flit messages using deterministic XY
/// routing, bounded per-input queues, and deterministic per-output round-robin arbitration.
/// </summary>
public static class AspdacReviewerScalingRuntime
{
    private const int Local = 0;
    private const int East = 1;
    private const int South = 2;
    private const int West = 3;
    private const int North = 4;
    private const int InputCount = 5;
    private const int OutputCount = 5;

    /// <summary>Builds the production flat-mesh graph and executes one exact packet-count run.</summary>
    public static AspdacReviewerScalingResult Run(AspdacReviewerScalingOptions options)
    {
        ValidateOptions(options);
        var scenarioWatch = Stopwatch.StartNew();

        var buildWatch = Stopwatch.StartNew();
        var build = new Flat2DMeshTopologyPresetBuilder().Build(new TopologyPresetRequest(
            Flat2DMeshTopologyPresetBuilder.TopologyPresetId,
            options.MeshDimension,
            options.MeshDimension,
            1,
            wordBits: 128,
            leafLaneCount: 1,
            leafLinkDistance: 1d,
            treeDistanceScale: 1d,
            meshHopDistance: 1d,
            routerLatencyCycles: 1,
            adderLatencyCycles: 1,
            placementCellSizeMicrometers: 100d));
        buildWatch.Stop();
        if (!build.IsSuccess || build.TopologyManifest is null)
        {
            throw new InvalidOperationException(
                "Reviewer flat-mesh topology build failed: " +
                string.Join("; ", build.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        }

        var hardwareGraph = build.HardwareGraph;
        hardwareGraph.Parameters["physical_route_contract"] = "explicit-per-logical-link";
        hardwareGraph.Parameters["link_base_latency_cycles"] = "1";
        hardwareGraph.Parameters["delay_ps_per_um"] = "0";
        hardwareGraph.Parameters["routing_resource_capacity"] = "2147483647";
        hardwareGraph.Parameters["routing_resource_capacity_electrical"] = "2147483647";
        hardwareGraph.Parameters["routing_congestion_penalty_cycles_per_over_capacity_link"] = "0";
        hardwareGraph.Parameters["routing_congestion_max_penalty_cycles"] = "0";
        foreach (var link in hardwareGraph.Links)
        {
            link.Parameters["physical_route_id"] = link.Id;
        }
        var validationWatch = Stopwatch.StartNew();
        var validation = new HardwareGraphValidator().Validate(hardwareGraph);
        validationWatch.Stop();
        var blockingValidationIssues = validation.Issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Where(issue => issue.Code is not "missing_workload_source" and not "missing_workload_sink")
            .ToArray();
        if (blockingValidationIssues.Length > 0)
        {
            throw new InvalidOperationException(
                "Reviewer flat-mesh graph validation failed: " +
                string.Join("; ", blockingValidationIssues.Select(issue => $"{issue.Code}: {issue.Message}")));
        }

        var compileWatch = Stopwatch.StartNew();
        var compilation = new SimulationGraphCompiler().CompileHardware(
            hardwareGraph,
            simulationConfig: new SimulationConfig
            {
                MaxCycles = int.MaxValue,
                FlitSizeBits = 128
            },
            traceConfig: new TraceConfig
            {
                Enabled = options.TraceMode == AspdacReviewerTraceMode.FullProvenance,
                Level = options.TraceMode == AspdacReviewerTraceMode.FullProvenance ? "full_provenance" : "metrics_only"
            });
        compileWatch.Stop();
        if (!compilation.IsSuccess || compilation.Graph is null)
        {
            throw new InvalidOperationException(
                "Reviewer flat-mesh compilation failed: " +
                string.Join("; ", compilation.Errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        var simulation = Simulate(compilation.Graph, options);
        scenarioWatch.Stop();

        return new AspdacReviewerScalingResult
        {
            Completed = simulation.Completed,
            Timeout = simulation.Timeout,
            CompletionReason = simulation.CompletionReason,
            MeshDimension = options.MeshDimension,
            CompiledComponentCount = compilation.Graph.Components.Count,
            CompiledLinkCount = compilation.Graph.Links.Count,
            TopologyCanonicalHash = build.TopologyManifest.CanonicalHash,
            CompiledSourceGraphHash = compilation.Graph.Provenance.SourceGraphHash,
            RequestedPackets = options.PacketCount,
            InjectedPackets = simulation.InjectedPackets,
            CompletedPackets = simulation.CompletedPackets,
            SimulatedCycles = simulation.SimulatedCycles,
            EventCount = simulation.EventCount,
            RouterConflictEvents = simulation.RouterConflictEvents,
            BackpressureEvents = simulation.BackpressureEvents,
            InjectionBlockedEvents = simulation.InjectionBlockedEvents,
            CanonicalDeliveryHash = simulation.CanonicalDeliveryHash,
            RawTraceSha256 = simulation.RawTraceSha256,
            CompressedTraceSha256 = simulation.CompressedTraceSha256,
            RawTraceBytes = simulation.RawTraceBytes,
            CompressedTraceBytes = simulation.CompressedTraceBytes,
            RawTracePath = simulation.RawTracePath,
            CompressedTracePath = simulation.CompressedTracePath,
            PeakWorkingSetBytes = PeakWorkingSetBytes(),
            PeakManagedBytes = simulation.PeakManagedBytes,
            GraphValidationIssueCount = validation.Issues.Count,
            Timings = new AspdacReviewerScalingTimings
            {
                GraphBuildSeconds = buildWatch.Elapsed.TotalSeconds,
                GraphValidationSeconds = validationWatch.Elapsed.TotalSeconds,
                CompileSeconds = compileWatch.Elapsed.TotalSeconds,
                SimulationWallSeconds = simulation.SimulationWallSeconds,
                SimulationCoreSeconds = simulation.SimulationCoreSeconds,
                TracePersistSeconds = simulation.TracePersistSeconds,
                TraceCompressionSeconds = simulation.TraceCompressionSeconds,
                ScenarioWallSeconds = scenarioWatch.Elapsed.TotalSeconds
            }
        };
    }

    private static PacketSimulationResult Simulate(
        HardwareSimulationGraph compiledGraph,
        AspdacReviewerScalingOptions options)
    {
        if (compiledGraph.SimulationConfig.FlitSizeBits != 128)
            throw new InvalidOperationException("Reviewer scaling supports only the compiled 128-bit flit contract.");
        var expectedTraceEnabled = options.TraceMode == AspdacReviewerTraceMode.FullProvenance;
        if (compiledGraph.TraceConfig.Enabled != expectedTraceEnabled)
            throw new InvalidOperationException("Compiled trace configuration does not match the requested reviewer trace mode.");
        var routerComponents = compiledGraph.Components
            .Where(component => component.Type == ComponentKind.Router)
            .OrderBy(component => component.Position.Y)
            .ThenBy(component => component.Position.X)
            .ToArray();
        var routerCount = checked(options.MeshDimension * options.MeshDimension);
        if (routerComponents.Length != routerCount)
        {
            throw new InvalidOperationException(
                $"Compiled flat mesh contains {routerComponents.Length} routers; expected {routerCount}.");
        }
        if (routerComponents.Any(component => component.GetIntParameter("router_latency_cycles", -1) != 1))
            throw new InvalidOperationException("Reviewer scaling supports only compiled one-cycle flat-mesh routers.");
        if (routerComponents.Any(component => component.GetIntParameter("buffer_depth", 16) != options.InputBufferDepth))
            throw new InvalidOperationException("Compiled router input depth does not match the reviewer scaling contract.");
        var coordinateToRouter = routerComponents
            .Select((component, index) => (component, index))
            .ToDictionary(
                item => (item.component.Position.X, item.component.Position.Y),
                item => item.index);
        for (var row = 0; row < options.MeshDimension; row++)
        {
            for (var column = 0; column < options.MeshDimension; column++)
            {
                if (!coordinateToRouter.ContainsKey((column * 2, row * 2)))
                {
                    throw new InvalidOperationException(
                        $"Compiled graph is missing the expected flat-mesh router at physical grid coordinate ({column * 2},{row * 2}).");
                }
            }
        }
        var routerIndexById = routerComponents
            .Select((component, index) => (component.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
        var compiledNeighbors = Enumerable.Repeat(-1, checked(routerCount * OutputCount)).ToArray();
        var directedMeshLinks = 0;
        foreach (var link in compiledGraph.Links)
        {
            if (!routerIndexById.TryGetValue(link.Source.ComponentId, out var source) ||
                !routerIndexById.TryGetValue(link.Destination.ComponentId, out var destination))
            {
                continue;
            }
            if (link.BandwidthBitsPerCycle != 128 || link.LatencyCycles != 1)
            {
                throw new InvalidOperationException(
                    $"Compiled router link '{link.Id}' must use 128 bits/cycle and one-cycle latency for this scaling contract.");
            }
            var sourcePosition = routerComponents[source].Position;
            var destinationPosition = routerComponents[destination].Position;
            var output = (destinationPosition.X - sourcePosition.X, destinationPosition.Y - sourcePosition.Y) switch
            {
                (2, 0) => East,
                (-2, 0) => West,
                (0, 2) => South,
                (0, -2) => North,
                _ => throw new InvalidOperationException(
                    $"Compiled router link '{link.Id}' does not connect adjacent flat-mesh coordinates.")
            };
            var neighborIndex = checked(source * OutputCount + output);
            if (compiledNeighbors[neighborIndex] >= 0)
                throw new InvalidOperationException($"Compiled router output has duplicate directed links at router {source}, output {output}.");
            compiledNeighbors[neighborIndex] = destination;
            directedMeshLinks++;
        }
        var expectedDirectedMeshLinks = checked(4 * options.MeshDimension * (options.MeshDimension - 1));
        if (directedMeshLinks != expectedDirectedMeshLinks)
        {
            throw new InvalidOperationException(
                $"Compiled flat mesh contains {directedMeshLinks} directed router links; expected {expectedDirectedMeshLinks}.");
        }
        var routers = Enumerable.Range(0, routerCount)
            .Select(_ => new RouterState(options.InputBufferDepth))
            .ToArray();
        var nextArbiter = new int[checked(routerCount * OutputCount)];
        var currentArrivals = new List<Arrival>();
        var nextArrivals = new List<Arrival>();
        var rng = new StableRandom(unchecked((ulong)(uint)options.Seed) + 0x9e3779b97f4a7c15UL);
        using var deliveryHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var trace = StreamingTraceSink.Create(options.TraceMode, options.TraceDirectory);

        var injected = 0L;
        var delivered = 0L;
        var eventCount = 0L;
        var routerConflicts = 0L;
        var backpressure = 0L;
        var injectionBlocked = 0L;
        var peakManaged = GC.GetTotalMemory(false);
        var injectionCredit = 0d;
        var maxCycles = options.MaxCycles ?? DefaultMaxCycles(options, routerCount);
        var simulatedCycles = 0L;
        var simulationWatch = Stopwatch.StartNew();

        for (var cycle = 0L; cycle < maxCycles; cycle++)
        {
            simulatedCycles = cycle + 1;
            foreach (var arrival in currentArrivals)
            {
                if (!routers[arrival.Router].TryEnqueue(arrival.Input, arrival.Packet))
                {
                    throw new InvalidOperationException("Reserved downstream input capacity was not available on arrival.");
                }
                Record(trace, ref eventCount, cycle, "flight_arrive", arrival.Packet.Id, arrival.Router, arrival.Input, 0);
            }
            currentArrivals.Clear();

            if (injected < options.PacketCount)
            {
                injectionCredit += options.InjectionRate * routerCount;
                var remaining = options.PacketCount - injected;
                var injectionBudget = (int)Math.Min(Math.Floor(injectionCredit), Math.Min(remaining, routerCount));
                var admitted = 0;
                for (var offer = 0; offer < injectionBudget; offer++)
                {
                    var requestedSource = rng.NextInt(routerCount);
                    var source = FindInjectableSource(routers, requestedSource);
                    if (source < 0)
                    {
                        injectionBlocked++;
                        Record(trace, ref eventCount, cycle, "inject_blocked", -1, requestedSource, Local, 0);
                        break;
                    }

                    var destination = rng.NextInt(routerCount - 1);
                    if (destination >= source) destination++;
                    var packet = new PacketState(injected, source, destination, cycle);
                    if (!routers[source].TryEnqueue(Local, packet))
                    {
                        throw new InvalidOperationException("An injectable source rejected a reserved local packet.");
                    }
                    injected++;
                    admitted++;
                    Record(trace, ref eventCount, cycle, "offered", packet.Id, source, destination, 0);
                    Record(trace, ref eventCount, cycle, "inject", packet.Id, source, Local, 0);
                }
                injectionCredit -= admitted;
            }

            for (var routerId = 0; routerId < routerCount; routerId++)
            {
                var router = routers[routerId];
                if (router.TotalOccupancy == 0) continue;

                for (var output = 0; output < OutputCount; output++)
                {
                    var contenderCount = 0;
                    for (var input = 0; input < InputCount; input++)
                    {
                        var packet = router.Peek(input);
                        if (packet is not null && ResolveOutput(routerId, packet.Destination, options.MeshDimension) == output)
                            contenderCount++;
                    }
                    if (contenderCount == 0) continue;

                    var arbiterIndex = checked(routerId * OutputCount + output);
                    var winningInput = SelectWinningInput(router, routerId, output, nextArbiter[arbiterIndex], options.MeshDimension);
                    var winner = router.Peek(winningInput)!;
                    if (contenderCount > 1)
                    {
                        routerConflicts += contenderCount - 1;
                        Record(trace, ref eventCount, cycle, "router_conflict", winner.Id, routerId, output, contenderCount - 1);
                    }

                    if (output == Local)
                    {
                        router.Dequeue(winningInput);
                        delivered++;
                        nextArbiter[arbiterIndex] = (winningInput + 1) % InputCount;
                        Record(trace, ref eventCount, cycle, "issue_sink", winner.Id, routerId, Local, 0);
                        Record(trace, ref eventCount, cycle, "deliver", winner.Id, routerId, winner.Destination, 0);
                        AppendDeliveryHash(deliveryHash, winner, cycle);
                        continue;
                    }

                    var neighbor = compiledNeighbors[checked(routerId * OutputCount + output)];
                    if (neighbor < 0)
                        throw new InvalidOperationException($"XY routing selected absent compiled output {output} on router {routerId}.");
                    var neighborInput = Opposite(output);
                    // Each directed compiled mesh link is the sole producer for its opposite
                    // router input and each output grants at most once per cycle.
                    if (routers[neighbor].InputOccupancy(neighborInput) >= options.InputBufferDepth)
                    {
                        backpressure++;
                        Record(trace, ref eventCount, cycle, "downstream_blocked", winner.Id, routerId, output, neighbor);
                        continue;
                    }

                    router.Dequeue(winningInput);
                    nextArbiter[arbiterIndex] = (winningInput + 1) % InputCount;
                    nextArrivals.Add(new Arrival(winner, neighbor, neighborInput));
                    Record(trace, ref eventCount, cycle, "issue_link", winner.Id, routerId, output, neighbor);
                }
            }

            (currentArrivals, nextArrivals) = (nextArrivals, currentArrivals);
            if ((cycle & 1023L) == 0L) peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
            if (delivered == options.PacketCount && injected == options.PacketCount &&
                currentArrivals.Count == 0 && routers.All(router => router.TotalOccupancy == 0))
            {
                break;
            }
        }

        simulationWatch.Stop();
        peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
        var tracePersistBeforeComplete = trace.PersistSeconds;
        trace.Complete();
        var tracePersistSeconds = trace.PersistSeconds;
        var simulationWallSeconds = simulationWatch.Elapsed.TotalSeconds + (tracePersistSeconds - tracePersistBeforeComplete);

        var compressionWatch = Stopwatch.StartNew();
        string? compressedPath = null;
        string? compressedHash = null;
        long compressedBytes = 0;
        if (options.TraceMode == AspdacReviewerTraceMode.FullProvenance && options.CompressTrace)
        {
            compressedPath = trace.RawPath + ".gz";
            using (var source = File.OpenRead(trace.RawPath!))
            using (var destination = new FileStream(compressedPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: false))
            {
                source.CopyTo(gzip);
            }
            compressedBytes = new FileInfo(compressedPath).Length;
            compressedHash = Sha256File(compressedPath);
        }
        compressionWatch.Stop();

        var timeout = delivered != options.PacketCount;
        return new PacketSimulationResult
        {
            Completed = !timeout,
            Timeout = timeout,
            CompletionReason = timeout
                ? $"Maximum cycle bound {maxCycles} reached with {options.PacketCount - delivered} packet(s) undelivered."
                : "Exact packet target injected and drained.",
            InjectedPackets = injected,
            CompletedPackets = delivered,
            SimulatedCycles = simulatedCycles,
            EventCount = eventCount,
            RouterConflictEvents = routerConflicts,
            BackpressureEvents = backpressure,
            InjectionBlockedEvents = injectionBlocked,
            CanonicalDeliveryHash = Hex(deliveryHash.GetHashAndReset()),
            RawTraceSha256 = trace.RawSha256,
            CompressedTraceSha256 = compressedHash,
            RawTraceBytes = trace.RawBytes,
            CompressedTraceBytes = compressedBytes,
            RawTracePath = trace.RawPath,
            CompressedTracePath = compressedPath,
            PeakManagedBytes = peakManaged,
            SimulationWallSeconds = simulationWallSeconds,
            SimulationCoreSeconds = Math.Max(0d, simulationWallSeconds - tracePersistSeconds),
            TracePersistSeconds = tracePersistSeconds,
            TraceCompressionSeconds = compressionWatch.Elapsed.TotalSeconds
        };
    }

    private static void ValidateOptions(AspdacReviewerScalingOptions options)
    {
        if (options.MeshDimension < 2) throw new ArgumentOutOfRangeException(nameof(options.MeshDimension), "Mesh dimension must be at least two.");
        if (options.PacketCount <= 0) throw new ArgumentOutOfRangeException(nameof(options.PacketCount));
        if (options.InjectionRate <= 0d || options.InjectionRate > 1d) throw new ArgumentOutOfRangeException(nameof(options.InjectionRate));
        if (options.InputBufferDepth != 16)
            throw new NotSupportedException("Reviewer scaling currently freezes the production router input depth at 16 flits.");
        if (options.MaxCycles is <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCycles));
        if (options.TraceMode == AspdacReviewerTraceMode.FullProvenance && string.IsNullOrWhiteSpace(options.TraceDirectory))
            throw new ArgumentException("Full provenance mode requires an explicit trace directory.", nameof(options.TraceDirectory));
    }

    private static long DefaultMaxCycles(AspdacReviewerScalingOptions options, int routerCount)
    {
        var expectedGenerationCycles = Math.Ceiling(options.PacketCount / (options.InjectionRate * routerCount));
        var bound = expectedGenerationCycles * 8d + options.MeshDimension * 64d + 1024d;
        if (bound > long.MaxValue) throw new OverflowException("Derived reviewer scaling cycle bound exceeds Int64.");
        return Math.Max(1024L, (long)Math.Ceiling(bound));
    }

    private static int FindInjectableSource(IReadOnlyList<RouterState> routers, int requestedSource)
    {
        for (var offset = 0; offset < routers.Count; offset++)
        {
            var source = (requestedSource + offset) % routers.Count;
            if (routers[source].HasInputCapacity(Local)) return source;
        }
        return -1;
    }

    private static int SelectWinningInput(
        RouterState router,
        int routerId,
        int output,
        int startInput,
        int dimension)
    {
        for (var offset = 0; offset < InputCount; offset++)
        {
            var input = (startInput + offset) % InputCount;
            var packet = router.Peek(input);
            if (packet is not null && ResolveOutput(routerId, packet.Destination, dimension) == output) return input;
        }
        throw new InvalidOperationException("Router arbitration had contenders but no winning input.");
    }

    private static int ResolveOutput(int routerId, int destination, int dimension)
    {
        if (routerId == destination) return Local;
        var x = routerId % dimension;
        var destinationX = destination % dimension;
        if (destinationX > x) return East;
        if (destinationX < x) return West;
        return destination / dimension > routerId / dimension ? South : North;
    }

    private static int Opposite(int output) => output switch
    {
        East => West,
        West => East,
        South => North,
        North => South,
        _ => throw new ArgumentOutOfRangeException(nameof(output))
    };

    private static void Record(
        StreamingTraceSink trace,
        ref long eventCount,
        long cycle,
        string eventName,
        long packetId,
        int router,
        int peer,
        int value)
    {
        eventCount++;
        trace.Record(cycle, eventName, packetId, router, peer, value);
    }

    private static void AppendDeliveryHash(IncrementalHash hash, PacketState packet, long deliveredCycle)
    {
        var line = string.Concat(
            packet.Id.ToString(CultureInfo.InvariantCulture), ",",
            packet.Source.ToString(CultureInfo.InvariantCulture), ",",
            packet.Destination.ToString(CultureInfo.InvariantCulture), ",",
            packet.CreatedCycle.ToString(CultureInfo.InvariantCulture), ",",
            deliveredCycle.ToString(CultureInfo.InvariantCulture), "\n");
        hash.AppendData(Encoding.UTF8.GetBytes(line));
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Hex(sha256.ComputeHash(stream));
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    private static long PeakWorkingSetBytes()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PeakWorkingSet64;
    }

    private sealed class PacketState
    {
        public PacketState(long id, int source, int destination, long createdCycle)
        {
            Id = id;
            Source = source;
            Destination = destination;
            CreatedCycle = createdCycle;
        }

        public long Id { get; }
        public int Source { get; }
        public int Destination { get; }
        public long CreatedCycle { get; }
    }

    private sealed class RouterState
    {
        private readonly Queue<PacketState>[] _inputs;
        private readonly int _capacity;

        public RouterState(int capacity)
        {
            _capacity = capacity;
            _inputs = Enumerable.Range(0, InputCount).Select(_ => new Queue<PacketState>()).ToArray();
        }

        public int TotalOccupancy { get; private set; }
        public bool HasInputCapacity(int input) => _inputs[input].Count < _capacity;
        public int InputOccupancy(int input) => _inputs[input].Count;
        public PacketState? Peek(int input) => _inputs[input].Count == 0 ? null : _inputs[input].Peek();

        public bool TryEnqueue(int input, PacketState packet)
        {
            if (!HasInputCapacity(input)) return false;
            _inputs[input].Enqueue(packet);
            TotalOccupancy++;
            return true;
        }

        public PacketState Dequeue(int input)
        {
            var packet = _inputs[input].Dequeue();
            TotalOccupancy--;
            return packet;
        }
    }

    private sealed record Arrival(PacketState Packet, int Router, int Input);

    private sealed class PacketSimulationResult
    {
        public bool Completed { get; init; }
        public bool Timeout { get; init; }
        public string CompletionReason { get; init; } = "";
        public long InjectedPackets { get; init; }
        public long CompletedPackets { get; init; }
        public long SimulatedCycles { get; init; }
        public long EventCount { get; init; }
        public long RouterConflictEvents { get; init; }
        public long BackpressureEvents { get; init; }
        public long InjectionBlockedEvents { get; init; }
        public string CanonicalDeliveryHash { get; init; } = "";
        public string? RawTraceSha256 { get; init; }
        public string? CompressedTraceSha256 { get; init; }
        public long RawTraceBytes { get; init; }
        public long CompressedTraceBytes { get; init; }
        public string? RawTracePath { get; init; }
        public string? CompressedTracePath { get; init; }
        public long PeakManagedBytes { get; init; }
        public double SimulationWallSeconds { get; init; }
        public double SimulationCoreSeconds { get; init; }
        public double TracePersistSeconds { get; init; }
        public double TraceCompressionSeconds { get; init; }
    }

    private sealed class StreamingTraceSink : IDisposable
    {
        private readonly IncrementalHash? _hash;
        private StreamWriter? _writer;
        private long _persistTicks;
        private bool _completed;

        private StreamingTraceSink(string? rawPath)
        {
            RawPath = rawPath;
            if (rawPath is null) return;
            var directory = Path.GetDirectoryName(rawPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _writer = new StreamWriter(
                new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
            _writer.NewLine = "\n";
        }

        public string? RawPath { get; }
        public string? RawSha256 { get; private set; }
        public long RawBytes { get; private set; }
        public double PersistSeconds => _persistTicks / (double)Stopwatch.Frequency;

        public static StreamingTraceSink Create(AspdacReviewerTraceMode mode, string? traceDirectory)
        {
            if (mode == AspdacReviewerTraceMode.MetricsOnly) return new StreamingTraceSink(null);
            var directory = Path.GetFullPath(traceDirectory!);
            return new StreamingTraceSink(Path.Combine(directory, "trace-events.jsonl"));
        }

        public void Record(long cycle, string eventName, long packetId, int router, int peer, int value)
        {
            if (_writer is null) return;
            var started = Stopwatch.GetTimestamp();
            var line = string.Concat(
                "{\"cycle\":", cycle.ToString(CultureInfo.InvariantCulture),
                ",\"event\":\"", eventName,
                "\",\"packet_id\":", packetId.ToString(CultureInfo.InvariantCulture),
                ",\"router\":", router.ToString(CultureInfo.InvariantCulture),
                ",\"peer\":", peer.ToString(CultureInfo.InvariantCulture),
                ",\"value\":", value.ToString(CultureInfo.InvariantCulture), "}");
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            _hash!.AppendData(bytes);
            _writer.WriteLine(line);
            RawBytes += bytes.LongLength;
            _persistTicks += Stopwatch.GetTimestamp() - started;
        }

        public void Complete()
        {
            if (_completed) return;
            _completed = true;
            if (_writer is null) return;
            var started = Stopwatch.GetTimestamp();
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
            RawSha256 = Hex(_hash!.GetHashAndReset());
            _persistTicks += Stopwatch.GetTimestamp() - started;
        }

        public void Dispose()
        {
            Complete();
            _hash?.Dispose();
        }
    }

    private struct StableRandom
    {
        private ulong _state;

        public StableRandom(ulong seed) => _state = seed;

        public int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            return (int)(NextUInt64() % (uint)exclusiveUpperBound);
        }

        private ulong NextUInt64()
        {
            _state += 0x9e3779b97f4a7c15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
            z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
            return z ^ (z >> 31);
        }
    }
}
