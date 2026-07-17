using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Builds the twelve first-party Phase 8 optical plugin contracts without enum dispatch.</summary>
internal static class Phase8OpticalPluginContracts
{
    private sealed record Definition(
        string TypeId,
        string DisplayName,
        string Glyph,
        string ColorHex,
        string Abbreviation,
        string Summary,
        int SortOrder,
        IReadOnlyList<string>? LegacyAliases = null);

    private static readonly IReadOnlyList<Definition> Definitions =
    [
        new(Phase8OpticalTypeIds.Link, "Optical Link", "optical-link", "#22D3EE", "OLK", "RoutePath-backed optical waveguide", 300),
        new(Phase8OpticalTypeIds.Laser, "Laser", "laser", "#FBBF24", "LAS", "Wavelength carrier and static optical source", 301),
        new(Phase8OpticalTypeIds.MrrRouter, "MRR Router", "mrr-router", "#A78BFA", "MRR", "Wavelength-selective through/drop router", 302),
        new(Phase8OpticalTypeIds.MziSwitch, "MZI Switch", "mzi-switch", "#818CF8", "MZI", "Two-by-two bar/cross optical switch", 303),
        new(Phase8OpticalTypeIds.Splitter, "Optical Splitter", "splitter", "#34D399", "SPL", "One-to-two optical power splitter", 304),
        new(Phase8OpticalTypeIds.Combiner, "Optical Combiner", "combiner", "#2DD4BF", "CMB", "Deterministically arbitrated optical combiner", 305),
        new(Phase8OpticalTypeIds.Photodetector, "Photodetector", "photodetector", "#FB7185", "PD", "Optical-to-electrical analog detector", 306),
        new(Phase8OpticalTypeIds.Modulator, "Modulator", "modulator", "#E879F9", "MOD", "Electrical-drive optical modulator", 307),
        new(Phase8OpticalTypeIds.WdmMux, "WDM Mux", "wdm-mux", "#FB923C", "MUX", "Fixed or dynamically allocated wavelength multiplexer", 308),
        new(Phase8OpticalTypeIds.WdmDemux, "WDM Demux", "wdm-demux", "#A3E635", "DMX", "Wavelength-selective demultiplexer", 309),
        new(Phase8OpticalTypeIds.EoConverter, "E/O Converter", "eo-converter", "#F87171", "EO", "Digital-to-optical domain converter", 310),
        new(Phase8OpticalTypeIds.OeConverter, "O/E Converter", "oe-converter", "#38BDF8", "OE", "Optical-to-digital domain converter", 311, ["O/E Receiver"])
    ];

    public static IReadOnlyList<ComponentPluginDescriptor> Descriptors() => Definitions.Select(Create).ToList();

    private static ComponentPluginDescriptor Create(Definition definition)
    {
        var ports = PortsFor(definition.TypeId);
        var parameters = ParametersFor(definition.TypeId);
        var traces = TraceDescriptorsFor(definition.TypeId);
        var metrics = MetricDescriptorsFor(definition.TypeId);
        return new ComponentPluginDescriptor(
            definition.TypeId,
            definition.DisplayName,
            "Optical",
            "8.0.0",
            ports,
            parameters,
            Phase8OpticalValidationProvider.Instance,
            Phase8OpticalCompileProvider.Instance,
            Phase8OpticalSimulationRuntimeFactory.Instance,
            traces,
            metrics,
            PrimitiveDescriptor: new ComponentTemplatePrimitiveDescriptor(
                definition.TypeId + ".primitive",
                definition.DisplayName + " Primitive",
                "Optical",
                ports,
                parameters),
            CompiledProfileFactoryDescriptor: new CompiledProfileFactoryDescriptor(
                definition.TypeId + ".profile-factory",
                "compiled-profile",
                "8.0.0",
                $"Produces a Phase 7C CompiledComponentProfile for {definition.DisplayName}."),
            UnityPresentationDescriptor: new UnityPresentationDescriptor(
                definition.Glyph,
                definition.ColorHex,
                definition.Abbreviation,
                definition.Summary,
                definition.SortOrder),
            SourceKind: ComponentPluginSourceKind.FirstParty,
            LegacyKind: null,
            ShowInPalette: true,
            RuntimeKernelFactory: Phase8OpticalRuntimeKernelFactory.For(definition.TypeId),
            LegacyAliases: definition.LegacyAliases);
    }

    private static IReadOnlyList<ComponentPortSchema> PortsFor(string typeId) => typeId switch
    {
        Phase8OpticalTypeIds.Link => [OpticalIn("optical_in"), OpticalOut("optical_out")],
        Phase8OpticalTypeIds.Laser => [ControlIn("enable", "enable", "boolean", required: false), OpticalOut("optical_out")],
        Phase8OpticalTypeIds.MrrRouter =>
        [
            OpticalIn("optical_in"), OpticalOut("through_out"), OpticalOut("drop_out"),
            ControlIn("tune", "tuning_command", "V|mW|cycles|degC", required: false)
        ],
        Phase8OpticalTypeIds.MziSwitch =>
        [
            OpticalIn("in_0"), OpticalIn("in_1"), OpticalOut("out_0"), OpticalOut("out_1"),
            ControlIn("control", "switch_state", "state", required: false)
        ],
        Phase8OpticalTypeIds.Splitter => [OpticalIn("optical_in"), OpticalOut("optical_out_0"), OpticalOut("optical_out_1")],
        Phase8OpticalTypeIds.Combiner => [OpticalIn("optical_in_0"), OpticalIn("optical_in_1"), OpticalOut("optical_out")],
        Phase8OpticalTypeIds.Photodetector => [OpticalIn("optical_in"), AnalogOut("electrical_out", "photocurrent", "A")],
        Phase8OpticalTypeIds.Modulator =>
        [
            AnalogIn("electrical_drive", "drive_voltage", "V"), OpticalIn("optical_carrier_in"),
            OpticalOut("optical_out"), ControlIn("bias", "bias_voltage", "V", required: false)
        ],
        Phase8OpticalTypeIds.WdmMux =>
        [
            OpticalIn("ch0_in"), OpticalIn("ch1_in", required: false), OpticalIn("ch2_in", required: false), OpticalIn("ch3_in", required: false),
            OpticalOut("wdm_out"), ControlIn("config", "wavelength_allocation", "configuration", required: false)
        ],
        Phase8OpticalTypeIds.WdmDemux =>
        [
            OpticalIn("wdm_in"), OpticalOut("ch0_out"), OpticalOut("ch1_out", required: false),
            OpticalOut("ch2_out", required: false), OpticalOut("ch3_out", required: false),
            ControlIn("config", "wavelength_allocation", "configuration", required: false)
        ],
        Phase8OpticalTypeIds.EoConverter =>
        [
            DigitalIn("digital_in"), OpticalOut("optical_out"),
            ControlIn("control", "converter_control", "configuration", required: false)
        ],
        Phase8OpticalTypeIds.OeConverter =>
        [
            OpticalIn("optical_in", required: false),
            AnalogIn("detector_current_in", "photocurrent", "A", required: false),
            DigitalOut("digital_out"),
            ControlIn("control", "converter_control", "configuration", required: false)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Unknown Phase 8 optical type id.")
    };

    private static IReadOnlyList<ComponentParameterSchema> ParametersFor(string typeId) => typeId switch
    {
        Phase8OpticalTypeIds.Link =>
        [
            Choice(Phase8OpticalParameterKeys.WaveguideMaterial, "silicon_nitride", ["silicon_nitride", "silicon"], "Waveguide material selected before RoutePath loss evaluation."),
            Number(Phase8OpticalParameterKeys.DynamicEnergyPicojoulesPerBit, 0.001, "pJ/bit", 0, 1_000_000, "Synthetic functional optical-link dynamic energy."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 0, 0, 1_000_000, "Component-local latency; route propagation latency remains RoutePath-derived.")
        ],
        Phase8OpticalTypeIds.Laser =>
        [
            Number(Phase8OpticalParameterKeys.WavelengthNanometers, 1550, "nm", 1, 1_000_000, "Laser wavelength."),
            Text(Phase8OpticalParameterKeys.ChannelId, "ch0", "channel", "Stable wavelength channel id."),
            Number(Phase8OpticalParameterKeys.OpticalPowerDbm, 0, "dBm", -200, 200, "Laser optical output power."),
            Number(Phase8OpticalParameterKeys.ElectricalPowerMilliwatts, 10, "mW", 0, 1_000_000, "Synthetic functional laser static electrical power."),
            Integer(Phase8OpticalParameterKeys.StartupLatencyCycles, 1, 0, 1_000_000, "Laser enable-to-output latency.")
        ],
        Phase8OpticalTypeIds.MrrRouter => TuningParameters(
        [
            Number(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.MrrInsertionDb, "dB", 0, 100, "Frozen Phase 8 MRR insertion loss."),
            Number(Phase8OpticalParameterKeys.PassbandNanometers, 0.8, "nm", 0.000001, 1_000_000, "Synthetic functional resonance passband."),
            Choice(Phase8OpticalParameterKeys.SwitchState, "auto", ["auto", "through", "drop"], "MRR wavelength routing state.")
        ]),
        Phase8OpticalTypeIds.MziSwitch => TuningParameters(
        [
            Number(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.MziInsertionDb, "dB", 0, 100, "Frozen Phase 8 MZI insertion loss."),
            Choice(Phase8OpticalParameterKeys.SwitchState, "bar", ["bar", "cross"], "MZI two-by-two route state.")
        ]),
        Phase8OpticalTypeIds.Splitter =>
        [
            Choice(Phase8OpticalParameterKeys.SplitRatio, "1:2", ["1:2"], "Phase 8 one-to-two splitter ratio."),
            Number(Phase8OpticalParameterKeys.BranchLossDb, OpticalLossDefaults.OneToTwoSplitterDb, "dB/branch", 0, 100, "Frozen Phase 8 one-to-two splitter loss per branch."),
            Number(Phase8OpticalParameterKeys.DynamicEnergyPicojoulesPerBit, 0, "pJ/bit", 0, 1_000_000, "Synthetic functional splitter dynamic energy.")
        ],
        Phase8OpticalTypeIds.Combiner =>
        [
            Number(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.CouplerDb, "dB", 0, 100, "Synthetic functional combiner insertion loss."),
            Number(Phase8OpticalParameterKeys.CrosstalkDb, 0, "dB", 0, 300, "Synthetic functional combiner crosstalk penalty."),
            Integer(Phase8OpticalParameterKeys.QueueDepth, 2, 1, 65_536, "Per-input deterministic queue depth.")
        ],
        Phase8OpticalTypeIds.Photodetector =>
        [
            Number(Phase8OpticalParameterKeys.ReceiverSensitivityDbm, -20, "dBm", -300, 300, "Synthetic functional receiver sensitivity."),
            Number(Phase8OpticalParameterKeys.ResponsivityAmperesPerWatt, 0.8, "A/W", 0, 1_000_000, "Synthetic functional photodetector responsivity."),
            Number(Phase8OpticalParameterKeys.SignalToNoiseThresholdDb, 10, "dB", -300, 300, "Optional SNR threshold when an SNR model is present."),
            Choice(Phase8OpticalParameterKeys.BerModel, "none", ["none"], "Phase 8 does not predict BER."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 1, 0, 1_000_000, "Detection latency."),
            Number(Phase8OpticalParameterKeys.DynamicEnergyPicojoulesPerBit, 0.01, "pJ/bit", 0, 1_000_000, "Synthetic functional detector dynamic energy.")
        ],
        Phase8OpticalTypeIds.Modulator =>
        [
            Number(Phase8OpticalParameterKeys.InsertionLossDb, 1, "dB", 0, 100, "Synthetic functional modulator insertion loss."),
            Number(Phase8OpticalParameterKeys.ExtinctionRatioDb, 20, "dB", 0, 300, "Synthetic functional extinction ratio."),
            Number(Phase8OpticalParameterKeys.BiasVoltageVolts, 0, "V", -1_000_000, 1_000_000, "Modulator bias voltage."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 1, 0, 1_000_000, "Modulation latency."),
            Number(Phase8OpticalParameterKeys.DynamicEnergyPicojoulesPerBit, 0.04, "pJ/bit", 0, 1_000_000, "Synthetic functional modulation energy.")
        ],
        Phase8OpticalTypeIds.WdmMux => WdmParameters(includeUnmatchedPolicy: false),
        Phase8OpticalTypeIds.WdmDemux => WdmParameters(includeUnmatchedPolicy: true),
        Phase8OpticalTypeIds.EoConverter => AdapterCompatible(
        [
            Number(Phase8OpticalParameterKeys.WavelengthNanometers, 1550, "nm", 1, 1_000_000, "Output wavelength."),
            Text(Phase8OpticalParameterKeys.ChannelId, "ch0", "channel", "Output wavelength channel id."),
            Number(Phase8OpticalParameterKeys.OpticalPowerDbm, 0, "dBm", -200, 200, "Synthetic functional converter output power."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 1, 0, 1_000_000, "E/O conversion latency."),
            Number(Phase8OpticalParameterKeys.ConversionEnergyPicojoulesPerBit, 0.04, "pJ/bit", 0, 1_000_000, "Synthetic functional conversion energy.")
        ]),
        Phase8OpticalTypeIds.OeConverter => AdapterCompatible(
        [
            Number(Phase8OpticalParameterKeys.ReceiverSensitivityDbm, -20, "dBm", -300, 300, "Synthetic functional receiver sensitivity."),
            Number(Phase8OpticalParameterKeys.SignalToNoiseThresholdDb, 10, "dB", -300, 300, "Optional SNR threshold when an SNR model is present."),
            Choice(Phase8OpticalParameterKeys.BerModel, "none", ["none"], "Phase 8 does not predict BER."),
            Choice(Phase8OpticalParameterKeys.OutputPrecision, "int8", ["binary", "int2", "int4", "int8", "int16", "int32", "fp8", "fp16", "fp32"], "Digital output precision."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 1, 0, 1_000_000, "O/E conversion latency."),
            Number(Phase8OpticalParameterKeys.ConversionEnergyPicojoulesPerBit, 0.04, "pJ/bit", 0, 1_000_000, "Synthetic functional conversion energy.")
        ]),
        _ => throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Unknown Phase 8 optical type id.")
    };

    private static IReadOnlyList<ComponentParameterSchema> AdapterCompatible(IReadOnlyList<ComponentParameterSchema> parameters)
    {
        var result = parameters.ToList();
        result.AddRange(AdapterRuntimeMetadata.AdapterParameterSchemas(1, 0.04, contributesOpticalEnergy: false));
        return result;
    }

    private static IReadOnlyList<ComponentParameterSchema> TuningParameters(IReadOnlyList<ComponentParameterSchema> componentParameters)
    {
        var result = componentParameters.ToList();
        result.AddRange(
        [
            Number(Phase8OpticalParameterKeys.NominalResonanceNanometers, 1550, "nm", 1, 1_000_000, "Nominal resonance wavelength."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 1, 0, 1_000_000, "Packet routing latency, independent of tuning state latency."),
            Number(Phase8OpticalParameterKeys.ResonanceOffsetNanometers, 0, "nm", -1_000_000, 1_000_000, "Explicit resonance offset."),
            Number(Phase8OpticalParameterKeys.TuningVoltageVolts, 0, "V", -1_000_000, 1_000_000, "Tuning voltage state."),
            Number(Phase8OpticalParameterKeys.TuningPowerMilliwatts, 0, "mW", 0, 1_000_000, "Static tuning power, never charged per bit."),
            Integer(Phase8OpticalParameterKeys.TuningLatencyCycles, 1, 0, 1_000_000, "Current/next/commit tuning latency."),
            Number(Phase8OpticalParameterKeys.TemperatureCelsius, 25, "degC", -273.15, 10_000, "Current temperature state."),
            Number(Phase8OpticalParameterKeys.ReferenceTemperatureCelsius, 25, "degC", -273.15, 10_000, "Reference temperature."),
            Number(Phase8OpticalParameterKeys.ReferenceVoltageVolts, 0, "V", -1_000_000, 1_000_000, "Reference tuning voltage."),
            Number(Phase8OpticalParameterKeys.ThermalDriftNanometersPerCelsius, 0.01, "nm/degC", -1_000_000, 1_000_000, "Synthetic functional thermal resonance drift."),
            Number(Phase8OpticalParameterKeys.VoltageDriftNanometersPerVolt, 0.1, "nm/V", -1_000_000, 1_000_000, "Synthetic functional voltage resonance drift.")
        ]);
        return result;
    }

    private static IReadOnlyList<ComponentParameterSchema> WdmParameters(bool includeUnmatchedPolicy)
    {
        var result = new List<ComponentParameterSchema>
        {
            Integer(Phase8OpticalParameterKeys.ChannelCount, 4, 1, 4, "Number of physical plugin ports and frozen wavelength channels."),
            Choice(Phase8OpticalParameterKeys.AllocationMode, "fixed", ["fixed", "dynamic"], "Fixed or deterministic dynamic wavelength allocation."),
            Text(Phase8OpticalParameterKeys.ChannelTableNanometers, "1550,1551,1552,1553", "nm", "Comma-separated frozen channel wavelengths."),
            Number(Phase8OpticalParameterKeys.ChannelToleranceNanometers, 0.1, "nm", 0, 1_000_000, "Channel match tolerance."),
            Number(Phase8OpticalParameterKeys.InsertionLossDb, OpticalLossDefaults.CouplerDb, "dB", 0, 100, "Synthetic functional WDM insertion loss."),
            Number(Phase8OpticalParameterKeys.CrosstalkDb, 0, "dB", 0, 300, "Synthetic functional WDM crosstalk penalty."),
            Integer(Phase8OpticalParameterKeys.QueueDepth, 4, 1, 65_536, "Per-channel deterministic queue depth."),
            Integer(Phase8OpticalParameterKeys.LatencyCycles, 1, 0, 1_000_000, "Mux/demux processing latency.")
        };
        if (includeUnmatchedPolicy)
        {
            result.Add(Choice(Phase8OpticalParameterKeys.UnmatchedPolicy, "stall", ["stall", "error"], "Structured behavior for unmatched wavelengths."));
        }

        return result;
    }

    private static IReadOnlyList<ComponentTraceDescriptor> TraceDescriptorsFor(string typeId)
    {
        var stem = typeId[(typeId.LastIndexOf('.') + 1)..];
        var traces = new List<ComponentTraceDescriptor>
        {
            new($"phase8.optical.{stem}.runtime", TraceEventType.OperationComplete, "Deterministic Phase 8 optical runtime transition")
        };
        if (typeId is Phase8OpticalTypeIds.MrrRouter or Phase8OpticalTypeIds.MziSwitch)
            traces.Add(new($"phase8.optical.{stem}.tuning", TraceEventType.Compute, "Current/next/commit optical tuning transition"));
        if (typeId is Phase8OpticalTypeIds.WdmMux or Phase8OpticalTypeIds.WdmDemux)
            traces.Add(new($"phase8.optical.{stem}.arbitration", TraceEventType.Arbitration, "Deterministic wavelength resource arbitration"));
        if (typeId is Phase8OpticalTypeIds.Photodetector or Phase8OpticalTypeIds.OeConverter)
            traces.Add(new($"phase8.optical.{stem}.boundary", TraceEventType.Warning, "Receiver sensitivity, SNR, and BER-model boundary"));
        return traces;
    }

    private static IReadOnlyList<ComponentMetricDescriptor> MetricDescriptorsFor(string typeId)
    {
        var category = typeId is Phase8OpticalTypeIds.EoConverter or Phase8OpticalTypeIds.OeConverter
            ? EnergyCategory.Conversion
            : EnergyCategory.Optical;
        var metrics = new List<ComponentMetricDescriptor>
        {
            new("total_loss_dB", "dB", category, "Additive optical path/device loss"),
            new("min_received_power_dBm", "dBm", category, "Minimum observed optical receive power")
        };
        if (typeId == Phase8OpticalTypeIds.Laser)
            metrics.Add(new("laser_static_energy_pJ", "pJ", EnergyCategory.Optical, "Laser static energy integrated over active cycles"));
        if (typeId is Phase8OpticalTypeIds.MrrRouter or Phase8OpticalTypeIds.MziSwitch)
            metrics.Add(new("tuning_energy_pJ", "pJ", EnergyCategory.Optical, "Static tuning power integrated over active cycles"));
        if (typeId is Phase8OpticalTypeIds.WdmMux or Phase8OpticalTypeIds.WdmDemux)
        {
            metrics.Add(new("channel_utilization", "ratio", EnergyCategory.Optical, "Occupied channel cycles divided by available channel cycles"));
            metrics.Add(new("wavelength_conflicts", "count", EnergyCategory.Optical, "Deterministically arbitrated wavelength conflicts"));
        }
        if (typeId is Phase8OpticalTypeIds.Combiner or Phase8OpticalTypeIds.WdmMux or Phase8OpticalTypeIds.WdmDemux)
            metrics.Add(new("max_crosstalk_dB", "dB", EnergyCategory.Optical, "Maximum accumulated crosstalk"));
        metrics.Add(new(typeId is Phase8OpticalTypeIds.EoConverter or Phase8OpticalTypeIds.OeConverter ? "conversion_energy_pJ" : "optical_dynamic_energy_pJ", "pJ", category, "Exact Phase 8 energy contribution"));
        return metrics;
    }

    private static ComponentPortSchema OpticalIn(string name, bool required = true, PortProtocol protocol = PortProtocol.Packet) =>
        Port(name, PortDirection.Input, SignalType.Optical, HardwareDataType.Packet, PrecisionKind.Any, protocol, required, false, "optical_packet_state", "nm|channel|dBm|dB");

    private static ComponentPortSchema OpticalOut(string name, bool required = true, PortProtocol protocol = PortProtocol.Packet) =>
        Port(name, PortDirection.Output, SignalType.Optical, HardwareDataType.Packet, PrecisionKind.Any, protocol, required, false, "optical_packet_state", "nm|channel|dBm|dB");

    private static ComponentPortSchema AnalogIn(string name, string quantity, string units, bool required = true) =>
        Port(name, PortDirection.Input, SignalType.Analog, HardwareDataType.Scalar, PrecisionKind.Analog, PortProtocol.Streaming, required, false, quantity, units);

    private static ComponentPortSchema AnalogOut(string name, string quantity, string units) =>
        Port(name, PortDirection.Output, SignalType.Analog, HardwareDataType.Scalar, PrecisionKind.Analog, PortProtocol.Streaming, true, false, quantity, units);

    private static ComponentPortSchema DigitalIn(string name) =>
        Port(name, PortDirection.Input, SignalType.Digital, HardwareDataType.Packet, PrecisionKind.Any, PortProtocol.Packet, true, false, "digital_payload", "bit");

    private static ComponentPortSchema DigitalOut(string name) =>
        Port(name, PortDirection.Output, SignalType.Digital, HardwareDataType.Packet, PrecisionKind.Any, PortProtocol.Packet, true, false, "digital_payload", "bit");

    private static ComponentPortSchema ControlIn(string name, string quantity, string units, bool required) =>
        Port(name, PortDirection.Input, SignalType.Control, HardwareDataType.Config, PrecisionKind.Any, PortProtocol.RequestResponse, required, false, quantity, units);

    private static ComponentPortSchema Port(
        string name,
        PortDirection direction,
        SignalType signalType,
        HardwareDataType dataType,
        PrecisionKind precision,
        PortProtocol protocol,
        bool required,
        bool multiConnect,
        string quantity,
        string units) =>
        new(name, direction, signalType, dataType, precision, protocol, ComponentDefaults.LinkBandwidthBitsPerCycle, 0, required, multiConnect, quantity, units);

    private static ComponentParameterSchema Number(string name, double defaultValue, string units, double minimum, double maximum, string description) =>
        new(name, defaultValue.ToString("R", CultureInfo.InvariantCulture), units, minimum, maximum, false, description);

    private static ComponentParameterSchema Integer(string name, int defaultValue, int minimum, int maximum, string description) =>
        new(name, defaultValue.ToString(CultureInfo.InvariantCulture), name.EndsWith("cycles", StringComparison.Ordinal) ? "cycles" : "count", minimum, maximum, false, description, IntegerOnly: true);

    private static ComponentParameterSchema Text(string name, string defaultValue, string units, string description) =>
        new(name, defaultValue, units, Description: description);

    private static ComponentParameterSchema Choice(string name, string defaultValue, IReadOnlyList<string> allowedValues, string description) =>
        new(name, defaultValue, "state", Description: description, AllowedValues: allowedValues);
}

/// <summary>Performs strict Phase 8 optical instance, port, unit, and parameter validation.</summary>
internal sealed class Phase8OpticalValidationProvider : IComponentValidationProvider
{
    public static readonly Phase8OpticalValidationProvider Instance = new();

    public IReadOnlyList<ComponentPluginIssue> Validate(ComponentValidationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        var issues = new List<ComponentPluginIssue>();
        var component = context.Component;
        var legacyImport = component.Type != ComponentKind.Custom;
        var hasExplicitTypeId = !string.IsNullOrWhiteSpace(component.TypeId);
        if ((!legacyImport || hasExplicitTypeId) && !string.Equals(ComponentTypeIds.Normalize(component.TypeId), context.Plugin.TypeId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue("OpticalPluginTypeIdMismatch", ValidationSeverity.Error, "$.type_id", $"Component type id must be '{context.Plugin.TypeId}'.", component.Id));
        }

        if (legacyImport)
        {
            issues.Add(Issue("OpticalLegacyKindImport", ValidationSeverity.Warning, "$.type", "Legacy optical enum identity is accepted for import only; new components use Custom plus stable type_id.", component.Id));
        }
        else
        {
            ValidatePorts(context, issues);
        }

        ValidateParameters(context, issues);
        if (string.Equals(context.Plugin.TypeId, Phase8OpticalTypeIds.Link, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var forbidden in new[] { "length_mm", "route_length_mm", "bend_count", "route_bend_count" })
            {
                if (component.Parameters.ContainsKey(forbidden))
                    issues.Add(Issue("OpticalRouteTruthOverrideForbidden", ValidationSeverity.Error, "$.parameters." + forbidden, "Waveguide length and bend count must come from explicit RoutePath provenance.", component.Id));
            }
        }

        if (context.Plugin.TypeId is Phase8OpticalTypeIds.WdmMux or Phase8OpticalTypeIds.WdmDemux)
            ValidateChannelTable(context, issues);
        if (context.Plugin.TypeId is Phase8OpticalTypeIds.Photodetector or Phase8OpticalTypeIds.OeConverter)
        {
            var berModel = Value(context, Phase8OpticalParameterKeys.BerModel);
            if (string.Equals(berModel, "none", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue(OpticalWarningCodes.BerNotModeled, ValidationSeverity.Warning, "$.parameters.ber_model", "BER not modeled", component.Id));
        }

        return issues;
    }

    private static void ValidatePorts(ComponentValidationContext context, ICollection<ComponentPluginIssue> issues)
    {
        foreach (var expected in context.Plugin.Ports)
        {
            var actual = context.Component.FindPort(expected.Name);
            var location = "$.ports." + expected.Name;
            if (actual is null)
            {
                issues.Add(Issue("OpticalPortMissing", ValidationSeverity.Error, location, $"Required plugin schema port '{expected.Name}' is missing.", context.Component.Id));
                continue;
            }

            if (actual.Direction != expected.Direction || actual.SignalType != expected.SignalType || actual.Protocol != expected.Protocol || actual.DataType != expected.DataType)
                issues.Add(Issue("OpticalPortContractMismatch", ValidationSeverity.Error, location, $"Port '{expected.Name}' direction/domain/protocol/data type differs from the registered plugin schema.", context.Component.Id));
            if (!ExtensionTextEquals(actual, "quantity", expected.Quantity) || !ExtensionTextEquals(actual, "units", expected.Units))
                issues.Add(Issue("OpticalPortUnitsMismatch", ValidationSeverity.Error, location, $"Port '{expected.Name}' quantity/units must be '{expected.Quantity}'/'{expected.Units}'.", context.Component.Id));
        }
    }

    private static void ValidateParameters(ComponentValidationContext context, ICollection<ComponentPluginIssue> issues)
    {
        foreach (var schema in context.Plugin.Parameters)
        {
            var raw = Value(context, schema.Name);
            var location = "$.parameters." + schema.Name;
            if (string.IsNullOrWhiteSpace(raw) && schema.Required)
            {
                issues.Add(Issue("OpticalParameterRequired", ValidationSeverity.Error, location, $"Parameter '{schema.Name}' is required.", context.Component.Id));
                continue;
            }

            if (string.Equals(schema.Name, Phase8OpticalParameterKeys.ChannelId, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(raw))
            {
                issues.Add(Issue("OpticalChannelIdRequired", ValidationSeverity.Error, location, "Optical channel id must be non-empty.", context.Component.Id));
                continue;
            }

            if (schema.AllowedValues is { Count: > 0 } && !schema.AllowedValues.Any(item => string.Equals(item, raw, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue("OpticalParameterValueInvalid", ValidationSeverity.Error, location, $"Parameter '{schema.Name}' must be one of: {string.Join(", ", schema.AllowedValues)}.", context.Component.Id));
                continue;
            }

            if (!schema.Minimum.HasValue && !schema.Maximum.HasValue && !schema.IntegerOnly)
                continue;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            {
                issues.Add(Issue("OpticalParameterNumberInvalid", ValidationSeverity.Error, location, $"Parameter '{schema.Name}' must be a finite invariant number.", context.Component.Id));
                continue;
            }

            if (schema.IntegerOnly && number != Math.Truncate(number))
                issues.Add(Issue("OpticalParameterIntegerRequired", ValidationSeverity.Error, location, $"Parameter '{schema.Name}' must be integral.", context.Component.Id));
            if (schema.Minimum.HasValue && number < schema.Minimum.Value || schema.Maximum.HasValue && number > schema.Maximum.Value)
                issues.Add(Issue("OpticalParameterOutOfRange", ValidationSeverity.Error, location, $"Parameter '{schema.Name}' is outside its inclusive range.", context.Component.Id));
        }
    }

    private static void ValidateChannelTable(ComponentValidationContext context, ICollection<ComponentPluginIssue> issues)
    {
        var raw = Value(context, Phase8OpticalParameterKeys.ChannelTableNanometers);
        var values = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        var valid = values.Length > 0 && values.All(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed > 0);
        var unique = values.Distinct(StringComparer.Ordinal).Count() == values.Length;
        var countRaw = Value(context, Phase8OpticalParameterKeys.ChannelCount);
        var countValid = int.TryParse(countRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count == values.Length;
        if (!valid || !unique || !countValid)
            issues.Add(Issue("OpticalWavelengthTableInvalid", ValidationSeverity.Error, "$.parameters.channel_table_nm", "WDM channel table must contain channel_count unique positive invariant wavelengths.", context.Component.Id));
    }

    private static bool ExtensionTextEquals(HardwarePort port, string key, string expected) =>
        port.ExtensionData.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static string Value(ComponentValidationContext context, string name) =>
        context.Component.Parameters.TryGetValue(name, out var value)
            ? value
            : context.Plugin.Parameters.First(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)).DefaultValue;

    private static ComponentPluginIssue Issue(string code, ValidationSeverity severity, string location, string message, string relatedId) =>
        new(code, severity, location, message, relatedId);
}

/// <summary>Compiles canonical optical parameters and per-quantity provenance without mutating the source graph.</summary>
internal sealed class Phase8OpticalCompileProvider : IComponentCompileProvider
{
    public static readonly Phase8OpticalCompileProvider Instance = new();

    public ComponentCompileProviderResult Compile(ComponentCompileContext context)
    {
        var validationContext = new ComponentValidationContext(context.Plugin, context.Component, context.Graph);
        var parameters = ResolveParameters(context.Plugin, context.Component, includeProvenance: true);
        parameters[Phase8OpticalParameterKeys.ModelVersion] = "8.0.0";
        parameters[Phase8OpticalParameterKeys.TypeId] = context.Plugin.TypeId;
        parameters[ComponentPluginRuntimeKeys.TypeId] = context.Plugin.TypeId;
        if (context.Plugin.LegacyAliases is { Count: > 0 })
            parameters[Phase8OpticalParameterKeys.LegacyDisplayAlias] = string.Join("|", context.Plugin.LegacyAliases);
        return new ComponentCompileProviderResult
        {
            Issues = Phase8OpticalValidationProvider.Instance.Validate(validationContext),
            Parameters = new ReadOnlyDictionary<string, string>(parameters)
        };
    }

    internal static Dictionary<string, string> ResolveParameters(ComponentPluginDescriptor plugin, HardwareComponent component, bool includeProvenance)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in plugin.Parameters)
        {
            var raw = component.Parameters.GetValueOrDefault(schema.Name, schema.DefaultValue);
            result[schema.Name] = Canonicalize(schema, raw);
            if (includeProvenance)
            {
                var overridden = !string.Equals(result[schema.Name], Canonicalize(schema, schema.DefaultValue), StringComparison.OrdinalIgnoreCase);
                result[Phase8OpticalParameterKeys.ProvenancePrefix + schema.Name] = overridden
                    ? OpticalProvenanceSources.PluginInstanceOverride
                    : ContractDefault(plugin.TypeId, schema.Name)
                        ? OpticalProvenanceSources.Phase8ContractDefault
                        : OpticalProvenanceSources.SyntheticFunctionalDefault;
            }
        }

        ApplyFrozenContractMetadata(plugin.TypeId, result);
        var legacyCompatibility = component.Type != ComponentKind.Custom;
        result[Phase8OpticalParameterKeys.LegacyCompatibility] = legacyCompatibility.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        result[ComponentPluginRuntimeKeys.LegacyRuntimeCompatibility] = legacyCompatibility.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        if (plugin.TypeId is Phase8OpticalTypeIds.EoConverter or Phase8OpticalTypeIds.OeConverter)
            result[AdapterRuntimeMetadata.OpticalEnergyContributionKey] = legacyCompatibility ? "true" : "false";

        return result;
    }

    internal static string Canonicalize(ComponentParameterSchema schema, string raw)
    {
        if (schema.IntegerOnly && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer.ToString(CultureInfo.InvariantCulture);
        if ((schema.Minimum.HasValue || schema.Maximum.HasValue) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            return number.ToString("R", CultureInfo.InvariantCulture);
        return raw.Trim();
    }

    private static bool ContractDefault(string typeId, string key) =>
        typeId == Phase8OpticalTypeIds.Link && key == Phase8OpticalParameterKeys.WaveguideMaterial ||
        typeId == Phase8OpticalTypeIds.Splitter && key == Phase8OpticalParameterKeys.BranchLossDb ||
        typeId == Phase8OpticalTypeIds.MrrRouter && key == Phase8OpticalParameterKeys.InsertionLossDb ||
        typeId == Phase8OpticalTypeIds.MziSwitch && key == Phase8OpticalParameterKeys.InsertionLossDb;
    private static void ApplyFrozenContractMetadata(string typeId, IDictionary<string, string> parameters)
    {
        if (typeId != Phase8OpticalTypeIds.Link)
            return;

        var propagationLoss = parameters.TryGetValue(Phase8OpticalParameterKeys.WaveguideMaterial, out var material) &&
            string.Equals(material, "silicon", StringComparison.OrdinalIgnoreCase)
                ? OpticalLossDefaults.SiliconDbPerMillimeter
                : OpticalLossDefaults.SiliconNitrideDbPerMillimeter;
        Add(Phase8OpticalParameterKeys.PropagationLossDbPerMillimeter, propagationLoss);
        Add(Phase8OpticalParameterKeys.BendLossDb, OpticalLossDefaults.NinetyDegreeBendDb);
        Add(Phase8OpticalParameterKeys.CrossingLossDb, OpticalLossDefaults.CrossingDb);
        Add(Phase8OpticalParameterKeys.CouplerLossDb, OpticalLossDefaults.CouplerDb);

        void Add(string key, double value)
        {
            parameters[key] = value.ToString("R", CultureInfo.InvariantCulture);
            parameters[Phase8OpticalParameterKeys.ProvenancePrefix + key] = OpticalProvenanceSources.Phase8ContractDefault;
        }
    }


}
/// <summary>Exports exact kernel identity and canonical optical configuration into compiled runtime metadata.</summary>
internal sealed class Phase8OpticalSimulationRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly Phase8OpticalSimulationRuntimeFactory Instance = new();

    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context)
    {
        var source = new HardwareComponent
        {
            Id = context.Component.Id,
            Type = context.Component.Type,
            TypeId = context.Component.TypeId,
            Parameters = new Dictionary<string, string>(context.Component.Parameters, StringComparer.OrdinalIgnoreCase)
        };
        var parameters = Phase8OpticalCompileProvider.ResolveParameters(context.Plugin, source, includeProvenance: true);
        parameters[Phase8OpticalParameterKeys.ModelVersion] = "8.0.0";
        parameters[Phase8OpticalParameterKeys.TypeId] = context.Plugin.TypeId;
        parameters[ComponentPluginRuntimeKeys.TypeId] = context.Plugin.TypeId;
        parameters[Phase8OpticalParameterKeys.ClockPeriodPicoseconds] = context.Graph.SimulationConfig.Clock.ClockPeriodPs.ToString("R", CultureInfo.InvariantCulture);
        var kernelFactory = Phase8OpticalRuntimeKernelFactory.For(context.Plugin.TypeId);
        var descriptor = kernelFactory.Descriptor;
        var latencyKey = context.Plugin.TypeId switch
        {
            Phase8OpticalTypeIds.Laser => Phase8OpticalParameterKeys.StartupLatencyCycles,
            _ => Phase8OpticalParameterKeys.LatencyCycles
        };
        var latency = parameters.TryGetValue(latencyKey, out var rawLatency) && int.TryParse(rawLatency, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLatency)
            ? Math.Max(0, parsedLatency)
            : 0;
        return new ComponentSimulationRuntimeDescriptor
        {
            ProcessingLatencyCycles = latency,
            EnergyCategory = context.Plugin.MetricDescriptors.FirstOrDefault()?.Category ?? EnergyCategory.Optical,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = new ReadOnlyDictionary<string, string>(parameters),
            KernelId = descriptor.KernelId,
            KernelVersion = descriptor.KernelVersion,
            ContractSchemaId = descriptor.ContractSchemaId,
            CanonicalKernelConfiguration = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(
                parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                HardwareGraphJson.Options)),
            KernelImplementationHash = descriptor.ImplementationHash
        };
    }
}
