using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Stable packet metadata shared by Phase 8A collective runtime kernels.</summary>
public static class Phase8ACollectiveRuntimeMetadata
{
    /// <summary>Stable packet metadata key for OperationKind.</summary>
    public const string OperationKind = "phase8a.collective.operation";
    /// <summary>Stable packet metadata key for GroupKey.</summary>
    public const string GroupKey = "phase8a.collective.group_key";
    /// <summary>Stable packet metadata key for ExpectedContributors.</summary>
    public const string ExpectedContributors = "phase8a.collective.expected_contributors";
    /// <summary>Stable packet metadata key for ContributorId.</summary>
    public const string ContributorId = "phase8a.collective.contributor_id";
    /// <summary>Stable packet metadata key for OutputMOffset.</summary>
    public const string OutputMOffset = "phase8a.collective.output_m_offset";
    /// <summary>Stable packet metadata key for OutputMExtent.</summary>
    public const string OutputMExtent = "phase8a.collective.output_m_extent";
    /// <summary>Stable packet metadata key for OutputNOffset.</summary>
    public const string OutputNOffset = "phase8a.collective.output_n_offset";
    /// <summary>Stable packet metadata key for OutputNExtent.</summary>
    public const string OutputNExtent = "phase8a.collective.output_n_extent";
    /// <summary>Stable packet metadata key for TensorMExtent.</summary>
    public const string TensorMExtent = "phase8a.collective.tensor_m_extent";
    /// <summary>Stable packet metadata key for TensorNExtent.</summary>
    public const string TensorNExtent = "phase8a.collective.tensor_n_extent";
    /// <summary>Stable packet metadata key for DType.</summary>
    public const string DType = "phase8a.collective.dtype";
    /// <summary>Stable packet metadata key for OutputRoutePathId.</summary>
    public const string OutputRoutePathId = "phase8a.collective.output_route_path_id";
    /// <summary>Stable packet metadata key for OutputDestinationComponentId.</summary>
    public const string OutputDestinationComponentId = "phase8a.collective.output_destination_component_id";
    /// <summary>Stable packet metadata key for OutputRouteLinkIds.</summary>
    public const string OutputRouteLinkIds = "phase8a.collective.output_route_link_ids";

    /// <summary>Stable packet metadata key for MulticastFlowId.</summary>
    public const string MulticastFlowId = "phase8a.multicast.flow_id";
    /// <summary>Stable packet metadata key for MulticastBranchId.</summary>
    public const string MulticastBranchId = "phase8a.multicast.branch_id";
    /// <summary>Stable packet metadata key for MulticastTargets.</summary>
    public const string MulticastTargets = "phase8a.multicast.targets";
    /// <summary>Stable packet metadata key for MulticastParentPacketId.</summary>
    public const string MulticastParentPacketId = "phase8a.multicast.parent_packet_id";
    /// <summary>Stable packet metadata key for MulticastConsumerId.</summary>
    public const string MulticastConsumerId = "phase8a.multicast.consumer_id";
    /// <summary>Stable packet metadata key for MulticastConsumerSet.</summary>
    public const string MulticastConsumerSet = "phase8a.multicast.consumer_set";
    /// <summary>Stable packet metadata key for MulticastCloneOrdinal.</summary>
    public const string MulticastCloneOrdinal = "phase8a.multicast.clone_ordinal";
}

/// <summary>One branch-local multicast target and its exact directed-link route.</summary>
public sealed class Phase8AMulticastBranchTarget
{
    /// <summary>Creates an immutable branch target.</summary>
    public Phase8AMulticastBranchTarget(string consumerComponentId, IEnumerable<string>? routeLinkIds, string routePathId = "")
    {
        ConsumerComponentId = consumerComponentId?.Trim() ?? "";
        RoutePathId = routePathId?.Trim() ?? "";
        RouteLinkIds = Array.AsReadOnly((routeLinkIds ?? [])
            .Select(value => value?.Trim() ?? "")
            .ToArray());
    }

    /// <summary>Gets the exact consumer component id.</summary>
    public string ConsumerComponentId { get; }
    /// <summary>Gets the explicit route-path identity.</summary>
    public string RoutePathId { get; }
    /// <summary>Gets ordered directed link ids executed by the runtime.</summary>
    public IReadOnlyList<string> RouteLinkIds { get; }
}

/// <summary>Typed immutable branch plan consumed by the multicast packet binder.</summary>
public sealed class Phase8AMulticastBranchPlan
{
    /// <summary>Creates an immutable branch plan.</summary>
    public Phase8AMulticastBranchPlan(string flowId, string branchId, IEnumerable<Phase8AMulticastBranchTarget>? targets)
    {
        FlowId = flowId?.Trim() ?? "";
        BranchId = branchId?.Trim() ?? "";
        Targets = Array.AsReadOnly((targets ?? [])
            .Select(target => target is null
                ? new Phase8AMulticastBranchTarget("", [])
                : new Phase8AMulticastBranchTarget(target.ConsumerComponentId, target.RouteLinkIds, target.RoutePathId))
            .ToArray());
    }

    /// <summary>Gets the communication-flow identity.</summary>
    public string FlowId { get; }
    /// <summary>Gets the declared replication branch identity.</summary>
    public string BranchId { get; }
    /// <summary>Gets branch targets in stable clone order.</summary>
    public IReadOnlyList<Phase8AMulticastBranchTarget> Targets { get; }
}

/// <summary>Binds typed collective runtime metadata without exposing an ad-hoc string format to callers.</summary>
public static class Phase8ACollectivePacketBinder
{
    /// <summary>Binds a typed branch plan to a parent packet.</summary>
    public static void BindMulticast(Packet packet, Phase8AMulticastBranchPlan plan)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(plan.FlowId) || string.IsNullOrWhiteSpace(plan.BranchId))
            throw new ArgumentException("A multicast plan requires non-empty flow and branch ids.", nameof(plan));
        if (plan.Targets.Count < 2 ||
            plan.Targets.Select(target => target.ConsumerComponentId).Any(string.IsNullOrWhiteSpace) ||
            plan.Targets.Select(target => target.ConsumerComponentId).Distinct(StringComparer.Ordinal).Count() != plan.Targets.Count ||
            plan.Targets.Select(target => target.RoutePathId).Any(string.IsNullOrWhiteSpace) ||
            plan.Targets.Select(target => target.RoutePathId).Distinct(StringComparer.Ordinal).Count() != plan.Targets.Count ||
            plan.Targets.Any(target => target.RouteLinkIds.Count == 0 || target.RouteLinkIds.Any(string.IsNullOrWhiteSpace) ||
                target.RouteLinkIds.Distinct(StringComparer.Ordinal).Count() != target.RouteLinkIds.Count))
            throw new ArgumentException("A multicast plan requires at least two unique consumers with unique path ids and structurally valid exact routes.", nameof(plan));
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.OperationKind] = Phase8ABranchMulticastContract.MulticastOperation;
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastFlowId] = plan.FlowId;
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastBranchId] = plan.BranchId;
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastTargets] = Phase8ACollectiveMetadataCodec.EncodeTargets(plan.Targets);
    }

    /// <summary>Binds the exact route to use for a collective output packet.</summary>
    public static void BindOutputRoute(Packet packet, string routePathId, IEnumerable<string>? routeLinkIds, string destinationComponentId = "")
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        var normalizedPathId = routePathId?.Trim() ?? "";
        var links = (routeLinkIds ?? []).Select(value => value?.Trim() ?? "").ToArray();
        if (string.IsNullOrWhiteSpace(normalizedPathId))
            throw new ArgumentException("A collective output route requires a non-empty path id.", nameof(routePathId));
        if (links.Length == 0 || links.Any(string.IsNullOrWhiteSpace) ||
            links.Distinct(StringComparer.Ordinal).Count() != links.Length)
            throw new ArgumentException("A collective output route requires unique non-empty directed link ids.", nameof(routeLinkIds));
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.OutputRoutePathId] = normalizedPathId;
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.OutputDestinationComponentId] = destinationComponentId?.Trim() ?? "";
        packet.Metadata[Phase8ACollectiveRuntimeMetadata.OutputRouteLinkIds] =
            Phase8ACollectiveMetadataCodec.EncodeStringList(links);
    }
}

internal static class Phase8ACollectiveMetadataCodec
{
    public static string EncodeStringList(IEnumerable<string> values) => JsonSerializer.Serialize(
        values.Select(value => value?.Trim() ?? "").ToArray(), HardwareGraphJson.Options);

    public static bool TryDecodeStringList(string? raw, out IReadOnlyList<string> values)
    {
        values = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            if (!raw.TrimStart().StartsWith("[", StringComparison.Ordinal)) return false;
            var decoded = JsonSerializer.Deserialize<List<string>>(raw, HardwareGraphJson.Options);
            if (decoded is null) return false;
            values = Array.AsReadOnly(decoded.Select(value => value?.Trim() ?? "").ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string EncodeTargets(IEnumerable<Phase8AMulticastBranchTarget> targets) => JsonSerializer.Serialize(
        targets.Select(target => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["consumerComponentId"] = target.ConsumerComponentId,
            ["routePathId"] = target.RoutePathId,
            ["routeLinkIds"] = target.RouteLinkIds
        }).ToArray(), HardwareGraphJson.Options);

    public static bool TryDecodeTargets(string? raw, out IReadOnlyList<Phase8AMulticastBranchTarget> targets)
    {
        targets = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var decoded = new List<Phase8AMulticastBranchTarget>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("consumerComponentId", out var consumer) ||
                    consumer.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("routeLinkIds", out var route) ||
                    route.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }
                var pathId = element.TryGetProperty("routePathId", out var path) && path.ValueKind == JsonValueKind.String
                    ? path.GetString() ?? ""
                    : "";
                decoded.Add(new Phase8AMulticastBranchTarget(
                    consumer.GetString() ?? "",
                    route.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : ""),
                    pathId));
            }
            targets = new ReadOnlyCollection<Phase8AMulticastBranchTarget>(decoded);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryReadRequired(Packet packet, string key, out string value)
    {
        value = "";
        if (!packet.Metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
        value = raw.Trim();
        return true;
    }

    public static bool TryReadInt(Packet packet, string key, int minimum, out int value)
    {
        value = minimum - 1;
        return TryReadRequired(packet, key, out var raw) &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
               value >= minimum;
    }

    public static bool TryParsePrecision(string raw, out PrecisionKind precision)
    {
        if (Enum.TryParse(raw, true, out precision)) return precision != PrecisionKind.Any;
        precision = raw.Trim().ToLowerInvariant() switch
        {
            "fp8" => PrecisionKind.FP8_E4M3,
            "fp16" => PrecisionKind.FP16,
            "fp32" => PrecisionKind.FP32,
            "bf16" => PrecisionKind.BF16,
            "int8" => PrecisionKind.INT8,
            "int16" => PrecisionKind.INT16,
            "int32" => PrecisionKind.INT32,
            _ => PrecisionKind.Any
        };
        return precision != PrecisionKind.Any;
    }

    public static bool TryReadOutputRoute(
        Packet packet,
        out string pathId,
        out string destinationComponentId,
        out IReadOnlyList<string> links,
        out string reason)
    {
        pathId = "";
        destinationComponentId = "";
        links = [];
        reason = "";
        var hasPath = packet.Metadata.ContainsKey(Phase8ACollectiveRuntimeMetadata.OutputRoutePathId);
        var hasDestination = packet.Metadata.ContainsKey(Phase8ACollectiveRuntimeMetadata.OutputDestinationComponentId);
        var hasLinks = packet.Metadata.ContainsKey(Phase8ACollectiveRuntimeMetadata.OutputRouteLinkIds);
        if (!hasPath && !hasDestination && !hasLinks) return true;
        if (!hasPath || !hasDestination || !hasLinks)
        {
            reason = "collective output route metadata must provide path, destination, and link-list fields together";
            return false;
        }

        pathId = packet.Metadata[Phase8ACollectiveRuntimeMetadata.OutputRoutePathId].Trim();
        destinationComponentId = packet.Metadata[Phase8ACollectiveRuntimeMetadata.OutputDestinationComponentId].Trim();
        if (string.IsNullOrWhiteSpace(pathId) ||
            !TryDecodeStringList(packet.Metadata[Phase8ACollectiveRuntimeMetadata.OutputRouteLinkIds], out links) ||
            links.Count == 0 || links.Any(string.IsNullOrWhiteSpace) ||
            links.Distinct(StringComparer.Ordinal).Count() != links.Count)
        {
            reason = "collective output route requires a non-empty path id and unique non-empty JSON link ids";
            return false;
        }
        return true;
    }

    public static void ApplyOutputRoute(Packet source, Packet output)
    {
        if (source.Metadata.ContainsKey(Phase8AStageRouteMetadata.RemainingRoutes))
        {
            if (!Phase8AStageRouteMetadata.TryValidateBoundMetadata(source, out var stageReason) ||
                !Phase8AStageRouteMetadata.ApplyNext(source, output))
                throw new InvalidOperationException("Malformed Phase 8A stage routing metadata: " + stageReason);
            return;
        }
        if (!TryReadOutputRoute(source, out var pathId, out var destinationComponentId, out var links, out var outputReason))
            throw new InvalidOperationException("Malformed Phase 8A output routing metadata: " + outputReason);
        output.Metadata.Remove(Phase8AExplicitRouteMetadata.LinkIds);
        output.Metadata.Remove(Phase8AExplicitRouteMetadata.NextLinkIndex);
        output.Metadata.Remove(Phase8AExplicitRouteMetadata.PathId);
        output.RoutePath = [];
        if (links.Count == 0) return;
        output.DestinationComponentId = destinationComponentId;
        Phase8AExplicitRouteMetadata.Bind(output, pathId, links);
    }
}
