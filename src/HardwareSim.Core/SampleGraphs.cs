namespace HardwareSim.Core;

/// <summary>Provides sample graphs operations for hardware design and simulation workflows.</summary>
public static class SampleGraphs
{
    /// <summary>Creates route grid congestion routing from the supplied inputs.</summary>
    public static PhysicalRouting CreateRouteGridCongestionRouting()
    {
        return new PhysicalRouting
        {
            Routes =
            [
                new PhysicalRoute
                {
                    LinkId = "route_a",
                    Layer = "M3",
                    Path =
                    [
                        new PhysicalPoint(0, 0),
                        new PhysicalPoint(200, 0),
                        new PhysicalPoint(200, 100)
                    ]
                },
                new PhysicalRoute
                {
                    LinkId = "route_b",
                    Layer = "M3",
                    Path =
                    [
                        new PhysicalPoint(100, 0),
                        new PhysicalPoint(200, 0),
                        new PhysicalPoint(300, 0)
                    ]
                }
            ]
        };
    }

    /// <summary>Creates memory router pe reduction sink graph from the supplied inputs.</summary>
    public static HardwareGraph CreateMemoryRouterPeReductionSinkGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source", "Workload Source", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "4", ["packet_bits"] = "128" }));
        graph.Components.Add(Component("memory", "Global Buffer", ComponentKind.Memory, 1, 0,
            [In("in"), Out("out")], new() { ["memory_latency_cycles"] = "1" }));
        graph.Components.Add(Component("router", "Router", ComponentKind.Router, 2, 0, [In("in"), Out("out")]));
        graph.Components.Add(Component("pe0", "PE 0", ComponentKind.ProcessingElement, 3, 0,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "2" }));
        graph.Components.Add(Component("reduce", "Reduction Unit", ComponentKind.ReductionUnit, 4, 0,
            [In("in"), Out("out")], new() { ["num_inputs"] = "1", ["accumulate_latency"] = "1" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 5, 0, [In("in")]));

        graph.Links.Add(Link("l_source_memory", "source", "out", "memory", "in"));
        graph.Links.Add(Link("l_memory_router", "memory", "out", "router", "in"));
        graph.Links.Add(Link("l_router_pe0", "router", "out", "pe0", "in"));
        graph.Links.Add(Link("l_pe0_reduce", "pe0", "out", "reduce", "in"));
        graph.Links.Add(Link("l_reduce_sink", "reduce", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates contended shared router graph from the supplied inputs.</summary>
    public static HardwareGraph CreateContendedSharedRouterGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source_a", "Source A", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "4", ["packet_bits"] = "128", ["queue_capacity"] = "2", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("source_b", "Source B", ComponentKind.WorkloadSource, 0, 1,
            [Out("out")], new() { ["packet_count"] = "4", ["packet_bits"] = "128", ["queue_capacity"] = "2", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("router", "Shared Router", ComponentKind.Router, 1, 0,
            [In("in", multiConnect: true), Out("out")], new() { ["queue_capacity"] = "1" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 2, 0, [In("in")]));

        graph.Links.Add(Link("l_source_a_router", "source_a", "out", "router", "in", latencyCycles: 1));
        graph.Links.Add(Link("l_source_b_router", "source_b", "out", "router", "in", latencyCycles: 1));
        graph.Links.Add(Link("l_router_sink", "router", "out", "sink", "in", bandwidthBitsPerCycle: 64, latencyCycles: 3));
        return graph;
    }

    /// <summary>Creates router arbitration graph from the supplied inputs.</summary>
    public static HardwareGraph CreateRouterArbitrationGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source_a", "Source A", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "3", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("source_b", "Source B", ComponentKind.WorkloadSource, 0, 1,
            [Out("out")], new() { ["packet_count"] = "3", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("router", "Round-Robin Router", ComponentKind.Router, 1, 0,
            [In("in", multiConnect: true), Out("out")],
            new() { ["queue_capacity"] = "8", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 2, 0, [In("in")]));

        graph.Links.Add(Link("l_source_a_router", "source_a", "out", "router", "in"));
        graph.Links.Add(Link("l_source_b_router", "source_b", "out", "router", "in"));
        graph.Links.Add(Link("l_router_sink", "router", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates memory contention graph from the supplied inputs.</summary>
    public static HardwareGraph CreateMemoryContentionGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source_a", "Source A", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "3", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("source_b", "Source B", ComponentKind.WorkloadSource, 0, 1,
            [Out("out")], new() { ["packet_count"] = "3", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("memory", "Single-Port Memory", ComponentKind.Memory, 1, 0,
            [In("in", multiConnect: true), Out("out")],
            new() { ["memory_latency_cycles"] = "1", ["max_concurrent_accesses"] = "1", ["queue_capacity"] = "4" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 2, 0, [In("in")]));

        graph.Links.Add(Link("l_source_a_memory", "source_a", "out", "memory", "in"));
        graph.Links.Add(Link("l_source_b_memory", "source_b", "out", "memory", "in"));
        graph.Links.Add(Link("l_memory_sink", "memory", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates banked memory contention graph from the supplied inputs.</summary>
    public static HardwareGraph CreateBankedMemoryContentionGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source_a", "Source A", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "2", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("source_b", "Source B", ComponentKind.WorkloadSource, 0, 1,
            [Out("out")], new() { ["packet_count"] = "2", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("source_c", "Source C", ComponentKind.WorkloadSource, 0, 2,
            [Out("out")], new() { ["packet_count"] = "2", ["packet_bits"] = "128", ["queue_capacity"] = "3", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("memory", "Two-Bank Memory", ComponentKind.Memory, 1, 1,
            [In("in", multiConnect: true), Out("out")],
            new() { ["memory_latency_cycles"] = "1", ["memory_banks"] = "2", ["max_concurrent_accesses"] = "1", ["queue_capacity"] = "4" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 2, 1, [In("in")]));

        graph.Links.Add(Link("l_source_a_memory", "source_a", "out", "memory", "in"));
        graph.Links.Add(Link("l_source_b_memory", "source_b", "out", "memory", "in"));
        graph.Links.Add(Link("l_source_c_memory", "source_c", "out", "memory", "in"));
        graph.Links.Add(Link("l_memory_sink", "memory", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates multi pe shared reduction graph from the supplied inputs.</summary>
    public static HardwareGraph CreateMultiPeSharedReductionGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source", "Workload Source", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "8", ["packet_bits"] = "128", ["queue_capacity"] = "4", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("router", "Round-Robin Router", ComponentKind.Router, 1, 0,
            [In("in"), Out("out0"), Out("out1")],
            new() { ["queue_capacity"] = "6", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("pe0", "PE 0", ComponentKind.ProcessingElement, 2, -1,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "1", ["queue_capacity"] = "1" }));
        graph.Components.Add(Component("pe1", "PE 1", ComponentKind.ProcessingElement, 2, 1,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "1", ["queue_capacity"] = "1" }));
        graph.Components.Add(Component("reduce", "Shared Reduction Unit", ComponentKind.ReductionUnit, 3, 0,
            [In("in", multiConnect: true), Out("out")], new() { ["num_inputs"] = "1", ["accumulate_latency"] = "1", ["queue_capacity"] = "1" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 4, 0, [In("in")]));

        graph.Links.Add(Link("l_source_router", "source", "out", "router", "in"));
        graph.Links.Add(Link("l_router_pe0", "router", "out0", "pe0", "in"));
        graph.Links.Add(Link("l_router_pe1", "router", "out1", "pe1", "in"));
        graph.Links.Add(Link("l_pe0_reduce", "pe0", "out", "reduce", "in"));
        graph.Links.Add(Link("l_pe1_reduce", "pe1", "out", "reduce", "in"));
        graph.Links.Add(Link("l_reduce_sink", "reduce", "out", "sink", "in", bandwidthBitsPerCycle: 32, latencyCycles: 2));
        return graph;
    }

    /// <summary>Creates a source-router-router-pes-reductions-router-reduction-router-sink flow graph.</summary>
    public static HardwareGraph CreateSourceRouterPeReductionFlowGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source", "Workload Source", ComponentKind.WorkloadSource, 0, 2,
            [Out("out")], new() { ["packet_count"] = "8", ["packet_bits"] = "128", ["queue_capacity"] = "8", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("ingress_router", "Ingress Router", ComponentKind.Router, 2, 2,
            [In("in"), Out("out0"), Out("out1")],
            new() { ["queue_capacity"] = "8", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("branch_router_0", "Branch Router 0", ComponentKind.Router, 4, 1,
            [In("in"), Out("out0"), Out("out1")],
            new() { ["queue_capacity"] = "4", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("branch_router_1", "Branch Router 1", ComponentKind.Router, 4, 3,
            [In("in"), Out("out0"), Out("out1")],
            new() { ["queue_capacity"] = "4", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("pe0", "PE 0", ComponentKind.ProcessingElement, 6, 0,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "2", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("pe1", "PE 1", ComponentKind.ProcessingElement, 6, 1,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "2", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("pe2", "PE 2", ComponentKind.ProcessingElement, 6, 3,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "2", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("pe3", "PE 3", ComponentKind.ProcessingElement, 6, 4,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "2", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("reduce0", "Reduction 0", ComponentKind.ReductionUnit, 8, 1,
            [In("in", multiConnect: true), Out("out")], new() { ["num_inputs"] = "2", ["accumulate_latency"] = "1", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("reduce1", "Reduction 1", ComponentKind.ReductionUnit, 8, 3,
            [In("in", multiConnect: true), Out("out")], new() { ["num_inputs"] = "2", ["accumulate_latency"] = "1", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("merge_router_0", "Merge Router 0", ComponentKind.Router, 10, 1,
            [In("in"), Out("out")], new() { ["queue_capacity"] = "4", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("merge_router_1", "Merge Router 1", ComponentKind.Router, 10, 3,
            [In("in"), Out("out")], new() { ["queue_capacity"] = "4", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("final_reduce", "Final Reduction", ComponentKind.ReductionUnit, 12, 2,
            [In("in", multiConnect: true), Out("out")], new() { ["num_inputs"] = "2", ["accumulate_latency"] = "2", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("egress_router", "Egress Router", ComponentKind.Router, 14, 2,
            [In("in"), Out("out")], new() { ["queue_capacity"] = "4", ["routing_policy"] = "round_robin", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("sink", "Output Sink", ComponentKind.WorkloadSink, 16, 2, [In("in")]));

        graph.Links.Add(Link("l_source_ingress", "source", "out", "ingress_router", "in"));
        graph.Links.Add(Link("l_ingress_branch0", "ingress_router", "out0", "branch_router_0", "in"));
        graph.Links.Add(Link("l_ingress_branch1", "ingress_router", "out1", "branch_router_1", "in"));
        graph.Links.Add(Link("l_branch0_pe0", "branch_router_0", "out0", "pe0", "in"));
        graph.Links.Add(Link("l_branch0_pe1", "branch_router_0", "out1", "pe1", "in"));
        graph.Links.Add(Link("l_branch1_pe2", "branch_router_1", "out0", "pe2", "in"));
        graph.Links.Add(Link("l_branch1_pe3", "branch_router_1", "out1", "pe3", "in"));
        graph.Links.Add(Link("l_pe0_reduce0", "pe0", "out", "reduce0", "in"));
        graph.Links.Add(Link("l_pe1_reduce0", "pe1", "out", "reduce0", "in"));
        graph.Links.Add(Link("l_pe2_reduce1", "pe2", "out", "reduce1", "in"));
        graph.Links.Add(Link("l_pe3_reduce1", "pe3", "out", "reduce1", "in"));
        graph.Links.Add(Link("l_reduce0_merge0", "reduce0", "out", "merge_router_0", "in"));
        graph.Links.Add(Link("l_reduce1_merge1", "reduce1", "out", "merge_router_1", "in"));
        graph.Links.Add(Link("l_merge0_final_reduce", "merge_router_0", "out", "final_reduce", "in"));
        graph.Links.Add(Link("l_merge1_final_reduce", "merge_router_1", "out", "final_reduce", "in"));
        graph.Links.Add(Link("l_final_reduce_egress", "final_reduce", "out", "egress_router", "in", bandwidthBitsPerCycle: 64, latencyCycles: 2));
        graph.Links.Add(Link("l_egress_sink", "egress_router", "out", "sink", "in", bandwidthBitsPerCycle: 64, latencyCycles: 2));
        return graph;
    }

    /// <summary>Creates adaptive routing graph from the supplied inputs.</summary>
    public static HardwareGraph CreateAdaptiveRoutingGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source", "Workload Source", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "6", ["packet_bits"] = "128", ["queue_capacity"] = "6", ["inject_interval"] = "1" }));
        graph.Components.Add(Component("router", "Adaptive Router", ComponentKind.Router, 1, 0,
            [In("in"), Out("out0"), Out("out1")],
            new() { ["queue_capacity"] = "6", ["routing_policy"] = "adaptive_least_busy", ["arbitration_policy"] = "round_robin" }));
        graph.Components.Add(Component("pe_slow", "Slow Path PE", ComponentKind.ProcessingElement, 2, -1,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "1", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("pe_fast", "Fast Path PE", ComponentKind.ProcessingElement, 2, 1,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "1", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 3, 0, [In("in", multiConnect: true)]));

        graph.Links.Add(Link("l_source_router", "source", "out", "router", "in"));
        graph.Links.Add(Link("l_router_slow", "router", "out0", "pe_slow", "in", latencyCycles: 4));
        graph.Links.Add(Link("l_router_fast", "router", "out1", "pe_fast", "in", latencyCycles: 1));
        graph.Links.Add(Link("l_slow_sink", "pe_slow", "out", "sink", "in"));
        graph.Links.Add(Link("l_fast_sink", "pe_fast", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates optical channel contention graph from the supplied inputs.</summary>
    public static HardwareGraph CreateOpticalChannelContentionGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source_a", "Optical Source A", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "2", ["packet_bits"] = "128", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("source_b", "Optical Source B", ComponentKind.WorkloadSource, 0, 1,
            [Out("out")], new() { ["packet_count"] = "2", ["packet_bits"] = "128", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("sink", "Optical Sink", ComponentKind.WorkloadSink, 2, 0,
            [In("in", multiConnect: true)]));

        var linkA = Link("l_source_a_sink", "source_a", "out", "sink", "in", latencyCycles: 2);
        linkA.RouteType = "optical";
        linkA.EnergyPerBit = 0.05;
        linkA.Parameters["optical_channel"] = "lambda0";
        var linkB = Link("l_source_b_sink", "source_b", "out", "sink", "in", latencyCycles: 2);
        linkB.RouteType = "optical";
        linkB.EnergyPerBit = 0.05;
        linkB.Parameters["optical_channel"] = "lambda0";

        graph.Links.Add(linkA);
        graph.Links.Add(linkB);
        return graph;
    }

    /// <summary>Creates precision converter graph from the supplied inputs.</summary>
    public static HardwareGraph CreatePrecisionConverterGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source", "FP16 Source", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "4", ["packet_bits"] = "512", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("quantizer", "FP16 to INT4 Quantizer", ComponentKind.Quantizer, 1, 0,
            [In("in"), Out("out")],
            new()
            {
                ["source_precision"] = "FP16",
                ["target_precision"] = "INT4",
                ["conversion_latency_cycles"] = "1",
                ["conversion_energy_pj_per_bit"] = "0.02"
            }));
        graph.Components.Add(Component("pe0", "INT4 PE", ComponentKind.ProcessingElement, 2, 0,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "1" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 3, 0, [In("in")]));

        graph.Components.Single(c => c.Id == "source").Ports.Single(p => p.Name == "out").Precision = PrecisionKind.FP16;
        graph.Components.Single(c => c.Id == "quantizer").Ports.Single(p => p.Name == "in").Precision = PrecisionKind.FP16;
        graph.Components.Single(c => c.Id == "quantizer").Ports.Single(p => p.Name == "out").Precision = PrecisionKind.INT4;
        graph.Components.Single(c => c.Id == "pe0").Ports.Single(p => p.Name == "in").Precision = PrecisionKind.INT4;
        graph.Components.Single(c => c.Id == "pe0").Ports.Single(p => p.Name == "out").Precision = PrecisionKind.INT4;
        graph.Components.Single(c => c.Id == "sink").Ports.Single(p => p.Name == "in").Precision = PrecisionKind.INT4;

        graph.Links.Add(Link("l_source_quantizer", "source", "out", "quantizer", "in"));
        graph.Links.Add(Link("l_quantizer_pe0", "quantizer", "out", "pe0", "in"));
        graph.Links.Add(Link("l_pe0_sink", "pe0", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates compiler precision adapter graph from the supplied inputs.</summary>
    public static HardwareGraph CreateCompilerPrecisionAdapterGraph()
    {
        var graph = new HardwareGraph();
        graph.Components.Add(Component("source", "FP16 Source", ComponentKind.WorkloadSource, 0, 0,
            [Out("out")], new() { ["packet_count"] = "4", ["packet_bits"] = "512", ["queue_capacity"] = "2" }));
        graph.Components.Add(Component("pe0", "INT4 PE", ComponentKind.ProcessingElement, 1, 0,
            [In("in"), Out("out")], new() { ["compute_latency_cycles"] = "1" }));
        graph.Components.Add(Component("sink", "Output Buffer", ComponentKind.WorkloadSink, 2, 0, [In("in")]));

        graph.Components.Single(c => c.Id == "source").Ports.Single(p => p.Name == "out").Precision = PrecisionKind.FP16;
        graph.Components.Single(c => c.Id == "pe0").Ports.Single(p => p.Name == "in").Precision = PrecisionKind.INT4;
        graph.Components.Single(c => c.Id == "pe0").Ports.Single(p => p.Name == "out").Precision = PrecisionKind.INT4;
        graph.Components.Single(c => c.Id == "sink").Ports.Single(p => p.Name == "in").Precision = PrecisionKind.INT4;

        graph.Links.Add(Link("l_source_pe0", "source", "out", "pe0", "in"));
        graph.Links.Add(Link("l_pe0_sink", "pe0", "out", "sink", "in"));
        return graph;
    }

    /// <summary>Creates adapter planning mismatch graph from the supplied inputs.</summary>
    public static HardwareGraph CreateAdapterPlanningMismatchGraph()
    {
        var graph = new HardwareGraph();
        AddMismatchExample(graph, "quantizer", 0, SignalType.Digital, SignalType.Digital,
            PrecisionKind.FP16, PrecisionKind.INT4, HardwareDataType.Packet, HardwareDataType.Packet,
            PortProtocol.Packet, PortProtocol.Packet);
        AddMismatchExample(graph, "dequantizer", 1, SignalType.Digital, SignalType.Digital,
            PrecisionKind.INT4, PrecisionKind.FP16, HardwareDataType.Packet, HardwareDataType.Packet,
            PortProtocol.Packet, PortProtocol.Packet);
        AddMismatchExample(graph, "protocol", 2, SignalType.Digital, SignalType.Digital,
            PrecisionKind.Any, PrecisionKind.Any, HardwareDataType.Tensor, HardwareDataType.Packet,
            PortProtocol.Streaming, PortProtocol.Packet);
        AddMismatchExample(graph, "eo", 3, SignalType.Digital, SignalType.Optical,
            PrecisionKind.Any, PrecisionKind.Any, HardwareDataType.Packet, HardwareDataType.Packet,
            PortProtocol.Packet, PortProtocol.Packet);
        AddMismatchExample(graph, "oe", 4, SignalType.Optical, SignalType.Digital,
            PrecisionKind.Any, PrecisionKind.Any, HardwareDataType.Packet, HardwareDataType.Packet,
            PortProtocol.Packet, PortProtocol.Packet);
        AddMismatchExample(graph, "adc", 5, SignalType.Analog, SignalType.Digital,
            PrecisionKind.Any, PrecisionKind.Any, HardwareDataType.Packet, HardwareDataType.Packet,
            PortProtocol.Packet, PortProtocol.Packet);
        AddMismatchExample(graph, "dac", 6, SignalType.Digital, SignalType.Analog,
            PrecisionKind.Any, PrecisionKind.Any, HardwareDataType.Packet, HardwareDataType.Packet,
            PortProtocol.Packet, PortProtocol.Packet);
        return graph;
    }

    /// <summary>Creates multi link adapter chain mismatch graph from the supplied inputs.</summary>
    public static HardwareGraph CreateMultiLinkAdapterChainMismatchGraph()
    {
        var graph = new HardwareGraph();
        AddMismatchExample(graph, "chain_a", 0, SignalType.Optical, SignalType.Digital,
            PrecisionKind.FP16, PrecisionKind.INT4, HardwareDataType.Tensor, HardwareDataType.Packet,
            PortProtocol.Streaming, PortProtocol.Packet);
        AddMismatchExample(graph, "chain_b", 1, SignalType.Optical, SignalType.Digital,
            PrecisionKind.FP16, PrecisionKind.INT4, HardwareDataType.Tensor, HardwareDataType.Packet,
            PortProtocol.Streaming, PortProtocol.Packet);
        return graph;
    }

    private static HardwareComponent Component(
        string id,
        string name,
        ComponentKind kind,
        int x,
        int y,
        List<HardwarePort> ports,
        Dictionary<string, string>? parameters = null) =>
        new()
        {
            Id = id,
            Name = name,
            Type = kind,
            Position = new GridPosition(x, y),
            Ports = ports,
            Parameters = parameters ?? new(StringComparer.OrdinalIgnoreCase)
        };

    private static HardwarePort In(string name, bool multiConnect = false) => new()
    {
        Name = name,
        Direction = PortDirection.Input,
        Required = true,
        SignalType = SignalType.Digital,
        DataType = HardwareDataType.Packet,
        Precision = PrecisionKind.Any,
        Protocol = PortProtocol.Packet,
        MultiConnect = multiConnect
    };

    private static HardwarePort Out(string name) => new()
    {
        Name = name,
        Direction = PortDirection.Output,
        Required = true,
        SignalType = SignalType.Digital,
        DataType = HardwareDataType.Packet,
        Precision = PrecisionKind.Any,
        Protocol = PortProtocol.Packet
    };

    private static HardwareLink Link(
        string id,
        string source,
        string sourcePort,
        string destination,
        string destinationPort,
        int bandwidthBitsPerCycle = 128,
        int latencyCycles = 1) => new()
    {
        Id = id,
        Source = new PortRef(source, sourcePort),
        Destination = new PortRef(destination, destinationPort),
        BandwidthBitsPerCycle = bandwidthBitsPerCycle,
        LatencyCycles = latencyCycles,
        EnergyPerBit = 0.01
    };

    private static void AddMismatchExample(
        HardwareGraph graph,
        string id,
        int y,
        SignalType sourceSignal,
        SignalType destinationSignal,
        PrecisionKind sourcePrecision,
        PrecisionKind destinationPrecision,
        HardwareDataType sourceDataType,
        HardwareDataType destinationDataType,
        PortProtocol sourceProtocol,
        PortProtocol destinationProtocol)
    {
        var source = Component($"{id}_source", $"{id} source", ComponentKind.WorkloadSource, 0, y,
            [Out("out")], new() { ["packet_count"] = "1", ["packet_bits"] = "128" });
        var destination = Component($"{id}_sink", $"{id} sink", ComponentKind.WorkloadSink, 1, y, [In("in")]);
        var sourcePort = source.Ports.Single(p => p.Name == "out");
        sourcePort.SignalType = sourceSignal;
        sourcePort.Precision = sourcePrecision;
        sourcePort.DataType = sourceDataType;
        sourcePort.Protocol = sourceProtocol;

        var destinationPort = destination.Ports.Single(p => p.Name == "in");
        destinationPort.SignalType = destinationSignal;
        destinationPort.Precision = destinationPrecision;
        destinationPort.DataType = destinationDataType;
        destinationPort.Protocol = destinationProtocol;

        graph.Components.Add(source);
        graph.Components.Add(destination);
        graph.Links.Add(Link($"l_{id}", source.Id, "out", destination.Id, "in"));
    }
}
