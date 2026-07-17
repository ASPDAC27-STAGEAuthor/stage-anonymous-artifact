using System.Text;

namespace HardwareSim.Core;

/// <summary>Represents dse candidate data exchanged by hardware design and simulation workflows.</summary>
public sealed class DseCandidate
{
    /// <summary>Gets or sets the stable identifier used to correlate this design-space candidate.</summary>
    public string CandidateId { get; set; } = "";
    /// <summary>Gets or sets the precision value carried by the enclosing dse candidate contract.</summary>
    public string Precision { get; set; } = "";
    /// <summary>Gets or sets the queue capacity value carried by the enclosing dse candidate contract.</summary>
    public int QueueCapacity { get; set; }
    /// <summary>Gets or sets the packet count value carried by the enclosing dse candidate contract.</summary>
    public int PacketCount { get; set; }
    /// <summary>Gets or sets whether simulation completed for this candidate.</summary>
    public bool Completed { get; set; }
    /// <summary>Gets or sets the total cycles value carried by the enclosing dse candidate contract.</summary>
    public long TotalCycles { get; set; }
    /// <summary>Gets or sets the packets delivered value carried by the enclosing dse candidate contract.</summary>
    public long PacketsDelivered { get; set; }
    /// <summary>Gets or sets the throughput packets per cycle value carried by the enclosing dse candidate contract.</summary>
    public double ThroughputPacketsPerCycle { get; set; }
    /// <summary>Gets or sets the total energy value carried by the enclosing dse candidate contract.</summary>
    public double TotalEnergy { get; set; }
    /// <summary>Gets or sets the congested cycles value carried by the enclosing dse candidate contract.</summary>
    public double CongestedCycles { get; set; }
    /// <summary>Gets or sets the stall cycles value carried by the enclosing dse candidate contract.</summary>
    public double StallCycles { get; set; }
    /// <summary>Gets or sets the score value carried by the enclosing dse candidate contract.</summary>
    public double Score { get; set; }
    /// <summary>Gets or sets the bottleneck value carried by the enclosing dse candidate contract.</summary>
    public string Bottleneck { get; set; } = "";
}

/// <summary>Represents dse report data exchanged by hardware design and simulation workflows.</summary>
public sealed class DseReport
{
    /// <summary>Gets or sets the report id value carried by the enclosing dse report contract.</summary>
    public string ReportId { get; set; } = "bounded_matmul_dse";
    /// <summary>Gets or sets the workload id value carried by the enclosing dse report contract.</summary>
    public string WorkloadId { get; set; } = "matmul_1x2048_2048x2048";
    /// <summary>Gets or sets the objective value carried by the enclosing dse report contract.</summary>
    public string Objective { get; set; } = "Minimize cycles, energy, congestion, and stalls over a bounded deterministic candidate set.";
    /// <summary>Gets or sets the best candidate id value carried by the enclosing dse report contract.</summary>
    public string BestCandidateId { get; set; } = "";
    /// <summary>Gets or sets the best score value carried by the enclosing dse report contract.</summary>
    public double BestScore { get; set; }
    /// <summary>Gets or sets the evaluated candidates included in the design-space report.</summary>
    public List<DseCandidate> Candidates { get; set; } = [];
}

/// <summary>Provides dse runner operations for hardware design and simulation workflows.</summary>
public static class DseRunner
{
    private static readonly PrecisionKind[] Precisions =
    [
        PrecisionKind.FP16,
        PrecisionKind.INT8,
        PrecisionKind.INT4
    ];

    private static readonly int[] QueueCapacities = [1, 4];

    /// <summary>Runs the bounded mat mul dse workflow and returns its deterministic result.</summary>
    public static DseReport RunBoundedMatMulDse(SimulationOptions? options = null)
    {
        options ??= new SimulationOptions { MaxCycles = 1000 };
        var report = new DseReport();
        var engine = new CycleSimulationEngine();

        foreach (var precision in Precisions)
        {
            foreach (var queueCapacity in QueueCapacities)
            {
                var graph = SampleGraphs.CreateMemoryRouterPeReductionSinkGraph();
                ApplyQueueCapacity(graph, queueCapacity);
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

                report.Candidates.Add(Summarize(
                    $"matmul_{precision}_q{queueCapacity}",
                    precision,
                    queueCapacity,
                    source.GetIntParameter("packet_count", 0),
                    result));
            }
        }

        report.Candidates = report.Candidates
            .OrderBy(c => c.Score)
            .ThenBy(c => c.TotalCycles)
            .ThenBy(c => c.TotalEnergy)
            .ThenBy(c => c.CandidateId, StringComparer.Ordinal)
            .ToList();

        var best = report.Candidates.First();
        report.BestCandidateId = best.CandidateId;
        report.BestScore = best.Score;
        return report;
    }

    /// <summary>Builds and returns the render markdown text representation from the supplied inputs.</summary>
    public static string RenderMarkdown(DseReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Bounded MatMul DSE Report");
        builder.AppendLine();
        builder.AppendLine(report.Objective);
        builder.AppendLine();
        builder.AppendLine($"Best candidate: {report.BestCandidateId} (score {report.BestScore:0.###})");
        builder.AppendLine();
        builder.AppendLine("| Rank | Candidate | Precision | Queue | Completed | Cycles | Delivered | Throughput | Energy | Congested Cycles | Stall Cycles | Score | Bottleneck |");
        builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        for (var i = 0; i < report.Candidates.Count; i++)
        {
            var c = report.Candidates[i];
            builder.AppendLine(
                $"| {i + 1} | {Escape(c.CandidateId)} | {c.Precision} | {c.QueueCapacity} | {c.Completed} | {c.TotalCycles} | {c.PacketsDelivered} | {c.ThroughputPacketsPerCycle:0.###} | {c.TotalEnergy:0.###} | {c.CongestedCycles:0.###} | {c.StallCycles:0.###} | {c.Score:0.###} | {Escape(c.Bottleneck)} |");
        }

        return builder.ToString();
    }

    private static void ApplyQueueCapacity(HardwareGraph graph, int queueCapacity)
    {
        foreach (var component in graph.Components.Where(c => c.Type != ComponentKind.WorkloadSink))
        {
            component.Parameters["queue_capacity"] = queueCapacity.ToString();
        }
    }

    private static DseCandidate Summarize(
        string candidateId,
        PrecisionKind precision,
        int queueCapacity,
        int packetCount,
        SimulationResult result)
    {
        var congestedCycles = result.Metrics.Links.Values.Sum(l => l.CongestionCycles);
        var stallCycles = result.Metrics.Components.Values.Sum(c => c.StallCycles);
        var remainingPackets = Math.Max(0, packetCount - result.Metrics.Global.PacketsDelivered);
        var score = result.Completed
            ? result.Metrics.Global.TotalCycles
              + result.Metrics.Global.TotalEnergy * 0.01
              + congestedCycles * 2.0
              + stallCycles * 0.5
            : result.Metrics.Global.TotalCycles * 10.0
              + remainingPackets * 100.0
              + congestedCycles * 5.0
              + stallCycles;

        return new DseCandidate
        {
            CandidateId = candidateId,
            Precision = precision.ToString(),
            QueueCapacity = queueCapacity,
            PacketCount = packetCount,
            Completed = result.Completed,
            TotalCycles = result.Metrics.Global.TotalCycles,
            PacketsDelivered = result.Metrics.Global.PacketsDelivered,
            ThroughputPacketsPerCycle = result.Metrics.Global.AverageThroughputPacketsPerCycle,
            TotalEnergy = result.Metrics.Global.TotalEnergy,
            CongestedCycles = congestedCycles,
            StallCycles = stallCycles,
            Score = score,
            Bottleneck = result.BottleneckReport.MainBottleneck
        };
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
