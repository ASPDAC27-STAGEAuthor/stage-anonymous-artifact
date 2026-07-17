using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

/// <summary>Stable issue codes emitted by the component execution framework.</summary>
public static class ComponentExecutionIssueCodes
{
    /// <summary>Indicates that an execution contract references an unavailable kernel id.</summary>
    public const string KernelMissing = "ComponentRuntimeKernelMissing";
    /// <summary>Indicates that more than one plugin registered the same exact kernel identity.</summary>
    public const string KernelDuplicate = "ComponentRuntimeKernelDuplicate";
    /// <summary>Indicates that a kernel version, schema, or provider identity is incompatible.</summary>
    public const string KernelIncompatible = "ComponentRuntimeKernelIncompatible";
    /// <summary>Indicates that a kernel descriptor or canonical configuration is invalid.</summary>
    public const string KernelConfigurationInvalid = "ComponentRuntimeKernelConfigurationInvalid";
    /// <summary>Indicates that an exact kernel has no deterministic component-test scenario provider.</summary>
    public const string KernelTestScenarioMissing = "ComponentKernelTestScenarioMissing";
    /// <summary>Indicates that a deterministic component-test scenario is malformed.</summary>
    public const string KernelTestScenarioInvalid = "ComponentKernelTestScenarioInvalid";
    /// <summary>Indicates that the production cycle engine could not complete a component-test scenario.</summary>
    public const string KernelTestRuntimeFailed = "ComponentKernelTestRuntimeFailed";
    /// <summary>Indicates that a component-test output differs from its independent reference.</summary>
    public const string KernelTestOutputMismatch = "ComponentKernelTestOutputMismatch";
    /// <summary>Indicates that observed component timing differs from the compiled timing contract.</summary>
    public const string KernelTestTimingMismatch = "ComponentKernelTestTimingMismatch";
    /// <summary>Indicates that a PE runtime input contains an unsupported non-finite value.</summary>
    public const string PeNumericInputInvalid = "TemplatePeNumericInputInvalid";
    /// <summary>Indicates that a runtime input payload does not match its compiled port shape.</summary>
    public const string KernelInputShapeMismatch = "ComponentRuntimeKernelInputShapeMismatch";
}

/// <summary>Selects a registered runtime kernel and maps resolved template values into its configuration.</summary>
public sealed class ComponentTemplateExecutionBinding
{
    /// <summary>Gets or sets the stable kernel id selected by the template.</summary>
    [JsonPropertyName("kernel_id")]
    public string KernelId { get; set; } = "";
    /// <summary>Gets or sets the design-time version requirement resolved to an exact version at compile time.</summary>
    [JsonPropertyName("kernel_version_requirement")]
    public string KernelVersionRequirement { get; set; } = "";
    /// <summary>Gets or sets the versioned kernel configuration schema id.</summary>
    [JsonPropertyName("contract_schema_id")]
    public string ContractSchemaId { get; set; } = "";
    /// <summary>Gets or sets the stable operation kind used by trace and test tooling.</summary>
    [JsonPropertyName("operation_kind")]
    public string OperationKind { get; set; } = "";
    /// <summary>Gets or sets canonical configuration-field to resolved-value bindings.</summary>
    [JsonPropertyName("configuration_bindings")]
    public Dictionary<string, string> ConfigurationBindings { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Defines one external port in a compiled component execution contract.</summary>
public sealed class CompiledComponentPortContract
{
    /// <summary>Gets or sets the stable external port name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the port direction.</summary>
    public PortDirection Direction { get; set; }
    /// <summary>Gets or sets the signal domain.</summary>
    public SignalType SignalType { get; set; } = SignalType.Digital;
    /// <summary>Gets or sets the transported data type.</summary>
    public HardwareDataType DataType { get; set; } = HardwareDataType.Packet;
    /// <summary>Gets or sets the transported precision.</summary>
    public PrecisionKind Precision { get; set; } = PrecisionKind.Any;
    /// <summary>Gets or sets the transport protocol.</summary>
    public PortProtocol Protocol { get; set; } = PortProtocol.Packet;
    /// <summary>Gets or sets the extensible kernel-specific semantic role.</summary>
    [JsonPropertyName("semantic_role")]
    public string SemanticRole { get; set; } = "";
    /// <summary>Gets or sets the logical payload shape.</summary>
    public List<int> Shape { get; set; } = [];
    /// <summary>Gets or sets the payload size in bits.</summary>
    public long Bits { get; set; }
    /// <summary>Gets or sets whether the port must be connected.</summary>
    public bool Required { get; set; }
    /// <summary>Gets or sets whether the port permits multiple connections.</summary>
    public bool MultiConnect { get; set; }
    /// <summary>Gets or sets nominal bandwidth in bits per cycle.</summary>
    public double BandwidthBitsPerCycle { get; set; }
    /// <summary>Gets or sets fixed port-local latency in cycles.</summary>
    public int LatencyCycles { get; set; }
}

/// <summary>Separates fixed operation timing from runtime-dependent stalls.</summary>
public sealed class CompiledComponentTimingContract
{
    /// <summary>Gets or sets total fixed operation latency in cycles.</summary>
    public int OperationLatencyCycles { get; set; }
    /// <summary>Gets or sets the pipeline contribution already included in operation latency.</summary>
    public int PipelineLatencyCycles { get; set; }
    /// <summary>Gets or sets the minimum issue interval in cycles.</summary>
    public int IssueIntervalCycles { get; set; } = 1;
    /// <summary>Gets or sets the fixed service contribution in cycles.</summary>
    public int FixedServiceLatencyCycles { get; set; }
    /// <summary>Gets or sets whether queue or output backpressure may extend completion dynamically.</summary>
    public bool RuntimeDependentStallAllowed { get; set; } = true;
}

/// <summary>Defines bounded input and output queues for one compiled kernel instance.</summary>
public sealed class CompiledComponentQueueContract
{
    /// <summary>Gets or sets input queue capacity in transactions.</summary>
    public int InputDepth { get; set; } = 1;
    /// <summary>Gets or sets output queue capacity in transactions.</summary>
    public int OutputDepth { get; set; } = 1;
}

/// <summary>Defines one typed, unit-bearing resource available to a runtime kernel.</summary>
public sealed class CompiledComponentResourceEntry
{
    /// <summary>Gets or sets the stable resource name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the resource kind interpreted by the contract schema.</summary>
    public string ResourceKind { get; set; } = "";
    /// <summary>Gets or sets normalized units.</summary>
    public string Units { get; set; } = "";
    /// <summary>Gets or sets the canonical invariant value.</summary>
    public string CanonicalValue { get; set; } = "";
    /// <summary>Gets or sets the stable value type such as integer, number, boolean, or string.</summary>
    public string ValueType { get; set; } = "string";
}

/// <summary>Stores a schema-bound, recursively canonicalized JSON kernel configuration.</summary>
public sealed class CanonicalComponentKernelConfiguration
{
    /// <summary>Gets or sets the schema id used to interpret the configuration.</summary>
    [JsonPropertyName("schema_id")]
    public string SchemaId { get; set; } = "";
    /// <summary>Gets or sets canonical compact JSON with object keys sorted ordinally.</summary>
    [JsonPropertyName("canonical_json")]
    public string CanonicalJson { get; set; } = "{}";
    /// <summary>Gets or sets the SHA256 hash of schema id plus canonical JSON.</summary>
    [JsonPropertyName("configuration_hash")]
    public string ConfigurationHash { get; set; } = "";

    /// <summary>Creates a canonical configuration from arbitrary valid JSON.</summary>
    public static CanonicalComponentKernelConfiguration Create(string schemaId, string json)
    {
        if (string.IsNullOrWhiteSpace(schemaId)) throw new ArgumentException("Kernel configuration schema id is required.", nameof(schemaId));
        var canonical = ComponentExecutionJson.CanonicalizeJson(json);
        return new CanonicalComponentKernelConfiguration
        {
            SchemaId = schemaId.Trim(),
            CanonicalJson = canonical,
            ConfigurationHash = ComponentExecutionJson.ComputeSha256(schemaId.Trim() + "\n" + canonical)
        };
    }
}

/// <summary>Captures immutable implementation, registry, and characterized-profile provenance.</summary>
public sealed class CompiledComponentExecutionProvenance
{
    /// <summary>Gets or sets the exact runtime kernel implementation hash.</summary>
    public string KernelImplementationHash { get; set; } = "";
    /// <summary>Gets or sets the frozen runtime kernel registry snapshot hash.</summary>
    public string RegistrySnapshotHash { get; set; } = "";
    /// <summary>Gets or sets characterized profile hashes by stable profile id.</summary>
    public Dictionary<string, string> ProfileSnapshotHashes { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets whether all device characterization is synthetic-only.</summary>
    public bool SyntheticProfileOnly { get; set; }
    /// <summary>Gets or sets whether functional behavior excludes analog or non-ideal effects.</summary>
    public bool FunctionalIdealOnly { get; set; }
}

/// <summary>Immutable-once-compiled envelope consumed by registered deterministic component kernels.</summary>
public sealed class CompiledComponentExecutionContract
{
    /// <summary>Defines the current execution contract schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the execution contract schema version.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the exact stable kernel id.</summary>
    public string KernelId { get; set; } = "";
    /// <summary>Gets or sets the exact resolved kernel version.</summary>
    public string KernelVersion { get; set; } = "";
    /// <summary>Gets or sets the exact configuration contract schema id.</summary>
    public string ContractSchemaId { get; set; } = "";
    /// <summary>Gets or sets the stable operation kind.</summary>
    public string OperationKind { get; set; } = "";
    /// <summary>Gets or sets compiled external port contracts.</summary>
    public List<CompiledComponentPortContract> Ports { get; set; } = [];
    /// <summary>Gets or sets fixed and pipeline timing.</summary>
    public CompiledComponentTimingContract Timing { get; set; } = new();
    /// <summary>Gets or sets bounded queue capacities.</summary>
    public CompiledComponentQueueContract Queues { get; set; } = new();
    /// <summary>Gets or sets typed, normalized resources.</summary>
    public List<CompiledComponentResourceEntry> Resources { get; set; } = [];
    /// <summary>Gets or sets canonical typed kernel configuration.</summary>
    public CanonicalComponentKernelConfiguration KernelConfiguration { get; set; } = new();
    /// <summary>Gets or sets trace descriptors declared by the kernel.</summary>
    public List<ComponentTraceDescriptor> TraceDescriptors { get; set; } = [];
    /// <summary>Gets or sets metric descriptors declared by the kernel.</summary>
    public List<ComponentMetricDescriptor> MetricDescriptors { get; set; } = [];
    /// <summary>Gets or sets immutable execution provenance.</summary>
    public CompiledComponentExecutionProvenance Provenance { get; set; } = new();
    /// <summary>Gets or sets the deterministic semantic contract hash.</summary>
    public string ContractHash { get; set; } = "";

    /// <summary>Recomputes and stores the deterministic contract hash.</summary>
    public string RefreshContractHash()
    {
        ComponentExecutionJson.Normalize(this);
        ContractHash = ComponentExecutionJson.ComputeContractHash(this);
        return ContractHash;
    }
}

/// <summary>Canonical JSON and hashing helpers for component execution contracts.</summary>
public static class ComponentExecutionJson
{
    /// <summary>Serializes a normalized contract after refreshing its semantic hash.</summary>
    public static string Serialize(CompiledComponentExecutionContract contract)
    {
        if (contract is null) throw new ArgumentNullException(nameof(contract));
        contract.RefreshContractHash();
        return JsonSerializer.Serialize(contract, HardwareGraphJson.Options);
    }

    /// <summary>Deserializes and validates an exact execution contract schema and hash.</summary>
    public static CompiledComponentExecutionContract Deserialize(string json)
    {
        var contract = JsonSerializer.Deserialize<CompiledComponentExecutionContract>(json, HardwareGraphJson.Options)
            ?? throw new JsonException("Compiled component execution contract JSON did not contain an object.");
        Normalize(contract);
        if (!string.Equals(contract.SchemaVersion, CompiledComponentExecutionContract.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new JsonException($"Unsupported compiled component execution contract schema '{contract.SchemaVersion}'.");
        }

        var expectedHash = ComputeContractHash(contract);
        if (!string.IsNullOrWhiteSpace(contract.ContractHash) && !string.Equals(contract.ContractHash, expectedHash, StringComparison.Ordinal))
        {
            throw new JsonException("Compiled component execution contract hash does not match its semantic contents.");
        }

        contract.ContractHash = expectedHash;
        return contract;
    }

    /// <summary>Recursively sorts JSON object properties while preserving array order and primitive values.</summary>
    public static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Computes a lowercase SHA256 hash for UTF-8 text.</summary>
    public static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")).Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>Computes the deterministic semantic hash of an execution contract.</summary>
    public static string ComputeContractHash(CompiledComponentExecutionContract contract)
    {
        Normalize(contract);
        var semantic = new
        {
            contract.SchemaVersion,
            contract.KernelId,
            contract.KernelVersion,
            contract.ContractSchemaId,
            contract.OperationKind,
            Ports = contract.Ports.OrderBy(port => port.Name, StringComparer.Ordinal).Select(port => new
            {
                port.Name,
                port.Direction,
                port.SignalType,
                port.DataType,
                port.Precision,
                port.Protocol,
                port.SemanticRole,
                port.Shape,
                port.Bits,
                port.Required,
                port.MultiConnect,
                port.BandwidthBitsPerCycle,
                port.LatencyCycles
            }),
            contract.Timing,
            contract.Queues,
            Resources = contract.Resources.OrderBy(resource => resource.Name, StringComparer.Ordinal).Select(resource => new
            {
                resource.Name,
                resource.ResourceKind,
                resource.Units,
                resource.CanonicalValue,
                resource.ValueType
            }),
            KernelConfiguration = new
            {
                contract.KernelConfiguration.SchemaId,
                contract.KernelConfiguration.CanonicalJson,
                contract.KernelConfiguration.ConfigurationHash
            },
            TraceDescriptors = contract.TraceDescriptors.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => new { item.Name, item.EventType, item.Description }),
            MetricDescriptors = contract.MetricDescriptors.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => new { item.Name, item.Units, item.Category, item.Description }),
            Provenance = new
            {
                contract.Provenance.KernelImplementationHash,
                contract.Provenance.RegistrySnapshotHash,
                ProfileSnapshotHashes = contract.Provenance.ProfileSnapshotHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key, pair.Value }),
                contract.Provenance.SyntheticProfileOnly,
                contract.Provenance.FunctionalIdealOnly
            }
        };
        return ComputeSha256(CanonicalizeJson(JsonSerializer.Serialize(semantic, HardwareGraphJson.Options)));
    }

    internal static void Normalize(CompiledComponentExecutionContract contract)
    {
        contract.SchemaVersion = string.IsNullOrWhiteSpace(contract.SchemaVersion) ? CompiledComponentExecutionContract.CurrentSchemaVersion : contract.SchemaVersion.Trim();
        contract.KernelId = contract.KernelId?.Trim() ?? "";
        contract.KernelVersion = contract.KernelVersion?.Trim() ?? "";
        contract.ContractSchemaId = contract.ContractSchemaId?.Trim() ?? "";
        contract.OperationKind = contract.OperationKind?.Trim() ?? "";
        contract.Ports ??= [];
        foreach (var port in contract.Ports) port.Shape ??= [];
        contract.Timing ??= new();
        contract.Queues ??= new();
        contract.Resources ??= [];
        contract.KernelConfiguration ??= new();
        contract.KernelConfiguration.SchemaId = contract.KernelConfiguration.SchemaId?.Trim() ?? "";
        contract.KernelConfiguration.CanonicalJson = CanonicalizeJson(contract.KernelConfiguration.CanonicalJson);
        contract.KernelConfiguration.ConfigurationHash = ComputeSha256(contract.KernelConfiguration.SchemaId + "\n" + contract.KernelConfiguration.CanonicalJson);
        contract.TraceDescriptors ??= [];
        contract.MetricDescriptors ??= [];
        contract.Provenance ??= new();
        contract.Provenance.ProfileSnapshotHashes ??= new(StringComparer.Ordinal);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject().ToList();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Count)
                {
                    throw new JsonException("Canonical kernel configuration cannot contain duplicate object property names.");
                }
                foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }
}

/// <summary>Describes one exact runtime kernel implementation registered by a plugin.</summary>
public sealed class ComponentRuntimeKernelDescriptor
{
    /// <summary>Gets or sets the stable runtime kernel id.</summary>
    public string KernelId { get; init; } = "";
    /// <summary>Gets or sets the exact semantic version.</summary>
    public string KernelVersion { get; init; } = "";
    /// <summary>Gets or sets the exact supported configuration schema id.</summary>
    public string ContractSchemaId { get; init; } = "";
    /// <summary>Gets or sets a deterministic implementation hash.</summary>
    public string ImplementationHash { get; init; } = "";
    /// <summary>Gets or sets supported operation kinds.</summary>
    public IReadOnlyList<string> SupportedOperationKinds { get; init; } = [];
}

/// <summary>Read-only inputs supplied to a kernel-specific execution-contract compiler.</summary>
/// <param name="Template">Resolved semantic template clone.</param>
/// <param name="ResolvedParameters">Typed parameters represented as canonical invariant strings.</param>
/// <param name="DerivedMetrics">Derived profile metrics represented as canonical invariant strings.</param>
/// <param name="ConfigurationValues">Execution-binding fields resolved by Core into canonical values.</param>
/// <param name="ProfileSnapshots">Frozen characterized profile snapshots.</param>
/// <param name="KernelDescriptor">Exact resolved runtime kernel descriptor.</param>
/// <param name="RegistrySnapshotHash">Frozen registry snapshot hash.</param>
public sealed record ComponentExecutionContractCompileContext(
    ComponentTemplate Template,
    IReadOnlyDictionary<string, string> ResolvedParameters,
    IReadOnlyDictionary<string, string> DerivedMetrics,
    IReadOnlyDictionary<string, string> ConfigurationValues,
    IReadOnlyList<CharacterizedProfileSnapshot> ProfileSnapshots,
    ComponentRuntimeKernelDescriptor KernelDescriptor,
    string RegistrySnapshotHash);

/// <summary>Result returned by a kernel-specific execution-contract compiler.</summary>
public sealed class ComponentExecutionContractCompileResult
{
    /// <summary>Gets the exact compiled contract when compilation succeeds.</summary>
    public CompiledComponentExecutionContract? Contract { get; init; }
    /// <summary>Gets structured template diagnostics emitted by the kernel compiler.</summary>
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; init; } = [];
    /// <summary>Gets whether a usable contract was produced without blocking issues.</summary>
    public bool IsSuccess => Contract is not null && Issues.All(issue => issue.Severity is not ComponentTemplateIssueSeverity.Error and not ComponentTemplateIssueSeverity.Fatal);
}

/// <summary>Compiles resolved template semantics into one exact kernel execution contract.</summary>
public interface IComponentExecutionContractCompiler
{
    /// <summary>Compiles and validates an exact execution contract without mutating the source template.</summary>
    ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context);
}

/// <summary>Deep-cloneable current/next state owned by one component kernel instance.</summary>
public interface IComponentRuntimeKernelState
{
    /// <summary>Gets whether no queued, active, or blocked output work remains.</summary>
    bool IsIdle { get; }
    /// <summary>Creates a deep next-state clone with no mutable alias to current state.</summary>
    IComponentRuntimeKernelState DeepClone();
}

/// <summary>Read-only cycle context supplied by the engine to a phase-safe kernel.</summary>
/// <param name="Cycle">Current engine cycle.</param>
/// <param name="Component">Compiled top-level component.</param>
/// <param name="Contract">Exact compiled execution contract.</param>
public sealed record ComponentRuntimeKernelCycleContext(long Cycle, SimComponentDef Component, CompiledComponentExecutionContract Contract)
{
    /// <summary>Gets the exact number of engine-owned output queue slots available for this advance.</summary>
    public int AvailableOutputSlots { get; init; } = 1;
}

/// <summary>One trace-ready fact returned by a kernel without mutating the global trace.</summary>
/// <param name="EventType">Trace event category.</param>
/// <param name="Detail">Deterministic event detail.</param>
/// <param name="PacketId">Optional related packet id.</param>
/// <param name="Bits">Optional related payload bits.</param>
public sealed record ComponentRuntimeKernelEventFact(TraceEventType EventType, string Detail, string? PacketId = null, int Bits = 0);

/// <summary>One structured runtime diagnostic returned by a component kernel.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Lowercase info, warning, or error severity.</param>
/// <param name="Message">Human-readable diagnostic.</param>
/// <param name="PacketId">Optional related packet id.</param>
public sealed record ComponentRuntimeKernelIssueFact(string Code, string Severity, string Message, string? PacketId = null);

/// <summary>One exactly-once, typed energy contribution returned by a component kernel.</summary>
/// <param name="Name">Stable contribution name used for component drill-down.</param>
/// <param name="Kind">Physical contribution kind.</param>
/// <param name="Category">System-level aggregation category.</param>
/// <param name="Energy">Non-negative energy accounted exactly once.</param>
public sealed record ComponentRuntimeEnergyContribution(string Name, EnergyKind Kind, EnergyCategory Category, Picojoules Energy);

/// <summary>Selects how repeated named metric contributions are aggregated.</summary>
public enum NamedMetricAggregationKind
{
    /// <summary>Adds every sample to the accumulated value.</summary>
    Sum,
    /// <summary>Stores the most recent deterministic sample.</summary>
    Last,
    /// <summary>Stores the largest deterministic sample.</summary>
    Maximum,
    /// <summary>Stores the smallest deterministic sample.</summary>
    Minimum
}

/// <summary>One unit-bearing generic metric contribution returned by a component kernel.</summary>
/// <param name="Name">Stable metric name.</param>
/// <param name="Value">Finite numeric sample.</param>
/// <param name="Units">Normalized display and validation units.</param>
/// <param name="Aggregation">Deterministic repeated-sample aggregation.</param>
public sealed record NamedMetricContribution(
    string Name,
    double Value,
    string Units,
    NamedMetricAggregationKind Aggregation = NamedMetricAggregationKind.Sum);

/// <summary>Aggregated value and provenance for one generic exact-kernel metric.</summary>
public sealed class NamedMetricAggregate
{
    /// <summary>Gets or sets the accumulated value.</summary>
    public double Value { get; set; }
    /// <summary>Gets or sets normalized units.</summary>
    public string Units { get; set; } = "";
    /// <summary>Gets or sets the aggregation rule.</summary>
    public NamedMetricAggregationKind Aggregation { get; set; }
    /// <summary>Gets or sets the number of finite samples observed.</summary>
    public long Samples { get; set; }
}

/// <summary>One packet sampled at a named compiled input port.</summary>
/// <param name="InputPortName">Exact external input port name.</param>
/// <param name="Packet">Packet presented by the production engine.</param>
public sealed record ComponentRuntimeKernelInput(string InputPortName, Packet Packet);

/// <summary>One packet emitted through a named compiled output port.</summary>
/// <param name="OutputPortName">Exact external output port name.</param>
/// <param name="Packet">Packet returned to the engine-owned output queue.</param>
public sealed record ComponentRuntimeKernelOutput(string OutputPortName, Packet Packet);

/// <summary>Result of phase-1 input sampling by a component kernel.</summary>
public sealed class ComponentRuntimeKernelInputResult
{
    /// <summary>Gets whether the input transaction was accepted into next state.</summary>
    public bool Accepted { get; init; }
    /// <summary>Gets a stable stall reason when the input was not accepted.</summary>
    public string StallReason { get; init; } = "";
    /// <summary>Gets deterministic event facts emitted by input sampling.</summary>
    public IReadOnlyList<ComponentRuntimeKernelEventFact> Events { get; init; } = [];
    /// <summary>Gets structured input validation diagnostics.</summary>
    public IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues { get; init; } = [];
}

/// <summary>Result of phase-4 progress and output emission by a component kernel.</summary>
public sealed class ComponentRuntimeKernelAdvanceResult
{
    /// <summary>Gets named output transactions staged for engine-owned queues.</summary>
    public IReadOnlyList<ComponentRuntimeKernelOutput> Outputs { get; init; } = [];
    /// <summary>Gets deterministic event facts emitted by progress.</summary>
    public IReadOnlyList<ComponentRuntimeKernelEventFact> Events { get; init; } = [];
    /// <summary>Gets structured progress or output diagnostics.</summary>
    public IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues { get; init; } = [];
    /// <summary>Gets once-only dynamic energy accounted during this advance.</summary>
    public double DynamicEnergyPicojoules { get; init; }
    /// <summary>Gets preferred typed energy contributions; when non-empty these replace the legacy dynamic scalar.</summary>
    public IReadOnlyList<ComponentRuntimeEnergyContribution> EnergyContributions { get; init; } = [];
    /// <summary>Gets generic unit-bearing metric samples emitted during this advance.</summary>
    public IReadOnlyList<NamedMetricContribution> NamedMetricContributions { get; init; } = [];
}

/// <summary>Phase-safe kernel contract: current is read-only by convention and all mutation targets a deep-cloned next state.</summary>
public interface IPhaseSafeComponentRuntimeKernel : IComponentRuntimeKernel
{
    /// <summary>Creates initial state for one exact compiled contract.</summary>
    IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract);
    /// <summary>Checks whether phase-1 input sampling may accept another transaction.</summary>
    bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName);
    /// <summary>Samples one input packet into next state without mutating current.</summary>
    ComponentRuntimeKernelInputResult SampleInput(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, IComponentRuntimeKernelState next, ComponentRuntimeKernelInput input);
    /// <summary>Advances work and optionally emits output when the engine-owned output queue has capacity.</summary>
    ComponentRuntimeKernelAdvanceResult Advance(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, IComponentRuntimeKernelState next, bool outputQueueAvailable);
 }

/// <summary>Marker for a stateful runtime kernel created from an exact compiled contract.</summary>
public interface IComponentRuntimeKernel
{
}

/// <summary>Context passed to an exact runtime kernel factory.</summary>
/// <param name="ComponentId">Top-level compiled component id.</param>
/// <param name="Contract">Exact compiled execution contract.</param>
public sealed record ComponentRuntimeKernelCreateContext(string ComponentId, CompiledComponentExecutionContract Contract);

/// <summary>Creates an exact runtime kernel and exposes its immutable registration descriptor.</summary>
public interface IComponentRuntimeKernelFactory
{
    /// <summary>Gets the exact runtime kernel descriptor.</summary>
    ComponentRuntimeKernelDescriptor Descriptor { get; }
    /// <summary>Creates a stateful kernel for one compiled component instance.</summary>
    IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context);
}

/// <summary>Describes a deterministic test-scenario provider attached to an exact kernel identity.</summary>
public sealed class ComponentKernelTestScenarioProviderDescriptor
{
    /// <summary>Gets or sets the exact kernel id.</summary>
    public string KernelId { get; init; } = "";
    /// <summary>Gets or sets the exact kernel version.</summary>
    public string KernelVersion { get; init; } = "";
    /// <summary>Gets or sets the exact contract schema id.</summary>
    public string ContractSchemaId { get; init; } = "";
    /// <summary>Gets or sets the provider implementation version.</summary>
    public string ProviderVersion { get; init; } = "1.0.0";
}

/// <summary>One deterministic packet transaction injected into a named component input port.</summary>
public sealed class ComponentKernelTestInputTransaction
{
    /// <summary>Gets the stable transaction id.</summary>
    public string TransactionId { get; init; } = "";
    /// <summary>Gets the exact external input port name.</summary>
    public string InputPortName { get; init; } = "";
    /// <summary>Gets the cycle at which the source may first inject the packet.</summary>
    public long InjectionCycle { get; init; }
    /// <summary>Gets the deterministic packet payload.</summary>
    public Packet Packet { get; init; } = new();
}

/// <summary>Machine-readable deterministic scenario consumed by the generic component test runner.</summary>
public sealed class ComponentKernelTestScenario
{
    /// <summary>Gets or sets the stable scenario id.</summary>
    public string ScenarioId { get; init; } = "";
    /// <summary>Gets or sets the deterministic seed.</summary>
    public int Seed { get; init; }
    /// <summary>Gets or sets the maximum cycle budget.</summary>
    public int MaxCycles { get; init; } = 1;
    /// <summary>Gets or sets canonical input transaction JSON.</summary>
    public string CanonicalInputJson { get; init; } = "{}";
    /// <summary>Gets or sets canonical reference or invariant JSON.</summary>
    public string CanonicalExpectationJson { get; init; } = "{}";
    /// <summary>Gets the external output ports observed by the scenario.</summary>
    public IReadOnlyList<string> OutputPortNames { get; init; } = [];
    /// <summary>Gets the ordered deterministic input transactions injected through production source components.</summary>
    public IReadOnlyList<ComponentKernelTestInputTransaction> Inputs { get; init; } = [];
}

/// <summary>One cycle-qualified trace event captured for the component under test.</summary>
/// <param name="Cycle">Engine cycle containing the event.</param>
/// <param name="Event">Immutable trace event value.</param>
public sealed record ComponentKernelTestTimelineEvent(long Cycle, TraceEvent Event);

/// <summary>Production-engine observation passed back to a kernel-specific scenario evaluator.</summary>
public sealed class ComponentKernelTestObservation
{
    /// <summary>Gets the compiled component profile used by the run.</summary>
    public CompiledComponentProfile Profile { get; init; } = new();
    /// <summary>Gets the complete production simulation result.</summary>
    public SimulationResult Simulation { get; init; } = new();
    /// <summary>Gets deterministic events associated with the component under test.</summary>
    public IReadOnlyList<ComponentKernelTestTimelineEvent> ComponentEvents { get; init; } = [];
}

/// <summary>Kernel-specific functional and timing comparison result.</summary>
public sealed class ComponentKernelTestEvaluationResult
{
    /// <summary>Gets structured comparison diagnostics.</summary>
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; init; } = [];
    /// <summary>Gets the expected output artifact hash.</summary>
    public string ExpectedOutputHash { get; init; } = "";
    /// <summary>Gets the observed output artifact hash.</summary>
    public string ActualOutputHash { get; init; } = "";
    /// <summary>Gets kernel-specific machine-readable input, reference, and timing artifacts.</summary>
    public IReadOnlyDictionary<string, string> Artifacts { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    /// <summary>Gets whether the independent comparison succeeded.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity is not ComponentTemplateIssueSeverity.Error and not ComponentTemplateIssueSeverity.Fatal) && string.Equals(ExpectedOutputHash, ActualOutputHash, StringComparison.Ordinal);
}
/// <summary>Builds deterministic test scenarios for one exact kernel identity.</summary>
public interface IComponentKernelTestScenarioProvider
{
    /// <summary>Gets the exact provider identity.</summary>
    ComponentKernelTestScenarioProviderDescriptor Descriptor { get; }
    /// <summary>Creates one deterministic scenario for a compiled execution contract.</summary>
    ComponentKernelTestScenario CreateScenario(CompiledComponentExecutionContract contract, int seed);
    /// <summary>Evaluates functional output and exact timing from a production-engine observation.</summary>
    ComponentKernelTestEvaluationResult EvaluateScenario(ComponentKernelTestScenario scenario, ComponentKernelTestObservation observation);
}

/// <summary>Associates one plugin with an exact runtime kernel factory and optional test provider.</summary>
public sealed class ComponentRuntimeKernelRegistration
{
    internal ComponentRuntimeKernelRegistration(string pluginTypeId, IComponentRuntimeKernelFactory factory, ComponentRuntimeKernelDescriptor descriptor, IComponentKernelTestScenarioProvider? testScenarioProvider, ComponentKernelTestScenarioProviderDescriptor? testScenarioProviderDescriptor)
    {
        PluginTypeId = pluginTypeId;
        Factory = factory;
        Descriptor = new ComponentRuntimeKernelDescriptor
        {
            KernelId = descriptor.KernelId.Trim(),
            KernelVersion = descriptor.KernelVersion.Trim(),
            ContractSchemaId = descriptor.ContractSchemaId.Trim(),
            ImplementationHash = descriptor.ImplementationHash.Trim(),
            SupportedOperationKinds = new ReadOnlyCollection<string>((descriptor.SupportedOperationKinds ?? []).OrderBy(value => value, StringComparer.Ordinal).ToList())
        };
        TestScenarioProvider = testScenarioProvider;
        if (testScenarioProviderDescriptor is not null)
        {
            var source = testScenarioProviderDescriptor;
            TestScenarioProviderDescriptor = new ComponentKernelTestScenarioProviderDescriptor
            {
                KernelId = source.KernelId.Trim(),
                KernelVersion = source.KernelVersion.Trim(),
                ContractSchemaId = source.ContractSchemaId.Trim(),
                ProviderVersion = source.ProviderVersion.Trim()
            };
        }
    }

    /// <summary>Gets the owning Phase 7B plugin type id.</summary>
    public string PluginTypeId { get; }
    /// <summary>Gets the deep-frozen exact kernel descriptor.</summary>
    public ComponentRuntimeKernelDescriptor Descriptor { get; }
    /// <summary>Gets the runtime kernel factory.</summary>
    [JsonIgnore]
    public IComponentRuntimeKernelFactory Factory { get; }
    /// <summary>Gets the optional deterministic test-scenario provider.</summary>
    [JsonIgnore]
    public IComponentKernelTestScenarioProvider? TestScenarioProvider { get; }
    /// <summary>Gets the deep-frozen test-scenario provider identity.</summary>
    public ComponentKernelTestScenarioProviderDescriptor? TestScenarioProviderDescriptor { get; }
}

/// <summary>Result of freezing registered plugin kernels into an immutable snapshot.</summary>
public sealed class ComponentRuntimeKernelRegistryBuildResult
{
    /// <summary>Gets the frozen snapshot when all descriptors are valid and unique.</summary>
    public ComponentRuntimeKernelRegistrySnapshot? Snapshot { get; init; }
    /// <summary>Gets structured registry diagnostics.</summary>
    public IReadOnlyList<ComponentPluginIssue> Issues { get; init; } = [];
    /// <summary>Gets whether a usable snapshot was produced without errors.</summary>
    public bool IsSuccess => Snapshot is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Result of compile-time or runtime exact kernel resolution.</summary>
public sealed class ComponentRuntimeKernelResolutionResult
{
    /// <summary>Gets the resolved exact registration.</summary>
    public ComponentRuntimeKernelRegistration? Registration { get; init; }
    /// <summary>Gets structured resolution diagnostics.</summary>
    public IReadOnlyList<ComponentPluginIssue> Issues { get; init; } = [];
    /// <summary>Gets whether exact resolution succeeded.</summary>
    public bool IsSuccess => Registration is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Immutable deterministic runtime-kernel registry derived from the Phase 7B PluginManager.</summary>
public sealed class ComponentRuntimeKernelRegistrySnapshot
{
    private readonly IReadOnlyList<ComponentRuntimeKernelRegistration> registrations;

    private ComponentRuntimeKernelRegistrySnapshot(IReadOnlyList<ComponentRuntimeKernelRegistration> registrations, string contentHash)
    {
        this.registrations = registrations;
        ContentHash = contentHash;
    }

    /// <summary>Gets exact registrations in stable identity order.</summary>
    public IReadOnlyList<ComponentRuntimeKernelRegistration> Registrations => registrations;
    /// <summary>Gets the deterministic content hash excluding factory object identity.</summary>
    public string ContentHash { get; }

    /// <summary>Freezes optional kernel factories already accepted by the supplied PluginManager.</summary>
    public static ComponentRuntimeKernelRegistryBuildResult Build(ComponentPluginManager manager)
    {
        if (manager is null) throw new ArgumentNullException(nameof(manager));
        var issues = new List<ComponentPluginIssue>();
        var candidates = new List<ComponentRuntimeKernelRegistration>();
        foreach (var plugin in manager.GetPlugins())
        {
            var factory = plugin.RuntimeKernelFactory;
            if (factory is null) continue;
            ComponentRuntimeKernelDescriptor? descriptor = factory.Descriptor;
            var scenarioProvider = plugin.KernelTestScenarioProvider;
            ComponentKernelTestScenarioProviderDescriptor? scenarioDescriptor = scenarioProvider?.Descriptor;
            if (scenarioProvider is not null && scenarioDescriptor is null)
            {
                issues.Add(Issue(ComponentExecutionIssueCodes.KernelIncompatible, "$.kernel_test_scenario_provider", "Kernel test scenario provider descriptor is required.", plugin.TypeId));
            }
            ValidateDescriptor(plugin, descriptor, scenarioDescriptor, issues);
            if (descriptor is not null && (scenarioProvider is null || scenarioDescriptor is not null))
            {
                candidates.Add(new ComponentRuntimeKernelRegistration(plugin.TypeId, factory, descriptor, scenarioProvider, scenarioDescriptor));
            }
        }

        foreach (var duplicate in candidates
                     .GroupBy(item => KernelKey(item.Descriptor.KernelId, item.Descriptor.KernelVersion), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(Issue(
                ComponentExecutionIssueCodes.KernelDuplicate,
                "$.runtime_kernels",
                $"Runtime kernel '{duplicate.First().Descriptor.KernelId}' version '{duplicate.First().Descriptor.KernelVersion}' is registered by multiple plugins: {string.Join(", ", duplicate.Select(item => item.PluginTypeId).OrderBy(id => id, StringComparer.Ordinal))}.",
                duplicate.First().Descriptor.KernelId));
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new ComponentRuntimeKernelRegistryBuildResult { Issues = issues.AsReadOnly() };
        }

        var ordered = candidates
            .OrderBy(item => item.Descriptor.KernelId, StringComparer.Ordinal)
            .ThenBy(item => ParseVersion(item.Descriptor.KernelVersion))
            .ThenBy(item => item.Descriptor.ContractSchemaId, StringComparer.Ordinal)
            .ThenBy(item => item.PluginTypeId, StringComparer.Ordinal)
            .ToList();
        var contentHash = ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(
            ordered.Select(item => new
            {
                item.PluginTypeId,
                item.Descriptor.KernelId,
                item.Descriptor.KernelVersion,
                item.Descriptor.ContractSchemaId,
                item.Descriptor.ImplementationHash,
                SupportedOperationKinds = item.Descriptor.SupportedOperationKinds.OrderBy(value => value, StringComparer.Ordinal),
                TestScenarioProviderVersion = item.TestScenarioProviderDescriptor?.ProviderVersion ?? ""
            }),
            HardwareGraphJson.Options)));
        return new ComponentRuntimeKernelRegistryBuildResult
        {
            Snapshot = new ComponentRuntimeKernelRegistrySnapshot(new ReadOnlyCollection<ComponentRuntimeKernelRegistration>(ordered), contentHash),
            Issues = issues.AsReadOnly()
        };
    }

    /// <summary>Resolves a design-time exact or major-wildcard version requirement to one exact registration.</summary>
    public ComponentRuntimeKernelResolutionResult ResolveRequirement(string kernelId, string versionRequirement, string contractSchemaId)
    {
        var byId = registrations.Where(item => string.Equals(item.Descriptor.KernelId, kernelId?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count == 0) return Missing(kernelId);
        var bySchema = byId.Where(item => string.Equals(item.Descriptor.ContractSchemaId, contractSchemaId?.Trim(), StringComparison.Ordinal)).ToList();
        if (bySchema.Count == 0) return Incompatible(kernelId, $"No registered version of kernel '{kernelId}' supports contract schema '{contractSchemaId}'.");
        var compatible = bySchema.Where(item => VersionMatches(item.Descriptor.KernelVersion, versionRequirement)).OrderByDescending(item => ParseVersion(item.Descriptor.KernelVersion)).ThenBy(item => item.Descriptor.KernelVersion, StringComparer.Ordinal).ToList();
        return compatible.Count == 0
            ? Incompatible(kernelId, $"Kernel '{kernelId}' has no version compatible with requirement '{versionRequirement}'.")
            : new ComponentRuntimeKernelResolutionResult { Registration = compatible[0] };
    }

    /// <summary>Resolves the exact id, version, and schema stored in a SimulationGraph contract.</summary>
    public ComponentRuntimeKernelResolutionResult ResolveExact(string kernelId, string exactVersion, string contractSchemaId)
    {
        var byId = registrations.Where(item => string.Equals(item.Descriptor.KernelId, kernelId?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count == 0) return Missing(kernelId);
        var exact = byId.FirstOrDefault(item =>
            string.Equals(item.Descriptor.KernelVersion, exactVersion?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Descriptor.ContractSchemaId, contractSchemaId?.Trim(), StringComparison.Ordinal));
        return exact is not null
            ? new ComponentRuntimeKernelResolutionResult { Registration = exact }
            : Incompatible(kernelId, $"Kernel '{kernelId}' exact version '{exactVersion}' and schema '{contractSchemaId}' are not registered together.");
    }

    private static void ValidateDescriptor(ComponentPluginDescriptor plugin, ComponentRuntimeKernelDescriptor? descriptor, ComponentKernelTestScenarioProviderDescriptor? scenario, List<ComponentPluginIssue> issues)
    {
        if (descriptor is null)
        {
            issues.Add(Issue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, "$.runtime_kernel_factory.descriptor", "Runtime kernel descriptor is required.", plugin.TypeId));
            return;
        }

        Require(descriptor.KernelId, "kernel_id", plugin.TypeId, issues);
        Require(descriptor.KernelVersion, "kernel_version", plugin.TypeId, issues);
        Require(descriptor.ContractSchemaId, "contract_schema_id", plugin.TypeId, issues);
        Require(descriptor.ImplementationHash, "implementation_hash", plugin.TypeId, issues);
        if (!TryParseVersion(descriptor.KernelVersion, out _))
        {
            issues.Add(Issue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, "$.runtime_kernel_factory.descriptor.kernel_version", $"Kernel version '{descriptor.KernelVersion}' is not a numeric semantic version.", descriptor.KernelId));
        }

        if (scenario is null) return;
        if (scenario is null ||
            !string.Equals(scenario.KernelId, descriptor.KernelId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(scenario.KernelVersion, descriptor.KernelVersion, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(scenario.ContractSchemaId, descriptor.ContractSchemaId, StringComparison.Ordinal))
        {
            issues.Add(Issue(ComponentExecutionIssueCodes.KernelIncompatible, "$.kernel_test_scenario_provider", "Kernel test scenario provider identity must exactly match its runtime kernel descriptor.", descriptor.KernelId));
        }
    }

    private static void Require(string value, string field, string relatedId, List<ComponentPluginIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Issue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, "$.runtime_kernel_factory.descriptor." + field, $"Runtime kernel descriptor field '{field}' is required.", relatedId));
        }
    }

    private static ComponentRuntimeKernelResolutionResult Missing(string kernelId) => new()
    {
        Issues = [Issue(ComponentExecutionIssueCodes.KernelMissing, "$.execution_contract.kernel_id", $"Runtime kernel '{kernelId}' is not registered in the frozen snapshot.", kernelId)]
    };

    private static ComponentRuntimeKernelResolutionResult Incompatible(string kernelId, string message) => new()
    {
        Issues = [Issue(ComponentExecutionIssueCodes.KernelIncompatible, "$.execution_contract", message, kernelId)]
    };

    private static ComponentPluginIssue Issue(string code, string location, string message, string? relatedId) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);

    private static string KernelKey(string id, string version) => (id?.Trim() ?? "") + "\n" + (version?.Trim() ?? "");

    private static bool VersionMatches(string exactVersion, string requirement)
    {
        var normalized = string.IsNullOrWhiteSpace(requirement) ? "*" : requirement.Trim();
        if (normalized is "*" or "x" or "X") return true;
        var parts = normalized.Split('.');
        if (parts.Length == 2 && (parts[1].Equals("x", StringComparison.OrdinalIgnoreCase) || parts[1] == "*") && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return TryParseVersion(exactVersion, out var parsed) && parsed.Major == major;
        }

        return string.Equals(exactVersion, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static Version ParseVersion(string value) => TryParseVersion(value, out var version) ? version : new Version(0, 0, 0);

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value?.Trim() ?? "";
        var numeric = normalized.Split('-', 2)[0];
        var components = numeric.Split('.');
        if (components.Length == 2) numeric += ".0";
        return Version.TryParse(numeric, out version!);
    }
}
