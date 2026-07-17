namespace HardwareSim.Core;

internal sealed class Phase8AReluKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new Phase8AReluState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        string.Equals(inputPortName, Phase8AReluContract.InputPort, StringComparison.Ordinal) &&
        ((Phase8AReluState)current).Inputs.Count < Math.Max(1, context.Contract.Queues.InputDepth);

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next ReLU states must not alias.");
        if (!string.Equals(input.InputPortName, Phase8AReluContract.InputPort, StringComparison.Ordinal))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };
        if (!Phase8AElementwisePacketValidation.TryValidate(input.Packet, out var payloadError))
            return new ComponentRuntimeKernelInputResult
            {
                Accepted = false,
                Issues = [new("ReluPayloadInvalid", "error", $"Packet '{input.Packet.Id}' is invalid: {payloadError}.", input.Packet.Id)]
            };
        var state = (Phase8AReluState)next;
        if (state.Inputs.Count >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };
        state.Inputs.Add(PacketClone.Clone(input.Packet));
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute,
                $"phase=1;exact_kernel=true;relu_sample;layer={Layer(input.Packet)};invocation={Invocation(input.Packet)}",
                input.Packet.Id, input.Packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next ReLU states must not alias.");
        var currentState = (Phase8AReluState)current;
        var nextState = (Phase8AReluState)next;
        var outputs = new List<ComponentRuntimeKernelOutput>();
        var events = new List<ComponentRuntimeKernelEventFact>();
        var ready = currentState.Results.OrderBy(item => item.ReadyCycle).ThenBy(item => item.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && outputQueueAvailable)
        {
            nextState.Results.RemoveAll(item => item.ReadyCycle == ready.ReadyCycle && string.Equals(item.Packet.Id, ready.Packet.Id, StringComparison.Ordinal));
            outputs.Add(new(Phase8AReluContract.OutputPort, PacketClone.Clone(ready.Packet)));
            events.Add(OutputEvent(context.Cycle, ready.Packet));
        }
        if (currentState.Inputs.Count > 0)
        {
            var input = currentState.Inputs[0];
            var readyCycle = context.Cycle + Math.Max(1, context.Contract.Timing.OperationLatencyCycles) - 1;
            var canEmitImmediately = readyCycle <= context.Cycle && outputQueueAvailable && outputs.Count == 0;
            if (!canEmitImmediately && nextState.Results.Count >= Math.Max(1, context.Contract.Queues.OutputDepth))
                return new ComponentRuntimeKernelAdvanceResult { Outputs = outputs, Events = events };
            nextState.Inputs.RemoveAll(packet => string.Equals(packet.Id, input.Id, StringComparison.Ordinal));
            var result = Execute(context.Component.Id, input);
            events.Add(new(TraceEventType.Compute,
                $"phase=4;exact_kernel=true;relu_issue;layer={Layer(input)};invocation={Invocation(input)};issue_cycle={context.Cycle};ready_cycle={readyCycle}",
                input.Id, input.Bits));
            if (canEmitImmediately)
            {
                outputs.Add(new(Phase8AReluContract.OutputPort, result));
                events.Add(OutputEvent(context.Cycle, result));
            }
            else nextState.Results.Add(new Phase8AElementwisePendingResult(result, readyCycle));
        }
        return new ComponentRuntimeKernelAdvanceResult { Outputs = outputs, Events = events };
    }

    private static Packet Execute(string componentId, Packet input)
    {
        var result = PacketClone.Clone(input);
        result.Id = input.Id + ":relu";
        result.SourceComponentId = componentId;
        result.CurrentComponentId = componentId;
        result.SourcePort = Phase8AReluContract.OutputPort;
        result.Values = input.Values.Select(value => Math.Max(0, value)).ToList();
        result.DependencyIds = input.DependencyIds.Append(input.Id).Distinct(StringComparer.Ordinal).ToList();
        result.Metadata[Phase8AOperandPipelineMetadata.Operation] = "relu";
        Phase8ACollectiveMetadataCodec.ApplyOutputRoute(input, result);
        return result;
    }

    private static ComponentRuntimeKernelEventFact OutputEvent(long cycle, Packet packet) => new(
        TraceEventType.Compute,
        $"phase=4;exact_kernel=true;relu_output;layer={Layer(packet)};invocation={Invocation(packet)};output_cycle={cycle}",
        packet.Id, packet.Bits);
    private static string Layer(Packet packet) => packet.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.LayerId, "");
    private static string Invocation(Packet packet) => packet.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.InvocationId, "");
}

internal sealed class Phase8AReluState : IComponentRuntimeKernelState
{
    public List<Packet> Inputs { get; } = [];
    public List<Phase8AElementwisePendingResult> Results { get; } = [];
    public bool IsIdle => Inputs.Count == 0 && Results.Count == 0;
    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8AReluState();
        clone.Inputs.AddRange(Inputs.Select(PacketClone.Clone));
        clone.Results.AddRange(Results.Select(item => new Phase8AElementwisePendingResult(PacketClone.Clone(item.Packet), item.ReadyCycle)));
        return clone;
    }
}
