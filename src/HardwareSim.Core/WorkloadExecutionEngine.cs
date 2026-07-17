namespace HardwareSim.Core;

/// <summary>Provides the workload execution engine service for hardware design and simulation workflows.</summary>
public sealed class WorkloadExecutionEngine
{
    /// <summary>Runs the enclosing simulation engine with the supplied graph and options, returning deterministic trace and metrics data.</summary>
    public SimulationResult Run(HardwareSimulationGraph graph, WorkloadSchedule schedule)
    {
        var result = new SimulationResult();
        InitializeMetrics(graph, result.Metrics);

        for (long cycle = 0; cycle <= schedule.TotalCycles; cycle++)
        {
            var record = new CycleTraceRecord { Cycle = cycle };
            result.Trace.Cycles.Add(record);

            foreach (var operation in schedule.Operations.Where(o => o.StartCycle == cycle))
            {
                record.Events.Add(new(
                    TraceEventType.OperationStart,
                    ComponentId: operation.ComponentId,
                    Bits: operation.PacketBits * operation.PacketCount,
                    Detail: $"{operation.OperationId}:{operation.OperationKind}"));
            }

            foreach (var operation in schedule.Operations.Where(o => o.StartCycle <= cycle && o.EndCycle > cycle))
            {
                var metrics = result.Metrics.Components[operation.ComponentId];
                metrics.ActiveCycles++;
                metrics.OutputTrafficBits += operation.PacketBits * operation.PacketCount;
                record.Events.Add(new(
                    TraceEventType.Compute,
                    ComponentId: operation.ComponentId,
                    Bits: operation.PacketBits,
                    Detail: $"operation_active:{operation.OperationId}"));
            }

            foreach (var operation in schedule.Operations.Where(o => o.EndCycle == cycle))
            {
                result.Metrics.Global.PacketsDelivered += operation.PacketCount;
                record.Events.Add(new(
                    TraceEventType.OperationComplete,
                    ComponentId: operation.ComponentId,
                    Bits: operation.PacketBits * operation.PacketCount,
                    Detail: $"{operation.OperationId}:{operation.OperationKind}"));
            }

            AccountIdleCycles(graph, schedule, cycle, result.Metrics);
        }

        result.Metrics.Global.TotalCycles = schedule.TotalCycles;
        result.Metrics.Global.PacketsInjected = schedule.Operations.Sum(o => o.PacketCount);
        AccumulateOperationEnergy(graph, schedule, result.Metrics);
        result.Completed = true;
        result.CompletionReason = $"Executed {schedule.Operations.Count} scheduled workload operation(s).";
        result.BottleneckReport = AnalyzeScheduleBottleneck(schedule, result.Metrics);
        return result;
    }

    private static void InitializeMetrics(HardwareSimulationGraph graph, SimulationMetrics metrics)
    {
        foreach (var component in graph.Components)
        {
            metrics.Components[component.Id] = new ComponentMetrics { ComponentId = component.Id };
        }

        foreach (var link in graph.Links)
        {
            metrics.Links[link.Id] = new LinkMetrics { LinkId = link.Id };
        }
    }

    private static void AccountIdleCycles(HardwareSimulationGraph graph, WorkloadSchedule schedule, long cycle, SimulationMetrics metrics)
    {
        foreach (var component in graph.Components)
        {
            var active = schedule.Operations.Any(o => o.ComponentId == component.Id && o.StartCycle <= cycle && o.EndCycle > cycle);
            if (!active)
            {
                metrics.Components[component.Id].IdleCycles++;
            }
        }
    }

    private static void AccumulateOperationEnergy(HardwareSimulationGraph graph, WorkloadSchedule schedule, SimulationMetrics metrics)
    {
        foreach (var operation in schedule.Operations)
        {
            var component = graph.FindComponent(operation.ComponentId);
            if (component is null)
            {
                continue;
            }

            var energyPerCycle = component.GetDoubleParameter("operation_energy_pj_per_cycle", 0.1);
            var energy = (operation.EndCycle - operation.StartCycle) * energyPerCycle;
            metrics.Components[operation.ComponentId].Energy += energy;
            metrics.Global.ComputeEnergy += energy;
            metrics.Global.TotalEnergy += energy;
        }
    }

    private static BottleneckReport AnalyzeScheduleBottleneck(WorkloadSchedule schedule, SimulationMetrics metrics)
    {
        var busiest = metrics.Components.Values
            .OrderByDescending(c => c.ActiveCycles)
            .FirstOrDefault(c => c.ActiveCycles > 0);

        if (busiest is null)
        {
            return new BottleneckReport();
        }

        var operations = schedule.Operations.Where(o => o.ComponentId == busiest.ComponentId).Select(o => o.OperationId);
        return new BottleneckReport
        {
            MainBottleneck = $"Component {busiest.ComponentId} is scheduled active for {busiest.ActiveCycles} cycles.",
            Cause = $"Mapped operations: {string.Join(", ", operations)}.",
            SuggestedFix = "Use a different mapping, add parallel capable components, or split long operations across multiple resources."
        };
    }
}
