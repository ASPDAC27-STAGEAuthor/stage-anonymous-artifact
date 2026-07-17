using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Stable traffic categories reported by the Phase 8A estimator.</summary>
public static class Phase8ATrafficKinds
{
    /// <summary>Weight preload traffic.</summary>
    public const string WeightPreload = "weight_preload";
    /// <summary>Activation or multicast traffic.</summary>
    public const string ActivationMulticast = "activation_multicast";
    /// <summary>Partial-sum reduction traffic.</summary>
    public const string Reduction = "reduction";
    /// <summary>Tensor assembly traffic.</summary>
    public const string Assembly = "assembly";
}

/// <summary>One exact estimator hop derived from a directed RoutePath.</summary>
public sealed record Phase8AEstimatorHop(
    string LinkId,
    long BandwidthBitsPerCycle,
    decimal RouteLengthMicrometers,
    long RouterLatencyCycles,
    bool PerformsReduction,
    long AdderLatencyCycles,
    decimal CongestionMultiplier);

/// <summary>One packet transaction and its exact estimator route.</summary>
public sealed class Phase8AEstimatorTransaction
{
    /// <summary>Creates an immutable estimator transaction.</summary>
    public Phase8AEstimatorTransaction(
        string transactionId,
        string trafficKind,
        long packetBits,
        IEnumerable<Phase8AEstimatorHop>? hops,
        long predictedBufferedBits,
        string bufferBasis,
        decimal modelEnergyPicojoules = 0,
        string modelEnergySource = "not_available",
        decimal analyticalProxyEnergyPicojoules = 0,
        string analyticalProxySource = "not_available")
    {
        var materializedHops = (hops ?? []).ToArray();
        if (materializedHops.Any(hop => hop is null))
            throw new ArgumentException("Estimator routes cannot contain null hops.", nameof(hops));
        TransactionId = transactionId?.Trim() ?? "";
        TrafficKind = trafficKind?.Trim() ?? "";
        PacketBits = packetBits;
        Hops = Array.AsReadOnly(materializedHops.Select(hop => hop with { }).ToArray());
        PredictedBufferedBits = predictedBufferedBits;
        BufferBasis = bufferBasis?.Trim() ?? "";
        ModelEnergyPicojoules = modelEnergyPicojoules;
        ModelEnergySource = modelEnergySource?.Trim() ?? "";
        AnalyticalProxyEnergyPicojoules = analyticalProxyEnergyPicojoules;
        AnalyticalProxySource = analyticalProxySource?.Trim() ?? "";
    }

    /// <summary>Gets the stable transaction id.</summary>
    public string TransactionId { get; }
    /// <summary>Gets the traffic category.</summary>
    public string TrafficKind { get; }
    /// <summary>Gets packet payload bits counted once at the producer.</summary>
    public long PacketBits { get; }
    /// <summary>Gets exact directed route hops.</summary>
    public IReadOnlyList<Phase8AEstimatorHop> Hops { get; }
    /// <summary>Gets the estimator buffer upper bound contributed by this transaction.</summary>
    public long PredictedBufferedBits { get; }
    /// <summary>Gets explicit provenance for the buffer estimate.</summary>
    public string BufferBasis { get; }
    /// <summary>Gets characterized/model energy, separate from analytical proxy energy.</summary>
    public decimal ModelEnergyPicojoules { get; }
    /// <summary>Gets characterized/model energy provenance.</summary>
    public string ModelEnergySource { get; }
    /// <summary>Gets analytical paper-proxy energy.</summary>
    public decimal AnalyticalProxyEnergyPicojoules { get; }
    /// <summary>Gets analytical proxy provenance.</summary>
    public string AnalyticalProxySource { get; }
}

/// <summary>Per-transaction estimator breakdown used for independent recomputation.</summary>
public sealed record Phase8AEstimatorTransactionMetrics(
    string TransactionId,
    string TrafficKind,
    long PacketBits,
    long Words,
    long HopCount,
    long BitHops,
    long WordHops,
    decimal DistanceWeightedBitMicrometers,
    long ContentionFreeLatencyCycles,
    long CongestionAdjustedLatencyCycles,
    long PredictedBufferedBits,
    string BufferBasis,
    decimal ModelEnergyPicojoules,
    string ModelEnergySource,
    decimal AnalyticalProxyEnergyPicojoules,
    string AnalyticalProxySource);

/// <summary>Immutable Phase 8A analytical estimate with disjoint energy sources.</summary>
public sealed class Phase8AEstimatorResult
{
    internal Phase8AEstimatorResult(
        int wordBits,
        IReadOnlyList<Phase8AEstimatorTransactionMetrics> transactions,
        IReadOnlyDictionary<string, long> trafficBitsByKind,
        long transferredBits,
        long transferredWords,
        long bitHops,
        long wordHops,
        decimal distanceWeightedBitMicrometers,
        long contentionFreeLatencyCycles,
        long congestionAdjustedLatencyCycles,
        long predictedPeakBufferBits,
        decimal modelEnergyPicojoules,
        decimal analyticalProxyEnergyPicojoules,
        string canonicalHash)
    {
        WordBits = wordBits;
        Transactions = transactions;
        TrafficBitsByKind = new ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(
            trafficBitsByKind.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal));
        TransferredBits = transferredBits;
        TransferredWords = transferredWords;
        BitHops = bitHops;
        WordHops = wordHops;
        DistanceWeightedBitMicrometers = distanceWeightedBitMicrometers;
        ContentionFreeLatencyCycles = contentionFreeLatencyCycles;
        CongestionAdjustedLatencyCycles = congestionAdjustedLatencyCycles;
        PredictedPeakBufferBits = predictedPeakBufferBits;
        ModelEnergyPicojoules = modelEnergyPicojoules;
        AnalyticalProxyEnergyPicojoules = analyticalProxyEnergyPicojoules;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets report word width.</summary>
    public int WordBits { get; }
    /// <summary>Gets stable per-transaction metrics.</summary>
    public IReadOnlyList<Phase8AEstimatorTransactionMetrics> Transactions { get; }
    /// <summary>Gets transferred bits grouped by provenance category.</summary>
    public IReadOnlyDictionary<string, long> TrafficBitsByKind { get; }
    /// <summary>Gets producer-counted transferred bits.</summary>
    public long TransferredBits { get; }
    /// <summary>Gets producer-counted words rounded per transaction.</summary>
    public long TransferredWords { get; }
    /// <summary>Gets exact bit-hop traffic.</summary>
    public long BitHops { get; }
    /// <summary>Gets exact word-hop traffic.</summary>
    public long WordHops { get; }
    /// <summary>Gets sum(bits times RoutePath length in micrometers).</summary>
    public decimal DistanceWeightedBitMicrometers { get; }
    /// <summary>Gets contention-free analytical latency.</summary>
    public long ContentionFreeLatencyCycles { get; }
    /// <summary>Gets congestion-adjusted analytical latency.</summary>
    public long CongestionAdjustedLatencyCycles { get; }
    /// <summary>Gets predicted peak buffered bits and not runtime occupancy.</summary>
    public long PredictedPeakBufferBits { get; }
    /// <summary>Gets characterized/model energy only.</summary>
    public decimal ModelEnergyPicojoules { get; }
    /// <summary>Gets analytical paper-proxy energy only.</summary>
    public decimal AnalyticalProxyEnergyPicojoules { get; }
    /// <summary>Gets deterministic result hash.</summary>
    public string CanonicalHash { get; }
}
