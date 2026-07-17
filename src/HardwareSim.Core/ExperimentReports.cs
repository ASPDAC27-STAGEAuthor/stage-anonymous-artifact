using System.Text;

namespace HardwareSim.Core;

/// <summary>Represents experiment case summary data exchanged by hardware design and simulation workflows.</summary>
public sealed class ExperimentCaseSummary
{
    /// <summary>Gets or sets the case id value carried by the enclosing experiment case summary contract.</summary>
    public string CaseId { get; set; } = "";
    /// <summary>Gets or sets the name value carried by the enclosing experiment case summary contract.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets whether the experiment case reached its configured completion condition.</summary>
    public bool Completed { get; set; }
    /// <summary>Gets or sets the total cycles value carried by the enclosing experiment case summary contract.</summary>
    public long TotalCycles { get; set; }
    /// <summary>Gets or sets the packets delivered value carried by the enclosing experiment case summary contract.</summary>
    public long PacketsDelivered { get; set; }
    /// <summary>Gets or sets the throughput packets per cycle value carried by the enclosing experiment case summary contract.</summary>
    public double ThroughputPacketsPerCycle { get; set; }
    /// <summary>Gets or sets the busiest link id value carried by the enclosing experiment case summary contract.</summary>
    public string BusiestLinkId { get; set; } = "";
    /// <summary>Gets or sets the busiest link bits value carried by the enclosing experiment case summary contract.</summary>
    public long BusiestLinkBits { get; set; }
    /// <summary>Gets or sets the most congested link id value carried by the enclosing experiment case summary contract.</summary>
    public string MostCongestedLinkId { get; set; } = "";
    /// <summary>Gets or sets the most congested link cycles value carried by the enclosing experiment case summary contract.</summary>
    public long MostCongestedLinkCycles { get; set; }
    /// <summary>Gets or sets the top stalled component id value carried by the enclosing experiment case summary contract.</summary>
    public string TopStalledComponentId { get; set; } = "";
    /// <summary>Gets or sets the top stalled component cycles value carried by the enclosing experiment case summary contract.</summary>
    public long TopStalledComponentCycles { get; set; }
    /// <summary>Gets or sets the dominant stall reason value carried by the enclosing experiment case summary contract.</summary>
    public string DominantStallReason { get; set; } = "";
    /// <summary>Gets or sets the dominant stall cycles value carried by the enclosing experiment case summary contract.</summary>
    public long DominantStallCycles { get; set; }
    /// <summary>Gets or sets the bottleneck value carried by the enclosing experiment case summary contract.</summary>
    public string Bottleneck { get; set; } = "";
    /// <summary>Gets or sets the cause value carried by the enclosing experiment case summary contract.</summary>
    public string Cause { get; set; } = "";
    /// <summary>Gets or sets the suggested fix value carried by the enclosing experiment case summary contract.</summary>
    public string SuggestedFix { get; set; } = "";
}

/// <summary>Represents experiment report data exchanged by hardware design and simulation workflows.</summary>
public sealed class ExperimentReport
{
    /// <summary>Gets or sets the report id value carried by the enclosing experiment report contract.</summary>
    public string ReportId { get; set; } = "sample_case_study";
    /// <summary>Gets or sets the title value carried by the enclosing experiment report contract.</summary>
    public string Title { get; set; } = "Sample Hardware Simulation Case Study";
    /// <summary>Gets or sets the cases collection carried by the enclosing experiment report contract.</summary>
    public List<ExperimentCaseSummary> Cases { get; set; } = [];
}

/// <summary>Provides experiment runner operations for hardware design and simulation workflows.</summary>
public static class ExperimentRunner
{
    /// <summary>Runs the default case study workflow and returns its deterministic result.</summary>
    public static ExperimentReport RunDefaultCaseStudy(SimulationOptions? options = null)
    {
        options ??= new SimulationOptions { MaxCycles = 500 };
        var engine = new CycleSimulationEngine();
        var cases = new (string Id, string Name, HardwareGraph Graph)[]
        {
            ("baseline_mvp", "Baseline MVP flow", SampleGraphs.CreateMemoryRouterPeReductionSinkGraph()),
            ("shared_router_contention", "Shared-router contention", SampleGraphs.CreateContendedSharedRouterGraph()),
            ("router_arbitration", "Router arbitration", SampleGraphs.CreateRouterArbitrationGraph()),
            ("memory_contention", "Memory contention", SampleGraphs.CreateMemoryContentionGraph()),
            ("multi_pe_shared_reduction", "Multi-PE shared reduction", SampleGraphs.CreateMultiPeSharedReductionGraph())
        };

        var report = new ExperimentReport();
        foreach (var experimentCase in cases)
        {
            var compilation = new SimulationGraphCompiler().CompileHardware(experimentCase.Graph);
            if (!compilation.IsSuccess)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Errors.Select(error => error.Message)));
            }

            var result = engine.Run(compilation.Graph!, options);
            report.Cases.Add(Summarize(experimentCase.Id, experimentCase.Name, result));
        }

        return report;
    }

    /// <summary>Builds and returns the render markdown text representation from the supplied inputs.</summary>
    public static string RenderMarkdown(ExperimentReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {report.Title}");
        builder.AppendLine();
        builder.AppendLine("| Case | Completed | Cycles | Delivered | Throughput | Busiest Link | Congested Link | Top Stall | Bottleneck |");
        builder.AppendLine("|---|---:|---:|---:|---:|---|---|---|---|");

        foreach (var c in report.Cases)
        {
            var congestedLink = string.IsNullOrWhiteSpace(c.MostCongestedLinkId)
                ? "-"
                : $"{c.MostCongestedLinkId} ({c.MostCongestedLinkCycles})";
            var stalledComponent = string.IsNullOrWhiteSpace(c.TopStalledComponentId)
                ? "-"
                : $"{c.TopStalledComponentId} ({c.TopStalledComponentCycles}, {c.DominantStallReason})";
            builder.AppendLine(
                $"| {Escape(c.Name)} | {c.Completed} | {c.TotalCycles} | {c.PacketsDelivered} | {c.ThroughputPacketsPerCycle:0.###} | {Escape(c.BusiestLinkId)} ({c.BusiestLinkBits}) | {Escape(congestedLink)} | {Escape(stalledComponent)} | {Escape(c.Bottleneck)} |");
        }

        return builder.ToString();
    }

    private static ExperimentCaseSummary Summarize(string caseId, string name, SimulationResult result)
    {
        var busiestLink = result.Metrics.Links.Values
            .OrderByDescending(l => l.TotalBitsTransferred)
            .FirstOrDefault();
        var congestedLink = result.Metrics.Links.Values
            .OrderByDescending(l => l.CongestionCycles)
            .FirstOrDefault(l => l.CongestionCycles > 0);
        var stalledComponent = result.Metrics.Components.Values
            .OrderByDescending(c => c.StallCycles)
            .FirstOrDefault(c => c.StallCycles > 0);
        var dominantStall = stalledComponent?.StallCyclesByReason
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault();

        return new ExperimentCaseSummary
        {
            CaseId = caseId,
            Name = name,
            Completed = result.Completed,
            TotalCycles = result.Metrics.Global.TotalCycles,
            PacketsDelivered = result.Metrics.Global.PacketsDelivered,
            ThroughputPacketsPerCycle = result.Metrics.Global.AverageThroughputPacketsPerCycle,
            BusiestLinkId = busiestLink?.LinkId ?? "",
            BusiestLinkBits = busiestLink?.TotalBitsTransferred ?? 0,
            MostCongestedLinkId = congestedLink?.LinkId ?? "",
            MostCongestedLinkCycles = congestedLink?.CongestionCycles ?? 0,
            TopStalledComponentId = stalledComponent?.ComponentId ?? "",
            TopStalledComponentCycles = stalledComponent?.StallCycles ?? 0,
            DominantStallReason = dominantStall?.Key.ToString() ?? "",
            DominantStallCycles = dominantStall?.Value ?? 0,
            Bottleneck = result.BottleneckReport.MainBottleneck,
            Cause = result.BottleneckReport.Cause,
            SuggestedFix = result.BottleneckReport.SuggestedFix
        };
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
