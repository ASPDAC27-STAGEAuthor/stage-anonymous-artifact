namespace HardwareSim.Core;

/// <summary>Defines deterministic buffer runtime settings for Phase 3C.</summary>
public sealed class BufferRuntimeConfig
{
    /// <summary>Gets or sets total buffer capacity in bits.</summary>
    public long CapacityBits { get; set; } = ComponentDefaults.BufferCapacityBits;
    /// <summary>Gets or sets write service bandwidth in bits per cycle.</summary>
    public int WriteBandwidthBitsPerCycle { get; set; } = ComponentDefaults.BufferWriteBandwidthBitsPerCycle;
    /// <summary>Gets or sets read service bandwidth in bits per cycle.</summary>
    public int ReadBandwidthBitsPerCycle { get; set; } = ComponentDefaults.BufferReadBandwidthBitsPerCycle;
    /// <summary>Gets or sets write latency after write service completes.</summary>
    public int WriteLatencyCycles { get; set; } = ComponentDefaults.BufferWriteLatencyCycles;
    /// <summary>Gets or sets read latency after read service completes.</summary>
    public int ReadLatencyCycles { get; set; } = ComponentDefaults.BufferReadLatencyCycles;
}

/// <summary>Represents one exact buffer cycle record.</summary>
/// <param name="Cycle">Cycle index.</param>
/// <param name="OccupancyBits">Resident and reserved occupancy in bits.</param>
/// <param name="WriteBits">Bits serviced by write path this cycle.</param>
/// <param name="ReadBits">Bits serviced by read path this cycle.</param>
/// <param name="PeakOccupancyBits">Peak occupancy observed through this cycle.</param>
/// <param name="OutputStalled">Whether a completed output flit was held by downstream backpressure.</param>
public sealed record BufferCycleRecord(long Cycle, long OccupancyBits, int WriteBits, int ReadBits, long PeakOccupancyBits, bool OutputStalled);

/// <summary>Represents one buffer accept result.</summary>
public sealed class BufferAcceptResult
{
    private BufferAcceptResult(bool accepted, string code, string message, string flitId)
    {
        Accepted = accepted;
        Code = code;
        Message = message;
        FlitId = flitId;
    }

    /// <summary>Gets whether the flit was accepted.</summary>
    public bool Accepted { get; }
    /// <summary>Gets the stable result or error code.</summary>
    public string Code { get; }
    /// <summary>Gets a human-readable message.</summary>
    public string Message { get; }
    /// <summary>Gets the flit id.</summary>
    public string FlitId { get; }

    /// <summary>Creates an accepted result.</summary>
    public static BufferAcceptResult Success(string flitId) => new(true, "Accepted", "Flit accepted into buffer.", flitId);
    /// <summary>Creates a rejected result.</summary>
    public static BufferAcceptResult Failure(string code, string message, string flitId) => new(false, code, message, flitId);
}

/// <summary>Provides a deterministic bit-capacity buffer runtime.</summary>
public sealed class BufferRuntime
{
    private readonly BufferRuntimeConfig config;
    private readonly Queue<BufferItem> writeQueue = new();
    private readonly List<BufferItem> writeLatency = [];
    private readonly Queue<BufferItem> readyQueue = new();
    private readonly List<BufferItem> readLatency = [];
    private BufferItem? readItem;
    private long occupancyBits;
    private long peakOccupancyBits;

    /// <summary>Initializes a buffer runtime from config.</summary>
    public BufferRuntime(BufferRuntimeConfig? config = null)
    {
        this.config = config ?? new BufferRuntimeConfig();
        if (this.config.CapacityBits <= 0 || this.config.WriteBandwidthBitsPerCycle <= 0 || this.config.ReadBandwidthBitsPerCycle <= 0 || this.config.WriteLatencyCycles < 0 || this.config.ReadLatencyCycles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Buffer capacity, bandwidth, and latencies must be valid.");
        }
    }

    /// <summary>Gets current reserved/resident occupancy in bits.</summary>
    public long OccupancyBits => occupancyBits;
    /// <summary>Gets peak occupancy in bits.</summary>
    public long PeakOccupancyBits => peakOccupancyBits;
    /// <summary>Gets emitted exact cycle records.</summary>
    public List<BufferCycleRecord> CycleRecords { get; } = [];

    /// <summary>Attempts to accept a flit, reserving its payload bits before write service.</summary>
    public BufferAcceptResult TryAccept(Flit flit)
    {
        if (flit.PayloadBits > config.CapacityBits)
        {
            return BufferAcceptResult.Failure("BufferCapacityOverflow", "Single flit exceeds buffer capacity.", flit.Id);
        }
        if (occupancyBits + flit.PayloadBits > config.CapacityBits)
        {
            return BufferAcceptResult.Failure("OutputBufferFull", "Buffer is temporarily full; upstream must hold the flit.", flit.Id);
        }

        occupancyBits += flit.PayloadBits;
        peakOccupancyBits = Math.Max(peakOccupancyBits, occupancyBits);
        writeQueue.Enqueue(new BufferItem(CloneFlit(flit), flit.PayloadBits));
        return BufferAcceptResult.Success(flit.Id);
    }

    /// <summary>Advances write/read service and optionally delivers one completed flit to a slow sink.</summary>
    public BufferCycleRecord Tick(long cycle, SlowSinkRuntime? sink = null)
    {
        MoveReadyWrites(cycle);
        var outputStalled = TryDeliverReadReady(cycle, sink);
        var writeBits = ServiceWrite(cycle);
        var readBits = ServiceRead(cycle);
        var record = new BufferCycleRecord(cycle, occupancyBits, writeBits, readBits, peakOccupancyBits, outputStalled);
        CycleRecords.Add(record);
        return record;
    }

    private void MoveReadyWrites(long cycle)
    {
        foreach (var item in writeLatency.Where(item => item.ReadyCycle <= cycle).OrderBy(item => item.Sequence).ToList())
        {
            writeLatency.Remove(item);
            readyQueue.Enqueue(item with { RemainingBits = item.Flit.PayloadBits });
        }
    }

    private bool TryDeliverReadReady(long cycle, SlowSinkRuntime? sink)
    {
        var stalled = false;
        foreach (var item in readLatency.Where(item => item.ReadyCycle <= cycle).OrderBy(item => item.Sequence).ToList())
        {
            if (sink is not null && !sink.TryAccept(item.Flit, cycle))
            {
                stalled = true;
                continue;
            }
            readLatency.Remove(item);
            occupancyBits -= item.Flit.PayloadBits;
        }
        return stalled;
    }

    private int ServiceWrite(long cycle)
    {
        var budget = config.WriteBandwidthBitsPerCycle;
        var serviced = 0;
        while (budget > 0 && writeQueue.Count > 0)
        {
            var item = writeQueue.Peek();
            var bits = Math.Min(budget, item.RemainingBits);
            item.RemainingBits -= bits;
            budget -= bits;
            serviced += bits;
            if (item.RemainingBits == 0)
            {
                writeQueue.Dequeue();
                item.ReadyCycle = cycle + config.WriteLatencyCycles + 1;
                writeLatency.Add(item);
            }
        }
        return serviced;
    }

    private int ServiceRead(long cycle)
    {
        readItem ??= readyQueue.Count > 0 ? readyQueue.Dequeue() : null;
        if (readItem is null)
        {
            return 0;
        }

        var bits = Math.Min(config.ReadBandwidthBitsPerCycle, readItem.RemainingBits);
        readItem.RemainingBits -= bits;
        if (readItem.RemainingBits == 0)
        {
            readItem.ReadyCycle = cycle + config.ReadLatencyCycles + 1;
            readLatency.Add(readItem);
            readItem = null;
        }
        return bits;
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

    private sealed record BufferItem(Flit Flit, long Sequence)
    {
        public int RemainingBits { get; set; } = Flit.PayloadBits;
        public long ReadyCycle { get; set; }
    }
}

/// <summary>Provides a deterministic slow sink with capacity, accept bandwidth, and consume latency.</summary>
public sealed class SlowSinkRuntime
{
    private readonly long inputCapacityBits;
    private readonly int acceptBandwidthBitsPerCycle;
    private readonly int consumeLatencyCycles;
    private readonly List<(long ReadyCycle, int Bits)> pending = [];
    private long occupancyBits;
    private long currentCycle = -1;
    private int acceptedThisCycle;

    /// <summary>Initializes a slow sink runtime.</summary>
    public SlowSinkRuntime(long inputCapacityBits, int acceptBandwidthBitsPerCycle, int consumeLatencyCycles)
    {
        if (inputCapacityBits <= 0 || acceptBandwidthBitsPerCycle <= 0 || consumeLatencyCycles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputCapacityBits), "Slow sink capacity, bandwidth, and latency must be valid.");
        }
        this.inputCapacityBits = inputCapacityBits;
        this.acceptBandwidthBitsPerCycle = acceptBandwidthBitsPerCycle;
        this.consumeLatencyCycles = consumeLatencyCycles;
    }

    /// <summary>Gets current sink occupancy in bits.</summary>
    public long OccupancyBits => occupancyBits;
    /// <summary>Gets accepted flit ids in deterministic order.</summary>
    public List<string> AcceptedFlitIds { get; } = [];

    /// <summary>Attempts to accept a flit at a cycle.</summary>
    public bool TryAccept(Flit flit, long cycle)
    {
        Advance(cycle);
        if (acceptedThisCycle + flit.PayloadBits > acceptBandwidthBitsPerCycle || occupancyBits + flit.PayloadBits > inputCapacityBits)
        {
            return false;
        }
        acceptedThisCycle += flit.PayloadBits;
        occupancyBits += flit.PayloadBits;
        pending.Add((cycle + consumeLatencyCycles + 1, flit.PayloadBits));
        AcceptedFlitIds.Add(flit.Id);
        return true;
    }

    /// <summary>Advances sink consumption to a cycle.</summary>
    public void Advance(long cycle)
    {
        if (cycle != currentCycle)
        {
            currentCycle = cycle;
            acceptedThisCycle = 0;
        }
        foreach (var item in pending.Where(item => item.ReadyCycle <= cycle).ToList())
        {
            pending.Remove(item);
            occupancyBits -= item.Bits;
        }
    }
}
