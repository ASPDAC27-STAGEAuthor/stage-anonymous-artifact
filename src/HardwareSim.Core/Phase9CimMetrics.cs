#pragma warning disable CS1591

using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

public enum Phase9MetricCategory { Energy, Area }

public sealed record Phase9MetricContribution(
    string AttributionKey,
    Phase9MetricCategory Category,
    string OperationOrBlock,
    double? Value,
    string Units,
    string Scope,
    NormalizedDeviceEvidenceStatus EvidenceStatus,
    string ProfileHash,
    IReadOnlyList<string> SourceRecordIds,
    string ModelVersion,
    string Formula,
    string Uncertainty,
    bool Required);

public sealed record Phase9FootprintMetricSnapshot(
    double? AreaUm2,
    double? WidthUm,
    double? HeightUm,
    string Scope,
    string SourceKind,
    string EvidenceStatus,
    string Uncertainty,
    string FootprintHash,
    bool Complete);

public sealed record Phase9ErrorMetricSnapshot(
    string Mode,
    long Seed,
    string EffectSet,
    bool EstimatedResearchResult,
    int? SampleCount,
    double? Mean,
    double? StandardDeviation,
    double? ConfidenceInterval95Low,
    double? ConfidenceInterval95High,
    string StatisticsHash);

/// <summary>Cross-checkable Phase 9 report whose complete totals never replace unknown values with zero.</summary>
public sealed class Phase9CimMetricReport
{
    internal Phase9CimMetricReport(
        IEnumerable<Phase9MetricContribution> contributions,
        Phase9FootprintMetricSnapshot footprint,
        Phase9ErrorMetricSnapshot error,
        string reportHash)
    {
        Contributions = new ReadOnlyCollection<Phase9MetricContribution>(contributions.OrderBy(value => value.AttributionKey, StringComparer.Ordinal).ToArray());
        Footprint = footprint;
        Error = error;
        ReportHash = reportHash;
    }

    public IReadOnlyList<Phase9MetricContribution> Contributions { get; }
    public Phase9FootprintMetricSnapshot Footprint { get; }
    public Phase9ErrorMetricSnapshot Error { get; }
    public string ReportHash { get; }
    public double KnownEnergySubtotalPicojoules => Contributions.Where(value => value.Category == Phase9MetricCategory.Energy && value.Value.HasValue).Sum(value => value.Value!.Value);
    public double? CompleteEnergyTotalPicojoules => Contributions.Any(value => value.Category == Phase9MetricCategory.Energy && value.Required && !value.Value.HasValue) ? null : KnownEnergySubtotalPicojoules;
    public double KnownAreaSubtotalUm2 => Contributions.Where(value => value.Category == Phase9MetricCategory.Area && value.Value.HasValue).Sum(value => value.Value!.Value);
    public double? CompleteAreaTotalUm2 => Contributions.Any(value => value.Category == Phase9MetricCategory.Area && value.Required && !value.Value.HasValue) ? null : KnownAreaSubtotalUm2;
    public bool HasDuplicateAttribution => Contributions.GroupBy(value => value.AttributionKey, StringComparer.Ordinal).Any(group => group.Count() > 1);
}

public static class Phase9CimMetricReportBuilder
{
    public static Phase9CimMetricReport Build(
        ComponentTemplate template,
        CompiledComponentProfile profile,
        IEnumerable<Phase9CimOperationResult> arrayOperations,
        IEnumerable<(Phase9ConverterResult Result, Phase9ConverterProfile Profile)> converterOperations,
        Phase9NonIdealResult? nonIdealResult = null,
        Phase9NonIdealStatistics? statistics = null)
    {
        if (template is null || profile is null) throw new ArgumentNullException();
        var contributions = new List<Phase9MetricContribution>();
        foreach (var result in arrayOperations ?? [])
        {
            if (!result.IsSuccess || result.Characterization is null)
                throw new InvalidOperationException("Only successful array operations with frozen characterization can enter metrics.");
            var model = result.Characterization;
            contributions.Add(new Phase9MetricContribution(
                "energy:array:" + result.Operation.ToString().ToLowerInvariant(), Phase9MetricCategory.Energy,
                result.Operation.ToString().ToLowerInvariant(), result.EnergyPicojoules, "pJ", "array",
                model.EvidenceStatus, model.ProfileHash, model.SourceRecordIds, model.ModelVersion,
                model.Formula, model.Uncertainty, true));
        }

        foreach (var (result, converterProfile) in converterOperations ?? [])
        {
            if (!result.IsSuccess) throw new InvalidOperationException("Only successful converter operations can enter metrics.");
            var point = converterProfile.OperatingPoints.SingleOrDefault(value => value.OperatingPointHash == result.OperatingPointHash)
                ?? throw new InvalidOperationException("Converter result does not bind an exact operating point.");
            var operation = result.Kind.ToString().ToLowerInvariant();
            contributions.Add(new Phase9MetricContribution(
                "energy:converter:" + operation, Phase9MetricCategory.Energy, operation,
                result.EnergyPicojoules, "pJ", "core", point.EvidenceStatus, point.ProfileHash,
                point.SourceRecordIds, point.ModelVersion, point.Formula, point.Uncertainty, true));
        }

        foreach (var block in PeripheralBlocks(template))
        {
            var evidence = Evidence(block, "phase9_energy_evidence");
            contributions.Add(new Phase9MetricContribution(
                "energy:peripheral:" + block.Id, Phase9MetricCategory.Energy, block.Id,
                block.EnergyPicojoules > 0 ? block.EnergyPicojoules : null, "pJ", "core",
                evidence.Status, profile.ProfileHash, evidence.SourceRecordIds, "phase9-template-peripheral-v1",
                evidence.Formula, evidence.Uncertainty, true));
        }

        foreach (var block in template.InternalBlocks.Where(value => value.Layer == InternalBlockLayer.Structural).OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            var evidence = Evidence(block, "phase9_area_evidence");
            contributions.Add(new Phase9MetricContribution(
                "area:block:" + block.Id, Phase9MetricCategory.Area, block.Id,
                block.AreaUm2 > 0 ? block.AreaUm2 : null, "um2",
                block.Id == "compute_core" ? PhysicalFootprintScope.Array.ToString() : PhysicalFootprintScope.Core.ToString(),
                evidence.Status, profile.ProfileHash, evidence.SourceRecordIds, "phase9-template-floorplan-v1",
                evidence.Formula, evidence.Uncertainty, true));
        }

        var duplicates = contributions.GroupBy(value => value.AttributionKey, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException("Duplicate Phase 9 attribution keys: " + string.Join(",", duplicates));
        var footprint = Footprint(profile.PhysicalFootprint);
        var error = Error(nonIdealResult, statistics);
        var hash = ComponentTemplateJson.StableHash(new
        {
            profile.ProfileHash,
            Contributions = contributions.OrderBy(value => value.AttributionKey, StringComparer.Ordinal),
            footprint,
            error
        });
        return new Phase9CimMetricReport(contributions, footprint, error, hash);
    }

    private static IEnumerable<InternalBlock> PeripheralBlocks(ComponentTemplate template)
    {
        var ids = new HashSet<string>(new[] { "decoder", "sense_amp", "controller", "input_buffer", "accumulator", "egress" }, StringComparer.Ordinal);
        return template.InternalBlocks.Where(block => block.Layer == InternalBlockLayer.Structural && ids.Contains(block.Id)).OrderBy(block => block.Id, StringComparer.Ordinal);
    }

    private static Phase9FootprintMetricSnapshot Footprint(PhysicalFootprint? footprint) => footprint is null
        ? new(null, null, null, "Unknown", "Unknown", "Unknown", "unknown; no compiled footprint", "", false)
        : new(footprint.AreaUm2, footprint.WidthUm, footprint.HeightUm, footprint.Scope.ToString(), footprint.SourceKind.ToString(),
            footprint.EvidenceStatus.ToString(), footprint.Uncertainty, footprint.FootprintHash, footprint.IsKnown);

    private static Phase9ErrorMetricSnapshot Error(Phase9NonIdealResult? result, Phase9NonIdealStatistics? statistics) => new(
        result?.Tier.ToString() ?? Phase9NonIdealTier.Functional.ToString(), result?.Seed ?? 0,
        result?.Effects.ToString() ?? Phase9NonIdealEffect.None.ToString(), result?.EstimatedResearchResult ?? false,
        statistics?.SampleCount, statistics?.Mean, statistics?.StandardDeviation,
        statistics?.ConfidenceInterval95Low, statistics?.ConfidenceInterval95High, statistics?.StatisticsHash ?? "");

    private static EvidenceProjection Evidence(InternalBlock block, string key)
    {
        if (!block.ExtensionData.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Object)
            return new(NormalizedDeviceEvidenceStatus.Estimated, [], "template-declared contribution", "model-derived; inspect template provenance");
        var statusText = value.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "";
        var evidenceStatus = statusText switch
        {
            var text when text.Contains("reported", StringComparison.OrdinalIgnoreCase) => NormalizedDeviceEvidenceStatus.Reported,
            var text when text.Contains("derived", StringComparison.OrdinalIgnoreCase) => NormalizedDeviceEvidenceStatus.Derived,
            var text when text.Contains("unknown", StringComparison.OrdinalIgnoreCase) => NormalizedDeviceEvidenceStatus.Unknown,
            _ => NormalizedDeviceEvidenceStatus.Estimated
        };
        var records = value.TryGetProperty("source_record_ids", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).OrderBy(item => item, StringComparer.Ordinal).ToArray()
            : [];
        var uncertainty = value.TryGetProperty("uncertainty", out var uncertaintyElement) ? uncertaintyElement.GetString() ?? "unknown" : "unknown";
        return new(evidenceStatus, records, statusText, uncertainty);
    }

    private sealed record EvidenceProjection(NormalizedDeviceEvidenceStatus Status, IReadOnlyList<string> SourceRecordIds, string Formula, string Uncertainty);
}
