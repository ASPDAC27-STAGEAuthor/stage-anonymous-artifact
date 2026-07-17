namespace HardwareSim.Core;

/// <summary>Defines deterministic memory runtime settings for Phase 3C.</summary>
public sealed class MemoryRuntimeConfig
{
    /// <summary>Gets or sets memory port count.</summary>
    public int MemoryPorts { get; set; } = ComponentDefaults.MemoryPorts;
    /// <summary>Gets or sets memory bank count.</summary>
    public int MemoryBanks { get; set; } = ComponentDefaults.MemoryBanks;
    /// <summary>Gets or sets per-bank port count.</summary>
    public int BankPorts { get; set; } = ComponentDefaults.MemoryBankPorts;
    /// <summary>Gets or sets line size in bits for bank selection.</summary>
    public int LineSizeBits { get; set; } = ComponentDefaults.MemoryLineSizeBits;
    /// <summary>Gets or sets read bandwidth in bits per cycle.</summary>
    public int ReadBandwidthBitsPerCycle { get; set; } = ComponentDefaults.MemoryReadBandwidthBitsPerCycle;
    /// <summary>Gets or sets write bandwidth in bits per cycle.</summary>
    public int WriteBandwidthBitsPerCycle { get; set; } = ComponentDefaults.MemoryWriteBandwidthBitsPerCycle;
    /// <summary>Gets or sets read latency after data service completes.</summary>
    public int ReadLatencyCycles { get; set; } = ComponentDefaults.MemoryReadLatency;
    /// <summary>Gets or sets write latency after data service completes.</summary>
    public int WriteLatencyCycles { get; set; } = ComponentDefaults.MemoryWriteLatency;
}

/// <summary>Represents one exact memory service grant.</summary>
/// <param name="RequestId">Memory request id.</param>
/// <param name="Operation">Memory operation type.</param>
/// <param name="Bank">Selected memory bank.</param>
/// <param name="Cycle">Grant cycle.</param>
/// <param name="Bits">Serviced bits.</param>
public sealed record MemoryGrant(string RequestId, MemoryOperationType Operation, int Bank, long Cycle, int Bits);

/// <summary>Represents one memory completion or deterministic request error.</summary>
/// <param name="RequestId">Memory request id.</param>
/// <param name="Operation">Memory operation type.</param>
/// <param name="Cycle">Completion cycle.</param>
/// <param name="IsError">Whether the completion is an error.</param>
/// <param name="Code">Stable completion or error code.</param>
/// <param name="Message">Completion message.</param>
public sealed record MemoryCompletion(string RequestId, MemoryOperationType Operation, long Cycle, bool IsError, string Code, string Message);

/// <summary>Represents one exact memory cycle record.</summary>
public sealed class MemoryCycleRecord
{
    /// <summary>Gets or sets the cycle index.</summary>
    public long Cycle { get; set; }
    /// <summary>Gets grants issued in this cycle.</summary>
    public List<MemoryGrant> Grants { get; } = [];
    /// <summary>Gets completions observed in this cycle.</summary>
    public List<MemoryCompletion> Completions { get; } = [];
    /// <summary>Gets read bits serviced in this cycle.</summary>
    public int ReadBits { get; set; }
    /// <summary>Gets write bits serviced in this cycle.</summary>
    public int WriteBits { get; set; }
}

/// <summary>Provides deterministic read/write memory request service against a StorageMap.</summary>
public sealed class MemoryRuntime
{
    private readonly MemoryRuntimeConfig config;
    private readonly StorageMap storageMap;
    private readonly Queue<MemoryRequestState> readQueue = new();
    private readonly Queue<MemoryRequestState> writeQueue = new();
    private readonly List<MemoryRequestState> pendingCompletion = [];

    /// <summary>Initializes a memory runtime.</summary>
    public MemoryRuntime(StorageMap storageMap, MemoryRuntimeConfig? config = null)
    {
        this.storageMap = storageMap ?? throw new ArgumentNullException(nameof(storageMap));
        this.config = config ?? new MemoryRuntimeConfig();
        if (this.config.MemoryPorts <= 0 || this.config.MemoryBanks <= 0 || this.config.BankPorts <= 0 || this.config.LineSizeBits <= 0 || this.config.ReadBandwidthBitsPerCycle <= 0 || this.config.WriteBandwidthBitsPerCycle <= 0 || this.config.ReadLatencyCycles < 0 || this.config.WriteLatencyCycles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Memory ports, banks, line size, bandwidth, and latency settings must be valid.");
        }
    }

    /// <summary>Gets exact cycle records emitted by this runtime.</summary>
    public List<MemoryCycleRecord> CycleRecords { get; } = [];
    /// <summary>Gets all completions in deterministic order.</summary>
    public List<MemoryCompletion> Completions { get; } = [];
    /// <summary>Gets whether all queues and pending completions are drained.</summary>
    public bool IsDrained => readQueue.Count == 0 && writeQueue.Count == 0 && pendingCompletion.Count == 0;

    /// <summary>Enqueues an explicit memory request.</summary>
    public void Enqueue(MemoryRequest request)
    {
        if (request.SizeBits <= 0 || request.Operation is MemoryOperationType.None)
        {
            throw new ArgumentException("MemoryRequest must declare a positive size and read/write operation.", nameof(request));
        }
        if (!string.IsNullOrWhiteSpace(request.StorageId) && !string.Equals(request.StorageId, storageMap.StorageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"MemoryRequest storage id '{request.StorageId}' does not match runtime storage '{storageMap.StorageId}'.", nameof(request));
        }
        var state = new MemoryRequestState(CloneRequest(request));
        if (request.Operation == MemoryOperationType.Read)
        {
            readQueue.Enqueue(state);
        }
        else
        {
            writeQueue.Enqueue(state);
        }
    }

    /// <summary>Advances memory service and completions for one cycle.</summary>
    public MemoryCycleRecord Tick(long cycle)
    {
        var record = new MemoryCycleRecord { Cycle = cycle };
        CompleteReady(cycle, record);
        var portsRemaining = config.MemoryPorts;
        var bankUse = new Dictionary<int, int>();
        var readBudget = config.ReadBandwidthBitsPerCycle;
        var writeBudget = config.WriteBandwidthBitsPerCycle;

        ServiceQueue(readQueue, MemoryOperationType.Read, cycle, record, ref portsRemaining, bankUse, ref readBudget);
        ServiceQueue(writeQueue, MemoryOperationType.Write, cycle, record, ref portsRemaining, bankUse, ref writeBudget);

        CycleRecords.Add(record);
        return record;
    }

    /// <summary>Computes the deterministic bank index from a bit address.</summary>
    public int BankFor(long bitAddress) => (int)((bitAddress / config.LineSizeBits) % config.MemoryBanks);

    private void ServiceQueue(
        Queue<MemoryRequestState> queue,
        MemoryOperationType operation,
        long cycle,
        MemoryCycleRecord record,
        ref int portsRemaining,
        Dictionary<int, int> bankUse,
        ref int budget)
    {
        var deferred = new List<MemoryRequestState>();
        while (queue.Count > 0 && portsRemaining > 0 && budget > 0)
        {
            var state = queue.Dequeue();
            var bank = BankFor(state.Request.BitAddress);
            if (bankUse.GetValueOrDefault(bank) >= config.BankPorts)
            {
                deferred.Add(state);
                continue;
            }

            var bits = Math.Min(budget, (int)Math.Min(int.MaxValue, state.RemainingBits));
            state.RemainingBits -= bits;
            budget -= bits;
            portsRemaining--;
            bankUse[bank] = bankUse.GetValueOrDefault(bank) + 1;
            record.Grants.Add(new MemoryGrant(state.Request.RequestId, operation, bank, cycle, bits));
            if (operation == MemoryOperationType.Read)
            {
                record.ReadBits += bits;
            }
            else
            {
                record.WriteBits += bits;
            }

            if (state.RemainingBits == 0)
            {
                state.ReadyCycle = cycle + (operation == MemoryOperationType.Read ? config.ReadLatencyCycles : config.WriteLatencyCycles) + 1;
                pendingCompletion.Add(state);
            }
            else
            {
                deferred.Add(state);
            }
        }

        while (queue.Count > 0)
        {
            deferred.Add(queue.Dequeue());
        }
        foreach (var item in deferred.OrderBy(item => item.Sequence))
        {
            queue.Enqueue(item);
        }
    }

    private void CompleteReady(long cycle, MemoryCycleRecord record)
    {
        foreach (var state in pendingCompletion.Where(item => item.ReadyCycle <= cycle).OrderBy(item => item.Sequence).ToList())
        {
            pendingCompletion.Remove(state);
            var result = state.Request.Operation == MemoryOperationType.Write
                ? storageMap.Write(state.Request.BitAddress, state.Request.SizeBits, state.Request.RequestId)
                : storageMap.Read(state.Request.BitAddress, state.Request.SizeBits);
            var completion = result.IsSuccess
                ? new MemoryCompletion(state.Request.RequestId, state.Request.Operation, cycle, false, "Success", "Memory request completed.")
                : new MemoryCompletion(state.Request.RequestId, state.Request.Operation, cycle, true, result.Code, result.Message);
            record.Completions.Add(completion);
            Completions.Add(completion);
        }
    }

    private static MemoryRequest CloneRequest(MemoryRequest request) => new()
    {
        RequestId = request.RequestId,
        Operation = request.Operation,
        StorageId = request.StorageId,
        BitAddress = request.BitAddress,
        SizeBits = request.SizeBits,
        PacketId = request.PacketId,
        TileId = request.TileId,
        ResponseDestinationComponentId = request.ResponseDestinationComponentId,
        Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
    };

    private sealed class MemoryRequestState
    {
        private static long nextSequence;

        public MemoryRequestState(MemoryRequest request)
        {
            Request = request;
            RemainingBits = request.SizeBits;
            Sequence = nextSequence++;
        }

        public MemoryRequest Request { get; }
        public long RemainingBits { get; set; }
        public long ReadyCycle { get; set; }
        public long Sequence { get; }
    }
}
