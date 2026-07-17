using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

#pragma warning disable CS1591 // Phase 7C data contracts use stable member names as schema surface.

public enum ComponentTemplateTargetKind { ProcessingElement, Router, Memory, Buffer, Link, OpticalComposite, CimComposite, Custom }
public enum ComponentTemplateLifecycleState { Draft, Validated, Compiled, Published, Deprecated, BrokenDependency }
public enum ComponentTemplateIssueSeverity { Info, Warning, Error, Fatal }
public enum TemplateParameterValueKind { Integer, Number, String, Boolean, Enum }
public enum TemplateViewKind { Symbol, Dataflow, StructuralPort, ModelProfile, Storage, CompiledProfile }
public enum InternalBlockLayer { Dataflow, Structural }
public enum TemplateBackpressureBehavior { Propagate, StallLocal, DropWithError }
public enum TemplateCollectionTargetPolicy { IngressRouter, WorkloadMapping, ExplicitRouteMapping }

public static class ComponentTemplateRuntimeKeys
{
    public const string TemplateId = "component_template_id";
    public const string TemplateVersion = "component_template_version";
    public const string CompiledProfileHash = "component_template_compiled_profile_hash";
    public const string OperationLatency = "component_template_operation_latency";
    public const string PipelineLatency = "component_template_pipeline_latency";
    public const string IssueInterval = "component_template_issue_interval";
    public const string InputQueueDepth = "component_template_input_queue_depth";
    public const string OutputQueueDepth = "component_template_output_queue_depth";
    public const string DefaultResponseTargetPolicy = "component_template_default_response_target_policy";
    public const string TraceDescriptors = "component_template_trace_descriptors";
    public const string InternalDrilldownStages = "component_template_internal_drilldown_stages";
    public const string EnergyTotalPicojoules = "component_template_energy_total_pj";
    public const string EnergyBreakdownPicojoules = "component_template_energy_breakdown_pj";
    public const string AreaTotalUm2 = "component_template_area_total_um2";
    public const string ExecutionKernelId = "component_template_execution_kernel_id";
    public const string ExecutionKernelVersion = "component_template_execution_kernel_version";
    public const string ExecutionContractSchemaId = "component_template_execution_contract_schema_id";
    public const string ExecutionContractHash = "component_template_execution_contract_hash";
    public const string RuntimeKernelRegistryHash = "component_template_runtime_kernel_registry_hash";
}

public sealed class ComponentTemplateInstanceRef
{
    [JsonPropertyName("template_id")] public string TemplateId { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, string> ParameterOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string CompiledProfileHash { get; set; } = "";
}

public sealed record ComponentTemplateIssue(
    string Code,
    ComponentTemplateIssueSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null,
    string? Suggestion = null);

public sealed class TemplateParameter
{
    public string Name { get; set; } = "";
    public TemplateParameterValueKind ValueKind { get; set; } = TemplateParameterValueKind.String;
    public string DefaultValue { get; set; } = "";
    public string Units { get; set; } = "";
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public List<string> AllowedValues { get; set; } = [];
    public bool Required { get; set; }
    public string Description { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TemplateExternalPort
{
    public string Name { get; set; } = "";
    public PortDirection Direction { get; set; }
    public SignalType SignalType { get; set; } = SignalType.Digital;
    public HardwareDataType DataType { get; set; } = HardwareDataType.Packet;
    public PrecisionKind Precision { get; set; } = PrecisionKind.Any;
    public PortProtocol Protocol { get; set; } = PortProtocol.Packet;
    public List<int> Shape { get; set; } = [];
    public int BandwidthBitsPerCycle { get; set; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    public bool Required { get; set; }
    public string ShellBlockId { get; set; } = "";
    public string ShellPortName { get; set; } = "";
    public string Tooltip { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class InternalPort
{
    public string Name { get; set; } = "";
    public PortDirection Direction { get; set; }
    public SignalType SignalType { get; set; } = SignalType.Digital;
    public HardwareDataType DataType { get; set; } = HardwareDataType.Packet;
    public PrecisionKind Precision { get; set; } = PrecisionKind.Any;
    public PortProtocol Protocol { get; set; } = PortProtocol.Packet;
    public int WidthBits { get; set; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    public List<int> Shape { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class InternalBlock
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string BlockKind { get; set; } = "";
    public string TypeId { get; set; } = "";
    public InternalBlockLayer Layer { get; set; } = InternalBlockLayer.Structural;
    public bool Abstract { get; set; }
    public List<string> MappedStructuralBlockIds { get; set; } = [];
    public List<InternalPort> Ports { get; set; } = [];
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ProfileBindingIds { get; set; } = [];
    public string TraceStage { get; set; } = "";
    public double EnergyPicojoules { get; set; }
    public double AreaUm2 { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class InternalConnection
{
    public string Id { get; set; } = "";
    public string SourceBlockId { get; set; } = "";
    public string SourcePortName { get; set; } = "";
    public string TargetBlockId { get; set; } = "";
    public string TargetPortName { get; set; } = "";
    public string PayloadType { get; set; } = "packet";
    public List<int> Shape { get; set; } = [];
    public PrecisionKind Precision { get; set; } = PrecisionKind.Any;
    public double RatePerCycle { get; set; } = 1.0;
    public int LatencyCycles { get; set; }
    public int BandwidthBitsPerCycle { get; set; } = ComponentDefaults.LinkBandwidthBitsPerCycle;
    public int SerializationFactor { get; set; } = 1;
    public TemplateBackpressureBehavior BackpressureBehavior { get; set; } = TemplateBackpressureBehavior.Propagate;
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TemplateView
{
    public TemplateViewKind Kind { get; set; }
    public Dictionary<string, GridPosition> Layout { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TemplateOperandContract
{
    public string Name { get; set; } = "";
    public List<int> Shape { get; set; } = [];
    public string DType { get; set; } = "";
    public string Layout { get; set; } = "";
    public string StorageRef { get; set; } = "";
}

public sealed class TemplateQuantizationContract
{
    public string Mode { get; set; } = "nearest_even";
    public bool Saturation { get; set; } = true;
}

public sealed class TemplateErrorBehaviorContract
{
    public string InvalidShape { get; set; } = "structured_error";
    public string UnsupportedDType { get; set; } = "structured_error";
    public string MissingStoredOperand { get; set; } = "structured_error";
}

public sealed class TemplateOperationContract
{
    public string OperationName { get; set; } = "";
    public List<TemplateOperandContract> InputOperands { get; set; } = [];
    public List<TemplateOperandContract> StoredOperands { get; set; } = [];
    public List<TemplateOperandContract> OutputOperands { get; set; } = [];
    public string Equation { get; set; } = "";
    public string MultiplyDType { get; set; } = "";
    public string AccumulateDType { get; set; } = "";
    public string OutputDType { get; set; } = "";
    public TemplateQuantizationContract Quantization { get; set; } = new();
    public TemplateErrorBehaviorContract ErrorBehavior { get; set; } = new();
}

public sealed class TemplateTimingContract
{
    public string Handshake { get; set; } = "input_ready_input_valid";
    public string AcceptCycleDefinition { get; set; } = "cycle where input_valid && input_ready";
    public string BusyUntilCycleDefinition { get; set; } = "accept_cycle + operation_latency";
    public string OutputValidCycleDefinition { get; set; } = "accept_cycle + pipeline_latency + operation_latency";
    public bool OutputCreditRequired { get; set; } = true;
    public string OutputBackpressureBehavior { get; set; } = "hold_valid";
    public bool CanAcceptWhileBusy { get; set; }
    public int InputQueueDepth { get; set; } = 1;
    public int OutputQueueDepth { get; set; } = 1;
    public int IssueInterval { get; set; } = 1;
    public int PipelineLatency { get; set; }
    public int OperationLatency { get; set; } = 1;
    public TemplateCollectionTargetPolicy DefaultResponseTargetPolicy { get; set; } = TemplateCollectionTargetPolicy.IngressRouter;
}

public sealed class StorageBitSlice
{
    public int LogicalBitStart { get; set; }
    public int BitCount { get; set; }
    public int CellBitStart { get; set; }
}

public sealed class TemplateStorageLayout
{
    public string Id { get; set; } = "";
    public string LogicalName { get; set; } = "";
    public int Banks { get; set; } = 1;
    public int Rows { get; set; }
    public int Columns { get; set; }
    [JsonPropertyName("cell_bits")] public int CellBits { get; set; } = 1;
    public string Encoding { get; set; } = "binary";
    public bool Signed { get; set; }
    public string Endianness { get; set; } = "little";
    public string Order { get; set; } = "row_major";
    public int ReservedRows { get; set; }
    public int ReservedColumns { get; set; }
    public List<StorageBitSlice> BitSlices { get; set; } = [];
    public string MissingAddressBehavior { get; set; } = "structured_error";
    public bool RuntimeWriteAllowed { get; set; }
    [JsonIgnore] public long CapacityBits => Math.Max(0, Banks) * Math.Max(0, Rows - ReservedRows) * Math.Max(0, Columns - ReservedColumns) * Math.Max(0, CellBits);
}

public sealed class TemplateProfileBinding
{
    public string BindingId { get; set; } = "";
    public string BlockId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public bool Synthetic { get; set; }
    public ComponentTemplateIssueSeverity RangeExceededSeverity { get; set; } = ComponentTemplateIssueSeverity.Warning;
    public CharacterizedProfileSnapshot? Snapshot { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ComponentTemplateProvenance
{
    public string Source { get; set; } = "";
    public string Author { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public Dictionary<string, string> DependencyHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string CompileHash { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
}

public sealed class ComponentTemplate
{
    public const string CurrentSchemaVersion = "1.0";
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyName("template_id")] public string TemplateId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Category { get; set; } = "ComponentTemplate";
    [JsonPropertyName("target_kind")] public ComponentTemplateTargetKind TargetKind { get; set; } = ComponentTemplateTargetKind.Custom;
    public ComponentTemplateLifecycleState Lifecycle { get; set; } = ComponentTemplateLifecycleState.Draft;
    public List<TemplateExternalPort> ExternalPorts { get; set; } = [];
    public List<TemplateParameter> Parameters { get; set; } = [];
    public List<InternalBlock> InternalBlocks { get; set; } = [];
    public List<InternalConnection> InternalConnections { get; set; } = [];
    public List<TemplateView> Views { get; set; } = [];
    public TemplateOperationContract OperationContract { get; set; } = new();
    public TemplateTimingContract TimingContract { get; set; } = new();
    public List<TemplateStorageLayout> StorageLayouts { get; set; } = [];
    public List<TemplateProfileBinding> ProfileBindings { get; set; } = [];
    [JsonPropertyName("execution_binding")] public ComponentTemplateExecutionBinding? ExecutionBinding { get; set; }
    public ComponentTemplateProvenance Provenance { get; set; } = new();
    public CompiledComponentProfile? CompiledProfile { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CompiledComponentProfile
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
    public string TemplateId { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public ComponentTemplateTargetKind TargetKind { get; set; }
    public List<string> SupportedOperations { get; set; } = [];
    public Dictionary<string, string> InstanceOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ShapeContract { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int OperationLatency { get; set; }
    public int PipelineLatency { get; set; }
    public int IssueInterval { get; set; }
    public int InputQueueDepth { get; set; }
    public int OutputQueueDepth { get; set; }
    public TemplateCollectionTargetPolicy DefaultResponseTargetPolicy { get; set; } = TemplateCollectionTargetPolicy.IngressRouter;
    public Dictionary<string, long> Capacity { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> BandwidthBitsPerCycle { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> EnergyPicojoules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> AreaUm2 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
/// <summary>Gets or sets the compiled continuous physical footprint.</summary>
    public PhysicalFootprint? PhysicalFootprint { get; set; }
    public List<string> TraceDescriptors { get; set; } = [];
    public List<string> InternalDrilldownStages { get; set; } = [];
    public Dictionary<string, string> AggregationRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DerivedMetrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Provenance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("execution_contract")] public CompiledComponentExecutionContract? ExecutionContract { get; set; }
    public string ProfileHash { get; set; } = "";
    [JsonIgnore] public double TotalEnergyPicojoules => EnergyPicojoules.Values.Sum();
    [JsonIgnore] public double TotalAreaUm2 => AreaUm2.Values.Sum();
}

public sealed class ComponentTemplateValidationResult
{
    public List<ComponentTemplateIssue> Issues { get; } = [];
    public bool HasBlockingIssues => Issues.Any(i => i.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal);
    public void Add(ComponentTemplateIssue issue) => Issues.Add(issue);
}

public sealed class ComponentTemplateDeserializationResult
{
    private ComponentTemplateDeserializationResult(ComponentTemplate? template, IReadOnlyList<ComponentTemplateIssue> issues) { Template = template; Issues = issues; }
    public bool IsSuccess => Template is not null && Issues.Count == 0;
    public ComponentTemplate? Template { get; }
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; }
    public static ComponentTemplateDeserializationResult Success(ComponentTemplate template) => new(template, []);
    public static ComponentTemplateDeserializationResult Failure(params ComponentTemplateIssue[] issues) => new(null, issues);
}

public sealed class ComponentTemplateSerializationException : Exception
{
    public ComponentTemplateSerializationException(IReadOnlyList<ComponentTemplateIssue> issues) : base(string.Join(Environment.NewLine, issues.Select(i => $"{i.Code} at {i.Location}: {i.Message}"))) { Issues = issues; }
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; }
}

public static class ComponentTemplateJson
{
    public static JsonSerializerOptions Options => HardwareGraphJson.Options;

    public static string Serialize(ComponentTemplate template)
    {
        Normalize(template);
        return JsonSerializer.Serialize(template, Options);
    }

    public static ComponentTemplate Deserialize(string json)
    {
        var result = TryDeserialize(json);
        if (result.Template is not null) return result.Template;
        throw new ComponentTemplateSerializationException(result.Issues);
    }

    public static ComponentTemplateDeserializationResult TryDeserialize(string json)
    {
        try
        {
            var template = JsonSerializer.Deserialize<ComponentTemplate>(json, Options);
            if (template is null) return ComponentTemplateDeserializationResult.Failure(new ComponentTemplateIssue("InvalidJson", ComponentTemplateIssueSeverity.Error, "$", "ComponentTemplate JSON did not contain an object."));
            var version = ValidateSchemaVersion(template.SchemaVersion, nameof(ComponentTemplate), "$.schema_version");
            if (version is not null) return ComponentTemplateDeserializationResult.Failure(version);
            Normalize(template);
            return ComponentTemplateDeserializationResult.Success(template);
        }
        catch (JsonException e)
        {
            return ComponentTemplateDeserializationResult.Failure(new ComponentTemplateIssue("InvalidJson", ComponentTemplateIssueSeverity.Error, e.Path ?? "$", e.Message));
        }
    }

    public static ComponentTemplate Clone(ComponentTemplate template) => Deserialize(Serialize(template));

    public static string ComputeSemanticHash(ComponentTemplate template, IReadOnlyDictionary<string, string>? instanceOverrides = null, IReadOnlyList<CharacterizedProfileSnapshot>? snapshots = null)
    {
        Normalize(template);
        var semantic = new
        {
            template.SchemaVersion,
            template.TemplateId,
            template.DisplayName,
            template.Version,
            template.Category,
            template.TargetKind,
            ExecutionBinding = template.ExecutionBinding is null ? null : new { template.ExecutionBinding.KernelId, template.ExecutionBinding.KernelVersionRequirement, template.ExecutionBinding.ContractSchemaId, template.ExecutionBinding.OperationKind, ConfigurationBindings = Ordered(template.ExecutionBinding.ConfigurationBindings) },
            ExternalPorts = template.ExternalPorts.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new { p.Name, p.Direction, p.SignalType, p.DataType, p.Precision, p.Protocol, p.Shape, p.BandwidthBitsPerCycle, p.Required, p.ShellBlockId, p.ShellPortName }),
            Parameters = template.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new { p.Name, p.ValueKind, p.DefaultValue, p.Units, p.Minimum, p.Maximum, AllowedValues = p.AllowedValues.OrderBy(v => v, StringComparer.Ordinal), p.Required }),
            Blocks = template.InternalBlocks.OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => new { b.Id, b.DisplayName, b.BlockKind, b.TypeId, b.Layer, b.Abstract, MappedStructuralBlockIds = b.MappedStructuralBlockIds.OrderBy(id => id, StringComparer.Ordinal), Ports = b.Ports.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new { p.Name, p.Direction, p.SignalType, p.DataType, p.Precision, p.Protocol, p.WidthBits, p.Shape }), Parameters = Ordered(b.Parameters), ProfileBindingIds = b.ProfileBindingIds.OrderBy(id => id, StringComparer.Ordinal), b.TraceStage, b.EnergyPicojoules, b.AreaUm2 }),
            Connections = template.InternalConnections.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new { c.Id, c.SourceBlockId, c.SourcePortName, c.TargetBlockId, c.TargetPortName, c.PayloadType, c.Shape, c.Precision, c.RatePerCycle, c.LatencyCycles, c.BandwidthBitsPerCycle, c.SerializationFactor, c.BackpressureBehavior }),
            template.OperationContract,
            template.TimingContract,
            StorageLayouts = template.StorageLayouts.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new { s.Id, s.LogicalName, s.Banks, s.Rows, s.Columns, s.CellBits, s.Encoding, s.Signed, s.Endianness, s.Order, s.ReservedRows, s.ReservedColumns, BitSlices = s.BitSlices.OrderBy(b => b.LogicalBitStart).ThenBy(b => b.CellBitStart).Select(b => new { b.LogicalBitStart, b.BitCount, b.CellBitStart }), s.MissingAddressBehavior, s.RuntimeWriteAllowed }),
            ProfileBindings = template.ProfileBindings.OrderBy(b => b.BindingId, StringComparer.Ordinal).Select(b => new { b.BindingId, b.BlockId, b.ProfileId, b.ModelId, b.Synthetic, b.RangeExceededSeverity, SnapshotHash = b.Snapshot?.Hash ?? "" }),
            InstanceOverrides = Ordered(instanceOverrides ?? new Dictionary<string, string>()),
            ProfileSnapshots = (snapshots ?? []).OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new { s.Id, s.Hash })
        };
        return StableHash(semantic);
    }

    public static string StableHash(object value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    internal static ComponentTemplateIssue? ValidateSchemaVersion(string? schemaVersion, string contract, string location)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion)) return new("MissingSchemaVersion", ComponentTemplateIssueSeverity.Error, location, $"{contract} schema_version is required.");
        var majorText = schemaVersion.Split('.', 2)[0];
        if (!int.TryParse(majorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) || major != 1) return new("UnsupportedSchemaVersion", ComponentTemplateIssueSeverity.Error, location, $"{contract} schema major version '{majorText}' is not supported; supported major version is 1.");
        if (!string.Equals(schemaVersion, ComponentTemplate.CurrentSchemaVersion, StringComparison.Ordinal)) return new("UnsupportedSchemaVersion", ComponentTemplateIssueSeverity.Error, location, $"{contract} schema version '{schemaVersion}' is not supported by the current reader; supported version is {ComponentTemplate.CurrentSchemaVersion}.");
        return null;
    }

    internal static void Normalize(ComponentTemplate template)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        template.SchemaVersion = string.IsNullOrWhiteSpace(template.SchemaVersion) ? ComponentTemplate.CurrentSchemaVersion : template.SchemaVersion;
        template.ExternalPorts ??= [];
        template.Parameters ??= [];
        template.InternalBlocks ??= [];
        template.InternalConnections ??= [];
        template.Views ??= [];
        template.OperationContract ??= new();
        template.TimingContract ??= new();
        template.StorageLayouts ??= [];
        template.ProfileBindings ??= [];
        if (template.ExecutionBinding is not null) template.ExecutionBinding.ConfigurationBindings ??= new(StringComparer.Ordinal);
        template.Provenance ??= new();
        template.ExtensionData ??= new(StringComparer.Ordinal);
        foreach (var p in template.Parameters) { p.AllowedValues ??= []; p.ExtensionData ??= new(StringComparer.Ordinal); }
        foreach (var p in template.ExternalPorts) { p.Shape ??= []; p.ExtensionData ??= new(StringComparer.Ordinal); }
        foreach (var b in template.InternalBlocks)
        {
            b.Ports ??= []; b.Parameters ??= new(StringComparer.OrdinalIgnoreCase); b.MappedStructuralBlockIds ??= []; b.ProfileBindingIds ??= []; b.ExtensionData ??= new(StringComparer.Ordinal);
            foreach (var p in b.Ports) { p.Shape ??= []; p.ExtensionData ??= new(StringComparer.Ordinal); }
        }
        foreach (var c in template.InternalConnections) { c.Shape ??= []; c.ExtensionData ??= new(StringComparer.Ordinal); }
        foreach (var v in template.Views) { v.Layout ??= new(StringComparer.OrdinalIgnoreCase); v.Metadata ??= new(StringComparer.OrdinalIgnoreCase); v.ExtensionData ??= new(StringComparer.Ordinal); }
        template.OperationContract.InputOperands ??= [];
        template.OperationContract.StoredOperands ??= [];
        template.OperationContract.OutputOperands ??= [];
        template.OperationContract.Quantization ??= new();
        template.OperationContract.ErrorBehavior ??= new();
        foreach (var op in template.OperationContract.InputOperands.Concat(template.OperationContract.OutputOperands).Concat(template.OperationContract.StoredOperands)) op.Shape ??= [];
        foreach (var s in template.StorageLayouts) s.BitSlices ??= [];
        foreach (var b in template.ProfileBindings) b.ExtensionData ??= new(StringComparer.Ordinal);
        template.Provenance.DependencyHashes ??= new(StringComparer.OrdinalIgnoreCase);
        template.Provenance.Warnings ??= [];
    }

    private static IReadOnlyDictionary<string, string> Ordered(IReadOnlyDictionary<string, string> source) =>
        new ReadOnlyDictionary<string, string>(source.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
}

public sealed class ComponentTemplateValidator
{
    public ComponentTemplateValidationResult Validate(ComponentTemplate template)
    {
        ComponentTemplateJson.Normalize(template);
        var r = new ComponentTemplateValidationResult();
        Identity(template, r); Ports(template, r); Blocks(template, r); Operation(template, r); Storage(template, r); Profiles(template, r); Lifecycle(template, r);
        return r;
    }

    internal static int DTypeBits(string dtype)
    {
        var n = (dtype ?? "").Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
        return n switch { "fp8" or "fp8e4m3" or "fp8e5m2" or "int8" or "uint8" => 8, "fp16" or "bf16" or "int16" => 16, "fp32" or "tf32" or "int32" => 32, "int4" or "uint4" => 4, "int2" or "uint2" => 2, "binary" or "int1" or "uint1" => 1, _ => 8 };
    }

    internal static long OperandBits(TemplateOperandContract op) => op.Shape.Aggregate(1L, (a, d) => checked(a * Math.Max(1, d))) * DTypeBits(op.DType);

    private static void Identity(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        var version = ComponentTemplateJson.ValidateSchemaVersion(t.SchemaVersion, nameof(ComponentTemplate), "$.schema_version"); if (version is not null) r.Add(version);
        if (string.IsNullOrWhiteSpace(t.TemplateId)) r.Add(Error("TemplateIdMissing", "$.template_id", "Template id is required."));
        if (string.IsNullOrWhiteSpace(t.Version)) r.Add(Error("TemplateVersionMissing", "$.version", "Template version is required."));
        if (t.TargetKind != ComponentTemplateTargetKind.ProcessingElement && t.ExecutionBinding is null) r.Add(new("TemplateTargetDeferred", ComponentTemplateIssueSeverity.Warning, "$.target_kind", $"Target kind '{t.TargetKind}' requires an explicit registered execution binding in Phase 7C.", t.TemplateId));
    }

    private static void Ports(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        foreach (var dup in t.ExternalPorts.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) r.Add(Error("TemplateExternalPortDuplicate", "$.external_ports", $"External port '{dup.Key}' is duplicated.", dup.Key));
        var blocks = t.InternalBlocks.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var p in t.ExternalPorts)
        {
            if (string.IsNullOrWhiteSpace(p.ShellBlockId) || string.IsNullOrWhiteSpace(p.ShellPortName)) { r.Add(Error("TemplateExternalPortShellBindingMissing", $"$.external_ports[{p.Name}]", $"External port '{p.Name}' must bind exactly one shell block/port.", p.Name)); continue; }
            if (!blocks.TryGetValue(p.ShellBlockId, out var b) || b.Ports.All(port => !string.Equals(port.Name, p.ShellPortName, StringComparison.OrdinalIgnoreCase))) r.Add(Error("TemplateExternalPortShellBindingInvalid", $"$.external_ports[{p.Name}]", $"External port '{p.Name}' shell binding '{p.ShellBlockId}.{p.ShellPortName}' does not exist.", p.Name));
        }
    }

    private static void Blocks(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        foreach (var dup in t.InternalBlocks.GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) r.Add(Error("TemplateInternalBlockDuplicate", "$.internal_blocks", $"Internal block '{dup.Key}' is duplicated.", dup.Key));
        var blocks = t.InternalBlocks.Where(b => !string.IsNullOrWhiteSpace(b.Id)).ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var b in t.InternalBlocks)
        {
            if (string.IsNullOrWhiteSpace(b.Id)) r.Add(Error("TemplateInternalBlockIdMissing", "$.internal_blocks", "Internal block id is required."));
            foreach (var dupPort in b.Ports.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) r.Add(Error("TemplateInternalPortDuplicate", $"$.internal_blocks[{b.Id}].ports", $"Internal port '{b.Id}.{dupPort.Key}' is duplicated.", b.Id));
            foreach (var p in b.Ports.Where(p => string.IsNullOrWhiteSpace(p.Name) || p.WidthBits <= 0)) r.Add(Error("TemplateInternalPortInvalid", $"$.internal_blocks[{b.Id}].ports", $"Internal port '{b.Id}.{p.Name}' must have name and positive width.", b.Id));
            if (b.Layer == InternalBlockLayer.Dataflow && !b.Abstract && (b.MappedStructuralBlockIds.Count == 0 || b.MappedStructuralBlockIds.Any(id => !blocks.TryGetValue(id, out var sb) || sb.Layer != InternalBlockLayer.Structural))) r.Add(Error("TemplateDataflowStructuralMappingMissing", $"$.internal_blocks[{b.Id}].mapped_structural_block_ids", $"Dataflow block '{b.Id}' must map to one or more structural blocks or be explicitly abstract.", b.Id));
        }
        foreach (var c in t.InternalConnections)
        {
            var source = FindPort(blocks, c.SourceBlockId, c.SourcePortName); var target = FindPort(blocks, c.TargetBlockId, c.TargetPortName);
            if (source is null || target is null) { r.Add(Error("TemplateConnectionEndpointMissing", $"$.internal_connections[{c.Id}]", $"Connection '{c.Id}' references a missing source or target port.", c.Id)); continue; }
            if (source.Direction is not PortDirection.Output and not PortDirection.Bidirectional) r.Add(Error("TemplateConnectionSourceDirectionInvalid", $"$.internal_connections[{c.Id}].source", $"Connection '{c.Id}' source must be output or bidirectional.", c.Id));
            if (target.Direction is not PortDirection.Input and not PortDirection.Bidirectional) r.Add(Error("TemplateConnectionTargetDirectionInvalid", $"$.internal_connections[{c.Id}].target", $"Connection '{c.Id}' target must be input or bidirectional.", c.Id));
            if (source.SignalType != target.SignalType || source.Protocol != target.Protocol) r.Add(Error("TemplateConnectionDomainMismatch", $"$.internal_connections[{c.Id}]", $"Connection '{c.Id}' connects incompatible domain/protocol endpoints.", c.Id));
            if (source.DataType != target.DataType && source.DataType != HardwareDataType.Config && target.DataType != HardwareDataType.Config) r.Add(Error("TemplateConnectionDataTypeMismatch", $"$.internal_connections[{c.Id}]", $"Connection '{c.Id}' connects incompatible data type endpoints.", c.Id));
            if (source.Precision != PrecisionKind.Any && target.Precision != PrecisionKind.Any && source.Precision != target.Precision) r.Add(Error("TemplateConnectionPrecisionMismatch", $"$.internal_connections[{c.Id}]", $"Connection '{c.Id}' connects incompatible precision endpoints.", c.Id));
        }
    }

    private static void Operation(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        var op = t.OperationContract;
        if (string.IsNullOrWhiteSpace(op.OperationName)) r.Add(Error("TemplateOperationMissing", "$.operation_contract.op_name", "Operation contract must name the supported operation."));
        if (op.InputOperands.Count == 0 || op.OutputOperands.Count == 0) r.Add(Error("TemplateOperationOperandMissing", "$.operation_contract", "Operation contract must declare input and output operands."));
        foreach (var operand in op.InputOperands.Concat(op.OutputOperands).Concat(op.StoredOperands).Where(o => string.IsNullOrWhiteSpace(o.Name) || string.IsNullOrWhiteSpace(o.DType) || o.Shape.Count == 0 || o.Shape.Any(d => d <= 0))) r.Add(Error("TemplateOperationOperandInvalid", "$.operation_contract", $"Operand '{operand.Name}' must declare name, dtype, and positive shape.", operand.Name));
        if (string.IsNullOrWhiteSpace(op.MultiplyDType) || string.IsNullOrWhiteSpace(op.AccumulateDType) || string.IsNullOrWhiteSpace(op.OutputDType)) r.Add(Error("TemplateOperationDTypeMissing", "$.operation_contract", "multiply_dtype, accumulate_dtype, and output_dtype must be explicit."));
        if (op.MultiplyDType.Equals("fp8", StringComparison.OrdinalIgnoreCase) && op.AccumulateDType.Equals("fp8", StringComparison.OrdinalIgnoreCase)) r.Add(new("TemplateFp8AccumulationRisk", ComponentTemplateIssueSeverity.Warning, "$.operation_contract.accumulate_dtype", "FP8 accumulation is explicit but carries accuracy risk."));
    }

    private static void Storage(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        foreach (var dup in t.StorageLayouts.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) r.Add(Error("TemplateStorageDuplicate", "$.storage_layouts", $"Storage layout '{dup.Key}' is duplicated.", dup.Key));
        var layouts = t.StorageLayouts.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var s in t.StorageLayouts)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || s.Rows <= 0 || s.Columns <= 0 || s.CellBits <= 0 || s.CapacityBits <= 0) r.Add(Error("TemplateStorageLayoutInvalid", $"$.storage_layouts[{s.Id}]", $"Storage layout '{s.Id}' must have positive rows, columns, cell_bits, and capacity.", s.Id));
            if (s.BitSlices.Count > 0 && s.BitSlices.Sum(b => Math.Max(0, b.BitCount)) > s.CellBits * Math.Max(1, s.BitSlices.Count)) r.Add(Error("TemplateStorageBitSliceInvalid", $"$.storage_layouts[{s.Id}].bit_slices", $"Storage layout '{s.Id}' bit slices exceed cell capacity.", s.Id));
        }
        foreach (var op in t.OperationContract.StoredOperands)
        {
            if (string.IsNullOrWhiteSpace(op.StorageRef) || !layouts.TryGetValue(op.StorageRef, out var layout)) { r.Add(Error("TemplateStorageBindingMissing", "$.operation_contract.stored_operands", $"Stored operand '{op.Name}' must bind an existing StorageMap-compatible layout.", op.Name)); continue; }
            var bits = OperandBits(op); if (bits > layout.CapacityBits) r.Add(Error("TemplateStorageCapacityExceeded", $"$.storage_layouts[{layout.Id}]", $"Stored operand '{op.Name}' requires {bits} bits but layout '{layout.Id}' capacity is {layout.CapacityBits} bits.", op.Name));
        }
    }

    private static void Profiles(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        var blocks = t.InternalBlocks.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var b in t.ProfileBindings)
        {
            if (string.IsNullOrWhiteSpace(b.BindingId)) r.Add(Error("TemplateProfileBindingIdMissing", "$.profile_bindings", "Profile binding id is required."));
            if (!string.IsNullOrWhiteSpace(b.BlockId) && !blocks.ContainsKey(b.BlockId)) r.Add(Error("TemplateProfileBindingBlockMissing", $"$.profile_bindings[{b.BindingId}]", $"Profile binding '{b.BindingId}' references missing block '{b.BlockId}'.", b.BindingId));
            if (b.Snapshot is null) r.Add(Error("TemplateProfileSnapshotMissing", $"$.profile_bindings[{b.BindingId}].snapshot", $"Profile binding '{b.BindingId}' must resolve to a precompiled snapshot before compile.", b.BindingId));
        }
    }

    private static void Lifecycle(ComponentTemplate t, ComponentTemplateValidationResult r)
    {
        if (t.Lifecycle == ComponentTemplateLifecycleState.BrokenDependency) r.Add(Error("TemplateBrokenDependency", "$.lifecycle", "Template lifecycle is BrokenDependency and cannot compile.", t.TemplateId));
        if ((t.Lifecycle == ComponentTemplateLifecycleState.Compiled || t.Lifecycle == ComponentTemplateLifecycleState.Published) && t.CompiledProfile is null) r.Add(new("TemplateCompiledProfileMissing", ComponentTemplateIssueSeverity.Warning, "$.compiled_profile", "Compiled or Published template should carry its latest compiled profile snapshot.", t.TemplateId));
    }

    private static InternalPort? FindPort(IReadOnlyDictionary<string, InternalBlock> blocks, string block, string port) => blocks.TryGetValue(block, out var b) ? b.Ports.FirstOrDefault(p => string.Equals(p.Name, port, StringComparison.OrdinalIgnoreCase)) : null;
    private static ComponentTemplateIssue Error(string code, string location, string message, string? relatedId = null) => new(code, ComponentTemplateIssueSeverity.Error, location, message, relatedId);
}

public sealed record ComponentTemplateBlockProjection(string Id, string DisplayName, string BlockKind, InternalBlockLayer Layer, IReadOnlyList<string> PortNames);
public sealed record ComponentTemplateEdgeProjection(string Id, string Source, string Target, string PayloadType);
public sealed record ComponentTemplateGraphProjection(TemplateViewKind Kind, IReadOnlyList<ComponentTemplateBlockProjection> Blocks, IReadOnlyList<ComponentTemplateEdgeProjection> Edges);

public static class ComponentTemplateProjection
{
    public static ComponentTemplateGraphProjection ProjectDataflow(ComponentTemplate template) => Project(template, TemplateViewKind.Dataflow, InternalBlockLayer.Dataflow);
    public static ComponentTemplateGraphProjection ProjectStructural(ComponentTemplate template) => Project(template, TemplateViewKind.StructuralPort, InternalBlockLayer.Structural);
    private static ComponentTemplateGraphProjection Project(ComponentTemplate template, TemplateViewKind kind, InternalBlockLayer layer)
    {
        ComponentTemplateJson.Normalize(template);
        var blockIds = template.InternalBlocks.Where(b => b.Layer == layer).Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocks = template.InternalBlocks.Where(b => b.Layer == layer).OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => new ComponentTemplateBlockProjection(b.Id, b.DisplayName, b.BlockKind, b.Layer, b.Ports.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList())).ToList();
        var edges = template.InternalConnections.Where(c => blockIds.Contains(c.SourceBlockId) && blockIds.Contains(c.TargetBlockId)).OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new ComponentTemplateEdgeProjection(c.Id, $"{c.SourceBlockId}.{c.SourcePortName}", $"{c.TargetBlockId}.{c.TargetPortName}", c.PayloadType)).ToList();
        return new(kind, blocks, edges);
    }
}

public sealed class ComponentTemplateDependencyRef
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Source { get; set; } = "";
    public string Units { get; set; } = "";
    public string ValidRange { get; set; } = "";
}

public sealed class ComponentTemplatePackage
{
    public const string CurrentSchemaVersion = "1.0";
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string PackageId { get; set; } = "";
    public string PackageVersion { get; set; } = "1.0.0";
    public List<ComponentTemplate> Templates { get; set; } = [];
    public List<ComponentTemplateDependencyRef> ModelProfileRefs { get; set; } = [];
    public List<ComponentTemplateDependencyRef> PrimitivePluginRefs { get; set; } = [];
    public string Author { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public string License { get; set; } = "";
    public string Notes { get; set; } = "";
    public string PackageHash { get; set; } = "";
    public string RefreshPackageHash()
    {
        PackageHash = ComponentTemplateJson.StableHash(new { SchemaVersion, PackageId, PackageVersion, Templates = Templates.OrderBy(t => t.TemplateId, StringComparer.Ordinal).ThenBy(t => t.Version, StringComparer.Ordinal).Select(t => ComponentTemplateJson.ComputeSemanticHash(t)), ModelProfileRefs = ModelProfileRefs.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new { r.Id, r.Version, r.Hash, r.Source, r.Units, r.ValidRange }), PrimitivePluginRefs = PrimitivePluginRefs.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new { r.Id, r.Version, r.Hash, r.Source }), Author, ToolVersion, License, Notes });
        return PackageHash;
    }
}

public static class ComponentTemplatePackageJson
{
    public static string Serialize(ComponentTemplatePackage package) { Normalize(package); package.RefreshPackageHash(); return JsonSerializer.Serialize(package, ComponentTemplateJson.Options); }
    public static ComponentTemplatePackage Deserialize(string json)
    {
        var package = JsonSerializer.Deserialize<ComponentTemplatePackage>(json, ComponentTemplateJson.Options) ?? throw new ComponentTemplateSerializationException([new("InvalidJson", ComponentTemplateIssueSeverity.Error, "$", "ComponentTemplatePackage JSON did not contain an object.")]);
        Normalize(package);
        var issue = ComponentTemplateJson.ValidateSchemaVersion(package.SchemaVersion, nameof(ComponentTemplatePackage), "$.schema_version");
        if (issue is not null) throw new ComponentTemplateSerializationException([issue]);
        return package;
    }
    public static void Normalize(ComponentTemplatePackage package)
    {
        package.SchemaVersion = string.IsNullOrWhiteSpace(package.SchemaVersion) ? ComponentTemplatePackage.CurrentSchemaVersion : package.SchemaVersion;
        package.Templates ??= []; package.ModelProfileRefs ??= []; package.PrimitivePluginRefs ??= [];
        foreach (var template in package.Templates) ComponentTemplateJson.Normalize(template);
    }
}

public sealed class ComponentTemplateLibrary
{
    private readonly List<ComponentTemplate> templates = [];
    public IReadOnlyList<ComponentTemplate> Templates => templates.OrderBy(t => t.TemplateId, StringComparer.Ordinal).ThenBy(t => t.Version, StringComparer.Ordinal).Select(ComponentTemplateJson.Clone).ToList();
    public long DirtyRevision { get; private set; }
    public void AddOrReplace(ComponentTemplate template)
    {
        ComponentTemplateJson.Normalize(template);
        templates.RemoveAll(t => string.Equals(t.TemplateId, template.TemplateId, StringComparison.OrdinalIgnoreCase) && string.Equals(t.Version, template.Version, StringComparison.OrdinalIgnoreCase));
        templates.Add(ComponentTemplateJson.Clone(template));
        DirtyRevision++;
    }
    public ComponentTemplate? Find(string templateId, string version) => templates.Where(t => string.Equals(t.TemplateId, templateId, StringComparison.OrdinalIgnoreCase) && string.Equals(t.Version, version, StringComparison.OrdinalIgnoreCase)).Select(ComponentTemplateJson.Clone).FirstOrDefault();
    public IReadOnlyList<ComponentTemplateIssue> ImportPackage(ComponentTemplatePackage package, ComponentTypeRegistry? plugins = null)
    {
        ComponentTemplatePackageJson.Normalize(package);
        var issues = new List<ComponentTemplateIssue>();
        foreach (var p in package.PrimitivePluginRefs.Where(p => plugins is not null && plugins.GetPlugin(p.Id) is null)) issues.Add(new("TemplatePackagePluginDependencyMissing", ComponentTemplateIssueSeverity.Error, "$.primitive_plugin_refs", $"Primitive plugin dependency '{p.Id}' is missing.", p.Id));
        foreach (var template in package.Templates) { if (issues.Any(i => i.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal)) template.Lifecycle = ComponentTemplateLifecycleState.BrokenDependency; AddOrReplace(template); }
        return issues;
    }
    public ComponentTemplatePackage ExportPackage(string packageId, string packageVersion, IEnumerable<(string TemplateId, string Version)> selected)
    {
        var package = new ComponentTemplatePackage { PackageId = packageId, PackageVersion = packageVersion, ToolVersion = "phase7c-mvp" };
        foreach (var key in selected.OrderBy(x => x.TemplateId, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal)) { var t = Find(key.TemplateId, key.Version); if (t is not null) package.Templates.Add(t); }
        package.RefreshPackageHash();
        return package;
    }
}

public sealed class ComponentTemplateCompileResult
{
    private ComponentTemplateCompileResult(CompiledComponentProfile? profile, IReadOnlyList<ComponentTemplateIssue> issues, IReadOnlyDictionary<string, string> derivedMetrics)
    {
        Profile = profile;
        Issues = issues;
        DerivedMetrics = derivedMetrics;
    }

    public bool IsSuccess => Profile is not null && Issues.All(i => i.Severity is not ComponentTemplateIssueSeverity.Error and not ComponentTemplateIssueSeverity.Fatal);
    public CompiledComponentProfile? Profile { get; }
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; }
    public IReadOnlyDictionary<string, string> DerivedMetrics { get; }
    public static ComponentTemplateCompileResult Success(CompiledComponentProfile profile, IReadOnlyList<ComponentTemplateIssue> issues) => new(profile, issues, profile.DerivedMetrics);
    public static ComponentTemplateCompileResult Failure(IReadOnlyList<ComponentTemplateIssue> issues, IReadOnlyDictionary<string, string>? derivedMetrics = null) => new(null, issues, derivedMetrics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
public sealed class ComponentTemplateCompiler
{
    public ComponentTemplateCompileResult Compile(
        ComponentTemplate template,
        IReadOnlyDictionary<string, string>? instanceOverrides = null,
        IReadOnlyDictionary<string, CharacterizedProfileSnapshot>? externalSnapshots = null,
        ComponentRuntimeKernelRegistrySnapshot? kernelRegistry = null)
    {
        var issues = new List<ComponentTemplateIssue>();
        var resolution = new ComponentTemplateParameterResolver().Resolve(template, instanceOverrides);
        issues.AddRange(resolution.Issues);
        var clone = resolution.Template;
        ResolveSnapshots(clone, externalSnapshots, issues);
        var snapshots = clone.ProfileBindings.Where(binding => binding.Snapshot is not null).Select(binding => binding.Snapshot!).ToList();
        var derived = new ComponentTemplateDerivedProfileCalculator().Calculate(clone, resolution.Parameters, snapshots, issues);
        issues.AddRange(new ComponentTemplateValidator().Validate(clone).Issues);
        issues.AddRange(new ComponentTemplateSemanticAnalyzer().Analyze(clone).Issues);
        CompiledComponentExecutionContract? executionContract = null;
        if (clone.ExecutionBinding is not null)
        {
            executionContract = CompileExecutionContract(clone, resolution.Parameters.Values, snapshots, derived, kernelRegistry, issues);
        }
        else if (clone.TargetKind != ComponentTemplateTargetKind.ProcessingElement)
        {
            issues.Add(new("ComponentExecutionBindingMissing", ComponentTemplateIssueSeverity.Error, "$.execution_binding", $"Target kind '{clone.TargetKind}' requires an explicit registered execution binding.", clone.TemplateId));
        }

        if (issues.Any(i => i.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal)) return ComponentTemplateCompileResult.Failure(issues, derived.Metrics);
        var profile = BuildProfile(clone, resolution.Parameters.Values, snapshots, derived, executionContract, issues);
        return ComponentTemplateCompileResult.Success(profile, issues);
    }

    private static CompiledComponentExecutionContract? CompileExecutionContract(
        ComponentTemplate template,
        Dictionary<string, string> resolvedValues,
        IReadOnlyList<CharacterizedProfileSnapshot> snapshots,
        ComponentTemplateDerivedProfile derived,
        ComponentRuntimeKernelRegistrySnapshot? kernelRegistry,
        List<ComponentTemplateIssue> issues)
    {
        var binding = template.ExecutionBinding!;
        if (kernelRegistry is null)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelMissing, ComponentTemplateIssueSeverity.Error, "$.execution_binding.kernel_id", $"Execution binding kernel '{binding.KernelId}' cannot compile without a frozen runtime kernel registry.", binding.KernelId));
            return null;
        }

        var resolution = kernelRegistry.ResolveRequirement(binding.KernelId, binding.KernelVersionRequirement, binding.ContractSchemaId);
        foreach (var issue in resolution.Issues)
        {
            issues.Add(new(issue.Code, issue.Severity == ValidationSeverity.Error ? ComponentTemplateIssueSeverity.Error : ComponentTemplateIssueSeverity.Warning, issue.Location, issue.Message, issue.RelatedId));
        }
        if (!resolution.IsSuccess || resolution.Registration is null) return null;
        var registration = resolution.Registration;
        var descriptor = registration.Descriptor;
        if (descriptor.SupportedOperationKinds.Count > 0 && !descriptor.SupportedOperationKinds.Any(kind => string.Equals(kind, binding.OperationKind, StringComparison.Ordinal)))
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelIncompatible, ComponentTemplateIssueSeverity.Error, "$.execution_binding.operation_kind", $"Kernel '{descriptor.KernelId}' does not support operation kind '{binding.OperationKind}'.", descriptor.KernelId));
            return null;
        }
        if (registration.Factory is not IComponentExecutionContractCompiler compiler)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_binding", $"Kernel '{descriptor.KernelId}' does not provide an execution-contract compiler.", descriptor.KernelId));
            return null;
        }

        var configurationValues = ResolveExecutionConfigurationBindings(binding, resolvedValues, derived.Metrics, issues);
        if (issues.Any(issue => issue.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal)) return null;

        ComponentExecutionContractCompileResult result;
        try
        {
            result = compiler.CompileExecutionContract(new ComponentExecutionContractCompileContext(
                ComponentTemplateJson.Clone(template),
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(resolvedValues, StringComparer.OrdinalIgnoreCase)),
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(derived.Metrics, StringComparer.OrdinalIgnoreCase)),
                configurationValues,
                snapshots.ToList().AsReadOnly(),
                descriptor,
                kernelRegistry.ContentHash));
        }
        catch (Exception exception)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_binding", $"Kernel execution-contract compiler failed: {exception.Message}", descriptor.KernelId));
            return null;
        }

        if (result is null)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_binding", "Kernel execution-contract compiler returned no result.", descriptor.KernelId));
            return null;
        }

        issues.AddRange(result.Issues);
        if (!result.IsSuccess || result.Contract is null) return null;
        var contract = result.Contract;
        try
        {
            ComponentExecutionJson.Normalize(contract);
        }
        catch (Exception exception)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_contract.kernel_configuration", $"Kernel configuration is not canonicalizable: {exception.Message}", descriptor.KernelId));
            return null;
        }
        var identityMatches =
            string.Equals(contract.KernelId, descriptor.KernelId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(contract.KernelVersion, descriptor.KernelVersion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(contract.ContractSchemaId, descriptor.ContractSchemaId, StringComparison.Ordinal) &&
            string.Equals(contract.OperationKind, binding.OperationKind, StringComparison.Ordinal) &&
            string.Equals(contract.KernelConfiguration.SchemaId, descriptor.ContractSchemaId, StringComparison.Ordinal);
        if (!identityMatches)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelIncompatible, ComponentTemplateIssueSeverity.Error, "$.execution_contract", "Kernel compiler returned a contract whose exact id, version, schema, operation, or configuration schema does not match the resolved binding.", descriptor.KernelId));
            return null;
        }
        if ((!string.IsNullOrWhiteSpace(contract.Provenance.KernelImplementationHash) && !string.Equals(contract.Provenance.KernelImplementationHash, descriptor.ImplementationHash, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(contract.Provenance.RegistrySnapshotHash) && !string.Equals(contract.Provenance.RegistrySnapshotHash, kernelRegistry.ContentHash, StringComparison.Ordinal)))
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelIncompatible, ComponentTemplateIssueSeverity.Error, "$.execution_contract.provenance", "Kernel compiler returned implementation or registry provenance that does not match the frozen registry.", descriptor.KernelId));
            return null;
        }

        contract.Provenance.KernelImplementationHash = descriptor.ImplementationHash;
        contract.Provenance.RegistrySnapshotHash = kernelRegistry.ContentHash;
        try
        {
            contract.RefreshContractHash();
        }
        catch (Exception exception)
        {
            issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_contract.kernel_configuration", $"Kernel configuration is not canonicalizable: {exception.Message}", descriptor.KernelId));
            return null;
        }
        return contract;
    }
    private static IReadOnlyDictionary<string, string> ResolveExecutionConfigurationBindings(
        ComponentTemplateExecutionBinding binding,
        IReadOnlyDictionary<string, string> resolvedValues,
        IReadOnlyDictionary<string, string> derivedMetrics,
        List<ComponentTemplateIssue> issues)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in binding.ConfigurationBindings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var destination = pair.Key?.Trim() ?? "";
            var source = pair.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(destination))
            {
                issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, "$.execution_binding.configuration_bindings", "Kernel configuration binding destination field is required.", binding.KernelId));
                continue;
            }

            string? value = null;
            if (source.StartsWith("resolved.", StringComparison.Ordinal))
            {
                resolvedValues.TryGetValue(source["resolved.".Length..], out value);
            }
            else if (source.StartsWith("derived.", StringComparison.Ordinal))
            {
                derivedMetrics.TryGetValue(source["derived.".Length..], out value);
            }
            else if (source.StartsWith("constant:", StringComparison.Ordinal))
            {
                value = source["constant:".Length..];
            }

            if (value is null)
            {
                issues.Add(new(ComponentExecutionIssueCodes.KernelConfigurationInvalid, ComponentTemplateIssueSeverity.Error, $"$.execution_binding.configuration_bindings.{destination}", $"Configuration source '{source}' did not resolve to a typed parameter, derived metric, or explicit constant.", binding.KernelId));
                continue;
            }
            values[destination] = value;
        }
        return new ReadOnlyDictionary<string, string>(values);
    }

    private static Dictionary<string, string> ValidateOverrides(ComponentTemplate template, IReadOnlyDictionary<string, string> input, List<ComponentTemplateIssue> issues)
    {
        var result = template.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in input.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var schema = template.Parameters.FirstOrDefault(p => string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (schema is null) { issues.Add(new("TemplateParameterUnknown", ComponentTemplateIssueSeverity.Error, "$.instance_overrides", $"Override parameter '{pair.Key}' is not declared by the template.", pair.Key)); continue; }
            if (!ParameterValid(schema, pair.Value, out var message)) { issues.Add(new("TemplateParameterInvalid", ComponentTemplateIssueSeverity.Error, $"$.instance_overrides.{pair.Key}", message, pair.Key)); continue; }
            result[pair.Key] = pair.Value;
        }
        foreach (var required in template.Parameters.Where(p => p.Required && string.IsNullOrWhiteSpace(result.GetValueOrDefault(p.Name)))) issues.Add(new("TemplateParameterRequired", ComponentTemplateIssueSeverity.Error, $"$.parameters[{required.Name}]", $"Required template parameter '{required.Name}' has no default or override.", required.Name));
        return result;
    }

    private static bool ParameterValid(TemplateParameter schema, string value, out string message)
    {
        message = "";
        if (schema.ValueKind == TemplateParameterValueKind.Enum && !schema.AllowedValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))) { message = $"Parameter '{schema.Name}' value '{value}' is not allowed."; return false; }
        if (schema.ValueKind is TemplateParameterValueKind.Integer or TemplateParameterValueKind.Number)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || double.IsNaN(parsed) || double.IsInfinity(parsed)) { message = $"Parameter '{schema.Name}' must be numeric."; return false; }
            if (schema.ValueKind == TemplateParameterValueKind.Integer && Math.Abs(parsed - Math.Round(parsed)) > 0) { message = $"Parameter '{schema.Name}' must be an integer."; return false; }
            if ((schema.Minimum.HasValue && parsed < schema.Minimum.Value) || (schema.Maximum.HasValue && parsed > schema.Maximum.Value)) { message = $"Parameter '{schema.Name}' is outside its valid range."; return false; }
        }
        if (schema.ValueKind == TemplateParameterValueKind.Boolean && !bool.TryParse(value, out _)) { message = $"Parameter '{schema.Name}' must be boolean."; return false; }
        return true;
    }

    private static void ResolveSnapshots(ComponentTemplate template, IReadOnlyDictionary<string, CharacterizedProfileSnapshot>? external, List<ComponentTemplateIssue> issues)
    {
        foreach (var b in template.ProfileBindings.Where(b => b.Snapshot is null))
        {
            if (external is not null && external.TryGetValue(b.ProfileId, out var snapshot)) b.Snapshot = snapshot;
            else issues.Add(new("TemplateProfileSnapshotMissing", ComponentTemplateIssueSeverity.Error, $"$.profile_bindings[{b.BindingId}].snapshot", $"Profile binding '{b.BindingId}' cannot resolve snapshot '{b.ProfileId}'.", b.BindingId));
        }
    }

    private static CompiledComponentProfile BuildProfile(ComponentTemplate template, Dictionary<string, string> resolvedValues, IReadOnlyList<CharacterizedProfileSnapshot> snapshots, ComponentTemplateDerivedProfile derived, CompiledComponentExecutionContract? executionContract, List<ComponentTemplateIssue> issues)
    {
        var semanticHash = ComponentTemplateJson.ComputeSemanticHash(template, resolvedValues, snapshots);
        var compiledEnergy = new Dictionary<string, double>(derived.EnergyPicojoules, StringComparer.OrdinalIgnoreCase);
        if (executionContract is not null)
        {
            foreach (var resource in executionContract.Resources.Where(resource => resource.ResourceKind.Contains("energy", StringComparison.OrdinalIgnoreCase) && resource.Units.StartsWith("pJ", StringComparison.OrdinalIgnoreCase)))
            {
                if (compiledEnergy.Count > 0 && resource.Name.StartsWith("total_", StringComparison.OrdinalIgnoreCase)) continue;
                if (double.TryParse(resource.CanonicalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0) compiledEnergy["kernel:" + resource.Name] = value;
            }
        }
        var physicalFootprint = PhysicalFootprintResolver.ResolveTemplateFloorplan(template);
var physicalFootprintAuthoritative = template.Provenance.DependencyHashes.TryGetValue("phase9_physical_footprint_authoritative", out var physicalAuthority) &&
            string.Equals(physicalAuthority, "true", StringComparison.OrdinalIgnoreCase);
        var profileHash = ComponentTemplateJson.StableHash(new
        {
            semanticHash,
            DerivedMetrics = derived.Metrics.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }),
            Energy = compiledEnergy.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }),
            Area = derived.AreaUm2.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }),
            Capacity = derived.Capacity.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }),
            ExecutionContractHash = executionContract?.ContractHash ?? "",
            PhysicalFootprintHash = physicalFootprintAuthoritative ? physicalFootprint.FootprintHash : ""
        });
        var syntheticMetric = derived.Metrics.TryGetValue("synthetic_profile_only", out var syntheticValue) && string.Equals(syntheticValue, "true", StringComparison.OrdinalIgnoreCase);
        var synthetic = template.ProfileBindings.Any(b => b.Synthetic) || snapshots.Any(s => s.Source.Contains("synthetic", StringComparison.OrdinalIgnoreCase)) || syntheticMetric;
        if (synthetic) issues.Add(new("TemplateSyntheticProfile", ComponentTemplateIssueSeverity.Warning, "$.profile_bindings", "Compiled profile uses synthetic characterized data and must not be presented as real device accuracy.", template.TemplateId));
        var trace = executionContract is null
            ? new List<string> { "pe_shell_summary", "pe_output_route" }
            : executionContract.TraceDescriptors.Select(descriptor => descriptor.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
        trace.AddRange(template.InternalBlocks.Where(b => !string.IsNullOrWhiteSpace(b.TraceStage)).Select(b => "internal:" + b.TraceStage).OrderBy(s => s, StringComparer.Ordinal));
        var provenance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["semantic_hash"] = semanticHash,
            ["synthetic_profile"] = synthetic.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            ["synthetic_profile_only"] = synthetic.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            ["default_response_target_policy"] = template.TimingContract.DefaultResponseTargetPolicy.ToString(),
            ["profile_snapshot_hashes"] = string.Join(",", snapshots.Select(s => s.Hash).OrderBy(s => s, StringComparer.Ordinal)),
            ["structural_metric_semantics"] = "internal structural block energy_pj and area_um2 are declared additive contributions, not measured characterization",
            ["physical_footprint_hash"] = physicalFootprint.FootprintHash,
            ["physical_footprint_method"] = physicalFootprint.MethodId,
            ["physical_footprint_authoritative"] = physicalFootprintAuthoritative.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()
        };
        if (executionContract is not null)
        {
            provenance["execution_contract_hash"] = executionContract.ContractHash;
            provenance["execution_kernel_id"] = executionContract.KernelId;
            provenance["execution_kernel_version"] = executionContract.KernelVersion;
            provenance["execution_contract_schema_id"] = executionContract.ContractSchemaId;
            provenance["runtime_kernel_registry_hash"] = executionContract.Provenance.RegistrySnapshotHash;
        }
        return new CompiledComponentProfile
        {
            TemplateId = template.TemplateId,
            TemplateVersion = template.Version,
            TargetKind = template.TargetKind,
            SupportedOperations = [template.OperationContract.OperationName],
            InstanceOverrides = new Dictionary<string, string>(resolvedValues, StringComparer.OrdinalIgnoreCase),
            ShapeContract = ShapeContract(template),
            OperationLatency = executionContract?.Timing.OperationLatencyCycles ?? derived.OperationLatency,
            PipelineLatency = executionContract?.Timing.PipelineLatencyCycles ?? derived.PipelineLatency,
            IssueInterval = executionContract?.Timing.IssueIntervalCycles ?? derived.IssueInterval,
            InputQueueDepth = executionContract?.Queues.InputDepth ?? derived.InputQueueDepth,
            OutputQueueDepth = executionContract?.Queues.OutputDepth ?? derived.OutputQueueDepth,
            DefaultResponseTargetPolicy = template.TimingContract.DefaultResponseTargetPolicy,
            Capacity = new Dictionary<string, long>(derived.Capacity, StringComparer.OrdinalIgnoreCase),
            BandwidthBitsPerCycle = Bandwidth(template),
            EnergyPicojoules = compiledEnergy,
            AreaUm2 = new Dictionary<string, double>(derived.AreaUm2, StringComparer.OrdinalIgnoreCase),
            PhysicalFootprint = physicalFootprint,
            TraceDescriptors = trace,
            InternalDrilldownStages = template.InternalBlocks.Where(b => !string.IsNullOrWhiteSpace(b.TraceStage)).Select(b => b.TraceStage).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            AggregationRules = new(StringComparer.OrdinalIgnoreCase) { ["latency"] = "base_latency+delta_compute+delta_pipeline+delta_adc+dac;queue_wait_runtime", ["energy"] = "synthetic device terms plus declared structural block contributions sum to total_dynamic_energy_pj", ["area"] = "synthetic device areas plus declared structural block contributions sum to total_area_um2", ["bandwidth"] = "bottleneck min with ingress/internal/egress separate" },
            DerivedMetrics = new Dictionary<string, string>(derived.Metrics, StringComparer.OrdinalIgnoreCase),
            Provenance = provenance,
            ExecutionContract = executionContract,
            ProfileHash = profileHash
        };
    }
    private static bool IsComputeBlock(InternalBlock b) => b.BlockKind.Contains("compute", StringComparison.OrdinalIgnoreCase) || b.BlockKind.Contains("mac", StringComparison.OrdinalIgnoreCase) || b.BlockKind.Contains("array", StringComparison.OrdinalIgnoreCase);
    private static Dictionary<string, string> ShapeContract(ComponentTemplate t) => t.OperationContract.InputOperands.Concat(t.OperationContract.OutputOperands).Concat(t.OperationContract.StoredOperands).OrderBy(o => o.Name, StringComparer.Ordinal).ToDictionary(o => o.Name, o => $"{string.Join("x", o.Shape)} {o.DType}", StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, double> Bandwidth(ComponentTemplate t) => new(StringComparer.OrdinalIgnoreCase) { ["ingress"] = t.ExternalPorts.Where(p => p.Direction != PortDirection.Output).Select(p => (double)p.BandwidthBitsPerCycle).DefaultIfEmpty(0).Max(), ["internal_bottleneck"] = t.InternalConnections.Select(c => (double)Math.Max(0, c.BandwidthBitsPerCycle)).DefaultIfEmpty(0).Min(), ["egress"] = t.ExternalPorts.Where(p => p.Direction != PortDirection.Input).Select(p => (double)p.BandwidthBitsPerCycle).DefaultIfEmpty(0).Max() };
}

public static class ComponentTemplateExamples
{
    public static ComponentTemplate PeArray32x32Fp8SramSynthetic() => CreatePeArray("PE_Array_32x32_FP8_SRAM_Synthetic", "PE Array 32x32 FP8 SRAM Synthetic", 1, false, "phase7c.synthetic.sram.profile");
    public static ComponentTemplate PeArray32x32Fp8ReramLikeSynthetic() => CreatePeArray("PE_Array_32x32_FP8_ReRAMLike_Synthetic", "PE Array 32x32 FP8 ReRAM-Like Synthetic", 4, true, "phase7c.synthetic.reram_like.profile");

    private static ComponentTemplate CreatePeArray(string id, string name, int cellBits, bool reramLike, string source)
    {
        var t = new ComponentTemplate
        {
            TemplateId = id, DisplayName = name, Version = "1.0.0", Category = "ProcessingElement", TargetKind = ComponentTemplateTargetKind.ProcessingElement, Lifecycle = ComponentTemplateLifecycleState.Published,
            Provenance = new() { Source = source, Author = "HardwareSim Phase7C", ToolVersion = "phase7c-mvp", Warnings = ["Synthetic profile only; does not claim real ReRAM, ADC, DAC, or analog accuracy."] },
            Parameters = [IntParam("array_rows", "32", 1, 4096), IntParam("array_cols", "32", 1, 4096), EnumParam("input_dtype", "fp8", ["fp8", "fp16", "fp32"]), EnumParam("weight_dtype", "fp8", ["fp8", "fp16", "fp32"]), EnumParam("output_dtype", "fp8", ["fp8", "fp16", "fp32"]), EnumParam("accumulate_dtype", "fp16", ["fp16", "fp32", "fp8"]), IntParam("cell_bits", cellBits.ToString(CultureInfo.InvariantCulture), 1, 8), IntParam("adc_bits", reramLike ? "8" : "0", 0, 16), IntParam("dac_bits", reramLike ? "8" : "0", 0, 16), IntParam("input_queue_depth", "2", 1, 64), IntParam("output_queue_depth", "2", 1, 64), IntParam("issue_interval_override", "1", 1, 64), IntParam("macs_per_cycle", "1024", 1, 1048576), IntParam("pipeline_latency", (reramLike ? 3 : 1).ToString(CultureInfo.InvariantCulture), 0, 128), IntParam("weight_write_bandwidth_bits_per_cycle", "1024", 1, 1048576), IntParam("weight_write_latency_cycles", "1", 0, 128)],
            ExternalPorts = [External("in_activation", PortDirection.Input, HardwareDataType.Tensor, PrecisionKind.FP8_E4M3, [1, 32], "ingress", "activation_in"), External("in_weight", PortDirection.Input, HardwareDataType.Tensor, PrecisionKind.FP8_E4M3, [32, 32], "weight_store", "weight_in"), External("out_result", PortDirection.Output, HardwareDataType.Tensor, PrecisionKind.FP8_E4M3, [1, 32], "egress", "result_out"), External("ctrl", PortDirection.Input, HardwareDataType.Config, PrecisionKind.Any, [], "controller", "ctrl")],
            InternalBlocks = Blocks(reramLike), InternalConnections = Connections(reramLike),
            Views = [new TemplateView { Kind = TemplateViewKind.Symbol, Metadata = { ["glyph"] = "PE" } }, new TemplateView { Kind = TemplateViewKind.Dataflow, Layout = { ["ingress_df"] = new GridPosition(0, 0), ["input_buffer_df"] = new GridPosition(2, 0), ["weight_store_df"] = new GridPosition(2, 2), ["compute_df"] = new GridPosition(5, 0), ["accumulator_df"] = new GridPosition(7, 2), ["egress_df"] = new GridPosition(9, 0) } }, new TemplateView { Kind = TemplateViewKind.StructuralPort, Layout = StructuralLayout(reramLike) }, new TemplateView { Kind = TemplateViewKind.ModelProfile }, new TemplateView { Kind = TemplateViewKind.Storage }, new TemplateView { Kind = TemplateViewKind.CompiledProfile }],
            OperationContract = new() { OperationName = "vector_matrix_multiply", InputOperands = [new() { Name = "activation", Shape = [1, 32], DType = "fp8", Layout = "row_vector" }], StoredOperands = [new() { Name = "weight", Shape = [32, 32], DType = "fp8", Layout = "matrix", StorageRef = "weight_store_0" }], OutputOperands = [new() { Name = "result", Shape = [1, 32], DType = "fp8", Layout = "row_vector" }], Equation = "Y = X @ W", MultiplyDType = "fp8", AccumulateDType = "fp16", OutputDType = "fp8", Quantization = new() { Mode = "nearest_even", Saturation = true } },
            TimingContract = new() { InputQueueDepth = 2, OutputQueueDepth = 2, IssueInterval = 1, PipelineLatency = reramLike ? 3 : 1, OperationLatency = reramLike ? 14 : 12, CanAcceptWhileBusy = true, DefaultResponseTargetPolicy = TemplateCollectionTargetPolicy.IngressRouter },
            ExecutionBinding = new ComponentTemplateExecutionBinding
            {
                KernelId = CoreDigitalVmmKernelFactory.KernelId,
                KernelVersionRequirement = "1.x",
                ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId,
                OperationKind = "digital_vmm",
                ConfigurationBindings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rows"] = "resolved.array_rows",
                    ["columns"] = "resolved.array_cols",
                    ["input_dtype"] = "resolved.input_dtype",
                    ["weight_dtype"] = "resolved.weight_dtype",
                    ["accumulate_dtype"] = "resolved.accumulate_dtype",
                    ["output_dtype"] = "resolved.output_dtype",
                    ["macs_per_cycle"] = "resolved.macs_per_cycle",
                    ["operation_latency_cycles"] = "derived.operation_latency",
                    ["pipeline_latency_cycles"] = "resolved.pipeline_latency",
                    ["issue_interval_cycles"] = "resolved.issue_interval_override",
                    ["input_queue_depth"] = "resolved.input_queue_depth",
                    ["output_queue_depth"] = "resolved.output_queue_depth",
                    ["storage_capacity_bits"] = "derived.storage_capacity_bits",
                    ["weight_write_bandwidth_bits_per_cycle"] = "resolved.weight_write_bandwidth_bits_per_cycle",
                    ["weight_write_latency_cycles"] = "resolved.weight_write_latency_cycles",
                    ["total_dynamic_energy_pj"] = "derived.total_dynamic_energy_pj",
                    ["weight_write_energy_pj"] = "derived.weight_write_energy_pj",
                    ["total_area_um2"] = "derived.total_area_um2"
                }
            },
            StorageLayouts = [new() { Id = "weight_store_0", LogicalName = "weight", Banks = 1, Rows = cellBits == 1 ? 64 : 32, Columns = 128, CellBits = cellBits, Encoding = reramLike ? "synthetic_reram_like_unsigned_bit_sliced" : "fp8_sram_synthetic", Signed = true, BitSlices = cellBits == 4 ? [new() { LogicalBitStart = 0, BitCount = 4, CellBitStart = 0 }, new() { LogicalBitStart = 4, BitCount = 4, CellBitStart = 0 }] : [new() { LogicalBitStart = 0, BitCount = 1, CellBitStart = 0 }], MissingAddressBehavior = "structured_error" }],
            ProfileBindings = [Binding("binding_energy", "compute_core", source, reramLike ? 7.5 : 4.0)]
        };
        var frozenKernels = ComponentTypeRegistry.CreateDefault().FreezeRuntimeKernels();
        if (!frozenKernels.IsSuccess || frozenKernels.Snapshot is null)
        {
            throw new InvalidOperationException(string.Join("; ", frozenKernels.Issues.Select(issue => issue.Message)));
        }
        var compile = new ComponentTemplateCompiler().Compile(t, kernelRegistry: frozenKernels.Snapshot);
        if (compile.Profile is not null) { t.CompiledProfile = compile.Profile; t.Provenance.CompileHash = compile.Profile.ProfileHash; }
        return t;
    }

    private static List<InternalBlock> Blocks(bool reram)
    {
        var blocks = new List<InternalBlock>
        {
            Dataflow("ingress_df", "Ingress", ["ingress"], [In("activation_in"), Out("activation_out")]),
            Dataflow("input_buffer_df", "InputBuffer", ["input_buffer"], [In("in"), Out("out")]),
            Dataflow("weight_store_df", "WeightStore", ["weight_store"], [In("weight_in"), Out("weight_out")]),
            Dataflow("compute_df", "ComputeCore", ["compute_core"], [In("activation_in"), In("weight_in"), Out("psum_out")]),
            Dataflow("accumulator_df", "Accumulator", ["accumulator"], [In("in"), Out("out")]),
            Dataflow("egress_df", "Egress", ["egress"], [In("in"), Out("out")]),
            Structural("ingress", "Ingress", [In("activation_in"), In("enable", HardwareDataType.Config), Out("activation_out")]),
            Structural("input_buffer", "InputBuffer", [In("in"), In("enable", HardwareDataType.Config), Out("out")], 0.2, 400, "buffer_wait"),
            Structural("weight_store", "WeightStore", [In("weight_in"), In("read_enable", HardwareDataType.Config), Out("weight_out")], 0.4, 1400, "weight_read"),
            Structural(
                "compute_core",
                reram ? "ReRAMLikeArray" : "ComputeCore",
                reram
                    ? [In("activation_in", HardwareDataType.Tensor, SignalType.Analog), In("weight_in"), In("wordline", HardwareDataType.Config), In("issue", HardwareDataType.Config), Out("psum_out", HardwareDataType.Tensor, SignalType.Analog)]
                    : [In("activation_in"), In("weight_in"), In("issue", HardwareDataType.Config), Out("psum_out")],
                reram ? 1.8 : 2.4,
                5000,
                "compute_issue"),
            Structural("accumulator", "Accumulator", [In("psum_in"), In("enable", HardwareDataType.Config), Out("result_out")], 0.5, 900, "accumulate"),
            Structural("controller", "Controller", [In("ctrl", HardwareDataType.Config), Out("issue", HardwareDataType.Config)], 0.2, 600, "controller"),
            Structural("egress", "Egress", [In("result_in"), In("enable", HardwareDataType.Config), Out("result_out")], 0, 0, "egress")
        };
        if (reram)
        {
            blocks.Add(Structural("decoder", "Decoder", [In("address", HardwareDataType.Config), Out("wordline", HardwareDataType.Config)], 0.25, 700, "decode"));
            blocks.Add(Structural("dac_like", "DACLikeInputConversion", [In("digital_in"), In("enable", HardwareDataType.Config), Out("analog_placeholder", HardwareDataType.Tensor, SignalType.Analog)], 0.9, 800, "dac_like"));
            blocks.Add(Structural("adc_like", "ADCLikeOutputConversion", [In("analog_placeholder", HardwareDataType.Tensor, SignalType.Analog), In("enable", HardwareDataType.Config), Out("digital_out")], 1.1, 900, "adc_like"));
        }
        return blocks;
    }

    private static List<InternalConnection> Connections(bool reram)
    {
        var connections = new List<InternalConnection>
        {
            Conn("df_ingress_buffer", "ingress_df", "activation_out", "input_buffer_df", "in"),
            Conn("df_buffer_compute", "input_buffer_df", "out", "compute_df", "activation_in"),
            Conn("df_weight_compute", "weight_store_df", "weight_out", "compute_df", "weight_in"),
            Conn("df_compute_acc", "compute_df", "psum_out", "accumulator_df", "in"),
            Conn("df_acc_egress", "accumulator_df", "out", "egress_df", "in"),
            Conn("s_ingress_buffer", "ingress", "activation_out", "input_buffer", "in"),
            Conn("s_weight_compute", "weight_store", "weight_out", "compute_core", "weight_in"),
            Conn("s_acc_egress", "accumulator", "result_out", "egress", "result_in"),
            Conn("s_ctrl_ingress", "controller", "issue", "ingress", "enable", HardwareDataType.Config),
            Conn("s_ctrl_input_buffer", "controller", "issue", "input_buffer", "enable", HardwareDataType.Config),
            Conn("s_ctrl_weight_store", "controller", "issue", "weight_store", "read_enable", HardwareDataType.Config),
            Conn("s_ctrl_compute", "controller", "issue", "compute_core", "issue", HardwareDataType.Config),
            Conn("s_ctrl_accumulator", "controller", "issue", "accumulator", "enable", HardwareDataType.Config),
            Conn("s_ctrl_egress", "controller", "issue", "egress", "enable", HardwareDataType.Config)
        };
        if (reram)
        {
            connections.Add(Conn("s_buffer_dac", "input_buffer", "out", "dac_like", "digital_in"));
            connections.Add(Conn("s_dac_array", "dac_like", "analog_placeholder", "compute_core", "activation_in", HardwareDataType.Tensor, SignalType.Analog));
            connections.Add(Conn("s_array_adc", "compute_core", "psum_out", "adc_like", "analog_placeholder", HardwareDataType.Tensor, SignalType.Analog));
            connections.Add(Conn("s_adc_acc", "adc_like", "digital_out", "accumulator", "psum_in"));
            connections.Add(Conn("s_ctrl_decoder", "controller", "issue", "decoder", "address", HardwareDataType.Config));
            connections.Add(Conn("s_decoder_array", "decoder", "wordline", "compute_core", "wordline", HardwareDataType.Config));
            connections.Add(Conn("s_ctrl_dac", "controller", "issue", "dac_like", "enable", HardwareDataType.Config));
            connections.Add(Conn("s_ctrl_adc", "controller", "issue", "adc_like", "enable", HardwareDataType.Config));
        }
        else
        {
            connections.Add(Conn("s_buffer_compute", "input_buffer", "out", "compute_core", "activation_in"));
            connections.Add(Conn("s_compute_acc", "compute_core", "psum_out", "accumulator", "psum_in"));
        }
        return connections;
    }

    private static Dictionary<string, GridPosition> StructuralLayout(bool reram)
    {
        if (reram)
        {
            return new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase)
            {
                ["ingress"] = new GridPosition(0, 3),
                ["input_buffer"] = new GridPosition(3, 3),
                ["dac_like"] = new GridPosition(6, 3),
                ["compute_core"] = new GridPosition(9, 3),
                ["adc_like"] = new GridPosition(12, 3),
                ["accumulator"] = new GridPosition(15, 3),
                ["egress"] = new GridPosition(18, 3),
                ["weight_store"] = new GridPosition(6, 6),
                ["controller"] = new GridPosition(6, 0),
                ["decoder"] = new GridPosition(9, 0)
            };
        }

        return new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ingress"] = new GridPosition(0, 3),
            ["input_buffer"] = new GridPosition(3, 3),
            ["compute_core"] = new GridPosition(6, 3),
            ["accumulator"] = new GridPosition(9, 3),
            ["egress"] = new GridPosition(12, 3),
            ["weight_store"] = new GridPosition(3, 6),
            ["controller"] = new GridPosition(6, 0)
        };
    }
    private static TemplateParameter IntParam(string name, string value, int min, int max) => new() { Name = name, ValueKind = TemplateParameterValueKind.Integer, DefaultValue = value, Minimum = min, Maximum = max, Units = "count" };
    private static TemplateParameter EnumParam(string name, string value, List<string> allowed) => new() { Name = name, ValueKind = TemplateParameterValueKind.Enum, DefaultValue = value, AllowedValues = allowed };
    private static TemplateExternalPort External(string name, PortDirection direction, HardwareDataType dataType, PrecisionKind precision, List<int> shape, string block, string port) => new() { Name = name, Direction = direction, SignalType = dataType == HardwareDataType.Config ? SignalType.Control : SignalType.Digital, DataType = dataType, Precision = precision, Protocol = PortProtocol.Packet, Shape = shape, Required = true, ShellBlockId = block, ShellPortName = port };
    private static InternalBlock Dataflow(string id, string kind, List<string> mapped, List<InternalPort> ports) => new() { Id = id, DisplayName = kind, BlockKind = kind, Layer = InternalBlockLayer.Dataflow, MappedStructuralBlockIds = mapped, Ports = ports };
    private static InternalBlock Structural(string id, string kind, List<InternalPort> ports, double energy = 0, double area = 0, string stage = "") => new() { Id = id, DisplayName = kind, BlockKind = kind, Layer = InternalBlockLayer.Structural, Ports = ports, EnergyPicojoules = energy, AreaUm2 = area, TraceStage = stage };
    private static InternalPort In(string name, HardwareDataType dataType = HardwareDataType.Tensor, SignalType signal = SignalType.Digital) => new() { Name = name, Direction = PortDirection.Input, SignalType = dataType == HardwareDataType.Config ? SignalType.Control : signal, DataType = dataType, Precision = dataType == HardwareDataType.Config ? PrecisionKind.Any : PrecisionKind.FP8_E4M3, Protocol = PortProtocol.Packet, WidthBits = 256, Shape = dataType == HardwareDataType.Tensor ? [1, 32] : [] };
    private static InternalPort Out(string name, HardwareDataType dataType = HardwareDataType.Tensor, SignalType signal = SignalType.Digital) => new() { Name = name, Direction = PortDirection.Output, SignalType = dataType == HardwareDataType.Config ? SignalType.Control : signal, DataType = dataType, Precision = dataType == HardwareDataType.Config ? PrecisionKind.Any : PrecisionKind.FP8_E4M3, Protocol = PortProtocol.Packet, WidthBits = 256, Shape = dataType == HardwareDataType.Tensor ? [1, 32] : [] };
    private static InternalConnection Conn(string id, string sb, string sp, string tb, string tp, HardwareDataType dataType = HardwareDataType.Tensor, SignalType signal = SignalType.Digital) => new() { Id = id, SourceBlockId = sb, SourcePortName = sp, TargetBlockId = tb, TargetPortName = tp, PayloadType = dataType.ToString().ToLowerInvariant(), Precision = dataType == HardwareDataType.Tensor ? PrecisionKind.FP8_E4M3 : PrecisionKind.Any, BandwidthBitsPerCycle = 256 };
    private static TemplateProfileBinding Binding(string id, string block, string source, double energy) => new() { BindingId = id, BlockId = block, ProfileId = id + ".profile", ModelId = id + ".model", Synthetic = true, Snapshot = Snapshot(id + ".profile", block, energy, source) };
    private static CharacterizedProfileSnapshot Snapshot(string id, string target, double value, string source) => new() { Id = id, TargetKind = ModelBindingTargetKind.Component, TargetId = target, ModelId = id + ".model", OutputQuantity = "synthetic_read_energy", Units = "pJ", Value = value, Source = source + ":synthetic_profile_only", Version = "1.0.0", Calibrated = false, Hash = ComponentTemplateJson.StableHash(new { id, target, value, source, synthetic = true }) };
}
