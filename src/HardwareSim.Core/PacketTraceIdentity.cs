namespace HardwareSim.Core;

/// <summary>Maintains physical-to-logical packet identity across deterministic packet transformations.</summary>
public static class PacketTraceIdentity
{
    /// <summary>Metadata key containing the stable root packet id shared by a derived packet family.</summary>
    public const string LogicalPacketIdKey = "logical_packet_id";
    /// <summary>Metadata key containing the immediate physical parent packet id.</summary>
    public const string ParentPacketIdKey = "parent_packet_id";

    /// <summary>Assigns a deterministic derived id while retaining logical and immediate-parent provenance.</summary>
    public static void AssignDerived(Packet derived, Packet parent, string derivedId)
    {
        if (derived is null) throw new ArgumentNullException(nameof(derived));
        if (parent is null) throw new ArgumentNullException(nameof(parent));
        if (string.IsNullOrWhiteSpace(derivedId))
        {
            throw new ArgumentException("Derived packet id is required.", nameof(derivedId));
        }

        derived.Id = derivedId;
        derived.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        derived.Metadata[LogicalPacketIdKey] = LogicalPacketId(parent);
        derived.Metadata[ParentPacketIdKey] = parent.Id;
    }

    /// <summary>Returns the stable logical root id, falling back to the packet's physical id.</summary>
    public static string LogicalPacketId(Packet packet)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        return packet.Metadata is not null &&
               packet.Metadata.TryGetValue(LogicalPacketIdKey, out var logicalId) &&
               !string.IsNullOrWhiteSpace(logicalId)
            ? logicalId
            : packet.Id;
    }

    /// <summary>Appends available packet identity provenance to a semicolon-delimited trace detail.</summary>
    public static string AppendTraceDetail(string detail, Packet packet)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        var identity = TraceDetail(packet);
        if (string.IsNullOrWhiteSpace(identity))
        {
            return detail;
        }

        return string.IsNullOrWhiteSpace(detail) ? identity : detail + ";" + identity;
    }

    /// <summary>Returns identity trace detail, preserving null when the packet has no derived identity.</summary>
    public static string? TraceDetailOrNull(Packet packet)
    {
        var detail = TraceDetail(packet);
        return string.IsNullOrWhiteSpace(detail) ? null : detail;
    }

    /// <summary>Formats available packet identity provenance for trace persistence.</summary>
    public static string TraceDetail(Packet packet)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        if (packet.Metadata is null)
        {
            return "";
        }

        var fields = new List<string>();
        if (packet.Metadata.TryGetValue(LogicalPacketIdKey, out var logicalId) &&
            !string.IsNullOrWhiteSpace(logicalId))
        {
            fields.Add(LogicalPacketIdKey + "=" + logicalId);
        }
        if (packet.Metadata.TryGetValue(ParentPacketIdKey, out var parentId) &&
            !string.IsNullOrWhiteSpace(parentId))
        {
            fields.Add(ParentPacketIdKey + "=" + parentId);
        }
        return string.Join(";", fields);
    }

    /// <summary>Reads one value from the repository's semicolon-delimited trace detail format.</summary>
    public static string? DetailValue(string? detail, string key)
    {
        if (string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var token in detail.Split(';'))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 ||
                !string.Equals(token.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = token.Substring(separator + 1).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
