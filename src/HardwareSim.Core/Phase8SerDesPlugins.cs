using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Stable first-party electrical interface plugin identities introduced with Phase 8.</summary>
public static class Phase8SerDesTypeIds
{
    /// <summary>Gets the stable Serializer plugin type id.</summary>
    public const string Serializer = "com.hardware-sim.first-party.interface.serializer";
    /// <summary>Gets the stable Deserializer/CDR plugin type id.</summary>
    public const string Deserializer = "com.hardware-sim.first-party.interface.deserializer";
    /// <summary>Gets both stable interface plugin type ids.</summary>
    public static IReadOnlyList<string> All { get; } = [Serializer, Deserializer];
}

/// <summary>Stable Serializer/Deserializer parameter and packet metadata keys.</summary>
public static class Phase8SerDesKeys
{
    /// <summary>Gets the stable parallel word width key.</summary>
    public const string ParallelWidthBits = "parallel_width_bits";
    /// <summary>Gets the stable serial lane count key.</summary>
    public const string LaneCount = "lane_count";
    /// <summary>Gets the stable per-lane transport rate key.</summary>
    public const string LaneRateBitsPerCycle = "lane_rate_bits_per_cycle";
    /// <summary>Gets the stable line-coding scheme key.</summary>
    public const string Encoding = "encoding";
    /// <summary>Gets the stable gearbox latency key.</summary>
    public const string GearboxLatencyCycles = "gearbox_latency_cycles";
    /// <summary>Gets the stable clock-data-recovery latency key.</summary>
    public const string CdrLatencyCycles = "cdr_latency_cycles";
    /// <summary>Gets the stable input queue depth key.</summary>
    public const string QueueDepth = "queue_depth";
    /// <summary>Gets the stable per-bit conversion energy key.</summary>
    public const string EnergyPicojoulesPerBit = "energy_pj_per_bit";
    /// <summary>Gets the stable serialization timing owner key.</summary>
    public const string SerializationOwner = "serialization_owner";
    /// <summary>Gets the stable original logical packet bits key.</summary>
    public const string OriginalBits = "serdes_original_bits";
    /// <summary>Gets the stable encoded line bits key.</summary>
    public const string EncodedBits = "serdes_encoded_bits";
    /// <summary>Gets the stable line-coding overhead bits key.</summary>
    public const string CodingOverheadBits = "serdes_coding_overhead_bits";
}

/// <summary>First-party Serializer and Deserializer/CDR plugin package.</summary>
public static class FirstPartySerDesComponentPlugins
{
    /// <summary>Registers both first-party interface plugins.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        foreach (var plugin in Descriptors()) FirstPartyComponentPlugins.RegisterOrThrow(registry, plugin);
    }

    /// <summary>Builds deterministic Serializer and Deserializer/CDR descriptors.</summary>
    public static IReadOnlyList<ComponentPluginDescriptor> Descriptors() =>
    [
        Create(
            Phase8SerDesTypeIds.Serializer,
            "Serializer",
            "serializer",
            "#FF6B9A",
            "SER",
            "Parallel packet to encoded serial lanes",
            320,
            "parallel_in",
            "serial_out"),
        Create(
            Phase8SerDesTypeIds.Deserializer,
            "Deserializer / CDR",
            "deserializer",
            "#60A5FA",
            "DES",
            "Clock recovery and serial lanes to parallel packet",
            321,
            "serial_in",
            "parallel_out")
    ];

    private static ComponentPluginDescriptor Create(
        string typeId,
        string displayName,
        string glyph,
        string color,
        string abbreviation,
        string summary,
        int order,
        string inputName,
        string outputName)
    {
        var ports = new List<ComponentPortSchema>
        {
            new(
                inputName,
                PortDirection.Input,
                SignalType.Digital,
                HardwareDataType.Packet,
                PrecisionKind.Any,
                PortProtocol.Packet,
                ComponentDefaults.LinkBandwidthBitsPerCycle,
                0,
                true,
                false,
                inputName == "parallel_in" ? "parallel_packet" : "encoded_serial_bitstream",
                "bit|lane"),
            new(
                outputName,
                PortDirection.Output,
                SignalType.Digital,
                HardwareDataType.Packet,
                PrecisionKind.Any,
                PortProtocol.Packet,
                ComponentDefaults.LinkBandwidthBitsPerCycle,
                0,
                true,
                false,
                outputName == "serial_out" ? "encoded_serial_bitstream" : "parallel_packet",
                "bit|lane")
        };
        var parameters = Parameters();
        var traces = new List<ComponentTraceDescriptor>
        {
            new("phase8.interface." + abbreviation.ToLowerInvariant() + ".runtime", TraceEventType.Compute,
                "Exact current-next-commit Serializer/Deserializer runtime")
        };
        var metrics = new List<ComponentMetricDescriptor>
        {
            new("serdes_dynamic_energy_pJ", "pJ", EnergyCategory.Conversion, "Serializer/Deserializer conversion energy"),
            new("serdes_coding_overhead_bits", "bit", EnergyCategory.Conversion, "Encoding overhead"),
            new("serdes_lane_utilization", "ratio", EnergyCategory.Conversion, "Encoded bits relative to lane capacity")
        };
        var kernel = Phase8SerDesRuntimeKernelFactory.For(typeId);
        return new ComponentPluginDescriptor(
            typeId,
            displayName,
            "Interface",
            "8.1.0",
            ports,
            parameters,
            Phase8SerDesValidationProvider.Instance,
            Phase8SerDesCompileProvider.Instance,
            Phase8SerDesSimulationRuntimeFactory.Instance,
            traces,
            metrics,
            PrimitiveDescriptor: new ComponentTemplatePrimitiveDescriptor(
                typeId + ".primitive",
                displayName + " Primitive",
                "Interface",
                ports,
                parameters),
            CompiledProfileFactoryDescriptor: new CompiledProfileFactoryDescriptor(
                typeId + ".profile-factory",
                "compiled-profile",
                "8.1.0",
                "Produces a Phase 7C-compatible compiled interface profile."),
            UnityPresentationDescriptor: new UnityPresentationDescriptor(glyph, color, abbreviation, summary, order),
            SourceKind: ComponentPluginSourceKind.FirstParty,
            LegacyKind: null,
            ShowInPalette: true,
            RuntimeKernelFactory: kernel);
    }

    private static IReadOnlyList<ComponentParameterSchema> Parameters() =>
    [
        new(Phase8SerDesKeys.ParallelWidthBits, "64", "bit", 1, 1_048_576, false,
            "Logical parallel word width.", IntegerOnly: true),
        new(Phase8SerDesKeys.LaneCount, "4", "lane", 1, 1024, false,
            "Number of physical serial lanes.", IntegerOnly: true),
        new(Phase8SerDesKeys.LaneRateBitsPerCycle, "32", "bit/cycle/lane", 1, 1_048_576, false,
            "Transport capacity of each serial lane.", IntegerOnly: true),
        new(Phase8SerDesKeys.Encoding, "64b66b", "scheme", Description: "Line coding.",
            AllowedValues: ["raw", "64b66b"]),
        new(Phase8SerDesKeys.GearboxLatencyCycles, "1", "cycles", 0, 1_000_000, false,
            "Internal gearbox latency; link transfer time is not included.", IntegerOnly: true),
        new(Phase8SerDesKeys.CdrLatencyCycles, "2", "cycles", 0, 1_000_000, false,
            "Deserializer clock-data-recovery latency; ignored by Serializer.", IntegerOnly: true),
        new(Phase8SerDesKeys.QueueDepth, "4", "packets", 1, 65_536, false,
            "Exact input queue capacity.", IntegerOnly: true),
        new(Phase8SerDesKeys.EnergyPicojoulesPerBit, "0.01", "pJ/bit", 0, 1_000_000, false,
            "Synthetic functional interface conversion energy."),
        new(Phase8SerDesKeys.SerializationOwner, "link", "owner", Description:
            "SerDes owns coding and fixed gearbox/CDR delay; the adjacent link owns encoded-bit transfer time.",
            AllowedValues: ["link"])
    ];
}

internal sealed class Phase8SerDesValidationProvider : IComponentValidationProvider
{
    public static readonly Phase8SerDesValidationProvider Instance = new();

    public IReadOnlyList<ComponentPluginIssue> Validate(ComponentValidationContext context)
    {
        var issues = new List<ComponentPluginIssue>();
        if (!Phase8SerDesTypeIds.All.Contains(context.Plugin.TypeId, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new("SerDesTypeIdUnsupported", ValidationSeverity.Error, "$.type_id",
                "Unsupported Serializer/Deserializer stable type id.", context.Component.Id));
            return issues;
        }

        if (!string.Equals(ComponentTypeIds.Normalize(context.Component.TypeId), context.Plugin.TypeId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("SerDesTypeIdMismatch", ValidationSeverity.Error, "$.type_id",
                "Component type id must equal the registered interface plugin type id.", context.Component.Id));
        }

        foreach (var schema in context.Plugin.Parameters)
        {
            var raw = context.Component.Parameters.GetValueOrDefault(schema.Name, schema.DefaultValue);
            if (schema.AllowedValues is { Count: > 0 } &&
                !schema.AllowedValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new("SerDesParameterValueInvalid", ValidationSeverity.Error,
                    "$.parameters." + schema.Name, "Unsupported value '" + raw + "'.", context.Component.Id));
                continue;
            }

            if (!schema.Minimum.HasValue && !schema.Maximum.HasValue && !schema.IntegerOnly) continue;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) ||
                schema.IntegerOnly && number != Math.Truncate(number) ||
                schema.Minimum.HasValue && number < schema.Minimum.Value ||
                schema.Maximum.HasValue && number > schema.Maximum.Value)
            {
                issues.Add(new("SerDesParameterInvalid", ValidationSeverity.Error,
                    "$.parameters." + schema.Name, "Parameter '" + schema.Name + "' is outside its typed range.", context.Component.Id));
            }
        }

        return issues;
    }
}

internal sealed class Phase8SerDesCompileProvider : IComponentCompileProvider
{
    public static readonly Phase8SerDesCompileProvider Instance = new();

    public ComponentCompileProviderResult Compile(ComponentCompileContext context)
    {
        var parameters = Resolve(context.Plugin, context.Component);
        return new ComponentCompileProviderResult
        {
            Issues = Phase8SerDesValidationProvider.Instance.Validate(
                new ComponentValidationContext(context.Plugin, context.Component, context.Graph)),
            Parameters = new ReadOnlyDictionary<string, string>(parameters)
        };
    }

    internal static Dictionary<string, string> Resolve(ComponentPluginDescriptor plugin, HardwareComponent component)
    {
        var result = plugin.Parameters.ToDictionary(
            schema => schema.Name,
            schema => Canonical(schema, component.Parameters.GetValueOrDefault(schema.Name, schema.DefaultValue)),
            StringComparer.OrdinalIgnoreCase);
        result[ComponentPluginRuntimeKeys.TypeId] = plugin.TypeId;
        result["serdes_model_version"] = "8.1.0";
        result["provenance"] = "phase8_synthetic_functional_default_or_instance_override";
        return result;
    }

    private static string Canonical(ComponentParameterSchema schema, string raw)
    {
        if (schema.IntegerOnly && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer.ToString(CultureInfo.InvariantCulture);
        if ((schema.Minimum.HasValue || schema.Maximum.HasValue) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number.ToString("R", CultureInfo.InvariantCulture);
        return raw.Trim().ToLowerInvariant();
    }
}

internal sealed class Phase8SerDesSimulationRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly Phase8SerDesSimulationRuntimeFactory Instance = new();

    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context)
    {
        var source = new HardwareComponent
        {
            Id = context.Component.Id,
            Type = context.Component.Type,
            TypeId = context.Component.TypeId,
            Parameters = new Dictionary<string, string>(context.Component.Parameters, StringComparer.OrdinalIgnoreCase)
        };
        var parameters = Phase8SerDesCompileProvider.Resolve(context.Plugin, source);
        var descriptor = Phase8SerDesRuntimeKernelFactory.For(context.Plugin.TypeId).Descriptor;
        var latency = ReadInt(parameters, Phase8SerDesKeys.GearboxLatencyCycles, 1);
        if (string.Equals(context.Plugin.TypeId, Phase8SerDesTypeIds.Deserializer, StringComparison.OrdinalIgnoreCase))
            latency += ReadInt(parameters, Phase8SerDesKeys.CdrLatencyCycles, 2);
        return new ComponentSimulationRuntimeDescriptor
        {
            ProcessingLatencyCycles = latency,
            EnergyCategory = EnergyCategory.Conversion,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = new ReadOnlyDictionary<string, string>(parameters),
            KernelId = descriptor.KernelId,
            KernelVersion = descriptor.KernelVersion,
            ContractSchemaId = descriptor.ContractSchemaId,
            CanonicalKernelConfiguration = ComponentExecutionJson.CanonicalizeJson(
                JsonSerializer.Serialize(parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), HardwareGraphJson.Options)),
            KernelImplementationHash = descriptor.ImplementationHash
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw) &&
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}

/// <summary>Creates exact Serializer and Deserializer/CDR runtime kernels.</summary>
public static class Phase8SerDesRuntimeKernelFactory
{
    private static readonly IReadOnlyDictionary<string, IComponentRuntimeKernelFactory> Factories =
        Phase8SerDesTypeIds.All.ToDictionary(
            typeId => typeId,
            typeId => (IComponentRuntimeKernelFactory)new Factory(typeId),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the exact runtime kernel factory for a stable interface type id.</summary>
    public static IComponentRuntimeKernelFactory For(string typeId) =>
        Factories.TryGetValue(ComponentTypeIds.Normalize(typeId), out var factory)
            ? factory
            : throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "No Serializer/Deserializer kernel is registered.");

    private sealed class Factory : IComponentRuntimeKernelFactory, IComponentExecutionContractCompiler
    {
        private readonly string typeId;

        public Factory(string typeId)
        {
            this.typeId = typeId;
            var suffix = typeId.EndsWith(".serializer", StringComparison.OrdinalIgnoreCase) ? "serializer" : "deserializer";
            Descriptor = new ComponentRuntimeKernelDescriptor
            {
                KernelId = "phase8.interface." + suffix,
                KernelVersion = "1.0.0",
                ContractSchemaId = "phase8.interface." + suffix + ".config.v1",
                ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8-serdes-runtime-v1\n" + typeId),
                SupportedOperationKinds = [typeId]
            };
        }

        public ComponentRuntimeKernelDescriptor Descriptor { get; }

        public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context)
        {
            if (!string.Equals(context.Contract.OperationKind, typeId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SerDes kernel operation/type mismatch.");
            return new Phase8SerDesRuntimeKernel(typeId);
        }

        public ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context)
        {
            var values = new SortedDictionary<string, string>(
                context.ConfigurationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
            var gearbox = ReadInt(values, Phase8SerDesKeys.GearboxLatencyCycles, 1);
            var cdr = string.Equals(typeId, Phase8SerDesTypeIds.Deserializer, StringComparison.OrdinalIgnoreCase)
                ? ReadInt(values, Phase8SerDesKeys.CdrLatencyCycles, 2)
                : 0;
            var queueDepth = Math.Max(1, ReadInt(values, Phase8SerDesKeys.QueueDepth, 4));
            var contract = new CompiledComponentExecutionContract
            {
                KernelId = Descriptor.KernelId,
                KernelVersion = Descriptor.KernelVersion,
                ContractSchemaId = Descriptor.ContractSchemaId,
                OperationKind = typeId,
                Ports = context.Template.ExternalPorts.OrderBy(port => port.Name, StringComparer.Ordinal)
                    .Select(port => new CompiledComponentPortContract
                    {
                        Name = port.Name,
                        Direction = port.Direction,
                        SignalType = port.SignalType,
                        DataType = port.DataType,
                        Precision = port.Precision,
                        Protocol = port.Protocol,
                        SemanticRole = port.Name,
                        Shape = port.Shape.ToList(),
                        Bits = Math.Max(0, port.BandwidthBitsPerCycle),
                        Required = port.Required,
                        MultiConnect = false,
                        BandwidthBitsPerCycle = port.BandwidthBitsPerCycle
                    }).ToList(),
                Timing = new CompiledComponentTimingContract
                {
                    OperationLatencyCycles = gearbox + cdr,
                    FixedServiceLatencyCycles = gearbox + cdr,
                    IssueIntervalCycles = 1,
                    RuntimeDependentStallAllowed = true
                },
                Queues = new CompiledComponentQueueContract { InputDepth = queueDepth, OutputDepth = queueDepth },
                Resources = values.Select(pair => new CompiledComponentResourceEntry
                {
                    Name = pair.Key,
                    ResourceKind = "phase8_serdes_parameter",
                    Units = Units(pair.Key),
                    CanonicalValue = pair.Value,
                    ValueType = ValueType(pair.Value)
                }).ToList(),
                KernelConfiguration = CanonicalComponentKernelConfiguration.Create(
                    Descriptor.ContractSchemaId,
                    JsonSerializer.Serialize(values, HardwareGraphJson.Options)),
                TraceDescriptors =
                [
                    new ComponentTraceDescriptor(Descriptor.KernelId + ".runtime", TraceEventType.Compute,
                        "Exact Serializer/Deserializer runtime")
                ],
                MetricDescriptors =
                [
                    new ComponentMetricDescriptor("serdes_dynamic_energy_pJ", "pJ", EnergyCategory.Conversion,
                        "Serializer/Deserializer conversion energy")
                ],
                Provenance = new CompiledComponentExecutionProvenance
                {
                    KernelImplementationHash = Descriptor.ImplementationHash,
                    RegistrySnapshotHash = context.RegistrySnapshotHash,
                    SyntheticProfileOnly = true,
                    FunctionalIdealOnly = true
                }
            };
            contract.RefreshContractHash();
            return new ComponentExecutionContractCompileResult { Contract = contract };
        }

        private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
            values.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : fallback;

        private static string Units(string key) => key switch
        {
            Phase8SerDesKeys.ParallelWidthBits => "bit",
            Phase8SerDesKeys.LaneCount => "lane",
            Phase8SerDesKeys.LaneRateBitsPerCycle => "bit/cycle/lane",
            Phase8SerDesKeys.GearboxLatencyCycles or Phase8SerDesKeys.CdrLatencyCycles => "cycles",
            Phase8SerDesKeys.QueueDepth => "packets",
            Phase8SerDesKeys.EnergyPicojoulesPerBit => "pJ/bit",
            _ => "state"
        };

        private static string ValueType(string value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? "integer" :
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "number" :
            "string";
    }
}

internal sealed class Phase8SerDesRuntimeKernel : IPhaseSafeComponentRuntimeKernel
{
    private readonly bool serializer;

    public Phase8SerDesRuntimeKernel(string typeId)
    {
        serializer = string.Equals(typeId, Phase8SerDesTypeIds.Serializer, StringComparison.OrdinalIgnoreCase);
    }

    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) =>
        new Phase8SerDesRuntimeState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName)
    {
        var expected = serializer ? "parallel_in" : "serial_in";
        return string.Equals(inputPortName, expected, StringComparison.Ordinal) &&
            ((Phase8SerDesRuntimeState)current).Inputs.Count < Math.Max(1, context.Contract.Queues.InputDepth);
    }

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next SerDes states must not alias.");
        var expected = serializer ? "parallel_in" : "serial_in";
        if (!string.Equals(input.InputPortName, expected, StringComparison.Ordinal))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };
        var currentState = (Phase8SerDesRuntimeState)current;
        var nextState = (Phase8SerDesRuntimeState)next;
        if (currentState.Inputs.Count >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };
        nextState.Inputs.Add(PacketClone.Clone(input.Packet));
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events =
            [
                new(TraceEventType.Compute,
                    "phase=1;exact_kernel=true;serdes_sample;operation=" + context.Contract.OperationKind,
                    input.Packet.Id,
                    input.Packet.Bits)
            ]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next SerDes states must not alias.");
        var currentState = (Phase8SerDesRuntimeState)current;
        var nextState = (Phase8SerDesRuntimeState)next;
        var outputs = new List<ComponentRuntimeKernelOutput>();
        var events = new List<ComponentRuntimeKernelEventFact>();
        var energy = new List<ComponentRuntimeEnergyContribution>();
        var metrics = new List<NamedMetricContribution>();

        var ready = currentState.Active.OrderBy(item => item.ReadyCycle)
            .ThenBy(item => item.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && outputQueueAvailable)
        {
            nextState.Active.RemoveAll(item =>
                item.ReadyCycle == ready.ReadyCycle &&
                string.Equals(item.Packet.Id, ready.Packet.Id, StringComparison.Ordinal));
            outputs.Add(new(serializer ? "serial_out" : "parallel_out", PacketClone.Clone(ready.Packet)));
            events.Add(new(TraceEventType.Compute,
                "phase=4;exact_kernel=true;serdes_output;operation=" + context.Contract.OperationKind,
                ready.Packet.Id,
                ready.Packet.Bits));
        }

        if (currentState.Inputs.Count > 0 && context.Cycle >= currentState.NextIssueCycle)
        {
            var packet = PacketClone.Clone(currentState.Inputs[0]);
            nextState.Inputs.RemoveAt(0);
            nextState.NextIssueCycle = context.Cycle + Math.Max(1, context.Contract.Timing.IssueIntervalCycles);
            var inputBits = Math.Max(0, packet.Bits);
            var originalBits = serializer ? inputBits : ReadPacketInt(packet, Phase8SerDesKeys.OriginalBits, inputBits);
            var encodedBits = serializer ? EncodeBits(inputBits, ReadResource(context.Contract, Phase8SerDesKeys.Encoding, "64b66b")) : inputBits;
            var outputBits = serializer ? encodedBits : originalBits;
            var overhead = Math.Max(0, encodedBits - originalBits);
            packet.Metadata[Phase8SerDesKeys.OriginalBits] = originalBits.ToString(CultureInfo.InvariantCulture);
            packet.Metadata[Phase8SerDesKeys.EncodedBits] = encodedBits.ToString(CultureInfo.InvariantCulture);
            packet.Metadata[Phase8SerDesKeys.CodingOverheadBits] = overhead.ToString(CultureInfo.InvariantCulture);
            packet.Metadata[Phase8SerDesKeys.Encoding] = ReadResource(context.Contract, Phase8SerDesKeys.Encoding, "64b66b");
            packet.Metadata[Phase8SerDesKeys.SerializationOwner] = "link";
            packet.Metadata[Phase8SerDesKeys.LaneCount] = ReadResource(context.Contract, Phase8SerDesKeys.LaneCount, "4");
            packet.Bits = outputBits;
            packet.SignalDomain = PacketSignalDomain.Digital;

            var latency = Math.Max(0, context.Contract.Timing.OperationLatencyCycles);
            var readyCycle = context.Cycle + Math.Max(1, latency) - 1;
            events.Add(new(TraceEventType.Compute,
                "phase=4;exact_kernel=true;serdes_issue;operation=" + context.Contract.OperationKind +
                ";original_bits=" + originalBits.ToString(CultureInfo.InvariantCulture) +
                ";encoded_bits=" + encodedBits.ToString(CultureInfo.InvariantCulture) +
                ";serialization_owner=link;ready_cycle=" + readyCycle.ToString(CultureInfo.InvariantCulture),
                packet.Id,
                packet.Bits));

            if (readyCycle <= context.Cycle && outputQueueAvailable && outputs.Count == 0)
                outputs.Add(new(serializer ? "serial_out" : "parallel_out", packet));
            else
                nextState.Active.Add(new(packet, readyCycle));

            var energyPerBit = ReadResourceDouble(context.Contract, Phase8SerDesKeys.EnergyPicojoulesPerBit, 0.01);
            var chargedBits = serializer ? originalBits : encodedBits;
            energy.Add(new(
                serializer ? "serializer_dynamic" : "deserializer_dynamic",
                EnergyKind.Dynamic,
                EnergyCategory.Conversion,
                new Picojoules(Math.Max(0, chargedBits * energyPerBit))));
            metrics.Add(new("serdes_coding_overhead_bits", overhead, "bit"));
            var lanes = Math.Max(1, ReadResourceInt(context.Contract, Phase8SerDesKeys.LaneCount, 4));
            var laneRate = Math.Max(1, ReadResourceInt(context.Contract, Phase8SerDesKeys.LaneRateBitsPerCycle, 32));
            var transportCycles = Math.Max(1, (int)Math.Ceiling(encodedBits / (double)(lanes * laneRate)));
            var utilization = encodedBits / (double)(transportCycles * lanes * laneRate);
            metrics.Add(new("serdes_lane_utilization", utilization, "ratio", NamedMetricAggregationKind.Last));
        }

        return new ComponentRuntimeKernelAdvanceResult
        {
            Outputs = outputs,
            Events = events,
            EnergyContributions = energy,
            NamedMetricContributions = metrics
        };
    }

    private static int EncodeBits(int bits, string encoding) =>
        string.Equals(encoding, "64b66b", StringComparison.OrdinalIgnoreCase)
            ? checked((int)(Math.Ceiling(Math.Max(0, bits) / 64.0) * 66.0))
            : Math.Max(0, bits);

    private static string ReadResource(CompiledComponentExecutionContract contract, string key, string fallback) =>
        contract.Resources.FirstOrDefault(resource => string.Equals(resource.Name, key, StringComparison.OrdinalIgnoreCase))
            ?.CanonicalValue ?? fallback;

    private static int ReadResourceInt(CompiledComponentExecutionContract contract, string key, int fallback) =>
        int.TryParse(ReadResource(contract, key, fallback.ToString(CultureInfo.InvariantCulture)),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double ReadResourceDouble(CompiledComponentExecutionContract contract, string key, double fallback) =>
        double.TryParse(ReadResource(contract, key, fallback.ToString("R", CultureInfo.InvariantCulture)),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static int ReadPacketInt(Packet packet, string key, int fallback) =>
        packet.Metadata.TryGetValue(key, out var raw) &&
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}

internal sealed class Phase8SerDesRuntimeState : IComponentRuntimeKernelState
{
    public List<Packet> Inputs { get; } = [];
    public List<Phase8SerDesPendingPacket> Active { get; } = [];
    public long NextIssueCycle { get; set; }
    public bool IsIdle => Inputs.Count == 0 && Active.Count == 0;

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8SerDesRuntimeState { NextIssueCycle = NextIssueCycle };
        clone.Inputs.AddRange(Inputs.Select(PacketClone.Clone));
        clone.Active.AddRange(Active.Select(item => new Phase8SerDesPendingPacket(PacketClone.Clone(item.Packet), item.ReadyCycle)));
        return clone;
    }
}

internal sealed record Phase8SerDesPendingPacket(Packet Packet, long ReadyCycle);
