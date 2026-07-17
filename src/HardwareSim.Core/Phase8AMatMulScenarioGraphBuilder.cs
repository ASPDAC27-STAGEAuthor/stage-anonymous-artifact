namespace HardwareSim.Core;

internal sealed record Phase8AMatMulExecutableScenario(
    HardwareGraph HardwareGraph,
    HardwareSimulationGraph SimulationGraph,
    ComponentRuntimeKernelRegistrySnapshot Kernels,
    WorkloadMappingV2 Mapping,
    ExecutableSimulationGraph Executable,
    IReadOnlyList<Packet> Operands,
    string OperandPlanHash);

internal static class Phase8AMatMulScenarioGraphBuilder
{
    public static (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan) => Phase8AMatMulResolvedRuntimeBuilder.Build(plan);
}
