using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

#pragma warning disable CS1591 // Contract names intentionally expose exact metric meanings.

public sealed record MetricCrossValidationIssue(string Code, string Message);

public sealed class MetricCrossValidationResult
{
    internal MetricCrossValidationResult(
        CycleCount totalCycles,
        AggregatedEnergy energy,
        AreaAggregationResult area,
        UtilizationAggregationResult utilization,
        IReadOnlyList<BufferOccupancySummary> buffers,
        IReadOnlyList<LinkCycleSummary> links,
        IReadOnlyList<MetricCrossValidationIssue> issues)
    {
        TotalCycles = totalCycles;
        Energy = energy;
        Area = area;
        Utilization = utilization;
        Buffers = buffers;
        Links = links;
        Issues = issues;
    }

    public CycleCount TotalCycles { get; }
    public AggregatedEnergy Energy { get; }
    public AreaAggregationResult Area { get; }
    public UtilizationAggregationResult Utilization { get; }
    public IReadOnlyList<BufferOccupancySummary> Buffers { get; }
    public IReadOnlyList<LinkCycleSummary> Links { get; }
    public IReadOnlyList<MetricCrossValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
    public string CanonicalHash => MetricCanonicalHasher.Compute(this);
}

public static class MetricCrossValidation
{
    public static MetricCrossValidationResult Validate(
        SimulationMetrics metrics,
        HardwareSimulationGraph graph,
        IEnumerable<EnergyContribution> energyContributions,
        IEnumerable<ComponentActivitySample> activitySamples,
        IEnumerable<BufferOccupancySummary>? buffers = null,
        IEnumerable<LinkCycleSummary>? links = null,
        double tolerance = EnergyAggregation.DefaultTolerancePJ)
    {
        if (metrics is null)
        {
            throw new ArgumentNullException(nameof(metrics));
        }

        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (energyContributions is null)
        {
            throw new ArgumentNullException(nameof(energyContributions));
        }

        if (activitySamples is null)
        {
            throw new ArgumentNullException(nameof(activitySamples));
        }

        MetricUnitGuard.NonNegativeFinite(tolerance, nameof(tolerance));

        var issues = new List<MetricCrossValidationIssue>();
        var contributions = energyContributions.ToList();
        var energy = EnergyAggregation.Aggregate(contributions);
        var area = AreaAggregation.Aggregate(graph);
        var utilization = UtilizationAggregation.Aggregate(activitySamples);
        var bufferSummaries = (buffers ?? Enumerable.Empty<BufferOccupancySummary>())
            .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        var linkSummaries = (links ?? Enumerable.Empty<LinkCycleSummary>())
            .OrderBy(item => item.LinkId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        var totalCycles = CrossValidateCycles(metrics, utilization, issues);
        CrossValidateEnergy(metrics, contributions, energy, issues, tolerance);
        CrossValidateArea(metrics, area, issues, tolerance);
        CrossValidateUtilization(metrics, utilization, issues, tolerance);
        CrossValidateBuffers(metrics, bufferSummaries, issues, tolerance);
        CrossValidateLinks(metrics, linkSummaries, totalCycles, issues);

        return new MetricCrossValidationResult(
            totalCycles,
            energy,
            area,
            utilization,
            bufferSummaries,
            linkSummaries,
            issues.AsReadOnly());
    }

    private static CycleCount CrossValidateCycles(
        SimulationMetrics metrics,
        UtilizationAggregationResult utilization,
        List<MetricCrossValidationIssue> issues)
    {
        var totals = utilization.Components
            .Select(item => item.TotalCycles.Value)
            .Distinct()
            .OrderBy(item => item)
            .ToList();
        if (totals.Count > 1)
        {
            issues.Add(new MetricCrossValidationIssue(
                "P3D.Cycles.ComponentTotalsMismatch",
                "Component utilization samples do not agree on one global cycle count."));
        }

        var expected = totals.Count == 0 ? metrics.Global.TotalCycles : totals[^1];
        CompareLong(issues, "P3D.Cycles.GlobalMismatch", "global total cycles", expected, metrics.Global.TotalCycles);

        foreach (var component in utilization.Components)
        {
            if (!metrics.Components.TryGetValue(component.ComponentId, out var componentMetrics))
            {
                issues.Add(new MetricCrossValidationIssue(
                    "P3D.Cycles.MissingComponentMetrics",
                    $"Component '{component.ComponentId}' has utilization samples but no component metrics."));
                continue;
            }

            CompareLong(issues, "P3D.Cycles.ActiveMismatch", $"{component.ComponentId} active cycles", component.ActiveCycles.Value, componentMetrics.ActiveCycles);
            CompareLong(issues, "P3D.Cycles.IdleMismatch", $"{component.ComponentId} idle cycles", component.IdleCycles.Value, componentMetrics.IdleCycles);
            CompareLong(issues, "P3D.Cycles.StallMismatch", $"{component.ComponentId} stall cycles", component.StallCycles.Value, componentMetrics.StallCycles);
            CompareLong(issues, "P3D.Cycles.ComponentTotalMismatch", $"{component.ComponentId} total cycles", component.TotalCycles.Value, componentMetrics.TotalCycleCount.Value);
        }

        return new CycleCount(expected);
    }

    private static void CrossValidateEnergy(
        SimulationMetrics metrics,
        IReadOnlyList<EnergyContribution> contributions,
        AggregatedEnergy energy,
        List<MetricCrossValidationIssue> issues,
        double tolerance)
    {
        if (!energy.IsConsistent(tolerance))
        {
            issues.Add(new MetricCrossValidationIssue(
                "P3D.Energy.ClassificationTotalsMismatch",
                "Energy kind and category totals do not match."));
        }

        CompareDouble(issues, "P3D.Energy.TotalMismatch", "global total energy", energy.TotalPJ.Value, metrics.Global.TotalEnergy, tolerance);
        CompareDouble(issues, "P3D.Energy.ComputeMismatch", "global compute energy", energy.ByCategory.Compute.Value, metrics.Global.ComputeEnergy, tolerance);
        CompareDouble(issues, "P3D.Energy.NoCMismatch", "global NoC energy", energy.ByCategory.NoC.Value, metrics.Global.NoCEnergy, tolerance);
        CompareDouble(issues, "P3D.Energy.ConversionMismatch", "global conversion energy", energy.ByCategory.Conversion.Value, metrics.Global.ConversionEnergy, tolerance);
        CompareDouble(issues, "P3D.Energy.OpticalMismatch", "global optical energy", energy.ByCategory.Optical.Value, metrics.Global.OpticalEnergy, tolerance);
        CompareEnergyCategoryBreakdown(issues, energy.ByCategory, metrics.Global.EnergyByCategory, tolerance);

        foreach (var group in contributions.GroupBy(item => item.ComponentId, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var expected = group.Sum(item => item.Amount.Value);
            if (metrics.Components.TryGetValue(group.Key, out var componentMetrics))
            {
                CompareDouble(issues, "P3D.Energy.ComponentMismatch", $"{group.Key} component energy", expected, componentMetrics.Energy, tolerance);
                CompareDouble(issues, "P3D.Energy.ComponentBreakdownMismatch", $"{group.Key} component energy breakdown", expected, componentMetrics.EnergyBreakdown.TotalPJ.Value, tolerance);
            }
            else if (metrics.Links.TryGetValue(group.Key, out var linkMetrics))
            {
                CompareDouble(issues, "P3D.Energy.LinkMismatch", $"{group.Key} link energy", expected, linkMetrics.Energy, tolerance);
                CompareDouble(issues, "P3D.Energy.LinkBreakdownMismatch", $"{group.Key} link energy breakdown", expected, linkMetrics.EnergyBreakdown.TotalPJ.Value, tolerance);
            }
            else
            {
                issues.Add(new MetricCrossValidationIssue(
                    "P3D.Energy.MissingMetricTarget",
                    $"Energy contribution target '{group.Key}' has neither component nor link metrics."));
            }
        }
    }

    private static void CrossValidateArea(
        SimulationMetrics metrics,
        AreaAggregationResult area,
        List<MetricCrossValidationIssue> issues,
        double tolerance)
    {
        CompareDouble(issues, "P3D.Area.TotalMismatch", "global total area", area.TotalAreaUm2.Value, metrics.Global.TotalAreaUm2.Value, tolerance);
        foreach (var component in area.Components)
        {
            if (!metrics.Components.TryGetValue(component.ComponentId, out var componentMetrics))
            {
                issues.Add(new MetricCrossValidationIssue(
                    "P3D.Area.MissingComponentMetrics",
                    $"Physical component '{component.ComponentId}' has area but no component metrics."));
                continue;
            }

            CompareDouble(issues, "P3D.Area.ComponentMismatch", $"{component.ComponentId} area", component.AreaUm2.Value, componentMetrics.AreaUm2.Value, tolerance);
        }
    }

    private static void CrossValidateUtilization(
        SimulationMetrics metrics,
        UtilizationAggregationResult utilization,
        List<MetricCrossValidationIssue> issues,
        double tolerance)
    {
        CompareDouble(issues, "P3D.Utilization.AverageMismatch", "average utilization", utilization.Global.Average, metrics.Global.AverageUtilization, tolerance);
        CompareDouble(issues, "P3D.Utilization.AreaWeightedMismatch", "area-weighted utilization", utilization.Global.AreaWeighted, metrics.Global.AreaWeightedUtilization, tolerance);
        CompareDouble(issues, "P3D.Utilization.PeOnlyMismatch", "PE-only utilization", utilization.Global.PeOnly, metrics.Global.PeOnlyUtilization, tolerance);
        CompareDouble(issues, "P3D.Utilization.RouterOnlyMismatch", "router-only utilization", utilization.Global.RouterOnly, metrics.Global.RouterOnlyUtilization, tolerance);
    }

    private static void CrossValidateBuffers(
        SimulationMetrics metrics,
        IReadOnlyList<BufferOccupancySummary> buffers,
        List<MetricCrossValidationIssue> issues,
        double tolerance)
    {
        foreach (var buffer in buffers)
        {
            if (!metrics.Components.TryGetValue(buffer.ComponentId, out var componentMetrics))
            {
                issues.Add(new MetricCrossValidationIssue(
                    "P3D.Buffer.MissingComponentMetrics",
                    $"Buffer '{buffer.ComponentId}' has occupancy summary but no component metrics."));
                continue;
            }

            CompareDouble(issues, "P3D.Buffer.AverageMismatch", $"{buffer.ComponentId} average occupancy", buffer.AverageOccupancyBits, componentMetrics.AverageOccupancyBits, tolerance);
            CompareLong(issues, "P3D.Buffer.PeakMismatch", $"{buffer.ComponentId} peak occupancy", buffer.PeakOccupancyBits.Value, componentMetrics.PeakOccupancyBits);
            CompareLong(issues, "P3D.Buffer.CycleMismatch", $"{buffer.ComponentId} occupancy cycles", buffer.TotalCycles.Value, componentMetrics.TotalCycleCount.Value);
        }
    }

    private static void CrossValidateLinks(
        SimulationMetrics metrics,
        IReadOnlyList<LinkCycleSummary> links,
        CycleCount totalCycles,
        List<MetricCrossValidationIssue> issues)
    {
        foreach (var link in links)
        {
            if (!metrics.Links.TryGetValue(link.LinkId, out var linkMetrics))
            {
                issues.Add(new MetricCrossValidationIssue(
                    "P3D.Link.MissingLinkMetrics",
                    $"Link '{link.LinkId}' has cycle summary but no link metrics."));
                continue;
            }

            CompareLong(issues, "P3D.Link.ActiveMismatch", $"{link.LinkId} active cycles", link.ActiveCycles.Value, linkMetrics.BusyCycles);
            CompareLong(issues, "P3D.Link.TotalMismatch", $"{link.LinkId} total cycles", link.TotalCycles.Value, linkMetrics.TotalCycles);
            CompareLong(issues, "P3D.Link.GlobalTotalMismatch", $"{link.LinkId} total cycles versus global", totalCycles.Value, link.TotalCycles.Value);
        }
    }

    private static void CompareEnergyCategoryBreakdown(
        List<MetricCrossValidationIssue> issues,
        EnergyCategoryBreakdown expected,
        EnergyCategoryBreakdown actual,
        double tolerance)
    {
        CompareDouble(issues, "P3D.Energy.CategoryComputeMismatch", "category compute energy", expected.Compute.Value, actual.Compute.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryMemoryMismatch", "category memory energy", expected.Memory.Value, actual.Memory.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryNoCMismatch", "category NoC energy", expected.NoC.Value, actual.NoC.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryConversionMismatch", "category conversion energy", expected.Conversion.Value, actual.Conversion.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryOpticalMismatch", "category optical energy", expected.Optical.Value, actual.Optical.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryCimMismatch", "category CIM energy", expected.Cim.Value, actual.Cim.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryLeakageMismatch", "category leakage energy", expected.Leakage.Value, actual.Leakage.Value, tolerance);
        CompareDouble(issues, "P3D.Energy.CategoryTotalMismatch", "category total energy", expected.TotalPJ.Value, actual.TotalPJ.Value, tolerance);
    }

    private static void CompareLong(List<MetricCrossValidationIssue> issues, string code, string label, long expected, long actual)
    {
        if (expected != actual)
        {
            issues.Add(new MetricCrossValidationIssue(code, $"{label}: expected {expected}, actual {actual}."));
        }
    }

    private static void CompareDouble(List<MetricCrossValidationIssue> issues, string code, string label, double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            issues.Add(new MetricCrossValidationIssue(
                code,
                string.Format(CultureInfo.InvariantCulture, "{0}: expected {1:R}, actual {2:R}, tolerance {3:R}.", label, expected, actual, tolerance)));
        }
    }
}

public static class MetricCanonicalHasher
{
    public static string Compute(MetricCrossValidationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var builder = new StringBuilder();
        AppendLine(builder, "cycles", result.TotalCycles.Value);
        AppendLine(builder, "energy.total", result.Energy.TotalPJ.Value);
        AppendLine(builder, "energy.kind.dynamic", result.Energy.ByKind.Dynamic.Value);
        AppendLine(builder, "energy.kind.static", result.Energy.ByKind.Static.Value);
        AppendLine(builder, "energy.kind.leakage", result.Energy.ByKind.Leakage.Value);
        AppendLine(builder, "energy.kind.conversion", result.Energy.ByKind.Conversion.Value);
        AppendLine(builder, "energy.kind.tuning", result.Energy.ByKind.Tuning.Value);
        AppendLine(builder, "energy.kind.calibration", result.Energy.ByKind.Calibration.Value);
        AppendLine(builder, "energy.category.compute", result.Energy.ByCategory.Compute.Value);
        AppendLine(builder, "energy.category.memory", result.Energy.ByCategory.Memory.Value);
        AppendLine(builder, "energy.category.noc", result.Energy.ByCategory.NoC.Value);
        AppendLine(builder, "energy.category.conversion", result.Energy.ByCategory.Conversion.Value);
        AppendLine(builder, "energy.category.optical", result.Energy.ByCategory.Optical.Value);
        AppendLine(builder, "energy.category.cim", result.Energy.ByCategory.Cim.Value);
        AppendLine(builder, "energy.category.leakage", result.Energy.ByCategory.Leakage.Value);
        AppendLine(builder, "area.total", result.Area.TotalAreaUm2.Value);
        foreach (var component in result.Area.Components.OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            AppendLine(builder, $"area.component.{component.ComponentId}.{component.ComponentKind}.{component.Source}", component.AreaUm2.Value);
        }

        AppendLine(builder, "util.average", result.Utilization.Global.Average);
        AppendLine(builder, "util.area_weighted", result.Utilization.Global.AreaWeighted);
        AppendLine(builder, "util.pe_only", result.Utilization.Global.PeOnly);
        AppendLine(builder, "util.router_only", result.Utilization.Global.RouterOnly);
        foreach (var component in result.Utilization.Components.OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            AppendLine(builder, $"util.component.{component.ComponentId}.active", component.ActiveCycles.Value);
            AppendLine(builder, $"util.component.{component.ComponentId}.idle", component.IdleCycles.Value);
            AppendLine(builder, $"util.component.{component.ComponentId}.stall", component.StallCycles.Value);
        }

        foreach (var buffer in result.Buffers.OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            AppendLine(builder, $"buffer.{buffer.ComponentId}.average", buffer.AverageOccupancyBits);
            AppendLine(builder, $"buffer.{buffer.ComponentId}.peak", buffer.PeakOccupancyBits.Value);
            AppendLine(builder, $"buffer.{buffer.ComponentId}.cycles", buffer.TotalCycles.Value);
        }

        foreach (var link in result.Links.OrderBy(item => item.LinkId, StringComparer.Ordinal))
        {
            AppendLine(builder, $"link.{link.LinkId}.active", link.ActiveCycles.Value);
            AppendLine(builder, $"link.{link.LinkId}.cycles", link.TotalCycles.Value);
        }

        foreach (var issue in result.Issues.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Message, StringComparer.Ordinal))
        {
            builder.Append("issue|").Append(issue.Code).Append('|').Append(issue.Message).Append('\n');
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private static void AppendLine(StringBuilder builder, string key, long value) =>
        builder.Append(key).Append('|').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void AppendLine(StringBuilder builder, string key, double value) =>
        builder.Append(key).Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
}