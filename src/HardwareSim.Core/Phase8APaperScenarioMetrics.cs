using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Cycle-measured metrics kept separate from analytical estimator values.</summary>
public sealed record Phase8ARuntimeMetricSnapshot(
    long TotalCycles,
    long LinkTransferredBitHops,
    long PeakBufferOccupancyBits,
    double AverageUtilization,
    double PeUtilization,
    double RouterUtilization,
    decimal ModelEnergyPicojoules,
    string ModelEnergySource,
    string TraceHash);

/// <summary>Extracts cross-checkable runtime metrics without adding paper proxy energy.</summary>
public static class Phase8ARuntimeMetricSnapshotBuilder
{
    /// <summary>Builds one immutable runtime snapshot from a completed cycle run.</summary>
    public static Phase8ARuntimeMetricSnapshot FromSimulation(SimulationResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (!result.Completed) throw new InvalidOperationException("A completed cycle simulation is required for runtime metrics.");
        return new Phase8ARuntimeMetricSnapshot(
            result.Metrics.Global.TotalCycles,
            checked(result.Metrics.Links.Values.Sum(link => link.TotalBitsTransferred)),
            result.Metrics.Components.Count == 0 ? 0 : result.Metrics.Components.Values.Max(component => component.PeakOccupancyBits),
            result.Metrics.Global.AverageUtilization,
            result.Metrics.Global.PeOnlyUtilization,
            result.Metrics.Global.RouterOnlyUtilization,
            Convert.ToDecimal(result.Metrics.Global.TotalEnergy, System.Globalization.CultureInfo.InvariantCulture),
            "cycle_runtime_phase3d_phase7_models",
            result.TraceHash?.Hash ?? "");
    }
}

/// <summary>One factorial paper-scenario row.</summary>
public sealed class Phase8APaperScenarioInput
{
    /// <summary>Creates one immutable comparison input.</summary>
    public Phase8APaperScenarioInput(
        string scenarioId,
        string topologyId,
        bool inNetworkReduction,
        IEnumerable<Phase8AEstimatorTransaction>? estimatorTransactions,
        SimulationResult runtime,
        string numericOutputHash,
        string numericEvidenceSource,
        string topologyEvidenceSource,
        IReadOnlyDictionary<string, string>? frozenWorkload)
    {
        ScenarioId = scenarioId?.Trim() ?? "";
        TopologyId = topologyId?.Trim() ?? "";
        InNetworkReduction = inNetworkReduction;
        EstimatorTransactions = Array.AsReadOnly((estimatorTransactions ?? []).ToArray());
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        NumericOutputHash = numericOutputHash?.Trim() ?? "";
        NumericEvidenceSource = numericEvidenceSource?.Trim() ?? "";
        TopologyEvidenceSource = topologyEvidenceSource?.Trim() ?? "";
        FrozenWorkload = new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(
            (frozenWorkload ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal));
    }

    /// <summary>Gets stable scenario id.</summary>
    public string ScenarioId { get; }
    /// <summary>Gets stable topology id.</summary>
    public string TopologyId { get; }
    /// <summary>Gets whether reduction is explicitly enabled by a registered capability.</summary>
    public bool InNetworkReduction { get; }
    /// <summary>Gets exact analytical transactions.</summary>
    public IReadOnlyList<Phase8AEstimatorTransaction> EstimatorTransactions { get; }
    /// <summary>Gets completed numeric cycle runtime evidence.</summary>
    public SimulationResult Runtime { get; }
    /// <summary>Gets exact numeric output hash.</summary>
    public string NumericOutputHash { get; }
    /// <summary>Gets numeric evidence provenance.</summary>
    public string NumericEvidenceSource { get; }
    /// <summary>Gets topology/route evidence provenance.</summary>
    public string TopologyEvidenceSource { get; }
    /// <summary>Gets frozen common workload controls.</summary>
    public IReadOnlyDictionary<string, string> FrozenWorkload { get; }
}

/// <summary>One evaluated paper-scenario row with estimator/runtime columns kept disjoint.</summary>
public sealed record Phase8APaperScenarioRow(
    string ScenarioId,
    string TopologyId,
    bool InNetworkReduction,
    string NumericOutputHash,
    string NumericEvidenceSource,
    string TopologyEvidenceSource,
    Phase8AEstimatorResult Estimator,
    Phase8ARuntimeMetricSnapshot Runtime);

/// <summary>Factorial effect decomposition for one estimator metric.</summary>
public sealed record Phase8AFactorialEffect(decimal TopologyEffect, decimal InNetworkReductionEffect, decimal Interaction);

/// <summary>Validated four-row paper comparison.</summary>
public sealed class Phase8APaperScenarioReport
{
    internal Phase8APaperScenarioReport(
        IReadOnlyList<Phase8APaperScenarioRow> rows,
        IReadOnlyDictionary<string, Phase8AFactorialEffect> effects,
        IReadOnlyDictionary<string, string> frozenWorkload,
        string canonicalHash)
    {
        Rows = rows;
        Effects = new ReadOnlyDictionary<string, Phase8AFactorialEffect>(new SortedDictionary<string, Phase8AFactorialEffect>(
            effects.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal));
        FrozenWorkload = new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(
            frozenWorkload.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal));
        CanonicalHash = canonicalHash;
    }
    /// <summary>Gets stable factorial rows.</summary>
    public IReadOnlyList<Phase8APaperScenarioRow> Rows { get; }
    /// <summary>Gets topology, INR, and interaction effects by metric.</summary>
    public IReadOnlyDictionary<string, Phase8AFactorialEffect> Effects { get; }
    /// <summary>Gets exact common workload controls.</summary>
    public IReadOnlyDictionary<string, string> FrozenWorkload { get; }
    /// <summary>Gets deterministic comparison hash.</summary>
    public string CanonicalHash { get; }
}

/// <summary>Structured paper-scenario comparison issue.</summary>
public sealed record Phase8APaperScenarioIssue(string Code, string Message, string RelatedId = "");

/// <summary>Result of the four-row factorial comparison.</summary>
public sealed class Phase8APaperScenarioEvaluationResult
{
    /// <summary>Gets whether a complete report was produced.</summary>
    public bool IsSuccess => Report is not null && Issues.Count == 0;
    /// <summary>Gets the report.</summary>
    public Phase8APaperScenarioReport? Report { get; init; }
    /// <summary>Gets structured comparison issues.</summary>
    public IReadOnlyList<Phase8APaperScenarioIssue> Issues { get; init; } = [];
}

/// <summary>Builds the required Flat/MoT by NoINR/INR factorial comparison.</summary>
public static class Phase8APaperScenarioEvaluator
{
    /// <summary>Evaluates exactly four controlled rows and decomposes estimator effects.</summary>
    public static Phase8APaperScenarioEvaluationResult Evaluate(IEnumerable<Phase8APaperScenarioInput>? scenarios, int wordBits = 32)
    {
        var supplied = (scenarios ?? []).ToArray();
        if (supplied.Any(input => input is null))
            return Failed([new Phase8APaperScenarioIssue("PaperScenarioNull", "Paper scenario inputs cannot contain null entries.")]);
        var inputs = supplied.OrderBy(input => input.ScenarioId, StringComparer.Ordinal).ToList();
        var issues = new List<Phase8APaperScenarioIssue>();
        if (inputs.Count != 4) issues.Add(new("PaperScenarioCountInvalid", "Exactly four factorial rows are required."));
        if (inputs.Any(input => string.IsNullOrWhiteSpace(input.ScenarioId)) ||
            inputs.Select(input => input.ScenarioId).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
            issues.Add(new("PaperScenarioIdInvalid", "Scenario ids must be non-empty and unique."));
        var requiredTopologies = new[]
        {
            ReferenceMappingTopologyIds.Flat2DMeshV1,
            ReferenceMappingTopologyIds.MeshOfTreesV1
        };
        var actualTopologies = inputs.Select(input => input.TopologyId).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!actualTopologies.SequenceEqual(requiredTopologies.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            issues.Add(new("PaperScenarioTopologyInvalid", "Comparison requires the exact ordinary Flat 2D Mesh and Mesh-of-Trees topology ids."));
        var cells = inputs.GroupBy(input => (input.TopologyId, input.InNetworkReduction)).ToList();
        if (cells.Count != 4 || cells.Any(cell => cell.Count() != 1))
            issues.Add(new("PaperScenarioFactorialInvalid", "Comparison requires exactly one NoINR and one INR cell for each topology."));
        var outputHashes = inputs.Select(input => input.NumericOutputHash).Distinct(StringComparer.Ordinal).ToList();
        if (outputHashes.Count != 1 || string.IsNullOrWhiteSpace(outputHashes.FirstOrDefault()))
            issues.Add(new("PaperScenarioNumericMismatch", "All four scenarios must have the same non-empty exact numeric output hash."));
        if (inputs.Any(input => !IsLowerSha256(input.NumericOutputHash)))
            issues.Add(new("PaperScenarioNumericHashInvalid", "Every numeric output hash must be a lowercase SHA-256 digest."));
        if (inputs.Any(input => !input.Runtime.Completed || string.IsNullOrWhiteSpace(input.Runtime.TraceHash?.Hash) ||
            string.IsNullOrWhiteSpace(input.NumericEvidenceSource) || string.IsNullOrWhiteSpace(input.TopologyEvidenceSource)))
            issues.Add(new("PaperScenarioEvidenceIncomplete", "Every row requires completed runtime, trace hash, numeric provenance, and topology provenance."));
        if (inputs.Any(input => input.FrozenWorkload.Count == 0 ||
            input.FrozenWorkload.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))))
            issues.Add(new("PaperScenarioControlsInvalid", "Every row requires non-empty explicit frozen workload controls."));
        var workloadHashes = inputs.Select(input => ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(
            JsonSerializer.Serialize(input.FrozenWorkload, HardwareGraphJson.Options)))).Distinct(StringComparer.Ordinal).ToList();
        if (workloadHashes.Count != 1) issues.Add(new("PaperScenarioControlDrift", "All frozen workload controls must be identical."));

        var rows = new List<Phase8APaperScenarioRow>();
        foreach (var input in inputs)
        {
            var estimate = Phase8AAnalyticalEstimator.Evaluate(input.EstimatorTransactions, wordBits);
            if (!estimate.IsSuccess || estimate.Estimate is null)
            {
                issues.AddRange(estimate.Issues.Select(issue => new Phase8APaperScenarioIssue(issue.Code, issue.Message, input.ScenarioId)));
                continue;
            }
            Phase8ARuntimeMetricSnapshot runtime;
            try
            {
                runtime = Phase8ARuntimeMetricSnapshotBuilder.FromSimulation(input.Runtime);
            }
            catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
            {
                issues.Add(new Phase8APaperScenarioIssue("PaperScenarioRuntimeMetricsInvalid", exception.Message, input.ScenarioId));
                continue;
            }
            rows.Add(new Phase8APaperScenarioRow(
                input.ScenarioId, input.TopologyId, input.InNetworkReduction, input.NumericOutputHash,
                input.NumericEvidenceSource, input.TopologyEvidenceSource, estimate.Estimate, runtime));
        }
        if (issues.Count > 0) return Failed(issues);
        var topologies = requiredTopologies;
        Dictionary<string, Phase8AFactorialEffect> effects;
        try
        {
            effects = new Dictionary<string, Phase8AFactorialEffect>(StringComparer.Ordinal)
            {
                ["contention_free_latency_cycles"] = Effect(rows, topologies, row => row.Estimator.ContentionFreeLatencyCycles),
                ["congestion_adjusted_latency_cycles"] = Effect(rows, topologies, row => row.Estimator.CongestionAdjustedLatencyCycles),
                ["bit_hops"] = Effect(rows, topologies, row => row.Estimator.BitHops),
                ["distance_weighted_bit_um"] = Effect(rows, topologies, row => row.Estimator.DistanceWeightedBitMicrometers)
            };
        }
        catch (OverflowException)
        {
            return Failed([new Phase8APaperScenarioIssue("PaperScenarioEffectArithmeticOverflow", "Factorial effect arithmetic exceeded supported decimal bounds.")]);
        }
        var canonical = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
        {
            Rows = rows.Select(row => new
            {
                row.ScenarioId, row.TopologyId, row.InNetworkReduction, row.NumericOutputHash,
                EstimatorHash = row.Estimator.CanonicalHash,
                RuntimeTraceHash = row.Runtime.TraceHash
            }),
            Effects = effects.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            FrozenWorkload = inputs[0].FrozenWorkload
        }, HardwareGraphJson.Options));
        return new Phase8APaperScenarioEvaluationResult
        {
            Report = new Phase8APaperScenarioReport(
                new ReadOnlyCollection<Phase8APaperScenarioRow>(rows), effects, inputs[0].FrozenWorkload,
                ComponentExecutionJson.ComputeSha256(canonical))
        };
    }

    private static Phase8AFactorialEffect Effect(
        IReadOnlyList<Phase8APaperScenarioRow> rows,
        IReadOnlyList<string> topologies,
        Func<Phase8APaperScenarioRow, decimal> selector)
    {
        decimal Cell(string topology, bool inr) => selector(rows.Single(row =>
            string.Equals(row.TopologyId, topology, StringComparison.Ordinal) && row.InNetworkReduction == inr));
        var flat = topologies[0];
        var mot = topologies[1];
        var topologyEffect = ((Cell(mot, false) - Cell(flat, false)) + (Cell(mot, true) - Cell(flat, true))) / 2m;
        var inrEffect = ((Cell(flat, true) - Cell(flat, false)) + (Cell(mot, true) - Cell(mot, false))) / 2m;
        var interaction = (Cell(mot, true) - Cell(mot, false)) - (Cell(flat, true) - Cell(flat, false));
        return new Phase8AFactorialEffect(topologyEffect, inrEffect, interaction);
    }

    private static bool IsLowerSha256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Phase8APaperScenarioEvaluationResult Failed(IEnumerable<Phase8APaperScenarioIssue> issues) => new()
    {
        Issues = new ReadOnlyCollection<Phase8APaperScenarioIssue>(issues.ToList())
    };
}
