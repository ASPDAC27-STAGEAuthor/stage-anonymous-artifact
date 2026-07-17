using System.Text;

namespace HardwareSim.Core;

/// <summary>Represents precision sweep variant data exchanged by hardware design and simulation workflows.</summary>
public sealed class PrecisionSweepVariant
{
    /// <summary>Gets or sets the precision value carried by the enclosing precision sweep variant contract.</summary>
    public string Precision { get; set; } = "";
    /// <summary>Gets or sets the packet count value carried by the enclosing precision sweep variant contract.</summary>
    public int PacketCount { get; set; }
    /// <summary>Gets or sets whether simulation completed for this precision variant.</summary>
    public bool Completed { get; set; }
    /// <summary>Gets or sets the total cycles value carried by the enclosing precision sweep variant contract.</summary>
    public long TotalCycles { get; set; }
    /// <summary>Gets or sets the packets delivered value carried by the enclosing precision sweep variant contract.</summary>
    public long PacketsDelivered { get; set; }
    /// <summary>Gets or sets the throughput packets per cycle value carried by the enclosing precision sweep variant contract.</summary>
    public double ThroughputPacketsPerCycle { get; set; }
    /// <summary>Gets or sets the total energy value carried by the enclosing precision sweep variant contract.</summary>
    public double TotalEnergy { get; set; }
    /// <summary>Gets or sets the no c energy value carried by the enclosing precision sweep variant contract.</summary>
    public double NoCEnergy { get; set; }
    /// <summary>Gets or sets the compute energy value carried by the enclosing precision sweep variant contract.</summary>
    public double ComputeEnergy { get; set; }
    /// <summary>Gets or sets the score value carried by the enclosing precision sweep variant contract.</summary>
    public double Score { get; set; }
    /// <summary>Gets or sets the bottleneck value carried by the enclosing precision sweep variant contract.</summary>
    public string Bottleneck { get; set; } = "";
}

/// <summary>Represents precision sweep report data exchanged by hardware design and simulation workflows.</summary>
public sealed class PrecisionSweepReport
{
    /// <summary>Gets or sets the report id value carried by the enclosing precision sweep report contract.</summary>
    public string ReportId { get; set; } = "matmul_precision_sweep";
    /// <summary>Gets or sets the workload id value carried by the enclosing precision sweep report contract.</summary>
    public string WorkloadId { get; set; } = "matmul_1x2048_2048x2048";
    /// <summary>Gets or sets the best precision value carried by the enclosing precision sweep report contract.</summary>
    public string BestPrecision { get; set; } = "";
    /// <summary>Gets or sets the best score value carried by the enclosing precision sweep report contract.</summary>
    public double BestScore { get; set; }
    /// <summary>Gets or sets the variants collection carried by the enclosing precision sweep report contract.</summary>
    public List<PrecisionSweepVariant> Variants { get; set; } = [];
}

/// <summary>Provides precision sweep runner operations for hardware design and simulation workflows.</summary>
public static class PrecisionSweepRunner
{
    private static readonly PrecisionKind[] DefaultPrecisions =
    [
        PrecisionKind.FP16,
        PrecisionKind.INT8,
        PrecisionKind.INT4
    ];

    /// <summary>Runs the mat mul precision sweep workflow and returns its deterministic result.</summary>
    public static PrecisionSweepReport RunMatMulPrecisionSweep(SimulationOptions? options = null)
    {
        options ??= new SimulationOptions { MaxCycles = 1000 };
        var report = new PrecisionSweepReport();
        var engine = new CycleSimulationEngine();

        foreach (var precision in DefaultPrecisions)
        {
            var graph = SampleGraphs.CreateMemoryRouterPeReductionSinkGraph();
            var workload = SampleWorkloads.CreateMatMul1By2048x2048(precision);
            var mapping = SampleWorkloads.MapMatMulToPe("pe0");
            var compiled = LegacyWorkloadGraphPreparation.Prepare(graph, workload, mapping);
            var source = compiled.Graph.Components.First(c => c.Type == ComponentKind.WorkloadSource);
            var simulationCompilation = new SimulationGraphCompiler().CompileHardware(compiled.Graph);
            if (!simulationCompilation.IsSuccess)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, simulationCompilation.Errors.Select(error => error.Message)));
            }

            var result = engine.Run(simulationCompilation.Graph!, options);
            var variant = Summarize(precision, source.GetIntParameter("packet_count", 0), result);
            report.Variants.Add(variant);
        }

        var best = report.Variants
            .Where(v => v.Completed)
            .OrderBy(v => v.Score)
            .ThenBy(v => v.TotalEnergy)
            .FirstOrDefault()
            ?? report.Variants.OrderBy(v => v.Score).First();
        report.BestPrecision = best.Precision;
        report.BestScore = best.Score;
        return report;
    }

    /// <summary>Builds and returns the render markdown text representation from the supplied inputs.</summary>
    public static string RenderMarkdown(PrecisionSweepReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MatMul Precision Sweep");
        builder.AppendLine();
        builder.AppendLine($"Best precision: {report.BestPrecision} (score {report.BestScore:0.###})");
        builder.AppendLine();
        builder.AppendLine("| Precision | Packets | Completed | Cycles | Delivered | Throughput | Total Energy | NoC Energy | Bottleneck |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (var variant in report.Variants)
        {
            builder.AppendLine(
                $"| {variant.Precision} | {variant.PacketCount} | {variant.Completed} | {variant.TotalCycles} | {variant.PacketsDelivered} | {variant.ThroughputPacketsPerCycle:0.###} | {variant.TotalEnergy:0.###} | {variant.NoCEnergy:0.###} | {Escape(variant.Bottleneck)} |");
        }

        return builder.ToString();
    }

    private static PrecisionSweepVariant Summarize(PrecisionKind precision, int packetCount, SimulationResult result)
    {
        var score = result.Completed
            ? result.Metrics.Global.TotalCycles
            : result.Metrics.Global.TotalCycles * 10.0 + Math.Max(0, packetCount - result.Metrics.Global.PacketsDelivered);

        return new PrecisionSweepVariant
        {
            Precision = precision.ToString(),
            PacketCount = packetCount,
            Completed = result.Completed,
            TotalCycles = result.Metrics.Global.TotalCycles,
            PacketsDelivered = result.Metrics.Global.PacketsDelivered,
            ThroughputPacketsPerCycle = result.Metrics.Global.AverageThroughputPacketsPerCycle,
            TotalEnergy = result.Metrics.Global.TotalEnergy,
            NoCEnergy = result.Metrics.Global.NoCEnergy,
            ComputeEnergy = result.Metrics.Global.ComputeEnergy,
            Score = score,
            Bottleneck = result.BottleneckReport.MainBottleneck
        };
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
