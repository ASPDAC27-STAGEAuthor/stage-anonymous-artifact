using System.Text.Json;
using System.Text.Json.Nodes;
using HardwareSim.Core;

internal static class ExactCycleGoldenTests
{
    public static IReadOnlyList<TestCase> All { get; } =
    [
        new("P1C-GOLD-001 Single Link Latency exact trace hash", GoldenSingleLinkLatency),
        new("P1C-GOLD-002 Buffer Backpressure exact trace hash", GoldenBufferBackpressure),
        new("P1C-GOLD-003 Router Round-Robin exact trace hash", GoldenRouterRoundRobin),
        new("P1C-GOLD-004 PE Compute exact trace hash", GoldenPeCompute),
        new("P1C-GOLD-005 Memory Read exact trace hash", GoldenMemoryRead),
        new("P1C-GOLD-006 2x2 Mesh exact trace hash", GoldenMeshRouting),
        new("P1C-GOLD-007 Workload Injection exact trace hash", GoldenWorkloadInjection)
    ];

    private static void FullSemanticRoundtrip()
    {
        var graph = CreateFullFidelityGraph();
        var loaded = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(graph));
        var reloaded = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(loaded));

        AssertGraphEquivalent(graph, loaded);
        AssertGraphEquivalent(loaded, reloaded);
    }

    private static void OptionalUnknownAndEmptyGraphCompatibility()
    {
        var empty = new HardwareGraph();
        var loadedEmpty = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(empty));
        TestAssert.Equal(HardwareGraph.CurrentSchemaVersion, loadedEmpty.SchemaVersion, "empty graph must keep schema version");
        TestAssert.Equal(0, loadedEmpty.Components.Count, "empty graph components must roundtrip empty");
        TestAssert.Equal(0, loadedEmpty.Links.Count, "empty graph links must roundtrip empty");
        TestAssert.Equal(0, loadedEmpty.Groups.Count, "empty graph groups must roundtrip empty");
        TestAssert.Equal(0, loadedEmpty.Macros.Count, "empty graph macros must roundtrip empty");

        var missingOptional = HardwareGraphJson.Deserialize("{\"schema_version\":\"1.0\",\"components\":[],\"links\":[]}");
        TestAssert.Equal(0, missingOptional.Groups.Count, "missing groups must load as empty");
        TestAssert.Equal(0, missingOptional.Macros.Count, "missing macros must load as empty");

        var unknownJson = """
        {
          "schema_version": "1.0",
          "unknown_root": { "kept": true },
          "components": [
            {
              "id": "custom0",
              "name": "Custom",
              "type": "custom",
              "position": { "x": 0, "y": 0 },
              "ports": [
                { "name": "in", "direction": "input", "unknown_port": "port-ext" }
              ],
              "unknown_component": "component-ext"
            }
          ],
          "links": []
        }
        """;
        var loadedUnknown = HardwareGraphJson.Deserialize(unknownJson);
        var savedUnknown = HardwareGraphJson.Serialize(loadedUnknown);
        TestAssert.True(loadedUnknown.ExtensionData.ContainsKey("unknown_root"), "unknown root field must be preserved as extension data");
        TestAssert.True(loadedUnknown.Components.Single().ExtensionData.ContainsKey("unknown_component"), "unknown component field must be preserved");
        TestAssert.True(loadedUnknown.Components.Single().Ports.Single().ExtensionData.ContainsKey("unknown_port"), "unknown port field must be preserved");
        TestAssert.True(savedUnknown.Contains("\"unknown_root\"", StringComparison.Ordinal), "unknown root field must save back out");
        TestAssert.True(savedUnknown.Contains("\"unknown_component\"", StringComparison.Ordinal), "unknown component field must save back out");
        TestAssert.True(savedUnknown.Contains("\"unknown_port\"", StringComparison.Ordinal), "unknown port field must save back out");
    }

    private static void SchemaVersionWrittenAndRead()
    {
        var json = HardwareGraphJson.Serialize(new HardwareGraph());
        TestAssert.True(json.Contains("\"schema_version\": \"1.0\"", StringComparison.Ordinal), "writer must emit schema_version 1.0");

        var loaded = HardwareGraphJson.Deserialize(json);
        TestAssert.Equal("1.0", loaded.SchemaVersion, "reader must keep current schema version");

        var missing = HardwareGraphJson.TryDeserialize("{\"components\":[]}");
        TestAssert.True(!missing.IsSuccess, "missing schema version must not be silently treated as current");
        TestAssert.Equal("MissingSchemaVersion", missing.Issues.Single().Code, "missing schema version must be structured");
    }

    private static void LegacyMigrationAndUnityRejection()
    {
        var sample = HardwareGraphSchemaMigrator.ImportToCurrent(File.ReadAllText("samples/hardware.json"));
        TestAssert.True(sample.IsSuccess, string.Join("; ", sample.Issues.Select(issue => issue.Message)));
        TestAssert.Equal("0.1.0", sample.SourceVersion, "sample legacy source version must be reported");
        TestAssert.True(sample.MigrationPath.SequenceEqual([new HardwareGraphMigrationStep("0.1.0", "1.0")]), "legacy sample must use the explicit 0.1.0->1.0 migration");
        TestAssert.Equal("1.0", sample.Graph!.SchemaVersion, "legacy sample must migrate to current schema");
        TestAssert.True(sample.Graph.Components.Any(component => component.Id == "source"), "legacy sample components must survive migration");

        var legacyWithModelBindings = """
        {
          "schemaVersion": "0.1.0",
          "components": [
            {
              "id": "source",
              "name": "Source",
              "type": "workloadSource",
              "position": { "x": 0, "y": 0 },
              "ports": [ { "name": "out", "direction": "output" } ],
              "modelRef": "legacy-source-model",
              "parameters": { "inject_count": "1" }
            },
            {
              "id": "sink",
              "name": "Sink",
              "type": "workloadSink",
              "position": { "x": 1, "y": 0 },
              "ports": [ { "name": "in", "direction": "input" } ]
            }
          ],
          "links": [
            {
              "id": "legacy_link",
              "source": { "componentId": "source", "portName": "out" },
              "destination": { "componentId": "sink", "portName": "in" },
              "modelRef": "legacy-link-model"
            }
          ]
        }
        """;
        var imported = HardwareGraphSchemaMigrator.ImportToCurrent(legacyWithModelBindings);
        TestAssert.True(imported.IsSuccess, string.Join("; ", imported.Issues.Select(issue => issue.Message)));
        TestAssert.Equal("legacy-source-model", imported.Graph!.Components.Single(c => c.Id == "source").ModelRef, "legacy modelRef must migrate to model_ref");
        TestAssert.Equal("legacy-link-model", imported.Graph.Links.Single().ModelRef, "legacy link modelRef must migrate to model_ref");

        var unitySnapshot = HardwareGraphSchemaMigrator.ImportToCurrent(File.ReadAllText("samples/editor_snapshot.json"));
        TestAssert.True(!unitySnapshot.IsSuccess, "Unity simplified editor snapshots must not be partially loaded as HardwareGraph");
        TestAssert.Equal("UnsupportedUnitySnapshotImport", unitySnapshot.Issues.Single().Code, "Unity snapshot rejection must be explicit");
    }

    private static void MigrationChainAndUnknownVersionErrors()
    {
        var source = new JsonObject { ["schema_version"] = "1.0" };
        var chain = HardwareGraphSchemaMigrator.Migrate(
            source,
            "1.2",
            [new MarkerMigration("1.0", "1.1"), new MarkerMigration("1.1", "1.2")]);

        TestAssert.True(chain.IsSuccess, string.Join("; ", chain.Issues.Select(issue => issue.Message)));
        TestAssert.True(chain.MigrationPath.SequenceEqual([
            new HardwareGraphMigrationStep("1.0", "1.1"),
            new HardwareGraphMigrationStep("1.1", "1.2")
        ]), "migration chain must execute 1.0->1.1->1.2 in order");
        TestAssert.Equal("1.2", chain.Document!["schema_version"]!.GetValue<string>(), "migration chain must produce target version");
        TestAssert.True(chain.Document.ContainsKey("migrated_1_1") && chain.Document.ContainsKey("migrated_1_2"), "migration markers must prove both steps executed");

        var unknownMajor = HardwareGraphJson.TryDeserialize("{\"schema_version\":\"9.0\"}");
        TestAssert.True(!unknownMajor.IsSuccess, "unknown major version must fail");
        TestAssert.Equal("UnsupportedSchemaVersion", unknownMajor.Issues.Single().Code, "unknown major must return structured unsupported schema error");

        var unknownMinor = HardwareGraphJson.TryDeserialize("{\"schema_version\":\"1.99\"}");
        TestAssert.True(!unknownMinor.IsSuccess, "unknown minor version must not load as current schema");
        TestAssert.True(
            unknownMinor.Issues.Single().Code is "MigrationPathNotFound" or "UnsupportedSchemaVersion",
            "unknown minor version must return a structured version or migration error");
    }

    private static void CanonicalTraceHashStability()
    {
        var trace = new SimulationTrace
        {
            Cycles =
            [
                new CycleTraceRecord
                {
                    Cycle = 0,
                    Events = [new TraceEvent(TraceEventType.PacketInjection, PacketId: "P0", ComponentId: "source", Bits: 128)]
                },
                new CycleTraceRecord
                {
                    Cycle = 3,
                    Events = [new TraceEvent(TraceEventType.PacketMove, PacketId: "P0", ComponentId: "sink", LinkId: "l0", Bits: 128)]
                }
            ]
        };
        var first = CanonicalTraceHasher.Compute(trace, new Dictionary<string, string>
        {
            ["scenario"] = "hash",
            ["max_cycles"] = "10",
            ["timestamp"] = "2026-06-18T01:02:03Z",
            ["absolute_path"] = Path.GetFullPath("samples/hardware.json")
        }, seed: 42);
        var second = CanonicalTraceHasher.Compute(trace, new Dictionary<string, string>
        {
            ["max_cycles"] = "10",
            ["scenario"] = "hash",
            ["timestamp"] = "2027-01-01T00:00:00Z",
            ["absolute_path"] = Path.GetFullPath("samples/other.json")
        }, seed: 42);

        TestAssert.Equal(first.Hash, second.Hash, "timestamps absolute paths and dictionary order must not affect canonical hash");
        TestAssert.Equal("SHA-256", first.Algorithm, "hash algorithm must be recorded");
        TestAssert.Equal("1.0", first.TraceSchemaVersion, "trace schema version must be recorded");
        TestAssert.Equal(42, first.Seed, "seed must be recorded");
        TestAssert.True(first.Config.ContainsKey("scenario") && first.Config.ContainsKey("max_cycles"), "stable config must be recorded");
        TestAssert.True(!first.Config.ContainsKey("timestamp") && !first.Config.ContainsKey("absolute_path"), "unstable config keys must be excluded");

        trace.Cycles[1].Events[0] = new TraceEvent(TraceEventType.PacketMove, PacketId: "P0", ComponentId: "sink", LinkId: "l0", Bits: 256);
        var different = CanonicalTraceHasher.Compute(trace, new Dictionary<string, string>
        {
            ["scenario"] = "hash",
            ["max_cycles"] = "10"
        }, seed: 42);
        TestAssert.True(first.Hash != different.Hash, "different valid trace input must produce a different hash");
    }

    private static void GoldenSingleLinkLatency() => RunGolden(new GoldenCase(
        "P1C-GOLD-001",
        "Single Link Latency: cycle 0 issue -> cycle 3 arrive",
        CreateDirectGraph("single_latency", sourceParameters: new()
        {
            ["inject_count"] = "1",
            ["inject_interval"] = "10",
            ["queue_capacity"] = "1"
        }, linkLatency: 3),
        "ea5d8d34ae5bb553ca1f1d6fb813ad4d5b47016c42a56760054f21578c572f7f",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-001", ["max_cycles"] = "20" },
        Seed: 101,
        MaxCycles: 20,
        AssertSingleLinkLatency));

    private static void GoldenBufferBackpressure() => RunGolden(new GoldenCase(
        "P1C-GOLD-002",
        "Buffer Backpressure: buffer full creates source stall",
        CreateBufferBackpressureGraph(),
        "5ec3b01df3e3df5b060a4c254c324488f32a4e7bb853ee4898c0085b721eeab3",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-002", ["max_cycles"] = "40" },
        Seed: 102,
        MaxCycles: 40,
        AssertBufferBackpressure));

    private static void GoldenRouterRoundRobin() => RunGolden(new GoldenCase(
        "P1C-GOLD-003",
        "Router Round-Robin: 3 inputs served north,south,east repeatedly",
        CreateThreeInputRouterGraph(),
        "7d95c56d35b6051b6bb997f1bb75c4d7331eae4af12432462fd134b0661e4440",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-003", ["max_cycles"] = "80" },
        Seed: 103,
        MaxCycles: 80,
        AssertRouterRoundRobin));

    private static void GoldenPeCompute() => RunGolden(new GoldenCase(
        "P1C-GOLD-004",
        "PE Compute: 1024 MAC / 256 per cycle = 4 cycles",
        CreatePeComputeGraph(),
        "1c4ba6ffcb00444d7409ae2a724d9938d42a8058eb72aeeac76d7ef3d37fb116",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-004", ["max_cycles"] = "40" },
        Seed: 104,
        MaxCycles: 40,
        AssertPeCompute));

    private static void GoldenMemoryRead() => RunGolden(new GoldenCase(
        "P1C-GOLD-005",
        "Memory Read: request@10 -> response@15",
        CreateMemoryReadGraph(),
        "a785341081d4343a23cade6575177df572aa530c9c7e4c884476f4cd7a93372f",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-005", ["max_cycles"] = "40", ["schedule_start_cycle"] = "9" },
        Seed: 105,
        MaxCycles: 40,
        AssertMemoryRead,
        CreateMemoryReadSchedule()));

    private static void GoldenMeshRouting() => RunGolden(new GoldenCase(
        "P1C-GOLD-006",
        "2x2 Mesh: from logical (0,0) to (1,1), east then north",
        CreateMeshRoutingGraph(),
        "99eb4144b50a7fd2f029ffc279098faedc0f1cf0a419863601815dfe7d7362f0",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-006", ["max_cycles"] = "80" },
        Seed: 106,
        MaxCycles: 80,
        AssertMeshRouting));

    private static void GoldenWorkloadInjection() => RunGolden(new GoldenCase(
        "P1C-GOLD-007",
        "Workload Injection: 16 packets, interval=10",
        CreateDirectGraph("workload_injection", sourceParameters: new()),
        "e8fd4ab1bd6506b46d8c4343fec15d5a170a550ee99d9e295144622fd311bd89",
        new Dictionary<string, string> { ["scenario"] = "P1C-GOLD-007", ["max_cycles"] = "220" },
        Seed: 107,
        MaxCycles: 220,
        AssertWorkloadInjection));

    private static void RunGolden(GoldenCase golden)
    {
        TestAssert.Equal("1.0", golden.Version, $"{golden.Id} version must be recorded");
        TestAssert.True(!string.IsNullOrWhiteSpace(golden.Expected), $"{golden.Id} expected description must be recorded");
        TestAssert.True(golden.Input.SchemaVersion == HardwareGraph.CurrentSchemaVersion, $"{golden.Id} input graph must declare current schema");

        var first = Run(golden.Input, golden.MaxCycles, golden.Schedule);
        var second = Run(golden.Input, golden.MaxCycles, golden.Schedule);
        golden.AssertExpected(first);

        var firstHash = CanonicalTraceHasher.Compute(first.Trace, golden.Config, golden.Seed);
        var secondHash = CanonicalTraceHasher.Compute(second.Trace, golden.Config, golden.Seed);
        TestAssert.Equal(firstHash.Hash, secondHash.Hash, $"{golden.Id} two identical runs must produce identical canonical hash");
        TestAssert.Equal(CanonicalTraceHasher.Algorithm, firstHash.Algorithm, $"{golden.Id} must record hash algorithm");
        TestAssert.Equal(CanonicalTraceHasher.TraceSchemaVersion, firstHash.TraceSchemaVersion, $"{golden.Id} must record trace schema version");
        TestAssert.Equal(golden.Seed, firstHash.Seed, $"{golden.Id} must record seed");
        TestAssert.True(golden.Config.All(pair => firstHash.Config.TryGetValue(pair.Key, out var value) && value == pair.Value), $"{golden.Id} must record stable config");
        TestAssert.Equal(golden.ExpectedHash, firstHash.Hash, $"{golden.Id} trace hash must match the locked expected baseline");
    }

    private static void AssertSingleLinkLatency(SimulationResult result)
    {
        TestAssert.True(result.Completed, result.CompletionReason);
        TestAssert.Equal(0L, EventCycle(result, e => e.Type == TraceEventType.PacketInjection && e.PacketId == "P_0"), "P1C-GOLD-001 issue cycle must be 0");
        TestAssert.Equal(1L, EventCycle(result, e => e.Type == TraceEventType.LinkTransfer && e.LinkId == "l_source_sink" && e.Detail?.Contains("arrival_cycle=4", StringComparison.Ordinal) == true), "P1C-GOLD-001 link transfer must be issued at cycle 1 with arrival 4 under t+1 injection sampling");
        TestAssert.Equal(4L, EventCycle(result, e => e.Type == TraceEventType.PacketMove && e.ComponentId == "sink" && e.PacketId == "P_0"), "P1C-GOLD-001 packet must arrive at cycle 4");
    }

    private static void AssertBufferBackpressure(SimulationResult result)
    {
        TestAssert.True(result.Completed, result.CompletionReason);
        TestAssert.True(result.Trace.Cycles.SelectMany(c => c.Events).Any(e =>
            e.Type == TraceEventType.Stall &&
            e.ComponentId == "buffer" &&
            e.Detail?.Contains($"stall_reason={StallReason.OutputBufferFull}", StringComparison.Ordinal) == true), "P1C-GOLD-002 buffer must report full output queue");
        TestAssert.True(result.Trace.Cycles.SelectMany(c => c.Events).Any(e =>
            e.Type == TraceEventType.Stall &&
            e.ComponentId == "source" &&
            e.Detail?.Contains("stall_reason=", StringComparison.Ordinal) == true), "P1C-GOLD-002 source must stall while buffer pressure clears");
        TestAssert.Equal(4L, result.Metrics.Global.PacketsInjected, "P1C-GOLD-002 must inject all packets");
        TestAssert.Equal(4L, result.Metrics.Global.PacketsDelivered, "P1C-GOLD-002 must not drop packets");
    }

    private static void AssertRouterRoundRobin(SimulationResult result)
    {
        var winners = result.Trace.Cycles.SelectMany(c => c.Events)
            .Where(e => e.Type == TraceEventType.Arbitration && e.ComponentId == "router")
            .Select(e => DetailValue(e.Detail, "winner_port"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(6)
            .ToList();
        TestAssert.True(winners.SequenceEqual(["north", "south", "east", "north", "south", "east"]), $"P1C-GOLD-003 winners must round-robin north,south,east twice; actual={string.Join(",", winners)}");
    }

    private static void AssertPeCompute(SimulationResult result)
    {
        var startCycle = EventCycle(result, e =>
            e.Type == TraceEventType.Compute &&
            e.ComponentId == "pe" &&
            e.Detail?.Contains("processing until cycle", StringComparison.Ordinal) == true);
        var completeCycle = EventCycle(result, e =>
            e.Type == TraceEventType.Compute &&
            e.ComponentId == "pe" &&
            e.Detail?.Contains("processing complete", StringComparison.Ordinal) == true);
        TestAssert.Equal(4L, completeCycle - startCycle, "P1C-GOLD-004 PE compute must take exactly 4 cycles");
    }

    private static void AssertMemoryRead(SimulationResult result)
    {
        var requestCycle = EventCycle(result, e =>
            e.Type == TraceEventType.Compute &&
            e.ComponentId == "memory" &&
            e.Detail?.Contains("processing_until=16", StringComparison.Ordinal) == true &&
            e.Detail.Contains("op_type=read", StringComparison.Ordinal));
        var responseCycle = EventCycle(result, e =>
            e.Type == TraceEventType.Compute &&
            e.ComponentId == "memory" &&
            e.Detail?.StartsWith("memory_read_complete", StringComparison.Ordinal) == true);
        TestAssert.Equal(11L, requestCycle, "P1C-GOLD-005 memory request must be accepted at cycle 11 after t+1 sampling and link latency");
        TestAssert.Equal(16L, responseCycle, "P1C-GOLD-005 memory response must complete at cycle 16");
    }

    private static void AssertMeshRouting(SimulationResult result)
    {
        var routerTransfers = result.Trace.Cycles.SelectMany(c => c.Events)
            .Where(e => e.Type == TraceEventType.LinkTransfer && e.ComponentId is "r00" or "r10")
            .ToList();
        var first = routerTransfers.First(e => e.ComponentId == "r00");
        var second = routerTransfers.First(e => e.ComponentId == "r10");
        TestAssert.Equal("l_r00_r10", first.LinkId, "P1C-GOLD-006 first mesh hop must go east");
        TestAssert.True(first.Detail?.Contains("routing=xy;axis=x;direction=east", StringComparison.Ordinal) == true, "P1C-GOLD-006 first hop must expose east X routing");
        TestAssert.Equal("l_r10_sink", second.LinkId, "P1C-GOLD-006 second mesh hop must go north");
        TestAssert.True(second.Detail?.Contains("routing=xy;axis=y;direction=north", StringComparison.Ordinal) == true, "P1C-GOLD-006 second hop must expose north Y routing");
    }

    private static void AssertWorkloadInjection(SimulationResult result)
    {
        var cycles = result.Trace.Cycles
            .Where(c => c.Events.Any(e => e.Type == TraceEventType.PacketInjection))
            .Select(c => c.Cycle)
            .ToList();
        TestAssert.Equal(16, cycles.Count, "P1C-GOLD-007 must inject 16 packets");
        TestAssert.True(cycles.SequenceEqual(Enumerable.Range(0, 16).Select(index => (long)index * 10)), $"P1C-GOLD-007 must inject every 10 cycles; actual={string.Join(",", cycles)}");
    }

    private static HardwareGraph CreateFullFidelityGraph()
    {
        var graph = new HardwareGraph
        {
            Parameters = { ["project"] = "phase1c", ["owner"] = "golden" },
            Placement = new PhysicalPlacement
            {
                GridCellMicrometers = 25,
                ComponentPositions =
                {
                    ["source"] = new PhysicalPoint(0, 0),
                    ["pe"] = new PhysicalPoint(100, 25),
                    ["sink"] = new PhysicalPoint(200, 25)
                }
            },
            Routing = new PhysicalRouting
            {
                Routes =
                [
                    new PhysicalRoute
                    {
                        LinkId = "l_source_pe",
                        RouteType = "electrical",
                        Layer = "M4",
                        Path = [new PhysicalPoint(0, 0), new PhysicalPoint(100, 25)]
                    },
                    new PhysicalRoute
                    {
                        LinkId = "l_pe_sink",
                        RouteType = "optical",
                        Layer = "WG",
                        Path = [new PhysicalPoint(100, 25), new PhysicalPoint(200, 25)]
                    }
                ]
            },
            Components =
            [
                new HardwareComponent
                {
                    Id = "source",
                    Name = "Source",
                    Type = ComponentKind.WorkloadSource,
                    Position = new GridPosition(0, 0),
                    ModelRef = "source-model",
                    LatencyModel = "source-latency",
                    EnergyModel = "source-energy",
                    AreaModel = "source-area",
                    Parameters = { ["inject_count"] = "2", ["inject_interval"] = "3" },
                    VisualStyle = { ["color"] = "#247a2e", ["icon"] = "source" },
                    InternalState = { ["expanded"] = "true" },
                    Ports =
                    [
                        new HardwarePort
                        {
                            Name = "out",
                            Direction = PortDirection.Output,
                            SignalType = SignalType.Digital,
                            DataType = HardwareDataType.Packet,
                            Precision = PrecisionKind.INT8,
                            Protocol = PortProtocol.Packet,
                            BandwidthBitsPerCycle = 256,
                            LatencyCycles = 1,
                            ClockDomain = "core",
                            Required = true,
                            MultiConnect = false
                        }
                    ]
                },
                new HardwareComponent
                {
                    Id = "pe",
                    Name = "PE",
                    Type = ComponentKind.ProcessingElement,
                    Position = new GridPosition(1, 1),
                    ModelRef = "pe-model",
                    Parameters = { ["total_macs"] = "1024", ["mac_per_cycle"] = "256" },
                    VisualStyle = { ["color"] = "#315fb5" },
                    Ports =
                    [
                        new HardwarePort { Name = "in", Direction = PortDirection.Input, Required = true, SignalType = SignalType.Digital, DataType = HardwareDataType.Packet, Precision = PrecisionKind.INT8, Protocol = PortProtocol.Packet },
                        new HardwarePort { Name = "out", Direction = PortDirection.Output, Required = true, SignalType = SignalType.Optical, DataType = HardwareDataType.Packet, Precision = PrecisionKind.INT8, Protocol = PortProtocol.Packet, MultiConnect = true }
                    ]
                },
                new HardwareComponent
                {
                    Id = "sink",
                    Name = "Sink",
                    Type = ComponentKind.WorkloadSink,
                    Position = new GridPosition(2, 1),
                    Ports = [new HardwarePort { Name = "in", Direction = PortDirection.Input, Required = true, MultiConnect = true }]
                }
            ],
            Links =
            [
                new HardwareLink
                {
                    Id = "l_source_pe",
                    Source = new PortRef("source", "out"),
                    Destination = new PortRef("pe", "in"),
                    ModelRef = "link-model-a",
                    BandwidthBitsPerCycle = 256,
                    LatencyCycles = 2,
                    EnergyPerBit = 0.02,
                    PhysicalLength = 100,
                    RouteType = "electrical",
                    Parameters = { ["wire"] = "M4" }
                },
                new HardwareLink
                {
                    Id = "l_pe_sink",
                    Source = new PortRef("pe", "out"),
                    Destination = new PortRef("sink", "in"),
                    ModelRef = "link-model-b",
                    BandwidthBitsPerCycle = 128,
                    LatencyCycles = 3,
                    EnergyPerBit = 0.05,
                    PhysicalLength = 150,
                    RouteType = "optical",
                    Parameters = { ["optical_channel"] = "lambda0" }
                }
            ],
            Groups =
            [
                new VisualGroup { Id = "g_compute", Name = "Compute", ComponentIds = ["source", "pe"], Collapsed = true }
            ],
            Macros =
            [
                new MacroComponent
                {
                    Id = "macro_tile",
                    Name = "Tile",
                    InternalComponents =
                    [
                        new HardwareComponent
                        {
                            Id = "inner_pe",
                            Name = "Inner PE",
                            Type = ComponentKind.ProcessingElement,
                            Ports = [new HardwarePort { Name = "in", Direction = PortDirection.Input }]
                        }
                    ],
                    InternalLinks =
                    [
                        new HardwareLink
                        {
                            Id = "inner_link",
                            Source = new PortRef("inner_pe", "out"),
                            Destination = new PortRef("inner_sink", "in"),
                            Parameters = { ["internal"] = "true" }
                        }
                    ],
                    ExternalPortMappings = { ["tile_in"] = new PortRef("inner_pe", "in") }
                }
            ]
        };

        return graph;
    }

    private static HardwareGraph CreateDirectGraph(string name, Dictionary<string, string> sourceParameters, int linkLatency = 1) => new()
    {
        Parameters = { ["golden"] = name },
        Components =
        [
            Component("source", ComponentKind.WorkloadSource, 0, 0, [Out("out")], sourceParameters),
            Component("sink", ComponentKind.WorkloadSink, 1, 0, [In("in")])
        ],
        Links = [Link("l_source_sink", "source", "out", "sink", "in", latencyCycles: linkLatency)]
    };

    private static HardwareGraph CreateBufferBackpressureGraph() => new()
    {
        Components =
        [
            Component("source", ComponentKind.WorkloadSource, 0, 0, [Out("out")], new()
            {
                ["inject_count"] = "4",
                ["inject_interval"] = "1",
                ["queue_capacity"] = "1"
            }),
            Component("buffer", ComponentKind.Buffer, 1, 0, [In("in"), Out("out")], new()
            {
                ["queue_capacity"] = "1"
            }),
            Component("sink", ComponentKind.WorkloadSink, 2, 0, [In("in")])
        ],
        Links =
        [
            Link("l_source_buffer", "source", "out", "buffer", "in"),
            Link("l_buffer_sink", "buffer", "out", "sink", "in", latencyCycles: 6)
        ]
    };

    private static HardwareGraph CreateThreeInputRouterGraph() => new()
    {
        Components =
        [
            Component("source_n", ComponentKind.WorkloadSource, 1, -1, [Out("out")], SourceParameters(2)),
            Component("source_s", ComponentKind.WorkloadSource, 1, 1, [Out("out")], SourceParameters(2)),
            Component("source_e", ComponentKind.WorkloadSource, 2, 0, [Out("out")], SourceParameters(2)),
            Component("router", ComponentKind.Router, 1, 0, [In("north"), In("south"), In("east"), Out("out")], new()
            {
                ["buffer_depth"] = "4",
                ["arbitration_policy"] = "round_robin"
            }),
            Component("sink", ComponentKind.WorkloadSink, 3, 0, [In("in")])
        ],
        Links =
        [
            Link("l_n_router", "source_n", "out", "router", "north"),
            Link("l_s_router", "source_s", "out", "router", "south"),
            Link("l_e_router", "source_e", "out", "router", "east"),
            Link("l_router_sink", "router", "out", "sink", "in")
        ]
    };

    private static HardwareGraph CreatePeComputeGraph() => new()
    {
        Components =
        [
            Component("source", ComponentKind.WorkloadSource, 0, 0, [Out("out")], new()
            {
                ["inject_count"] = "1",
                ["inject_interval"] = "10"
            }),
            Component("pe", ComponentKind.ProcessingElement, 1, 0, [In("in"), Out("out")], new()
            {
                ["total_macs"] = "1024",
                ["mac_per_cycle"] = "256"
            }),
            Component("sink", ComponentKind.WorkloadSink, 2, 0, [In("in")])
        ],
        Links =
        [
            Link("l_source_pe", "source", "out", "pe", "in"),
            Link("l_pe_sink", "pe", "out", "sink", "in")
        ]
    };

    private static HardwareGraph CreateMemoryReadGraph() => new()
    {
        Components =
        [
            Component("source", ComponentKind.WorkloadSource, 0, 0, [Out("out")]),
            Component("memory", ComponentKind.Memory, 1, 0, [In("in"), Out("out")], new()
            {
                ["read_latency"] = "5"
            }),
            Component("sink", ComponentKind.WorkloadSink, 2, 0, [In("in")])
        ],
        Links =
        [
            Link("l_source_memory", "source", "out", "memory", "in"),
            Link("l_memory_sink", "memory", "out", "sink", "in")
        ]
    };

    private static WorkloadSchedule CreateMemoryReadSchedule() => new()
    {
        WorkloadId = "memory_read_at_10",
        Operations =
        [
            new ScheduledOperation
            {
                OperationId = "read0",
                OperationKind = WorkloadOperationKind.SyntheticTraffic,
                ComponentId = "source",
                StartCycle = 9,
                EndCycle = 9,
                PacketCount = 1,
                PacketBits = 128
            }
        ]
    };

    private static HardwareGraph CreateMeshRoutingGraph() => new()
    {
        Parameters = { ["logical_target"] = "(1,1)" },
        Components =
        [
            Component("source", ComponentKind.WorkloadSource, -1, 0, [Out("out")], new()
            {
                ["inject_count"] = "1",
                ["inject_interval"] = "10"
            }),
            Component("r00", ComponentKind.Router, 0, 0, [In("west"), Out("east"), Out("north")], RouterParameters()),
            Component("r10", ComponentKind.Router, 1, 0, [In("west"), Out("north")], RouterParameters()),
            Component("r01", ComponentKind.Router, 0, -1, [In("south"), Out("east")], RouterParameters()),
            Component("sink", ComponentKind.WorkloadSink, 1, -1, [In("in", multiConnect: true)])
        ],
        Links =
        [
            Link("l_source_r00", "source", "out", "r00", "west"),
            Link("l_r00_r10", "r00", "east", "r10", "west"),
            Link("l_r00_r01", "r00", "north", "r01", "south"),
            Link("l_r01_sink", "r01", "east", "sink", "in"),
            Link("l_r10_sink", "r10", "north", "sink", "in")
        ]
    };

    private static SimulationResult Run(HardwareGraph graph, int maxCycles, WorkloadSchedule? schedule = null)
    {
        var compiled = Compile(graph);
        return schedule is null
            ? new CycleSimulationEngine().Run(compiled, new SimulationOptions { MaxCycles = maxCycles })
            : new CycleSimulationEngine().Run(compiled, schedule, new SimulationOptions { MaxCycles = maxCycles });
    }

    private static HardwareSimulationGraph Compile(HardwareGraph graph)
    {
        var result = new SimulationGraphCompiler().CompileHardware(graph);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        return result.Graph!;
    }

    private static long EventCycle(SimulationResult result, Func<TraceEvent, bool> predicate)
    {
        foreach (var cycle in result.Trace.Cycles)
        {
            if (cycle.Events.Any(predicate))
            {
                return cycle.Cycle;
            }
        }

        throw new InvalidOperationException("Expected trace event was not found.");
    }

    private static string DetailValue(string? detail, string key)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "";
        }

        var prefix = key + "=";
        return detail
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.Ordinal))?
            .Substring(prefix.Length) ?? "";
    }

    private static Dictionary<string, string> SourceParameters(int packetCount) => new()
    {
        ["inject_count"] = packetCount.ToString(),
        ["inject_interval"] = "1",
        ["queue_capacity"] = packetCount.ToString()
    };

    private static Dictionary<string, string> RouterParameters() => new()
    {
        ["buffer_depth"] = "4",
        ["routing_policy"] = "xy",
        ["arbitration_policy"] = "round_robin"
    };

    private static HardwareComponent Component(
        string id,
        ComponentKind kind,
        int x,
        int y,
        List<HardwarePort> ports,
        Dictionary<string, string>? parameters = null) => new()
        {
            Id = id,
            Name = id,
            Type = kind,
            Position = new GridPosition(x, y),
            Ports = ports,
            Parameters = parameters ?? new()
        };

    private static HardwarePort In(string name, bool multiConnect = false) => new()
    {
        Name = name,
        Direction = PortDirection.Input,
        Required = true,
        MultiConnect = multiConnect
    };

    private static HardwarePort Out(string name) => new()
    {
        Name = name,
        Direction = PortDirection.Output,
        Required = true
    };

    private static HardwareLink Link(
        string id,
        string source,
        string sourcePort,
        string destination,
        string destinationPort,
        int latencyCycles = 1,
        int bandwidthBitsPerCycle = 128) => new()
        {
            Id = id,
            Source = new PortRef(source, sourcePort),
            Destination = new PortRef(destination, destinationPort),
            LatencyCycles = latencyCycles,
            BandwidthBitsPerCycle = bandwidthBitsPerCycle
        };

    private static void AssertGraphEquivalent(HardwareGraph expected, HardwareGraph actual)
    {
        TestAssert.Equal(expected.SchemaVersion, actual.SchemaVersion, "schema_version must roundtrip");
        AssertDictionary(expected.Parameters, actual.Parameters, "graph parameters");
        TestAssert.Equal(expected.Components.Count, actual.Components.Count, "component set size must roundtrip");
        foreach (var expectedComponent in expected.Components.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var actualComponent = actual.Components.Single(component => component.Id == expectedComponent.Id);
            AssertComponent(expectedComponent, actualComponent, $"component {expectedComponent.Id}");
        }

        TestAssert.Equal(expected.Links.Count, actual.Links.Count, "link set size must roundtrip");
        foreach (var expectedLink in expected.Links.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            var actualLink = actual.Links.Single(link => link.Id == expectedLink.Id);
            AssertLink(expectedLink, actualLink, $"link {expectedLink.Id}");
        }

        TestAssert.Equal(expected.Groups.Count, actual.Groups.Count, "group set size must roundtrip");
        foreach (var expectedGroup in expected.Groups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            var actualGroup = actual.Groups.Single(group => group.Id == expectedGroup.Id);
            TestAssert.Equal(expectedGroup.Name, actualGroup.Name, $"group {expectedGroup.Id} name");
            TestAssert.True(expectedGroup.ComponentIds.SequenceEqual(actualGroup.ComponentIds), $"group {expectedGroup.Id} component ids");
            TestAssert.Equal(expectedGroup.Collapsed, actualGroup.Collapsed, $"group {expectedGroup.Id} collapsed");
        }

        TestAssert.Equal(expected.Macros.Count, actual.Macros.Count, "macro set size must roundtrip");
        foreach (var expectedMacro in expected.Macros.OrderBy(macro => macro.Id, StringComparer.Ordinal))
        {
            var actualMacro = actual.Macros.Single(macro => macro.Id == expectedMacro.Id);
            TestAssert.Equal(expectedMacro.Name, actualMacro.Name, $"macro {expectedMacro.Id} name");
            TestAssert.Equal(expectedMacro.InternalComponents.Count, actualMacro.InternalComponents.Count, $"macro {expectedMacro.Id} internal components");
            TestAssert.Equal(expectedMacro.InternalLinks.Count, actualMacro.InternalLinks.Count, $"macro {expectedMacro.Id} internal links");
            AssertDictionary(
                expectedMacro.ExternalPortMappings.ToDictionary(pair => pair.Key, pair => $"{pair.Value.ComponentId}.{pair.Value.PortName}", StringComparer.OrdinalIgnoreCase),
                actualMacro.ExternalPortMappings.ToDictionary(pair => pair.Key, pair => $"{pair.Value.ComponentId}.{pair.Value.PortName}", StringComparer.OrdinalIgnoreCase),
                $"macro {expectedMacro.Id} external mappings");
        }

        TestAssert.Near(expected.Placement!.GridCellMicrometers, actual.Placement!.GridCellMicrometers, 0, "placement grid cell");
        foreach (var pair in expected.Placement.ComponentPositions)
        {
            var actualPoint = actual.Placement.ComponentPositions[pair.Key];
            TestAssert.Near(pair.Value.X, actualPoint.X, 0, $"placement {pair.Key} x");
            TestAssert.Near(pair.Value.Y, actualPoint.Y, 0, $"placement {pair.Key} y");
        }

        TestAssert.Equal(expected.Routing!.Routes.Count, actual.Routing!.Routes.Count, "routing route count");
        foreach (var expectedRoute in expected.Routing.Routes.OrderBy(route => route.LinkId, StringComparer.Ordinal))
        {
            var actualRoute = actual.Routing.Routes.Single(route => route.LinkId == expectedRoute.LinkId);
            TestAssert.Equal(expectedRoute.RouteType, actualRoute.RouteType, $"route {expectedRoute.LinkId} type");
            TestAssert.Equal(expectedRoute.Layer, actualRoute.Layer, $"route {expectedRoute.LinkId} layer");
            TestAssert.Equal(expectedRoute.Path.Count, actualRoute.Path.Count, $"route {expectedRoute.LinkId} path count");
            for (var index = 0; index < expectedRoute.Path.Count; index++)
            {
                TestAssert.Near(expectedRoute.Path[index].X, actualRoute.Path[index].X, 0, $"route {expectedRoute.LinkId} path {index} x");
                TestAssert.Near(expectedRoute.Path[index].Y, actualRoute.Path[index].Y, 0, $"route {expectedRoute.LinkId} path {index} y");
            }
        }
    }

    private static void AssertComponent(HardwareComponent expected, HardwareComponent actual, string label)
    {
        TestAssert.Equal(expected.Name, actual.Name, $"{label} name");
        TestAssert.Equal(expected.Type, actual.Type, $"{label} type");
        TestAssert.Equal(expected.Position, actual.Position, $"{label} position");
        TestAssert.Equal(expected.ModelRef, actual.ModelRef, $"{label} model_ref");
        TestAssert.Equal(expected.LatencyModel, actual.LatencyModel, $"{label} latency_model");
        TestAssert.Equal(expected.EnergyModel, actual.EnergyModel, $"{label} energy_model");
        TestAssert.Equal(expected.AreaModel, actual.AreaModel, $"{label} area_model");
        AssertDictionary(expected.Parameters, actual.Parameters, $"{label} parameters");
        AssertDictionary(expected.VisualStyle, actual.VisualStyle, $"{label} visual style");
        AssertDictionary(expected.InternalState, actual.InternalState, $"{label} internal state");
        TestAssert.Equal(expected.Ports.Count, actual.Ports.Count, $"{label} port count");
        foreach (var expectedPort in expected.Ports.OrderBy(port => port.Name, StringComparer.Ordinal))
        {
            var actualPort = actual.Ports.Single(port => port.Name == expectedPort.Name);
            TestAssert.Equal(expectedPort.Direction, actualPort.Direction, $"{label}.{expectedPort.Name} direction");
            TestAssert.Equal(expectedPort.SignalType, actualPort.SignalType, $"{label}.{expectedPort.Name} signal type");
            TestAssert.Equal(expectedPort.DataType, actualPort.DataType, $"{label}.{expectedPort.Name} data type");
            TestAssert.Equal(expectedPort.Precision, actualPort.Precision, $"{label}.{expectedPort.Name} precision");
            TestAssert.Equal(expectedPort.Protocol, actualPort.Protocol, $"{label}.{expectedPort.Name} protocol");
            TestAssert.Equal(expectedPort.BandwidthBitsPerCycle, actualPort.BandwidthBitsPerCycle, $"{label}.{expectedPort.Name} bandwidth");
            TestAssert.Equal(expectedPort.LatencyCycles, actualPort.LatencyCycles, $"{label}.{expectedPort.Name} latency");
            TestAssert.Equal(expectedPort.ClockDomain, actualPort.ClockDomain, $"{label}.{expectedPort.Name} clock domain");
            TestAssert.Equal(expectedPort.Required, actualPort.Required, $"{label}.{expectedPort.Name} required");
            TestAssert.Equal(expectedPort.MultiConnect, actualPort.MultiConnect, $"{label}.{expectedPort.Name} multi-connect");
        }
    }

    private static void AssertLink(HardwareLink expected, HardwareLink actual, string label)
    {
        TestAssert.Equal(expected.Source, actual.Source, $"{label} source");
        TestAssert.Equal(expected.Destination, actual.Destination, $"{label} destination");
        TestAssert.Equal(expected.ModelRef, actual.ModelRef, $"{label} model_ref");
        TestAssert.Equal(expected.BandwidthBitsPerCycle, actual.BandwidthBitsPerCycle, $"{label} bandwidth");
        TestAssert.Equal(expected.LatencyCycles, actual.LatencyCycles, $"{label} latency");
        TestAssert.Near(expected.EnergyPerBit, actual.EnergyPerBit, 0, $"{label} energy");
        TestAssert.Near(expected.PhysicalLength, actual.PhysicalLength, 0, $"{label} physical length");
        TestAssert.Equal(expected.RouteType, actual.RouteType, $"{label} route type");
        AssertDictionary(expected.Parameters, actual.Parameters, $"{label} parameters");
    }

    private static void AssertDictionary(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual, string label)
    {
        TestAssert.Equal(expected.Count, actual.Count, $"{label} key count");
        foreach (var pair in expected.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            TestAssert.True(actual.TryGetValue(pair.Key, out var value), $"{label} missing key {pair.Key}");
            TestAssert.Equal(pair.Value, value, $"{label} value for {pair.Key}");
        }
    }

    private sealed record GoldenCase(
        string Id,
        string Expected,
        HardwareGraph Input,
        string ExpectedHash,
        IReadOnlyDictionary<string, string> Config,
        int Seed,
        int MaxCycles,
        Action<SimulationResult> AssertExpected,
        WorkloadSchedule? Schedule = null)
    {
        public string Version { get; } = "1.0";
    }

    private sealed class MarkerMigration : IHardwareGraphMigration
    {
        public MarkerMigration(string fromVersion, string toVersion)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }

        public string FromVersion { get; }
        public string ToVersion { get; }

        public JsonObject Migrate(JsonObject source)
        {
            var migrated = JsonNode.Parse(source.ToJsonString())!.AsObject();
            migrated["schema_version"] = ToVersion;
            migrated[$"migrated_{ToVersion.Replace(".", "_", StringComparison.Ordinal)}"] = true;
            return migrated;
        }
    }
}
