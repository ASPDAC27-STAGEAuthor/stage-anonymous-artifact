using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

internal sealed class CoreDigitalVmmKernelFactory : IComponentRuntimeKernelFactory, IComponentExecutionContractCompiler
{
    public const string KernelId = "core.digital.vmm";
    public const string SchemaId = "core.digital.vmm.config.v1";
    public static readonly CoreDigitalVmmKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = KernelId,
        KernelVersion = "1.1.0",
        ContractSchemaId = SchemaId,
        ImplementationHash = ComponentExecutionJson.ComputeSha256(
            "core-digital-vmm-v1.1\nlegacy-output-compatible\nphase8a-grouped-contributor\nphase8a-explicit-stage-routing\nstrict-phase8a-route-metadata"),
        SupportedOperationKinds = ["digital_vmm"]
    };

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context) =>
        new CoreDigitalVmmKernel(CoreDigitalVmmConfiguration.FromContract(context.Contract));

    public ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context)
    {
        var issues = new List<ComponentTemplateIssue>();
        var values = context.ConfigurationValues;
        var rows = ReadInt(values, "rows", 1, issues);
        var columns = ReadInt(values, "columns", 1, issues);
        var inputDType = ReadDType(values, "input_dtype", issues);
        var weightDType = ReadDType(values, "weight_dtype", issues);
        var accumulateDType = ReadDType(values, "accumulate_dtype", issues);
        var outputDType = ReadDType(values, "output_dtype", issues);
        var macsPerCycle = ReadInt(values, "macs_per_cycle", 1, issues);
        var operationLatency = ReadInt(values, "operation_latency_cycles", 1, issues);
        var pipelineLatency = ReadInt(values, "pipeline_latency_cycles", 0, issues);
        var issueInterval = ReadInt(values, "issue_interval_cycles", 1, issues);
        var inputQueueDepth = ReadInt(values, "input_queue_depth", 1, issues);
        var outputQueueDepth = ReadInt(values, "output_queue_depth", 1, issues);
        var storageCapacityBits = ReadLong(values, "storage_capacity_bits", 1, issues);
        var weightWriteBandwidth = ReadLong(values, "weight_write_bandwidth_bits_per_cycle", 1, issues);
        var weightWriteLatency = ReadInt(values, "weight_write_latency_cycles", 0, issues);
        var operationEnergy = ReadDouble(values, "total_dynamic_energy_pj", 0, issues);
        var weightWriteEnergy = ReadDouble(values, "weight_write_energy_pj", 0, issues);
        var totalArea = ReadDouble(values, "total_area_um2", 0, issues);
        if (pipelineLatency > operationLatency)
        {
            issues.Add(Issue("pipeline_latency_cycles", "Pipeline latency cannot exceed total operation latency."));
        }

        var requiredWeightBits = checked((long)rows * columns * DigitalNumericFormats.BitWidth(weightDType));
        if (requiredWeightBits > storageCapacityBits)
        {
            issues.Add(new(
                "TemplateStorageCapacityExceeded",
                ComponentTemplateIssueSeverity.Error,
                "$.execution_binding.configuration_bindings.storage_capacity_bits",
                $"Digital VMM weights require {requiredWeightBits} bits but compiled storage capacity is {storageCapacityBits} bits.",
                context.Template.TemplateId));
        }
        if (issues.Any(issue => issue.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal))
        {
            return new ComponentExecutionContractCompileResult { Issues = issues.AsReadOnly() };
        }

        var canonicalValues = new SortedDictionary<string, string>(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        var contract = new CompiledComponentExecutionContract
        {
            KernelId = context.KernelDescriptor.KernelId,
            KernelVersion = context.KernelDescriptor.KernelVersion,
            ContractSchemaId = context.KernelDescriptor.ContractSchemaId,
            OperationKind = "digital_vmm",
            Ports = context.Template.ExternalPorts
                .OrderBy(port => port.Name, StringComparer.Ordinal)
                .Select(port => Port(port, rows, columns, inputDType, weightDType, outputDType))
                .ToList(),
            Timing = new CompiledComponentTimingContract
            {
                OperationLatencyCycles = operationLatency,
                PipelineLatencyCycles = pipelineLatency,
                IssueIntervalCycles = issueInterval,
                FixedServiceLatencyCycles = operationLatency,
                RuntimeDependentStallAllowed = true
            },
            Queues = new CompiledComponentQueueContract
            {
                InputDepth = inputQueueDepth,
                OutputDepth = outputQueueDepth
            },
            Resources =
            [
                Resource("storage_capacity_bits", "storage_capacity", "bit", storageCapacityBits),
                Resource("weight_required_bits", "storage_requirement", "bit", requiredWeightBits),
                Resource("mac_count", "compute_work", "MAC", checked((long)rows * columns)),
                Resource("macs_per_cycle", "compute_throughput", "MAC/cycle", macsPerCycle),
                Resource("weight_write_bandwidth_bits_per_cycle", "write_bandwidth", "bit/cycle", weightWriteBandwidth),
                Resource("total_dynamic_energy_pj", "dynamic_energy", "pJ/op", operationEnergy),
                Resource("weight_write_energy_pj", "write_energy", "pJ/write", weightWriteEnergy),
                Resource("total_area_um2", "area", "um2", totalArea)
            ],
            KernelConfiguration = CanonicalComponentKernelConfiguration.Create(
                SchemaId,
                JsonSerializer.Serialize(canonicalValues, HardwareGraphJson.Options)),
            TraceDescriptors =
            [
                new ComponentTraceDescriptor("pe_shell_summary", TraceEventType.Compute, "PE shell summary compatibility event"),
                new ComponentTraceDescriptor("core.digital.vmm.weight", TraceEventType.Compute, "Weight preload and commit timeline"),
                new ComponentTraceDescriptor("core.digital.vmm.issue", TraceEventType.Compute, "VMM issue timeline"),
                new ComponentTraceDescriptor("core.digital.vmm.output", TraceEventType.Compute, "VMM output-valid timeline")
            ],
            MetricDescriptors =
            [
                new ComponentMetricDescriptor("core.digital.vmm.dynamic_energy", "pJ", EnergyCategory.Compute, "Digital VMM operation and weight-write energy")
            ],
            Provenance = new CompiledComponentExecutionProvenance
            {
                ProfileSnapshotHashes = context.ProfileSnapshots.ToDictionary(snapshot => snapshot.Id, snapshot => snapshot.Hash, StringComparer.Ordinal),
                SyntheticProfileOnly = context.ProfileSnapshots.Any(snapshot => snapshot.Source.Contains("synthetic", StringComparison.OrdinalIgnoreCase)),
                FunctionalIdealOnly = true
            }
        };
        return new ComponentExecutionContractCompileResult { Contract = contract, Issues = issues.AsReadOnly() };
    }

    private static CompiledComponentPortContract Port(
        TemplateExternalPort port,
        int rows,
        int columns,
        string inputDType,
        string weightDType,
        string outputDType)
    {
        var role = port.Name switch
        {
            "in_activation" => "activation",
            "in_weight" => "weight",
            "ctrl" => "control",
            "out_result" => "result",
            _ => "unspecified"
        };
        var bits = role switch
        {
            "activation" => checked((long)rows * DigitalNumericFormats.BitWidth(inputDType)),
            "weight" => checked((long)rows * columns * DigitalNumericFormats.BitWidth(weightDType)),
            "result" => checked((long)columns * DigitalNumericFormats.BitWidth(outputDType)),
            _ => 32
        };
        return new CompiledComponentPortContract
        {
            Name = port.Name,
            Direction = port.Direction,
            SignalType = port.SignalType,
            DataType = port.DataType,
            Precision = role switch
            {
                "activation" => DigitalNumericFormats.Precision(inputDType),
                "weight" => DigitalNumericFormats.Precision(weightDType),
                "result" => DigitalNumericFormats.Precision(outputDType),
                _ => port.Precision
            },
            Protocol = port.Protocol,
            SemanticRole = role,
            Shape = role switch
            {
                "activation" => [1, rows],
                "weight" => [rows, columns],
                "result" => [1, columns],
                _ => port.Shape.ToList()
            },
            Bits = bits,
            Required = port.Required,
            MultiConnect = false,
            BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
            LatencyCycles = 0
        };
    }

    private static CompiledComponentResourceEntry Resource(string name, string kind, string units, long value) => new()
    {
        Name = name,
        ResourceKind = kind,
        Units = units,
        CanonicalValue = value.ToString(CultureInfo.InvariantCulture),
        ValueType = "integer"
    };

    private static CompiledComponentResourceEntry Resource(string name, string kind, string units, double value) => new()
    {
        Name = name,
        ResourceKind = kind,
        Units = units,
        CanonicalValue = value.ToString("R", CultureInfo.InvariantCulture),
        ValueType = "number"
    };

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int minimum, List<ComponentTemplateIssue> issues)
    {
        if (values.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= minimum) return value;
        issues.Add(Issue(key, $"Configuration '{key}' must be an integer >= {minimum}."));
        return minimum;
    }

    private static long ReadLong(IReadOnlyDictionary<string, string> values, string key, long minimum, List<ComponentTemplateIssue> issues)
    {
        if (values.TryGetValue(key, out var raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= minimum) return value;
        issues.Add(Issue(key, $"Configuration '{key}' must be an integer >= {minimum}."));
        return minimum;
    }

    private static double ReadDouble(IReadOnlyDictionary<string, string> values, string key, double minimum, List<ComponentTemplateIssue> issues)
    {
        if (values.TryGetValue(key, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= minimum) return value;
        issues.Add(Issue(key, $"Configuration '{key}' must be a finite number >= {minimum.ToString(CultureInfo.InvariantCulture)}."));
        return minimum;
    }

    private static string ReadDType(IReadOnlyDictionary<string, string> values, string key, List<ComponentTemplateIssue> issues)
    {
        try
        {
            return DigitalNumericFormats.NormalizeDType(values.GetValueOrDefault(key, ""));
        }
        catch (ArgumentOutOfRangeException)
        {
            issues.Add(Issue(key, $"Configuration '{key}' must be fp8, fp16, or fp32."));
            return "fp8";
        }
    }

    private static ComponentTemplateIssue Issue(string key, string message) => new(
        ComponentExecutionIssueCodes.KernelConfigurationInvalid,
        ComponentTemplateIssueSeverity.Error,
        "$.execution_binding.configuration_bindings." + key,
        message,
        KernelId);
}

internal sealed class CoreDigitalVmmConfiguration
{
    public int Rows { get; init; }
    public int Columns { get; init; }
    public string InputDType { get; init; } = "fp8";
    public string WeightDType { get; init; } = "fp8";
    public string AccumulateDType { get; init; } = "fp16";
    public string OutputDType { get; init; } = "fp8";
    public int OperationLatencyCycles { get; init; }
    public int IssueIntervalCycles { get; init; }
    public int InputQueueDepth { get; init; }
    public long StorageCapacityBits { get; init; }
    public long WeightWriteBandwidthBitsPerCycle { get; init; }
    public int WeightWriteLatencyCycles { get; init; }
    public double OperationEnergyPicojoules { get; init; }
    public double WeightWriteEnergyPicojoules { get; init; }

    public int WeightValueCount => checked(Rows * Columns);
    public int OutputBits => checked(Columns * DigitalNumericFormats.BitWidth(OutputDType));

    public static CoreDigitalVmmConfiguration FromContract(CompiledComponentExecutionContract contract)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(contract.KernelConfiguration.CanonicalJson, HardwareGraphJson.Options)
            ?? throw new InvalidOperationException("Digital VMM kernel configuration is missing.");
        return new CoreDigitalVmmConfiguration
        {
            Rows = Int(values, "rows"),
            Columns = Int(values, "columns"),
            InputDType = DigitalNumericFormats.NormalizeDType(values["input_dtype"]),
            WeightDType = DigitalNumericFormats.NormalizeDType(values["weight_dtype"]),
            AccumulateDType = DigitalNumericFormats.NormalizeDType(values["accumulate_dtype"]),
            OutputDType = DigitalNumericFormats.NormalizeDType(values["output_dtype"]),
            OperationLatencyCycles = Int(values, "operation_latency_cycles"),
            IssueIntervalCycles = Int(values, "issue_interval_cycles"),
            InputQueueDepth = Int(values, "input_queue_depth"),
            StorageCapacityBits = Long(values, "storage_capacity_bits"),
            WeightWriteBandwidthBitsPerCycle = Long(values, "weight_write_bandwidth_bits_per_cycle"),
            WeightWriteLatencyCycles = Int(values, "weight_write_latency_cycles"),
            OperationEnergyPicojoules = Double(values, "total_dynamic_energy_pj"),
            WeightWriteEnergyPicojoules = Double(values, "weight_write_energy_pj")
        };
    }

    public int WeightWriteServiceCycles(int bits)
    {
        var serialization = (long)Math.Ceiling(Math.Max(1, bits) / (double)Math.Max(1, WeightWriteBandwidthBitsPerCycle));
        return (int)Math.Max(1, Math.Min(int.MaxValue, serialization + Math.Max(0, WeightWriteLatencyCycles) - 1L));
    }

    private static int Int(IReadOnlyDictionary<string, string> values, string key) => int.Parse(values[key], NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static long Long(IReadOnlyDictionary<string, string> values, string key) => long.Parse(values[key], NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static double Double(IReadOnlyDictionary<string, string> values, string key) => double.Parse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture);
}

internal sealed class CoreDigitalVmmKernel : IPhaseSafeComponentRuntimeKernel
{
    private readonly CoreDigitalVmmConfiguration configuration;

    public CoreDigitalVmmKernel(CoreDigitalVmmConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new CoreDigitalVmmState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName)
    {
        var state = (CoreDigitalVmmState)current;
        return inputPortName switch
        {
            "in_activation" => state.Activations.Count < Math.Max(1, configuration.InputQueueDepth),
            "in_weight" => state.PendingWeightWrite is null &&
                state.ActiveResults.Count == 0 &&
                (state.CommittedWeights is null || state.Activations.Count == 0),
            "ctrl" => true,
            _ => false
        };
    }

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next digital VMM states must not alias.");
        var nextState = (CoreDigitalVmmState)next;
        var packet = input.Packet;
        if (input.InputPortName == "ctrl")
        {
            return new ComponentRuntimeKernelInputResult
            {
                Accepted = true,
                Events = [new(TraceEventType.Compute, $"phase=1;kernel={CoreDigitalVmmKernelFactory.KernelId};control_accept_cycle={context.Cycle}", packet.Id, packet.Bits)]
            };
        }

        var expectedCount = input.InputPortName == "in_weight" ? configuration.WeightValueCount : configuration.Rows;
        var shapeIssues = ValidatePacket(packet, expectedCount, input.InputPortName);
        if (shapeIssues.Count > 0)
        {
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "invalid_input", Issues = shapeIssues };
        }

        if (input.InputPortName == "in_weight")
        {
            if (packet.Bits > configuration.StorageCapacityBits)
            {
                return new ComponentRuntimeKernelInputResult
                {
                    Accepted = false,
                    StallReason = "weight_capacity_exceeded",
                    Issues = [new("TemplateStorageCapacityExceeded", "error", $"Weight packet requires {packet.Bits} bits but capacity is {configuration.StorageCapacityBits} bits.", packet.Id)]
                };
            }
            if (nextState.PendingWeightWrite is not null ||
                nextState.ActiveResults.Count > 0 ||
                (nextState.CommittedWeights is not null && nextState.Activations.Count > 0))
            {
                return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "weight_bank_busy" };
            }
            var readyCycle = context.Cycle + configuration.WeightWriteServiceCycles(packet.Bits);
            nextState.PendingWeightWrite = new CoreDigitalVmmPendingWeight(PacketClone.Clone(packet), readyCycle);
            return new ComponentRuntimeKernelInputResult
            {
                Accepted = true,
                Events = [new(TraceEventType.Compute, $"phase=1;kernel={CoreDigitalVmmKernelFactory.KernelId};weight_accept_cycle={context.Cycle};weight_ready_cycle={readyCycle}", packet.Id, packet.Bits)]
            };
        }

        if (input.InputPortName == "in_activation")
        {
            if (nextState.Activations.Count >= Math.Max(1, configuration.InputQueueDepth))
            {
                return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "activation_queue_full" };
            }
            nextState.Activations.Add(PacketClone.Clone(packet));
            return new ComponentRuntimeKernelInputResult
            {
                Accepted = true,
                Events = [new(TraceEventType.Compute, $"phase=1;kernel={CoreDigitalVmmKernelFactory.KernelId};activation_accept_cycle={context.Cycle}", packet.Id, packet.Bits)]
            };
        }

        return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next digital VMM states must not alias.");
        var currentState = (CoreDigitalVmmState)current;
        var nextState = (CoreDigitalVmmState)next;
        var outputs = new List<ComponentRuntimeKernelOutput>();
        var events = new List<ComponentRuntimeKernelEventFact>();
        var energy = 0.0;

        if (currentState.PendingWeightWrite is not null && currentState.PendingWeightWrite.ReadyCycle <= context.Cycle)
        {
            var weightPacket = currentState.PendingWeightWrite.Packet;
            nextState.PendingWeightWrite = null;
            nextState.CommittedWeights = weightPacket.Values.Select(value => DigitalNumericFormats.Quantize(value, configuration.WeightDType).Value).ToArray();
            nextState.WeightPacketId = weightPacket.Id;
            nextState.WeightVersion = currentState.WeightVersion + 1;
            energy += configuration.WeightWriteEnergyPicojoules;
            events.Add(new(TraceEventType.Compute, $"phase=4;kernel={CoreDigitalVmmKernelFactory.KernelId};weight_commit_cycle={context.Cycle};weight_version={nextState.WeightVersion}", weightPacket.Id, weightPacket.Bits));
            events.Add(new(TraceEventType.Compute, "component_template_drilldown;stage=weight_read", weightPacket.Id, weightPacket.Bits));
        }

        var ready = currentState.ActiveResults
            .OrderBy(item => item.ReadyCycle)
            .ThenBy(item => item.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && outputQueueAvailable)
        {
            nextState.ActiveResults.RemoveAll(item => item.ReadyCycle == ready.ReadyCycle && string.Equals(item.Packet.Id, ready.Packet.Id, StringComparison.Ordinal));
            outputs.Add(new ComponentRuntimeKernelOutput("out_result", PacketClone.Clone(ready.Packet)));
            events.Add(new(TraceEventType.Compute, $"phase=4;kernel={CoreDigitalVmmKernelFactory.KernelId};output_cycle={context.Cycle};weight_version={ready.WeightVersion}", ready.Packet.Id, ready.Packet.Bits));
        }

        if (currentState.Activations.Count > 0 && currentState.CommittedWeights is not null && context.Cycle >= currentState.NextIssueCycle)
        {
            var activation = currentState.Activations[0];
            nextState.Activations.RemoveAll(packet => string.Equals(packet.Id, activation.Id, StringComparison.Ordinal));
            nextState.NextIssueCycle = context.Cycle + Math.Max(1, configuration.IssueIntervalCycles);
            var resultPacket = Execute(context.Component.Id, activation, currentState.CommittedWeights, currentState.WeightPacketId, currentState.WeightVersion);
            var readyCycle = context.Cycle + Math.Max(1, configuration.OperationLatencyCycles) - 1;
            events.Add(new(TraceEventType.Compute, $"phase=4;kernel={CoreDigitalVmmKernelFactory.KernelId};issue_cycle={context.Cycle};ready_cycle={readyCycle};weight_version={currentState.WeightVersion}", activation.Id, activation.Bits));
            events.Add(new(TraceEventType.Compute, "component_template_runtime;shell_summary=pe_shell_summary;kernel=core.digital.vmm", activation.Id, activation.Bits));
            events.Add(new(TraceEventType.Compute, "component_template_drilldown;stage=compute_issue", activation.Id, activation.Bits));
            if (readyCycle <= context.Cycle && outputQueueAvailable && outputs.Count == 0)
            {
                outputs.Add(new ComponentRuntimeKernelOutput("out_result", resultPacket));
                events.Add(new(TraceEventType.Compute, $"phase=4;kernel={CoreDigitalVmmKernelFactory.KernelId};output_cycle={context.Cycle};weight_version={currentState.WeightVersion}", resultPacket.Id, resultPacket.Bits));
            }
            else
            {
                nextState.ActiveResults.Add(new CoreDigitalVmmPendingResult(resultPacket, readyCycle, currentState.WeightVersion));
            }
            energy += configuration.OperationEnergyPicojoules;
        }

        return new ComponentRuntimeKernelAdvanceResult
        {
            Outputs = outputs,
            Events = events,
            DynamicEnergyPicojoules = energy
        };
    }

    private Packet Execute(string componentId, Packet activationPacket, IReadOnlyList<double> committedWeights, string weightPacketId, int weightVersion)
    {
        var activation = activationPacket.Values.Select(value => DigitalNumericFormats.Quantize(value, configuration.InputDType).Value).ToArray();
        var output = new double[configuration.Columns];
        for (var column = 0; column < configuration.Columns; column++)
        {
            var accumulator = DigitalNumericFormats.Quantize(0, configuration.AccumulateDType).Value;
            for (var row = 0; row < configuration.Rows; row++)
            {
                accumulator = DigitalNumericFormats.Quantize(
                    accumulator + activation[row] * committedWeights[row * configuration.Columns + column],
                    configuration.AccumulateDType).Value;
            }
            output[column] = DigitalNumericFormats.Quantize(accumulator, configuration.OutputDType).Value;
        }

        var result = PacketClone.Clone(activationPacket);
        result.Id = activationPacket.Id + ":vmm";
        result.PacketType = PacketType.PartialSum;
        result.NumElements = configuration.Columns;
        result.BitWidth = DigitalNumericFormats.BitWidth(configuration.OutputDType);
        result.Bits = configuration.OutputBits;
        result.Precision = DigitalNumericFormats.Precision(configuration.OutputDType);
        result.Values = output.ToList();
        result.Metadata["kernel_id"] = CoreDigitalVmmKernelFactory.KernelId;
        result.Metadata["weight_packet_id"] = weightPacketId;
        result.Metadata["weight_version"] = weightVersion.ToString(CultureInfo.InvariantCulture);
        result.Metadata["output_dtype"] = configuration.OutputDType;
        if (string.Equals(
                activationPacket.Metadata.GetValueOrDefault(Phase8ACollectiveRuntimeMetadata.OperationKind, ""),
                Phase8AGroupedVectorSumContract.SumOperation,
                StringComparison.Ordinal))
            result.Metadata[Phase8ACollectiveRuntimeMetadata.ContributorId] = componentId;
        if (activationPacket.Metadata.ContainsKey(Phase8AStageRouteMetadata.RemainingRoutes) ||
            activationPacket.Metadata.ContainsKey(Phase8ACollectiveRuntimeMetadata.OutputRouteLinkIds))
            Phase8ACollectiveMetadataCodec.ApplyOutputRoute(activationPacket, result);
        return result;
    }

    private static IReadOnlyList<ComponentRuntimeKernelIssueFact> ValidatePacket(Packet packet, int expectedValues, string inputPortName)
    {
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        if (packet.Values.Count != expectedValues)
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.KernelInputShapeMismatch,
                "error",
                $"Input port '{inputPortName}' expects {expectedValues} values but packet '{packet.Id}' carries {packet.Values.Count}.",
                packet.Id));
        }
        if (packet.Values.Any(value => !double.IsFinite(value)))
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.PeNumericInputInvalid,
                "error",
                $"Input packet '{packet.Id}' contains NaN or infinity, which is not valid for deterministic digital VMM.",
                packet.Id));
        }
        if (string.Equals(inputPortName, "in_activation", StringComparison.Ordinal) &&
            !Phase8AStageRouteMetadata.TryValidateBoundMetadata(packet, out var stageRouteReason))
        {
            issues.Add(new(
                "DigitalVmmPhase8AStageRouteInvalid",
                "error",
                $"Input packet '{packet.Id}' has invalid Phase 8A stage routing metadata: {stageRouteReason}.",
                packet.Id));
        }
        if (string.Equals(inputPortName, "in_activation", StringComparison.Ordinal) &&
            !Phase8ACollectiveMetadataCodec.TryReadOutputRoute(packet, out _, out _, out _, out var outputRouteReason))
        {
            issues.Add(new(
                "DigitalVmmPhase8AOutputRouteInvalid",
                "error",
                $"Input packet '{packet.Id}' has invalid Phase 8A output routing metadata: {outputRouteReason}.",
                packet.Id));
        }
        return issues.AsReadOnly();
    }
}

internal sealed class CoreDigitalVmmState : IComponentRuntimeKernelState
{
    public List<Packet> Activations { get; } = [];
    public CoreDigitalVmmPendingWeight? PendingWeightWrite { get; set; }
    public double[]? CommittedWeights { get; set; }
    public string WeightPacketId { get; set; } = "";
    public int WeightVersion { get; set; }
    public List<CoreDigitalVmmPendingResult> ActiveResults { get; } = [];
    public long NextIssueCycle { get; set; }
    public bool IsIdle => Activations.Count == 0 && PendingWeightWrite is null && ActiveResults.Count == 0;

    public override string ToString() => $"activations={Activations.Count},pending_weight_ready={PendingWeightWrite?.ReadyCycle.ToString(CultureInfo.InvariantCulture) ?? "none"},committed_weight_values={CommittedWeights?.Length ?? 0},active_result_ready=[{string.Join(",", ActiveResults.OrderBy(item => item.ReadyCycle).Select(item => item.ReadyCycle.ToString(CultureInfo.InvariantCulture)))}],next_issue={NextIssueCycle}";

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new CoreDigitalVmmState
        {
            PendingWeightWrite = PendingWeightWrite is null ? null : new CoreDigitalVmmPendingWeight(PacketClone.Clone(PendingWeightWrite.Packet), PendingWeightWrite.ReadyCycle),
            CommittedWeights = CommittedWeights?.ToArray(),
            WeightPacketId = WeightPacketId,
            WeightVersion = WeightVersion,
            NextIssueCycle = NextIssueCycle
        };
        clone.Activations.AddRange(Activations.Select(PacketClone.Clone));
        clone.ActiveResults.AddRange(ActiveResults.Select(item => new CoreDigitalVmmPendingResult(PacketClone.Clone(item.Packet), item.ReadyCycle, item.WeightVersion)));
        return clone;
    }
}

internal sealed record CoreDigitalVmmPendingWeight(Packet Packet, long ReadyCycle);
internal sealed record CoreDigitalVmmPendingResult(Packet Packet, long ReadyCycle, int WeightVersion);

internal sealed class CoreDigitalVmmScenarioProvider : IComponentKernelTestScenarioProvider
{
    public static readonly CoreDigitalVmmScenarioProvider Instance = new();

    public ComponentKernelTestScenarioProviderDescriptor Descriptor { get; } = new()
    {
        KernelId = CoreDigitalVmmKernelFactory.KernelId,
        KernelVersion = "1.1.0",
        ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId,
        ProviderVersion = "1.0.0"
    };

    public ComponentKernelTestScenario CreateScenario(CompiledComponentExecutionContract contract, int seed)
    {
        var configuration = CoreDigitalVmmConfiguration.FromContract(contract);
        var generator = new CoreDigitalVmmDeterministicGenerator(unchecked((ulong)(uint)seed) + 0x9e3779b97f4a7c15UL);
        var weights = ConformanceValues(generator, seed, configuration.WeightValueCount, isWeight: true);
        var activation = ConformanceValues(generator, seed, configuration.Rows, isWeight: false);
        var weightPacket = new Packet
        {
            Id = $"vmm-weight-{seed.ToString(CultureInfo.InvariantCulture)}",
            PacketType = PacketType.Weight,
            NumElements = configuration.WeightValueCount,
            BitWidth = DigitalNumericFormats.BitWidth(configuration.WeightDType),
            Bits = checked(configuration.WeightValueCount * DigitalNumericFormats.BitWidth(configuration.WeightDType)),
            Precision = DigitalNumericFormats.Precision(configuration.WeightDType),
            Values = weights
        };
        var controlPacket = new Packet
        {
            Id = $"vmm-control-{seed.ToString(CultureInfo.InvariantCulture)}",
            PacketType = PacketType.Control,
            NumElements = 1,
            BitWidth = 32,
            Bits = 32,
            Precision = PrecisionKind.Any,
            Values = [1]
        };
        var activationPacket = new Packet
        {
            Id = $"vmm-activation-{seed.ToString(CultureInfo.InvariantCulture)}",
            PacketType = PacketType.Activation,
            NumElements = configuration.Rows,
            BitWidth = DigitalNumericFormats.BitWidth(configuration.InputDType),
            Bits = checked(configuration.Rows * DigitalNumericFormats.BitWidth(configuration.InputDType)),
            Precision = DigitalNumericFormats.Precision(configuration.InputDType),
            Values = activation
        };
        var reference = DigitalVmmReferenceEvaluator.Evaluate(
            activation,
            weights,
            configuration.Rows,
            configuration.Columns,
            configuration.InputDType,
            configuration.WeightDType,
            configuration.AccumulateDType,
            configuration.OutputDType);
        var writeCycles = configuration.WeightWriteServiceCycles(weightPacket.Bits);
        return new ComponentKernelTestScenario
        {
            ScenarioId = "core.digital.vmm.seeded",
            Seed = seed,
            MaxCycles = Math.Max(64, writeCycles + configuration.OperationLatencyCycles + 32),
            CanonicalInputJson = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
            {
                Activation = activation,
                Weights = weights,
                configuration.Rows,
                configuration.Columns
            }, HardwareGraphJson.Options)),
            CanonicalExpectationJson = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
            {
                Reference = reference,
                DType = configuration.OutputDType,
                Compare = "exact_encoded_bits"
            }, HardwareGraphJson.Options)),
            Inputs =
            [
                new ComponentKernelTestInputTransaction { TransactionId = "weight", InputPortName = "in_weight", InjectionCycle = 0, Packet = weightPacket },
                new ComponentKernelTestInputTransaction { TransactionId = "control", InputPortName = "ctrl", InjectionCycle = 0, Packet = controlPacket },
                new ComponentKernelTestInputTransaction { TransactionId = "activation", InputPortName = "in_activation", InjectionCycle = 0, Packet = activationPacket }
            ],
            OutputPortNames = ["out_result"]
        };
    }

    private static List<double> ConformanceValues(
        CoreDigitalVmmDeterministicGenerator generator,
        int seed,
        int count,
        bool isWeight)
    {
        var caseIndex = seed - 7400;
        if (caseIndex is < 0 or > 11)
        {
            return Enumerable.Range(0, count).Select(_ => generator.NextSigned()).ToList();
        }

        var values = new List<double>(count);
        for (var index = 0; index < count; index++)
        {
            var selector = (index + (isWeight ? 3 : 0)) % 8;
            var sign = selector % 2 == 0 ? 1d : -1d;
            values.Add(caseIndex switch
            {
                0 => generator.NextSigned() * 0.125,
                1 => generator.NextSigned() * 1.5,
                2 => sign * Math.Pow(2, -25 + selector % 4),
                3 => sign * Math.Pow(2, -10 + selector % 4),
                4 => sign * (1d + Math.Pow(2, -11)),
                5 => sign * (1d + Math.Pow(2, -4)),
                6 => sign * (240d + selector * 32d),
                7 => sign * 1e40,
                8 => selector == 0 ? -0.0 : sign * selector / 16d,
                9 => sign * (0.5d + selector * 0.25d),
                10 => sign * (15.5d + selector * 0.5d),
                _ => generator.NextSigned() * 32d
            });
        }
        return values;
    }

    public ComponentKernelTestEvaluationResult EvaluateScenario(ComponentKernelTestScenario scenario, ComponentKernelTestObservation observation)
    {
        var configuration = CoreDigitalVmmConfiguration.FromContract(observation.Profile.ExecutionContract!);
        var weight = scenario.Inputs.Single(input => input.InputPortName == "in_weight").Packet;
        var activation = scenario.Inputs.Single(input => input.InputPortName == "in_activation").Packet;
        var expected = DigitalVmmReferenceEvaluator.Evaluate(
            activation.Values,
            weight.Values,
            configuration.Rows,
            configuration.Columns,
            configuration.InputDType,
            configuration.WeightDType,
            configuration.AccumulateDType,
            configuration.OutputDType);
        var expectedHash = DigitalNumericFormats.HashEncodedValues("result", configuration.OutputDType, 1, configuration.Columns, expected);
        var actualPacket = observation.Simulation.DeliveredPackets.SingleOrDefault();
        var actualHash = actualPacket is null
            ? "missing"
            : DigitalNumericFormats.HashEncodedValues("result", configuration.OutputDType, 1, configuration.Columns, actualPacket.Values);
        var issues = new List<ComponentTemplateIssue>();
        if (actualPacket is null || actualPacket.Values.Count != configuration.Columns || !string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.KernelTestOutputMismatch,
                ComponentTemplateIssueSeverity.Error,
                "$.simulation.delivered_packets",
                "Digital VMM runtime output does not match the independent exact encoded-bit reference.",
                CoreDigitalVmmKernelFactory.KernelId));
        }

        var commit = SingleTimeline(observation, "weight_commit_cycle=", issues);
        var issue = SingleTimeline(observation, "issue_cycle=", issues);
        var output = SingleTimeline(observation, "output_cycle=", issues);
        if (commit is not null && issue is not null && issue.Cycle <= commit.Cycle)
        {
            issues.Add(TimingIssue("Activation issued before the committed weight bank became visible."));
        }
        if (issue is not null && output is not null)
        {
            var expectedOutputCycle = issue.Cycle + Math.Max(1, configuration.OperationLatencyCycles) - 1;
            if (output.Cycle != expectedOutputCycle)
            {
                issues.Add(TimingIssue($"Output cycle {output.Cycle} does not match issue+latency-1 expectation {expectedOutputCycle}."));
            }
        }

        var artifacts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["input_vector_hash"] = DigitalNumericFormats.HashEncodedValues("activation", configuration.InputDType, 1, configuration.Rows, activation.Values),
            ["weight_matrix_hash"] = DigitalNumericFormats.HashEncodedValues("weight", configuration.WeightDType, configuration.Rows, configuration.Columns, weight.Values),
            ["reference_output_hash"] = expectedHash,
            ["actual_output_hash"] = actualHash,
            ["comparison"] = "exact_encoded_bits",
            ["tolerance_abs"] = "0",
            ["tolerance_rel"] = "0"
        };
        if (commit is not null) artifacts["weight_commit_cycle"] = commit.Cycle.ToString(CultureInfo.InvariantCulture);
        if (issue is not null) artifacts["issue_cycle"] = issue.Cycle.ToString(CultureInfo.InvariantCulture);
        if (output is not null) artifacts["output_cycle"] = output.Cycle.ToString(CultureInfo.InvariantCulture);
        return new ComponentKernelTestEvaluationResult
        {
            Issues = issues.AsReadOnly(),
            ExpectedOutputHash = expectedHash,
            ActualOutputHash = actualHash,
            Artifacts = new ReadOnlyDictionary<string, string>(artifacts)
        };
    }

    private static ComponentKernelTestTimelineEvent? SingleTimeline(
        ComponentKernelTestObservation observation,
        string marker,
        List<ComponentTemplateIssue> issues)
    {
        var matches = observation.ComponentEvents.Where(item => item.Event.Detail?.Contains(marker, StringComparison.Ordinal) == true).ToList();
        if (matches.Count == 1) return matches[0];
        issues.Add(TimingIssue($"Expected one '{marker}' event but observed {matches.Count}."));
        return null;
    }

    private static ComponentTemplateIssue TimingIssue(string message) => new(
        ComponentExecutionIssueCodes.KernelTestTimingMismatch,
        ComponentTemplateIssueSeverity.Error,
        "$.simulation.timeline",
        message,
        CoreDigitalVmmKernelFactory.KernelId);
}

internal sealed class CoreDigitalVmmDeterministicGenerator
{
    private ulong state;

    public CoreDigitalVmmDeterministicGenerator(ulong seed)
    {
        state = seed;
    }

    public double NextSigned()
    {
        state += 0x9e3779b97f4a7c15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        value ^= value >> 31;
        var unit = (value >> 11) * (1.0 / 9007199254740992.0);
        return (unit * 2.0 - 1.0) * 1.5;
    }
}
