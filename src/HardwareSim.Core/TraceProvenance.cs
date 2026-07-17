namespace HardwareSim.Core;

/// <summary>Structured provenance carried by critical trace events.</summary>
/// <param name="WorkloadOpId">Originating workload operation identifier.</param>
/// <param name="TensorId">Originating tensor identifier.</param>
/// <param name="TileId">Originating tensor tile identifier.</param>
/// <param name="PacketBits">Full logical packet size in bits.</param>
/// <param name="StallReason">Structured stall reason when the event is a stall.</param>
/// <param name="EnergyCategory">System-level energy category associated with the event.</param>
public sealed record TraceProvenance(
    string WorkloadOpId,
    string TensorId,
    string TileId,
    BitCount PacketBits,
    StallReason? StallReason,
    EnergyCategory? EnergyCategory);

/// <summary>Attaches packet and classification provenance to immutable trace events.</summary>
public static class TraceProvenanceFactory
{
    /// <summary>Returns a copy of the event with exact structured packet provenance.</summary>
    public static TraceEvent Attach(
        TraceEvent traceEvent,
        Packet packet,
        StallReason? stallReason = null,
        EnergyCategory? energyCategory = null)
    {
        if (traceEvent is null)
        {
            throw new ArgumentNullException(nameof(traceEvent));
        }
        if (packet is null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        return traceEvent with
        {
            Provenance = new TraceProvenance(
                packet.WorkloadOpId,
                packet.TensorId,
                packet.TileId,
                new BitCount(packet.Bits),
                stallReason,
                energyCategory)
        };
    }
}

/// <summary>One structured trace provenance validation failure.</summary>
/// <param name="EventIndex">Stable event index in the validated sequence.</param>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Message">Human-readable failure explanation.</param>
public sealed record TraceProvenanceIssue(int EventIndex, string Code, string Message);

/// <summary>Validates that critical events expose complete, structured provenance.</summary>
public static class TraceProvenanceValidator
{
    /// <summary>Returns every exact provenance failure in stable event order.</summary>
    public static IReadOnlyList<TraceProvenanceIssue> Validate(IEnumerable<TraceEvent> events)
    {
        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        var issues = new List<TraceProvenanceIssue>();
        var index = 0;
        foreach (var traceEvent in events)
        {
            if (!IsCritical(traceEvent.Type))
            {
                index++;
                continue;
            }

            var provenance = traceEvent.Provenance;
            if (provenance is null)
            {
                issues.Add(new TraceProvenanceIssue(index, "MissingTraceProvenance", "Critical event has no structured provenance."));
                index++;
                continue;
            }

            RequireIdentifier(issues, index, provenance.WorkloadOpId, "MissingWorkloadOpId");
            RequireIdentifier(issues, index, provenance.TensorId, "MissingTensorId");
            RequireIdentifier(issues, index, provenance.TileId, "MissingTileId");
            if (provenance.PacketBits.Value <= 0)
            {
                issues.Add(new TraceProvenanceIssue(index, "MissingPacketBits", "Packet bits must be greater than zero."));
            }
            if (traceEvent.Type != TraceEventType.FlitIssue &&
                traceEvent.Type != TraceEventType.FlitSerialization &&
                traceEvent.Type != TraceEventType.FlitArrival &&
                traceEvent.Bits > 0 &&
                traceEvent.Bits != provenance.PacketBits.Value)
            {
                issues.Add(new TraceProvenanceIssue(index, "PacketBitsMismatch", "Packet-level event bits do not match packet provenance."));
            }
            if (traceEvent.Type == TraceEventType.Stall && !provenance.StallReason.HasValue)
            {
                issues.Add(new TraceProvenanceIssue(index, "MissingStallReason", "Stall event requires a structured stall reason."));
            }
            if (!provenance.EnergyCategory.HasValue)
            {
                issues.Add(new TraceProvenanceIssue(index, "MissingEnergyCategory", "Critical event requires an energy category."));
            }

            index++;
        }

        return issues.AsReadOnly();
    }

    private static bool IsCritical(TraceEventType type) =>
        type is TraceEventType.PacketInjection or TraceEventType.PacketMove or TraceEventType.LinkTransfer or
            TraceEventType.Compute or TraceEventType.Stall or TraceEventType.Energy or
            TraceEventType.FlitIssue or TraceEventType.FlitSerialization or TraceEventType.FlitArrival;

    private static void RequireIdentifier(
        ICollection<TraceProvenanceIssue> issues,
        int eventIndex,
        string value,
        string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new TraceProvenanceIssue(eventIndex, code, "Required provenance identifier is empty."));
        }
    }
}
