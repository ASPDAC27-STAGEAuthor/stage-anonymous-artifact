using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HardwareSim.Core;

/// <summary>Resolved im2col/fully-connected layer supplied by the final MNIST experiment bundle.</summary>
public sealed record AspdacCnnLayerOptions(
    string LayerId,
    string LayerKind,
    long M,
    long N,
    long K,
    long ImageCount,
    int PrecisionBits,
    long PostOpElementsPerImage,
    bool FullSystem,
    string ModelHash,
    string DatasetHash,
    string PredictionHash,
    string LoweringHash,
    int ConversionPasses = 0,
    string ArithmeticProfileId = "fp32_a32",
    string ArithmeticProfileHash = "not-supplied");

/// <summary>Cycle-derived S-Native replay for one lowered CNN layer.</summary>
public sealed record AspdacCnnLayerResult(
    string LayerId,
    string LayerKind,
    string Mode,
    long ImageCount,
    long CompletedMacs,
    long InputElements,
    long WeightElements,
    long OutputElements,
    long LogicalBits,
    long PaddingBits,
    long PacketizedBits,
    long PacketCount,
    long ComputeCycles,
    long MemoryCycles,
    long NocCycles,
    long PostOpCycles,
    long ConversionCycles,
    double ConversionEnergyPj,
    long TotalCycles,
    double ComputeUtilizationPercent,
    string DominantService,
    string CanonicalTraceHash);

/// <summary>
/// Replays exact lowered layer work on the frozen S-Native baseline. This is a cycle service
/// runtime, not a numerical CNN implementation; PyTorch remains the independent functional oracle.
/// </summary>
public static class AspdacCnnRuntime
{
    /// <summary>Gets the frozen S-Native aggregate MAC service width.</summary>
    public const long AggregateMacsPerCycle = 16 * 256;
    /// <summary>Gets the frozen S-Native link width.</summary>
    public const int LinkBitsPerCycle = 128;
    /// <summary>Gets the frozen S-Native memory port count.</summary>
    public const int MemoryPorts = 1;
    /// <summary>Gets the frozen S-Native memory latency.</summary>
    public const int MemoryLatencyCycles = 5;
    /// <summary>Gets the declared synthetic post-operation service width.</summary>
    public const int PostOpElementsPerCycle = 16;

    /// <summary>Runs one deterministic lowered CNN layer service replay.</summary>
    public static AspdacCnnLayerResult Run(AspdacCnnLayerOptions options)
    {
        Validate(options);
        var completedMacs = checked(options.M * options.N * options.K * options.ImageCount);
        var inputElements = checked(options.M * options.K * options.ImageCount);
        var weightElements = checked(options.K * options.N);
        var outputElements = checked(options.M * options.N * options.ImageCount);
        var logicalBits = checked((inputElements + weightElements + outputElements) * options.PrecisionBits);
        var packetCount = DivideRoundUp(logicalBits, AspdacCodesignRuntime.PacketBits);
        var packetizedBits = checked(packetCount * AspdacCodesignRuntime.PacketBits);
        var paddingBits = packetizedBits - logicalBits;

        var computeCycles = DivideRoundUp(completedMacs, AggregateMacsPerCycle);
        var memoryCycles = options.FullSystem ? checked(DivideRoundUp(packetizedBits, LinkBitsPerCycle) + MemoryLatencyCycles) : 0;
        var nocCycles = options.FullSystem ? DivideRoundUp(packetizedBits, LinkBitsPerCycle) : 0;
        var postOpElements = checked(options.PostOpElementsPerImage * options.ImageCount);
        var postOpCycles = options.FullSystem ? DivideRoundUp(postOpElements, PostOpElementsPerCycle) : 0;
        var conversionCycles = options.FullSystem
            ? checked(options.ConversionPasses * DivideRoundUp(packetCount, AspdacCodesignRuntime.ProcessingElements))
            : 0;
        var conversionEnergy = options.FullSystem
            ? checked(logicalBits * (long)options.ConversionPasses) * AspdacCodesignRuntime.PrecisionConversionEnergyPerBitPj
            : 0d;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var cycle = 0L;
        Replay(hash, ref cycle, phase: 1, computeCycles, completedMacs, AggregateMacsPerCycle);
        Replay(hash, ref cycle, phase: 2, memoryCycles, options.FullSystem ? packetizedBits : 0, LinkBitsPerCycle, fixedLatencyCycles: options.FullSystem ? MemoryLatencyCycles : 0);
        Replay(hash, ref cycle, phase: 3, nocCycles, options.FullSystem ? packetizedBits : 0, LinkBitsPerCycle);
        Replay(hash, ref cycle, phase: 4, postOpCycles, options.FullSystem ? postOpElements : 0, PostOpElementsPerCycle);
        Replay(hash, ref cycle, phase: 5, conversionCycles, options.FullSystem ? checked(packetCount * (long)options.ConversionPasses) : 0, AspdacCodesignRuntime.ProcessingElements);

        var services = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["compute"] = computeCycles,
            ["memory"] = memoryCycles,
            ["noc"] = nocCycles,
            ["post_op"] = postOpCycles,
            ["conversion"] = conversionCycles,
        };
        var dominant = services.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
        return new AspdacCnnLayerResult(
            options.LayerId,
            options.LayerKind,
            options.FullSystem ? "full_system" : "compute_only",
            options.ImageCount,
            completedMacs,
            inputElements,
            weightElements,
            outputElements,
            logicalBits,
            paddingBits,
            packetizedBits,
            packetCount,
            computeCycles,
            memoryCycles,
            nocCycles,
            postOpCycles,
            conversionCycles,
            conversionEnergy,
            cycle,
            100.0 * completedMacs / (computeCycles * (double)AggregateMacsPerCycle),
            dominant,
            BitConverter.ToString(hash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant());
    }

    private static void Replay(IncrementalHash hash, ref long cycle, int phase, long cycles, long work, long width, long fixedLatencyCycles = 0)
    {
        var remaining = work;
        var record = new byte[28];
        for (var offset = 0L; offset < cycles; offset++)
        {
            var serviced = offset < fixedLatencyCycles ? 0 : Math.Min(width, remaining);
            remaining -= serviced;
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(), cycle++);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), phase);
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(12), serviced);
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(20), remaining);
            hash.AppendData(record);
        }
        if (remaining != 0)
        {
            throw new InvalidOperationException($"CNN phase {phase} did not drain {remaining} work units.");
        }
    }

    private static long DivideRoundUp(long value, long divisor) => value == 0 ? 0 : checked((value + divisor - 1) / divisor);

    private static void Validate(AspdacCnnLayerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LayerId) || string.IsNullOrWhiteSpace(options.LayerKind) ||
            options.M <= 0 || options.N <= 0 || options.K <= 0 || options.ImageCount <= 0 ||
            options.PrecisionBits <= 0 || options.PostOpElementsPerImage < 0 || options.ConversionPasses < 0 ||
            string.IsNullOrWhiteSpace(options.ArithmeticProfileId) || string.IsNullOrWhiteSpace(options.ArithmeticProfileHash))
        {
            throw new ArgumentException("CNN layer requires stable ids, positive M/N/K/image/precision values, and non-negative post-op/conversion work.", nameof(options));
        }
        if (new[] { options.ModelHash, options.DatasetHash, options.PredictionHash, options.LoweringHash }.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("CNN layer provenance hashes are required.", nameof(options));
        }
    }
}
