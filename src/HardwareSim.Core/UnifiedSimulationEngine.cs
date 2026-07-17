namespace HardwareSim.Core;

/// <summary>Provides the unified simulation engine service for hardware design and simulation workflows.</summary>
public sealed class UnifiedSimulationEngine
{
    /// <summary>Runs the enclosing simulation engine with the supplied graph and options, returning deterministic trace and metrics data.</summary>
    public SimulationResult Run(HardwareSimulationGraph graph, WorkloadSchedule schedule, SimulationOptions? options = null)
    {
        return new CycleSimulationEngine().Run(graph, schedule, options);
    }
}
