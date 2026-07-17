namespace HardwareSim.Core;

/// <summary>Stores immutable logical packets referenced by flit transport state.</summary>
public sealed class PacketCatalog
{
    private readonly SortedDictionary<string, Packet> packets = new(StringComparer.Ordinal);

    /// <summary>Adds or replaces a packet by stable packet id.</summary>
    public void Add(Packet packet)
    {
        if (packet is null)
        {
            throw new ArgumentNullException(nameof(packet));
        }
        if (string.IsNullOrWhiteSpace(packet.Id))
        {
            throw new ArgumentException("Packet id is required.", nameof(packet));
        }

        packets[packet.Id] = ClonePacket(packet);
    }

    /// <summary>Gets a packet clone by id.</summary>
    public Packet Get(string packetId) => packets.TryGetValue(packetId, out var packet)
        ? ClonePacket(packet)
        : throw new KeyNotFoundException($"Packet '{packetId}' is not present in the packet catalog.");

    /// <summary>Attempts to get a packet clone by id.</summary>
    public bool TryGet(string packetId, out Packet packet)
    {
        if (packets.TryGetValue(packetId, out var found))
        {
            packet = ClonePacket(found);
            return true;
        }

        packet = new Packet();
        return false;
    }

    /// <summary>Gets packet ids in deterministic order.</summary>
    public IReadOnlyList<string> PacketIds => packets.Keys.ToList();

    internal static Packet ClonePacket(Packet packet) => new()
    {
        Id = packet.Id,
        PacketType = packet.PacketType,
        NumElements = packet.NumElements,
        BitWidth = packet.BitWidth,
        Bits = packet.Bits,
        Precision = packet.Precision,
        SourceComponentId = packet.SourceComponentId,
        DestinationComponentId = packet.DestinationComponentId,
        SourcePort = packet.SourcePort,
        DestinationPort = packet.DestinationPort,
        WorkloadOpId = packet.WorkloadOpId,
        TensorId = packet.TensorId,
        TileId = packet.TileId,
        RoutePath = packet.RoutePath.ToList(),
        DependencyIds = packet.DependencyIds.ToList(),
        Metadata = new Dictionary<string, string>(packet.Metadata, StringComparer.OrdinalIgnoreCase),
        SignalDomain = packet.SignalDomain,
        OpticalState = packet.OpticalState is null ? null : packet.OpticalState with { },
        RequestId = packet.RequestId,
        MemoryOperation = packet.MemoryOperation,
        MemoryAddress = packet.MemoryAddress,
        CurrentComponentId = packet.CurrentComponentId,
        CreatedCycle = packet.CreatedCycle,
        InjectionCycle = packet.InjectionCycle,
        DeliveredCycle = packet.DeliveredCycle,
        ArrivalCycle = packet.ArrivalCycle,
        VisitedComponents = packet.VisitedComponents.ToList(),
        Values = packet.Values.ToList()
    };
}

/// <summary>Creates deterministic flit sequences from logical packets.</summary>
public static class FlitPacketizer
{
    /// <summary>Splits one packet into head/body/tail flits using the supplied flit size.</summary>
    public static IReadOnlyList<Flit> Packetize(Packet packet, int flitSizeBits)
    {
        if (packet is null)
        {
            throw new ArgumentNullException(nameof(packet));
        }
        if (flitSizeBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flitSizeBits), flitSizeBits, "Flit size must be positive.");
        }
        if (packet.TotalBits <= 0)
        {
            throw new ArgumentException("Packet total bits must be positive for flit packetization.", nameof(packet));
        }

        var totalFlits = (int)Math.Ceiling(packet.TotalBits / (double)flitSizeBits);
        var flits = new List<Flit>(totalFlits);
        for (var index = 0; index < totalFlits; index++)
        {
            var remaining = packet.TotalBits - index * flitSizeBits;
            var payloadBits = Math.Min(flitSizeBits, remaining);
            flits.Add(new Flit
            {
                Id = $"{packet.Id}:f{index:0000}",
                PacketId = packet.Id,
                FlitIndex = index,
                TotalFlits = totalFlits,
                PayloadBits = payloadBits,
                IsHead = index == 0,
                IsTail = index == totalFlits - 1,
                VirtualChannel = packet.Metadata.GetValueOrDefault("virtual_channel", ""),
                RouteMetadata = string.Join("->", packet.RoutePath),
                CreationCycle = packet.CreatedCycle,
                Metadata = new Dictionary<string, string>(packet.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["packet_bits"] = packet.TotalBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["flit_size_bits"] = flitSizeBits.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            });
        }

        return flits;
    }
}

/// <summary>Represents the result of accepting a flit into a packet assembler.</summary>
public sealed class FlitAssemblyResult
{
    private FlitAssemblyResult(bool isSuccess, bool isComplete, string code, string message, string packetId, Packet? packet)
    {
        IsSuccess = isSuccess;
        IsComplete = isComplete;
        Code = code;
        Message = message;
        PacketId = packetId;
        Packet = packet;
    }

    /// <summary>Gets whether the accepted flit preserved the sequence contract.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets whether the packet became complete after this flit.</summary>
    public bool IsComplete { get; }
    /// <summary>Gets the stable result or error code.</summary>
    public string Code { get; }
    /// <summary>Gets a human-readable result message.</summary>
    public string Message { get; }
    /// <summary>Gets the packet id associated with the result.</summary>
    public string PacketId { get; }
    /// <summary>Gets the reassembled packet clone when complete.</summary>
    public Packet? Packet { get; }

    /// <summary>Creates an incomplete successful result.</summary>
    public static FlitAssemblyResult Accepted(string packetId) => new(true, false, "Accepted", "Flit accepted.", packetId, null);
    /// <summary>Creates a complete successful result.</summary>
    public static FlitAssemblyResult Complete(string packetId, Packet packet) => new(true, true, "Complete", "Packet reassembled.", packetId, packet);
    /// <summary>Creates a sequence error result.</summary>
    public static FlitAssemblyResult Failure(string packetId, string message) => new(false, false, "FlitSequenceError", message, packetId, null);
}

/// <summary>Reassembles flits into logical packets after validating sequence and catalog contracts.</summary>
public sealed class PacketAssembler
{
    private readonly PacketCatalog catalog;
    private readonly Dictionary<string, SortedDictionary<int, Flit>> pending = new(StringComparer.Ordinal);

    /// <summary>Initializes a packet assembler backed by a packet catalog.</summary>
    public PacketAssembler(PacketCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>Accepts a flit and returns whether its packet is now complete.</summary>
    public FlitAssemblyResult Accept(Flit flit)
    {
        if (flit is null)
        {
            throw new ArgumentNullException(nameof(flit));
        }
        if (!catalog.TryGet(flit.PacketId, out var packet))
        {
            return FlitAssemblyResult.Failure(flit.PacketId, $"Packet '{flit.PacketId}' is missing from the catalog.");
        }
        if (flit.TotalFlits <= 0 || flit.FlitIndex < 0 || flit.FlitIndex >= flit.TotalFlits || flit.PayloadBits <= 0)
        {
            return FlitAssemblyResult.Failure(flit.PacketId, "Flit index, total count, and payload bits must be positive and in range.");
        }
        if (flit.Id != $"{flit.PacketId}:f{flit.FlitIndex:0000}")
        {
            return FlitAssemblyResult.Failure(flit.PacketId, "Flit id must match packet_id:f0000 sequence.");
        }
        if (flit.IsHead != (flit.FlitIndex == 0) || flit.IsTail != (flit.FlitIndex == flit.TotalFlits - 1))
        {
            return FlitAssemblyResult.Failure(flit.PacketId, "Exactly the first flit must be head and exactly the final flit must be tail.");
        }

        if (!pending.TryGetValue(flit.PacketId, out var flits))
        {
            pending[flit.PacketId] = flits = new SortedDictionary<int, Flit>();
        }
        if (flits.TryGetValue(flit.FlitIndex, out _))
        {
            return FlitAssemblyResult.Failure(flit.PacketId, $"Duplicate flit index {flit.FlitIndex}.");
        }

        flits[flit.FlitIndex] = CloneFlit(flit);
        if (flits.Count < flit.TotalFlits)
        {
            return FlitAssemblyResult.Accepted(flit.PacketId);
        }

        for (var index = 0; index < flit.TotalFlits; index++)
        {
            if (!flits.TryGetValue(index, out var item) || item.TotalFlits != flit.TotalFlits)
            {
                return FlitAssemblyResult.Failure(flit.PacketId, "Flit indexes must be contiguous and share the same total count.");
            }
        }

        var totalBits = flits.Values.Sum(item => item.PayloadBits);
        if (totalBits != packet.TotalBits)
        {
            return FlitAssemblyResult.Failure(flit.PacketId, $"Reassembled flit bits {totalBits} do not match packet bits {packet.TotalBits}.");
        }

        pending.Remove(flit.PacketId);
        return FlitAssemblyResult.Complete(flit.PacketId, packet);
    }

    private static Flit CloneFlit(Flit flit) => new()
    {
        Id = flit.Id,
        PacketId = flit.PacketId,
        FlitIndex = flit.FlitIndex,
        TotalFlits = flit.TotalFlits,
        PayloadBits = flit.PayloadBits,
        IsHead = flit.IsHead,
        IsTail = flit.IsTail,
        VirtualChannel = flit.VirtualChannel,
        RouteMetadata = flit.RouteMetadata,
        CreationCycle = flit.CreationCycle,
        Metadata = new Dictionary<string, string>(flit.Metadata, StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>Represents one cycle of serializer service for a flit.</summary>
public sealed record FlitSerializationSegment(string FlitId, long Cycle, int Bits);

/// <summary>Represents exact serialization timing for one flit.</summary>
public sealed record FlitLinkTraceEntry(
    string FlitId,
    string PacketId,
    int FlitIndex,
    int Bits,
    long IssueCycle,
    long SerializationDoneCycle,
    long ArrivalCycle,
    IReadOnlyList<FlitSerializationSegment> Segments);

/// <summary>Represents exact metrics and trace from deterministic link serialization.</summary>
public sealed class FlitLinkSimulationResult
{
    /// <summary>Gets exact per-flit timing entries.</summary>
    public List<FlitLinkTraceEntry> Trace { get; } = [];
    /// <summary>Gets total payload bits emitted by the serializer.</summary>
    public long TotalBitsTransferred { get; set; }
    /// <summary>Gets the number of cycles with nonzero serializer service.</summary>
    public long BusyCycles { get; set; }
    /// <summary>Gets total link energy in picojoules.</summary>
    public double EnergyPJ { get; set; }
}

/// <summary>Provides deterministic flit-level link serialization helpers.</summary>
public static class FlitLinkSerializer
{
    /// <summary>Serializes flits through one link with a strict per-cycle bit budget and base latency.</summary>
    public static FlitLinkSimulationResult Serialize(
        IReadOnlyList<Flit> flits,
        int bandwidthBitsPerCycle,
        int baseLatencyCycles,
        double energyPerBitPJ)
    {
        if (bandwidthBitsPerCycle <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bandwidthBitsPerCycle), bandwidthBitsPerCycle, "Bandwidth must be positive.");
        }
        if (baseLatencyCycles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseLatencyCycles), baseLatencyCycles, "Base latency cannot be negative.");
        }

        var result = new FlitLinkSimulationResult();
        var busyCycles = new HashSet<long>();
        var cycle = 0L;
        var remainingBudget = bandwidthBitsPerCycle;
        foreach (var flit in flits.OrderBy(flit => flit.PacketId, StringComparer.Ordinal).ThenBy(flit => flit.FlitIndex))
        {
            var remainingBits = flit.PayloadBits;
            long? issueCycle = null;
            var segments = new List<FlitSerializationSegment>();
            while (remainingBits > 0)
            {
                if (remainingBudget == 0)
                {
                    cycle++;
                    remainingBudget = bandwidthBitsPerCycle;
                }

                var sent = Math.Min(remainingBudget, remainingBits);
                issueCycle ??= cycle;
                segments.Add(new FlitSerializationSegment(flit.Id, cycle, sent));
                busyCycles.Add(cycle);
                result.TotalBitsTransferred += sent;
                remainingBudget -= sent;
                remainingBits -= sent;
            }

            var doneCycle = segments[^1].Cycle;
            result.Trace.Add(new FlitLinkTraceEntry(
                flit.Id,
                flit.PacketId,
                flit.FlitIndex,
                flit.PayloadBits,
                issueCycle!.Value,
                doneCycle,
                doneCycle + baseLatencyCycles,
                segments));
        }

        result.BusyCycles = busyCycles.Count;
        result.EnergyPJ = result.TotalBitsTransferred * energyPerBitPJ;
        return result;
    }
}
