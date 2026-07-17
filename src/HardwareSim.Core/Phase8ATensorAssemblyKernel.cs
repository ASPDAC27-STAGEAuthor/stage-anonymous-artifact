using System.Globalization;

namespace HardwareSim.Core;

internal sealed class Phase8ATensorAssemblyKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) => new Phase8ATensorAssemblyState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        string.Equals(inputPortName, Phase8ATensorAssemblyContract.InputPort, StringComparison.Ordinal) &&
        ((Phase8ATensorAssemblyState)current).BufferedSliceCount < Math.Max(1, context.Contract.Queues.InputDepth);

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next tensor assembly states must not alias.");
        if (!string.Equals(input.InputPortName, Phase8ATensorAssemblyContract.InputPort, StringComparison.Ordinal))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };
        var nextState = (Phase8ATensorAssemblyState)next;
        if (nextState.BufferedSliceCount >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };

        var parsed = ParseMetadata(input.Packet, Phase8ACollectivePluginRuntime.ReadResourceInt(context.Contract, "max_tensor_elements", 1_048_576));
        if (parsed.Issues.Count > 0)
            return new ComponentRuntimeKernelInputResult { Accepted = false, Issues = parsed.Issues };
        var metadata = parsed.Metadata!;
        if (nextState.ClosedGroupKeys.Contains(metadata.GroupKey))
            return Error("TensorAssemblyClosedGroup", $"Group '{metadata.GroupKey}' has already completed or failed.", input.Packet.Id);
        if (nextState.Groups.TryGetValue(metadata.GroupKey, out var group))
        {
            var mismatch = ValidateAgainstGroup(input.Packet, metadata, group);
            if (mismatch is not null)
                return Error(mismatch.Code, mismatch.Message, input.Packet.Id);
            if (group.Slices.ContainsKey(metadata.ContributorId))
                return Error("TensorAssemblyDuplicateContributor", $"Group '{metadata.GroupKey}' already contains contributor '{metadata.ContributorId}'.", input.Packet.Id);
            if (group.SliceMetadata.Values.Any(existing => Overlaps(existing, metadata)))
                return Error("TensorAssemblyRangeOverlap", $"Group '{metadata.GroupKey}' contains overlapping output ranges.", input.Packet.Id);
        }
        else
        {
            group = new Phase8ATensorAssemblyGroup(metadata, context.Cycle);
            nextState.Groups[metadata.GroupKey] = group;
        }

        group.Slices[metadata.ContributorId] = PacketClone.Clone(input.Packet);
        group.SliceMetadata[metadata.ContributorId] = metadata;
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute,
                $"phase=1;exact_kernel=true;tensor_assembly_sample;group={metadata.GroupKey};contributor={metadata.ContributorId};m_offset={metadata.MOffset};m_extent={metadata.MExtent};n_offset={metadata.NOffset};n_extent={metadata.NExtent};received={group.Slices.Count};expected={metadata.ExpectedContributors.Count}",
                input.Packet.Id, input.Packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next tensor assembly states must not alias.");
        var currentState = (Phase8ATensorAssemblyState)current;
        var nextState = (Phase8ATensorAssemblyState)next;
        var timeout = Math.Max(1, Phase8ACollectivePluginRuntime.ReadResourceInt(context.Contract, "missing_contributor_timeout_cycles", 64));
        var expired = currentState.Groups.Values
            .Where(group => !group.IsComplete && context.Cycle - group.FirstAcceptedCycle >= timeout)
            .OrderBy(group => group.Contract.GroupKey, StringComparer.Ordinal)
            .FirstOrDefault();
        if (expired is not null)
        {
            nextState.Groups.Remove(expired.Contract.GroupKey);
            nextState.ClosedGroupKeys.Add(expired.Contract.GroupKey);
            var missing = expired.Contract.ExpectedContributors.Where(id => !expired.Slices.ContainsKey(id)).ToArray();
            return new ComponentRuntimeKernelAdvanceResult
            {
                Issues = [new("TensorAssemblyMissingContributor", "error", $"Group '{expired.Contract.GroupKey}' timed out with missing contributors [{string.Join(",", missing)}].")],
                Events = [new(TraceEventType.Error, $"phase=4;code=TensorAssemblyMissingContributor;group={expired.Contract.GroupKey};missing={string.Join(",", missing)}")]
            };
        }

        var ready = currentState.PendingOutputs.OrderBy(item => item.ReadyCycle)
            .ThenBy(item => item.Packet.Id, StringComparer.Ordinal)
            .FirstOrDefault(item => item.ReadyCycle <= context.Cycle);
        if (ready is not null && outputQueueAvailable)
        {
            nextState.PendingOutputs.RemoveAll(item => item.ReadyCycle == ready.ReadyCycle &&
                string.Equals(item.Packet.Id, ready.Packet.Id, StringComparison.Ordinal));
            return Output(ready.Packet, context.Cycle);
        }

        var complete = currentState.Groups.Values.Where(group => group.IsComplete)
            .OrderBy(group => group.Contract.GroupKey, StringComparer.Ordinal)
            .FirstOrDefault();
        if (complete is null) return new ComponentRuntimeKernelAdvanceResult();
        var covered = complete.SliceMetadata.Values.Sum(slice => checked((long)slice.MExtent * slice.NExtent));
        var required = checked((long)complete.Contract.TensorMExtent * complete.Contract.TensorNExtent);
        if (covered != required)
        {
            nextState.Groups.Remove(complete.Contract.GroupKey);
            nextState.ClosedGroupKeys.Add(complete.Contract.GroupKey);
            return new ComponentRuntimeKernelAdvanceResult
            {
                Issues = [new("TensorAssemblyRangeGap", "error", $"Group '{complete.Contract.GroupKey}' covers {covered} of {required} required tensor elements.")],
                Events = [new(TraceEventType.Error, $"phase=4;code=TensorAssemblyRangeGap;group={complete.Contract.GroupKey};covered={covered};required={required}")]
            };
        }

        var result = Assemble(context.Component.Id, complete);
        var latency = Math.Max(1, context.Contract.Timing.OperationLatencyCycles);
        var readyCycle = context.Cycle + latency - 1;
        if (readyCycle <= context.Cycle && outputQueueAvailable)
        {
            nextState.Groups.Remove(complete.Contract.GroupKey);
            nextState.ClosedGroupKeys.Add(complete.Contract.GroupKey);
            return Output(result, context.Cycle);
        }
        var outputDepth = Math.Max(1, context.Contract.Queues.OutputDepth);
        if (nextState.PendingOutputs.Count >= outputDepth)
            return new ComponentRuntimeKernelAdvanceResult();
        nextState.Groups.Remove(complete.Contract.GroupKey);
        nextState.ClosedGroupKeys.Add(complete.Contract.GroupKey);
        nextState.PendingOutputs.Add(new Phase8ATensorAssemblyPendingOutput(result, readyCycle));
        return new ComponentRuntimeKernelAdvanceResult
        {
            Events = [new(TraceEventType.Compute,
                $"phase=4;exact_kernel=true;tensor_assembly_issue;group={complete.Contract.GroupKey};contributors={string.Join(",", complete.Contract.ExpectedContributors)};ready_cycle={readyCycle}",
                result.Id, result.Bits)]
        };
    }

    private static ComponentRuntimeKernelAdvanceResult Output(Packet packet, long cycle) => new()
    {
        Outputs = [new(Phase8ATensorAssemblyContract.OutputPort, PacketClone.Clone(packet))],
        Events = [new(TraceEventType.Compute,
            $"phase=4;exact_kernel=true;tensor_assembly_output;group={packet.Metadata.GetValueOrDefault(Phase8ACollectiveRuntimeMetadata.GroupKey, "")};output_cycle={cycle}",
            packet.Id, packet.Bits)]
    };

    private static Packet Assemble(string componentId, Phase8ATensorAssemblyGroup group)
    {
        var ordered = group.Contract.ExpectedContributors.Select(id => group.Slices[id]).ToArray();
        var output = new double[checked(group.Contract.TensorMExtent * group.Contract.TensorNExtent)];
        foreach (var contributor in group.Contract.ExpectedContributors)
        {
            var packet = group.Slices[contributor];
            var slice = group.SliceMetadata[contributor];
            for (var row = 0; row < slice.MExtent; row++)
            for (var column = 0; column < slice.NExtent; column++)
            {
                var source = row * slice.NExtent + column;
                var destination = (slice.MOffset + row) * slice.TensorNExtent + slice.NOffset + column;
                output[destination] = packet.Values[source];
            }
        }

        var result = PacketClone.Clone(ordered[0]);
        result.Id = group.Contract.GroupKey + ":tensor-assembly";
        result.NumElements = output.Length;
        result.Bits = checked(output.Length * Math.Max(1, result.BitWidth));
        result.Precision = group.Contract.DType;
        result.Values = output.ToList();
        result.SourceComponentId = componentId;
        result.CurrentComponentId = componentId;
        result.SourcePort = Phase8ATensorAssemblyContract.OutputPort;
        result.DestinationPort = "";
        result.DependencyIds = ordered.Select(packet => packet.Id).ToList();
        result.VisitedComponents = ordered.SelectMany(packet => packet.VisitedComponents)
            .Append(componentId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.Metadata[Phase8ACollectiveRuntimeMetadata.ContributorId] = "tensor-assembly";
        Phase8ACollectiveMetadataCodec.ApplyOutputRoute(ordered[0], result);
        return result;
    }

    private static Phase8ATensorAssemblyParseResult ParseMetadata(Packet packet, int maxElements)
    {
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        string Required(string key)
        {
            if (Phase8ACollectiveMetadataCodec.TryReadRequired(packet, key, out var value)) return value;
            issues.Add(new("TensorAssemblyMetadataMissing", "error", $"Packet '{packet.Id}' is missing required metadata '{key}'.", packet.Id));
            return "";
        }

        var operation = Required(Phase8ACollectiveRuntimeMetadata.OperationKind);
        var group = Required(Phase8ACollectiveRuntimeMetadata.GroupKey);
        var expectedRaw = Required(Phase8ACollectiveRuntimeMetadata.ExpectedContributors);
        var contributor = Required(Phase8ACollectiveRuntimeMetadata.ContributorId);
        var dtypeRaw = Required(Phase8ACollectiveRuntimeMetadata.DType);
        var mOffset = ReadRange(packet, Phase8ACollectiveRuntimeMetadata.OutputMOffset, 0, issues);
        var mExtent = ReadRange(packet, Phase8ACollectiveRuntimeMetadata.OutputMExtent, 1, issues);
        var nOffset = ReadRange(packet, Phase8ACollectiveRuntimeMetadata.OutputNOffset, 0, issues);
        var nExtent = ReadRange(packet, Phase8ACollectiveRuntimeMetadata.OutputNExtent, 1, issues);
        var tensorM = ReadRange(packet, Phase8ACollectiveRuntimeMetadata.TensorMExtent, 1, issues);
        var tensorN = ReadRange(packet, Phase8ACollectiveRuntimeMetadata.TensorNExtent, 1, issues);
        Phase8ACollectiveMetadataCodec.TryDecodeStringList(expectedRaw, out var expected);
        if (!string.Equals(operation, Phase8ATensorAssemblyContract.ConcatOperation, StringComparison.Ordinal))
            issues.Add(new("TensorAssemblyOperationMismatch", "error", $"Packet '{packet.Id}' requests unsupported operation '{operation}'.", packet.Id));
        if (expected.Count == 0 || expected.Distinct(StringComparer.Ordinal).Count() != expected.Count || !expected.Contains(contributor, StringComparer.Ordinal))
            issues.Add(new("TensorAssemblyExpectedContributorsInvalid", "error", $"Packet '{packet.Id}' requires a unique ordered contributor list containing itself.", packet.Id));
        if (!Phase8ACollectiveMetadataCodec.TryParsePrecision(dtypeRaw, out var dtype) || packet.Precision != dtype)
            issues.Add(new("TensorAssemblyPrecisionMismatch", "error", $"Packet '{packet.Id}' precision does not match its declared dtype '{dtypeRaw}'.", packet.Id));
        var inBounds = mOffset >= 0 && nOffset >= 0 && mExtent > 0 && nExtent > 0 && tensorM > 0 && tensorN > 0 &&
                       (long)mOffset + mExtent <= tensorM && (long)nOffset + nExtent <= tensorN;
        if (!inBounds)
            issues.Add(new("TensorAssemblyRangeMismatch", "error", $"Packet '{packet.Id}' output range is outside the assembled tensor extent.", packet.Id));
        var total = tensorM > 0 && tensorN > 0 ? (long)tensorM * tensorN : -1;
        if (total <= 0 || total > maxElements || packet.Values.Count != (long)mExtent * nExtent ||
            packet.NumElements != packet.Values.Count || !PrecisionModel.TryGetDigitalBitWidth(dtype, out var dtypeBitWidth) || packet.BitWidth != dtypeBitWidth || packet.Bits != (long)packet.Values.Count * packet.BitWidth)
            issues.Add(new("TensorAssemblyShapeMismatch", "error", $"Packet '{packet.Id}' slice shape, values, and bit count are inconsistent.", packet.Id));
        if (packet.Values.Any(value => !double.IsFinite(value)))
            issues.Add(new("TensorAssemblyValueInvalid", "error", $"Packet '{packet.Id}' contains NaN or infinity.", packet.Id));
        if (!Phase8AStageRouteMetadata.TryValidateBoundMetadata(packet, out var stageRouteReason))
            issues.Add(new("TensorAssemblyStageRouteInvalid", "error", $"Packet '{packet.Id}' has invalid stage routing metadata: {stageRouteReason}.", packet.Id));
        if (!Phase8ACollectiveMetadataCodec.TryReadOutputRoute(packet, out var outputPathId, out var outputDestination, out var outputRoute, out var outputRouteReason))
            issues.Add(new("TensorAssemblyOutputRouteInvalid", "error", $"Packet '{packet.Id}' has invalid output routing metadata: {outputRouteReason}.", packet.Id));
        var metadata = issues.Count == 0
            ? new Phase8ATensorAssemblySlice(group, expected, contributor, mOffset, mExtent, nOffset, nExtent, tensorM, tensorN, dtype, outputPathId, outputDestination, outputRoute)
            : null;
        return new(metadata, issues.AsReadOnly());
    }

    private static int ReadRange(Packet packet, string key, int minimum, List<ComponentRuntimeKernelIssueFact> issues)
    {
        if (Phase8ACollectiveMetadataCodec.TryReadInt(packet, key, minimum, out var value)) return value;
        issues.Add(new("TensorAssemblyRangeMismatch", "error", $"Packet '{packet.Id}' metadata '{key}' must be an integer >= {minimum}.", packet.Id));
        return minimum - 1;
    }

    private static ComponentRuntimeKernelIssueFact? ValidateAgainstGroup(Packet packet, Phase8ATensorAssemblySlice slice, Phase8ATensorAssemblyGroup group)
    {
        var contract = group.Contract;
        if (!slice.ExpectedContributors.SequenceEqual(contract.ExpectedContributors, StringComparer.Ordinal))
            return new("TensorAssemblyExpectedContributorsMismatch", "error", $"Group '{slice.GroupKey}' changed its ordered contributor contract.", packet.Id);
        if (slice.DType != contract.DType)
            return new("TensorAssemblyPrecisionMismatch", "error", $"Group '{slice.GroupKey}' changed dtype.", packet.Id);
        if (slice.TensorMExtent != contract.TensorMExtent || slice.TensorNExtent != contract.TensorNExtent)
            return new("TensorAssemblyShapeMismatch", "error", $"Group '{slice.GroupKey}' changed assembled tensor extent.", packet.Id);
        if (!string.Equals(slice.OutputRoutePathId, contract.OutputRoutePathId, StringComparison.Ordinal) ||
            !string.Equals(slice.OutputDestinationComponentId, contract.OutputDestinationComponentId, StringComparison.Ordinal) ||
            !slice.OutputRouteLinkIds.SequenceEqual(contract.OutputRouteLinkIds, StringComparer.Ordinal))
            return new("TensorAssemblyOutputRouteMismatch", "error", $"Group '{slice.GroupKey}' changed its exact output route.", packet.Id);
        return null;
    }

    private static bool Overlaps(Phase8ATensorAssemblySlice left, Phase8ATensorAssemblySlice right) =>
        left.MOffset < right.MOffset + right.MExtent && right.MOffset < left.MOffset + left.MExtent &&
        left.NOffset < right.NOffset + right.NExtent && right.NOffset < left.NOffset + left.NExtent;

    private static ComponentRuntimeKernelInputResult Error(string code, string message, string packetId) => new()
    {
        Accepted = false,
        Issues = [new(code, "error", message, packetId)]
    };
}

internal sealed class Phase8ATensorAssemblyState : IComponentRuntimeKernelState
{
    public Dictionary<string, Phase8ATensorAssemblyGroup> Groups { get; } = new(StringComparer.Ordinal);
    public List<Phase8ATensorAssemblyPendingOutput> PendingOutputs { get; } = [];
    public HashSet<string> ClosedGroupKeys { get; } = new(StringComparer.Ordinal);
    public int BufferedSliceCount => Groups.Values.Sum(group => group.Slices.Count);
    public bool IsIdle => Groups.Count == 0 && PendingOutputs.Count == 0;

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8ATensorAssemblyState();
        foreach (var pair in Groups) clone.Groups[pair.Key] = pair.Value.DeepClone();
        clone.PendingOutputs.AddRange(PendingOutputs.Select(item => new Phase8ATensorAssemblyPendingOutput(PacketClone.Clone(item.Packet), item.ReadyCycle)));
        clone.ClosedGroupKeys.UnionWith(ClosedGroupKeys);
        return clone;
    }
}

internal sealed class Phase8ATensorAssemblyGroup
{
    public Phase8ATensorAssemblyGroup(Phase8ATensorAssemblySlice contract, long firstAcceptedCycle)
    {
        Contract = contract;
        FirstAcceptedCycle = firstAcceptedCycle;
    }

    public Phase8ATensorAssemblySlice Contract { get; }
    public long FirstAcceptedCycle { get; }
    public Dictionary<string, Packet> Slices { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Phase8ATensorAssemblySlice> SliceMetadata { get; } = new(StringComparer.Ordinal);
    public bool IsComplete => Contract.ExpectedContributors.All(Slices.ContainsKey);

    public Phase8ATensorAssemblyGroup DeepClone()
    {
        var clone = new Phase8ATensorAssemblyGroup(Contract.DeepClone(), FirstAcceptedCycle);
        foreach (var pair in Slices) clone.Slices[pair.Key] = PacketClone.Clone(pair.Value);
        foreach (var pair in SliceMetadata) clone.SliceMetadata[pair.Key] = pair.Value.DeepClone();
        return clone;
    }
}

internal sealed record Phase8ATensorAssemblySlice(
    string GroupKey,
    IReadOnlyList<string> ExpectedContributors,
    string ContributorId,
    int MOffset,
    int MExtent,
    int NOffset,
    int NExtent,
    int TensorMExtent,
    int TensorNExtent,
    PrecisionKind DType,
    string OutputRoutePathId,
    string OutputDestinationComponentId,
    IReadOnlyList<string> OutputRouteLinkIds)
{
    public Phase8ATensorAssemblySlice DeepClone() => this with
    {
        ExpectedContributors = ExpectedContributors.ToList().AsReadOnly(),
        OutputRouteLinkIds = OutputRouteLinkIds.ToList().AsReadOnly()
    };
}

internal sealed record Phase8ATensorAssemblyParseResult(
    Phase8ATensorAssemblySlice? Metadata,
    IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues);

internal sealed record Phase8ATensorAssemblyPendingOutput(Packet Packet, long ReadyCycle);
