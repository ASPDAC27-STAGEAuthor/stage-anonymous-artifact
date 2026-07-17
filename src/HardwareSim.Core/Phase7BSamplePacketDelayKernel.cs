using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Creates the non-PE sample template used to prove the generic component execution framework.</summary>
public static class Phase7BSampleComponentTemplates
{
    /// <summary>Creates a published packet-delay template bound to the Phase 7B sample runtime kernel.</summary>
    public static ComponentTemplate PacketDelay()
    {
        var template = new ComponentTemplate
        {
            TemplateId = "Phase7B_Sample_Packet_Delay",
            DisplayName = "Phase 7B Sample Packet Delay",
            Version = "1.0.0",
            Category = "Custom",
            TargetKind = ComponentTemplateTargetKind.Custom,
            Lifecycle = ComponentTemplateLifecycleState.Published,
            Provenance = new ComponentTemplateProvenance
            {
                Source = "phase7b.sample.packet_delay",
                Author = "HardwareSim Phase7B/7C",
                ToolVersion = "phase7c-component-execution-framework"
            },
            Parameters =
            [
                IntegerParameter("processing_latency_cycles", "2", 1, 64),
                IntegerParameter("input_queue_depth", "2", 1, 64),
                IntegerParameter("output_queue_depth", "1", 1, 64),
                IntegerParameter("issue_interval_override", "1", 1, 64),
                NumberParameter("energy_pj_per_packet", "0.25", 0, 1000)
            ],
            ExternalPorts =
            [
                ExternalPort("in_packet", PortDirection.Input, "ingress", "in"),
                ExternalPort("out_packet", PortDirection.Output, "egress", "out")
            ],
            InternalBlocks = Blocks(),
            InternalConnections = Connections(),
            Views =
            [
                new TemplateView { Kind = TemplateViewKind.Symbol, Metadata = { ["glyph"] = "DLY" } },
                new TemplateView
                {
                    Kind = TemplateViewKind.Dataflow,
                    Layout =
                    {
                        ["ingress_df"] = new GridPosition(0, 0),
                        ["delay_df"] = new GridPosition(3, 0),
                        ["egress_df"] = new GridPosition(6, 0)
                    }
                },
                new TemplateView { Kind = TemplateViewKind.StructuralPort },
                new TemplateView { Kind = TemplateViewKind.ModelProfile },
                new TemplateView { Kind = TemplateViewKind.Storage },
                new TemplateView { Kind = TemplateViewKind.CompiledProfile }
            ],
            OperationContract = new TemplateOperationContract
            {
                OperationName = "packet_transform",
                InputOperands = [new TemplateOperandContract { Name = "packet", Shape = [1], DType = "int8", Layout = "packet" }],
                OutputOperands = [new TemplateOperandContract { Name = "result", Shape = [1], DType = "int8", Layout = "packet" }],
                Equation = "result = packet",
                MultiplyDType = "int8",
                AccumulateDType = "int8",
                OutputDType = "int8",
                Quantization = new TemplateQuantizationContract { Mode = "identity", Saturation = false }
            },
            TimingContract = new TemplateTimingContract
            {
                InputQueueDepth = 2,
                OutputQueueDepth = 1,
                IssueInterval = 1,
                PipelineLatency = 0,
                OperationLatency = 2,
                CanAcceptWhileBusy = true
            },
            ExecutionBinding = new ComponentTemplateExecutionBinding
            {
                KernelId = Phase7BSamplePacketDelayKernelFactory.KernelId,
                KernelVersionRequirement = "1.x",
                ContractSchemaId = Phase7BSamplePacketDelayKernelFactory.SchemaId,
                OperationKind = "packet_transform",
                ConfigurationBindings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["processing_latency_cycles"] = "resolved.processing_latency_cycles",
                    ["input_queue_depth"] = "resolved.input_queue_depth",
                    ["output_queue_depth"] = "resolved.output_queue_depth",
                    ["issue_interval_cycles"] = "resolved.issue_interval_override",
                    ["energy_pj_per_packet"] = "resolved.energy_pj_per_packet"
                }
            }
        };
        return template;
    }

    private static TemplateParameter IntegerParameter(string name, string value, int minimum, int maximum) => new()
    {
        Name = name,
        ValueKind = TemplateParameterValueKind.Integer,
        DefaultValue = value,
        Minimum = minimum,
        Maximum = maximum,
        Required = true
    };

    private static TemplateParameter NumberParameter(string name, string value, double minimum, double maximum) => new()
    {
        Name = name,
        ValueKind = TemplateParameterValueKind.Number,
        DefaultValue = value,
        Minimum = minimum,
        Maximum = maximum,
        Required = true
    };

    private static TemplateExternalPort ExternalPort(string name, PortDirection direction, string blockId, string portName) => new()
    {
        Name = name,
        Direction = direction,
        SignalType = SignalType.Digital,
        DataType = HardwareDataType.Packet,
        Precision = PrecisionKind.Any,
        Protocol = PortProtocol.Packet,
        Shape = [1],
        BandwidthBitsPerCycle = 256,
        Required = true,
        ShellBlockId = blockId,
        ShellPortName = portName
    };

    private static List<InternalBlock> Blocks() =>
    [
        DataflowBlock("ingress_df", "Ingress", "ingress"),
        DataflowBlock("delay_df", "Packet Delay", "delay"),
        DataflowBlock("egress_df", "Egress", "egress"),
        StructuralBlock("ingress", "Ingress", [Port("in", PortDirection.Input), Port("out", PortDirection.Output)]),
        StructuralBlock("delay", "Packet Delay", [Port("in", PortDirection.Input), Port("out", PortDirection.Output)]),
        StructuralBlock("egress", "Egress", [Port("in", PortDirection.Input), Port("out", PortDirection.Output)])
    ];

    private static InternalBlock DataflowBlock(string id, string name, string mappedId) => new()
    {
        Id = id,
        DisplayName = name,
        BlockKind = name.Replace(" ", "", StringComparison.Ordinal),
        Layer = InternalBlockLayer.Dataflow,
        MappedStructuralBlockIds = [mappedId],
        Ports = [Port("in", PortDirection.Input), Port("out", PortDirection.Output)]
    };

    private static InternalBlock StructuralBlock(string id, string name, List<InternalPort> ports) => new()
    {
        Id = id,
        DisplayName = name,
        BlockKind = name.Replace(" ", "", StringComparison.Ordinal),
        Layer = InternalBlockLayer.Structural,
        Ports = ports,
        TraceStage = id
    };

    private static InternalPort Port(string name, PortDirection direction) => new()
    {
        Name = name,
        Direction = direction,
        SignalType = SignalType.Digital,
        DataType = HardwareDataType.Packet,
        Precision = PrecisionKind.Any,
        Protocol = PortProtocol.Packet,
        WidthBits = 256,
        Shape = [1]
    };

    private static List<InternalConnection> Connections() =>
    [
        Connection("df_ingress_delay", "ingress_df", "out", "delay_df", "in"),
        Connection("df_delay_egress", "delay_df", "out", "egress_df", "in"),
        Connection("s_ingress_delay", "ingress", "out", "delay", "in"),
        Connection("s_delay_egress", "delay", "out", "egress", "in")
    ];

    private static InternalConnection Connection(string id, string sourceBlock, string sourcePort, string targetBlock, string targetPort) => new()
    {
        Id = id,
        SourceBlockId = sourceBlock,
        SourcePortName = sourcePort,
        TargetBlockId = targetBlock,
        TargetPortName = targetPort,
        PayloadType = "packet",
        Shape = [1],
        Precision = PrecisionKind.Any,
        RatePerCycle = 1,
        BandwidthBitsPerCycle = 256,
        BackpressureBehavior = TemplateBackpressureBehavior.Propagate
    };
}

internal sealed class Phase7BSamplePacketDelayKernelFactory : IComponentRuntimeKernelFactory, IComponentExecutionContractCompiler
{
    public const string KernelId = "phase7b.sample.packet_delay";
    public const string SchemaId = "phase7b.sample.packet_delay.config.v1";
    public static readonly Phase7BSamplePacketDelayKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = SchemaId,
        ImplementationHash = "f38f12392e2d7641043b1ea5b7e2fed6ee7d0dd9c81fbc56d8b74819dc901f1a",
        SupportedOperationKinds = ["packet_transform"]
    };

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context) => new Phase7BSamplePacketDelayKernel();

    public ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context)
    {
        var issues = new List<ComponentTemplateIssue>();
        var latency = ReadInt(context.ConfigurationValues, "processing_latency_cycles", 1, issues);
        var inputDepth = ReadInt(context.ConfigurationValues, "input_queue_depth", 1, issues);
        var outputDepth = ReadInt(context.ConfigurationValues, "output_queue_depth", 1, issues);
        var issueInterval = ReadInt(context.ConfigurationValues, "issue_interval_cycles", 1, issues);
        var energy = ReadDouble(context.ConfigurationValues, "energy_pj_per_packet", 0, issues);
        if (issues.Count > 0) return new ComponentExecutionContractCompileResult { Issues = issues.AsReadOnly() };

        var canonicalValues = new SortedDictionary<string, string>(context.ConfigurationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        var contract = new CompiledComponentExecutionContract
        {
            KernelId = context.KernelDescriptor.KernelId,
            KernelVersion = context.KernelDescriptor.KernelVersion,
            ContractSchemaId = context.KernelDescriptor.ContractSchemaId,
            OperationKind = "packet_transform",
            Ports = context.Template.ExternalPorts.OrderBy(port => port.Name, StringComparer.Ordinal).Select(port => new CompiledComponentPortContract
            {
                Name = port.Name,
                Direction = port.Direction,
                SignalType = port.SignalType,
                DataType = port.DataType,
                Precision = port.Precision,
                Protocol = port.Protocol,
                SemanticRole = port.Direction == PortDirection.Output ? "result" : "packet",
                Shape = port.Shape.ToList(),
                Bits = Math.Max(0, port.BandwidthBitsPerCycle),
                Required = port.Required,
                MultiConnect = false,
                BandwidthBitsPerCycle = port.BandwidthBitsPerCycle
            }).ToList(),
            Timing = new CompiledComponentTimingContract
            {
                OperationLatencyCycles = latency,
                PipelineLatencyCycles = 0,
                IssueIntervalCycles = issueInterval,
                FixedServiceLatencyCycles = latency,
                RuntimeDependentStallAllowed = true
            },
            Queues = new CompiledComponentQueueContract { InputDepth = inputDepth, OutputDepth = outputDepth },
            Resources =
            [
                new CompiledComponentResourceEntry { Name = "energy_pj_per_packet", ResourceKind = "dynamic_energy", Units = "pJ/packet", CanonicalValue = energy.ToString("R", CultureInfo.InvariantCulture), ValueType = "number" }
            ],
            KernelConfiguration = CanonicalComponentKernelConfiguration.Create(SchemaId, JsonSerializer.Serialize(canonicalValues, HardwareGraphJson.Options)),
            TraceDescriptors = [new ComponentTraceDescriptor("phase7b.sample.packet_delay", TraceEventType.Compute, "Packet delay kernel timeline")],
            MetricDescriptors = [new ComponentMetricDescriptor("phase7b.sample.packet_delay.energy", "pJ", EnergyCategory.NoC, "Packet delay dynamic energy")],
            Provenance = new CompiledComponentExecutionProvenance { FunctionalIdealOnly = true }
        };
        return new ComponentExecutionContractCompileResult { Contract = contract };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int minimum, List<ComponentTemplateIssue> issues)
    {
        if (values.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= minimum) return parsed;
        issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_binding.configuration_bindings." + key, $"Kernel configuration '{key}' must be an integer >= {minimum}.", KernelId));
        return minimum;
    }

    private static double ReadDouble(IReadOnlyDictionary<string, string> values, string key, double minimum, List<ComponentTemplateIssue> issues)
    {
        if (values.TryGetValue(key, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed >= minimum) return parsed;
        issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_binding.configuration_bindings." + key, $"Kernel configuration '{key}' must be a finite number >= {minimum}.", KernelId));
        return minimum;
    }
}

internal sealed class Phase7BSamplePacketDelayKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new Phase7BSamplePacketDelayState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        string.Equals(inputPortName, "in_packet", StringComparison.Ordinal) &&
        ((Phase7BSamplePacketDelayState)current).Inputs.Count < Math.Max(1, context.Contract.Queues.InputDepth);

    public ComponentRuntimeKernelInputResult SampleInput(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, IComponentRuntimeKernelState next, ComponentRuntimeKernelInput input)
    {
        var packet = input.Packet;
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next kernel states must not alias.");
        var currentState = (Phase7BSamplePacketDelayState)current;
        var nextState = (Phase7BSamplePacketDelayState)next;
        if (currentState.Inputs.Count >= Math.Max(1, context.Contract.Queues.InputDepth)) return new() { Accepted = false, StallReason = "input_queue_full" };
        nextState.Inputs.Add(PacketClone.Clone(packet));
        return new()
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute, $"phase=1;kernel={Phase7BSamplePacketDelayKernelFactory.KernelId};accept_cycle={context.Cycle}", packet.Id, packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, IComponentRuntimeKernelState next, bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next kernel states must not alias.");
        var currentState = (Phase7BSamplePacketDelayState)current;
        var nextState = (Phase7BSamplePacketDelayState)next;
        var outputs = new List<Packet>();
        var events = new List<ComponentRuntimeKernelEventFact>();
        var ready = currentState.Active.OrderBy(item => item.ReadyCycle).ThenBy(item => item.Packet.Id, StringComparer.Ordinal).FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && outputQueueAvailable)
        {
            nextState.Active.RemoveAll(item => item.ReadyCycle == ready.ReadyCycle && string.Equals(item.Packet.Id, ready.Packet.Id, StringComparison.Ordinal));
            outputs.Add(PacketClone.Clone(ready.Packet));
            events.Add(new(TraceEventType.Compute, $"phase=4;kernel={Phase7BSamplePacketDelayKernelFactory.KernelId};output_cycle={context.Cycle}", ready.Packet.Id, ready.Packet.Bits));
        }

        var energy = 0.0;
        if (currentState.Inputs.Count > 0 && context.Cycle >= currentState.NextIssueCycle)
        {
            var packet = PacketClone.Clone(currentState.Inputs[0]);
            nextState.Inputs.RemoveAt(0);
            nextState.NextIssueCycle = context.Cycle + Math.Max(1, context.Contract.Timing.IssueIntervalCycles);
            var readyCycle = context.Cycle + Math.Max(1, context.Contract.Timing.OperationLatencyCycles) - 1;
            events.Add(new(TraceEventType.Compute, $"phase=4;kernel={Phase7BSamplePacketDelayKernelFactory.KernelId};issue_cycle={context.Cycle};ready_cycle={readyCycle}", packet.Id, packet.Bits));
            if (readyCycle <= context.Cycle && outputQueueAvailable && outputs.Count == 0)
            {
                outputs.Add(packet);
                events.Add(new(TraceEventType.Compute, $"phase=4;kernel={Phase7BSamplePacketDelayKernelFactory.KernelId};output_cycle={context.Cycle}", packet.Id, packet.Bits));
            }
            else
            {
                nextState.Active.Add(new(packet, readyCycle));
            }
            energy = EnergyPerPacket(context.Contract);
        }

        return new()
        {
            Outputs = outputs.Select(packet => new ComponentRuntimeKernelOutput("out_packet", packet)).ToList(),
            Events = events,
            DynamicEnergyPicojoules = energy
        };
    }

    private static double EnergyPerPacket(CompiledComponentExecutionContract contract)
    {
        var raw = contract.Resources.FirstOrDefault(resource => resource.Name == "energy_pj_per_packet")?.CanonicalValue;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(0, parsed) : 0;
    }
}

internal sealed class Phase7BSamplePacketDelayState : IComponentRuntimeKernelState
{
    public List<Packet> Inputs { get; } = [];
    public List<Phase7BSamplePendingPacket> Active { get; } = [];
    public long NextIssueCycle { get; set; }
    public bool IsIdle => Inputs.Count == 0 && Active.Count == 0;

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase7BSamplePacketDelayState { NextIssueCycle = NextIssueCycle };
        clone.Inputs.AddRange(Inputs.Select(PacketClone.Clone));
        clone.Active.AddRange(Active.Select(item => new Phase7BSamplePendingPacket(PacketClone.Clone(item.Packet), item.ReadyCycle)));
        return clone;
    }
}

internal sealed record Phase7BSamplePendingPacket(Packet Packet, long ReadyCycle);

internal static class PacketClone
{
    public static Packet Clone(Packet packet) => new()
    {
        Id = packet.Id,
        PacketType = packet.PacketType,
        NumElements = packet.NumElements,
        BitWidth = packet.BitWidth,
        Bits = packet.Bits,
        Precision = packet.Precision,
        SourceComponentId = packet.SourceComponentId,
        DestinationComponentId = packet.DestinationComponentId,
        SourcePort = packet.SourcePort,
        DestinationPort = packet.DestinationPort,
        WorkloadOpId = packet.WorkloadOpId,
        TensorId = packet.TensorId,
        TileId = packet.TileId,
        RoutePath = packet.RoutePath.ToList(),
        DependencyIds = packet.DependencyIds.ToList(),
        Metadata = new Dictionary<string, string>(packet.Metadata, StringComparer.OrdinalIgnoreCase),
        SignalDomain = packet.SignalDomain,
        OpticalState = packet.OpticalState is null ? null : packet.OpticalState with { },
        RequestId = packet.RequestId,
        MemoryOperation = packet.MemoryOperation,
        MemoryAddress = packet.MemoryAddress,
        CurrentComponentId = packet.CurrentComponentId,
        CreatedCycle = packet.CreatedCycle,
        InjectionCycle = packet.InjectionCycle,
        DeliveredCycle = packet.DeliveredCycle,
        ArrivalCycle = packet.ArrivalCycle,
        VisitedComponents = packet.VisitedComponents.ToList(),
        Values = packet.Values.ToList()
    };
}
