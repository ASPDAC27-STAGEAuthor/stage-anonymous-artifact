using System.Globalization;

namespace HardwareSim.Core;

internal sealed class Phase8ABranchMulticastKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new Phase8ABranchMulticastState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        string.Equals(inputPortName, Phase8ABranchMulticastContract.InputPort, StringComparison.Ordinal) &&
        ((Phase8ABranchMulticastState)current).Items.Count < Math.Max(1, context.Contract.Queues.InputDepth);

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next multicast states must not alias.");
        if (!string.Equals(input.InputPortName, Phase8ABranchMulticastContract.InputPort, StringComparison.Ordinal))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };

        var nextState = (Phase8ABranchMulticastState)next;
        if (nextState.Items.Count >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };
        if (nextState.SeenParentIds.Contains(input.Packet.Id))
            return Error("BranchMulticastDuplicateParent", $"Parent packet '{input.Packet.Id}' is already buffered.", input.Packet.Id);

        var parsed = Parse(input.Packet, Phase8ACollectivePluginRuntime.ReadResourceInt(context.Contract, "max_fanout", 1024));
        if (parsed.Issues.Count > 0)
            return new ComponentRuntimeKernelInputResult { Accepted = false, Issues = parsed.Issues };

        var latency = Math.Max(1, context.Contract.Timing.OperationLatencyCycles);
        nextState.SeenParentIds.Add(input.Packet.Id);
        nextState.Items.Add(new Phase8ABranchMulticastWorkItem(
            PacketClone.Clone(input.Packet),
            parsed.FlowId,
            parsed.BranchId,
            parsed.Targets,
            context.Cycle,
            context.Cycle + latency));
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute,
                $"phase=1;exact_kernel=true;branch_multicast_sample;flow={parsed.FlowId};branch={parsed.BranchId};fanout={parsed.Targets.Count};parent_bits={input.Packet.Bits}",
                input.Packet.Id, input.Packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next multicast states must not alias.");
        if (!outputQueueAvailable || context.AvailableOutputSlots <= 0) return new ComponentRuntimeKernelAdvanceResult();
        var currentState = (Phase8ABranchMulticastState)current;
        var nextState = (Phase8ABranchMulticastState)next;
        var item = currentState.Items
            .Where(candidate => candidate.ReadyCycle <= context.Cycle && candidate.NextTargetIndex < candidate.Targets.Count)
            .OrderBy(candidate => candidate.AcceptedCycle)
            .ThenBy(candidate => candidate.Parent.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (item is null) return new ComponentRuntimeKernelAdvanceResult();

        var count = Math.Min(context.AvailableOutputSlots, item.Targets.Count - item.NextTargetIndex);
        var outputs = new List<ComponentRuntimeKernelOutput>(count);
        var events = new List<ComponentRuntimeKernelEventFact>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var ordinal = item.NextTargetIndex + offset;
            var target = item.Targets[ordinal];
            var clone = CreateClone(context.Component.Id, item, target, ordinal);
            outputs.Add(new(Phase8ABranchMulticastContract.OutputPort, clone));
            events.Add(new(TraceEventType.Compute,
                $"phase=4;exact_kernel=true;branch_multicast_clone;flow={item.FlowId};branch={item.BranchId};parent={item.Parent.Id};consumer={target.ConsumerComponentId};clone_ordinal={ordinal};fanout={item.Targets.Count};parent_bits={item.Parent.Bits};clone_bits={clone.Bits}",
                clone.Id, clone.Bits));
        }

        var nextItem = nextState.Items.Single(candidate =>
            candidate.AcceptedCycle == item.AcceptedCycle && string.Equals(candidate.Parent.Id, item.Parent.Id, StringComparison.Ordinal));
        nextItem.NextTargetIndex += count;
        if (nextItem.NextTargetIndex >= nextItem.Targets.Count)
        {
            nextState.Items.Remove(nextItem);
            nextState.SeenParentIds.Remove(nextItem.Parent.Id);
        }
        return new ComponentRuntimeKernelAdvanceResult { Outputs = outputs, Events = events };
    }

    private static Packet CreateClone(
        string componentId,
        Phase8ABranchMulticastWorkItem item,
        Phase8AMulticastBranchTarget target,
        int ordinal)
    {
        var clone = PacketClone.Clone(item.Parent);
        clone.Id = item.Parent.Id + ":branch:" + ordinal.ToString("D4", CultureInfo.InvariantCulture);
        clone.SourceComponentId = componentId;
        clone.CurrentComponentId = componentId;
        clone.SourcePort = Phase8ABranchMulticastContract.OutputPort;
        clone.DestinationComponentId = target.ConsumerComponentId;
        clone.DestinationPort = "";
        clone.DependencyIds = item.Parent.DependencyIds.Append(item.Parent.Id).Distinct(StringComparer.Ordinal).ToList();
        clone.VisitedComponents = item.Parent.VisitedComponents.Append(componentId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        clone.Metadata.Remove(Phase8ACollectiveRuntimeMetadata.MulticastTargets);
        clone.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastParentPacketId] = item.Parent.Id;
        clone.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastConsumerId] = target.ConsumerComponentId;
        clone.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastConsumerSet] =
            Phase8ACollectiveMetadataCodec.EncodeStringList(item.Targets.Select(value => value.ConsumerComponentId));
        clone.Metadata[Phase8ACollectiveRuntimeMetadata.MulticastCloneOrdinal] = ordinal.ToString(CultureInfo.InvariantCulture);
        Phase8ABranchPipelineMetadata.Apply(item.Parent, clone, target.ConsumerComponentId);
        Phase8AExplicitRouteMetadata.Bind(clone, target.RoutePathId, target.RouteLinkIds);
        return clone;
    }

    internal static bool TryCreateImmediateRouterClones(
        string componentId,
        Packet parent,
        out IReadOnlyList<Packet> clones,
        out IReadOnlyList<ComponentRuntimeKernelIssueFact> issues)
    {
        var parsed = Parse(parent, 1024);
        issues = parsed.Issues;
        if (issues.Count > 0)
        {
            clones = [];
            return false;
        }

        var item = new Phase8ABranchMulticastWorkItem(
            PacketClone.Clone(parent),
            parsed.FlowId,
            parsed.BranchId,
            parsed.Targets,
            0,
            0);
        clones = parsed.Targets
            .Select((target, ordinal) => CreateClone(componentId, item, target, ordinal))
            .ToArray();
        return true;
    }
    private static Phase8ABranchMulticastParsed Parse(Packet packet, int maxFanout)
    {
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        string Required(string key, string code)
        {
            if (Phase8ACollectiveMetadataCodec.TryReadRequired(packet, key, out var value)) return value;
            issues.Add(new(code, "error", $"Packet '{packet.Id}' is missing required metadata '{key}'.", packet.Id));
            return "";
        }

        var operation = Required(Phase8ACollectiveRuntimeMetadata.OperationKind, "BranchMulticastMetadataMissing");
        var flow = Required(Phase8ACollectiveRuntimeMetadata.MulticastFlowId, "BranchMulticastMetadataMissing");
        var branch = Required(Phase8ACollectiveRuntimeMetadata.MulticastBranchId, "BranchMulticastMetadataMissing");
        var targetsRaw = Required(Phase8ACollectiveRuntimeMetadata.MulticastTargets, "BranchMulticastMetadataMissing");
        if (!string.Equals(operation, Phase8ABranchMulticastContract.MulticastOperation, StringComparison.Ordinal))
            issues.Add(new("BranchMulticastOperationMismatch", "error", $"Packet '{packet.Id}' requests unsupported operation '{operation}'.", packet.Id));
        if (!Phase8ACollectiveMetadataCodec.TryDecodeTargets(targetsRaw, out var targets) || targets.Count < 2 || targets.Count > maxFanout)
        {
            issues.Add(new("BranchMulticastTargetsInvalid", "error", $"Packet '{packet.Id}' requires between 2 and {maxFanout} typed branch targets.", packet.Id));
            targets = [];
        }
        if (targets.Select(target => target.ConsumerComponentId).Any(string.IsNullOrWhiteSpace) ||
            targets.Select(target => target.ConsumerComponentId).Distinct(StringComparer.Ordinal).Count() != targets.Count ||
            targets.Select(target => target.RoutePathId).Any(string.IsNullOrWhiteSpace) ||
            targets.Select(target => target.RoutePathId).Distinct(StringComparer.Ordinal).Count() != targets.Count ||
            targets.Any(target => target.RouteLinkIds.Count == 0 || target.RouteLinkIds.Any(string.IsNullOrWhiteSpace) ||
                target.RouteLinkIds.Distinct(StringComparer.Ordinal).Count() != target.RouteLinkIds.Count))
        {
            issues.Add(new("BranchMulticastTargetsInvalid", "error", $"Packet '{packet.Id}' target ids and exact routes must be non-empty and unique.", packet.Id));
        }
        if (packet.Bits <= 0 || packet.NumElements <= 0 ||
            !PrecisionModel.TryGetDigitalBitWidth(packet.Precision, out var packetBitWidth) ||
            packet.BitWidth != packetBitWidth || packet.Bits != (long)packet.NumElements * packetBitWidth ||
            packet.Values.Any(value => !double.IsFinite(value)))
            issues.Add(new("BranchMulticastPayloadInvalid", "error", $"Packet '{packet.Id}' has invalid digital payload metadata or values.", packet.Id));
        if (packet.NumElements != packet.Values.Count)
            issues.Add(new("BranchMulticastShapeMismatch", "error", $"Packet '{packet.Id}' NumElements does not match its numeric payload.", packet.Id));
        return new Phase8ABranchMulticastParsed(flow, branch, targets, issues.AsReadOnly());
    }

    private static ComponentRuntimeKernelInputResult Error(string code, string message, string packetId) => new()
    {
        Accepted = false,
        Issues = [new(code, "error", message, packetId)]
    };
}

internal sealed class Phase8ABranchMulticastState : IComponentRuntimeKernelState
{
    public List<Phase8ABranchMulticastWorkItem> Items { get; } = [];
    public HashSet<string> SeenParentIds { get; } = new(StringComparer.Ordinal);
    public bool IsIdle => Items.Count == 0;

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8ABranchMulticastState();
        clone.Items.AddRange(Items.Select(item => item.DeepClone()));
        clone.SeenParentIds.UnionWith(SeenParentIds);
        return clone;
    }
}

internal sealed class Phase8ABranchMulticastWorkItem
{
    public Phase8ABranchMulticastWorkItem(
        Packet parent,
        string flowId,
        string branchId,
        IReadOnlyList<Phase8AMulticastBranchTarget> targets,
        long acceptedCycle,
        long readyCycle)
    {
        Parent = parent;
        FlowId = flowId;
        BranchId = branchId;
        Targets = targets.Select(target => new Phase8AMulticastBranchTarget(target.ConsumerComponentId, target.RouteLinkIds, target.RoutePathId)).ToList().AsReadOnly();
        AcceptedCycle = acceptedCycle;
        ReadyCycle = readyCycle;
    }

    public Packet Parent { get; }
    public string FlowId { get; }
    public string BranchId { get; }
    public IReadOnlyList<Phase8AMulticastBranchTarget> Targets { get; }
    public long AcceptedCycle { get; }
    public long ReadyCycle { get; }
    public int NextTargetIndex { get; set; }

    public Phase8ABranchMulticastWorkItem DeepClone() => new(
        PacketClone.Clone(Parent), FlowId, BranchId, Targets, AcceptedCycle, ReadyCycle)
    {
        NextTargetIndex = NextTargetIndex
    };
}

internal sealed record Phase8ABranchMulticastParsed(
    string FlowId,
    string BranchId,
    IReadOnlyList<Phase8AMulticastBranchTarget> Targets,
    IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues);
