namespace HardwareSim.Core;

/// <summary>Defines the formal Phase 3A workload operation type values.</summary>
public enum OpType
{
    /// <summary>Performs a matrix multiplication.</summary>
    MatMul,
    /// <summary>Computes query-key attention scores.</summary>
    Attention_QK,
    /// <summary>Computes normalized softmax values.</summary>
    Softmax,
    /// <summary>Applies attention scores to value vectors.</summary>
    Attention_V,
    /// <summary>Performs convolution over tensor windows.</summary>
    Conv,
    /// <summary>Performs elementwise tensor operations.</summary>
    Elementwise,
    /// <summary>Performs tensor reduction operations.</summary>
    Reduce,
    /// <summary>Represents a caller-defined operation.</summary>
    Custom
}
/// <summary>Defines the supported workload operation kind values used by hardware simulation contracts.</summary>
public enum WorkloadOperationKind
{
    /// <summary>Selects the synthetic traffic value for the workload operation kind contract.</summary>
    SyntheticTraffic,
    /// <summary>Selects the mat mul value for the workload operation kind contract.</summary>
    MatMul,
    /// <summary>Selects the attention qk t value for the workload operation kind contract.</summary>
    AttentionQkT,
    /// <summary>Selects the softmax value for the workload operation kind contract.</summary>
    Softmax,
    /// <summary>Selects the operation that applies attention weights to values.</summary>
    AttentionValue,
    /// <summary>Selects the mlp value for the workload operation kind contract.</summary>
    Mlp
}

/// <summary>Represents tensor shape data exchanged by hardware design and simulation workflows.</summary>
public sealed class TensorShape
{
    /// <summary>Gets or sets the dimensions collection carried by the enclosing tensor shape contract.</summary>
    public List<int> Dimensions { get; set; } = [];
    /// <summary>Gets the product of nonnegative dimensions, or zero for an empty shape.</summary>
    public long ElementCount => Dimensions.Count == 0 ? 0 : Dimensions.Aggregate(1L, (acc, dim) => acc * Math.Max(0, dim));
}

/// <summary>Represents workload operation data exchanged by hardware design and simulation workflows.</summary>
public sealed class WorkloadOperation
{
    private WorkloadOperationKind? legacyOperationKind;

    /// <summary>Gets or sets the id value carried by the enclosing workload operation contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the formal Phase 3A operation type.</summary>
    public OpType Type { get; set; } = OpType.Custom;
    /// <summary>Gets or sets the operation kind value carried by the legacy workload operation contract.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public WorkloadOperationKind OperationKind
    {
        get => legacyOperationKind ?? ToLegacyKind(Type);
        set
        {
            legacyOperationKind = value;
            Type = FromLegacyKind(value);
        }
    }
    /// <summary>Gets or sets compact operation dimensions such as [M,K,N] for MatMul.</summary>
    public List<int> TensorShape { get; set; } = [];
    /// <summary>Gets or sets the input shapes collection carried by the enclosing workload operation contract.</summary>
    public List<TensorShape> InputShapes { get; set; } = [];
    /// <summary>Gets or sets the output shape value carried by the enclosing workload operation contract.</summary>
    public TensorShape OutputShape { get; set; } = new();
    /// <summary>Gets or sets the precision value carried by the enclosing workload operation contract.</summary>
    public PrecisionKind Precision { get; set; } = PrecisionKind.INT8;
    /// <summary>Gets or sets the dependency ids collection carried by the formal workload operation contract.</summary>
    public List<string> DependencyIds { get; set; } = [];
    /// <summary>Gets or sets the dependencies collection carried by the legacy workload operation contract.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> Dependencies
    {
        get => DependencyIds;
        set => DependencyIds = value ?? [];
    }
    /// <summary>Gets or sets caller-defined operation metadata.</summary>
    public Dictionary<string, object> Attributes { get; set; } = new(StringComparer.Ordinal);

    internal static WorkloadOperationKind ToLegacyKind(OpType type) => type switch
    {
        OpType.MatMul => WorkloadOperationKind.MatMul,
        OpType.Attention_QK => WorkloadOperationKind.AttentionQkT,
        OpType.Softmax => WorkloadOperationKind.Softmax,
        OpType.Attention_V => WorkloadOperationKind.AttentionValue,
        _ => WorkloadOperationKind.SyntheticTraffic
    };

    internal static OpType FromLegacyKind(WorkloadOperationKind kind) => kind switch
    {
        WorkloadOperationKind.MatMul => OpType.MatMul,
        WorkloadOperationKind.AttentionQkT => OpType.Attention_QK,
        WorkloadOperationKind.Softmax => OpType.Softmax,
        WorkloadOperationKind.AttentionValue => OpType.Attention_V,
        WorkloadOperationKind.Mlp => OpType.MatMul,
        _ => OpType.Custom
    };
}

/// <summary>Describes one workload graph validation diagnostic.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Location">JSON-style location of the invalid workload element.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="RelatedId">Optional operation id associated with the issue.</param>
/// <param name="DependencyChain">Optional dependency chain that explains a cycle.</param>
public sealed record WorkloadValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null,
    IReadOnlyList<string>? DependencyChain = null);

/// <summary>Contains workload graph validation diagnostics.</summary>
public sealed class WorkloadValidationResult
{
    /// <summary>Gets workload validation issues in deterministic order.</summary>
    public List<WorkloadValidationIssue> Issues { get; } = [];
    /// <summary>Gets whether validation found no error diagnostics.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}
/// <summary>Represents workload graph data exchanged by hardware design and simulation workflows.</summary>
public sealed class WorkloadGraph
{
    /// <summary>Gets or sets the id value carried by the enclosing workload graph contract.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the schema version value carried by the workload graph contract.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the id value carried by the enclosing workload graph contract.</summary>
    public string Id { get; set; } = "workload";
    /// <summary>Gets or sets a display name for the workload graph.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets a human-readable workload description.</summary>
    public string Description { get; set; } = "";
    /// <summary>Gets or sets the operations collection carried by the enclosing workload graph contract.</summary>
    public List<WorkloadOperation> Ops { get; set; } = [];
    /// <summary>Gets or sets the operations collection carried by the legacy workload graph contract.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<WorkloadOperation> Operations
    {
        get => Ops;
        set => Ops = value ?? [];
    }

    /// <summary>Serializes the workload graph to the stable JSON contract.</summary>
    public string ToJson() => WorkloadGraphJson.Serialize(this);

    /// <summary>Deserializes the workload graph from the stable JSON contract.</summary>
    public static WorkloadGraph FromJson(string json) => WorkloadGraphJson.Deserialize(json);

    /// <summary>Validates schema, references, dimensions, precision, and dependency cycles.</summary>
    public WorkloadValidationResult Validate() => WorkloadGraphValidator.Validate(this);

    /// <summary>Converts the current value to pological order.</summary>
    public IReadOnlyList<WorkloadOperation> TopologicalOrder()
    {
        var validation = Validate();
        if (!validation.IsSuccess)
        {
            var issue = validation.Issues.First(item => item.Severity == ValidationSeverity.Error);
            throw new InvalidOperationException(issue.Message);
        }

        var byId = Ops.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<WorkloadOperation>();

        foreach (var operation in Ops.OrderBy(operation => operation.Id, StringComparer.Ordinal))
        {
            Visit(operation);
        }

        return ordered;

        void Visit(WorkloadOperation operation)
        {
            if (visited.Contains(operation.Id))
            {
                return;
            }

            if (!visiting.Add(operation.Id))
            {
                throw new InvalidOperationException($"Workload dependency cycle detected at operation '{operation.Id}'.");
            }

            foreach (var dependency in operation.DependencyIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!byId.TryGetValue(dependency, out var dependencyOperation))
                {
                    throw new InvalidOperationException($"Operation '{operation.Id}' depends on missing operation '{dependency}'.");
                }

                Visit(dependencyOperation);
            }

            visiting.Remove(operation.Id);
            visited.Add(operation.Id);
            ordered.Add(operation);
        }
    }
}


/// <summary>Serializes and deserializes workload graph JSON.</summary>
public static class WorkloadGraphJson
{
    /// <summary>Gets the workload serializer settings with stable camelCase names and enum text values.</summary>
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Serializes a workload graph to JSON.</summary>
    public static string Serialize(WorkloadGraph workload)
    {
        if (workload is null)
        {
            throw new ArgumentNullException(nameof(workload));
        }

        return System.Text.Json.JsonSerializer.Serialize(workload, Options);
    }

    /// <summary>Deserializes and normalizes a workload graph from JSON.</summary>
    public static WorkloadGraph Deserialize(string json)
    {
        var graph = System.Text.Json.JsonSerializer.Deserialize<WorkloadGraph>(json, Options) ?? new WorkloadGraph();
        Normalize(graph);
        if (graph.Ops.Count == 0)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("operations", out var legacyOps))
            {
                graph.Ops = System.Text.Json.JsonSerializer.Deserialize<List<WorkloadOperation>>(legacyOps.GetRawText(), Options) ?? [];
                Normalize(graph);
            }
        }

        return graph;
    }

    internal static void Normalize(WorkloadGraph workload)
    {
        workload.SchemaVersion = string.IsNullOrWhiteSpace(workload.SchemaVersion) ? WorkloadGraph.CurrentSchemaVersion : workload.SchemaVersion;
        workload.Id = string.IsNullOrWhiteSpace(workload.Id) ? "workload" : workload.Id;
        workload.Name ??= "";
        workload.Description ??= "";
        workload.Ops ??= [];
        foreach (var operation in workload.Ops)
        {
            operation.Id ??= "";
            operation.TensorShape ??= [];
            operation.InputShapes ??= [];
            operation.OutputShape ??= new TensorShape();
            operation.OutputShape.Dimensions ??= [];
            operation.DependencyIds ??= [];
            operation.Attributes ??= new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }
}

/// <summary>Validates workload graphs without mutating caller-owned objects.</summary>
public static class WorkloadGraphValidator
{
    /// <summary>Validates a workload graph and returns structured diagnostics.</summary>
    public static WorkloadValidationResult Validate(WorkloadGraph workload)
    {
        var result = new WorkloadValidationResult();
        if (workload is null)
        {
            result.Issues.Add(new WorkloadValidationIssue("WorkloadNull", ValidationSeverity.Error, "$", "WorkloadGraph is required."));
            return result;
        }

        if (!string.Equals(workload.SchemaVersion, WorkloadGraph.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add(new WorkloadValidationIssue("UnsupportedWorkloadSchemaVersion", ValidationSeverity.Error, "$.schema_version", $"WorkloadGraph schema version '{workload.SchemaVersion}' is not supported; expected {WorkloadGraph.CurrentSchemaVersion}."));
        }

        var byId = new Dictionary<string, WorkloadOperation>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < workload.Ops.Count; index++)
        {
            var operation = workload.Ops[index];
            if (string.IsNullOrWhiteSpace(operation.Id))
            {
                result.Issues.Add(new WorkloadValidationIssue("MissingWorkloadOpId", ValidationSeverity.Error, $"$.ops[{index}].id", "Every workload operation requires a non-empty id."));
                continue;
            }

            if (!byId.TryAdd(operation.Id, operation))
            {
                result.Issues.Add(new WorkloadValidationIssue("DuplicateWorkloadOpId", ValidationSeverity.Error, $"$.ops[{index}].id", $"Workload operation id '{operation.Id}' is not unique.", operation.Id));
            }

            foreach (var dimension in operation.TensorShape.Concat(operation.OutputShape.Dimensions))
            {
                if (dimension <= 0)
                {
                    result.Issues.Add(new WorkloadValidationIssue("InvalidWorkloadShape", ValidationSeverity.Error, $"$.ops[{index}].tensorShape", $"Operation '{operation.Id}' contains non-positive dimension {dimension}.", operation.Id));
                }
            }

            if (operation.Precision == PrecisionKind.Any)
            {
                result.Issues.Add(new WorkloadValidationIssue("InvalidWorkloadPrecision", ValidationSeverity.Error, $"$.ops[{index}].precision", $"Operation '{operation.Id}' must declare a concrete precision.", operation.Id));
            }
        }

        foreach (var operation in workload.Ops)
        {
            foreach (var dependency in operation.DependencyIds)
            {
                if (!byId.ContainsKey(dependency))
                {
                    result.Issues.Add(new WorkloadValidationIssue("MissingWorkloadDependency", ValidationSeverity.Error, $"$.ops[{operation.Id}].dependencyIds", $"Operation '{operation.Id}' depends on missing operation '{dependency}'.", operation.Id));
                }
            }
        }

        DetectCycles(workload, byId, result);
        return result;
    }

    private static void DetectCycles(WorkloadGraph workload, IReadOnlyDictionary<string, WorkloadOperation> byId, WorkloadValidationResult result)
    {
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        foreach (var operation in workload.Ops.OrderBy(operation => operation.Id, StringComparer.Ordinal))
        {
            if (Visit(operation, out var cycle))
            {
                result.Issues.Add(new WorkloadValidationIssue("WorkloadDependencyCycle", ValidationSeverity.Error, "$.ops", $"Workload dependency cycle detected: {string.Join(" -> ", cycle)}.", cycle[0], cycle));
                return;
            }
        }

        bool Visit(WorkloadOperation operation, out IReadOnlyList<string> cycle)
        {
            if (states.TryGetValue(operation.Id, out var state))
            {
                if (state == 1)
                {
                    var start = stack.FindIndex(id => string.Equals(id, operation.Id, StringComparison.OrdinalIgnoreCase));
                    cycle = stack.Skip(start).Append(operation.Id).ToList();
                    return true;
                }

                cycle = [];
                return false;
            }

            states[operation.Id] = 1;
            stack.Add(operation.Id);
            foreach (var dependencyId in operation.DependencyIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (byId.TryGetValue(dependencyId, out var dependency) && Visit(dependency, out cycle))
                {
                    return true;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            states[operation.Id] = 2;
            cycle = [];
            return false;
        }
    }
}
/// <summary>Represents mapping entry data exchanged by hardware design and simulation workflows.</summary>
public sealed class MappingEntry
{
    /// <summary>Gets or sets the workload operation id being mapped.</summary>
    public string WorkloadOpId { get; set; } = "";
    /// <summary>Gets or sets the target component id for the operation.</summary>
    public string TargetComponentId { get; set; } = "";
    /// <summary>Gets or sets the optional target port name for the operation.</summary>
    public string TargetPort { get; set; } = "";
    /// <summary>Gets or sets schedule hints such as start cycle, priority, or user tags.</summary>
    public Dictionary<string, string> ScheduleHints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the legacy operation id view.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string OperationId
    {
        get => WorkloadOpId;
        set => WorkloadOpId = value ?? "";
    }
    /// <summary>Gets or sets the legacy component id view.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ComponentId
    {
        get => TargetComponentId;
        set => TargetComponentId = value ?? "";
    }
    /// <summary>Gets or sets a legacy route hint stored in schedule hints.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RouteHint
    {
        get => ScheduleHints.TryGetValue("route_hint", out var value) ? value : null;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ScheduleHints.Remove("route_hint");
            }
            else
            {
                ScheduleHints["route_hint"] = value;
            }
        }
    }
}

/// <summary>Maps a tensor tile to a storage component and storage level.</summary>
public sealed class TensorPlacement
{
    /// <summary>Gets or sets the tensor identifier.</summary>
    public string TensorId { get; set; } = "";
    /// <summary>Gets or sets the tile identifier, or an empty value for a full tensor placement.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the component id that stores the tensor or tile.</summary>
    public string StorageComponentId { get; set; } = "";
    /// <summary>Gets or sets the storage hierarchy level such as GlobalMemory, SRAMBuffer, or PELocalBuffer.</summary>
    public string StorageLevel { get; set; } = "";
    /// <summary>Gets or sets an optional preferred bit address for deterministic storage placement.</summary>
    public string AddressHint { get; set; } = "";
}

/// <summary>Provides a route preference for a workload transfer.</summary>
public sealed class RouteMapping
{
    /// <summary>Gets or sets the link id to which this route hint applies.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Gets or sets the preferred path as a sequence of component or link ids.</summary>
    public List<string> PreferredPath { get; set; } = [];
    /// <summary>Gets or sets the route priority label.</summary>
    public string Priority { get; set; } = "normal";
}

/// <summary>Contains mapping validation diagnostics.</summary>
public sealed class WorkloadMappingValidationResult
{
    /// <summary>Gets mapping validation issues in deterministic order.</summary>
    public List<CompilationIssue> Issues { get; } = [];
    /// <summary>Gets whether the mapping has no errors.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Represents workload mapping data exchanged by hardware design and simulation workflows.</summary>
public sealed class WorkloadMapping
{
    /// <summary>Defines the current workload mapping schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the mapping schema version.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets operation-to-component mapping entries.</summary>
    public List<MappingEntry> Entries { get; set; } = [];
    /// <summary>Gets or sets tensor tile storage placements.</summary>
    public List<TensorPlacement> Placements { get; set; } = [];
    /// <summary>Gets or sets route mapping hints.</summary>
    public List<RouteMapping> RouteHints { get; set; } = [];
    /// <summary>Serializes this mapping to JSON.</summary>
    public string ToJson() => WorkloadMappingJson.Serialize(this);
    /// <summary>Deserializes a mapping from JSON.</summary>
    public static WorkloadMapping FromJson(string json) => WorkloadMappingJson.Deserialize(json);
    /// <summary>Validates operation, component, port, storage, and link references.</summary>
    public WorkloadMappingValidationResult Validate(WorkloadGraph workload, HardwareGraph hardwareGraph) => WorkloadMappingValidator.Validate(this, workload, hardwareGraph);
    /// <summary>Returns the component for text representation from the supplied inputs.</summary>
    public string? ComponentFor(string operationId) => Entries.FirstOrDefault(e => string.Equals(e.WorkloadOpId, operationId, StringComparison.OrdinalIgnoreCase))?.TargetComponentId;
    /// <summary>Returns the mapping entry for an operation, or null when not mapped.</summary>
    public MappingEntry? EntryFor(string operationId) => Entries.FirstOrDefault(e => string.Equals(e.WorkloadOpId, operationId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Serializes and deserializes workload mapping JSON.</summary>
public static class WorkloadMappingJson
{
    /// <summary>Serializes a workload mapping to JSON.</summary>
    public static string Serialize(WorkloadMapping mapping)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        return System.Text.Json.JsonSerializer.Serialize(mapping, WorkloadGraphJson.Options);
    }
    /// <summary>Deserializes and normalizes a workload mapping from JSON.</summary>
    public static WorkloadMapping Deserialize(string json)
    {
        var mapping = System.Text.Json.JsonSerializer.Deserialize<WorkloadMapping>(json, WorkloadGraphJson.Options) ?? new WorkloadMapping();
        Normalize(mapping);
        return mapping;
    }
    internal static void Normalize(WorkloadMapping mapping)
    {
        mapping.SchemaVersion = string.IsNullOrWhiteSpace(mapping.SchemaVersion) ? WorkloadMapping.CurrentSchemaVersion : mapping.SchemaVersion;
        mapping.Entries ??= [];
        mapping.Placements ??= [];
        mapping.RouteHints ??= [];
        foreach (var entry in mapping.Entries)
        {
            entry.WorkloadOpId ??= "";
            entry.TargetComponentId ??= "";
            entry.TargetPort ??= "";
            entry.ScheduleHints ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        foreach (var placement in mapping.Placements)
        {
            placement.TensorId ??= "";
            placement.TileId ??= "";
            placement.StorageComponentId ??= "";
            placement.StorageLevel ??= "";
        }
        foreach (var route in mapping.RouteHints)
        {
            route.LinkId ??= "";
            route.PreferredPath ??= [];
            route.Priority = string.IsNullOrWhiteSpace(route.Priority) ? "normal" : route.Priority;
        }
    }
}

/// <summary>Validates workload mapping references without mutating inputs.</summary>
public static class WorkloadMappingValidator
{
    /// <summary>Validates a workload mapping against a workload graph and hardware graph.</summary>
    public static WorkloadMappingValidationResult Validate(WorkloadMapping mapping, WorkloadGraph workload, HardwareGraph hardwareGraph)
    {
        var result = new WorkloadMappingValidationResult();
        var opIds = workload.Ops.Select(operation => operation.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mapping.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.WorkloadOpId) || !opIds.Contains(entry.WorkloadOpId))
            {
                Add(result, "MappingOperationReferenceError", "$.entries", $"Mapping entry references missing workload operation '{entry.WorkloadOpId}'.", entry.WorkloadOpId);
            }
            var component = hardwareGraph.FindComponent(entry.TargetComponentId);
            if (component is null)
            {
                Add(result, "MappingComponentReferenceError", "$.entries", $"Mapping entry for operation '{entry.WorkloadOpId}' references missing component '{entry.TargetComponentId}'.", entry.TargetComponentId);
            }
            else if (!string.IsNullOrWhiteSpace(entry.TargetPort) && component.FindPort(entry.TargetPort) is null)
            {
                Add(result, "MappingPortReferenceError", "$.entries", $"Mapping entry for operation '{entry.WorkloadOpId}' references missing port '{entry.TargetComponentId}.{entry.TargetPort}'.", entry.TargetComponentId);
            }
        }
        foreach (var placement in mapping.Placements)
        {
            if (string.IsNullOrWhiteSpace(placement.TensorId) || hardwareGraph.FindComponent(placement.StorageComponentId) is null)
            {
                Add(result, "TensorPlacementReferenceError", "$.placements", $"TensorPlacement for tile '{placement.TileId}' references invalid tensor or storage component '{placement.StorageComponentId}'.", placement.StorageComponentId);
            }
        }
        var linkIds = hardwareGraph.Links.Select(link => link.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var routeHint in mapping.RouteHints)
        {
            if (!linkIds.Contains(routeHint.LinkId))
            {
                Add(result, "RouteMappingReferenceError", "$.routeHints", $"RouteMapping references missing link '{routeHint.LinkId}'.", routeHint.LinkId);
            }
        }
        return result;
    }

    private static void Add(WorkloadMappingValidationResult result, string code, string location, string message, string? relatedId) =>
        result.Issues.Add(new CompilationIssue(code, ValidationSeverity.Error, location, message, relatedId));
}
/// <summary>Represents scheduled operation data exchanged by hardware design and simulation workflows.</summary>
public sealed class ScheduledOperation
{
    /// <summary>Gets or sets the operation id value carried by the enclosing scheduled operation contract.</summary>
    public string OperationId { get; set; } = "";
    /// <summary>Gets or sets the formal operation type carried by the scheduled operation contract.</summary>
    public OpType Type { get; set; } = OpType.Custom;
    /// <summary>Gets or sets the operation kind value carried by the enclosing scheduled operation contract.</summary>
    public WorkloadOperationKind OperationKind { get; set; }
    /// <summary>Gets or sets the component id value carried by the enclosing scheduled operation contract.</summary>
    public string ComponentId { get; set; } = "";
    /// <summary>Gets or sets the mapped component port carried by the scheduled operation contract.</summary>
    public string TargetPort { get; set; } = "";
    /// <summary>Gets or sets the start cycle value carried by the enclosing scheduled operation contract.</summary>
    public long StartCycle { get; set; }
    /// <summary>Gets or sets the end cycle value carried by the enclosing scheduled operation contract.</summary>
    public long EndCycle { get; set; }
    /// <summary>Gets or sets the packet count value carried by the enclosing scheduled operation contract.</summary>
    public int PacketCount { get; set; }
    /// <summary>Gets or sets the packet bits value carried by the enclosing scheduled operation contract.</summary>
    public int PacketBits { get; set; }
    /// <summary>Gets or sets the scheduled tensor provenance identifier.</summary>
    public string TensorId { get; set; } = "";
    /// <summary>Gets or sets the scheduled tile provenance identifier.</summary>
    public string TileId { get; set; } = "";
}

/// <summary>Represents workload schedule data exchanged by hardware design and simulation workflows.</summary>
public sealed class WorkloadSchedule
{
    /// <summary>Gets or sets the workload id value carried by the enclosing workload schedule contract.</summary>
    public string WorkloadId { get; set; } = "";
    /// <summary>Gets or sets the operations collection carried by the enclosing workload schedule contract.</summary>
    public List<ScheduledOperation> Operations { get; set; } = [];
    /// <summary>Gets the total cycles value carried by the enclosing workload schedule contract.</summary>
    public long TotalCycles => Operations.Count == 0 ? 0 : Operations.Max(o => o.EndCycle);
}

/// <summary>Provides precision model operations for hardware design and simulation workflows.</summary>
public static class PrecisionModel
{
    /// <summary>Gets the concrete Phase 3A precisions, excluding the port-only Any wildcard.</summary>
    public static IReadOnlyList<PrecisionKind> SupportedPrecisions { get; } =
    [
        PrecisionKind.FP32,
        PrecisionKind.FP16,
        PrecisionKind.BF16,
        PrecisionKind.TF32,
        PrecisionKind.FP8_E4M3,
        PrecisionKind.FP8_E5M2,
        PrecisionKind.INT32,
        PrecisionKind.INT16,
        PrecisionKind.INT8,
        PrecisionKind.INT4,
        PrecisionKind.INT2,
        PrecisionKind.Binary,
        PrecisionKind.Analog
    ];

    /// <summary>Returns true when the precision has a digital packet bit width.</summary>
    public static bool TryGetDigitalBitWidth(PrecisionKind precision, out int bitWidth)
    {
        bitWidth = precision switch
        {
            PrecisionKind.FP32 => 32,
            PrecisionKind.FP16 => 16,
            PrecisionKind.BF16 => 16,
            PrecisionKind.TF32 => 19,
            PrecisionKind.FP8_E4M3 => 8,
            PrecisionKind.FP8_E5M2 => 8,
            PrecisionKind.INT32 => 32,
            PrecisionKind.INT16 => 16,
            PrecisionKind.INT8 => 8,
            PrecisionKind.INT4 => 4,
            PrecisionKind.INT2 => 2,
            PrecisionKind.Binary => 1,
            _ => 0
        };
        return bitWidth > 0;
    }

    /// <summary>Returns the digital storage width in bits for a supported numeric precision.</summary>
    public static int BitsPerElement(PrecisionKind precision) => TryGetDigitalBitWidth(precision, out var bitWidth)
        ? bitWidth
        : throw new InvalidOperationException($"Precision '{precision}' does not have a digital packet bit width.");

    /// <summary>Returns output payload bits capped to the configured packet size.</summary>
    public static int PacketBitsFor(WorkloadOperation operation, int maxPacketBits = 1024)
    {
        var outputBits = Math.Max(1, operation.OutputShape.ElementCount) * BitsPerElement(operation.Precision);
        return (int)Math.Clamp(outputBits, 1, maxPacketBits);
    }

    /// <summary>Returns the number of bounded packets required for the operation output.</summary>
    public static int PacketCountFor(WorkloadOperation operation, int maxPacketBits = 1024)
    {
        var outputBits = Math.Max(1, operation.OutputShape.ElementCount) * BitsPerElement(operation.Precision);
        return (int)Math.Max(1, Math.Ceiling((double)outputBits / Math.Max(1, maxPacketBits)));
    }
}
/// <summary>Creates deterministic workload templates for common neural network operations.</summary>
public static class WorkloadTemplates
{
    /// <summary>Creates a one-operation matrix multiplication workload for C[M,N] = A[M,K] x B[K,N].</summary>
    public static WorkloadGraph CreateMatMul(int m, int k, int n, PrecisionKind precision = PrecisionKind.FP16) => new()
    {
        Id = $"matmul_{m}_{k}_{n}",
        Name = $"MatMul({m},{k},{n})",
        Ops =
        [
            new WorkloadOperation
            {
                Id = "MatMul_0",
                Type = OpType.MatMul,
                TensorShape = [m, k, n],
                InputShapes = [new TensorShape { Dimensions = [m, k] }, new TensorShape { Dimensions = [k, n] }],
                OutputShape = new TensorShape { Dimensions = [m, n] },
                Precision = precision
            }
        ]
    };

    /// <summary>Creates the three-stage attention workload QK, Softmax, and V projection.</summary>
    public static WorkloadGraph CreateAttention(int sequenceLength, int headDimension, PrecisionKind precision = PrecisionKind.FP16) => new()
    {
        Id = $"attention_s{sequenceLength}_h{headDimension}",
        Name = $"Attention({sequenceLength},{headDimension})",
        Ops =
        [
            new WorkloadOperation
            {
                Id = "Attention_QK",
                Type = OpType.Attention_QK,
                TensorShape = [sequenceLength, headDimension],
                InputShapes = [new TensorShape { Dimensions = [sequenceLength, headDimension] }, new TensorShape { Dimensions = [headDimension, sequenceLength] }],
                OutputShape = new TensorShape { Dimensions = [sequenceLength, sequenceLength] },
                Precision = precision
            },
            new WorkloadOperation
            {
                Id = "Attention_Softmax",
                Type = OpType.Softmax,
                TensorShape = [sequenceLength, sequenceLength],
                InputShapes = [new TensorShape { Dimensions = [sequenceLength, sequenceLength] }],
                OutputShape = new TensorShape { Dimensions = [sequenceLength, sequenceLength] },
                Precision = precision,
                DependencyIds = ["Attention_QK"],
                Attributes = { ["barrier"] = "row" }
            },
            new WorkloadOperation
            {
                Id = "Attention_V",
                Type = OpType.Attention_V,
                TensorShape = [sequenceLength, headDimension],
                InputShapes = [new TensorShape { Dimensions = [sequenceLength, sequenceLength] }, new TensorShape { Dimensions = [sequenceLength, headDimension] }],
                OutputShape = new TensorShape { Dimensions = [sequenceLength, headDimension] },
                Precision = precision,
                DependencyIds = ["Attention_Softmax"]
            }
        ]
    };

    /// <summary>Creates an MLP as adjacent matrix multiplication operations between layer dimensions.</summary>
    public static WorkloadGraph CreateMLP(IReadOnlyList<int> layerDimensions, PrecisionKind precision = PrecisionKind.FP16)
    {
        if (layerDimensions.Count < 2)
        {
            throw new ArgumentException("At least two layer dimensions are required.", nameof(layerDimensions));
        }

        var graph = new WorkloadGraph { Id = $"mlp_{string.Join("_", layerDimensions)}", Name = "MLP" };
        for (var index = 0; index < layerDimensions.Count - 1; index++)
        {
            graph.Ops.Add(new WorkloadOperation
            {
                Id = $"MLP_MatMul_{index}",
                Type = OpType.MatMul,
                TensorShape = [layerDimensions[index], layerDimensions[index + 1]],
                InputShapes = [new TensorShape { Dimensions = [1, layerDimensions[index]] }, new TensorShape { Dimensions = [layerDimensions[index], layerDimensions[index + 1]] }],
                OutputShape = new TensorShape { Dimensions = [1, layerDimensions[index + 1]] },
                Precision = precision,
                DependencyIds = index == 0 ? [] : [$"MLP_MatMul_{index - 1}"]
            });
        }

        return graph;
    }

    /// <summary>Creates a single convolution operation with explicit dimensions.</summary>
    public static WorkloadGraph CreateConv(int batch, int channels, int height, int width, int kernel, PrecisionKind precision = PrecisionKind.FP16) => new()
    {
        Id = $"conv_b{batch}_c{channels}_h{height}_w{width}_k{kernel}",
        Name = "Conv",
        Ops =
        [
            new WorkloadOperation
            {
                Id = "Conv_0",
                Type = OpType.Conv,
                TensorShape = [batch, channels, height, width, kernel],
                InputShapes = [new TensorShape { Dimensions = [batch, channels, height, width] }],
                OutputShape = new TensorShape { Dimensions = [batch, channels, Math.Max(1, height - kernel + 1), Math.Max(1, width - kernel + 1)] },
                Precision = precision
            }
        ]
    };
}
/// <summary>Provides sample workloads operations for hardware design and simulation workflows.</summary>
public static class SampleWorkloads
{
    /// <summary>Creates mat mul1 by2048x2048 from the supplied inputs.</summary>
    public static WorkloadGraph CreateMatMul1By2048x2048(PrecisionKind precision = PrecisionKind.INT8)
    {
        return new WorkloadGraph
        {
            Id = "matmul_1x2048_2048x2048",
            Operations =
            [
                new WorkloadOperation
                {
                    Id = "MatMul_0",
                    OperationKind = WorkloadOperationKind.MatMul,
                    InputShapes =
                    [
                        new TensorShape { Dimensions = [1, 2048] },
                        new TensorShape { Dimensions = [2048, 2048] }
                    ],
                    OutputShape = new TensorShape { Dimensions = [1, 2048] },
                    Precision = precision
                }
            ]
        };
    }

    /// <summary>Creates the sample matrix-multiplication mapping for a processing element.</summary>
    public static WorkloadMapping MapMatMulToPe(string componentId = "pe0")
    {
        return new WorkloadMapping
        {
            Entries = [new MappingEntry { OperationId = "MatMul_0", ComponentId = componentId }]
        };
    }

    /// <summary>Creates tiny attention block from the supplied inputs.</summary>
    public static WorkloadGraph CreateTinyAttentionBlock(int sequenceLength = 16, int hiddenSize = 64, PrecisionKind precision = PrecisionKind.INT8)
    {
        return new WorkloadGraph
        {
            Id = $"attention_s{sequenceLength}_h{hiddenSize}",
            Operations =
            [
                new WorkloadOperation
                {
                    Id = "Attention_QK",
                    OperationKind = WorkloadOperationKind.AttentionQkT,
                    InputShapes =
                    [
                        new TensorShape { Dimensions = [sequenceLength, hiddenSize] },
                        new TensorShape { Dimensions = [hiddenSize, sequenceLength] }
                    ],
                    OutputShape = new TensorShape { Dimensions = [sequenceLength, sequenceLength] },
                    Precision = precision
                },
                new WorkloadOperation
                {
                    Id = "Attention_Softmax",
                    OperationKind = WorkloadOperationKind.Softmax,
                    InputShapes = [new TensorShape { Dimensions = [sequenceLength, sequenceLength] }],
                    OutputShape = new TensorShape { Dimensions = [sequenceLength, sequenceLength] },
                    Precision = precision,
                    Dependencies = ["Attention_QK"]
                },
                new WorkloadOperation
                {
                    Id = "Attention_Value",
                    OperationKind = WorkloadOperationKind.AttentionValue,
                    InputShapes =
                    [
                        new TensorShape { Dimensions = [sequenceLength, sequenceLength] },
                        new TensorShape { Dimensions = [sequenceLength, hiddenSize] }
                    ],
                    OutputShape = new TensorShape { Dimensions = [sequenceLength, hiddenSize] },
                    Precision = precision,
                    Dependencies = ["Attention_Softmax"]
                },
                new WorkloadOperation
                {
                    Id = "MLP_0",
                    OperationKind = WorkloadOperationKind.Mlp,
                    InputShapes = [new TensorShape { Dimensions = [sequenceLength, hiddenSize] }],
                    OutputShape = new TensorShape { Dimensions = [sequenceLength, hiddenSize * 4] },
                    Precision = precision,
                    Dependencies = ["Attention_Value"]
                }
            ]
        };
    }
}

/// <summary>Provides template mapper operations for hardware design and simulation workflows.</summary>
public static class TemplateMapper
{
    /// <summary>Maps workload operations to available processing, memory, and interface components by operation type.</summary>
    public static WorkloadMapping MapToAvailableComponents(WorkloadGraph workload, HardwareGraph graph)
    {
        var entries = new List<MappingEntry>();
        var peIds = graph.Components.Where(c => c.Type == ComponentKind.ProcessingElement).Select(c => c.Id).ToList();
        var softmaxId = graph.Components.FirstOrDefault(c => c.Type == ComponentKind.SoftmaxUnit)?.Id;
        var reductionId = graph.Components.FirstOrDefault(c => c.Type == ComponentKind.ReductionUnit)?.Id;
        var peIndex = 0;

        foreach (var operation in workload.TopologicalOrder())
        {
            var componentId = operation.Type switch
            {
                OpType.Softmax when softmaxId is not null => softmaxId,
                OpType.Attention_V when reductionId is not null => reductionId,
                _ when peIds.Count > 0 => peIds[peIndex++ % peIds.Count],
                _ => graph.Components.FirstOrDefault()?.Id ?? ""
            };

            entries.Add(new MappingEntry
            {
                OperationId = operation.Id,
                ComponentId = componentId,
                RouteHint = operation.Type.ToString()
            });
        }

        return new WorkloadMapping { Entries = entries };
    }
}

/// <summary>Provides heuristic mapper operations for hardware design and simulation workflows.</summary>
public static class HeuristicMapper
{
    /// <summary>Maps each operation to the nearest compatible component using physical placement when available.</summary>
    public static WorkloadMapping MapToNearestCapableComponents(WorkloadGraph workload, HardwareGraph graph, PhysicalPlacement? placement = null)
    {
        placement ??= new PhysicalPlacement();
        var entries = new List<MappingEntry>();
        var source = graph.Components.FirstOrDefault(c => c.Type == ComponentKind.WorkloadSource) ?? graph.Components.FirstOrDefault();
        var sourcePoint = source is null ? new PhysicalPoint(0, 0) : placement.PositionFor(source);
        var peComponents = graph.Components.Where(c => c.Type == ComponentKind.ProcessingElement).ToList();

        foreach (var operation in workload.TopologicalOrder())
        {
            var candidates = CandidatesFor(operation, graph, peComponents);
            var selected = candidates
                .OrderBy(c => Manhattan(sourcePoint, placement.PositionFor(c)))
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            entries.Add(new MappingEntry
            {
                OperationId = operation.Id,
                ComponentId = selected?.Id ?? graph.Components.FirstOrDefault()?.Id ?? "",
                RouteHint = $"heuristic:{operation.Type}"
            });
        }

        return new WorkloadMapping { Entries = entries };
    }

    private static IReadOnlyList<HardwareComponent> CandidatesFor(WorkloadOperation operation, HardwareGraph graph, IReadOnlyList<HardwareComponent> peComponents)
    {
        var specialized = operation.Type switch
        {
            OpType.Softmax => graph.Components.Where(c => c.Type == ComponentKind.SoftmaxUnit).ToList(),
            OpType.Attention_V => graph.Components.Where(c => c.Type == ComponentKind.ReductionUnit).ToList(),
            _ => []
        };

        return specialized.Count > 0 ? specialized : peComponents.Count > 0 ? peComponents : graph.Components;
    }

    private static double Manhattan(PhysicalPoint a, PhysicalPoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}

/// <summary>Represents workload scheduler data exchanged by hardware design and simulation workflows.</summary>
public sealed class WorkloadScheduler
{
    /// <summary>Builds a dependency-ordered schedule with component availability, latency, and packet estimates.</summary>
    public WorkloadSchedule BuildSchedule(WorkloadGraph workload, WorkloadMapping mapping, HardwareGraph graph)
    {
        var schedule = new WorkloadSchedule { WorkloadId = workload.Id };
        var operationEnd = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var componentAvailable = graph.Components.ToDictionary(c => c.Id, _ => 0L, StringComparer.OrdinalIgnoreCase);

        foreach (var operation in workload.TopologicalOrder())
        {
            var entry = mapping.EntryFor(operation.Id);
            var componentId = entry?.TargetComponentId ?? graph.Components.FirstOrDefault()?.Id ?? "";
            var dependencyReady = operation.DependencyIds.Count == 0 ? 0 : operation.DependencyIds.Max(d => operationEnd[d]);
            var resourceReady = componentAvailable.TryGetValue(componentId, out var available) ? available : 0;
            var start = Math.Max(dependencyReady, resourceReady);
            var duration = EstimateDuration(operation, graph.FindComponent(componentId));
            var end = start + duration;

            operationEnd[operation.Id] = end;
            componentAvailable[componentId] = end;
            schedule.Operations.Add(new ScheduledOperation
            {
                OperationId = operation.Id,
                Type = operation.Type,
                OperationKind = operation.OperationKind,
                ComponentId = componentId,
                TargetPort = entry?.TargetPort ?? "",
                StartCycle = start,
                EndCycle = end,
                PacketCount = PrecisionModel.PacketCountFor(operation),
                PacketBits = PrecisionModel.PacketBitsFor(operation),
                TensorId = $"{operation.Id}_out",
                TileId = $"{operation.Id}_out_tile_0"
            });
        }

        return schedule;
    }

    private static long EstimateDuration(WorkloadOperation operation, HardwareComponent? component)
    {
        var baseLatency = operation.OperationKind == WorkloadOperationKind.Mlp
            ? 12
            : operation.Type switch
            {
                OpType.MatMul => 8,
                OpType.Attention_QK => 10,
                OpType.Softmax => 4,
                OpType.Attention_V => 8,
                OpType.Conv => 10,
                OpType.Elementwise => 2,
                OpType.Reduce => 4,
                _ => 2
            };

        var componentLatency = component?.GetIntParameter("operation_latency_cycles", 0) ?? 0;
        var packetPressure = PrecisionModel.PacketCountFor(operation);
        return Math.Max(1, baseLatency + componentLatency + packetPressure);
    }
}
