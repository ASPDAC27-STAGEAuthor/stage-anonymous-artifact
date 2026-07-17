using System.Globalization;

namespace HardwareSim.Core;

internal sealed class Phase8AGroupedVectorSumKernel : IPhaseSafeComponentRuntimeKernel
{
    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract) =>
        new Phase8AGroupedVectorSumState();

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        string.Equals(inputPortName, Phase8AGroupedVectorSumContract.InputPort, StringComparison.Ordinal) &&
        ((Phase8AGroupedVectorSumState)current).BufferedContributorCount < Math.Max(1, context.Contract.Queues.InputDepth);

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next grouped vector-sum states must not alias.");
        if (!string.Equals(input.InputPortName, Phase8AGroupedVectorSumContract.InputPort, StringComparison.Ordinal))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "unknown_input_port" };

        var nextState = (Phase8AGroupedVectorSumState)next;
        if (nextState.BufferedContributorCount >= Math.Max(1, context.Contract.Queues.InputDepth))
            return new ComponentRuntimeKernelInputResult { Accepted = false, StallReason = "input_queue_full" };

        var packet = PacketClone.Clone(input.Packet);
        var parsed = ParseMetadata(packet, ReadResourceInt(context.Contract, "max_vector_elements", 1_048_576));
        if (parsed.Issues.Count > 0)
            return new ComponentRuntimeKernelInputResult { Accepted = false, Issues = parsed.Issues };
        var metadata = parsed.Metadata!;
        if (nextState.ClosedGroupKeys.Contains(metadata.GroupKey))
            return Error("GroupedVectorSumClosedGroup", $"Group '{metadata.GroupKey}' has already completed or failed.", packet.Id);

        if (nextState.Groups.TryGetValue(metadata.GroupKey, out var group))
        {
            var mismatch = ValidateAgainstGroup(packet, metadata, group);
            if (mismatch is not null)
                return new ComponentRuntimeKernelInputResult { Accepted = false, Issues = [mismatch] };
            if (group.Contributors.ContainsKey(metadata.ContributorId))
            {
                return Error("GroupedVectorSumDuplicateContributor",
                    $"Group '{metadata.GroupKey}' already contains contributor '{metadata.ContributorId}'.", packet.Id);
            }
        }
        else
        {
            group = new Phase8AGroupedVectorSumGroup(metadata, packet.Values.Count, context.Cycle);
            nextState.Groups[metadata.GroupKey] = group;
        }

        group.Contributors[metadata.ContributorId] = packet;
        return new ComponentRuntimeKernelInputResult
        {
            Accepted = true,
            Events = [new(TraceEventType.Compute,
                $"phase=1;exact_kernel=true;grouped_vector_sum_sample;group={metadata.GroupKey};contributor={metadata.ContributorId};received={group.Contributors.Count};expected={metadata.ExpectedContributors.Count}",
                packet.Id, packet.Bits)]
        };
    }

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable)
    {
        if (ReferenceEquals(current, next)) throw new InvalidOperationException("Current and next grouped vector-sum states must not alias.");
        var currentState = (Phase8AGroupedVectorSumState)current;
        var nextState = (Phase8AGroupedVectorSumState)next;
        var timeout = Math.Max(1, ReadResourceInt(context.Contract, "missing_contributor_timeout_cycles", 64));
        var expired = currentState.Groups.Values
            .Where(group => !group.IsComplete && context.Cycle - group.FirstAcceptedCycle >= timeout)
            .OrderBy(group => group.Metadata.GroupKey, StringComparer.Ordinal)
            .FirstOrDefault();
        if (expired is not null)
        {
            nextState.Groups.Remove(expired.Metadata.GroupKey);
            nextState.ClosedGroupKeys.Add(expired.Metadata.GroupKey);
            var missing = expired.Metadata.ExpectedContributors.Where(id => !expired.Contributors.ContainsKey(id)).ToList();
            return new ComponentRuntimeKernelAdvanceResult
            {
                Issues = [new("GroupedVectorSumMissingContributor", "error",
                    $"Group '{expired.Metadata.GroupKey}' timed out with missing contributors [{string.Join(",", missing)}].")],
                Events = [new(TraceEventType.Error,
                    $"phase=4;code=GroupedVectorSumMissingContributor;group={expired.Metadata.GroupKey};missing={string.Join(",", missing)}")]
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
            .OrderBy(group => group.Metadata.GroupKey, StringComparer.Ordinal)
            .FirstOrDefault();
        if (complete is null) return new ComponentRuntimeKernelAdvanceResult();

        var result = Sum(context.Component.Id, complete);
        var latency = Math.Max(1, context.Contract.Timing.OperationLatencyCycles);
        var readyCycle = context.Cycle + latency - 1;
        if (readyCycle <= context.Cycle && outputQueueAvailable)
        {
            nextState.Groups.Remove(complete.Metadata.GroupKey);
            nextState.ClosedGroupKeys.Add(complete.Metadata.GroupKey);
            return Output(result, context.Cycle);
        }
        if (nextState.PendingOutputs.Count >= Math.Max(1, context.Contract.Queues.OutputDepth))
            return new ComponentRuntimeKernelAdvanceResult();
        nextState.Groups.Remove(complete.Metadata.GroupKey);
        nextState.ClosedGroupKeys.Add(complete.Metadata.GroupKey);
        nextState.PendingOutputs.Add(new Phase8AGroupedVectorSumPendingOutput(result, readyCycle));
        return new ComponentRuntimeKernelAdvanceResult
        {
            Events = [new(TraceEventType.Compute,
                $"phase=4;exact_kernel=true;grouped_vector_sum_issue;group={complete.Metadata.GroupKey};contributors={string.Join(",", complete.Metadata.ExpectedContributors)};ready_cycle={readyCycle}",
                result.Id, result.Bits)]
        };
    }

    private static ComponentRuntimeKernelAdvanceResult Output(Packet packet, long cycle) => new()
    {
        Outputs = [new(Phase8AGroupedVectorSumContract.OutputPort, PacketClone.Clone(packet))],
        Events = [new(TraceEventType.Compute,
            $"phase=4;exact_kernel=true;grouped_vector_sum_output;group={packet.Metadata.GetValueOrDefault(Phase8AGroupedVectorSumContract.GroupKey, "")};output_cycle={cycle}",
            packet.Id, packet.Bits)]
    };

    private static Packet Sum(string componentId, Phase8AGroupedVectorSumGroup group)
    {
        var ordered = group.Metadata.ExpectedContributors.Select(id => group.Contributors[id]).ToList();
        var values = new double[group.VectorElements];
        for (var element = 0; element < values.Length; element++)
        {
            var sum = 0d;
            foreach (var packet in ordered) sum += packet.Values[element];
            values[element] = sum;
        }
        var first = ordered[0];
        var result = PacketClone.Clone(first);
        result.Id = group.Metadata.GroupKey + ":grouped-vector-sum";
        result.PacketType = PacketType.PartialSum;
        result.NumElements = values.Length;
        result.Bits = checked(values.Length * Math.Max(1, result.BitWidth));
        result.Precision = group.Metadata.DType;
        result.Values = values.ToList();
        result.SourceComponentId = componentId;
        result.CurrentComponentId = componentId;
        result.SourcePort = Phase8AGroupedVectorSumContract.OutputPort;
        result.DestinationPort = "";
        result.DependencyIds = ordered.Select(packet => packet.Id).ToList();
        result.VisitedComponents = ordered.SelectMany(packet => packet.VisitedComponents)
            .Append(componentId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.Metadata[Phase8AGroupedVectorSumContract.ContributorId] = "grouped-sum";
        Phase8ACollectiveMetadataCodec.ApplyOutputRoute(first, result);
        return result;
    }

    private static Phase8AGroupedVectorSumParsedMetadata ParseMetadata(Packet packet, int maxElements)
    {
        var issues = new List<ComponentRuntimeKernelIssueFact>();
        string Required(string key)
        {
            if (packet.Metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)) return raw.Trim();
            issues.Add(new("GroupedVectorSumMetadataMissing", "error", $"Packet '{packet.Id}' is missing required metadata '{key}'.", packet.Id));
            return "";
        }

        var operation = Required(Phase8AGroupedVectorSumContract.OperationKind);
        var groupKey = Required(Phase8AGroupedVectorSumContract.GroupKey);
        var contributorsRaw = Required(Phase8AGroupedVectorSumContract.ExpectedContributors);
        var contributorId = Required(Phase8AGroupedVectorSumContract.ContributorId);
        var dtypeRaw = Required(Phase8AGroupedVectorSumContract.DType);
        var mOffset = ParseRange(Required(Phase8AGroupedVectorSumContract.OutputMOffset), Phase8AGroupedVectorSumContract.OutputMOffset, 0, packet, issues);
        var mExtent = ParseRange(Required(Phase8AGroupedVectorSumContract.OutputMExtent), Phase8AGroupedVectorSumContract.OutputMExtent, 1, packet, issues);
        var nOffset = ParseRange(Required(Phase8AGroupedVectorSumContract.OutputNOffset), Phase8AGroupedVectorSumContract.OutputNOffset, 0, packet, issues);
        var nExtent = ParseRange(Required(Phase8AGroupedVectorSumContract.OutputNExtent), Phase8AGroupedVectorSumContract.OutputNExtent, 1, packet, issues);
        Phase8ACollectiveMetadataCodec.TryDecodeStringList(contributorsRaw, out var expected);
        if (!string.Equals(operation, Phase8AGroupedVectorSumContract.SumOperation, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("GroupedVectorSumOperationMismatch", "error", $"Packet '{packet.Id}' requests unsupported collective operation '{operation}'.", packet.Id));
        if (expected.Count == 0 || expected.Distinct(StringComparer.Ordinal).Count() != expected.Count)
            issues.Add(new("GroupedVectorSumExpectedContributorsInvalid", "error", $"Packet '{packet.Id}' must carry a non-empty ordered list of unique contributors.", packet.Id));
        if (expected.Count > 0 && !expected.Contains(contributorId, StringComparer.Ordinal))
            issues.Add(new("GroupedVectorSumUnexpectedContributor", "error", $"Contributor '{contributorId}' is not in the expected ordered contributor list.", packet.Id));
        if (!TryParsePrecision(dtypeRaw, out var dtype) || dtype == PrecisionKind.Any)
            issues.Add(new("GroupedVectorSumDTypeInvalid", "error", $"Packet '{packet.Id}' carries unsupported dtype '{dtypeRaw}'.", packet.Id));
        else if (packet.Precision != dtype)
            issues.Add(new("GroupedVectorSumPrecisionMismatch", "error", $"Packet '{packet.Id}' precision '{packet.Precision}' does not match metadata dtype '{dtype}'.", packet.Id));
        if (packet.Values.Count <= 0 || packet.Values.Count > maxElements || packet.NumElements != packet.Values.Count ||
            !PrecisionModel.TryGetDigitalBitWidth(dtype, out var dtypeBitWidth) || packet.BitWidth != dtypeBitWidth || packet.Bits != (long)packet.Values.Count * packet.BitWidth ||
            mExtent <= 0 || nExtent <= 0 || (long)mExtent * nExtent != packet.Values.Count)
            issues.Add(new("GroupedVectorSumShapeMismatch", "error", $"Packet '{packet.Id}' vector shape is inconsistent with values and output range.", packet.Id));
        if (packet.Values.Any(value => !double.IsFinite(value)))
            issues.Add(new("GroupedVectorSumValueInvalid", "error", $"Packet '{packet.Id}' contains NaN or infinity.", packet.Id));
        if (!Phase8AStageRouteMetadata.TryValidateBoundMetadata(packet, out var stageRouteReason))
            issues.Add(new("GroupedVectorSumStageRouteInvalid", "error", $"Packet '{packet.Id}' has invalid stage routing metadata: {stageRouteReason}.", packet.Id));
        if (!Phase8ACollectiveMetadataCodec.TryReadOutputRoute(packet, out var outputPathId, out var outputDestination, out var outputRoute, out var outputRouteReason))
            issues.Add(new("GroupedVectorSumOutputRouteInvalid", "error", $"Packet '{packet.Id}' has invalid output routing metadata: {outputRouteReason}.", packet.Id));
        return new Phase8AGroupedVectorSumParsedMetadata(
            issues.Count == 0 ? new Phase8AGroupedVectorSumMetadata(groupKey, expected, contributorId, mOffset, mExtent, nOffset, nExtent, dtype, outputPathId, outputDestination, outputRoute) : null,
            issues.AsReadOnly());
    }

    private static ComponentRuntimeKernelIssueFact? ValidateAgainstGroup(
        Packet packet,
        Phase8AGroupedVectorSumMetadata metadata,
        Phase8AGroupedVectorSumGroup group)
    {
        if (!metadata.ExpectedContributors.SequenceEqual(group.Metadata.ExpectedContributors, StringComparer.Ordinal))
            return new("GroupedVectorSumExpectedContributorsMismatch", "error", $"Group '{metadata.GroupKey}' changed its ordered contributor contract.", packet.Id);
        if (metadata.DType != group.Metadata.DType)
            return new("GroupedVectorSumPrecisionMismatch", "error", $"Group '{metadata.GroupKey}' changed dtype from '{group.Metadata.DType}' to '{metadata.DType}'.", packet.Id);
        if (metadata.MOffset != group.Metadata.MOffset || metadata.MExtent != group.Metadata.MExtent ||
            metadata.NOffset != group.Metadata.NOffset || metadata.NExtent != group.Metadata.NExtent)
            return new("GroupedVectorSumRangeMismatch", "error", $"Group '{metadata.GroupKey}' changed its explicit output range.", packet.Id);
        if (packet.Values.Count != group.VectorElements)
            return new("GroupedVectorSumShapeMismatch", "error", $"Group '{metadata.GroupKey}' contributor vector lengths differ.", packet.Id);
        if (!string.Equals(metadata.OutputRoutePathId, group.Metadata.OutputRoutePathId, StringComparison.Ordinal) ||
            !string.Equals(metadata.OutputDestinationComponentId, group.Metadata.OutputDestinationComponentId, StringComparison.Ordinal) ||
            !metadata.OutputRouteLinkIds.SequenceEqual(group.Metadata.OutputRouteLinkIds, StringComparer.Ordinal))
            return new("GroupedVectorSumOutputRouteMismatch", "error", $"Group '{metadata.GroupKey}' changed its exact output route.", packet.Id);
        return null;
    }

    private static ComponentRuntimeKernelInputResult Error(string code, string message, string packetId) => new()
    {
        Accepted = false,
        Issues = [new(code, "error", message, packetId)]
    };

    private static int ParseRange(string raw, string key, int minimum, Packet packet, List<ComponentRuntimeKernelIssueFact> issues)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= minimum) return value;
        issues.Add(new("GroupedVectorSumRangeMismatch", "error", $"Packet '{packet.Id}' metadata '{key}' must be an integer >= {minimum}.", packet.Id));
        return minimum - 1;
    }

    private static bool TryParsePrecision(string raw, out PrecisionKind precision)
    {
        if (Enum.TryParse(raw, true, out precision)) return true;
        precision = raw.Trim().ToLowerInvariant() switch
        {
            "fp8" => PrecisionKind.FP8_E4M3,
            "fp16" => PrecisionKind.FP16,
            "fp32" => PrecisionKind.FP32,
            "bf16" => PrecisionKind.BF16,
            "int8" => PrecisionKind.INT8,
            "int16" => PrecisionKind.INT16,
            "int32" => PrecisionKind.INT32,
            _ => PrecisionKind.Any
        };
        return precision != PrecisionKind.Any;
    }

    private static int ReadResourceInt(CompiledComponentExecutionContract contract, string key, int fallback) =>
        int.TryParse(contract.Resources.FirstOrDefault(resource => string.Equals(resource.Name, key, StringComparison.OrdinalIgnoreCase))?.CanonicalValue,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

internal sealed class Phase8AGroupedVectorSumState : IComponentRuntimeKernelState
{
    public Dictionary<string, Phase8AGroupedVectorSumGroup> Groups { get; } = new(StringComparer.Ordinal);
    public List<Phase8AGroupedVectorSumPendingOutput> PendingOutputs { get; } = [];
    public HashSet<string> ClosedGroupKeys { get; } = new(StringComparer.Ordinal);
    public int BufferedContributorCount => Groups.Values.Sum(group => group.Contributors.Count);
    public bool IsIdle => Groups.Count == 0 && PendingOutputs.Count == 0;

    public IComponentRuntimeKernelState DeepClone()
    {
        var clone = new Phase8AGroupedVectorSumState();
        foreach (var pair in Groups) clone.Groups[pair.Key] = pair.Value.DeepClone();
        clone.PendingOutputs.AddRange(PendingOutputs.Select(item => new Phase8AGroupedVectorSumPendingOutput(PacketClone.Clone(item.Packet), item.ReadyCycle)));
        clone.ClosedGroupKeys.UnionWith(ClosedGroupKeys);
        return clone;
    }
}

internal sealed class Phase8AGroupedVectorSumGroup
{
    public Phase8AGroupedVectorSumGroup(Phase8AGroupedVectorSumMetadata metadata, int vectorElements, long firstAcceptedCycle)
    {
        Metadata = metadata;
        VectorElements = vectorElements;
        FirstAcceptedCycle = firstAcceptedCycle;
    }

    public Phase8AGroupedVectorSumMetadata Metadata { get; }
    public int VectorElements { get; }
    public long FirstAcceptedCycle { get; }
    public Dictionary<string, Packet> Contributors { get; } = new(StringComparer.Ordinal);
    public bool IsComplete => Metadata.ExpectedContributors.All(Contributors.ContainsKey);

    public Phase8AGroupedVectorSumGroup DeepClone()
    {
        var clone = new Phase8AGroupedVectorSumGroup(Metadata with { ExpectedContributors = Metadata.ExpectedContributors.ToList(), OutputRouteLinkIds = Metadata.OutputRouteLinkIds.ToList() }, VectorElements, FirstAcceptedCycle);
        foreach (var pair in Contributors) clone.Contributors[pair.Key] = PacketClone.Clone(pair.Value);
        return clone;
    }
}

internal sealed record Phase8AGroupedVectorSumMetadata(
    string GroupKey,
    IReadOnlyList<string> ExpectedContributors,
    string ContributorId,
    int MOffset,
    int MExtent,
    int NOffset,
    int NExtent,
    PrecisionKind DType,
    string OutputRoutePathId,
    string OutputDestinationComponentId,
    IReadOnlyList<string> OutputRouteLinkIds);

internal sealed record Phase8AGroupedVectorSumParsedMetadata(
    Phase8AGroupedVectorSumMetadata? Metadata,
    IReadOnlyList<ComponentRuntimeKernelIssueFact> Issues);

internal sealed record Phase8AGroupedVectorSumPendingOutput(Packet Packet, long ReadyCycle);