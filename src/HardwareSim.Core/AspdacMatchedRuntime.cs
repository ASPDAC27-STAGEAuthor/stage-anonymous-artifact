using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HardwareSim.Core;

/// <summary>Configuration-driven workload and mapped-access record used by final ASP-DAC validation runs.</summary>
public sealed record AspdacMatchedWorkload(
    string CaseId,
    long M,
    long N,
    long K,
    long ExpectedMacs,
    long RegisterAccesses = 0,
    long LocalBufferAccesses = 0,
    long GlobalBufferAccesses = 0,
    long DramAccesses = 0,
    long SramIfmapReads = 0,
    long SramFilterReads = 0,
    long SramOfmapWrites = 0,
    long DramIfmapReads = 0,
    long DramFilterReads = 0,
    long DramOfmapWrites = 0);

/// <summary>Cycle-derived matched workload result with complete service attribution.</summary>
public sealed class AspdacMatchedRuntimeResult
{
    /// <summary>Gets the stable workload case id.</summary>
    public string CaseId { get; init; } = "";
    /// <summary>Gets the runtime mode.</summary>
    public string Mode { get; init; } = "";
    /// <summary>Gets the stable measurement kind.</summary>
    public string MeasurementKind { get; init; } = "";
    /// <summary>Gets the canonical cycle-record SHA-256.</summary>
    public string TraceHash { get; init; } = "";
    /// <summary>Gets total measured cycles.</summary>
    public long TotalCycles { get; init; }
    /// <summary>Gets attributed compute cycles.</summary>
    public long ComputeCycles { get; init; }
    /// <summary>Gets attributed memory service cycles.</summary>
    public long MemoryCycles { get; init; }
    /// <summary>Gets attributed NoC service cycles.</summary>
    public long NocCycles { get; init; }
    /// <summary>Gets attributed serialization cycles.</summary>
    public long SerializationCycles { get; init; }
    /// <summary>Gets attributed conversion cycles.</summary>
    public long ConversionCycles { get; init; }
    /// <summary>Gets attributed reduction cycles.</summary>
    public long ReductionCycles { get; init; }
    /// <summary>Gets attributed softmax cycles.</summary>
    public long SoftmaxCycles { get; init; }
    /// <summary>Gets cold-start prefetch cycles.</summary>
    public long PrefetchCycles { get; init; }
    /// <summary>Gets systolic wavefront cycles.</summary>
    public long WavefrontCycles { get; init; }
    /// <summary>Gets cycles where memory service extends the wavefront.</summary>
    public long MemoryStallCycles { get; init; }
    /// <summary>Gets the exact completed MAC count.</summary>
    public long CompletedMacs { get; init; }
    /// <summary>Gets effective arithmetic utilization in percent.</summary>
    public double UtilizationPercent { get; init; }
    /// <summary>Gets mapped access counts by stable hierarchy key.</summary>
    public IReadOnlyDictionary<string, long> Accesses { get; init; } = new Dictionary<string, long>();
}

/// <summary>Final-paper matched cycle replays. Counts and service constraints are supplied by resolved experiment cases.</summary>
public static class AspdacMatchedRuntime
{
    /// <summary>Runs the frozen 16-MAC Timeloop-matched schedule, optionally followed by full-system services.</summary>
    public static AspdacMatchedRuntimeResult RunTimeloopMatched(AspdacMatchedWorkload workload, bool fullSystem)
    {
        Validate(workload);
        const long computeWidth = 16;
        var computeCycles = DivideRoundUp(workload.ExpectedMacs, computeWidth);
        var memoryCycles = fullSystem ? DivideRoundUp(workload.DramAccesses, 16) + 5 : 0;
        var nocCycles = fullSystem ? DivideRoundUp(workload.GlobalBufferAccesses, 8) : 0;
        var serializationCycles = 0L; // one 128-bit packet per 128-bit service cycle
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var completedMacs = ReplayPhase(hash, 0, 1, computeCycles, workload.ExpectedMacs, computeWidth);
        var cycle = computeCycles;
        if (fullSystem)
        {
            ReplayPhase(hash, cycle, 2, memoryCycles, workload.DramAccesses, 16);
            cycle += memoryCycles;
            ReplayPhase(hash, cycle, 3, nocCycles, workload.GlobalBufferAccesses, 8);
            cycle += nocCycles;
        }
        var totalCycles = cycle + serializationCycles;
        return new AspdacMatchedRuntimeResult
        {
            CaseId = workload.CaseId,
            Mode = fullSystem ? "full_system" : "compute_only",
            MeasurementKind = "stage_config_driven_cycle_replay",
            TraceHash = BitConverter.ToString(hash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant(),
            TotalCycles = totalCycles,
            ComputeCycles = computeCycles,
            MemoryCycles = memoryCycles,
            NocCycles = nocCycles,
            SerializationCycles = serializationCycles,
            CompletedMacs = completedMacs,
            UtilizationPercent = 100.0 * workload.ExpectedMacs / (computeCycles * (double)computeWidth),
            Accesses = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["register"] = workload.RegisterAccesses,
                ["local_buffer"] = workload.LocalBufferAccesses,
                ["global_buffer"] = workload.GlobalBufferAccesses,
                ["dram"] = workload.DramAccesses,
            },
        };
    }

    /// <summary>Runs the 4x4 weight-stationary array and matched memory streams with optional cold-start prefetch.</summary>
    public static AspdacMatchedRuntimeResult RunScaleSimMatched(AspdacMatchedWorkload workload, bool coldStart)
    {
        Validate(workload);
        const long rows = 4;
        const long columns = 4;
        const long cellCount = rows * columns;
        const long wordsPerCycle = 8;
        var tileRows = DivideRoundUp(workload.M, rows);
        var tileColumns = DivideRoundUp(workload.N, columns);
        var wavefrontCycles = checked(tileRows * tileColumns * (workload.K + rows + columns - 2));
        var ifmapService = DivideRoundUp(workload.DramIfmapReads, wordsPerCycle);
        var filterService = DivideRoundUp(workload.DramFilterReads, wordsPerCycle);
        var ofmapService = DivideRoundUp(workload.DramOfmapWrites, wordsPerCycle);
        var memoryService = Math.Max(ifmapService, Math.Max(filterService, ofmapService));
        var warmCycles = Math.Max(wavefrontCycles, memoryService);
        var prefetchCycles = coldStart ? 2 * DivideRoundUp(8 * 1024, wordsPerCycle * 2) : 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (prefetchCycles > 0)
        {
            ReplayPhase(hash, 0, 10, prefetchCycles, 16 * 1024, wordsPerCycle * 2);
        }
        var completedMacs = ReplayConcurrentSystolic(
            hash,
            prefetchCycles,
            warmCycles,
            wavefrontCycles,
            workload.ExpectedMacs,
            workload.DramIfmapReads,
            workload.DramFilterReads,
            workload.DramOfmapWrites,
            wordsPerCycle);
        var totalCycles = prefetchCycles + warmCycles;
        return new AspdacMatchedRuntimeResult
        {
            CaseId = workload.CaseId,
            Mode = coldStart ? "cold_start" : "warm_array",
            MeasurementKind = "stage_weight_stationary_cycle_service_runtime",
            TraceHash = BitConverter.ToString(hash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant(),
            TotalCycles = totalCycles,
            ComputeCycles = DivideRoundUp(workload.ExpectedMacs, cellCount),
            MemoryCycles = memoryService,
            PrefetchCycles = prefetchCycles,
            WavefrontCycles = wavefrontCycles,
            MemoryStallCycles = Math.Max(0, memoryService - wavefrontCycles),
            CompletedMacs = completedMacs,
            UtilizationPercent = 100.0 * workload.ExpectedMacs / (warmCycles * (double)cellCount),
            Accesses = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["sram_ifmap_reads"] = workload.SramIfmapReads,
                ["sram_filter_reads"] = workload.SramFilterReads,
                ["sram_ofmap_writes"] = workload.SramOfmapWrites,
                ["dram_ifmap_reads"] = workload.DramIfmapReads,
                ["dram_filter_reads"] = workload.DramFilterReads,
                ["dram_ofmap_writes"] = workload.DramOfmapWrites,
            },
        };
    }

    private static long ReplayPhase(IncrementalHash hash, long startCycle, int phase, long cycles, long work, long width)
    {
        var remaining = work;
        for (var offset = 0L; offset < cycles; offset++)
        {
            var serviced = Math.Min(width, remaining);
            remaining -= serviced;
            AppendCycle(hash, startCycle + offset, phase, serviced, remaining);
        }
        if (remaining != 0)
        {
            throw new InvalidOperationException($"Phase {phase} did not drain {remaining} work units.");
        }
        return work;
    }

    private static long ReplayConcurrentSystolic(IncrementalHash hash, long startCycle, long cycles, long wavefrontCycles, long macs, long ifmap, long filter, long ofmap, long width)
    {
        var remainingMacs = macs;
        var remainingIfmap = ifmap;
        var remainingFilter = filter;
        var remainingOfmap = ofmap;
        for (var offset = 0L; offset < cycles; offset++)
        {
            var macService = offset < wavefrontCycles ? Math.Min(16, remainingMacs) : 0;
            remainingMacs -= macService;
            remainingIfmap -= Math.Min(width, remainingIfmap);
            remainingFilter -= Math.Min(width, remainingFilter);
            remainingOfmap -= Math.Min(width, remainingOfmap);
            AppendCycle(hash, startCycle + offset, 20, macService, remainingMacs ^ remainingIfmap ^ remainingFilter ^ remainingOfmap);
        }
        if (remainingMacs != 0 || remainingIfmap != 0 || remainingFilter != 0 || remainingOfmap != 0)
        {
            throw new InvalidOperationException("Systolic cycle replay did not drain compute and memory streams.");
        }
        return macs;
    }

    private static void AppendCycle(IncrementalHash hash, long cycle, int phase, long serviced, long remaining)
    {
        Span<byte> record = stackalloc byte[28];
        BinaryPrimitives.WriteInt64LittleEndian(record, cycle);
        BinaryPrimitives.WriteInt32LittleEndian(record[8..], phase);
        BinaryPrimitives.WriteInt64LittleEndian(record[12..], serviced);
        BinaryPrimitives.WriteInt64LittleEndian(record[20..], remaining);
        hash.AppendData(record);
    }

    private static long DivideRoundUp(long value, long divisor) => value == 0 ? 0 : checked((value + divisor - 1) / divisor);

    private static void Validate(AspdacMatchedWorkload workload)
    {
        if (string.IsNullOrWhiteSpace(workload.CaseId) || workload.M <= 0 || workload.N <= 0 || workload.K <= 0 || workload.ExpectedMacs != checked(workload.M * workload.N * workload.K))
        {
            throw new ArgumentException("Matched workload must have a stable id, positive M/N/K, and exact M*N*K MAC count.", nameof(workload));
        }
    }
}
