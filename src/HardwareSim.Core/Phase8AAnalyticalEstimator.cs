using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Structured estimator validation issue.</summary>
public sealed record Phase8AEstimatorIssue(string Code, string Location, string Message, string RelatedId = "");

/// <summary>Result of validating and evaluating analytical transactions.</summary>
public sealed class Phase8AEstimatorEvaluationResult
{
    /// <summary>Gets whether a complete estimate was produced.</summary>
    public bool IsSuccess => Estimate is not null && Issues.Count == 0;
    /// <summary>Gets the immutable estimate.</summary>
    public Phase8AEstimatorResult? Estimate { get; init; }
    /// <summary>Gets structured validation issues.</summary>
    public IReadOnlyList<Phase8AEstimatorIssue> Issues { get; init; } = [];
}

/// <summary>Evaluates exact route traffic with the approved Phase 8A analytical formula.</summary>
public static class Phase8AAnalyticalEstimator
{
    /// <summary>Evaluates transactions using internal bits and an explicit report word width.</summary>
    public static Phase8AEstimatorEvaluationResult Evaluate(IEnumerable<Phase8AEstimatorTransaction>? transactions, int wordBits = 32)
    {
        var supplied = (transactions ?? []).ToArray();
        if (supplied.Any(item => item is null))
            return Failed([Issue("EstimatorTransactionNull", "$.transactions", "Estimator transactions cannot contain null entries.")]);
        var materialized = supplied.OrderBy(item => item.TransactionId, StringComparer.Ordinal).ToList();
        var issues = Validate(materialized, wordBits);
        if (issues.Count > 0) return Failed(issues);
        try
        {
            var breakdown = materialized.Select(transaction => EvaluateTransaction(transaction, wordBits)).ToList();
            var traffic = breakdown.GroupBy(item => item.TrafficKind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => checked(group.Sum(item => item.PacketBits)), StringComparer.Ordinal);
            var transferredBits = checked(breakdown.Sum(item => item.PacketBits));
            var transferredWords = checked(breakdown.Sum(item => item.Words));
            var bitHops = checked(breakdown.Sum(item => item.BitHops));
            var wordHops = checked(breakdown.Sum(item => item.WordHops));
            var distance = breakdown.Sum(item => item.DistanceWeightedBitMicrometers);
            var contentionFree = checked(breakdown.Sum(item => item.ContentionFreeLatencyCycles));
            var adjusted = checked(breakdown.Sum(item => item.CongestionAdjustedLatencyCycles));
            var peakBuffer = breakdown.Count == 0 ? 0 : breakdown.Max(item => item.PredictedBufferedBits);
            var modelEnergy = breakdown.Sum(item => item.ModelEnergyPicojoules);
            var proxyEnergy = breakdown.Sum(item => item.AnalyticalProxyEnergyPicojoules);
            var canonical = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
            {
                WordBits = wordBits,
                Transactions = breakdown,
                TrafficBitsByKind = traffic.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                TransferredBits = transferredBits,
                TransferredWords = transferredWords,
                BitHops = bitHops,
                WordHops = wordHops,
                DistanceWeightedBitMicrometers = distance,
                ContentionFreeLatencyCycles = contentionFree,
                CongestionAdjustedLatencyCycles = adjusted,
                PredictedPeakBufferBits = peakBuffer,
                ModelEnergyPicojoules = modelEnergy,
                AnalyticalProxyEnergyPicojoules = proxyEnergy
            }, HardwareGraphJson.Options));
            var hash = ComponentExecutionJson.ComputeSha256(canonical);
            return new Phase8AEstimatorEvaluationResult
            {
                Estimate = new Phase8AEstimatorResult(
                    wordBits, new ReadOnlyCollection<Phase8AEstimatorTransactionMetrics>(breakdown), traffic,
                    transferredBits, transferredWords, bitHops, wordHops, distance, contentionFree, adjusted,
                    peakBuffer, modelEnergy, proxyEnergy, hash)
            };
        }
        catch (OverflowException)
        {
            return Failed([Issue("EstimatorArithmeticOverflow", "$", "Estimator arithmetic exceeded supported 64-bit or decimal bounds.")]);
        }
    }

    private static Phase8AEstimatorTransactionMetrics EvaluateTransaction(Phase8AEstimatorTransaction transaction, int wordBits)
    {
        var words = CeilingDivide(transaction.PacketBits, wordBits);
        var latency = 0L;
        var adjusted = 0L;
        var routeLength = 0m;
        foreach (var hop in transaction.Hops)
        {
            var serialization = CeilingDivide(transaction.PacketBits, hop.BandwidthBitsPerCycle);
            var hopLatency = checked(hop.RouterLatencyCycles + serialization + (hop.PerformsReduction ? hop.AdderLatencyCycles : 0));
            latency = checked(latency + hopLatency);
            adjusted = checked(adjusted + CeilingDecimal(hopLatency * hop.CongestionMultiplier));
            routeLength += hop.RouteLengthMicrometers;
        }
        return new Phase8AEstimatorTransactionMetrics(
            transaction.TransactionId,
            transaction.TrafficKind,
            transaction.PacketBits,
            words,
            transaction.Hops.Count,
            checked(transaction.PacketBits * transaction.Hops.Count),
            checked(words * transaction.Hops.Count),
            transaction.PacketBits * routeLength,
            latency,
            adjusted,
            transaction.PredictedBufferedBits,
            transaction.BufferBasis,
            transaction.ModelEnergyPicojoules,
            transaction.ModelEnergySource,
            transaction.AnalyticalProxyEnergyPicojoules,
            transaction.AnalyticalProxySource);
    }

    private static List<Phase8AEstimatorIssue> Validate(IReadOnlyList<Phase8AEstimatorTransaction> transactions, int wordBits)
    {
        var issues = new List<Phase8AEstimatorIssue>();
        if (wordBits <= 0) issues.Add(Issue("EstimatorWordBitsInvalid", "$.word_bits", "Report word width must be positive."));
        if (transactions.Count == 0) issues.Add(Issue("EstimatorTransactionsMissing", "$.transactions", "At least one routed transaction is required."));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in transactions)
        {
            var location = "$.transactions[" + transaction.TransactionId + "]";
            if (string.IsNullOrWhiteSpace(transaction.TransactionId) || !ids.Add(transaction.TransactionId))
                issues.Add(Issue("EstimatorTransactionIdInvalid", location, "Transaction ids must be non-empty and unique.", transaction.TransactionId));
            if (!IsKnownTrafficKind(transaction.TrafficKind) || transaction.PacketBits <= 0 || transaction.Hops.Count == 0)
                issues.Add(Issue("EstimatorTransactionInvalid", location, "A registered traffic kind, positive packet bits, and at least one exact hop are required.", transaction.TransactionId));
            if (transaction.PredictedBufferedBits < 0 || string.IsNullOrWhiteSpace(transaction.BufferBasis))
                issues.Add(Issue("EstimatorBufferBasisInvalid", location, "Predicted buffer bits must be non-negative and carry explicit basis provenance.", transaction.TransactionId));
            if (transaction.ModelEnergyPicojoules < 0 || transaction.AnalyticalProxyEnergyPicojoules < 0 ||
                string.IsNullOrWhiteSpace(transaction.ModelEnergySource) || string.IsNullOrWhiteSpace(transaction.AnalyticalProxySource))
                issues.Add(Issue("EstimatorEnergyProvenanceInvalid", location, "Energy values must be non-negative and both source fields must be explicit.", transaction.TransactionId));
            if (transaction.ModelEnergyPicojoules > 0 && transaction.ModelEnergySource.Contains("proxy", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue("EstimatorProxyMisclassified", location, "Proxy energy cannot be labeled as characterized/model energy.", transaction.TransactionId));
            if (transaction.AnalyticalProxyEnergyPicojoules > 0 && !transaction.AnalyticalProxySource.Contains("analytical_proxy", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue("EstimatorProxyProvenanceInvalid", location, "Paper proxy energy must be explicitly labeled analytical_proxy.", transaction.TransactionId));
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hop in transaction.Hops)
            {
                if (string.IsNullOrWhiteSpace(hop.LinkId) || !linkIds.Add(hop.LinkId) || hop.BandwidthBitsPerCycle <= 0 || hop.RouteLengthMicrometers < 0 ||
                    hop.RouterLatencyCycles < 0 || hop.AdderLatencyCycles < 0 || hop.CongestionMultiplier < 1 ||
                    (!hop.PerformsReduction && hop.AdderLatencyCycles != 0))
                    issues.Add(Issue("EstimatorHopInvalid", location + ".hops", "Hop id/bandwidth/length/latency/congestion must be valid; adder latency is allowed only on a reduction hop.", hop.LinkId));
            }
        }
        return issues;
    }

    private static bool IsKnownTrafficKind(string value) => value is
        Phase8ATrafficKinds.WeightPreload or
        Phase8ATrafficKinds.ActivationMulticast or
        Phase8ATrafficKinds.Reduction or
        Phase8ATrafficKinds.Assembly;

    private static long CeilingDivide(long value, long divisor) => checked((value + divisor - 1) / divisor);
    private static long CeilingDecimal(decimal value)
    {
        var ceiling = decimal.Ceiling(value);
        if (ceiling > long.MaxValue) throw new OverflowException();
        return decimal.ToInt64(ceiling);
    }
    private static Phase8AEstimatorEvaluationResult Failed(IEnumerable<Phase8AEstimatorIssue> issues) => new()
    {
        Issues = new ReadOnlyCollection<Phase8AEstimatorIssue>(issues.ToList())
    };
    private static Phase8AEstimatorIssue Issue(string code, string location, string message, string relatedId = "") =>
        new(code, location, message, relatedId);
}
