using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Stable first-party activation slice-distribution contract.</summary>
public static class Phase8ATensorSliceContract
{
    /// <summary>Stable Phase 8A contract value for TypeId.</summary>
    public const string TypeId = "com.hardware-sim.first-party.collective.tensor-slice-distribution";
    /// <summary>Stable Phase 8A contract value for KernelId.</summary>
    public const string KernelId = "phase8a.collective.tensor-slice-distribution";
    /// <summary>Stable Phase 8A contract value for InputPort.</summary>
    public const string InputPort = "in_tensor";
    /// <summary>Stable Phase 8A contract value for OutputPort.</summary>
    public const string OutputPort = "out_slice";
    /// <summary>Stable Phase 8A contract value for TargetsMetadata.</summary>
    public const string TargetsMetadata = "phase8a.pipeline.tensor_slice_targets";
    /// <summary>Optional coverage mode for topology strategies that select or replicate subranges.</summary>
    public const string CoverageModeMetadata = "phase8a.pipeline.tensor_slice_coverage_mode";
    /// <summary>Allows one or more in-range targets whose ranges may overlap or cover only part of the parent.</summary>
    public const string SelectReplicateCoverageMode = "select-replicate/v1";
}

/// <summary>One exact tensor range distributed to a consumer.</summary>
public sealed class Phase8ATensorSliceTarget
{
    /// <summary>Creates one immutable slice target and its exact routes.</summary>
    public Phase8ATensorSliceTarget(
        string consumerComponentId,
        int elementOffset,
        int elementCount,
        string routePathId,
        IEnumerable<string>? routeLinkIds,
        IEnumerable<Phase8AStageRoute>? downstreamRoutes = null,
        IReadOnlyDictionary<string, string>? metadataOverrides = null)
    {
        ConsumerComponentId = consumerComponentId?.Trim() ?? "";
        ElementOffset = elementOffset;
        ElementCount = elementCount;
        RoutePathId = routePathId?.Trim() ?? "";
        RouteLinkIds = Array.AsReadOnly((routeLinkIds ?? []).Select(value => value?.Trim() ?? "").ToArray());
        DownstreamRoutes = Array.AsReadOnly((downstreamRoutes ?? [])
            .Select(route => route is null
                ? new Phase8AStageRoute("", "", [])
                : new Phase8AStageRoute(route.RoutePathId, route.DestinationComponentId, route.LinkIds, route.MetadataOverrides)).ToArray());
        MetadataOverrides = new ReadOnlyDictionary<string, string>((metadataOverrides ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key?.Trim() ?? "", pair => pair.Value?.Trim() ?? "", StringComparer.Ordinal));
    }

    /// <summary>Gets the target consumer.</summary>
    public string ConsumerComponentId { get; }
    /// <summary>Gets the zero-based element offset.</summary>
    public int ElementOffset { get; }
    /// <summary>Gets the number of elements in the slice.</summary>
    public int ElementCount { get; }
    /// <summary>Gets the immediate logical route identity.</summary>
    public string RoutePathId { get; }
    /// <summary>Gets immediate exact directed links.</summary>
    public IReadOnlyList<string> RouteLinkIds { get; }
    /// <summary>Gets routes consumed after the destination kernel executes.</summary>
    public IReadOnlyList<Phase8AStageRoute> DownstreamRoutes { get; }
    /// <summary>Gets target-specific metadata overrides.</summary>
    public IReadOnlyDictionary<string, string> MetadataOverrides { get; }
}

/// <summary>Binds a typed tensor slice-distribution plan.</summary>
public static class Phase8ATensorSlicePacketBinder
{
    /// <summary>Binds a validated target plan to a parent packet.</summary>
    public static void Bind(Packet packet, IEnumerable<Phase8ATensorSliceTarget>? targets)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        var materialized = (targets ?? []).ToArray();
        if (materialized.Length < 2 || materialized.Any(target => target is null || !Phase8ATensorSliceMetadata.IsStructurallyValid(target)) ||
            materialized.Select(target => target.ConsumerComponentId).Distinct(StringComparer.Ordinal).Count() != materialized.Length ||
            materialized.Select(target => target.RoutePathId).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("A tensor-slice plan requires at least two unique consumers with unique path ids and structurally valid exact routes.", nameof(targets));
        packet.Metadata[Phase8ATensorSliceContract.TargetsMetadata] = Phase8ATensorSliceMetadata.Encode(materialized);
    }
}

internal static class Phase8ATensorSliceMetadata
{
    public static string Encode(IEnumerable<Phase8ATensorSliceTarget> targets) => JsonSerializer.Serialize(
        targets.Select(target => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["consumerComponentId"] = target.ConsumerComponentId,
            ["elementOffset"] = target.ElementOffset,
            ["elementCount"] = target.ElementCount,
            ["routePathId"] = target.RoutePathId,
            ["routeLinkIds"] = target.RouteLinkIds,
            ["downstreamRoutes"] = JsonSerializer.Deserialize<object>(Phase8AStageRouteMetadata.Encode(target.DownstreamRoutes), HardwareGraphJson.Options),
            ["metadataOverrides"] = target.MetadataOverrides
        }).ToArray(), HardwareGraphJson.Options);

    public static bool TryDecode(string? raw, out IReadOnlyList<Phase8ATensorSliceTarget> targets)
    {
        targets = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var decoded = new List<Phase8ATensorSliceTarget>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("consumerComponentId", out var consumer) || consumer.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("elementOffset", out var offset) || !offset.TryGetInt32(out var elementOffset) ||
                    !element.TryGetProperty("elementCount", out var count) || !count.TryGetInt32(out var elementCount) ||
                    !element.TryGetProperty("routePathId", out var path) || path.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("routeLinkIds", out var links) || links.ValueKind != JsonValueKind.Array ||
                    links.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                    return false;
                IReadOnlyList<Phase8AStageRoute> downstream = [];
                if (element.TryGetProperty("downstreamRoutes", out var routes) &&
                    !Phase8AStageRouteMetadata.TryDecode(routes.GetRawText(), out downstream)) return false;
                var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
                if (element.TryGetProperty("metadataOverrides", out var metadata))
                {
                    if (metadata.ValueKind != JsonValueKind.Object) return false;
                    foreach (var property in metadata.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String) return false;
                        overrides[property.Name] = property.Value.GetString() ?? "";
                    }
                }
                var target = new Phase8ATensorSliceTarget(
                    consumer.GetString() ?? "", elementOffset, elementCount, path.GetString() ?? "",
                    links.EnumerateArray().Select(item => item.GetString() ?? ""), downstream, overrides);
                if (!IsStructurallyValid(target)) return false;
                decoded.Add(target);
            }
            targets = new ReadOnlyCollection<Phase8ATensorSliceTarget>(decoded);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool IsStructurallyValid(Phase8ATensorSliceTarget target) =>
        !string.IsNullOrWhiteSpace(target.ConsumerComponentId) &&
        !string.IsNullOrWhiteSpace(target.RoutePathId) &&
        target.RouteLinkIds.Count > 0 &&
        target.RouteLinkIds.All(linkId => !string.IsNullOrWhiteSpace(linkId)) &&
        target.RouteLinkIds.Distinct(StringComparer.Ordinal).Count() == target.RouteLinkIds.Count &&
        target.DownstreamRoutes.All(Phase8AStageRouteMetadata.IsStructurallyValid) &&
        target.MetadataOverrides.Keys.All(key => !string.IsNullOrWhiteSpace(key));
}

/// <summary>Registers activation slice distribution.</summary>
public static class Phase8ATensorSliceComponentPlugin
{
    /// <summary>Loads the tensor slice plugin into the supplied registry.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        FirstPartyComponentPlugins.RegisterOrThrow(registry, Create());
    }

    /// <summary>Creates the deterministic tensor slice descriptor.</summary>
    public static ComponentPluginDescriptor Create() => new(
        Phase8ATensorSliceContract.TypeId, "Tensor Slice Distribution", "Collective", "1.0.0",
        [
            new(Phase8ATensorSliceContract.InputPort, PortDirection.Input, SignalType.Digital, HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true, Quantity: "parent_tensor", Units: "element"),
            new(Phase8ATensorSliceContract.OutputPort, PortDirection.Output, SignalType.Digital, HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true, MultiConnect: true, Quantity: "tensor_slice", Units: "element")
        ],
        [
            new(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, "1", "cycles", 1, 1_000_000, false, "Slice distribution latency.", IntegerOnly: true),
            new("input_queue_depth", "8", "packets", 1, 65_536, false, "Maximum buffered parent tensors.", IntegerOnly: true),
            new("output_queue_depth", "8", "packets", 1, 65_536, false, "Maximum engine-owned output slices.", IntegerOnly: true),
            new("max_targets", "1024", "consumers", 2, 65_536, false, "Maximum disjoint tensor slices.", IntegerOnly: true)
        ],
        NoopValidationProvider.Instance, NoopCompileProvider.Instance, Phase8ATensorSliceRuntimeFactory.Instance,
        [new ComponentTraceDescriptor("phase8a.collective.tensor_slice", TraceEventType.Compute, "Exact disjoint activation slice distribution")], [],
        UnityPresentationDescriptor: new UnityPresentationDescriptor("tensor-slice", "#0891B2", "SLC", "Activation tile distribution", 99),
        SourceKind: ComponentPluginSourceKind.FirstParty, LegacyKind: null, ShowInPalette: false,
        RuntimeKernelFactory: Phase8ATensorSliceKernelFactory.Instance);
}

internal sealed class Phase8ATensorSliceRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly Phase8ATensorSliceRuntimeFactory Instance = new();
    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context) =>
        Phase8ACollectivePluginRuntime.CreateDescriptor(context, Phase8ATensorSliceKernelFactory.Instance.Descriptor, EnergyCategory.NoC);
}

internal sealed class Phase8ATensorSliceKernelFactory : IComponentRuntimeKernelFactory
{
    public static readonly Phase8ATensorSliceKernelFactory Instance = new();
    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase8ATensorSliceContract.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = "phase8a.collective.tensor-slice.config.v1",
        ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8a-tensor-slice-v1\ndisjoint-full-coverage\noverflow-safe-coverage\nfail-closed-metadata\nexact-digital-payload\nstrict-typed-plan\nexact-route\ncurrent-next-backpressure"),
        SupportedOperationKinds = [Phase8ATensorSliceContract.TypeId]
    };
    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context) => new Phase8ATensorSliceKernel();
}
