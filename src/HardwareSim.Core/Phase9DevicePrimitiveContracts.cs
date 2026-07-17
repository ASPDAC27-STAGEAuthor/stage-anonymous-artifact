using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

#pragma warning disable CS1591

namespace HardwareSim.Core;

/// <summary>Stable Phase 9 component parameter keys shared by authoring, compilation, runtime, and replay.</summary>
public static class Phase9DeviceRuntimeKeys
{
    public const string ProfileId = "device_profile_id";
    public const string ProfileHash = "device_profile_hash";
    public const string DeviceFamily = "device_family";
    public const string OperatingCorner = "device_operating_corner";
    public const string CapabilityHash = "device_capability_hash";
    public const string Rows = "array_rows";
    public const string Columns = "array_columns";
    public const string CellBits = "cell_bits";
    public const string Banks = "banks";
    public const string PrecisionBits = "precision_bits";
    public const string SupportedPrecisions = "supported_precisions";
    public const string SupportedOperations = "supported_operations";
    public const string SampleRateHz = "sample_rate_hz";
    public const string ThroughputPerCycle = "throughput_per_cycle";
    public const string VoltageMinimum = "voltage_min_v";
    public const string VoltageMaximum = "voltage_max_v";
    public const string CurrentMinimum = "current_min_a";
    public const string CurrentMaximum = "current_max_a";
}

/// <summary>Open capability identifiers; device families do not require a central enum.</summary>
public static class Phase9DeviceCapabilityIds
{
    public const string ArrayMvm = "cim.array.mvm";
    public const string ArrayRead = "cim.array.read";
    public const string ArrayWrite = "cim.array.write";
    public const string ArrayCalibration = "cim.array.calibration";
    public const string AnalogToDigital = "conversion.analog_to_digital";
    public const string DigitalToAnalog = "conversion.digital_to_analog";
    public const string Accumulation = "cim.analog.accumulation";
    public const string Sense = "cim.array.sense";
    public const string WriteDrive = "cim.array.write_drive";
    public const string Decode = "cim.array.decode";
    public const string Schedule = "cim.array.schedule";
    public const string Storage = "cim.array.storage";
}

/// <summary>One finite numeric constraint in a primitive contract.</summary>
public sealed record Phase9NumericConstraint(
    string ParameterName,
    double? Minimum = null,
    double? Maximum = null,
    bool IntegerOnly = false,
    string Units = "");

/// <summary>One open capability advertised by a device primitive.</summary>
public sealed record Phase9CapabilityDescriptor(
    string CapabilityId,
    IReadOnlyList<string> SupportedOperations,
    IReadOnlyList<int> SupportedPrecisions,
    IReadOnlyList<Phase9NumericConstraint> Constraints,
    IReadOnlyList<string> RequiredAdjacentCapabilities,
    string Description = "");

/// <summary>Schema consumed by the PluginManager, Phase 7C primitive view, compiler, and runtime registry.</summary>
public sealed record Phase9PrimitiveContract(
    string SchemaVersion,
    string PrimitiveId,
    string DeviceFamily,
    IReadOnlyList<Phase9CapabilityDescriptor> Capabilities,
    IReadOnlyList<ComponentPortSchema> Ports,
    IReadOnlyList<ComponentParameterSchema> Parameters,
    IReadOnlyList<string> SupportedOperatingCorners,
    string ContractHash)
{
    public const string CurrentSchemaVersion = "phase9-device-primitive-1.0";

    public static Phase9PrimitiveContract Create(
        string primitiveId,
        string deviceFamily,
        IReadOnlyList<Phase9CapabilityDescriptor> capabilities,
        IReadOnlyList<ComponentPortSchema> ports,
        IReadOnlyList<ComponentParameterSchema> parameters,
        IReadOnlyList<string>? supportedOperatingCorners = null)
    {
        var corners = (supportedOperatingCorners ?? ["nominal"]).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var hash = ComponentTemplateJson.StableHash(new
        {
            SchemaVersion = CurrentSchemaVersion,
            PrimitiveId = primitiveId.Trim(),
            DeviceFamily = deviceFamily.Trim(),
            Capabilities = capabilities.OrderBy(value => value.CapabilityId, StringComparer.Ordinal).Select(value => new
            {
                value.CapabilityId,
                SupportedOperations = value.SupportedOperations.OrderBy(item => item, StringComparer.Ordinal),
                SupportedPrecisions = value.SupportedPrecisions.OrderBy(item => item),
                Constraints = value.Constraints.OrderBy(item => item.ParameterName, StringComparer.Ordinal),
                RequiredAdjacentCapabilities = value.RequiredAdjacentCapabilities.OrderBy(item => item, StringComparer.Ordinal)
            }),
            Ports = ports.OrderBy(value => value.Name, StringComparer.Ordinal),
            Parameters = parameters.OrderBy(value => value.Name, StringComparer.Ordinal),
            SupportedOperatingCorners = corners
        });
        return new(CurrentSchemaVersion, primitiveId.Trim(), deviceFamily.Trim(), capabilities, ports, parameters, corners, hash);
    }
}

/// <summary>Structured Phase 9 primitive validation issue.</summary>
public sealed record Phase9PrimitiveIssue(string Code, ValidationSeverity Severity, string Path, string Message);

/// <summary>Validation result that keeps unrelated sparse capabilities usable.</summary>
public sealed class Phase9PrimitiveValidationResult
{
    public List<Phase9PrimitiveIssue> Issues { get; } = [];
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);
}

/// <summary>Validates open primitive contracts and concrete component instances without family switches.</summary>
public static class Phase9PrimitiveContractValidator
{
    public static Phase9PrimitiveValidationResult ValidateContract(Phase9PrimitiveContract contract)
    {
        var result = new Phase9PrimitiveValidationResult();
        if (contract.SchemaVersion != Phase9PrimitiveContract.CurrentSchemaVersion)
            Error(result, "PrimitiveSchemaVersionUnsupported", "$.schema_version", "Unsupported Phase 9 primitive schema version.");
        if (string.IsNullOrWhiteSpace(contract.PrimitiveId)) Error(result, "PrimitiveIdRequired", "$.primitive_id", "Primitive id is required.");
        if (string.IsNullOrWhiteSpace(contract.DeviceFamily)) Error(result, "DeviceFamilyRequired", "$.device_family", "Open device-family text is required.");
        Duplicate(result, contract.Ports.Select(port => port.Name), "PrimitivePortDuplicate", "$.ports");
        Duplicate(result, contract.Parameters.Select(parameter => parameter.Name), "PrimitiveParameterDuplicate", "$.parameters");
        Duplicate(result, contract.Capabilities.Select(capability => capability.CapabilityId), "PrimitiveCapabilityDuplicate", "$.capabilities");
        foreach (var capability in contract.Capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.CapabilityId)) Error(result, "CapabilityIdRequired", "$.capabilities", "Capability id is required.");
            foreach (var constraint in capability.Constraints)
            {
                if (constraint.Minimum.HasValue && constraint.Maximum.HasValue && constraint.Minimum > constraint.Maximum)
                    Error(result, "CapabilityRangeInvalid", $"$.capabilities[{capability.CapabilityId}].constraints[{constraint.ParameterName}]", "Minimum exceeds maximum.");
                if (!contract.Parameters.Any(parameter => string.Equals(parameter.Name, constraint.ParameterName, StringComparison.OrdinalIgnoreCase)))
                    Error(result, "CapabilityParameterMissing", $"$.capabilities[{capability.CapabilityId}]", $"Constraint references undeclared parameter '{constraint.ParameterName}'.");
            }
        }
        var expected = ComponentTemplateJson.StableHash(new
        {
            contract.SchemaVersion,
            contract.PrimitiveId,
            contract.DeviceFamily,
            Capabilities = contract.Capabilities.OrderBy(value => value.CapabilityId, StringComparer.Ordinal).Select(value => new
            {
                value.CapabilityId,
                SupportedOperations = value.SupportedOperations.OrderBy(item => item, StringComparer.Ordinal),
                SupportedPrecisions = value.SupportedPrecisions.OrderBy(item => item),
                Constraints = value.Constraints.OrderBy(item => item.ParameterName, StringComparer.Ordinal),
                RequiredAdjacentCapabilities = value.RequiredAdjacentCapabilities.OrderBy(item => item, StringComparer.Ordinal)
            }),
            Ports = contract.Ports.OrderBy(value => value.Name, StringComparer.Ordinal),
            Parameters = contract.Parameters.OrderBy(value => value.Name, StringComparer.Ordinal),
            SupportedOperatingCorners = contract.SupportedOperatingCorners.OrderBy(value => value, StringComparer.Ordinal)
        });
        if (!string.Equals(expected, contract.ContractHash, StringComparison.Ordinal)) Error(result, "PrimitiveContractHashMismatch", "$.contract_hash", "Primitive contract hash does not match semantic content.");
        return result;
    }

    public static Phase9PrimitiveValidationResult ValidateInstance(Phase9PrimitiveContract contract, HardwareComponent component)
    {
        var result = ValidateContract(contract);
        foreach (var port in contract.Ports)
        {
            var actual = component.Ports.FirstOrDefault(candidate => string.Equals(candidate.Name, port.Name, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
            {
                if (port.Required) Error(result, "PrimitiveRequiredPortMissing", $"$.ports[{port.Name}]", $"Required port '{port.Name}' is missing.");
                continue;
            }
            if (actual.Direction != port.Direction || actual.SignalType != port.SignalType || actual.Protocol != port.Protocol)
                Error(result, "PrimitivePortContractMismatch", $"$.ports[{port.Name}]", $"Port '{port.Name}' does not match direction/domain/protocol contract.");
        }
        foreach (var capability in contract.Capabilities)
        foreach (var constraint in capability.Constraints)
        {
            if (!component.Parameters.TryGetValue(constraint.ParameterName, out var raw) || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            {
                Error(result, "PrimitiveParameterInvalid", $"$.parameters[{constraint.ParameterName}]", $"Parameter '{constraint.ParameterName}' must be a finite number.");
                continue;
            }
            if (constraint.IntegerOnly && value != Math.Truncate(value)) Error(result, "PrimitiveParameterNotInteger", $"$.parameters[{constraint.ParameterName}]", $"Parameter '{constraint.ParameterName}' must be integral.");
            if (constraint.Minimum.HasValue && value < constraint.Minimum || constraint.Maximum.HasValue && value > constraint.Maximum)
                Error(result, "PrimitiveParameterOutOfRange", $"$.parameters[{constraint.ParameterName}]", $"Parameter '{constraint.ParameterName}' is outside [{constraint.Minimum}, {constraint.Maximum}].");
        }
        var rows = Int(component, Phase9DeviceRuntimeKeys.Rows);
        var columns = Int(component, Phase9DeviceRuntimeKeys.Columns);
        var cellBits = Int(component, Phase9DeviceRuntimeKeys.CellBits);
        var banks = Math.Max(1, Int(component, Phase9DeviceRuntimeKeys.Banks, 1));
        if (rows > 0 && columns > 0 && cellBits > 0)
        {
            try { _ = checked((long)rows * columns * cellBits * banks); }
            catch (OverflowException) { Error(result, "PrimitiveCapacityOverflow", "$.parameters", "Array capacity exceeds Int64."); }
        }
        ValidateRange(component, result, Phase9DeviceRuntimeKeys.VoltageMinimum, Phase9DeviceRuntimeKeys.VoltageMaximum, "PrimitiveVoltageRangeInvalid");
        ValidateRange(component, result, Phase9DeviceRuntimeKeys.CurrentMinimum, Phase9DeviceRuntimeKeys.CurrentMaximum, "PrimitiveCurrentRangeInvalid");
        return result;
    }

    private static void ValidateRange(HardwareComponent component, Phase9PrimitiveValidationResult result, string minimumKey, string maximumKey, string code)
    {
        if (!component.Parameters.TryGetValue(minimumKey, out var minimumText) || !component.Parameters.TryGetValue(maximumKey, out var maximumText)) return;
        if (!double.TryParse(minimumText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) || !double.TryParse(maximumText, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum)
            Error(result, code, "$.parameters", $"'{minimumKey}' must be finite and lower than '{maximumKey}'.");
    }

    private static int Int(HardwareComponent component, string key, int fallback = 0) => component.Parameters.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static void Duplicate(Phase9PrimitiveValidationResult result, IEnumerable<string> values, string code, string path)
    {
        foreach (var duplicate in values.Where(value => !string.IsNullOrWhiteSpace(value)).GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) Error(result, code, path, $"Duplicate id '{duplicate.Key}'.");
    }
    private static void Error(Phase9PrimitiveValidationResult result, string code, string path, string message) => result.Issues.Add(new(code, ValidationSeverity.Error, path, message));
}

/// <summary>Binds sparse normalized profiles to a primitive and reports only requested capability gaps.</summary>
public static class Phase9PrimitiveProfileBinder
{
    public static Phase9PrimitiveValidationResult ValidateBinding(Phase9PrimitiveContract contract, NormalizedDeviceProfile profile, IEnumerable<string> requestedCapabilities)
    {
        var result = new Phase9PrimitiveValidationResult();
        if (!string.Equals(contract.DeviceFamily, profile.DeviceFamily, StringComparison.OrdinalIgnoreCase) && !profile.DeviceFamily.Contains(contract.DeviceFamily, StringComparison.OrdinalIgnoreCase) && !contract.DeviceFamily.Contains(profile.DeviceFamily, StringComparison.OrdinalIgnoreCase))
            result.Issues.Add(new("DeviceFamilyMismatch", ValidationSeverity.Error, "$.device_profile_id", $"Profile family '{profile.DeviceFamily}' does not match primitive family '{contract.DeviceFamily}'."));
        if (contract.SupportedOperatingCorners.Count > 0 && !contract.SupportedOperatingCorners.Contains("any", StringComparer.OrdinalIgnoreCase) && !contract.SupportedOperatingCorners.Contains(profile.OperatingCorner, StringComparer.OrdinalIgnoreCase))
            result.Issues.Add(new("DeviceOperatingCornerUnsupported", ValidationSeverity.Error, "$.device_operating_corner", $"Operating corner '{profile.OperatingCorner}' is not supported."));
        foreach (var capability in requestedCapabilities.Distinct(StringComparer.Ordinal))
        {
            var resolution = profile.ResolveCapability(capability);
            if (resolution.Availability == NormalizedDeviceAvailability.Unknown)
                result.Issues.Add(new("DeviceCapabilityUnknown", ValidationSeverity.Error, resolution.Issue?.Path ?? "$.capabilities", resolution.Issue?.Message ?? $"Capability '{capability}' is unknown."));
        }
        return result;
    }
}

/// <summary>First-party open CIM/analog primitive package.</summary>
public static class Phase9CimPrimitivePlugins
{
    public const string SramArrayTypeId = "com.hardware-sim.first-party.cim.sram-array";
    public const string DecoderTypeId = "com.hardware-sim.first-party.cim.row-column-decoder";
    public const string ControllerTypeId = "com.hardware-sim.first-party.cim.array-controller";

    public static IReadOnlyList<ComponentPluginDescriptor> Descriptors() =>
    [
        Array(ComponentTypeIds.BuiltIn(ComponentKind.ReRamCrossbar), "ReRAM Array", "ReRAM", ComponentKind.ReRamCrossbar, 400, 4) with { RuntimeKernelFactory = Phase9CimVmmKernelFactory.Instance, KernelTestScenarioProvider = Phase9CimVmmScenarioProvider.Instance },
        Array(ComponentTypeIds.BuiltIn(ComponentKind.FeFetCrossbar), "FeFET Array", "FeFET", ComponentKind.FeFetCrossbar, 401, 2),
        Array(SramArrayTypeId, "SRAM/GainCell Array", "SRAM/GainCell", null, 402, 1),
        Converter(ComponentTypeIds.BuiltIn(ComponentKind.Adc), "ADC", "ADC", ComponentKind.Adc, 403, true),
        Converter(ComponentTypeIds.BuiltIn(ComponentKind.Dac), "DAC", "DAC", ComponentKind.Dac, 404, false),
        Simple(ComponentTypeIds.BuiltIn(ComponentKind.AnalogAccumulator), "Analog Accumulator", "AnalogAccumulator", ComponentKind.AnalogAccumulator, 405, Phase9DeviceCapabilityIds.Accumulation, AnalogIn("analog_in"), AnalogOut("analog_out")),
        Simple(ComponentTypeIds.BuiltIn(ComponentKind.SenseAmplifier), "Sense Amplifier", "SenseAmplifier", ComponentKind.SenseAmplifier, 406, Phase9DeviceCapabilityIds.Sense, AnalogIn("bitline_in"), DigitalOut("sense_out")),
        Simple(ComponentTypeIds.BuiltIn(ComponentKind.WriteDriver), "Write Driver", "WriteDriver", ComponentKind.WriteDriver, 407, Phase9DeviceCapabilityIds.WriteDrive, DigitalIn("write_data"), AnalogOut("bitline_drive")),
        Simple(DecoderTypeId, "Row/Column Decoder", "Decoder", null, 408, Phase9DeviceCapabilityIds.Decode, AddressIn("address"), ControlOut("wordline")),
        Simple(ControllerTypeId, "Array Controller", "ArrayController", null, 409, Phase9DeviceCapabilityIds.Schedule, ControlIn("command"), ControlOut("schedule"))
    ];

    private static ComponentPluginDescriptor Array(string typeId, string name, string family, ComponentKind? legacyKind, int order, int defaultCellBits)
    {
        var ports = new[] { AnalogIn("activation_in"), AddressIn("address"), DigitalIn("write_data"), ControlIn("command"), AnalogOut("column_current"), ControlOut("status") };
        var parameters = new[]
        {
            Integer(Phase9DeviceRuntimeKeys.Rows, "32", 1, 65536), Integer(Phase9DeviceRuntimeKeys.Columns, "32", 1, 65536),
            Integer(Phase9DeviceRuntimeKeys.CellBits, defaultCellBits.ToString(CultureInfo.InvariantCulture), 1, 16), Integer(Phase9DeviceRuntimeKeys.Banks, "1", 1, 1024),
            Text(Phase9DeviceRuntimeKeys.SupportedOperations, "mvm,read,write,calibration"), Text(Phase9DeviceRuntimeKeys.ProfileId, ""), Text(Phase9DeviceRuntimeKeys.ProfileHash, "")
        };
        var constraints = new[]
        {
            new Phase9NumericConstraint(Phase9DeviceRuntimeKeys.Rows, 1, 65536, true, "row"), new Phase9NumericConstraint(Phase9DeviceRuntimeKeys.Columns, 1, 65536, true, "column"),
            new Phase9NumericConstraint(Phase9DeviceRuntimeKeys.CellBits, 1, 16, true, "bit/cell"), new Phase9NumericConstraint(Phase9DeviceRuntimeKeys.Banks, 1, 1024, true, "bank")
        };
        var capabilities = new[]
        {
            Capability(Phase9DeviceCapabilityIds.ArrayMvm, ["mvm"], [1, 2, 4, 6, 8], constraints, [Phase9DeviceCapabilityIds.DigitalToAnalog, Phase9DeviceCapabilityIds.AnalogToDigital]),
            Capability(Phase9DeviceCapabilityIds.ArrayRead, ["read"], [1, 2, 4, 8], constraints),
            Capability(Phase9DeviceCapabilityIds.ArrayWrite, ["write"], [1, 2, 4, 8], constraints, [Phase9DeviceCapabilityIds.WriteDrive]),
            Capability(Phase9DeviceCapabilityIds.ArrayCalibration, ["calibration"], [], constraints),
            Capability(Phase9DeviceCapabilityIds.Storage, ["read", "write"], [1, 2, 4, 8], constraints)
        };
        return Descriptor(typeId, name, family, legacyKind, order, ports, parameters, capabilities, "array");
    }

    private static ComponentPluginDescriptor Converter(string typeId, string name, string family, ComponentKind legacyKind, int order, bool adc)
    {
        IReadOnlyList<ComponentPortSchema> ports = adc
            ? [AnalogIn("analog_in"), ControlIn("sample"), DigitalOut("digital_out")]
            : [DigitalIn("digital_in"), ControlIn("sample"), AnalogOut("analog_out")];
        var parameters = new[]
        {
            Integer(Phase9DeviceRuntimeKeys.PrecisionBits, "8", 1, 32), Text(Phase9DeviceRuntimeKeys.SupportedPrecisions, "4,6,8,10,12"),
            Number(Phase9DeviceRuntimeKeys.SampleRateHz, "1000000000", "Hz", 1, 1e15), Number(Phase9DeviceRuntimeKeys.ThroughputPerCycle, "1", "sample/cycle", 1e-9, 1e9),
            Number(Phase9DeviceRuntimeKeys.VoltageMinimum, "0", "V", -1e6, 1e6), Number(Phase9DeviceRuntimeKeys.VoltageMaximum, "1", "V", -1e6, 1e6),
            Text(Phase9DeviceRuntimeKeys.ProfileId, ""), Text(Phase9DeviceRuntimeKeys.ProfileHash, "")
        };
        var capabilityId = adc ? Phase9DeviceCapabilityIds.AnalogToDigital : Phase9DeviceCapabilityIds.DigitalToAnalog;
        IReadOnlyList<string> adjacent = [Phase9DeviceCapabilityIds.ArrayMvm];
        var capabilities = new[] { Capability(capabilityId, [adc ? "convert_adc" : "convert_dac"], [4, 6, 8, 10, 12],
            [new(Phase9DeviceRuntimeKeys.PrecisionBits, 1, 32, true, "bit"), new(Phase9DeviceRuntimeKeys.SampleRateHz, 1, 1e15, false, "Hz"), new(Phase9DeviceRuntimeKeys.ThroughputPerCycle, 1e-9, 1e9, false, "sample/cycle"), new(Phase9DeviceRuntimeKeys.VoltageMinimum, -1e6, 1e6, false, "V"), new(Phase9DeviceRuntimeKeys.VoltageMaximum, -1e6, 1e6, false, "V")], adjacent) };
        return Descriptor(typeId, name, family, legacyKind, order, ports, parameters, capabilities, adc ? "adc" : "dac");
    }

    private static ComponentPluginDescriptor Simple(string typeId, string name, string family, ComponentKind? legacyKind, int order, string capabilityId, ComponentPortSchema input, ComponentPortSchema output)
    {
        var ports = new[] { input, ControlIn("enable"), output };
        var parameters = new[] { Text(Phase9DeviceRuntimeKeys.ProfileId, ""), Text(Phase9DeviceRuntimeKeys.ProfileHash, "") };
        return Descriptor(typeId, name, family, legacyKind, order, ports, parameters, [Capability(capabilityId, [capabilityId], [], [])], family.ToLowerInvariant());
    }

    private static ComponentPluginDescriptor Descriptor(string typeId, string name, string family, ComponentKind? legacyKind, int order, IReadOnlyList<ComponentPortSchema> ports, IReadOnlyList<ComponentParameterSchema> parameters, IReadOnlyList<Phase9CapabilityDescriptor> capabilities, string glyph)
    {
        var contract = Phase9PrimitiveContract.Create(typeId + ".primitive", family, capabilities, ports, parameters, ["nominal", "any"]);
        return new ComponentPluginDescriptor(typeId, name, "CIM", "9.0.0", ports, parameters,
            new ValidationProvider(contract, typeId), new CompileProvider(contract, typeId), new RuntimeFactory(contract),
            [new(typeId + ".operation", TraceEventType.Compute, name + " operation")],
            [new(typeId + ".energy", "pJ", family is "ADC" or "DAC" ? EnergyCategory.Conversion : EnergyCategory.Cim, name + " energy")],
            new ComponentTemplatePrimitiveDescriptor(contract.PrimitiveId, name, "CIM", ports, parameters),
            new CompiledProfileFactoryDescriptor(typeId + ".profile-factory", "phase9-device-profile", "1.0.0", "Compiles sparse device capability bindings."),
            new UnityPresentationDescriptor(glyph, "#8A3800", name.Length <= 4 ? name : name.Substring(0, 4).ToUpperInvariant(), "Phase 9 profile-backed primitive", order),
            ComponentPluginSourceKind.FirstParty, legacyKind, true, null, null, null, contract);
    }

    private static Phase9CapabilityDescriptor Capability(string id, IReadOnlyList<string> operations, IReadOnlyList<int> precisions, IReadOnlyList<Phase9NumericConstraint> constraints, IReadOnlyList<string>? adjacent = null) =>
        new(id, operations, precisions, constraints, adjacent ?? [], id);
    private static ComponentParameterSchema Integer(string name, string value, double min, double max) => new(name, value, "count", min, max, true, IntegerOnly: true);
    private static ComponentParameterSchema Number(string name, string value, string units, double min, double max) => new(name, value, units, min, max, true);
    private static ComponentParameterSchema Text(string name, string value) => new(name, value);
    private static ComponentPortSchema AnalogIn(string name) => new(name, PortDirection.Input, SignalType.Analog, HardwareDataType.Scalar, PrecisionKind.Analog, PortProtocol.Streaming, Required: true, Quantity: "analog_value", Units: "V|A");
    private static ComponentPortSchema AnalogOut(string name) => new(name, PortDirection.Output, SignalType.Analog, HardwareDataType.Scalar, PrecisionKind.Analog, PortProtocol.Streaming, Required: true, Quantity: "analog_value", Units: "V|A");
    private static ComponentPortSchema DigitalIn(string name) => new(name, PortDirection.Input, SignalType.Digital, HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Streaming, Required: true, Quantity: "digital_code", Units: "bit");
    private static ComponentPortSchema DigitalOut(string name) => new(name, PortDirection.Output, SignalType.Digital, HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Streaming, Required: true, Quantity: "digital_code", Units: "bit");
    private static ComponentPortSchema ControlIn(string name) => new(name, PortDirection.Input, SignalType.Control, HardwareDataType.Config, PrecisionKind.Any, PortProtocol.RequestResponse, Required: false, Quantity: "control", Units: "");
    private static ComponentPortSchema ControlOut(string name) => new(name, PortDirection.Output, SignalType.Control, HardwareDataType.Status, PrecisionKind.Any, PortProtocol.RequestResponse, Required: false, Quantity: "control", Units: "");
    private static ComponentPortSchema AddressIn(string name) => new(name, PortDirection.Input, SignalType.Address, HardwareDataType.Config, PrecisionKind.Any, PortProtocol.MemoryMapped, Required: true, Quantity: "address", Units: "bit_address");

    private static bool IsExplicitPhase9Instance(HardwareComponent component, string typeId) =>
        !string.IsNullOrWhiteSpace(component.TypeId) &&
        string.Equals(ComponentTypeIds.Normalize(component.TypeId), ComponentTypeIds.Normalize(typeId), StringComparison.OrdinalIgnoreCase);

    private sealed class ValidationProvider(Phase9PrimitiveContract contract, string typeId) : IComponentValidationProvider
    {
        public IReadOnlyList<ComponentPluginIssue> Validate(ComponentValidationContext context) =>
            IsExplicitPhase9Instance(context.Component, typeId)
                ? Phase9PrimitiveContractValidator.ValidateInstance(contract, context.Component).Issues
                    .Select(issue => new ComponentPluginIssue(issue.Code, issue.Severity, issue.Path, issue.Message, context.Plugin.TypeId)).ToArray()
                : [];
    }
    private sealed class CompileProvider(Phase9PrimitiveContract contract, string typeId) : IComponentCompileProvider
    {
        public ComponentCompileProviderResult Compile(ComponentCompileContext context)
        {
            if (!IsExplicitPhase9Instance(context.Component, typeId))
                return new ComponentCompileProviderResult();
            var validation = Phase9PrimitiveContractValidator.ValidateInstance(contract, context.Component);
            return new ComponentCompileProviderResult
            {
                Issues = validation.Issues.Select(issue => new ComponentPluginIssue(issue.Code, issue.Severity, issue.Path, issue.Message, context.Plugin.TypeId)).ToArray(),
                Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [Phase9DeviceRuntimeKeys.DeviceFamily] = contract.DeviceFamily,
                    [Phase9DeviceRuntimeKeys.CapabilityHash] = contract.ContractHash,
                    [Phase9DeviceRuntimeKeys.SupportedOperations] = string.Join(",", contract.Capabilities.SelectMany(value => value.SupportedOperations).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                })
            };
        }
    }
    private sealed class RuntimeFactory(Phase9PrimitiveContract contract) : IComponentSimulationRuntimeFactory
    {
        public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context) => new()
        {
            ProcessingLatencyCycles = Math.Max(0, context.Component.GetIntParameter("processing_latency_cycles", 1)),
            EnergyCategory = context.Plugin.MetricDescriptors.FirstOrDefault()?.Category ?? EnergyCategory.Cim,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Phase9DeviceRuntimeKeys.DeviceFamily] = contract.DeviceFamily,
                [Phase9DeviceRuntimeKeys.CapabilityHash] = contract.ContractHash,
                [ComponentPluginRuntimeKeys.LegacyRuntimeCompatibility] = "true"
            })
        };
    }
}
