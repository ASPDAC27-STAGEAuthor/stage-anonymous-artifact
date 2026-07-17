namespace HardwareSim.Core;

/// <summary>Represents one active storage allocation in a storage map snapshot.</summary>
public sealed class StorageAllocationSnapshot
{
    /// <summary>Gets or sets the tile identifier occupying the allocation.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the base bit address, inclusive.</summary>
    public long BaseAddressBits { get; set; }
    /// <summary>Gets or sets the allocation size in bits.</summary>
    public long SizeBits { get; set; }
    /// <summary>Gets the end bit address, exclusive.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long EndAddressBits => BaseAddressBits + SizeBits;
}

/// <summary>Represents one written bit range in a storage map snapshot.</summary>
public sealed class StorageWrittenRangeSnapshot
{
    /// <summary>Gets or sets the tile identifier whose allocation contains the range.</summary>
    public string TileId { get; set; } = "";
    /// <summary>Gets or sets the base bit address, inclusive.</summary>
    public long BaseAddressBits { get; set; }
    /// <summary>Gets or sets the written range size in bits.</summary>
    public long SizeBits { get; set; }
    /// <summary>Gets or sets deterministic provenance for the write.</summary>
    public string Provenance { get; set; } = "";
    /// <summary>Gets the end bit address, exclusive.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long EndAddressBits => BaseAddressBits + SizeBits;
}

/// <summary>Represents a serializable bit-addressed storage map snapshot.</summary>
public sealed class StorageMapSnapshot
{
    /// <summary>Gets or sets the storage component identifier.</summary>
    public string StorageId { get; set; } = "";
    /// <summary>Gets or sets the storage capacity in bits.</summary>
    public long CapacityBits { get; set; }
    /// <summary>Gets or sets active allocations sorted by base address.</summary>
    public List<StorageAllocationSnapshot> Allocations { get; set; } = [];
    /// <summary>Gets or sets written ranges sorted by base address.</summary>
    public List<StorageWrittenRangeSnapshot> WrittenRanges { get; set; } = [];
}

/// <summary>Represents a structured result from a storage map operation.</summary>
public sealed class StorageMapResult
{
    private StorageMapResult(
        bool isSuccess,
        string code,
        string message,
        string storageId,
        string tileId,
        long? addressBits,
        long sizeBits,
        bool isLogicalZero,
        long writtenBits)
    {
        IsSuccess = isSuccess;
        Code = code;
        Message = message;
        StorageId = storageId;
        TileId = tileId;
        AddressBits = addressBits;
        SizeBits = sizeBits;
        IsLogicalZero = isLogicalZero;
        WrittenBits = writtenBits;
    }

    /// <summary>Gets whether the operation succeeded.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the stable result or error code.</summary>
    public string Code { get; }
    /// <summary>Gets the human-readable result message.</summary>
    public string Message { get; }
    /// <summary>Gets the storage component id.</summary>
    public string StorageId { get; }
    /// <summary>Gets the tile id associated with the operation.</summary>
    public string TileId { get; }
    /// <summary>Gets the operation address in bits, when applicable.</summary>
    public long? AddressBits { get; }
    /// <summary>Gets the operation size in bits.</summary>
    public long SizeBits { get; }
    /// <summary>Gets whether a successful read returned logical zero from an allocated but unwritten range.</summary>
    public bool IsLogicalZero { get; }
    /// <summary>Gets the number of bits in the read range that have written provenance.</summary>
    public long WrittenBits { get; }

    /// <summary>Creates a successful storage map result.</summary>
    public static StorageMapResult Success(string storageId, string tileId, long? addressBits, long sizeBits, bool isLogicalZero = false, long writtenBits = 0) =>
        new(true, "Success", "Storage map operation succeeded.", storageId, tileId, addressBits, sizeBits, isLogicalZero, writtenBits);

    /// <summary>Creates a failed storage map result with a stable error code.</summary>
    public static StorageMapResult Failure(string code, string message, string storageId, string tileId = "", long? addressBits = null, long sizeBits = 0) =>
        new(false, code, message, storageId, tileId, addressBits, sizeBits, false, 0);
}

/// <summary>Provides deterministic bit-address allocation, read, write, and free operations for one storage component.</summary>
public sealed class StorageMap
{
    private readonly List<StorageAllocationSnapshot> allocations = [];
    private readonly List<StorageWrittenRangeSnapshot> writtenRanges = [];

    /// <summary>Initializes a storage map for one storage component.</summary>
    public StorageMap(string storageId, long capacityBits)
    {
        if (string.IsNullOrWhiteSpace(storageId))
        {
            throw new ArgumentException("Storage id is required.", nameof(storageId));
        }
        if (capacityBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityBits), capacityBits, "Storage capacity must be positive.");
        }

        StorageId = storageId;
        CapacityBits = capacityBits;
    }

    /// <summary>Gets the storage component identifier.</summary>
    public string StorageId { get; }
    /// <summary>Gets the total capacity in bits.</summary>
    public long CapacityBits { get; }
    /// <summary>Gets active allocations in deterministic address order.</summary>
    public IReadOnlyList<StorageAllocationSnapshot> Allocations => allocations.Select(CloneAllocation).ToList();
    /// <summary>Gets written ranges in deterministic address order.</summary>
    public IReadOnlyList<StorageWrittenRangeSnapshot> WrittenRanges => writtenRanges.Select(CloneWrittenRange).ToList();

    /// <summary>Allocates a bit range for a tile using stable lowest-address first-fit.</summary>
    public StorageMapResult Allocate(string tileId, long sizeBits, long alignmentBits = 1, long? preferredAddressBits = null)
    {
        if (string.IsNullOrWhiteSpace(tileId))
        {
            return StorageMapResult.Failure("StorageCapacityError", "Tile id is required for allocation.", StorageId, tileId, preferredAddressBits, sizeBits);
        }
        if (sizeBits <= 0 || alignmentBits <= 0 || sizeBits > CapacityBits)
        {
            return StorageMapResult.Failure("StorageCapacityError", "Allocation size and alignment must be positive and fit within storage capacity.", StorageId, tileId, preferredAddressBits, sizeBits);
        }
        if (allocations.Any(allocation => string.Equals(allocation.TileId, tileId, StringComparison.OrdinalIgnoreCase)))
        {
            return StorageMapResult.Failure("StorageOverlapError", $"Tile '{tileId}' already has an allocation.", StorageId, tileId, preferredAddressBits, sizeBits);
        }

        var address = preferredAddressBits.HasValue
            ? ValidatePreferredAddress(tileId, sizeBits, alignmentBits, preferredAddressBits.Value)
            : FindFirstFit(sizeBits, alignmentBits);
        if (!address.IsSuccess)
        {
            return address;
        }

        allocations.Add(new StorageAllocationSnapshot { TileId = tileId, BaseAddressBits = address.AddressBits!.Value, SizeBits = sizeBits });
        SortState();
        return StorageMapResult.Success(StorageId, tileId, address.AddressBits, sizeBits);
    }

    /// <summary>Records a write into one existing allocation.</summary>
    public StorageMapResult Write(long addressBits, long sizeBits, string provenance = "")
    {
        var allocation = FindContainingAllocation(addressBits, sizeBits);
        if (allocation is null)
        {
            return StorageMapResult.Failure("MemoryAddressError", "Write range must be fully contained in one active allocation.", StorageId, addressBits: addressBits, sizeBits: sizeBits);
        }

        writtenRanges.Add(new StorageWrittenRangeSnapshot
        {
            TileId = allocation.TileId,
            BaseAddressBits = addressBits,
            SizeBits = sizeBits,
            Provenance = provenance ?? ""
        });
        MergeWrittenRanges();
        SortState();
        return StorageMapResult.Success(StorageId, allocation.TileId, addressBits, sizeBits, writtenBits: sizeBits);
    }

    /// <summary>Reads from one existing allocation; unwritten allocated ranges read as logical zero.</summary>
    public StorageMapResult Read(long addressBits, long sizeBits)
    {
        var allocation = FindContainingAllocation(addressBits, sizeBits);
        if (allocation is null)
        {
            return StorageMapResult.Failure("MemoryAddressError", "Read range must be fully contained in one active allocation.", StorageId, addressBits: addressBits, sizeBits: sizeBits);
        }

        var writtenBits = CountCoveredBits(addressBits, sizeBits);
        return StorageMapResult.Success(StorageId, allocation.TileId, addressBits, sizeBits, isLogicalZero: writtenBits == 0, writtenBits: writtenBits);
    }

    /// <summary>Frees the allocation associated with a tile id.</summary>
    public StorageMapResult Free(string tileId)
    {
        var allocation = allocations.FirstOrDefault(item => string.Equals(item.TileId, tileId, StringComparison.OrdinalIgnoreCase));
        if (allocation is null)
        {
            return StorageMapResult.Failure("MemoryAddressError", $"Tile '{tileId}' is not allocated.", StorageId, tileId);
        }

        allocations.Remove(allocation);
        writtenRanges.RemoveAll(range => string.Equals(range.TileId, allocation.TileId, StringComparison.OrdinalIgnoreCase));
        SortState();
        return StorageMapResult.Success(StorageId, allocation.TileId, allocation.BaseAddressBits, allocation.SizeBits);
    }

    /// <summary>Frees the allocation containing an address.</summary>
    public StorageMapResult FreeAt(long addressBits)
    {
        var allocation = allocations.FirstOrDefault(item => addressBits >= item.BaseAddressBits && addressBits < item.EndAddressBits);
        return allocation is null
            ? StorageMapResult.Failure("MemoryAddressError", "Address is not inside an active allocation.", StorageId, addressBits: addressBits)
            : Free(allocation.TileId);
    }

    /// <summary>Creates a deep clone of this storage map.</summary>
    public StorageMap Clone() => FromSnapshot(ToSnapshot());

    /// <summary>Creates a serializable snapshot of this storage map.</summary>
    public StorageMapSnapshot ToSnapshot() => new()
    {
        StorageId = StorageId,
        CapacityBits = CapacityBits,
        Allocations = allocations.Select(CloneAllocation).ToList(),
        WrittenRanges = writtenRanges.Select(CloneWrittenRange).ToList()
    };

    /// <summary>Restores a storage map from a serializable snapshot.</summary>
    public static StorageMap FromSnapshot(StorageMapSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var map = new StorageMap(snapshot.StorageId, snapshot.CapacityBits);
        foreach (var allocation in snapshot.Allocations.OrderBy(item => item.BaseAddressBits).ThenBy(item => item.TileId, StringComparer.Ordinal))
        {
            map.allocations.Add(CloneAllocation(allocation));
        }
        foreach (var range in snapshot.WrittenRanges.OrderBy(item => item.BaseAddressBits).ThenBy(item => item.TileId, StringComparer.Ordinal).ThenBy(item => item.Provenance, StringComparer.Ordinal))
        {
            map.writtenRanges.Add(CloneWrittenRange(range));
        }
        map.SortState();
        return map;
    }

    private StorageMapResult ValidatePreferredAddress(string tileId, long sizeBits, long alignmentBits, long addressBits)
    {
        if (addressBits < 0 || addressBits % alignmentBits != 0 || addressBits + sizeBits > CapacityBits)
        {
            return StorageMapResult.Failure("StorageCapacityError", "Preferred address must be nonnegative, aligned, and within capacity.", StorageId, tileId, addressBits, sizeBits);
        }
        if (Overlaps(addressBits, sizeBits))
        {
            return StorageMapResult.Failure("StorageOverlapError", "Preferred address overlaps an existing allocation.", StorageId, tileId, addressBits, sizeBits);
        }
        return StorageMapResult.Success(StorageId, tileId, addressBits, sizeBits);
    }

    private StorageMapResult FindFirstFit(long sizeBits, long alignmentBits)
    {
        var candidate = 0L;
        foreach (var allocation in allocations.OrderBy(item => item.BaseAddressBits).ThenBy(item => item.TileId, StringComparer.Ordinal))
        {
            candidate = AlignUp(candidate, alignmentBits);
            if (candidate + sizeBits <= allocation.BaseAddressBits)
            {
                return StorageMapResult.Success(StorageId, "", candidate, sizeBits);
            }
            candidate = Math.Max(candidate, allocation.EndAddressBits);
        }

        candidate = AlignUp(candidate, alignmentBits);
        return candidate + sizeBits <= CapacityBits
            ? StorageMapResult.Success(StorageId, "", candidate, sizeBits)
            : StorageMapResult.Failure("StorageCapacityError", "No contiguous aligned range fits within storage capacity.", StorageId, addressBits: candidate, sizeBits: sizeBits);
    }

    private bool Overlaps(long addressBits, long sizeBits) => allocations.Any(allocation => RangesOverlap(addressBits, addressBits + sizeBits, allocation.BaseAddressBits, allocation.EndAddressBits));

    private StorageAllocationSnapshot? FindContainingAllocation(long addressBits, long sizeBits)
    {
        if (addressBits < 0 || sizeBits <= 0 || addressBits + sizeBits > CapacityBits)
        {
            return null;
        }
        return allocations.FirstOrDefault(allocation => addressBits >= allocation.BaseAddressBits && addressBits + sizeBits <= allocation.EndAddressBits);
    }

    private long CountCoveredBits(long addressBits, long sizeBits)
    {
        var start = addressBits;
        var end = addressBits + sizeBits;
        var covered = 0L;
        foreach (var range in writtenRanges.OrderBy(item => item.BaseAddressBits).ThenBy(item => item.TileId, StringComparer.Ordinal))
        {
            var overlapStart = Math.Max(start, range.BaseAddressBits);
            var overlapEnd = Math.Min(end, range.EndAddressBits);
            if (overlapEnd > overlapStart)
            {
                covered += overlapEnd - overlapStart;
            }
        }
        return Math.Min(sizeBits, covered);
    }

    private void MergeWrittenRanges()
    {
        if (writtenRanges.Count <= 1)
        {
            return;
        }

        var merged = new List<StorageWrittenRangeSnapshot>();
        foreach (var range in writtenRanges.OrderBy(item => item.TileId, StringComparer.Ordinal).ThenBy(item => item.BaseAddressBits).ThenBy(item => item.Provenance, StringComparer.Ordinal))
        {
            var last = merged.LastOrDefault();
            if (last is not null && string.Equals(last.TileId, range.TileId, StringComparison.OrdinalIgnoreCase) && string.Equals(last.Provenance, range.Provenance, StringComparison.Ordinal) && range.BaseAddressBits <= last.EndAddressBits)
            {
                last.SizeBits = Math.Max(last.EndAddressBits, range.EndAddressBits) - last.BaseAddressBits;
                continue;
            }
            merged.Add(CloneWrittenRange(range));
        }
        writtenRanges.Clear();
        writtenRanges.AddRange(merged);
    }

    private void SortState()
    {
        allocations.Sort((left, right) => left.BaseAddressBits != right.BaseAddressBits
            ? left.BaseAddressBits.CompareTo(right.BaseAddressBits)
            : string.Compare(left.TileId, right.TileId, StringComparison.Ordinal));
        writtenRanges.Sort((left, right) => left.BaseAddressBits != right.BaseAddressBits
            ? left.BaseAddressBits.CompareTo(right.BaseAddressBits)
            : string.Compare(left.TileId, right.TileId, StringComparison.Ordinal));
    }

    private static bool RangesOverlap(long leftStart, long leftEnd, long rightStart, long rightEnd) => leftStart < rightEnd && rightStart < leftEnd;

    private static long AlignUp(long value, long alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static StorageAllocationSnapshot CloneAllocation(StorageAllocationSnapshot allocation) => new()
    {
        TileId = allocation.TileId,
        BaseAddressBits = allocation.BaseAddressBits,
        SizeBits = allocation.SizeBits
    };

    private static StorageWrittenRangeSnapshot CloneWrittenRange(StorageWrittenRangeSnapshot range) => new()
    {
        TileId = range.TileId,
        BaseAddressBits = range.BaseAddressBits,
        SizeBits = range.SizeBits,
        Provenance = range.Provenance
    };
}
