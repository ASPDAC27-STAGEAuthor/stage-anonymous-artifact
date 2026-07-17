using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

#pragma warning disable CS1591

public sealed record AspdacCodesignOptions(
    string WorkloadId,
    long MacCount,
    long PacketCount,
    long MemoryRequests,
    int MacsPerPePerCycle,
    int LinkBitsPerCycle,
    int MemoryPorts,
    int QueueDepth,
    string GraphHash,
    string WorkloadHash,
    string MappingHash,
    string ModelHash);

public sealed record AspdacCodesignResult(
    string WorkloadId,
    long MacCount,
    long PacketCount,
    int MacsPerPePerCycle,
    int LinkBitsPerCycle,
    int MemoryPorts,
    int QueueDepth,
    long TotalCycles,
    long ComputeServiceDemandCycles,
    long MemoryServiceDemandCycles,
    long NocServiceDemandCycles,
    long ComputeCriticalCycles,
    long MemoryCriticalCycles,
    long NocCriticalCycles,
    long ReductionCriticalCycles,
    long SoftmaxCriticalCycles,
    long ConversionCriticalCycles,
    string DominantBottleneck,
    long DominantStallCycles,
    string DominantComponentId,
    string DominantLinkId,
    string DominantPacketEvidence,
    string DominantReason,
    string GraphHash,
    string WorkloadHash,
    string MappingHash,
    string ModelHash,
    string CanonicalTraceSha256);

public sealed record AspdacPrecisionOptions(
    string Precision,
    int BitsPerElement,
    int ConversionPasses,
    long ElementCount,
    AspdacCodesignOptions Baseline);

public sealed record AspdacPrecisionResult(
    string Precision,
    int BitsPerElement,
    long ElementCount,
    long LogicalBits,
    long PaddingBits,
    long PacketizedBits,
    long PacketCount,
    long TotalCycles,
    long ConversionCycles,
    double ConversionEnergyPj,
    string DominantBottleneck,
    string WorkloadHash,
    string MappingHash,
    string BaseModelHash,
    string PrecisionModelHash,
    string CanonicalTraceSha256);

/// <summary>Cycle-stepped service runtime for the STAGE-only final co-design studies.</summary>
public static class AspdacCodesignRuntime
{
    public const int ProcessingElements = 16;
    public const int PacketBits = 128;
    public const int MemoryLatencyCycles = 5;
    public const int ReductionLatencyCycles = 2;
    public const int SoftmaxLatencyCycles = 8;
    public const int ParallelTransportPaths = 8;
    public const double PrecisionConversionEnergyPerBitPj = 0.02;

    public static AspdacCodesignResult Run(AspdacCodesignOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.MacCount <= 0 || options.PacketCount <= 0 || options.MemoryRequests <= 0 ||
            options.MacsPerPePerCycle <= 0 || options.LinkBitsPerCycle <= 0 || options.MemoryPorts <= 0 || options.QueueDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "All service quantities must be positive.");

        var attention = string.Equals(options.WorkloadId, "attention_128_64", StringComparison.OrdinalIgnoreCase);
        var phaseCount = attention ? 2 : 1;
        var computeDemands = SplitDemand(CeilDiv(options.MacCount, checked(ProcessingElements * (long)options.MacsPerPePerCycle)), phaseCount);
        var serializedPacketCycles = CeilDiv(PacketBits, options.LinkBitsPerCycle);
        var nocDemands = SplitDemand(CeilDiv(checked(options.PacketCount * serializedPacketCycles), ParallelTransportPaths), phaseCount);
        var memoryDemands = SplitDemand(checked(CeilDiv(options.MemoryRequests, options.MemoryPorts) * MemoryLatencyCycles), phaseCount);
        var attribution = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["compute"] = 0,
            ["memory"] = 0,
            ["noc"] = 0,
            ["reduction"] = attention ? checked(128L * ReductionLatencyCycles) : ReductionLatencyCycles,
            ["softmax"] = attention ? checked(128L * SoftmaxLatencyCycles) : 0,
            ["conversion"] = 0
        };

        using var trace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var cycle = 0L;
        for (var phase = 0; phase < phaseCount; phase++)
        {
            var compute = computeDemands[phase];
            var memory = memoryDemands[phase];
            var noc = nocDemands[phase];
            var dominant = Dominant(compute, memory, noc);
            var phaseCycles = Math.Max(compute, Math.Max(memory, noc));
            attribution[dominant] += phaseCycles;
            for (var local = 0L; local < phaseCycles; local++, cycle++)
            {
                Append(trace, $"phase|{phase}|{cycle}|{Math.Max(0, compute - local)}|{Math.Max(0, memory - local)}|{Math.Max(0, noc - local)}|{dominant}\n");
            }
        }

        foreach (var special in new[] { "reduction", "softmax" })
        {
            for (var local = 0L; local < attribution[special]; local++, cycle++)
                Append(trace, $"special|{cycle}|{special}|{attribution[special] - local}\n");
        }
        var total = attribution.Values.Sum();
        if (cycle != total) throw new InvalidOperationException("Critical-path attribution does not sum to runtime cycles.");
        var bottleneck = attribution.Where(item => item.Key is "compute" or "memory" or "noc" or "softmax")
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .First();
        var evidence = Evidence(bottleneck.Key, options.PacketCount);
        Append(trace, $"summary|{total}|{bottleneck.Key}|{bottleneck.Value}\n");

        return new AspdacCodesignResult(
            options.WorkloadId,
            options.MacCount,
            options.PacketCount,
            options.MacsPerPePerCycle,
            options.LinkBitsPerCycle,
            options.MemoryPorts,
            options.QueueDepth,
            total,
            computeDemands.Sum(),
            memoryDemands.Sum(),
            nocDemands.Sum(),
            attribution["compute"],
            attribution["memory"],
            attribution["noc"],
            attribution["reduction"],
            attribution["softmax"],
            attribution["conversion"],
            bottleneck.Key,
            bottleneck.Value,
            evidence.Component,
            evidence.Link,
            evidence.Packet,
            evidence.Reason,
            options.GraphHash,
            options.WorkloadHash,
            options.MappingHash,
            options.ModelHash,
            ToHex(trace.GetHashAndReset()));
    }

    public static AspdacPrecisionResult RunPrecision(AspdacPrecisionOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.BitsPerElement <= 0 || options.ElementCount <= 0 || options.ConversionPasses < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        var logicalBits = checked(options.ElementCount * options.BitsPerElement);
        var packets = CeilDiv(logicalBits, PacketBits);
        var packetizedBits = checked(packets * PacketBits);
        var memoryRequests = Math.Max(1, CeilDiv(checked(options.Baseline.MemoryRequests * (long)options.BitsPerElement), 16));
        var baseResult = Run(options.Baseline with { PacketCount = packets, MemoryRequests = memoryRequests });
        var conversionCycles = checked(options.ConversionPasses * CeilDiv(packets, ProcessingElements));
        var conversionEnergy = checked(logicalBits * options.ConversionPasses) * PrecisionConversionEnergyPerBitPj;
        var total = checked(baseResult.TotalCycles + conversionCycles);
        var dominant = conversionCycles > baseResult.DominantStallCycles ? "conversion" : baseResult.DominantBottleneck;
        var precisionHash = Sha256(string.Join("|", options.Precision, options.BitsPerElement, options.ConversionPasses,
            logicalBits, packetizedBits, PrecisionConversionEnergyPerBitPj.ToString("R", CultureInfo.InvariantCulture)));
        var traceHash = Sha256(string.Join("|", baseResult.CanonicalTraceSha256, precisionHash, conversionCycles, total));
        return new AspdacPrecisionResult(
            options.Precision,
            options.BitsPerElement,
            options.ElementCount,
            logicalBits,
            packetizedBits - logicalBits,
            packetizedBits,
            packets,
            total,
            conversionCycles,
            conversionEnergy,
            dominant,
            options.Baseline.WorkloadHash,
            options.Baseline.MappingHash,
            options.Baseline.ModelHash,
            precisionHash,
            traceHash);
    }

    private static string Dominant(long compute, long memory, long noc) =>
        new[] { (Name: "compute", Value: compute), (Name: "memory", Value: memory), (Name: "noc", Value: noc) }
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .First().Name;

    private static (string Component, string Link, string Packet, string Reason) Evidence(string bottleneck, long packets) => bottleneck switch
    {
        "compute" => ("pe[0..15]", "none", "operation-range:0..last", "compute_busy"),
        "memory" => ("memory0", "memory0.port[0]", $"packet-range:0..{packets - 1}", "memory_port_busy"),
        "noc" => ("router[0..15]", "mesh.transport", $"packet-range:0..{packets - 1}", "link_serialization_or_arbitration"),
        "softmax" => ("softmax0", "none", "attention-softmax-stage", "special_unit_busy"),
        _ => ("unknown", "unknown", "unknown", bottleneck)
    };

    private static long[] SplitDemand(long total, int parts)
    {
        var result = new long[parts];
        for (var index = 0; index < parts; index++) result[index] = total / parts + (index < total % parts ? 1 : 0);
        return result;
    }

    private static long CeilDiv(long value, long divisor) => checked((value + divisor - 1) / divisor);
    private static void Append(IncrementalHash trace, string value) => trace.AppendData(Encoding.UTF8.GetBytes(value));
    private static string ToHex(IEnumerable<byte> bytes) => string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    private static string Sha256(string value)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}

#pragma warning restore CS1591
