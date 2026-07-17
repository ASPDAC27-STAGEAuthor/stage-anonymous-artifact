using System.Globalization;

namespace HardwareSim.Core;

internal sealed class Phase8ATensorSliceKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new Phase8ATensorSliceState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        string.Equals(inputPortName, Phase8ATensorSliceContract.InputPort, StringComparison.Ordinal) &&
        ((Phase8ATensorSliceState)current).Items.Count < Math.Max(1, context.Contract.Queues.InputDepth);

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next tensor-slice states must not alias.");
        if (!string.Equals(input.InputPortName, Phase8ATensorSliceContract.InputPort, StringComparison.Ordinal))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };
        var nextState = (Phase8ATensorSliceState)next;
        if (nextState.Items.Count >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };
        var parsed = Parse(input.Packet, Phase8ACollectivePluginRuntime.ReadResourceInt(context.Contract, "max_targets", 1024));
        if (parsed.Issues.Count > 0)
            return new ComponentRuntimeKernelInputResult { Accepted = false, Issues = parsed.Issues };
        var latency = Math.Max(1, context.Contract.Timing.OperationLatencyCycles);
        nextState.Items.Add(new Phase8ATensorSliceWorkItem(PacketClone.Clone(input.Packet), parsed.Targets, context.Cycle, context.Cycle + latency));
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute,
                $"phase=1;exact_kernel=true;tensor_slice_sample;layer={Layer(input.Packet)};invocation={Invocation(input.Packet)};targets={parsed.Targets.Count};parent_elements={input.Packet.Values.Count}",
                input.Packet.Id, input.Packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next tensor-slice states must not alias.");
        if (!outputQueueAvailable || context.AvailableOutputSlots <= 0) return new ComponentRuntimeKernelAdvanceResult();
        var currentState = (Phase8ATensorSliceState)current;
        var nextState = (Phase8ATensorSliceState)next;
        var item = currentState.Items.Where(candidate => candidate.ReadyCycle <= context.Cycle && candidate.NextTargetIndex < candidate.Targets.Count)
            .OrderBy(candidate => candidate.AcceptedCycle).ThenBy(candidate => candidate.Parent.Id, StringComparer.Ordinal).FirstOrDefault();
        if (item is null) return new ComponentRuntimeKernelAdvanceResult();
        var count = Math.Min(context.AvailableOutputSlots, item.Targets.Count - item.NextTargetIndex);
        var outputs = new List<ComponentRuntimeKernelOutput>(count);
        var events = new List<ComponentRuntimeKernelEventFact>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var ordinal = item.NextTargetIndex + offset;
            var target = item.Targets[ordinal];
            var slice = Slice(context.Component.Id, item.Parent, target, ordinal);
            outputs.Add(new(Phase8ATensorSliceContract.OutputPort, slice));
            events.Add(new(TraceEventType.Compute,
                $"phase=4;exact_kernel=true;tensor_slice_output;layer={Layer(item.Parent)};invocation={Invocation(item.Parent)};consumer={target.ConsumerComponentId};offset={target.ElementOffset};extent={target.ElementCount};ordinal={ordinal}",
                slice.Id, slice.Bits));
        }
        var nextItem = nextState.Items.Single(candidate => candidate.AcceptedCycle == item.AcceptedCycle && string.Equals(candidate.Parent.Id, item.Parent.Id, StringComparison.Ordinal));
        nextItem.NextTargetIndex += count;
        if (nextItem.NextTargetIndex >= nextItem.Targets.Count) nextState.Items.Remove(nextItem);
        return new ComponentRuntimeKernelAdvanceResult { Outputs = outputs, Events = events };
    }

    private static Packet Slice(string componentId, Packet parent, Phase8ATensorSliceTarget target, int ordinal)
    {
        var result = PacketClone.Clone(parent);
        result.Id = parent.Id + ":slice:" + ordinal.ToString("D4", CultureInfo.InvariantCulture);
        result.SourceComponentId = componentId;
        result.CurrentComponentId = componentId;
        result.SourcePort = Phase8ATensorSliceContract.OutputPort;
        result.DestinationComponentId = target.ConsumerComponentId;
        result.Values = parent.Values.Skip(target.ElementOffset).Take(target.ElementCount).ToList();
        result.NumElements = target.ElementCount;
        result.Bits = checked(target.ElementCount * parent.BitWidth);
        result.DependencyIds = parent.DependencyIds.Append(parent.Id).Distinct(StringComparer.Ordinal).ToList();
        result.Metadata.Remove(Phase8ATensorSliceContract.TargetsMetadata);
        foreach (var pair in target.MetadataOverrides) result.Metadata[pair.Key] = pair.Value;
        Phase8AStageRouteMetadata.BindRemaining(result, target.DownstreamRoutes);
        Phase8AExplicitRouteMetadata.Bind(result, target.RoutePathId, target.RouteLinkIds);
        result.Metadata[Phase8AOperandPipelineMetadata.Operation] = "tensor_slice_distribution";
        result.Metadata["phase8a.pipeline.slice_offset"] = target.ElementOffset.ToString(CultureInfo.InvariantCulture);
        result.Metadata["phase8a.pipeline.slice_extent"] = target.ElementCount.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    private static Phase8ATensorSliceParsed Parse(Packet packet, int maxTargets)
    {
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        if (packet.Values.Count == 0 || packet.Values.Count != packet.NumElements || packet.Values.Any(value => !double.IsFinite(value)) ||
            !PrecisionModel.TryGetDigitalBitWidth(packet.Precision, out var bitWidth) || packet.BitWidth != bitWidth ||
            packet.Bits != (long)packet.NumElements * bitWidth)
            issues.Add(new("TensorSlicePayloadInvalid", "error", $"Packet '{packet.Id}' requires finite numeric values and exact digital bit metadata matching NumElements.", packet.Id));
        var selectReplicate = string.Equals(
            packet.Metadata.GetValueOrDefault(Phase8ATensorSliceContract.CoverageModeMetadata, ""),
            Phase8ATensorSliceContract.SelectReplicateCoverageMode,
            StringComparison.Ordinal);
        var minimumTargets = selectReplicate ? 1 : 2;
        if (!packet.Metadata.TryGetValue(Phase8ATensorSliceContract.TargetsMetadata, out var raw) ||
            !Phase8ATensorSliceMetadata.TryDecode(raw, out var targets) || targets.Count < minimumTargets || targets.Count > maxTargets)
        {
            issues.Add(new("TensorSliceTargetsInvalid", "error", $"Packet '{packet.Id}' requires between {minimumTargets} and {maxTargets} typed slice targets.", packet.Id));
            targets = [];
        }
        if (targets.Count > 0)
        {
            if (selectReplicate)
            {
                if (targets.Any(target => !Phase8ATensorSliceMetadata.IsStructurallyValid(target) ||
                                          target.ElementOffset < 0 || target.ElementCount <= 0 ||
                                          target.ElementOffset > packet.Values.Count - target.ElementCount))
                    issues.Add(new("TensorSliceCoverageInvalid", "error", $"Packet '{packet.Id}' select-replicate targets must be non-empty, exactly routed, and inside the parent tensor range.", packet.Id));
            }
            else
            {
                var ordered = targets.OrderBy(target => target.ElementOffset).ThenBy(target => target.ConsumerComponentId, StringComparer.Ordinal).ToList();
                var cursor = 0L;
                foreach (var target in ordered)
                {
                    if (!Phase8ATensorSliceMetadata.IsStructurallyValid(target) ||
                        target.ElementOffset != cursor || target.ElementCount <= 0)
                    {
                        issues.Add(new("TensorSliceCoverageInvalid", "error", $"Packet '{packet.Id}' slice targets must be non-empty, exactly routed, disjoint, and gap-free.", packet.Id));
                        break;
                    }
                    cursor += target.ElementCount;
                }
                if (cursor != packet.Values.Count)
                    issues.Add(new("TensorSliceCoverageInvalid", "error", $"Packet '{packet.Id}' slice coverage {cursor} does not equal tensor extent {packet.Values.Count}.", packet.Id));
            }
            if (targets.Select(target => target.ConsumerComponentId).Distinct(StringComparer.Ordinal).Count() != targets.Count ||
                targets.Select(target => target.RoutePathId).Distinct(StringComparer.Ordinal).Count() != targets.Count)
                issues.Add(new("TensorSliceTargetsInvalid", "error", $"Packet '{packet.Id}' slice consumers and route path ids must be unique.", packet.Id));
        }
        return new Phase8ATensorSliceParsed(targets, issues.AsReadOnly());
    }

    private static string Layer(Packet packet) => packet.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.LayerId, "");
    private static string Invocation(Packet packet) => packet.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.InvocationId, "");
}

internal sealed class Phase8ATensorSliceState : IComponentRuntimeKernelState
{
    public List<Phase8ATensorSliceWorkItem> Items { get; } = [];
    public bool IsIdle => Items.Count == 0;
    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8ATensorSliceState();
        clone.Items.AddRange(Items.Select(item => item.DeepClone()));
        return clone;
    }
}

internal sealed class Phase8ATensorSliceWorkItem
{
    public Phase8ATensorSliceWorkItem(Packet parent, IReadOnlyList<Phase8ATensorSliceTarget> targets, long acceptedCycle, long readyCycle)
    {
        Parent = parent;
        Targets = targets.Select(target => new Phase8ATensorSliceTarget(
            target.ConsumerComponentId, target.ElementOffset, target.ElementCount, target.RoutePathId,
            target.RouteLinkIds, target.DownstreamRoutes, target.MetadataOverrides)).ToList().AsReadOnly();
        AcceptedCycle = acceptedCycle;
        ReadyCycle = readyCycle;
    }
    public Packet Parent { get; }
    public IReadOnlyList<Phase8ATensorSliceTarget> Targets { get; }
    public long AcceptedCycle { get; }
    public long ReadyCycle { get; }
    public int NextTargetIndex { get; set; }
    public Phase8ATensorSliceWorkItem DeepClone() => new(PacketClone.Clone(Parent), Targets, AcceptedCycle, ReadyCycle) { NextTargetIndex = NextTargetIndex };
}

internal sealed record Phase8ATensorSliceParsed(
    IReadOnlyList<Phase8ATensorSliceTarget> Targets,
    IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues);
