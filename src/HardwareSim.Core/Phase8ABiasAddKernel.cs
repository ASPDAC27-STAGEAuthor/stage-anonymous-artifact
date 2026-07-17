namespace HardwareSim.Core;

internal sealed class Phase8ABiasAddKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new Phase8ABiasAddState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName)
    {
        var state = (Phase8ABiasAddState)current;
        return inputPortName switch
        {
            Phase8ABiasAddContract.BiasInputPort => state.Bias is null,
            Phase8ABiasAddContract.TensorInputPort => state.Inputs.Count < Math.Max(1, context.Contract.Queues.InputDepth),
            _ => false
        };
    }

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next BiasAdd states must not alias.");
        if (!Phase8AElementwisePacketValidation.TryValidate(input.Packet, out var payloadError))
            return Error("BiasAddPayloadInvalid", $"Packet '{input.Packet.Id}' is invalid: {payloadError}.", input.Packet.Id);
        if (!TryDType(input.Packet.Precision, out _))
            return Error("BiasAddPrecisionUnsupported", $"Packet '{input.Packet.Id}' uses unsupported BiasAdd precision '{input.Packet.Precision}'.", input.Packet.Id);
        var nextState = (Phase8ABiasAddState)next;
        if (input.InputPortName == Phase8ABiasAddContract.BiasInputPort)
        {
            if (nextState.Bias is not null) return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "bias_already_committed" };
            nextState.Bias = PacketClone.Clone(input.Packet);
            return new ComponentRuntimeKernelInputResult
            {
                Accepted = true,
                Events = [new(TraceEventType.Compute,
                    $"phase=1;exact_kernel=true;bias_add_bias_commit;layer={Layer(input.Packet)};bias_packet={input.Packet.Id};elements={input.Packet.Values.Count}",
                    input.Packet.Id, input.Packet.Bits)]
            };
        }
        if (input.InputPortName != Phase8ABiasAddContract.TensorInputPort)
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };
        if (nextState.Inputs.Count >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };
        nextState.Inputs.Add(PacketClone.Clone(input.Packet));
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute,
                $"phase=1;exact_kernel=true;bias_add_sample;layer={Layer(input.Packet)};invocation={Invocation(input.Packet)}",
                input.Packet.Id, input.Packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next BiasAdd states must not alias.");
        var currentState = (Phase8ABiasAddState)current;
        var nextState = (Phase8ABiasAddState)next;
        var outputs = new List<ComponentRuntimeKernelOutput>();
        var events = new List<ComponentRuntimeKernelEventFact>();
        var ready = currentState.Results.OrderBy(item => item.ReadyCycle).ThenBy(item => item.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && outputQueueAvailable)
        {
            nextState.Results.RemoveAll(item => item.ReadyCycle == ready.ReadyCycle && string.Equals(item.Packet.Id, ready.Packet.Id, StringComparison.Ordinal));
            outputs.Add(new(Phase8ABiasAddContract.OutputPort, PacketClone.Clone(ready.Packet)));
            events.Add(OutputEvent(context.Cycle, ready.Packet));
        }

        if (currentState.Bias is not null && currentState.Inputs.Count > 0)
        {
            var tensor = currentState.Inputs[0];
            if (tensor.Values.Count != currentState.Bias.Values.Count)
                return new ComponentRuntimeKernelAdvanceResult
                {
                    Issues = [new("BiasAddShapeMismatch", "error", $"Tensor '{tensor.Id}' and resident bias '{currentState.Bias.Id}' have different element counts.", tensor.Id)]
                };
            if (tensor.Precision != currentState.Bias.Precision || !TryDType(tensor.Precision, out var dtype))
                return new ComponentRuntimeKernelAdvanceResult
                {
                    Issues = [new("BiasAddPrecisionMismatch", "error", $"Tensor '{tensor.Id}' and resident bias '{currentState.Bias.Id}' require the same supported precision.", tensor.Id)]
                };
            var readyCycle = context.Cycle + Math.Max(1, context.Contract.Timing.OperationLatencyCycles) - 1;
            var canEmitImmediately = readyCycle <= context.Cycle && outputQueueAvailable && outputs.Count == 0;
            if (!canEmitImmediately && nextState.Results.Count >= Math.Max(1, context.Contract.Queues.OutputDepth))
                return new ComponentRuntimeKernelAdvanceResult { Outputs = outputs, Events = events };
            nextState.Inputs.RemoveAll(packet => string.Equals(packet.Id, tensor.Id, StringComparison.Ordinal));
            var result = Execute(context.Component.Id, tensor, currentState.Bias, dtype);
            events.Add(new(TraceEventType.Compute,
                $"phase=4;exact_kernel=true;bias_add_issue;layer={Layer(tensor)};invocation={Invocation(tensor)};issue_cycle={context.Cycle};ready_cycle={readyCycle};bias_packet={currentState.Bias.Id}",
                tensor.Id, tensor.Bits));
            if (canEmitImmediately)
            {
                outputs.Add(new(Phase8ABiasAddContract.OutputPort, result));
                events.Add(OutputEvent(context.Cycle, result));
            }
            else nextState.Results.Add(new Phase8AElementwisePendingResult(result, readyCycle));
        }
        return new ComponentRuntimeKernelAdvanceResult { Outputs = outputs, Events = events };
    }

    private static Packet Execute(string componentId, Packet tensor, Packet bias, string dtype)
    {

        var result = PacketClone.Clone(tensor);
        result.Id = tensor.Id + ":bias";
        result.SourceComponentId = componentId;
        result.CurrentComponentId = componentId;
        result.SourcePort = Phase8ABiasAddContract.OutputPort;
        result.Values = tensor.Values.Zip(bias.Values, (value, biasValue) => DigitalNumericFormats.Quantize(value + biasValue, dtype).Value).ToList();
        result.DependencyIds = tensor.DependencyIds.Append(tensor.Id).Append(bias.Id).Distinct(StringComparer.Ordinal).ToList();
        result.Metadata[Phase8AOperandPipelineMetadata.Operation] = "bias_add";
        result.Metadata["phase8a.pipeline.bias_packet_id"] = bias.Id;
        Phase8ACollectiveMetadataCodec.ApplyOutputRoute(tensor, result);
        return result;
    }

    private static ComponentRuntimeKernelEventFact OutputEvent(long cycle, Packet packet) => new(
        TraceEventType.Compute,
        $"phase=4;exact_kernel=true;bias_add_output;layer={Layer(packet)};invocation={Invocation(packet)};output_cycle={cycle};bias_packet={packet.Metadata.GetValueOrDefault("phase8a.pipeline.bias_packet_id", "")}",
        packet.Id, packet.Bits);

    private static string Layer(Packet packet) => packet.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.LayerId, "");
    private static string Invocation(Packet packet) => packet.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.InvocationId, "");
    private static bool TryDType(PrecisionKind precision, out string dtype)
    {
        dtype = precision switch
        {
            PrecisionKind.FP8_E4M3 => "fp8",
            PrecisionKind.FP16 => "fp16",
            PrecisionKind.FP32 => "fp32",
            _ => ""
        };
        return dtype.Length > 0;
    }
    private static ComponentRuntimeKernelInputResult Error(string code, string message, string packetId) => new()
    {
        Accepted = false,
        Issues = [new(code, "error", message, packetId)]
    };
}

internal sealed class Phase8ABiasAddState : IComponentRuntimeKernelState
{
    public Packet? Bias { get; set; }
    public List<Packet> Inputs { get; } = [];
    public List<Phase8AElementwisePendingResult> Results { get; } = [];
    public bool IsIdle => Inputs.Count == 0 && Results.Count == 0;
    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8ABiasAddState { Bias = Bias is null ? null : PacketClone.Clone(Bias) };
        clone.Inputs.AddRange(Inputs.Select(PacketClone.Clone));
        clone.Results.AddRange(Results.Select(item => new Phase8AElementwisePendingResult(PacketClone.Clone(item.Packet), item.ReadyCycle)));
        return clone;
    }
}

internal sealed record Phase8AElementwisePendingResult(Packet Packet, long ReadyCycle);
