namespace HardwareSim.Core;

public sealed partial class CycleSimulationEngine
{
    private SimulationResult RunExecutableDeterministic(
        ExecutableSimulationGraph executable,
        SimulationOptions? options,
        ProjectDirtyState? dirtyState)
    {
        IReadOnlyList<Packet>? exactOperands = executable.InitialPacketExecutionMode == ExecutableInitialPacketExecutionMode.ExactOperands
            ? executable.InitialPackets
            : null;
        var validationIssue = ValidateExecutableExactOperands(executable.HardwareGraph, exactOperands);
        if (validationIssue is not null) return FailureFromIssue(validationIssue);
        return RunDeterministicInternal(
            executable.HardwareGraph,
            exactOperands is null ? executable.Schedule : null,
            options,
            dirtyState,
            runtimeKernelRegistry,
            exactOperands);
    }
    private static SimulationIssue? ValidateExecutableExactOperands(ExecutableSimulationGraph executable)
    {
        IReadOnlyList<Packet>? exactOperands = executable.InitialPacketExecutionMode == ExecutableInitialPacketExecutionMode.ExactOperands
            ? executable.InitialPackets
            : null;
        return ValidateExecutableExactOperands(executable.HardwareGraph, exactOperands);
    }

    private static SimulationIssue? ValidateExecutableExactOperands(
        HardwareSimulationGraph graph,
        IReadOnlyList<Packet>? exactOperands)
    {
        if (exactOperands is null) return null;
        if (exactOperands.Count == 0)
            return ExactOperandIssue("ExecutableExactOperandsMissing", "executable", null, "ExactOperands mode requires at least one executable InitialPackets operand.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packet in exactOperands.OrderBy(packet => packet.InjectionCycle).ThenBy(packet => packet.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(packet.Id))
                return ExactOperandIssue("ExecutablePacketIdRequired", "executable", packet.Id, "Every executable InitialPackets operand requires a stable packet id.");
            if (!ids.Add(packet.Id))
                return ExactOperandIssue("ExecutableDuplicatePacketId", "executable", packet.Id, $"Executable InitialPackets contains duplicate packet id '{packet.Id}'.");
            var source = graph.FindComponent(packet.SourceComponentId);
            if (source is null)
                return ExactOperandIssue("ExecutablePacketSourceMissing", packet.SourceComponentId, packet.Id, $"Executable packet '{packet.Id}' references missing source component '{packet.SourceComponentId}'.");
            var sourcePortExists = graph.Ports.Any(port =>
                string.Equals(port.ComponentId, source.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(port.Name, packet.SourcePort, StringComparison.Ordinal) &&
                port.Direction == PortDirection.Output);
            if (!sourcePortExists)
                return ExactOperandIssue("ExecutablePacketSourcePortMissing", source.Id, packet.Id, $"Executable packet '{packet.Id}' references missing output port '{packet.SourcePort}' on source '{source.Id}'.");
        }
        return null;
    }

    private static SimulationIssue ExactOperandIssue(string code, string componentId, string? packetId, string message) =>
        new(code, "error", 0, componentId, packetId, null, null, message);
}
