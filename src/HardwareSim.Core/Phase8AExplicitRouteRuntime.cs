using System.Globalization;

namespace HardwareSim.Core;

/// <summary>Packet metadata for exact directed-link execution of a mapped Phase 8A route.</summary>
public static class Phase8AExplicitRouteMetadata
{
    /// <summary>Stable exact-route metadata key for PathId.</summary>
    public const string PathId = "phase8a.route.path_id";
    /// <summary>Stable exact-route metadata key for LinkIds.</summary>
    public const string LinkIds = "phase8a.route.link_ids";
    /// <summary>Stable exact-route metadata key for NextLinkIndex.</summary>
    public const string NextLinkIndex = "phase8a.route.next_link_index";

    /// <summary>Binds an ordered directed-link route and resets its execution cursor.</summary>
    public static void Bind(Packet packet, string pathId, IEnumerable<string>? directedLinkIds)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        var normalizedPathId = pathId?.Trim() ?? "";
        var links = (directedLinkIds ?? []).Select(value => value?.Trim() ?? "").ToArray();
        if (string.IsNullOrWhiteSpace(normalizedPathId))
            throw new ArgumentException("An exact route requires a non-empty path id.", nameof(pathId));
        if (links.Length == 0 || links.Any(string.IsNullOrWhiteSpace) ||
            links.Distinct(StringComparer.Ordinal).Count() != links.Length)
            throw new ArgumentException("An exact route requires unique non-empty directed link ids.", nameof(directedLinkIds));
        packet.Metadata[PathId] = normalizedPathId;
        packet.Metadata[LinkIds] = Phase8ACollectiveMetadataCodec.EncodeStringList(links);
        packet.Metadata[NextLinkIndex] = "0";
        packet.RoutePath = links.ToList();
    }
}

internal enum Phase8AExplicitRouteResolutionKind
{
    NotBound,
    Resolved,
    Error
}

internal sealed record Phase8AExplicitRouteResolution(
    Phase8AExplicitRouteResolutionKind Kind,
    SimLinkDef? Link,
    string Detail,
    string ErrorCode = "",
    string ErrorMessage = "");

internal static class Phase8AExplicitRouteResolver
{
    public static Phase8AExplicitRouteResolution Resolve(
        HardwareSimulationGraph graph,
        Packet packet,
        string currentComponentId,
        string? requiredSourcePort)
    {
        if (!packet.Metadata.TryGetValue(Phase8AExplicitRouteMetadata.LinkIds, out var raw))
            return new(Phase8AExplicitRouteResolutionKind.NotBound, null, "");
        if (!Phase8ACollectiveMetadataCodec.TryDecodeStringList(raw, out var ids) || ids.Count == 0 ||
            ids.Any(string.IsNullOrWhiteSpace))
        {
            return Error("Phase8AExplicitRouteMalformed", "The packet exact route must contain a non-empty JSON list of directed link ids.");
        }
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return Error("Phase8AExplicitRouteRepeatedLink", "The packet exact route cannot traverse the same directed link more than once.");
        if (!packet.Metadata.TryGetValue(Phase8AExplicitRouteMetadata.NextLinkIndex, out var indexRaw) ||
            !int.TryParse(indexRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
            index < 0 || index > ids.Count)
        {
            return Error("Phase8AExplicitRouteProgressInvalid", "The packet exact-route progress index is missing or outside the declared route.");
        }
        if (index == ids.Count)
            return Error("Phase8AExplicitRouteExhausted", $"Packet '{packet.Id}' has no remaining mapped link at component '{currentComponentId}'.");

        var matches = graph.Links.Where(link => string.Equals(link.Id, ids[index], StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            return Error("Phase8AExplicitRouteLinkMissing", $"Mapped link '{ids[index]}' is absent or non-unique in the executable graph.");
        var selected = matches[0];
        if (!string.Equals(selected.Source.ComponentId, currentComponentId, StringComparison.Ordinal))
            return Error("Phase8AExplicitRouteSourceMismatch", $"Mapped link '{selected.Id}' does not start at current component '{currentComponentId}'.");
        if (!string.IsNullOrWhiteSpace(requiredSourcePort) &&
            !string.Equals(selected.Source.PortName, requiredSourcePort, StringComparison.Ordinal))
        {
            return Error("Phase8AExplicitRoutePortMismatch", $"Mapped link '{selected.Id}' does not use required source port '{requiredSourcePort}'.");
        }
        if (index == ids.Count - 1 && !string.IsNullOrWhiteSpace(packet.DestinationComponentId) &&
            !string.Equals(selected.Destination.ComponentId, packet.DestinationComponentId, StringComparison.Ordinal))
        {
            return Error("Phase8AExplicitRouteEndpointMismatch", $"Mapped route ends at '{selected.Destination.ComponentId}' instead of packet destination '{packet.DestinationComponentId}'.");
        }

        var pathId = packet.Metadata.GetValueOrDefault(Phase8AExplicitRouteMetadata.PathId, "");
        return new(Phase8AExplicitRouteResolutionKind.Resolved, selected,
            $"routing=phase8a_explicit;path_id={pathId};hop_index={index};hop_count={ids.Count};selected={selected.Id}");
    }

    public static bool Advance(Packet packet, string selectedLinkId, out string error)
    {
        error = "";
        if (!packet.Metadata.TryGetValue(Phase8AExplicitRouteMetadata.LinkIds, out var raw)) return true;
        if (!Phase8ACollectiveMetadataCodec.TryDecodeStringList(raw, out var ids) ||
            !packet.Metadata.TryGetValue(Phase8AExplicitRouteMetadata.NextLinkIndex, out var indexRaw) ||
            !int.TryParse(indexRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
            index < 0 || index >= ids.Count || !string.Equals(ids[index], selectedLinkId, StringComparison.Ordinal))
        {
            error = "The selected link does not match the packet exact-route progress.";
            return false;
        }
        packet.Metadata[Phase8AExplicitRouteMetadata.NextLinkIndex] = (index + 1).ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static Phase8AExplicitRouteResolution Error(string code, string message) =>
        new(Phase8AExplicitRouteResolutionKind.Error, null, "", code, message);
}
