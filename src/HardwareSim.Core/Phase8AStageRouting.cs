using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>One exact directed route consumed by the next data-producing pipeline stage.</summary>
public sealed class Phase8AStageRoute
{
    /// <summary>Creates one immutable stage route.</summary>
    public Phase8AStageRoute(
        string routePathId,
        string destinationComponentId,
        IEnumerable<string>? linkIds,
        IReadOnlyDictionary<string, string>? metadataOverrides = null)
    {
        RoutePathId = routePathId?.Trim() ?? "";
        DestinationComponentId = destinationComponentId?.Trim() ?? "";
        LinkIds = Array.AsReadOnly((linkIds ?? []).Select(value => value?.Trim() ?? "").ToArray());
        MetadataOverrides = new ReadOnlyDictionary<string, string>((metadataOverrides ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key?.Trim() ?? "", pair => pair.Value?.Trim() ?? "", StringComparer.Ordinal));
    }

    /// <summary>Gets the logical route identity.</summary>
    public string RoutePathId { get; }
    /// <summary>Gets the route destination.</summary>
    public string DestinationComponentId { get; }
    /// <summary>Gets exact directed links in execution order.</summary>
    public IReadOnlyList<string> LinkIds { get; }
    /// <summary>Gets metadata applied atomically when this stage becomes the active destination.</summary>
    public IReadOnlyDictionary<string, string> MetadataOverrides { get; }
}

/// <summary>Binds downstream routes without exposing the runtime metadata encoding.</summary>
public static class Phase8AStageRouteBinder
{
    /// <summary>Binds routes that successive data-producing stages consume one at a time.</summary>
    public static void BindRemainingRoutes(Packet packet, IEnumerable<Phase8AStageRoute>? routes)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        Phase8AStageRouteMetadata.BindRemaining(packet, routes ?? []);
    }
}

internal static class Phase8AStageRouteMetadata
{
    public const string RemainingRoutes = "phase8a.pipeline.remaining_stage_routes";

    public static void BindRemaining(Packet packet, IEnumerable<Phase8AStageRoute> routes)
    {
        var materialized = routes.Select(route => route is null
            ? throw new ArgumentException("Stage routes cannot contain null entries.", nameof(routes))
            : Clone(route)).ToArray();
        if (materialized.Any(route => !IsStructurallyValid(route)))
            throw new ArgumentException("Every stage route requires non-empty path and destination ids plus unique non-empty directed link ids.", nameof(routes));
        if (materialized.Length == 0)
        {
            packet.Metadata.Remove(RemainingRoutes);
            return;
        }
        packet.Metadata[RemainingRoutes] = Encode(materialized);
    }

    public static bool ApplyNext(Packet source, Packet output)
    {
        if (!source.Metadata.TryGetValue(RemainingRoutes, out var raw) || !TryDecode(raw, out var routes) || routes.Count == 0)
            return false;

        var next = routes[0];
        output.Metadata.Remove(Phase8AExplicitRouteMetadata.LinkIds);
        output.Metadata.Remove(Phase8AExplicitRouteMetadata.NextLinkIndex);
        output.Metadata.Remove(Phase8AExplicitRouteMetadata.PathId);
        output.RoutePath = [];
        output.DestinationComponentId = next.DestinationComponentId;
        Phase8AExplicitRouteMetadata.Bind(output, next.RoutePathId, next.LinkIds);
        BindRemaining(output, routes.Skip(1));
        foreach (var pair in next.MetadataOverrides) output.Metadata[pair.Key] = pair.Value;
        if (next.MetadataOverrides.ContainsKey(Phase8ACollectiveRuntimeMetadata.GroupKey) &&
            source.Metadata.TryGetValue(Phase8AOperandPipelineMetadata.InvocationId, out var invocationId) &&
            !string.IsNullOrWhiteSpace(invocationId))
        {
            output.Metadata[Phase8ACollectiveRuntimeMetadata.GroupKey] =
                output.Metadata[Phase8ACollectiveRuntimeMetadata.GroupKey] + ";invocation=" + invocationId;
        }
        return true;
    }

    public static bool TryValidateBoundMetadata(Packet packet, out string reason)
    {
        reason = "";
        if (!packet.Metadata.TryGetValue(RemainingRoutes, out var raw)) return true;
        if (TryDecode(raw, out var routes) && routes.Count > 0) return true;
        reason = "remaining stage routes must be a non-empty JSON array of structurally valid exact routes";
        return false;
    }

    public static string Encode(IEnumerable<Phase8AStageRoute> routes) => JsonSerializer.Serialize(
        routes.Select(route =>
        {
            var encoded = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["routePathId"] = route.RoutePathId,
                ["destinationComponentId"] = route.DestinationComponentId,
                ["linkIds"] = route.LinkIds
            };
            if (route.MetadataOverrides.Count > 0) encoded["metadataOverrides"] = route.MetadataOverrides;
            return encoded;
        }).ToArray(), HardwareGraphJson.Options);

    public static bool TryDecode(string? raw, out IReadOnlyList<Phase8AStageRoute> routes)
    {
        routes = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var decoded = new List<Phase8AStageRoute>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("routePathId", out var path) || path.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("destinationComponentId", out var destination) || destination.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("linkIds", out var links) || links.ValueKind != JsonValueKind.Array)
                    return false;
                var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                if (element.TryGetProperty("metadataOverrides", out var overrides))
                {
                    if (overrides.ValueKind != JsonValueKind.Object) return false;
                    foreach (var property in overrides.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String) return false;
                        metadata[property.Name] = property.Value.GetString() ?? "";
                    }
                }
                var decodedRoute = new Phase8AStageRoute(
                    path.GetString() ?? "",
                    destination.GetString() ?? "",
                    links.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : ""),
                    metadata);
                if (!IsStructurallyValid(decodedRoute)) return false;
                decoded.Add(decodedRoute);
            }
            routes = new ReadOnlyCollection<Phase8AStageRoute>(decoded);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Phase8AStageRoute Clone(Phase8AStageRoute route) =>
        new(route.RoutePathId, route.DestinationComponentId, route.LinkIds, route.MetadataOverrides);

    internal static bool IsStructurallyValid(Phase8AStageRoute route) =>
        !string.IsNullOrWhiteSpace(route.RoutePathId) &&
        !string.IsNullOrWhiteSpace(route.DestinationComponentId) &&
        route.LinkIds.Count > 0 &&
        route.LinkIds.All(linkId => !string.IsNullOrWhiteSpace(linkId)) &&
        route.LinkIds.Distinct(StringComparer.Ordinal).Count() == route.LinkIds.Count &&
        route.MetadataOverrides.All(pair => IsMetadataOverrideAllowed(pair.Key));

    private static bool IsMetadataOverrideAllowed(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        !string.Equals(key, RemainingRoutes, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AExplicitRouteMetadata.PathId, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AExplicitRouteMetadata.LinkIds, StringComparison.Ordinal) &&
        !string.Equals(key, Phase8AExplicitRouteMetadata.NextLinkIndex, StringComparison.Ordinal);
}
