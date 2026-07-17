using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Typed signal domain carried by a runtime packet.</summary>
public enum PacketSignalDomain
{
    /// <summary>Packet carries a digital payload.</summary>
    Digital,
    /// <summary>Packet represents an electrical analog signal.</summary>
    ElectricalAnalog,
    /// <summary>Packet carries a typed optical carrier state.</summary>
    Optical,
    /// <summary>Packet carries a control-plane command.</summary>
    Control
}

/// <summary>Creates strict first-party Phase 8 exact runtime kernels by stable plugin type id.</summary>
public static class Phase8OpticalRuntimeKernelFactory
{
    private static readonly IReadOnlyDictionary<string, IComponentRuntimeKernelFactory> Factories =
        Phase8OpticalTypeIds.All.ToDictionary(
            typeId => typeId,
            typeId => (IComponentRuntimeKernelFactory)new Factory(typeId),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the exact kernel factory for a supported stable type id.</summary>
    public static IComponentRuntimeKernelFactory For(string typeId)
    {
        var normalized = ComponentTypeIds.Normalize(typeId);
        if (!Factories.TryGetValue(normalized, out var factory))
        {
            throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "No Phase 8 optical runtime kernel is registered for this type id.");
        }

        return factory;
    }

    /// <summary>Attempts to return an exact kernel factory without providing a fallback.</summary>
    public static bool TryFor(string typeId, out IComponentRuntimeKernelFactory? factory) =>
        Factories.TryGetValue(ComponentTypeIds.Normalize(typeId), out factory);

    private sealed class Factory : IComponentRuntimeKernelFactory, IComponentExecutionContractCompiler
    {
        private readonly OpticalOperation operation;
        private readonly string typeId;

        public Factory(string typeId)
        {
            this.typeId = typeId;
            operation = OpticalOperationMap.For(typeId);
            var suffix = typeId[(typeId.LastIndexOf('.') + 1)..];
            Descriptor = new ComponentRuntimeKernelDescriptor
            {
                KernelId = "phase8.optical." + suffix,
                KernelVersion = "1.0.0",
                ContractSchemaId = "phase8.optical." + suffix + ".config.v1",
                ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8-optical-runtime-v1\n" + typeId),
                SupportedOperationKinds = [typeId]
            };
        }

        public ComponentRuntimeKernelDescriptor Descriptor { get; }

        public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context)
        {
            if (!string.Equals(context.Contract.OperationKind, typeId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Optical kernel '" + Descriptor.KernelId + "' cannot execute operation kind '" + context.Contract.OperationKind + "'.");
            }

            return new Phase8OpticalRuntimeKernel(operation, OpticalKernelConfiguration.From(context));
        }

        public ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context)
        {
            var issues = new List<ComponentTemplateIssue>();
            if (!string.Equals(context.Template.ExecutionBinding?.OperationKind, typeId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ComponentTemplateIssue(
                    ComponentExecutionIssueCodes.KernelIncompatible,
                    ComponentTemplateIssueSeverity.Error,
                    "$.execution_binding.operation_kind",
                    "Optical kernel operation kind must equal stable plugin type id '" + typeId + "'.",
                    typeId));
                return new ComponentExecutionContractCompileResult { Issues = issues };
            }

            var values = new SortedDictionary<string, string>(
                context.ConfigurationValues.ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
            var latency = ReadPositiveOrZero(values, Phase8OpticalParameterKeys.LatencyCycles,
                ReadPositiveOrZero(values, Phase8OpticalParameterKeys.StartupLatencyCycles, 1));
            var queueDepth = Math.Max(1, ReadPositiveOrZero(values, Phase8OpticalParameterKeys.QueueDepth, 4));
            var minimumOutputDepth = operation is OpticalOperation.Splitter or OpticalOperation.MziSwitch ? 2 :
                operation is OpticalOperation.WdmMux ? Math.Max(1, ReadPositiveOrZero(values, Phase8OpticalParameterKeys.ChannelCount, 4)) : 1;
            var category = operation is OpticalOperation.EoConverter or OpticalOperation.OeConverter
                ? EnergyCategory.Conversion
                : EnergyCategory.Optical;
            var contract = new CompiledComponentExecutionContract
            {
                KernelId = Descriptor.KernelId,
                KernelVersion = Descriptor.KernelVersion,
                ContractSchemaId = Descriptor.ContractSchemaId,
                OperationKind = typeId,
                Ports = context.Template.ExternalPorts
                    .OrderBy(port => port.Name, StringComparer.Ordinal)
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
                    })
                    .ToList(),
                Timing = new CompiledComponentTimingContract
                {
                    OperationLatencyCycles = latency,
                    FixedServiceLatencyCycles = latency,
                    IssueIntervalCycles = 1,
                    RuntimeDependentStallAllowed = true
                },
                Queues = new CompiledComponentQueueContract
                {
                    InputDepth = queueDepth,
                    OutputDepth = Math.Max(queueDepth, minimumOutputDepth)
                },
                Resources = values.Select(pair => new CompiledComponentResourceEntry
                {
                    Name = pair.Key,
                    ResourceKind = "phase8_optical_parameter",
                    Units = UnitsFor(pair.Key),
                    CanonicalValue = pair.Value,
                    ValueType = ValueType(pair.Value)
                }).ToList(),
                KernelConfiguration = CanonicalComponentKernelConfiguration.Create(
                    Descriptor.ContractSchemaId,
                    JsonSerializer.Serialize(values, HardwareGraphJson.Options)),
                TraceDescriptors =
                [
                    new ComponentTraceDescriptor(typeId + ".runtime", TraceEventType.Compute, "Phase 8 exact optical runtime")
                ],
                MetricDescriptors =
                [
                    new ComponentMetricDescriptor(typeId + ".energy", "pJ", category, "Phase 8 exact optical runtime energy")
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
            return new ComponentExecutionContractCompileResult { Contract = contract, Issues = issues };
        }

        private static int ReadPositiveOrZero(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
            values.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 0
                ? parsed
                : fallback;

        private static string ValueType(string value) =>
            bool.TryParse(value, out _) ? "boolean" :
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? "integer" :
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "number" :
            "string";

        private static string UnitsFor(string key)
        {
            if (key.EndsWith("_nm", StringComparison.Ordinal)) return "nm";
            if (key.EndsWith("_dbm", StringComparison.Ordinal)) return "dBm";
            if (key.EndsWith("_db", StringComparison.Ordinal)) return "dB";
            if (key.EndsWith("_mw", StringComparison.Ordinal)) return "mW";
            if (key.EndsWith("_v", StringComparison.Ordinal)) return "V";
            if (key.EndsWith("_cycles", StringComparison.Ordinal)) return "cycles";
            if (key.EndsWith("_pj_per_bit", StringComparison.Ordinal)) return "pJ/bit";
            return "";
        }
    }
}

internal enum OpticalOperation
{
    Link,
    Laser,
    MrrRouter,
    MziSwitch,
    Splitter,
    Combiner,
    Photodetector,
    Modulator,
    WdmMux,
    WdmDemux,
    EoConverter,
    OeConverter
}

internal static class OpticalOperationMap
{
    public static OpticalOperation For(string typeId)
    {
        if (string.Equals(typeId, Phase8OpticalTypeIds.Link, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.Link;
        if (string.Equals(typeId, Phase8OpticalTypeIds.Laser, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.Laser;
        if (string.Equals(typeId, Phase8OpticalTypeIds.MrrRouter, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.MrrRouter;
        if (string.Equals(typeId, Phase8OpticalTypeIds.MziSwitch, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.MziSwitch;
        if (string.Equals(typeId, Phase8OpticalTypeIds.Splitter, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.Splitter;
        if (string.Equals(typeId, Phase8OpticalTypeIds.Combiner, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.Combiner;
        if (string.Equals(typeId, Phase8OpticalTypeIds.Photodetector, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.Photodetector;
        if (string.Equals(typeId, Phase8OpticalTypeIds.Modulator, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.Modulator;
        if (string.Equals(typeId, Phase8OpticalTypeIds.WdmMux, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.WdmMux;
        if (string.Equals(typeId, Phase8OpticalTypeIds.WdmDemux, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.WdmDemux;
        if (string.Equals(typeId, Phase8OpticalTypeIds.EoConverter, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.EoConverter;
        if (string.Equals(typeId, Phase8OpticalTypeIds.OeConverter, StringComparison.OrdinalIgnoreCase)) return OpticalOperation.OeConverter;
        throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Unsupported Phase 8 optical type id.");
    }
}

internal sealed class OpticalKernelConfiguration
{
    private readonly Dictionary<string, string> values;

    private OpticalKernelConfiguration(Dictionary<string, string> values)
    {
        this.values = values;
    }

    public static OpticalKernelConfiguration From(ComponentRuntimeKernelCreateContext context)
    {
        var values = new Dictionary<string, string>(context.Contract.Resources
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Name))
            .ToDictionary(resource => resource.Name, resource => resource.CanonicalValue, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(context.Contract.KernelConfiguration.CanonicalJson))
        {
            using var document = JsonDocument.Parse(context.Contract.KernelConfiguration.CanonicalJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : property.Value.GetRawText();
                }
            }
        }

        return new OpticalKernelConfiguration(values);
    }

    public string String(string key, string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    public double Double(string key, double fallback)
    {
        if (values.TryGetValue(key, out var value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public double NonNegative(string key, double fallback) => Math.Max(0, Double(key, fallback));

    public int Int(string key, int fallback)
    {
        if (values.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public int NonNegativeInt(string key, int fallback) => Math.Max(0, Int(key, fallback));

    public int Latency(OpticalOperation operation)
    {
        var fallback = operation == OpticalOperation.Laser
            ? NonNegativeInt(Phase8OpticalParameterKeys.StartupLatencyCycles, 1)
            : 1;
        return NonNegativeInt(Phase8OpticalParameterKeys.LatencyCycles, fallback);
    }

    public double DynamicEnergyPerBit(OpticalOperation operation)
    {
        var key = operation is OpticalOperation.EoConverter or OpticalOperation.OeConverter
            ? Phase8OpticalParameterKeys.ConversionEnergyPicojoulesPerBit
            : Phase8OpticalParameterKeys.DynamicEnergyPicojoulesPerBit;
        return NonNegative(key, 0);
    }

    public IReadOnlyList<double> ChannelTable()
    {
        var raw = String(Phase8OpticalParameterKeys.ChannelTableNanometers, "1550,1551,1552,1553");
        if (raw.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                var parsed = document.RootElement.EnumerateArray()
                    .Select(item => item.GetDouble())
                    .Where(value => double.IsFinite(value) && value > 0)
                    .ToList();
                if (parsed.Count > 0) return parsed;
            }
            catch (JsonException)
            {
            }
        }

        var result = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Select(item => double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN)
            .Where(value => double.IsFinite(value) && value > 0)
            .ToList();
        return result.Count > 0 ? result : [1550, 1551, 1552, 1553];
    }

    public OpticalTuningState InitialTuning() => OpticalTuningModel.Evaluate(new OpticalTuningInput(
        new Nanometers(Math.Max(0.000001, Double(Phase8OpticalParameterKeys.NominalResonanceNanometers, 1550))),
        Double(Phase8OpticalParameterKeys.ResonanceOffsetNanometers, 0),
        Double(Phase8OpticalParameterKeys.TemperatureCelsius, 25),
        Double(Phase8OpticalParameterKeys.ReferenceTemperatureCelsius, 25),
        Double(Phase8OpticalParameterKeys.ThermalDriftNanometersPerCelsius, 0),
        Double(Phase8OpticalParameterKeys.TuningVoltageVolts, 0),
        Double(Phase8OpticalParameterKeys.ReferenceVoltageVolts, 0),
        Double(Phase8OpticalParameterKeys.VoltageDriftNanometersPerVolt, 0),
        new Milliwatts(NonNegative(Phase8OpticalParameterKeys.TuningPowerMilliwatts, 0)),
        NonNegativeInt(Phase8OpticalParameterKeys.TuningLatencyCycles, 1)));

    public OpticalTuningState TuningFrom(Packet packet)
    {
        var baseline = InitialTuning();
        var voltage = PacketDouble(packet, Phase8OpticalParameterKeys.TuningVoltageVolts, baseline.VoltageVolts);
        var temperature = PacketDouble(packet, Phase8OpticalParameterKeys.TemperatureCelsius, baseline.TemperatureCelsius);
        var offset = PacketDouble(packet, Phase8OpticalParameterKeys.ResonanceOffsetNanometers,
            Double(Phase8OpticalParameterKeys.ResonanceOffsetNanometers, 0));
        return OpticalTuningModel.Evaluate(new OpticalTuningInput(
            baseline.NominalResonance,
            offset,
            temperature,
            Double(Phase8OpticalParameterKeys.ReferenceTemperatureCelsius, 25),
            Double(Phase8OpticalParameterKeys.ThermalDriftNanometersPerCelsius, 0),
            voltage,
            Double(Phase8OpticalParameterKeys.ReferenceVoltageVolts, 0),
            Double(Phase8OpticalParameterKeys.VoltageDriftNanometersPerVolt, 0),
            new Milliwatts(PacketDouble(packet, Phase8OpticalParameterKeys.TuningPowerMilliwatts,
                NonNegative(Phase8OpticalParameterKeys.TuningPowerMilliwatts, 0))),
            NonNegativeInt(Phase8OpticalParameterKeys.TuningLatencyCycles, 1)));
    }

    public OpticalPacketState ConfiguredCarrier(Phase8OpticalRuntimeState state) => new()
    {
        Wavelength = new Nanometers(Math.Max(0.000001, state.ConfiguredWavelengthNanometers)),
        ChannelId = state.ConfiguredChannelId,
        OpticalPower = new Dbm(state.ConfiguredOpticalPowerDbm),
        AccumulatedLoss = new Decibels(0),
        AccumulatedCrosstalk = new Decibels(0)
    };

    public static double PacketDouble(Packet packet, string key, double fallback)
    {
        if (packet.Metadata.TryGetValue(key, out var raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
        {
            return parsed;
        }

        return packet.Values.Count > 0 && double.IsFinite(packet.Values[0]) ? packet.Values[0] : fallback;
    }

    public static string PacketString(Packet packet, string key, string fallback) =>
        packet.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
}

internal sealed record OpticalQueuedInput(long Sequence, string PortName, Packet Packet);
internal sealed record OpticalPendingTuning(OpticalTuningState State, long ReadyCycle, string PacketId, string SwitchState);
internal sealed record OpticalPendingTransaction(long ReadyCycle, IReadOnlyList<ComponentRuntimeKernelOutput> Outputs, string Detail)
{
    public OpticalPendingTransaction DeepClone() => new(
        ReadyCycle,
        Outputs.Select(output => new ComponentRuntimeKernelOutput(output.OutputPortName, PacketClone.Clone(output.Packet))).ToList(),
        Detail);
}

internal sealed class Phase8OpticalRuntimeState : IComponentRuntimeKernelState
{
    public Phase8OpticalRuntimeState(OpticalTuningState tuning, string switchState, string allocationMode,
        double configuredWavelengthNanometers, string configuredChannelId, double configuredOpticalPowerDbm)
    {
        Tuning = tuning;
        SwitchState = switchState;
        AllocationMode = allocationMode;
        ConfiguredWavelengthNanometers = configuredWavelengthNanometers;
        ConfiguredChannelId = configuredChannelId;
        ConfiguredOpticalPowerDbm = configuredOpticalPowerDbm;
    }

    public List<OpticalQueuedInput> Inputs { get; } = [];
    public List<OpticalPendingTransaction> Active { get; } = [];
    public OpticalTuningState Tuning { get; set; }
    public OpticalPendingTuning? PendingTuning { get; set; }
    public string SwitchState { get; set; }
    public string AllocationMode { get; set; }
    public double ConfiguredWavelengthNanometers { get; set; }
    public string ConfiguredChannelId { get; set; }
    public double ConfiguredOpticalPowerDbm { get; set; }
    public long NextInputSequence { get; set; }
    public long NextIssueCycle { get; set; }
    public bool LaserEnabled { get; set; }
    public bool IsIdle => Inputs.Count == 0 && Active.Count == 0 && PendingTuning is null;

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8OpticalRuntimeState(
            Tuning,
            SwitchState,
            AllocationMode,
            ConfiguredWavelengthNanometers,
            ConfiguredChannelId,
            ConfiguredOpticalPowerDbm)
        {
            PendingTuning = PendingTuning,
            NextInputSequence = NextInputSequence,
            NextIssueCycle = NextIssueCycle,
            LaserEnabled = LaserEnabled
        };
        clone.Inputs.AddRange(Inputs.Select(input => new OpticalQueuedInput(input.Sequence, input.PortName, PacketClone.Clone(input.Packet))));
        clone.Active.AddRange(Active.Select(item => item.DeepClone()));
        return clone;
    }
}

internal sealed class Phase8OpticalRuntimeKernel : IPhaseSafeComponentRuntimeKernel
{
    private readonly OpticalOperation operation;
    private readonly OpticalKernelConfiguration configuration;

    public Phase8OpticalRuntimeKernel(OpticalOperation operation, OpticalKernelConfiguration configuration)
    {
        this.operation = operation;
        this.configuration = configuration;
    }

    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract)
    {
        _ = contract;
        var state = new Phase8OpticalRuntimeState(
            configuration.InitialTuning(),
            configuration.String(Phase8OpticalParameterKeys.SwitchState, "auto").ToLowerInvariant(),
            configuration.String(Phase8OpticalParameterKeys.AllocationMode, "fixed").ToLowerInvariant(),
            configuration.Double(Phase8OpticalParameterKeys.WavelengthNanometers, 1550),
            configuration.String(Phase8OpticalParameterKeys.ChannelId, "ch0"),
            configuration.Double(Phase8OpticalParameterKeys.OpticalPowerDbm, 0));
        state.LaserEnabled = operation == OpticalOperation.Laser;
        return state;
    }

    public bool CanAccept(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        string inputPortName)
    {
        var state = (Phase8OpticalRuntimeState)current;
        if (!context.Contract.Ports.Any(port =>
                port.Direction == PortDirection.Input &&
                string.Equals(port.Name, inputPortName, StringComparison.Ordinal)))
        {
            return false;
        }

        if (IsControlPort(inputPortName))
        {
            return state.PendingTuning is null;
        }

        var capacity = Math.Max(RequiredInputCapacity(), context.Contract.Queues.InputDepth);
        return state.Inputs.Count < capacity && state.Active.Count < capacity;
    }

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next))
        {
            throw new InvalidOperationException("Current and next optical runtime states must not alias.");
        }

        var nextState = (Phase8OpticalRuntimeState)next;
        var port = context.Contract.Ports.FirstOrDefault(candidate =>
            candidate.Direction == PortDirection.Input &&
            string.Equals(candidate.Name, input.InputPortName, StringComparison.Ordinal));
        if (port is null)
        {
            return Reject("unknown_input_port", "P8OpticalUnknownInputPort",
                "Input port '" + input.InputPortName + "' is not declared by the compiled optical contract.", input.Packet.Id);
        }

        if (IsControlPort(input.InputPortName))
        {
            if (nextState.PendingTuning is not null)
            {
                return Reject("tuning_busy", "P8OpticalTuningBusy",
                    "A prior optical tuning update is still pending commit.", input.Packet.Id);
            }

            ApplyControlInput(context, nextState, input);
            return Accept(context, input, "control_sample");
        }

        var opticalRequired = IsOpticalInput(input.InputPortName);
        var legacyOeCompatibility = opticalRequired &&
            operation == OpticalOperation.OeConverter &&
            context.Component.Type != ComponentKind.Custom;
        if (opticalRequired && input.Packet.OpticalState is null && !legacyOeCompatibility)
        {
            return Reject("optical_state_missing", "P8OpticalPacketStateMissing",
                "Optical input '" + input.InputPortName + "' requires typed OpticalPacketState.", input.Packet.Id);
        }

        if (string.Equals(input.InputPortName, "digital_in", StringComparison.Ordinal) &&
            input.Packet.OpticalState is not null)
        {
            return Reject("signal_domain_mismatch", "P8OpticalSignalDomainMismatch",
                "digital_in cannot accept a packet that still carries OpticalPacketState.", input.Packet.Id);
        }

        var capacity = Math.Max(RequiredInputCapacity(), context.Contract.Queues.InputDepth);
        if (nextState.Inputs.Count >= capacity)
        {
            return Reject("input_queue_full", "P8OpticalInputQueueFull",
                "The exact optical input queue is full.", input.Packet.Id);
        }

        var packet = PacketClone.Clone(input.Packet);
        if (opticalRequired)
        {
            packet.SignalDomain = PacketSignalDomain.Optical;
            if (packet.OpticalState is null)
            {
                packet.OpticalState = configuration.ConfiguredCarrier(nextState);
                packet.Metadata["phase8_legacy_oe_synthetic_carrier"] = "true";
            }
        }
        nextState.Inputs.Add(new OpticalQueuedInput(nextState.NextInputSequence++, input.InputPortName, packet));
        return Accept(context, input, "data_sample");
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next))
        {
            throw new InvalidOperationException("Current and next optical runtime states must not alias.");
        }

        var currentState = (Phase8OpticalRuntimeState)current;
        var nextState = (Phase8OpticalRuntimeState)next;
        var availableSlots = outputQueueAvailable ? Math.Max(0, context.AvailableOutputSlots) : 0;
        var outputs = new List<ComponentRuntimeKernelOutput>();
        var events = new List<ComponentRuntimeKernelEventFact>();
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        var energy = new List<ComponentRuntimeEnergyContribution>();
        var metrics = new List<NamedMetricContribution>();

        AccountSustainedStaticEnergy(currentState, energy, metrics);
        CommitTuning(context, currentState, nextState, events, metrics);

        var ready = currentState.Active
            .OrderBy(item => item.ReadyCycle)
            .ThenBy(item => item.Outputs.FirstOrDefault()?.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && ready.Outputs.Count <= availableSlots)
        {
            nextState.Active.RemoveAll(item =>
                item.ReadyCycle == ready.ReadyCycle &&
                string.Equals(item.Detail, ready.Detail, StringComparison.Ordinal));
            outputs.AddRange(ready.Outputs.Select(CloneOutput));
            availableSlots -= ready.Outputs.Count;
            events.Add(new ComponentRuntimeKernelEventFact(
                TraceEventType.Compute,
                "phase=4;phase8_optical_output;operation=" + context.Contract.OperationKind +
                ";output_count=" + ready.Outputs.Count.ToString(CultureInfo.InvariantCulture),
                ready.Outputs.FirstOrDefault()?.Packet.Id,
                ready.Outputs.Sum(item => item.Packet.Bits)));
        }

        if (context.Cycle >= currentState.NextIssueCycle &&
            currentState.Inputs.Count > 0 &&
            currentState.Active.Count < Math.Max(1, context.Contract.Queues.InputDepth))
        {
            var plan = BuildPlan(context, currentState, nextState);
            if (plan is not null)
            {
                foreach (var sequence in plan.ConsumedSequences)
                {
                    nextState.Inputs.RemoveAll(input => input.Sequence == sequence);
                }

                nextState.NextIssueCycle = context.Cycle + Math.Max(1, context.Contract.Timing.IssueIntervalCycles);
                issues.AddRange(plan.Issues);
                energy.AddRange(plan.Energy);
                metrics.AddRange(plan.Metrics);
                events.AddRange(plan.Events);
                if (!plan.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)) &&
                    plan.Outputs.Count > 0)
                {
                    var readyCycle = context.Cycle + Math.Max(1, configuration.Latency(operation)) - 1;
                    if (readyCycle <= context.Cycle && plan.Outputs.Count <= availableSlots)
                    {
                        outputs.AddRange(plan.Outputs.Select(CloneOutput));
                        availableSlots -= plan.Outputs.Count;
                    }
                    else
                    {
                        nextState.Active.Add(new OpticalPendingTransaction(
                            readyCycle,
                            plan.Outputs.Select(CloneOutput).ToList(),
                            plan.Detail));
                    }
                }
            }
        }

        return new ComponentRuntimeKernelAdvanceResult
        {
            Outputs = outputs,
            Events = events,
            Issues = issues,
            EnergyContributions = energy,
            NamedMetricContributions = metrics
        };
    }

    private void ApplyControlInput(
        ComponentRuntimeKernelCycleContext context,
        Phase8OpticalRuntimeState nextState,
        ComponentRuntimeKernelInput input)
    {
        var packet = input.Packet;
        if (operation == OpticalOperation.WdmMux || operation == OpticalOperation.WdmDemux)
        {
            nextState.AllocationMode = OpticalKernelConfiguration.PacketString(
                packet,
                Phase8OpticalParameterKeys.AllocationMode,
                nextState.AllocationMode).ToLowerInvariant();
            return;
        }

        if (operation == OpticalOperation.EoConverter || operation == OpticalOperation.Laser)
        {
            nextState.ConfiguredWavelengthNanometers = OpticalKernelConfiguration.PacketDouble(
                packet,
                Phase8OpticalParameterKeys.WavelengthNanometers,
                nextState.ConfiguredWavelengthNanometers);
            nextState.ConfiguredChannelId = OpticalKernelConfiguration.PacketString(
                packet,
                Phase8OpticalParameterKeys.ChannelId,
                nextState.ConfiguredChannelId);
            nextState.ConfiguredOpticalPowerDbm = OpticalKernelConfiguration.PacketDouble(
                packet,
                Phase8OpticalParameterKeys.OpticalPowerDbm,
                nextState.ConfiguredOpticalPowerDbm);
        }

        if (operation is OpticalOperation.MrrRouter or OpticalOperation.MziSwitch or OpticalOperation.Modulator)
        {
            var tuning = configuration.TuningFrom(packet);
            var switchState = OpticalKernelConfiguration.PacketString(
                packet,
                Phase8OpticalParameterKeys.SwitchState,
                nextState.SwitchState).ToLowerInvariant();
            nextState.PendingTuning = new OpticalPendingTuning(
                tuning,
                context.Cycle + Math.Max(1, tuning.LatencyCycles),
                packet.Id,
                switchState);
        }
    }

    private void CommitTuning(
        ComponentRuntimeKernelCycleContext context,
        Phase8OpticalRuntimeState current,
        Phase8OpticalRuntimeState next,
        List<ComponentRuntimeKernelEventFact> events,
        List<NamedMetricContribution> metrics)
    {
        if (current.PendingTuning is null || current.PendingTuning.ReadyCycle > context.Cycle) return;
        var pending = current.PendingTuning;
        next.PendingTuning = null;
        next.Tuning = pending.State;
        next.SwitchState = pending.SwitchState;
        metrics.Add(new NamedMetricContribution("tuning_voltage_v", pending.State.VoltageVolts, "V", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("tuning_power_mw", pending.State.TuningPower.Value, "mW", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("temperature_c", pending.State.TemperatureCelsius, "C", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("resonance_drift_nm", pending.State.ResonanceDriftNanometers, "nm", NamedMetricAggregationKind.Last));
        events.Add(new ComponentRuntimeKernelEventFact(
            TraceEventType.Compute,
            "phase=4;phase8_optical_tuning_commit;effective_resonance_nm=" +
            pending.State.EffectiveResonance.Value.ToString("R", CultureInfo.InvariantCulture) +
            ";switch_state=" + pending.SwitchState,
            pending.PacketId));
    }

    private void AccountSustainedStaticEnergy(
        Phase8OpticalRuntimeState current,
        List<ComponentRuntimeEnergyContribution> energy,
        List<NamedMetricContribution> metrics)
    {
        var clockPeriodPicoseconds = configuration.NonNegative(Phase8OpticalParameterKeys.ClockPeriodPicoseconds, 0);
        var powerMilliwatts = operation switch
        {
            OpticalOperation.Laser when current.LaserEnabled =>
                configuration.NonNegative(Phase8OpticalParameterKeys.ElectricalPowerMilliwatts, 0),
            OpticalOperation.MrrRouter or OpticalOperation.MziSwitch => current.Tuning.TuningPower.Value,
            _ => 0
        };
        if (clockPeriodPicoseconds <= 0 || powerMilliwatts <= 0) return;

        var name = operation == OpticalOperation.Laser ? "laser_static_energy_pJ" : "tuning_energy_pJ";
        var kind = operation == OpticalOperation.Laser ? EnergyKind.Static : EnergyKind.Tuning;
        var cycleEnergy = powerMilliwatts * clockPeriodPicoseconds / 1000.0;
        energy.Add(new ComponentRuntimeEnergyContribution(
            name,
            kind,
            EnergyCategory.Optical,
            new Picojoules(cycleEnergy)));
        metrics.Add(new NamedMetricContribution(name, cycleEnergy, "pJ", NamedMetricAggregationKind.Sum));
    }

    private OpticalExecutionPlan? BuildPlan(
        ComponentRuntimeKernelCycleContext context,
        Phase8OpticalRuntimeState state,
        Phase8OpticalRuntimeState nextState) =>
        operation switch
        {
            OpticalOperation.Link => BuildLink(context, state),
            OpticalOperation.Laser => BuildLaser(context, state, nextState),
            OpticalOperation.MrrRouter => BuildMrr(context, state),
            OpticalOperation.MziSwitch => BuildMzi(context, state),
            OpticalOperation.Splitter => BuildSplitter(context, state),
            OpticalOperation.Combiner => BuildCombiner(context, state),
            OpticalOperation.Photodetector => BuildPhotodetector(context, state),
            OpticalOperation.Modulator => BuildModulator(context, state),
            OpticalOperation.WdmMux => BuildWdmMux(context, state),
            OpticalOperation.WdmDemux => BuildWdmDemux(context, state),
            OpticalOperation.EoConverter => BuildEoConverter(context, state),
            OpticalOperation.OeConverter => BuildOeConverter(context, state),
            _ => throw new InvalidOperationException("Unsupported Phase 8 optical operation.")
        };

    private OpticalExecutionPlan? BuildLink(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var input = First(state, "optical_in");
        if (input is null) return null;
        var loss = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, 0);
        var packet = ApplyLoss(input.Packet, loss);
        return Plan(context, [input], [new ComponentRuntimeKernelOutput(Output(context, "optical_out"), packet)],
            "link", OpticalEnergy(input.Packet), OpticalMetrics(packet, loss));
    }

    private OpticalExecutionPlan? BuildLaser(
        ComponentRuntimeKernelCycleContext context,
        Phase8OpticalRuntimeState state,
        Phase8OpticalRuntimeState nextState)
    {
        var input = First(state, "enable");
        if (input is null) return null;
        var enabled = PacketBoolean(input.Packet, "enabled", true);
        nextState.LaserEnabled = enabled;
        if (!enabled)
        {
            return Plan(context, [input], [], "laser_disabled", [], []);
        }

        var packet = PacketClone.Clone(input.Packet);
        packet.SignalDomain = PacketSignalDomain.Optical;
        packet.OpticalState = configuration.ConfiguredCarrier(state);
        return Plan(context, [input], [new ComponentRuntimeKernelOutput(Output(context, "optical_out"), packet)],
            "laser_emit", [], OpticalMetrics(packet, 0));
    }

    private OpticalExecutionPlan? BuildMrr(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var input = First(state, "optical_in");
        if (input is null) return null;
        var optical = RequireOptical(input.Packet);
        var passband = configuration.NonNegative(Phase8OpticalParameterKeys.PassbandNanometers, 0.2);
        var resonant = Math.Abs(optical.Wavelength.Value - state.Tuning.EffectiveResonance.Value) <= passband / 2.0;
        var drop = state.SwitchState == "drop" || (state.SwitchState != "through" && resonant);
        var loss = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.MrrInsertionDb);
        var packet = ApplyLoss(input.Packet, loss);
        var metrics = OpticalMetrics(packet, loss).ToList();
        metrics.Add(new NamedMetricContribution("mrr_drop_selected", drop ? 1 : 0, "count", NamedMetricAggregationKind.Sum));
        return Plan(context, [input],
            [new ComponentRuntimeKernelOutput(Output(context, drop ? "drop_out" : "through_out"), packet)],
            drop ? "mrr_drop" : "mrr_through",
            OpticalEnergy(input.Packet),
            metrics);
    }

    private OpticalExecutionPlan? BuildMzi(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var selected = state.Inputs
            .Where(input => string.Equals(input.PortName, "in_0", StringComparison.Ordinal) ||
                            string.Equals(input.PortName, "in_1", StringComparison.Ordinal))
            .GroupBy(input => input.PortName, StringComparer.Ordinal)
            .Select(group => group.OrderBy(input => input.Sequence).First())
            .OrderBy(input => input.PortName, StringComparer.Ordinal)
            .ToList();
        if (selected.Count == 0) return null;
        var cross = string.Equals(state.SwitchState, "cross", StringComparison.OrdinalIgnoreCase);
        var loss = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.MziInsertionDb);
        var outputs = selected.Select(input =>
        {
            var outputName = string.Equals(input.PortName, "in_0", StringComparison.Ordinal)
                ? (cross ? "out_1" : "out_0")
                : (cross ? "out_0" : "out_1");
            return new ComponentRuntimeKernelOutput(Output(context, outputName), ApplyLoss(input.Packet, loss));
        }).ToList();
        var energy = selected.SelectMany(input => OpticalEnergy(input.Packet)).ToList();
        var metrics = outputs.SelectMany(output => OpticalMetrics(output.Packet, loss)).ToList();
        metrics.Add(new NamedMetricContribution("mzi_cross_selected", cross ? 1 : 0, "count", NamedMetricAggregationKind.Sum));
        return Plan(context, selected, outputs, cross ? "mzi_cross" : "mzi_bar", energy, metrics);
    }

    private OpticalExecutionPlan? BuildSplitter(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var input = First(state, "optical_in");
        if (input is null) return null;
        var loss = configuration.NonNegative(Phase8OpticalParameterKeys.BranchLossDb, OpticalLossDefaults.OneToTwoSplitterDb);
        var left = ApplyLoss(input.Packet, loss);
        PacketTraceIdentity.AssignDerived(left, input.Packet, input.Packet.Id + ":split:0");
        var right = ApplyLoss(input.Packet, loss);
        PacketTraceIdentity.AssignDerived(right, input.Packet, input.Packet.Id + ":split:1");
        var outputs = new List<ComponentRuntimeKernelOutput>
        {
            new(Output(context, "optical_out_0"), left),
            new(Output(context, "optical_out_1"), right)
        };
        var metrics = OpticalMetrics(left, loss).Concat(OpticalMetrics(right, loss)).ToList();
        return Plan(context, [input], outputs, "splitter_atomic_1_to_2", OpticalEnergy(input.Packet), metrics);
    }

    private OpticalExecutionPlan? BuildCombiner(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var contenders = state.Inputs
            .Where(input => input.PortName.StartsWith("optical_in_", StringComparison.Ordinal))
            .OrderBy(input => input.PortName, StringComparer.Ordinal)
            .ThenBy(input => input.Packet.Id, StringComparer.Ordinal)
            .ThenBy(input => input.Sequence)
            .ToList();
        if (contenders.Count == 0) return null;
        var winner = contenders[0];
        var loss = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.CouplerDb);
        var crosstalk = contenders.Count > 1
            ? configuration.NonNegative(Phase8OpticalParameterKeys.CrosstalkDb, 0)
            : 0;
        var packet = ApplyLoss(winner.Packet, loss);
        packet.OpticalState = packet.OpticalState! with
        {
            AccumulatedCrosstalk = packet.OpticalState.AccumulatedCrosstalk + new Decibels(crosstalk)
        };
        var metrics = OpticalMetrics(packet, loss).ToList();
        metrics.Add(new NamedMetricContribution("combiner_conflicts", Math.Max(0, contenders.Count - 1), "count", NamedMetricAggregationKind.Sum));
        metrics.Add(new NamedMetricContribution("accumulated_crosstalk_db", packet.OpticalState.AccumulatedCrosstalk.Value, "dB", NamedMetricAggregationKind.Last));
        return Plan(context, [winner], [new ComponentRuntimeKernelOutput(Output(context, "optical_out"), packet)],
            "combiner_deterministic_winner=" + winner.Packet.Id,
            OpticalEnergy(winner.Packet),
            metrics);
    }
    private OpticalExecutionPlan? BuildPhotodetector(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var input = First(state, "optical_in");
        if (input is null) return null;
        var optical = RequireOptical(input.Packet);
        var packet = PacketClone.Clone(input.Packet);
        packet.SignalDomain = PacketSignalDomain.ElectricalAnalog;
        packet.OpticalState = null;
        var responsivity = configuration.NonNegative(Phase8OpticalParameterKeys.ResponsivityAmperesPerWatt, 1);
        packet.Values = [optical.OpticalPower.ToMilliwatts().Value / 1000.0 * responsivity];
        var boundary = ReceiverBoundary(input.Packet);
        return Plan(context, [input], [new ComponentRuntimeKernelOutput(Output(context, "electrical_out"), packet)],
            "photodetector_receive",
            OpticalEnergy(input.Packet),
            boundary.Metrics,
            boundary.Issues);
    }

    private OpticalExecutionPlan? BuildModulator(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var drive = First(state, "electrical_drive");
        var carrier = First(state, "optical_carrier_in");
        if (drive is null || carrier is null) return null;
        var loss = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, 0);
        var carrierState = RequireOptical(carrier.Packet).ApplyLoss(new Decibels(loss));
        var packet = PacketClone.Clone(drive.Packet);
        PacketTraceIdentity.AssignDerived(packet, drive.Packet, drive.Packet.Id + ":modulated");
        packet.SignalDomain = PacketSignalDomain.Optical;
        packet.OpticalState = carrierState;
        var metrics = OpticalMetrics(packet, loss).ToList();
        metrics.Add(new NamedMetricContribution(
            "modulator_extinction_ratio_db",
            configuration.NonNegative(Phase8OpticalParameterKeys.ExtinctionRatioDb, 0),
            "dB",
            NamedMetricAggregationKind.Last));
        return Plan(context, [drive, carrier], [new ComponentRuntimeKernelOutput(Output(context, "optical_out"), packet)],
            "modulator_encode",
            OpticalEnergy(drive.Packet),
            metrics);
    }

    private OpticalExecutionPlan? BuildWdmMux(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var inputs = state.Inputs
            .Where(input => input.PortName.StartsWith("ch", StringComparison.Ordinal) &&
                            input.PortName.EndsWith("_in", StringComparison.Ordinal))
            .OrderBy(input => input.PortName, StringComparer.Ordinal)
            .ThenBy(input => input.Packet.Id, StringComparer.Ordinal)
            .ThenBy(input => input.Sequence)
            .ToList();
        if (inputs.Count == 0) return null;

        var table = configuration.ChannelTable().Take(Math.Max(1,
            configuration.NonNegativeInt(Phase8OpticalParameterKeys.ChannelCount, configuration.ChannelTable().Count))).ToList();
        var dynamicAllocation = string.Equals(state.AllocationMode, "dynamic", StringComparison.OrdinalIgnoreCase);
        var used = new HashSet<int>();
        var winners = new List<(OpticalQueuedInput Input, int ChannelIndex)>();
        foreach (var input in inputs)
        {
            int channelIndex;
            if (dynamicAllocation)
            {
                channelIndex = Enumerable.Range(0, table.Count).Where(index => !used.Contains(index)).DefaultIfEmpty(-1).First();
            }
            else
            {
                channelIndex = ParseChannelIndex(input.PortName);
            }

            if (channelIndex < 0 || channelIndex >= table.Count || !used.Add(channelIndex)) continue;
            winners.Add((input, channelIndex));
        }

        if (winners.Count == 0) return null;
        var insertion = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, 0);
        var crosstalkPerPeer = configuration.NonNegative(Phase8OpticalParameterKeys.CrosstalkDb, 0);
        var outputs = new List<ComponentRuntimeKernelOutput>();
        var metrics = new List<NamedMetricContribution>();
        foreach (var winner in winners)
        {
            var packet = ApplyLoss(winner.Input.Packet, insertion);
            var optical = packet.OpticalState!;
            packet.OpticalState = optical with
            {
                ChannelId = "ch" + winner.ChannelIndex.ToString(CultureInfo.InvariantCulture),
                Wavelength = new Nanometers(table[winner.ChannelIndex]),
                AccumulatedCrosstalk = optical.AccumulatedCrosstalk +
                    new Decibels(crosstalkPerPeer * Math.Max(0, winners.Count - 1))
            };
            outputs.Add(new ComponentRuntimeKernelOutput(Output(context, "wdm_out"), packet));
            metrics.AddRange(OpticalMetrics(packet, insertion));
        }

        var utilization = winners.Count / (double)Math.Max(1, table.Count);
        var conflicts = Math.Max(0, inputs.Count - winners.Count);
        metrics.Add(new NamedMetricContribution("wdm_channel_busy_packets", winners.Count, "packets", NamedMetricAggregationKind.Sum));
        metrics.Add(new NamedMetricContribution("wdm_channel_occupancy", utilization, "ratio", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("wdm_conflicts", conflicts, "count", NamedMetricAggregationKind.Sum));
        metrics.Add(new NamedMetricContribution("channel_utilization", utilization, "ratio", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("wavelength_conflicts", conflicts, "count", NamedMetricAggregationKind.Sum));
        var energy = winners.SelectMany(winner => OpticalEnergy(winner.Input.Packet)).ToList();
        return Plan(context, winners.Select(winner => winner.Input).ToList(), outputs,
            dynamicAllocation ? "wdm_mux_dynamic" : "wdm_mux_fixed",
            energy,
            metrics,
            [],
            TraceEventType.Arbitration);
    }

    private OpticalExecutionPlan? BuildWdmDemux(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var input = First(state, "wdm_in");
        if (input is null) return null;
        var optical = RequireOptical(input.Packet);
        var table = configuration.ChannelTable().Take(Math.Max(1,
            configuration.NonNegativeInt(Phase8OpticalParameterKeys.ChannelCount, configuration.ChannelTable().Count))).ToList();
        var channelIndex = ParseChannelIndex(optical.ChannelId);
        var tolerance = configuration.NonNegative(Phase8OpticalParameterKeys.ChannelToleranceNanometers, 0.1);
        if (channelIndex < 0 || channelIndex >= table.Count ||
            Math.Abs(table[channelIndex] - optical.Wavelength.Value) > tolerance)
        {
            channelIndex = Enumerable.Range(0, table.Count)
                .OrderBy(index => Math.Abs(table[index] - optical.Wavelength.Value))
                .ThenBy(index => index)
                .Where(index => Math.Abs(table[index] - optical.Wavelength.Value) <= tolerance).DefaultIfEmpty(-1).First();
        }

        if (channelIndex < 0)
        {
            var policy = configuration.String(Phase8OpticalParameterKeys.UnmatchedPolicy, "error");
            var unmatchedMetrics = OpticalMetrics(input.Packet, 0).ToList();
            unmatchedMetrics.Add(new NamedMetricContribution("channel_utilization", 0, "ratio", NamedMetricAggregationKind.Last));
            unmatchedMetrics.Add(new NamedMetricContribution("wavelength_conflicts", 1, "count", NamedMetricAggregationKind.Sum));
            var message = "WDM demux found no deterministic channel within wavelength tolerance.";
            if (string.Equals(policy, "stall", StringComparison.OrdinalIgnoreCase))
            {
                return StallPlan(
                    context,
                    input,
                    "wdm_demux_unmatched",
                    unmatchedMetrics,
                    [new ComponentRuntimeKernelIssueFact("P8WdmChannelUnmatchedStall", "warning", message, input.Packet.Id)]);
            }

            return Plan(context, [input], [], "wdm_demux_unmatched_error", [], unmatchedMetrics,
                [new ComponentRuntimeKernelIssueFact("P8WdmChannelUnmatched", "error", message, input.Packet.Id)]);
        }

        var insertion = configuration.NonNegative(Phase8OpticalParameterKeys.InsertionLossDb, 0);
        var packet = ApplyLoss(input.Packet, insertion);
        packet.OpticalState = packet.OpticalState! with
        {
            ChannelId = "ch" + channelIndex.ToString(CultureInfo.InvariantCulture),
            Wavelength = new Nanometers(table[channelIndex])
        };
        var metrics = OpticalMetrics(packet, insertion).ToList();
        metrics.Add(new NamedMetricContribution("wdm_demux_channel_index", channelIndex, "index", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("wdm_channel_occupancy", 1.0 / Math.Max(1, table.Count), "ratio", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("wdm_conflicts", 0, "count", NamedMetricAggregationKind.Sum));
        metrics.Add(new NamedMetricContribution("channel_utilization", 1.0 / Math.Max(1, table.Count), "ratio", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("wavelength_conflicts", 0, "count", NamedMetricAggregationKind.Sum));
        return Plan(context, [input],
            [new ComponentRuntimeKernelOutput(Output(context, "ch" + channelIndex.ToString(CultureInfo.InvariantCulture) + "_out"), packet)],
            "wdm_demux",
            OpticalEnergy(input.Packet),
            metrics);
    }

    private OpticalExecutionPlan? BuildEoConverter(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var input = First(state, "digital_in");
        if (input is null) return null;
        var packet = PacketClone.Clone(input.Packet);
        PacketTraceIdentity.AssignDerived(packet, input.Packet, input.Packet.Id + ":eo");
        packet.SignalDomain = PacketSignalDomain.Optical;
        packet.OpticalState = configuration.ConfiguredCarrier(state);
        return Plan(context, [input], [new ComponentRuntimeKernelOutput(Output(context, "optical_out"), packet)],
            "eo_convert",
            OpticalEnergy(input.Packet),
            OpticalMetrics(packet, 0));
    }

    private OpticalExecutionPlan? BuildOeConverter(ComponentRuntimeKernelCycleContext context, Phase8OpticalRuntimeState state)
    {
        var opticalInput = First(state, "optical_in");
        var detectorInput = First(state, "detector_current_in");
        var input = opticalInput ?? detectorInput;
        if (input is null) return null;
        var packet = PacketClone.Clone(input.Packet);
        PacketTraceIdentity.AssignDerived(packet, input.Packet, input.Packet.Id + ":oe");
        packet.SignalDomain = PacketSignalDomain.Digital;
        packet.OpticalState = null;

        IReadOnlyList<NamedMetricContribution> metrics;
        IReadOnlyList<ComponentRuntimeKernelIssueFact> issues;
        string detail;
        if (opticalInput is not null)
        {
            _ = RequireOptical(input.Packet);
            var boundary = ReceiverBoundary(input.Packet);
            metrics = boundary.Metrics;
            issues = boundary.Issues;
            detail = "oe_convert_optical_input";
        }
        else
        {
            if (input.Packet.SignalDomain != PacketSignalDomain.ElectricalAnalog)
            {
                throw new InvalidOperationException(
                    "O/E detector_current_in requires an ElectricalAnalog packet from a detector front end.");
            }

            var detectorCurrent = input.Packet.Values.Count > 0 ? input.Packet.Values[0] : 0;
            metrics =
            [
                new NamedMetricContribution("detector_current_A", detectorCurrent, "A", NamedMetricAggregationKind.Last)
            ];
            issues = [];
            detail = "oe_convert_external_detector_current";
        }
        return Plan(context, [input], [new ComponentRuntimeKernelOutput(Output(context, "digital_out"), packet)],
            detail,
            OpticalEnergy(input.Packet),
            metrics,
            issues);
    }

    private (IReadOnlyList<NamedMetricContribution> Metrics, IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues)
        ReceiverBoundary(Packet packet)
    {
        var optical = RequireOptical(packet);
        var sensitivity = configuration.Double(Phase8OpticalParameterKeys.ReceiverSensitivityDbm, -20);
        var margin = optical.OpticalPower.Value - sensitivity;
        var metrics = OpticalMetrics(packet, 0).ToList();
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        metrics.Add(new NamedMetricContribution("receiver_sensitivity_dbm", sensitivity, "dBm", NamedMetricAggregationKind.Last));
        metrics.Add(new NamedMetricContribution("receiver_margin_db", margin, "dB", NamedMetricAggregationKind.Last));
        if (margin < 0)
        {
            issues.Add(new ComponentRuntimeKernelIssueFact(
                OpticalWarningCodes.ReceiverMarginBelowSensitivity,
                "warning",
                "Received optical power " + optical.OpticalPower.Value.ToString("R", CultureInfo.InvariantCulture) +
                " dBm is below receiver sensitivity " + sensitivity.ToString("R", CultureInfo.InvariantCulture) +
                " dBm; margin=" + margin.ToString("R", CultureInfo.InvariantCulture) + " dB.",
                packet.Id));
        }

        var snrThreshold = configuration.Double(Phase8OpticalParameterKeys.SignalToNoiseThresholdDb, double.NegativeInfinity);
        if (optical.SignalToNoiseRatio is not null)
        {
            metrics.Add(new NamedMetricContribution("snr_db", optical.SignalToNoiseRatio.Value.Value, "dB", NamedMetricAggregationKind.Last));
            if (double.IsFinite(snrThreshold) && optical.SignalToNoiseRatio.Value.Value < snrThreshold)
            {
                issues.Add(new ComponentRuntimeKernelIssueFact(
                    "P8SnrBelowThreshold",
                    "warning",
                    "Optical SNR is below the configured receiver threshold.",
                    packet.Id));
            }
        }

        issues.Add(new ComponentRuntimeKernelIssueFact(
            OpticalWarningCodes.BerNotModeled,
            "warning",
            "BER not modeled",
            packet.Id));
        return (metrics, issues);
    }

    private IReadOnlyList<ComponentRuntimeEnergyContribution> OpticalEnergy(Packet packet)
    {
        var rate = configuration.DynamicEnergyPerBit(operation);
        if (rate <= 0 || packet.Bits <= 0) return [];
        var category = operation is OpticalOperation.EoConverter or OpticalOperation.OeConverter
            ? EnergyCategory.Conversion
            : EnergyCategory.Optical;
        var kind = category == EnergyCategory.Conversion ? EnergyKind.Conversion : EnergyKind.Dynamic;
        var name = category == EnergyCategory.Conversion ? "conversion_energy_pJ" : "optical_dynamic_energy_pJ";
        return [new ComponentRuntimeEnergyContribution(name, kind, category, new Picojoules(rate * packet.Bits))];
    }

    private static IReadOnlyList<NamedMetricContribution> OpticalMetrics(Packet packet, double appliedLossDb)
    {
        var optical = RequireOptical(packet);
        var metrics = new List<NamedMetricContribution>
        {
            new("optical_loss_applied_db", appliedLossDb, "dB", NamedMetricAggregationKind.Sum),
            new("optical_total_loss_db", optical.AccumulatedLoss.Value, "dB", NamedMetricAggregationKind.Last),
            new("optical_power_dbm", optical.OpticalPower.Value, "dBm", NamedMetricAggregationKind.Last),
            new("wavelength_nm", optical.Wavelength.Value, "nm", NamedMetricAggregationKind.Last),
            new("accumulated_crosstalk_db", optical.AccumulatedCrosstalk.Value, "dB", NamedMetricAggregationKind.Last),
            new("total_loss_dB", optical.AccumulatedLoss.Value, "dB", NamedMetricAggregationKind.Last),
            new("min_received_power_dBm", optical.OpticalPower.Value, "dBm", NamedMetricAggregationKind.Minimum),
            new("max_crosstalk_dB", optical.AccumulatedCrosstalk.Value, "dB", NamedMetricAggregationKind.Maximum)
        };
        if (optical.SignalToNoiseRatio is not null)
        {
            metrics.Add(new NamedMetricContribution("snr_db", optical.SignalToNoiseRatio.Value.Value, "dB", NamedMetricAggregationKind.Last));
        }
        return metrics;
    }

    private static Packet ApplyLoss(Packet source, double lossDb)
    {
        var packet = PacketClone.Clone(source);
        packet.SignalDomain = PacketSignalDomain.Optical;
        packet.OpticalState = RequireOptical(source).ApplyLoss(new Decibels(Math.Max(0, lossDb)));
        return packet;
    }

    private static OpticalPacketState RequireOptical(Packet packet) =>
        packet.OpticalState ?? throw new InvalidOperationException(
            "Phase 8 optical runtime received a packet without typed OpticalPacketState.");

    private static OpticalQueuedInput? First(Phase8OpticalRuntimeState state, string portName) =>
        state.Inputs
            .Where(input => string.Equals(input.PortName, portName, StringComparison.Ordinal))
            .OrderBy(input => input.Sequence)
            .ThenBy(input => input.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static ComponentRuntimeKernelOutput CloneOutput(ComponentRuntimeKernelOutput output) =>
        new(output.OutputPortName, PacketClone.Clone(output.Packet));

    private static int ParseChannelIndex(string value)
    {
        var digits = new string((value ?? "").SkipWhile(character => !char.IsDigit(character)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
    }

    private string Output(ComponentRuntimeKernelCycleContext context, string expected)
    {
        var exact = context.Contract.Ports.FirstOrDefault(port =>
            port.Direction == PortDirection.Output &&
            string.Equals(port.Name, expected, StringComparison.Ordinal));
        if (exact is not null) return exact.Name;
        var semantic = context.Contract.Ports.FirstOrDefault(port =>
            port.Direction == PortDirection.Output &&
            (string.Equals(port.SemanticRole, expected, StringComparison.OrdinalIgnoreCase) ||
             port.Name.EndsWith(expected, StringComparison.OrdinalIgnoreCase)));
        return semantic?.Name ?? throw new InvalidOperationException(
            "Compiled optical contract is missing required output port '" + expected + "'.");
    }

    private static OpticalExecutionPlan StallPlan(
        ComponentRuntimeKernelCycleContext context,
        OpticalQueuedInput heldInput,
        string detail,
        IReadOnlyList<NamedMetricContribution> metrics,
        IReadOnlyList<ComponentRuntimeKernelIssueFact> issues)
    {
        return new OpticalExecutionPlan(
            [],
            [],
            [],
            metrics.ToList(),
            issues.ToList(),
            [
                new ComponentRuntimeKernelEventFact(
                    TraceEventType.Stall,
                    "phase=4;phase8_optical_stall;operation=" + context.Contract.OperationKind +
                    ";behavior=" + detail + ";stall_reason=WavelengthUnmatched;held_packet=true;" +
                    OpticalPacketTraceDetail(heldInput.Packet.OpticalState),
                    heldInput.Packet.Id,
                    heldInput.Packet.Bits),
                new ComponentRuntimeKernelEventFact(
                    TraceEventType.Compute,
                    "plugin_runtime:type_id=" + context.Contract.OperationKind +
                    ";exact_kernel=true;behavior=" + detail + ";held_packet=true",
                    heldInput.Packet.Id,
                    heldInput.Packet.Bits)
            ],
            detail);
    }

    private OpticalExecutionPlan Plan(
        ComponentRuntimeKernelCycleContext context,
        IReadOnlyList<OpticalQueuedInput> consumed,
        IReadOnlyList<ComponentRuntimeKernelOutput> outputs,
        string detail,
        IReadOnlyList<ComponentRuntimeEnergyContribution> energy,
        IReadOnlyList<NamedMetricContribution> metrics,
        IReadOnlyList<ComponentRuntimeKernelIssueFact>? issues = null,
        TraceEventType eventType = TraceEventType.Compute)
    {
        var packet = consumed.FirstOrDefault()?.Packet;
        var allMetrics = metrics.Concat(energy.Select(contribution =>
            new NamedMetricContribution(
                contribution.Name,
                contribution.Energy.Value,
                "pJ",
                NamedMetricAggregationKind.Sum))).ToList();
        var opticalPacket = outputs
            .Select(output => output.Packet)
            .Concat(consumed.Select(input => input.Packet))
            .FirstOrDefault(candidate => candidate.OpticalState is not null);
        var traceFacts = string.Join(";", new[]
        {
            OpticalPacketTraceDetail(opticalPacket?.OpticalState),
            OpticalMetricTraceDetail(allMetrics),
            outputs.Count == 1 ? PacketTraceIdentity.TraceDetail(outputs[0].Packet) : ""
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var traceFactSuffix = string.IsNullOrWhiteSpace(traceFacts) ? "" : ";" + traceFacts;
        return new OpticalExecutionPlan(
            consumed.Select(input => input.Sequence).ToList(),
            outputs.Select(CloneOutput).ToList(),
            energy.ToList(),
            allMetrics,
            issues?.ToList() ?? [],
            [
                new ComponentRuntimeKernelEventFact(
                    eventType,
                    "phase=4;phase8_optical_issue;operation=" + context.Contract.OperationKind + ";behavior=" + detail +
                    ";output_count=" + outputs.Count.ToString(CultureInfo.InvariantCulture) + traceFactSuffix,
                    packet?.Id,
                    consumed.Sum(input => input.Packet.Bits)),
                new ComponentRuntimeKernelEventFact(
                    TraceEventType.Compute,
                    "plugin_runtime:type_id=" + context.Contract.OperationKind + ";exact_kernel=true;behavior=" + detail,
                    packet?.Id,
                    consumed.Sum(input => input.Packet.Bits))
            ],
            detail);
    }

    private static string OpticalPacketTraceDetail(OpticalPacketState? optical)
    {
        if (optical is null)
        {
            return "";
        }

        var fields = new List<string>
        {
            "signal_domain=" + PacketSignalDomain.Optical,
            "channel_id=" + optical.ChannelId,
            "wavelength_nm=" + optical.Wavelength.Value.ToString("R", CultureInfo.InvariantCulture),
            "optical_power_dbm=" + optical.OpticalPower.Value.ToString("R", CultureInfo.InvariantCulture),
            "accumulated_loss_db=" + optical.AccumulatedLoss.Value.ToString("R", CultureInfo.InvariantCulture),
            "accumulated_crosstalk_db=" + optical.AccumulatedCrosstalk.Value.ToString("R", CultureInfo.InvariantCulture)
        };
        if (optical.SignalToNoiseRatio is not null)
        {
            fields.Add("snr_db=" + optical.SignalToNoiseRatio.Value.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        return string.Join(";", fields);
    }

    private static string OpticalMetricTraceDetail(IReadOnlyList<NamedMetricContribution> metrics)
    {
        var fields = new (string MetricName, string TraceName)[]
        {
            ("receiver_sensitivity_dbm", "receiver_sensitivity_dbm"),
            ("receiver_margin_db", "receiver_margin_db"),
            ("detector_current_A", "detector_current_A")
        };
        return string.Join(";", fields.Select(field =>
            {
                var metric = metrics.LastOrDefault(candidate =>
                    string.Equals(candidate.Name, field.MetricName, StringComparison.OrdinalIgnoreCase));
                return metric is null || !double.IsFinite(metric.Value)
                    ? ""
                    : field.TraceName + "=" + metric.Value.ToString("R", CultureInfo.InvariantCulture);
            })
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private int RequiredInputCapacity() => operation switch
    {
        OpticalOperation.MziSwitch or OpticalOperation.Combiner or OpticalOperation.Modulator => 2,
        OpticalOperation.WdmMux => Math.Max(1, configuration.NonNegativeInt(Phase8OpticalParameterKeys.ChannelCount, 4)),
        _ => 1
    };

    private bool IsControlPort(string portName) => operation switch
    {
        OpticalOperation.MrrRouter => string.Equals(portName, "tune", StringComparison.Ordinal),
        OpticalOperation.MziSwitch => string.Equals(portName, "control", StringComparison.Ordinal),
        OpticalOperation.Modulator => string.Equals(portName, "bias", StringComparison.Ordinal),
        OpticalOperation.WdmMux or OpticalOperation.WdmDemux => string.Equals(portName, "config", StringComparison.Ordinal),
        OpticalOperation.EoConverter or OpticalOperation.OeConverter => string.Equals(portName, "control", StringComparison.Ordinal),
        _ => false
    };

    private static bool IsOpticalInput(string portName) =>
        portName.StartsWith("optical_", StringComparison.Ordinal) ||
        string.Equals(portName, "wdm_in", StringComparison.Ordinal) ||
        (portName.StartsWith("ch", StringComparison.Ordinal) && portName.EndsWith("_in", StringComparison.Ordinal));

    private static bool PacketBoolean(Packet packet, string key, bool fallback)
    {
        if (packet.Metadata.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed)) return parsed;
        if (packet.Values.Count > 0) return packet.Values[0] != 0;
        return fallback;
    }

    private static ComponentRuntimeKernelInputResult Accept(
        ComponentRuntimeKernelCycleContext context,
        ComponentRuntimeKernelInput input,
        string behavior) => new()
        {
            Accepted = true,
            Events =
            [
                new ComponentRuntimeKernelEventFact(
                    TraceEventType.Compute,
                    "phase=1;phase8_optical_sample;operation=" + context.Contract.OperationKind +
                    ";port=" + input.InputPortName + ";behavior=" + behavior,
                    input.Packet.Id,
                    input.Packet.Bits)
            ]
        };

    private static ComponentRuntimeKernelInputResult Reject(
        string stallReason,
        string code,
        string message,
        string packetId) => new()
        {
            Accepted = false,
            StallReason = stallReason,
            Issues = [new ComponentRuntimeKernelIssueFact(code, "error", message, packetId)]
        };

    private sealed record OpticalExecutionPlan(
        IReadOnlyList<long> ConsumedSequences,
        IReadOnlyList<ComponentRuntimeKernelOutput> Outputs,
        IReadOnlyList<ComponentRuntimeEnergyContribution> Energy,
        IReadOnlyList<NamedMetricContribution> Metrics,
        IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues,
        IReadOnlyList<ComponentRuntimeKernelEventFact> Events,
        string Detail);
}
