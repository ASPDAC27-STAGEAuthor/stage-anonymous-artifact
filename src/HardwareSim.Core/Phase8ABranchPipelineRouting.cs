using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Per-consumer metadata and downstream routes applied after a multicast clone is created.</summary>
public sealed class Phase8ABranchTargetPipeline
{
    /// <summary>Creates one immutable target pipeline.</summary>
    public Phase8ABranchTargetPipeline(
        string consumerComponentId,
        IEnumerable<Phase8AStageRoute>? downstreamRoutes,
        IReadOnlyDictionary<string, string>? metadataOverrides = null,
        Phase8AMulticastBranchPlan? downstreamMulticastPlan = null,
        IEnumerable<Phase8ABranchTargetPipeline>? downstreamTargetPipelines = null)
    {
        ConsumerComponentId = consumerComponentId?.Trim() ?? "";
        DownstreamRoutes = Array.AsReadOnly((downstreamRoutes ?? [])
            .Select(route => route is null
                ? new Phase8AStageRoute("", "", [])
                : new Phase8AStageRoute(route.RoutePathId, route.DestinationComponentId, route.LinkIds, route.MetadataOverrides))
            .ToArray());
        MetadataOverrides = new ReadOnlyDictionary<string, string>((metadataOverrides ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key?.Trim() ?? "", pair => pair.Value?.Trim() ?? "", StringComparer.Ordinal));
        DownstreamMulticastPlan = downstreamMulticastPlan is null
            ? null
            : new Phase8AMulticastBranchPlan(downstreamMulticastPlan.FlowId, downstreamMulticastPlan.BranchId, downstreamMulticastPlan.Targets);
        DownstreamTargetPipelines = Array.AsReadOnly((downstreamTargetPipelines ?? []).ToArray());
    }

    /// <summary>Gets the multicast consumer identity.</summary>
    public string ConsumerComponentId { get; }
    /// <summary>Gets routes consumed by data-producing stages after multicast delivery.</summary>
    public IReadOnlyList<Phase8AStageRoute> DownstreamRoutes { get; }
    /// <summary>Gets deterministic metadata overrides applied to this consumer clone.</summary>
    public IReadOnlyDictionary<string, string> MetadataOverrides { get; }
    /// <summary>Gets the next branch-local multicast plan applied when this clone targets another branch component.</summary>
    public Phase8AMulticastBranchPlan? DownstreamMulticastPlan { get; }
    /// <summary>Gets immutable per-target pipelines for the next branch-local multicast stage.</summary>
    public IReadOnlyList<Phase8ABranchTargetPipeline> DownstreamTargetPipelines { get; }
}

/// <summary>Binds per-target pipeline context to a multicast parent packet.</summary>
public static class Phase8ABranchPipelineBinder
{
    /// <summary>Binds exact downstream routes and metadata overrides by consumer id.</summary>
    public static void Bind(Packet packet, IEnumerable<Phase8ABranchTargetPipeline>? targets)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        Phase8ABranchPipelineMetadata.Bind(packet, targets ?? []);
    }
}

internal static class Phase8ABranchPipelineMetadata
{
    private const string TargetPipelines = "phase8a.pipeline.multicast_target_pipelines";
    private const int MaximumNestedDepth = 64;

    public static void Bind(Packet packet, IEnumerable<Phase8ABranchTargetPipeline> targets)
    {
        var materialized = Materialize(targets);
        if (materialized.Length == 0)
        {
            packet.Metadata.Remove(TargetPipelines);
            return;
        }
        if (!IsStructurallyValid(materialized, 0))
            throw new ArgumentException("Branch target pipelines require unique consumers, valid routes, and a finite matching nested multicast tree.", nameof(targets));
        packet.Metadata[TargetPipelines] = JsonSerializer.Serialize(materialized.Select(EncodePipeline).ToArray(), HardwareGraphJson.Options);
    }

    public static void Apply(Packet parent, Packet clone, string consumerComponentId)
    {
        clone.Metadata.Remove(TargetPipelines);
        clone.Metadata.Remove(Phase8AStageRouteMetadata.RemainingRoutes);
        if (!parent.Metadata.TryGetValue(TargetPipelines, out var raw) || string.IsNullOrWhiteSpace(raw)) return;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            var matches = document.RootElement.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.Object &&
                    element.TryGetProperty("consumerComponentId", out var consumer) &&
                    consumer.ValueKind == JsonValueKind.String &&
                    string.Equals(consumer.GetString(), consumerComponentId, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length != 1 || !TryDecodePipeline(matches[0], 0, out var pipeline) || pipeline is null) return;
            foreach (var pair in pipeline.MetadataOverrides) clone.Metadata[pair.Key] = pair.Value;
            clone.Metadata.Remove(TargetPipelines);
            Phase8AStageRouteMetadata.BindRemaining(clone, pipeline.DownstreamRoutes);
            if (pipeline.DownstreamMulticastPlan is not null)
            {
                Phase8ACollectivePacketBinder.BindMulticast(clone, pipeline.DownstreamMulticastPlan);
                Bind(clone, pipeline.DownstreamTargetPipelines);
            }
        }
        catch (JsonException)
        {
            clone.Metadata.Remove(TargetPipelines);
            clone.Metadata.Remove(Phase8AStageRouteMetadata.RemainingRoutes);
        }
        catch (InvalidOperationException)
        {
            clone.Metadata.Remove(TargetPipelines);
            clone.Metadata.Remove(Phase8AStageRouteMetadata.RemainingRoutes);
        }
    }

    private static Phase8ABranchTargetPipeline[] Materialize(IEnumerable<Phase8ABranchTargetPipeline> targets) =>
        targets.Select(target => target is null
                ? throw new ArgumentException("Branch target pipelines cannot contain null entries.", nameof(targets))
                : target)
            .OrderBy(target => target.ConsumerComponentId, StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, object?> EncodePipeline(Phase8ABranchTargetPipeline target)
    {
        var encoded = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["consumerComponentId"] = target.ConsumerComponentId,
            ["downstreamRoutes"] = JsonSerializer.Deserialize<object>(Phase8AStageRouteMetadata.Encode(target.DownstreamRoutes), HardwareGraphJson.Options),
            ["metadataOverrides"] = target.MetadataOverrides
        };
        if (target.DownstreamMulticastPlan is not null)
        {
            encoded["downstreamMulticastPlan"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["flowId"] = target.DownstreamMulticastPlan.FlowId,
                ["branchId"] = target.DownstreamMulticastPlan.BranchId,
                ["targets"] = JsonSerializer.Deserialize<object>(Phase8ACollectiveMetadataCodec.EncodeTargets(target.DownstreamMulticastPlan.Targets), HardwareGraphJson.Options),
                ["targetPipelines"] = target.DownstreamTargetPipelines.Select(EncodePipeline).ToArray()
            };
        }
        return encoded;
    }

    private static bool IsStructurallyValid(IReadOnlyList<Phase8ABranchTargetPipeline> pipelines, int depth)
    {
        if (depth > MaximumNestedDepth || pipelines.Count == 0 ||
            pipelines.Any(target => string.IsNullOrWhiteSpace(target.ConsumerComponentId) ||
                target.DownstreamRoutes.Any(route => !Phase8AStageRouteMetadata.IsStructurallyValid(route)) ||
                target.MetadataOverrides.Keys.Any(key => !IsMetadataOverrideAllowed(key))) ||
            pipelines.Select(target => target.ConsumerComponentId).Distinct(StringComparer.Ordinal).Count() != pipelines.Count)
            return false;
        foreach (var pipeline in pipelines)
        {
            if (pipeline.DownstreamMulticastPlan is null)
            {
                if (pipeline.DownstreamTargetPipelines.Count != 0) return false;
                continue;
            }
            if (!IsStructurallyValid(pipeline.DownstreamMulticastPlan) ||
                pipeline.DownstreamTargetPipelines.Select(item => item.ConsumerComponentId).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(pipeline.DownstreamMulticastPlan.Targets.Select(item => item.ConsumerComponentId)) is false ||
                !IsStructurallyValid(pipeline.DownstreamTargetPipelines, depth + 1)) return false;
        }
        return true;
    }

    private static bool IsMetadataOverrideAllowed(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        !string.Equals(key, TargetPipelines, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AStageRouteMetadata.RemainingRoutes, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AExplicitRouteMetadata.PathId, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AExplicitRouteMetadata.LinkIds, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AExplicitRouteMetadata.NextLinkIndex, StringComparison.Ordinal) &&
        !key.StartsWith("phase8a.multicast.", StringComparison.Ordinal);

    private static bool IsStructurallyValid(Phase8AMulticastBranchPlan plan) =>
        !string.IsNullOrWhiteSpace(plan.FlowId) &&
        !string.IsNullOrWhiteSpace(plan.BranchId) &&
        plan.Targets.Count >= 2 &&
        plan.Targets.Select(target => target.ConsumerComponentId).All(value => !string.IsNullOrWhiteSpace(value)) &&
        plan.Targets.Select(target => target.ConsumerComponentId).Distinct(StringComparer.Ordinal).Count() == plan.Targets.Count &&
        plan.Targets.Select(target => target.RoutePathId).All(value => !string.IsNullOrWhiteSpace(value)) &&
        plan.Targets.Select(target => target.RoutePathId).Distinct(StringComparer.Ordinal).Count() == plan.Targets.Count &&
        plan.Targets.All(target => target.RouteLinkIds.Count > 0 &&
            target.RouteLinkIds.All(value => !string.IsNullOrWhiteSpace(value)) &&
            target.RouteLinkIds.Distinct(StringComparer.Ordinal).Count() == target.RouteLinkIds.Count);

    private static bool TryDecodePipeline(JsonElement element, int depth, out Phase8ABranchTargetPipeline? pipeline)
    {
        pipeline = null;
        if (depth > MaximumNestedDepth || element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("consumerComponentId", out var consumer) || consumer.ValueKind != JsonValueKind.String)
            return false;
        IReadOnlyList<Phase8AStageRoute> routes = [];
        if (element.TryGetProperty("downstreamRoutes", out var encodedRoutes) &&
            !Phase8AStageRouteMetadata.TryDecode(encodedRoutes.GetRawText(), out routes)) return false;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("metadataOverrides", out var overrides))
        {
            if (overrides.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in overrides.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || !IsMetadataOverrideAllowed(property.Name)) return false;
                metadata[property.Name] = property.Value.GetString() ?? "";
            }
        }
        Phase8AMulticastBranchPlan? multicastPlan = null;
        var nested = new List<Phase8ABranchTargetPipeline>();
        if (element.TryGetProperty("downstreamMulticastPlan", out var multicast))
        {
            if (!TryDecodeMulticast(multicast, out multicastPlan) || multicastPlan is null ||
                !multicast.TryGetProperty("targetPipelines", out var targetPipelines) || targetPipelines.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var item in targetPipelines.EnumerateArray())
            {
                if (!TryDecodePipeline(item, depth + 1, out var decoded) || decoded is null) return false;
                nested.Add(decoded);
            }
        }
        var candidate = new Phase8ABranchTargetPipeline(consumer.GetString() ?? "", routes, metadata, multicastPlan, nested);
        if (!IsStructurallyValid([candidate], depth)) return false;
        pipeline = candidate;
        return true;
    }

    private static bool TryDecodeMulticast(JsonElement element, out Phase8AMulticastBranchPlan? plan)
    {
        plan = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("flowId", out var flow) || flow.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("branchId", out var branch) || branch.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("targets", out var targets) ||
            !Phase8ACollectiveMetadataCodec.TryDecodeTargets(targets.GetRawText(), out var decoded)) return false;
        var candidate = new Phase8AMulticastBranchPlan(flow.GetString() ?? "", branch.GetString() ?? "", decoded);
        if (!IsStructurallyValid(candidate)) return false;
        plan = candidate;
        return true;
    }
}
