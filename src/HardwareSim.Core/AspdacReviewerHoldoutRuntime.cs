using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>High-level, shape-only input for the independent 4x4 weight-stationary hold-out runtime.</summary>
public sealed record AspdacReviewerHoldoutInput(
    string CaseId,
    long M,
    long N,
    long K,
    int PrecisionBits = 16);

/// <summary>Independently lowered memory counts for one hold-out workload.</summary>
public sealed class AspdacReviewerHoldoutAccesses
{
    /// <summary>Gets array-side activation reads.</summary>
    public long SramIfmapReads { get; init; }
    /// <summary>Gets array-side weight reads.</summary>
    public long SramFilterReads { get; init; }
    /// <summary>Gets array-side output writes.</summary>
    public long SramOfmapWrites { get; init; }
    /// <summary>Gets off-chip activation reads after explicit output-column tiling.</summary>
    public long DramIfmapReads { get; init; }
    /// <summary>Gets off-chip weight reads after explicit output-row tiling.</summary>
    public long DramFilterReads { get; init; }
    /// <summary>Gets off-chip output writes.</summary>
    public long DramOfmapWrites { get; init; }
}

/// <summary>Deterministic result from independent shape lowering and cycle service.</summary>
public sealed class AspdacReviewerHoldoutResult
{
    /// <summary>Gets the stable workload id.</summary>
    public string CaseId { get; init; } = "";
    /// <summary>Gets the exact input shape.</summary>
    public AspdacReviewerHoldoutInput Input { get; init; } = new("", 1, 1, 1);
    /// <summary>Gets the exact MAC count M*N*K.</summary>
    public long ExpectedMacs { get; init; }
    /// <summary>Gets the number of four-row tiles.</summary>
    public long RowTiles { get; init; }
    /// <summary>Gets the number of four-column tiles.</summary>
    public long ColumnTiles { get; init; }
    /// <summary>Gets the sequential weight-stationary wavefront cycles.</summary>
    public long WavefrontCycles { get; init; }
    /// <summary>Gets the independently calculated memory service cycles.</summary>
    public long MemoryServiceCycles { get; init; }
    /// <summary>Gets warm cycles with compute and the three memory streams overlapped.</summary>
    public long WarmCycles { get; init; }
    /// <summary>Gets the frozen cold-start prefetch cycles.</summary>
    public long PrefetchCycles { get; init; }
    /// <summary>Gets cold-start total cycles.</summary>
    public long TotalCycles { get; init; }
    /// <summary>Gets memory extension beyond the array wavefront.</summary>
    public long MemoryStallCycles { get; init; }
    /// <summary>Gets effective 16-MAC array utilization over warm cycles.</summary>
    public double UtilizationPercent { get; init; }
    /// <summary>Gets independently lowered access counts.</summary>
    public AspdacReviewerHoldoutAccesses Accesses { get; init; } = new();
    /// <summary>Gets the canonical high-level input digest.</summary>
    public string InputSha256 { get; init; } = "";
    /// <summary>Gets the canonical tile-event trace digest.</summary>
    public string CanonicalTraceSha256 { get; init; } = "";
    /// <summary>Gets the frozen evidence label for timing comparisons.</summary>
    public string TimingEvidenceLabel { get; init; } = "Trend";
    /// <summary>Gets the frozen evidence label for same-candidate repeat hashes.</summary>
    public string RepeatEvidenceLabel { get; init; } = "Exact";
}

/// <summary>Independent 4x4 weight-stationary hold-out lowering used by the reviewer extension.</summary>
public static class AspdacReviewerHoldoutRuntime
{
    /// <summary>Gets the fixed array row count.</summary>
    public const int ArrayRows = 4;
    /// <summary>Gets the fixed array column count.</summary>
    public const int ArrayColumns = 4;
    /// <summary>Gets the fixed words serviced per memory stream per cycle.</summary>
    public const int MemoryWordsPerCycle = 8;
    /// <summary>Gets the fixed aggregate words prefetched for a cold start.</summary>
    public const int ColdStartPrefetchWords = 16 * 1024;
    /// <summary>Gets the fixed aggregate cold-start prefetch service width.</summary>
    public const int ColdStartWordsPerCycle = 16;

    private static readonly IReadOnlyList<AspdacReviewerHoldoutInput> FrozenCases = Array.AsReadOnly<AspdacReviewerHoldoutInput>(
    [
        new("holdout_gemm_096", 96, 96, 96),
        new("holdout_gemm_192", 192, 192, 192),
        new("holdout_gemm_384", 384, 384, 384),
        new("holdout_rect_128x256x64", 128, 256, 64),
        new("holdout_rect_256x64x192", 256, 64, 192),
        new("holdout_rect_64x384x128", 64, 384, 128),
        new("holdout_attn_qk_s096_d064", 96, 96, 64),
        new("holdout_attn_qk_s192_d064", 192, 192, 64)
    ]);

    /// <summary>Gets the frozen eight-case hold-out matrix.</summary>
    public static IReadOnlyList<AspdacReviewerHoldoutInput> Cases => FrozenCases;

    /// <summary>Finds one frozen case by exact id.</summary>
    public static AspdacReviewerHoldoutInput GetCase(string caseId) =>
        FrozenCases.SingleOrDefault(item => string.Equals(item.CaseId, caseId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unknown reviewer hold-out case '{caseId}'.", nameof(caseId));

    /// <summary>Lowers and runs one workload using only its high-level shape and frozen public contract.</summary>
    public static AspdacReviewerHoldoutResult Run(AspdacReviewerHoldoutInput input)
    {
        Validate(input);
        var expectedMacs = checked(checked(input.M * input.N) * input.K);
        var rowTiles = DivideRoundUp(input.M, ArrayRows);
        var columnTiles = DivideRoundUp(input.N, ArrayColumns);
        var wavefrontCycles = 0L;
        var dramIfmapReads = 0L;
        var dramFilterReads = 0L;
        var dramOfmapWrites = 0L;

        using var traceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(traceHash, "stage-reviewer-holdout-ws/v1");
        Append(traceHash, CanonicalInput(input));
        var startCycle = 0L;
        for (var rowTile = 0L; rowTile < rowTiles; rowTile++)
        {
            var activeRows = Math.Min((long)ArrayRows, input.M - rowTile * ArrayRows);
            for (var columnTile = 0L; columnTile < columnTiles; columnTile++)
            {
                var activeColumns = Math.Min((long)ArrayColumns, input.N - columnTile * ArrayColumns);
                var tileCycles = checked(input.K + activeRows + activeColumns - 2);
                var ifmapWords = checked(activeRows * input.K);
                var filterWords = checked(activeColumns * input.K);
                var outputWords = checked(activeRows * activeColumns);
                Append(traceHash, string.Join("|",
                    rowTile.ToString(CultureInfo.InvariantCulture),
                    columnTile.ToString(CultureInfo.InvariantCulture),
                    startCycle.ToString(CultureInfo.InvariantCulture),
                    tileCycles.ToString(CultureInfo.InvariantCulture),
                    activeRows.ToString(CultureInfo.InvariantCulture),
                    activeColumns.ToString(CultureInfo.InvariantCulture),
                    ifmapWords.ToString(CultureInfo.InvariantCulture),
                    filterWords.ToString(CultureInfo.InvariantCulture),
                    outputWords.ToString(CultureInfo.InvariantCulture)));
                wavefrontCycles = checked(wavefrontCycles + tileCycles);
                startCycle = checked(startCycle + tileCycles);
                dramIfmapReads = checked(dramIfmapReads + ifmapWords);
                dramFilterReads = checked(dramFilterReads + filterWords);
                dramOfmapWrites = checked(dramOfmapWrites + outputWords);
            }
        }

        var ifmapService = DivideRoundUp(dramIfmapReads, MemoryWordsPerCycle);
        var filterService = DivideRoundUp(dramFilterReads, MemoryWordsPerCycle);
        var ofmapService = DivideRoundUp(dramOfmapWrites, MemoryWordsPerCycle);
        var memoryService = Math.Max(ifmapService, Math.Max(filterService, ofmapService));
        var warmCycles = Math.Max(wavefrontCycles, memoryService);
        var prefetchCycles = DivideRoundUp(ColdStartPrefetchWords, ColdStartWordsPerCycle);
        var accesses = new AspdacReviewerHoldoutAccesses
        {
            SramIfmapReads = expectedMacs,
            SramFilterReads = expectedMacs,
            SramOfmapWrites = checked(input.M * input.N),
            DramIfmapReads = dramIfmapReads,
            DramFilterReads = dramFilterReads,
            DramOfmapWrites = dramOfmapWrites
        };
        Append(traceHash, string.Join("|",
            "summary",
            wavefrontCycles.ToString(CultureInfo.InvariantCulture),
            memoryService.ToString(CultureInfo.InvariantCulture),
            warmCycles.ToString(CultureInfo.InvariantCulture),
            accesses.SramIfmapReads.ToString(CultureInfo.InvariantCulture),
            accesses.SramFilterReads.ToString(CultureInfo.InvariantCulture),
            accesses.SramOfmapWrites.ToString(CultureInfo.InvariantCulture),
            accesses.DramIfmapReads.ToString(CultureInfo.InvariantCulture),
            accesses.DramFilterReads.ToString(CultureInfo.InvariantCulture),
            accesses.DramOfmapWrites.ToString(CultureInfo.InvariantCulture)));

        return new AspdacReviewerHoldoutResult
        {
            CaseId = input.CaseId,
            Input = input,
            ExpectedMacs = expectedMacs,
            RowTiles = rowTiles,
            ColumnTiles = columnTiles,
            WavefrontCycles = wavefrontCycles,
            MemoryServiceCycles = memoryService,
            WarmCycles = warmCycles,
            PrefetchCycles = prefetchCycles,
            TotalCycles = checked(prefetchCycles + warmCycles),
            MemoryStallCycles = Math.Max(0, memoryService - wavefrontCycles),
            UtilizationPercent = 100.0 * expectedMacs / (warmCycles * (double)(ArrayRows * ArrayColumns)),
            Accesses = accesses,
            InputSha256 = Hash(CanonicalInput(input)),
            CanonicalTraceSha256 = LowerHex(traceHash.GetHashAndReset())
        };
    }

    private static string CanonicalInput(AspdacReviewerHoldoutInput input) => string.Join("|",
        input.CaseId,
        input.M.ToString(CultureInfo.InvariantCulture),
        input.N.ToString(CultureInfo.InvariantCulture),
        input.K.ToString(CultureInfo.InvariantCulture),
        input.PrecisionBits.ToString(CultureInfo.InvariantCulture),
        $"{ArrayRows}x{ArrayColumns}",
        "weight_stationary",
        MemoryWordsPerCycle.ToString(CultureInfo.InvariantCulture));

    private static void Validate(AspdacReviewerHoldoutInput input)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));
        if (string.IsNullOrWhiteSpace(input.CaseId) || input.M <= 0 || input.N <= 0 || input.K <= 0)
            throw new ArgumentException("Hold-out input requires a stable id and positive M/N/K.", nameof(input));
        if (input.PrecisionBits is not (8 or 16 or 32))
            throw new ArgumentOutOfRangeException(nameof(input), "Precision must be 8, 16, or 32 bits.");
        _ = checked(checked(input.M * input.N) * input.K);
    }

    private static long DivideRoundUp(long value, long divisor) => value == 0 ? 0 : checked((value + divisor - 1) / divisor);
    private static void Append(IncrementalHash hash, string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
    private static string Hash(string value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        return LowerHex(hash.GetHashAndReset());
    }
    private static string LowerHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
}
