namespace HardwareSim.Core;

public sealed partial class CycleSimulationEngine
{
    private static readonly CyclePhaseTrace[] DeterministicPhaseOrder =
    [
        new((int)CycleKernelPhase.ExternalEventInjection, nameof(CycleKernelPhase.ExternalEventInjection)),
        new((int)CycleKernelPhase.InputSampling, nameof(CycleKernelPhase.InputSampling)),
        new((int)CycleKernelPhase.ReadyCheck, nameof(CycleKernelPhase.ReadyCheck)),
        new((int)CycleKernelPhase.ArbitrationScheduling, nameof(CycleKernelPhase.ArbitrationScheduling)),
        new((int)CycleKernelPhase.ComputeProgressUpdate, nameof(CycleKernelPhase.ComputeProgressUpdate)),
        new((int)CycleKernelPhase.LinkTransferIssue, nameof(CycleKernelPhase.LinkTransferIssue)),
        new((int)CycleKernelPhase.BufferMemoryUpdate, nameof(CycleKernelPhase.BufferMemoryUpdate)),
        new((int)CycleKernelPhase.StateCommit, nameof(CycleKernelPhase.StateCommit)),
        new((int)CycleKernelPhase.MetricsAccumulation, nameof(CycleKernelPhase.MetricsAccumulation)),
        new((int)CycleKernelPhase.TraceRecord, nameof(CycleKernelPhase.TraceRecord))
    ];

    private sealed class DeterministicKernelState
    {
        public Dictionary<string, int> PendingPackets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> NextSourceInjectionCycles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Packet> PendingExactOperands { get; } = [];
        public bool UsesExactOperandInjection { get; set; }
        public List<ScheduledPacketRelease> ActiveScheduledReleases { get; } = [];
        public HashSet<string> CompletedScheduledOperations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int NextScheduledOperationIndex { get; set; }
        public int PacketIndex { get; set; }
        public Dictionary<string, Packet> PacketCatalog { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Packet> DeliveredPackets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Queue<Packet>> OutputQueues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RouterState> RouterStates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ReductionUnitState> ReductionStates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ProcessingItem>> Processing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<FlitFlight> InFlightFlits { get; } = [];
        public List<FlitFlight> PendingArrivalFlits { get; } = [];
        public Dictionary<string, int> RouterArbitrationCursors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RouteChoiceCursors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> MemoryUsedBits { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> MemoryRejectedWrites { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IPhaseSafeComponentRuntimeKernel> ComponentKernels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IComponentRuntimeKernelState> ComponentKernelStates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ActiveComponentKernels { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DeterministicKernelState Clone(bool copyOnWriteKernelStates = false)
        {
            var clone = new DeterministicKernelState
            {
                NextScheduledOperationIndex = NextScheduledOperationIndex,
                PacketIndex = PacketIndex,
                UsesExactOperandInjection = UsesExactOperandInjection
            };
            foreach (var pair in PendingPackets) clone.PendingPackets[pair.Key] = pair.Value;
            foreach (var pair in NextSourceInjectionCycles) clone.NextSourceInjectionCycles[pair.Key] = pair.Value;
            clone.PendingExactOperands.AddRange(copyOnWriteKernelStates ? PendingExactOperands : PendingExactOperands.Select(ClonePacket));
            foreach (var release in ActiveScheduledReleases)
            {
                clone.ActiveScheduledReleases.Add(new ScheduledPacketRelease { Operation = release.Operation, RemainingPackets = release.RemainingPackets });
            }
            foreach (var operationId in CompletedScheduledOperations) clone.CompletedScheduledOperations.Add(operationId);
            foreach (var pair in PacketCatalog) clone.PacketCatalog[pair.Key] = copyOnWriteKernelStates ? pair.Value : ClonePacket(pair.Value);
            foreach (var pair in DeliveredPackets) clone.DeliveredPackets[pair.Key] = copyOnWriteKernelStates ? pair.Value : ClonePacket(pair.Value);
            foreach (var pair in OutputQueues) clone.OutputQueues[pair.Key] = new Queue<Packet>(pair.Value.Select(ClonePacket));
            foreach (var pair in RouterStates) clone.RouterStates[pair.Key] = CloneRouterState(pair.Value);
            foreach (var pair in ReductionStates)
            {
                var reductionState = new ReductionUnitState();
                foreach (var packet in pair.Value.Inputs) reductionState.Inputs.Add(ClonePacket(packet));
                clone.ReductionStates[pair.Key] = reductionState;
            }
            foreach (var pair in Processing) clone.Processing[pair.Key] = pair.Value.Select(CloneProcessingItem).ToList();
            clone.InFlightFlits.AddRange(InFlightFlits.Select(CloneFlitFlight));
            clone.PendingArrivalFlits.AddRange(PendingArrivalFlits.Select(CloneFlitFlight));
            foreach (var pair in RouterArbitrationCursors) clone.RouterArbitrationCursors[pair.Key] = pair.Value;
            foreach (var pair in RouteChoiceCursors) clone.RouteChoiceCursors[pair.Key] = pair.Value;
            foreach (var pair in MemoryUsedBits) clone.MemoryUsedBits[pair.Key] = pair.Value;
            foreach (var pair in MemoryRejectedWrites) clone.MemoryRejectedWrites[pair.Key] = pair.Value;
            foreach (var pair in ComponentKernels) clone.ComponentKernels[pair.Key] = pair.Value;
            foreach (var pair in ComponentKernelStates) clone.ComponentKernelStates[pair.Key] = copyOnWriteKernelStates ? pair.Value : pair.Value.DeepClone();
            foreach (var componentId in ActiveComponentKernels) clone.ActiveComponentKernels.Add(componentId);
            return clone;
        }
    }
    private static SimulationResult RunDeterministicInternal(
        HardwareSimulationGraph graph,
        WorkloadSchedule? schedule,
        SimulationOptions? options,
        ProjectDirtyState? dirtyState,
        ComponentRuntimeKernelRegistrySnapshot? runtimeKernelRegistry = null,
        IReadOnlyList<Packet>? exactInitialOperands = null)
    {
        options ??= new SimulationOptions();
        var metricsOnly = options.CycleTraceMode == SimulationCycleTraceMode.MetricsOnly;
        var result = new SimulationResult { CycleTraceMode = options.CycleTraceMode };
        InitializeMetrics(graph, result.Metrics);

        if (graph.Components.Count == 0)
        {
            result.Completed = true;
            result.CompletionReason = "Empty hardware simulation graph completed without work.";
            result.Metrics.Global.TotalCycles = 0;
            if (!metricsOnly) result.TraceHash = CanonicalTraceHasher.Compute(result.Trace, DeterministicTraceConfig(options, graph, schedule, exactInitialOperands), options.DeterministicSeed);
            dirtyState?.MarkSimulationSucceeded();
            return result;
        }

        var components = graph.Components.OrderBy(component => component.Id, StringComparer.Ordinal).ToList();
        var sources = components.Where(component => component.Type == ComponentKind.WorkloadSource).ToList();
        var source = sources.FirstOrDefault()
            ?? throw new InvalidOperationException("SimulationGraph does not contain a WorkloadSource component.");
        var sink = components.FirstOrDefault(component => component.Type == ComponentKind.WorkloadSink)
            ?? throw new InvalidOperationException("SimulationGraph does not contain a WorkloadSink component.");
        var scheduledOperations = schedule?.Operations
            .OrderBy(operation => operation.StartCycle)
            .ThenBy(operation => operation.OperationId, StringComparer.Ordinal)
            .ToList() ?? [];
        var packetBits = sources.ToDictionary(
            sourceComponent => sourceComponent.Id,
            sourceComponent => sourceComponent.GetIntParameter("packet_bits", options.DefaultPacketBits),
            StringComparer.OrdinalIgnoreCase);
        if (!TryCreateComponentKernels(graph, runtimeKernelRegistry, out var componentKernels, out var componentKernelStates, out var kernelIssues))
        {
            result.Issues.AddRange(kernelIssues);
            result.Completed = false;
            result.CompletionReason = string.Join("; ", kernelIssues.Select(issue => issue.Message));
            if (!metricsOnly) result.TraceHash = CanonicalTraceHasher.Compute(result.Trace, DeterministicTraceConfig(options, graph, schedule, exactInitialOperands), options.DeterministicSeed);
            return result;
        }
        var state = InitializeDeterministicState(graph, sources, schedule, options, exactInitialOperands);
        foreach (var pair in componentKernels) state.ComponentKernels[pair.Key] = pair.Value;
        foreach (var pair in componentKernelStates) state.ComponentKernelStates[pair.Key] = pair.Value;

        for (long cycle = 0; cycle < options.MaxCycles; cycle++)
        {
            var record = new CycleTraceRecord
            {
                Cycle = cycle,
                Phases = DeterministicPhaseOrder.Select(phase => new CyclePhaseTrace(phase.Index, phase.Name)).ToList()
            };
            var next = state.Clone(metricsOnly);
            var memoryAccessesThisCycle = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            RunPhase0Injection(graph, sources, source, sink, cycle, packetBits, scheduledOperations, schedule, options, state, next, record, result.Metrics);
            RunPhase1InputSampling(graph, cycle, state, next, memoryAccessesThisCycle, record, result.Metrics, result.Issues);
            RunPhase2ReadyCheck(graph, cycle, state, next, record);
            RunPhase3Arbitration(graph, cycle, state, next, record);
            RunPhase4ComputeUpdate(graph, cycle, scheduledOperations, state, next, record, result.Metrics, result.Issues, metricsOnly);
            RunPhase5LinkTransferIssue(graph, sink.Id, cycle, components, state, next, record, result.Metrics, result.Issues);
            RunPhase6BufferMemoryUpdate(graph, next, result.Metrics);

            state = next;
            result.Metrics.Global.TotalCycles = cycle + 1;
            RunPhase8MetricsAccumulation(graph, state, result.Metrics);
            if (!metricsOnly)
            {
                EnrichTraceProvenance(graph, record, state.PacketCatalog);
                result.Trace.Cycles.Add(record);
            }

            if (IsCompleteAfterCommit(state, schedule, scheduledOperations))
            {
                result.Completed = true;
                result.CompletionReason = "All packets delivered or drained from the graph.";
                break;
            }
        }

        result.DeliveredPackets = state.DeliveredPackets.Values.OrderBy(packet => packet.Id, StringComparer.Ordinal).Select(ClonePacket).ToList().AsReadOnly();
        result.BottleneckReport = BottleneckAnalyzer.Analyze(result.Metrics);
        if (!result.Completed)
        {
            result.CompletionReason = $"Max cycle limit ({options.MaxCycles}) reached before all packets drained. {DescribeUndrainedState(state)}";
        }

        if (!metricsOnly) result.TraceHash = CanonicalTraceHasher.Compute(result.Trace, DeterministicTraceConfig(options, graph, schedule, exactInitialOperands), options.DeterministicSeed);
        if (result.Completed) dirtyState?.MarkSimulationSucceeded();
        return result;
    }

    private static IComponentRuntimeKernelState GetMutableKernelState(
        DeterministicKernelState current,
        DeterministicKernelState next,
        string componentId)
    {
        var currentState = current.ComponentKernelStates[componentId];
        var nextState = next.ComponentKernelStates[componentId];
        if (!ReferenceEquals(currentState, nextState)) return nextState;
        nextState = currentState.DeepClone();
        next.ComponentKernelStates[componentId] = nextState;
        return nextState;
    }

    private static bool TryCreateComponentKernels(
        HardwareSimulationGraph graph,
        ComponentRuntimeKernelRegistrySnapshot? runtimeKernelRegistry,
        out IReadOnlyDictionary<string, IPhaseSafeComponentRuntimeKernel> kernels,
        out IReadOnlyDictionary<string, IComponentRuntimeKernelState> states,
        out IReadOnlyList<SimulationIssue> issues)
    {
        var kernelMap = new Dictionary<string, IPhaseSafeComponentRuntimeKernel>(StringComparer.OrdinalIgnoreCase);
        var stateMap = new Dictionary<string, IComponentRuntimeKernelState>(StringComparer.OrdinalIgnoreCase);
        var issueList = new List<SimulationIssue>();
        var components = graph.Components.Where(component => component.ExecutionContract is not null).OrderBy(component => component.Id, StringComparer.Ordinal).ToList();
        if (components.Count == 0)
        {
            kernels = kernelMap;
            states = stateMap;
            issues = issueList;
            return true;
        }

        if (runtimeKernelRegistry is null)
        {
            issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelMissing, "simulation", "SimulationGraph contains component execution contracts but the engine has no frozen runtime kernel registry."));
            kernels = kernelMap;
            states = stateMap;
            issues = issueList;
            return false;
        }
        if (!string.Equals(graph.Provenance.ComponentRuntimeKernelRegistryHash, runtimeKernelRegistry.ContentHash, StringComparison.Ordinal))
        {
            issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelIncompatible, "simulation", "SimulationGraph registry snapshot hash does not match the engine registry snapshot."));
        }

        foreach (var component in components)
        {
            var contract = component.ExecutionContract!;
            var semanticHash = ComponentExecutionJson.ComputeContractHash(contract);
            if (!string.Equals(contract.ContractHash, semanticHash, StringComparison.Ordinal))
            {
                issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, component.Id, "Compiled execution contract hash does not match its semantic contents."));
                continue;
            }
            if (!string.Equals(contract.Provenance.RegistrySnapshotHash, runtimeKernelRegistry.ContentHash, StringComparison.Ordinal))
            {
                issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelIncompatible, component.Id, "Component execution contract registry hash does not match the engine registry snapshot."));
                continue;
            }

            var resolution = runtimeKernelRegistry.ResolveExact(contract.KernelId, contract.KernelVersion, contract.ContractSchemaId);
            if (!resolution.IsSuccess || resolution.Registration is null)
            {
                issueList.AddRange(resolution.Issues.Select(issue => KernelSimulationIssue(issue.Code, component.Id, issue.Message)));
                continue;
            }
            var registration = resolution.Registration;
            if (!string.Equals(contract.Provenance.KernelImplementationHash, registration.Descriptor.ImplementationHash, StringComparison.Ordinal))
            {
                issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelIncompatible, component.Id, "Component execution contract implementation hash does not match the frozen kernel descriptor."));
                continue;
            }
            var liveDescriptor = registration.Factory.Descriptor;
            if (liveDescriptor is null ||
                !string.Equals(liveDescriptor.KernelId, registration.Descriptor.KernelId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(liveDescriptor.KernelVersion, registration.Descriptor.KernelVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(liveDescriptor.ContractSchemaId, registration.Descriptor.ContractSchemaId, StringComparison.Ordinal) ||
                !string.Equals(liveDescriptor.ImplementationHash, registration.Descriptor.ImplementationHash, StringComparison.Ordinal))
            {
                issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelIncompatible, component.Id, "Runtime kernel factory identity changed after the registry snapshot was frozen."));
                continue;
            }

            try
            {
                var created = registration.Factory.CreateKernel(new ComponentRuntimeKernelCreateContext(component.Id, contract));
                if (created is not IPhaseSafeComponentRuntimeKernel kernel)
                {
                    issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelIncompatible, component.Id, "Registered runtime kernel does not implement the phase-safe current/next interface."));
                    continue;
                }
                var initial = kernel.CreateInitialState(contract);
                var clone = initial?.DeepClone();
                if (initial is null || clone is null || ReferenceEquals(initial, clone))
                {
                    issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, component.Id, "Runtime kernel initial state must support a non-aliased deep clone."));
                    continue;
                }
                kernelMap[component.Id] = kernel;
                stateMap[component.Id] = initial;
            }
            catch (Exception exception)
            {
                issueList.Add(KernelSimulationIssue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, component.Id, $"Runtime kernel initialization failed: {exception.Message}"));
            }
        }

        kernels = kernelMap;
        states = stateMap;
        issues = issueList;
        return issueList.Count == 0;
    }

    private static SimulationIssue KernelSimulationIssue(string code, string componentId, string message) =>
        new(code, "error", 0, componentId, null, null, null, message);

    private static DeterministicKernelState InitializeDeterministicState(
        HardwareSimulationGraph graph,
        IReadOnlyList<SimComponentDef> sources,
        WorkloadSchedule? schedule,
        SimulationOptions options,
        IReadOnlyList<Packet>? exactInitialOperands)
    {
        var state = new DeterministicKernelState();
        foreach (var component in graph.Components.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            state.OutputQueues[component.Id] = new Queue<Packet>();
            state.Processing[component.Id] = [];
            if (component.Type == ComponentKind.Router && component.ExecutionContract is null) state.RouterStates[component.Id] = new RouterState(RouterVirtualChannels(component));
            if (component.Type == ComponentKind.ReductionUnit && component.ExecutionContract is null) state.ReductionStates[component.Id] = new ReductionUnitState();
            if (component.Type == ComponentKind.Memory)
            {
                state.MemoryUsedBits[component.Id] = 0;
                state.MemoryRejectedWrites[component.Id] = 0;
            }
        }

        if (exactInitialOperands is not null)
        {
            state.UsesExactOperandInjection = true;
            state.PendingExactOperands.AddRange(exactInitialOperands
                .OrderBy(packet => packet.InjectionCycle)
                .ThenBy(packet => packet.Id, StringComparer.Ordinal)
                .Select(ClonePacket));
        }
        else if (schedule is null)
        {
            foreach (var source in sources.OrderBy(source => source.Id, StringComparer.Ordinal))
            {
                state.PendingPackets[source.Id] = source.GetIntParameter("inject_count", source.GetIntParameter("packet_count", options.DefaultInjectCount));
                state.NextSourceInjectionCycles[source.Id] = Math.Max(0, source.GetIntParameter("initial_injection_cycle", 0));
            }
        }

        return state;
    }
    private static void RunPhase0Injection(
        HardwareSimulationGraph graph,
        IReadOnlyList<SimComponentDef> sources,
        SimComponentDef scheduleSource,
        SimComponentDef sink,
        long cycle,
        IReadOnlyDictionary<string, int> packetBits,
        IReadOnlyList<ScheduledOperation> scheduledOperations,
        WorkloadSchedule? schedule,
        SimulationOptions options,
        DeterministicKernelState current,
        DeterministicKernelState next,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        if (current.UsesExactOperandInjection)
        {
            foreach (var pending in current.PendingExactOperands
                         .Where(packet => packet.InjectionCycle <= cycle)
                         .OrderBy(packet => packet.InjectionCycle)
                         .ThenBy(packet => packet.Id, StringComparer.Ordinal))
            {
                var sourceComponent = graph.FindComponent(pending.SourceComponentId)
                    ?? throw new InvalidOperationException("Exact operand source component is missing: " + pending.SourceComponentId);
                if (options.CycleTraceMode == SimulationCycleTraceMode.MetricsOnly &&
                    next.OutputQueues[pending.SourceComponentId].Count >= QueueCapacity(sourceComponent))
                {
                    RecordStall(metrics, pending.SourceComponentId, StallReason.OutputBufferFull);
                    record.Events.Add(new(TraceEventType.Stall, PacketId: pending.Id, ComponentId: pending.SourceComponentId,
                        Bits: pending.Bits, Detail: $"{StallReason.OutputBufferFull};stall_reason={StallReason.OutputBufferFull};queue_capacity={QueueCapacity(sourceComponent)}"));
                    continue;
                }
                var exact = ClonePacket(pending);
                if (!TryEnqueueOutputPacket(graph, exact.SourceComponentId, exact, next.OutputQueues, record, metrics)) continue;
                next.PacketCatalog[exact.Id] = ClonePacket(exact);
                next.PendingExactOperands.RemoveAll(packet => string.Equals(packet.Id, exact.Id, StringComparison.Ordinal));
                metrics.Global.PacketsInjected++;
                metrics.Components[exact.SourceComponentId].ActiveCycles++;
                record.Events.Add(new(
                    TraceEventType.PacketInjection,
                    PacketId: exact.Id,
                    ComponentId: exact.SourceComponentId,
                    Bits: exact.Bits,
                    Detail: $"phase=0;visible_cycle={cycle + 1};exact_operand=true;requested_injection_cycle={exact.InjectionCycle};packet_type={exact.PacketType};total_bits={exact.TotalBits};request_id={exact.RequestId}"));
            }
            return;
        }

        if (schedule is null)
        {
            foreach (var source in sources.OrderBy(source => source.Id, StringComparer.Ordinal))
            {
                if (current.PendingPackets.GetValueOrDefault(source.Id) <= 0 || cycle < current.NextSourceInjectionCycles.GetValueOrDefault(source.Id)) continue;
                var injectInterval = Math.Max(1, source.GetIntParameter("inject_interval", options.DefaultInjectInterval));
                var currentPacketIndex = next.PacketIndex;
                var totalForSource = source.GetIntParameter("inject_count", source.GetIntParameter("packet_count", options.DefaultInjectCount));
                var injectedFromSource = totalForSource - current.PendingPackets[source.Id];
                var packetType = ParsePacketType(source.Parameters.GetValueOrDefault("packet_type", PacketType.Activation.ToString()));
                var memoryOperation = ParseMemoryOperation(source.Parameters.GetValueOrDefault("memory_operation", ""));
                var addressBase = source.GetIntParameter("memory_address_base", 0);
                var addressStride = source.GetIntParameter("memory_address_stride", Math.Max(1, packetBits[source.Id]));
                var bitWidth = Math.Max(1, source.GetIntParameter("packet_bit_width", 8));
                var sourcePacket = new Packet
                {
                    Id = source.Parameters.GetValueOrDefault("packet_id", sources.Count == 1 ? $"P_{currentPacketIndex}" : $"{source.Id}_P_{currentPacketIndex}"),
                    NumElements = Math.Max(1, source.GetIntParameter("packet_num_elements", Math.Max(1, packetBits[source.Id] / bitWidth))),
                    BitWidth = bitWidth,
                    Bits = packetBits[source.Id],
                    Precision = ParsePrecision(source.Parameters.GetValueOrDefault("packet_precision", PrecisionKind.INT8.ToString())),
                    PacketType = packetType,
                    RequestId = $"{source.Id}_REQ_{currentPacketIndex:000000}",
                    MemoryOperation = memoryOperation,
                    MemoryAddress = addressBase + (long)currentPacketIndex * addressStride,
                    SourceComponentId = source.Id,
                    SourcePort = source.Parameters.GetValueOrDefault("source_port", "out"),
                    DestinationComponentId = sink.Id,
                    CurrentComponentId = source.Id,
                    CreatedCycle = cycle,
                    InjectionCycle = cycle,
                    VisitedComponents = [source.Id],
                    Values = PacketValues(source, injectedFromSource)
                };
                if (!TryEnqueueOutputPacket(graph, source.Id, sourcePacket, next.OutputQueues, record, metrics)) continue;
                next.PacketCatalog[sourcePacket.Id] = ClonePacket(sourcePacket);
                next.PacketIndex++;
                next.PendingPackets[source.Id] = current.PendingPackets[source.Id] - 1;
                next.NextSourceInjectionCycles[source.Id] = cycle + injectInterval;
                metrics.Global.PacketsInjected++;
                metrics.Components[source.Id].ActiveCycles++;
                record.Events.Add(new(TraceEventType.PacketInjection, PacketId: sourcePacket.Id, ComponentId: source.Id, Bits: sourcePacket.Bits,
                    Detail: $"phase=0;visible_cycle={cycle + 1};packet_type={sourcePacket.PacketType};total_bits={sourcePacket.TotalBits};inject_interval={injectInterval};inject_index={injectedFromSource};request_id={sourcePacket.RequestId};memory_address={sourcePacket.MemoryAddress}"));
            }
            return;
        }

        var nextIndex = current.NextScheduledOperationIndex;
        while (nextIndex < scheduledOperations.Count && scheduledOperations[nextIndex].StartCycle <= cycle)
        {
            var operation = scheduledOperations[nextIndex++];
            next.ActiveScheduledReleases.Add(new ScheduledPacketRelease { Operation = operation, RemainingPackets = operation.PacketCount });
            if (metrics.Components.TryGetValue(operation.ComponentId, out var componentMetrics)) componentMetrics.ActiveCycles++;
            record.Events.Add(new(TraceEventType.OperationStart, ComponentId: operation.ComponentId, Bits: operation.PacketBits * operation.PacketCount,
                Detail: $"phase=0;{operation.OperationId}:{operation.OperationKind}"));
        }
        next.NextScheduledOperationIndex = nextIndex;

        var activeRelease = next.ActiveScheduledReleases
            .OrderBy(release => release.Operation.StartCycle)
            .ThenBy(release => release.Operation.OperationId, StringComparer.Ordinal)
            .FirstOrDefault(release => release.RemainingPackets > 0);
        if (activeRelease is null) return;
        var packetIndex = next.PacketIndex;
        var scheduledPacket = new Packet
        {
            Id = $"{activeRelease.Operation.OperationId}_P_{packetIndex}",
            Bits = activeRelease.Operation.PacketBits,
            PacketType = PacketType.Activation,
            RequestId = $"{activeRelease.Operation.OperationId}_REQ_{packetIndex:000000}",
            MemoryOperation = MemoryOperationType.Read,
            MemoryAddress = packetIndex * Math.Max(1, activeRelease.Operation.PacketBits),
            SourceComponentId = scheduleSource.Id,
            DestinationComponentId = sink.Id,
            CurrentComponentId = scheduleSource.Id,
            CreatedCycle = cycle,
            InjectionCycle = cycle,
            WorkloadOpId = activeRelease.Operation.OperationId,
            TensorId = activeRelease.Operation.TensorId,
            TileId = activeRelease.Operation.TileId,
            VisitedComponents = [scheduleSource.Id],
            Values = PacketValues(scheduleSource, packetIndex)
        };
        if (!TryEnqueueOutputPacket(graph, scheduleSource.Id, scheduledPacket, next.OutputQueues, record, metrics)) return;
        next.PacketCatalog[scheduledPacket.Id] = ClonePacket(scheduledPacket);
        next.PacketIndex++;
        activeRelease.RemainingPackets--;
        next.ActiveScheduledReleases.RemoveAll(release => release.RemainingPackets <= 0);
        metrics.Global.PacketsInjected++;
        metrics.Components[scheduleSource.Id].ActiveCycles++;
        record.Events.Add(new(TraceEventType.PacketInjection, PacketId: scheduledPacket.Id, ComponentId: scheduleSource.Id, Bits: scheduledPacket.Bits,
            Detail: $"phase=0;visible_cycle={cycle + 1};operation={activeRelease.Operation.OperationId};packet_type={scheduledPacket.PacketType};total_bits={scheduledPacket.TotalBits};request_id={scheduledPacket.RequestId}"));
    }
    private static void RunPhase1InputSampling(
        HardwareSimulationGraph graph,
        long cycle,
        DeterministicKernelState current,
        DeterministicKernelState next,
        Dictionary<string, int> memoryAccessesThisCycle,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues)
    {
        var readyGroups = current.PendingArrivalFlits
            .Concat(current.InFlightFlits.Where(flight => flight.ArrivalCycle <= cycle))
            .GroupBy(flight => new { LinkId = flight.Link.Id, flight.Packet.Id })
            .Select(group => group
                .GroupBy(flight => flight.Flit.Id, StringComparer.Ordinal)
                .Select(flitGroup => flitGroup.OrderBy(flight => flight.ArrivalCycle).First())
                .OrderBy(flight => flight.ArrivalCycle)
                .ThenBy(flight => flight.Link.Id, StringComparer.Ordinal)
                .ThenBy(flight => flight.Packet.Id, StringComparer.Ordinal)
                .ThenBy(flight => flight.Flit.FlitIndex)
                .ToList())
            .OrderBy(group => group.Max(flight => flight.ArrivalCycle))
            .ThenBy(group => group[0].Link.Id, StringComparer.Ordinal)
            .ThenBy(group => group[0].Packet.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var groupFlights in readyGroups)
        {
            var first = groupFlights[0];
            var arrivingNow = current.InFlightFlits
                .Where(flight => flight.ArrivalCycle <= cycle && SamePacketLink(flight, first))
                .OrderBy(flight => flight.Flit.FlitIndex)
                .ToList();
            foreach (var flight in arrivingNow)
            {
                RemoveMatchingFlitFlight(next.InFlightFlits, flight);
            }

            var expectedFlits = first.Flit.TotalFlits;
            var complete = expectedFlits > 0 &&
                groupFlights.Select(flight => flight.Flit.FlitIndex).Distinct().Count() == expectedFlits &&
                groupFlights.All(flight => flight.Flit.TotalFlits == expectedFlits);
            if (!complete)
            {
                AddPendingArrivalFlits(next.PendingArrivalFlits, arrivingNow);
                continue;
            }

            var destination = graph.FindComponent(first.Link.Destination.ComponentId)
                ?? throw new InvalidOperationException($"Missing destination component '{first.Link.Destination.ComponentId}'.");
            var packet = ClonePacket(first.Packet);
            var flits = groupFlights.OrderBy(flight => flight.Flit.FlitIndex).Select(flight => CloneFlit(flight.Flit)).ToList();
            if (TryAcceptArrivedPacket(graph, destination, packet, flits, first.Link, cycle, current, next, memoryAccessesThisCycle, record, metrics, issues))
            {
                RemoveMatchingFlitFlights(next.PendingArrivalFlits, groupFlights);
                RemoveMatchingFlitFlights(next.InFlightFlits, groupFlights);
                continue;
            }

            AddPendingArrivalFlits(next.PendingArrivalFlits, arrivingNow);
        }
    }

    private static bool TryAcceptArrivedPacket(
        HardwareSimulationGraph graph,
        SimComponentDef destination,
        Packet packet,
        IReadOnlyList<Flit> flits,
        SimLinkDef link,
        long cycle,
        DeterministicKernelState current,
        DeterministicKernelState next,
        Dictionary<string, int> memoryAccessesThisCycle,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues)
    {
        if (destination.Type == ComponentKind.WorkloadSink)
        {
            AcceptPacketAtDestination(packet, link, destination, record, metrics);
            packet.DeliveredCycle = cycle;
            packet.ArrivalCycle = cycle;
            metrics.Global.PacketsDelivered++;
            metrics.Global.FlitsDelivered += flits.Count;
            metrics.Components[destination.Id].ActiveCycles++;
            next.DeliveredPackets[packet.Id] = ClonePacket(packet);
            return true;
        }

        if (destination.Type == ComponentKind.Router && destination.ExecutionContract is null)
        {
            var hasBranchCapability =
                string.Equals(
                    destination.Parameters.GetValueOrDefault(Phase8ATopologyExecutionStrategies.RouterBranchCapabilityParameter, ""),
                    Phase8ATopologyExecutionStrategies.RouterBranchCapabilityValue,
                    StringComparison.Ordinal) &&
                string.Equals(packet.DestinationComponentId, destination.Id, StringComparison.Ordinal) &&
                packet.Metadata.ContainsKey(Phase8ACollectiveRuntimeMetadata.MulticastTargets);
            if (hasBranchCapability)
            {
                if (!Phase8ABranchMulticastKernel.TryCreateImmediateRouterClones(
                        destination.Id,
                        packet,
                        out var clones,
                        out var branchIssues))
                {
                    foreach (var issue in branchIssues)
                    {
                        issues.Add(new SimulationIssue(
                            issue.Code,
                            issue.Severity,
                            cycle,
                            destination.Id,
                            packet.Id,
                            link.Id,
                            null,
                            issue.Message));
                    }
                    AcceptPacketAtDestination(packet, link, destination, record, metrics);
                    packet.ArrivalCycle = cycle;
                    metrics.Global.FlitsDelivered += flits.Count;
                    metrics.Components[destination.Id].ActiveCycles++;
                    return true;
                }

                foreach (var clone in clones)
                {
                    var cloneFlits = BuildTransportFlits(graph, clone);
                    if (!TryEnqueueRouterInputFlits(
                            graph,
                            destination,
                            link,
                            clone,
                            cloneFlits,
                            next.RouterStates[destination.Id],
                            record,
                            metrics))
                        return false;
                    record.Events.Add(new TraceEvent(
                        TraceEventType.Compute,
                        PacketId: clone.Id,
                        ComponentId: destination.Id,
                        Bits: clone.Bits,
                        Detail: $"phase=1;explicit_router_branch=true;parent={packet.Id};consumer={clone.DestinationComponentId}"));
                }

                AcceptPacketAtDestination(packet, link, destination, record, metrics);
                packet.ArrivalCycle = cycle;
                metrics.Global.FlitsDelivered += flits.Count;
                metrics.Components[destination.Id].ActiveCycles++;
                return true;
            }

            if (!TryEnqueueRouterInputFlits(graph, destination, link, packet, flits, next.RouterStates[destination.Id], record, metrics)) return false;
            AcceptPacketAtDestination(packet, link, destination, record, metrics);
            packet.ArrivalCycle = cycle;
            metrics.Global.FlitsDelivered += flits.Count;
            metrics.Components[destination.Id].ActiveCycles++;
            return true;
        }

        if (destination.Type == ComponentKind.ReductionUnit && destination.ExecutionContract is null)
        {
            if (!TryEnqueueReductionInput(destination, packet, link, next.ReductionStates[destination.Id], record, metrics)) return false;
            AcceptPacketAtDestination(packet, link, destination, record, metrics);
            packet.ArrivalCycle = cycle;
            metrics.Global.FlitsDelivered += flits.Count;
            metrics.Components[destination.Id].ActiveCycles++;
            return true;
        }

        if (destination.ExecutionContract is not null)
        {
            var contract = destination.ExecutionContract;
            var kernel = current.ComponentKernels[destination.Id];
            var immediateAfterCommit = ReferenceEquals(current, next);
            var currentState = current.ComponentKernelStates[destination.Id];
            var nextState = immediateAfterCommit
                ? currentState.DeepClone()
                : GetMutableKernelState(current, next, destination.Id);
            var context = new ComponentRuntimeKernelCycleContext(cycle, destination, contract);
            var inputPortName = ResolveKernelInputPort(contract, link.Destination.PortName);
            if (!kernel.CanAccept(context, currentState, inputPortName))
            {
                RecordStall(metrics, destination.Id, StallReason.OutputBufferFull);
                record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: destination.Id, LinkId: link.Id, Bits: packet.Bits,
                    Detail: "phase=1;component_kernel_input_not_ready;stall_reason=OutputBufferFull"));
                return false;
            }

            var input = kernel.SampleInput(context, currentState, nextState, new ComponentRuntimeKernelInput(inputPortName, ClonePacket(packet)));
            AddKernelEventFacts(record, destination.Id, input.Events);
            AddKernelIssueFacts(issues, destination.Id, cycle, input.Issues);
            if (input.Issues.Any(fact => string.Equals(fact.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            {
                AcceptPacketAtDestination(packet, link, destination, record, metrics);
                packet.ArrivalCycle = cycle;
                metrics.Global.FlitsDelivered += flits.Count;
                metrics.Components[destination.Id].ActiveCycles++;
                record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: destination.Id, LinkId: link.Id, Bits: packet.Bits,
                    Detail: "phase=1;component_kernel_input_error;packet_consumed=true"));
                return true;
            }
            if (!input.Accepted)
            {
                RecordStall(metrics, destination.Id, StallReason.OutputBufferFull);
                record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: destination.Id, LinkId: link.Id, Bits: packet.Bits,
                    Detail: $"phase=1;component_kernel_rejected;stall_reason={input.StallReason}"));
                return false;
            }

            if (immediateAfterCommit)
            {
                next.ComponentKernelStates[destination.Id] = nextState;
            }
            if (!nextState.IsIdle) next.ActiveComponentKernels.Add(destination.Id);
            AcceptPacketAtDestination(packet, link, destination, record, metrics);
            packet.ArrivalCycle = cycle;
            metrics.Global.FlitsDelivered += flits.Count;
            metrics.Components[destination.Id].ActiveCycles++;
            return true;
        }

        if (destination.Type == ComponentKind.SoftmaxUnit &&
            (current.Processing[destination.Id].Count > 0 || current.OutputQueues[destination.Id].Count > 0))
        {
            RecordStall(metrics, destination.Id, StallReason.DependencyNotReady);
            record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: destination.Id, LinkId: link.Id,
                Bits: packet.Bits, Detail: $"phase=1;{StallReason.DependencyNotReady};stall_reason={StallReason.DependencyNotReady};state=ComputingOrWaitingOutput"));
            return false;
        }

        var memoryBankId = "";
        var memoryOperation = destination.Type == ComponentKind.Memory ? DetermineMemoryOperation(destination, packet) : MemoryOperationType.None;
        if (destination.Type == ComponentKind.Memory)
        {
            EnsureMemoryRequestMetadata(packet, memoryOperation);
            if (!TryReserveMemoryAccess(destination, packet, memoryAccessesThisCycle, metrics, out memoryBankId))
            {
                RecordStall(metrics, destination.Id, StallReason.MemoryBusy);
                if (!string.IsNullOrWhiteSpace(memoryBankId))
                {
                    metrics.Components[destination.Id].MemoryBankBusyCycles[memoryBankId] =
                        metrics.Components[destination.Id].MemoryBankBusyCycles.GetValueOrDefault(memoryBankId) + 1;
                }
                record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: destination.Id,
                    LinkId: link.Id, Source: link.Source.ComponentId, Destination: destination.Id,
                    Bits: packet.Bits, Detail: string.IsNullOrWhiteSpace(memoryBankId)
                        ? $"phase=1;{StallReason.MemoryBusy};stall_reason={StallReason.MemoryBusy};request_id={packet.RequestId};op_type={memoryOperation.ToString().ToLowerInvariant()}"
                        : $"phase=1;{StallReason.MemoryBusy}:bank={memoryBankId};stall_reason={StallReason.MemoryBusy};request_id={packet.RequestId};op_type={memoryOperation.ToString().ToLowerInvariant()}"));
                return false;
            }
        }

        AcceptPacketAtDestination(packet, link, destination, record, metrics);
        packet.ArrivalCycle = cycle;
        metrics.Global.FlitsDelivered += flits.Count;
        var latency = destination.Type == ComponentKind.Memory ? MemoryLatency(destination, memoryOperation) : ProcessingLatency(destination);
        if (latency > 0)
        {
            next.Processing[destination.Id].Add(new ProcessingItem(packet, cycle + latency, memoryOperation, memoryBankId));
            metrics.Components[destination.Id].ActiveCycles++;
            record.Events.Add(new(TraceEventType.Compute, PacketId: packet.Id, ComponentId: destination.Id,
                Detail: destination.Type == ComponentKind.Memory
                    ? $"phase=1;processing_until={cycle + latency};request_id={packet.RequestId};op_type={memoryOperation.ToString().ToLowerInvariant()};memory_bank={memoryBankId};latency={latency}"
                    : $"phase=1;{ProcessingStartDetail(destination, packet, cycle, latency)}"));
            return true;
        }

        return TryEnqueueOutputPacket(graph, destination.Id, packet, next.OutputQueues, record, metrics, link.Id);
    }

    private static void RunPhase2ReadyCheck(
        HardwareSimulationGraph graph,
        long cycle,
        DeterministicKernelState current,
        DeterministicKernelState next,
        CycleTraceRecord record)
    {
        foreach (var reduction in graph.Components.Where(component => component.Type == ComponentKind.ReductionUnit && component.ExecutionContract is null).OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            if (!current.ReductionStates.TryGetValue(reduction.Id, out var state) || current.Processing[reduction.Id].Count > 0) continue;
            var numInputs = ReductionNumInputs(reduction);
            if (state.Inputs.Count < numInputs) continue;
            var batch = state.Inputs.Take(numInputs).Select(ClonePacket).ToList();
            RemoveReductionInputs(next.ReductionStates[reduction.Id], batch.Select(packet => packet.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
            var output = CreateReductionOutput(reduction, batch, cycle, out var sum);
            var latency = ReductionLatency(reduction);
            next.Processing[reduction.Id].Add(new ProcessingItem(output, cycle + latency));
            record.Events.Add(new(TraceEventType.Compute, PacketId: output.Id, ComponentId: reduction.Id, Bits: output.Bits,
                Detail: $"phase=2;state=WaitingInput->Computing;inputs={numInputs};accumulate_latency={latency};processing_until={cycle + latency};partial_sum={FormatDouble(sum)}"));
        }
    }

    private static void RunPhase3Arbitration(HardwareSimulationGraph graph, long cycle, DeterministicKernelState current, DeterministicKernelState next, CycleTraceRecord record)
    {
        _ = graph;
        _ = cycle;
        _ = current;
        _ = next;
        _ = record;
    }
    private static void RunPhase4ComputeUpdate(
        HardwareSimulationGraph graph,
        long cycle,
        IReadOnlyList<ScheduledOperation> scheduledOperations,
        DeterministicKernelState current,
        DeterministicKernelState next,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues,
        bool metricsOnly)
    {
        foreach (var pair in current.Processing.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var componentId = pair.Key;
            var component = graph.FindComponent(componentId)!;
            foreach (var item in pair.Value.Where(item => item.ReadyCycle <= cycle).OrderBy(item => item.ReadyCycle).ThenBy(item => item.Packet.Id, StringComparer.Ordinal).ToList())
            {
                var completed = CloneProcessingItem(item);
                if (component.Type == ComponentKind.Memory)
                {
                    if (!TryCompleteMemoryProcessingDeterministic(component, completed, current, next, cycle, record, metrics, issues))
                    {
                        RemoveProcessingItem(next.Processing[componentId], item);
                        continue;
                    }
                }
                else if (!completed.RuntimeApplied)
                {
                    ApplyComponentRuntimeIfNeeded(component, completed.Packet, record, metrics);
                    completed.RuntimeApplied = true;
                }

                if (!TryEnqueueOutputPacket(graph, componentId, completed.Packet, next.OutputQueues, record, metrics))
                {
                    ReplaceProcessingItem(next.Processing[componentId], item, completed);
                    continue;
                }
                RemoveProcessingItem(next.Processing[componentId], item);
                metrics.Components[componentId].OutputTrafficBits += completed.Packet.Bits;
                record.Events.Add(new(TraceEventType.Compute, PacketId: completed.Packet.Id, ComponentId: componentId,
                    Detail: $"phase=4;{ProcessingCompleteDetail(component, completed.Packet)}"));
            }
        }

        var kernelComponents = metricsOnly
            ? next.ActiveComponentKernels.OrderBy(id => id, StringComparer.Ordinal).Select(graph.FindComponent).Where(component => component?.ExecutionContract is not null).Select(component => component!)
            : graph.Components.Where(component => component.ExecutionContract is not null).OrderBy(component => component.Id, StringComparer.Ordinal);
        foreach (var component in kernelComponents)
        {
            var contract = component.ExecutionContract!;
            var kernel = current.ComponentKernels[component.Id];
            var currentState = current.ComponentKernelStates[component.Id];
            var nextState = GetMutableKernelState(current, next, component.Id);
            var outputCapacity = Math.Max(1, Math.Min(QueueCapacity(component), contract.Queues.OutputDepth));
            var availableOutputSlots = Math.Max(0, outputCapacity - next.OutputQueues[component.Id].Count);
            var cycleContext = new ComponentRuntimeKernelCycleContext(cycle, component, contract)
            {
                AvailableOutputSlots = availableOutputSlots
            };
            var advance = kernel.Advance(cycleContext, currentState, nextState, availableOutputSlots > 0);
            AddKernelEventFacts(record, component.Id, advance.Events);
            AddKernelIssueFacts(issues, component.Id, cycle, advance.Issues);
            if (advance.Issues.Any(fact => string.Equals(fact.Severity, "error", StringComparison.OrdinalIgnoreCase))) continue;
            if (advance.Outputs.Count > availableOutputSlots)
            {
                issues.Add(new SimulationIssue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, "error", cycle, component.Id, null, null, null, $"Runtime kernel emitted {advance.Outputs.Count} output(s) with only {availableOutputSlots} engine-owned queue slot(s) available."));
                continue;
            }

            foreach (var output in advance.Outputs.OrderBy(item => item.OutputPortName, StringComparer.Ordinal).ThenBy(item => item.Packet.Id, StringComparer.Ordinal))
            {
                var outputPort = contract.Ports.FirstOrDefault(port =>
                    port.Direction == PortDirection.Output &&
                    string.Equals(port.Name, output.OutputPortName, StringComparison.Ordinal));
                if (outputPort is null)
                {
                    issues.Add(new SimulationIssue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, "error", cycle, component.Id, output.Packet.Id, null, null, $"Runtime kernel emitted through undeclared output port '{output.OutputPortName}'."));
                    continue;
                }

                var staged = ClonePacket(output.Packet);
                staged.SourcePort = ResolveKernelOutputPort(graph, component, outputPort.Name);
                staged.DestinationPort = "";
                if (!TryEnqueueOutputPacket(graph, component.Id, staged, next.OutputQueues, record, metrics))
                {
                    issues.Add(new SimulationIssue(ComponentExecutionIssueCodes.KernelConfigurationInvalid, "error", cycle, component.Id, staged.Id, null, null, "Runtime kernel emitted more output than the compiled queue contract can accept."));
                    break;
                }
                metrics.Components[component.Id].OutputTrafficBits += staged.Bits;
            }

            if (advance.EnergyContributions.Count > 0)
            {
                foreach (var contribution in advance.EnergyContributions)
                {
                    AccountKernelEnergy(component, contribution, metrics);
                }
            }
            else if (advance.DynamicEnergyPicojoules > 0)
            {
                AccountKernelEnergy(component, advance.DynamicEnergyPicojoules, metrics);
            }
            AccountKernelNamedMetrics(component, advance.NamedMetricContributions, metrics);
            if (!currentState.IsIdle || !nextState.IsIdle || advance.Outputs.Count > 0)
            {
                metrics.Components[component.Id].ActiveCycles++;
            }
            if (nextState.IsIdle) next.ActiveComponentKernels.Remove(component.Id);
            else next.ActiveComponentKernels.Add(component.Id);
        }

        foreach (var operation in scheduledOperations.Where(operation => operation.EndCycle <= cycle && !current.CompletedScheduledOperations.Contains(operation.OperationId)).OrderBy(operation => operation.EndCycle).ThenBy(operation => operation.OperationId, StringComparer.Ordinal))
        {
            next.CompletedScheduledOperations.Add(operation.OperationId);
            if (metrics.Components.TryGetValue(operation.ComponentId, out var componentMetrics))
            {
                var component = graph.FindComponent(operation.ComponentId);
                var energyPerCycle = component?.GetDoubleParameter("operation_energy_pj_per_cycle", 0.1) ?? 0.1;
                var energy = Math.Max(0, operation.EndCycle - operation.StartCycle) * energyPerCycle;
                componentMetrics.Energy += energy;
                metrics.Global.ComputeEnergy += energy;
                metrics.Global.TotalEnergy += energy;
            }
            record.Events.Add(new(TraceEventType.OperationComplete, ComponentId: operation.ComponentId, Bits: operation.PacketBits * operation.PacketCount,
                Detail: $"phase=4;{operation.OperationId}:{operation.OperationKind}"));
        }
    }

    private static void RunPhase5LinkTransferIssue(
        HardwareSimulationGraph graph,
        string sinkId,
        long cycle,
        IReadOnlyList<SimComponentDef> components,
        DeterministicKernelState current,
        DeterministicKernelState next,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues)
    {
        foreach (var component in components)
        {
            if (component.Type == ComponentKind.Router && component.ExecutionContract is null)
            {
                if (current.RouterStates.TryGetValue(component.Id, out var routerState) && !RouterStateEmpty(routerState))
                {
                    IssueRouterTransfer(graph, component, routerState, sinkId, cycle, current, next, record, metrics, issues);
                }
                continue;
            }

            if (!current.OutputQueues.TryGetValue(component.Id, out var currentQueue) || currentQueue.Count == 0) continue;
            metrics.Components[component.Id].MaxQueueLength = Math.Max(metrics.Components[component.Id].MaxQueueLength, currentQueue.Count);
            var queuedPacket = currentQueue.Peek();
            var exactSourceOperand = current.UsesExactOperandInjection &&
                current.PacketCatalog.ContainsKey(queuedPacket.Id) &&
                string.Equals(queuedPacket.SourceComponentId, component.Id, StringComparison.OrdinalIgnoreCase);
            var requestedOutputPort = component.ExecutionContract is not null || exactSourceOperand ? queuedPacket.SourcePort : "";
            var explicitRoute = Phase8AExplicitRouteResolver.Resolve(graph, queuedPacket, component.Id, requestedOutputPort);
            if (explicitRoute.Kind == Phase8AExplicitRouteResolutionKind.Error)
            {
                RecordExplicitRouteError(component.Id, queuedPacket, explicitRoute, cycle, record, issues);
                RemoveOutputPacket(next.OutputQueues[component.Id], queuedPacket.Id);
                continue;
            }
            var routingDetail = explicitRoute.Detail;
            var nextLink = explicitRoute.Kind == Phase8AExplicitRouteResolutionKind.Resolved
                ? explicitRoute.Link
                : FindNextLink(graph, component.Id, sinkId, next.RouteChoiceCursors, ActiveInFlight(current, next, cycle), metrics, out routingDetail, requestedOutputPort);
            if (nextLink is null)
            {
                RecordStall(metrics, component.Id, StallReason.NoRoute);
                record.Events.Add(new(TraceEventType.Stall, ComponentId: component.Id, Detail: $"phase=5;{StallReason.NoRoute};stall_reason={StallReason.NoRoute}"));
                continue;
            }
            var packet = SelectPacketForSend(component, new Queue<Packet>(currentQueue.Select(ClonePacket)), nextLink, next.RouterArbitrationCursors, record, metrics);
            if (IsLinkBusy(nextLink, packet, current, next, cycle))
            {
                RecordStall(metrics, component.Id, StallReason.LinkBusy);
                metrics.Links[nextLink.Id].CongestionCycles++;
                record.Events.Add(new(TraceEventType.Stall, ComponentId: component.Id, LinkId: nextLink.Id, Detail: $"phase=5;{StallReason.LinkBusy};stall_reason={StallReason.LinkBusy}"));
                continue;
            }
            if (TryFindBusyOpticalChannel(nextLink, packet, ActiveInFlight(current, next, cycle), out var busyOpticalLinkId, out var opticalChannel))
            {
                RecordStall(metrics, component.Id, StallReason.OpticalChannelUnavailable);
                metrics.Links[nextLink.Id].CongestionCycles++;
                record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: component.Id, LinkId: nextLink.Id, Bits: packet.Bits,
                    Detail: $"phase=5;{StallReason.OpticalChannelUnavailable}:channel={opticalChannel};channel_id={opticalChannel};busy={busyOpticalLinkId};busy_link={busyOpticalLinkId};held_packet=true;stall_reason={StallReason.OpticalChannelUnavailable}",
                    StallReason: StallReason.OpticalChannelUnavailable.ToString()));
                continue;
            }

            if (IssueTransferOrImmediateArrival(graph, component.Id, packet, nextLink, cycle, current, next, routingDetail, record, metrics, issues, inputPortDetail: ""))
            {
                RemoveOutputPacket(next.OutputQueues[component.Id], packet.Id);
            }
        }
    }
    private static void IssueRouterTransfer(
        HardwareSimulationGraph graph,
        SimComponentDef router,
        RouterState currentRouterState,
        string sinkId,
        long cycle,
        DeterministicKernelState current,
        DeterministicKernelState next,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues)
    {
        var activePorts = currentRouterState.InputVcs
            .Where(pair => pair.Value.Any(queue => queue.Count > 0))
            .Select(pair => pair.Key)
            .OrderBy(port => (int)port)
            .ToList();
        if (activePorts.Count == 0) return;
        foreach (var queues in currentRouterState.InputVcs.Values)
        {
            metrics.Components[router.Id].MaxQueueLength = Math.Max(metrics.Components[router.Id].MaxQueueLength, queues.Sum(queue => queue.Count));
        }

        var startCursor = Math.Abs(currentRouterState.RoundRobinCursor) % currentRouterState.InputVcs.Count;
        RouterInputPort? winningPort = null;
        var winningVc = 0;
        FlitEnvelope? winningEnvelope = null;
        for (var attempt = 0; attempt < currentRouterState.InputVcs.Count; attempt++)
        {
            var candidate = (RouterInputPort)((startCursor + attempt) % currentRouterState.InputVcs.Count);
            if (TryPeekRouterEnvelope(currentRouterState, candidate, out winningVc, out winningEnvelope))
            {
                winningPort = candidate;
                break;
            }
        }
        if (winningPort is null || winningEnvelope is null) return;

        var packet = ClonePacket(winningEnvelope.Packet);
        var packetFlits = RouterPacketFlits(currentRouterState, winningPort.Value, winningVc, packet.Id);
        var targetId = string.IsNullOrWhiteSpace(packet.DestinationComponentId) ? sinkId : packet.DestinationComponentId;
        var explicitRoute = Phase8AExplicitRouteResolver.Resolve(graph, packet, router.Id, null);
        if (explicitRoute.Kind == Phase8AExplicitRouteResolutionKind.Error)
        {
            RecordExplicitRouteError(router.Id, packet, explicitRoute, cycle, record, issues);
            RemoveRouterFlits(next.RouterStates[router.Id], winningPort.Value, winningVc, packet.Id);
            return;
        }
        var routingDetail = explicitRoute.Detail;
        var nextLink = explicitRoute.Kind == Phase8AExplicitRouteResolutionKind.Resolved
            ? explicitRoute.Link
            : FindNextLink(graph, router.Id, targetId, next.RouteChoiceCursors, ActiveInFlight(current, next, cycle), metrics, out routingDetail);
        if (nextLink is null)
        {
            RecordStall(metrics, router.Id, StallReason.NoRoute);
            record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: router.Id, Bits: packet.Bits,
                Detail: $"phase=5;{StallReason.NoRoute};stall_reason={StallReason.NoRoute};input_port={RouterPortName(winningPort.Value)}"));
            return;
        }
        if (IsLinkBusy(nextLink, packet, current, next, cycle))
        {
            RecordStall(metrics, router.Id, StallReason.LinkBusy);
            metrics.Links[nextLink.Id].CongestionCycles++;
            record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: router.Id, LinkId: nextLink.Id, Bits: packet.Bits,
                Detail: $"phase=5;{StallReason.LinkBusy};stall_reason={StallReason.LinkBusy};input_port={RouterPortName(winningPort.Value)};output_link={nextLink.Id}"));
            return;
        }
        if (TryFindBusyOpticalChannel(nextLink, packet, ActiveInFlight(current, next, cycle), out var busyOpticalLinkId, out var opticalChannel))
        {
            RecordStall(metrics, router.Id, StallReason.OpticalChannelUnavailable);
            metrics.Links[nextLink.Id].CongestionCycles++;
            record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: router.Id, LinkId: nextLink.Id, Bits: packet.Bits,
                Detail: $"phase=5;{StallReason.OpticalChannelUnavailable}:channel={opticalChannel};channel_id={opticalChannel};busy={busyOpticalLinkId};busy_link={busyOpticalLinkId};held_packet=true;stall_reason={StallReason.OpticalChannelUnavailable};input_port={RouterPortName(winningPort.Value)}",
                StallReason: StallReason.OpticalChannelUnavailable.ToString()));
            return;
        }

        var nextCursor = ((int)winningPort.Value + 1) % currentRouterState.InputVcs.Count;
        record.Events.Add(new(TraceEventType.Arbitration, PacketId: packet.Id, ComponentId: router.Id, LinkId: nextLink.Id, Bits: packet.Bits,
            Detail: $"phase=3;policy=round_robin;rr_start={RouterPortName((RouterInputPort)startCursor)};winner_port={RouterPortName(winningPort.Value)};request_ports={string.Join(",", activePorts.Select(RouterPortName))};rr_next={RouterPortName((RouterInputPort)nextCursor)};{routingDetail}"));
        foreach (var loserPort in activePorts.Where(port => port != winningPort.Value))
        {
            if (!TryPeekRouterEnvelope(currentRouterState, loserPort, out _, out var loserEnvelope) || loserEnvelope is null) continue;
            var loserPacket = loserEnvelope.Packet;
            RecordStall(metrics, router.Id, StallReason.RouterConflict);
            metrics.Links[nextLink.Id].CongestionCycles++;
            record.Events.Add(new(TraceEventType.Stall, PacketId: loserPacket.Id, ComponentId: router.Id, LinkId: nextLink.Id, Bits: loserPacket.Bits,
                Detail: $"phase=3;{StallReason.RouterConflict};stall_reason={StallReason.RouterConflict};policy=round_robin;winner={packet.Id};winner_port={RouterPortName(winningPort.Value)};request_port={RouterPortName(loserPort)}"));
        }
        if (IssueTransferOrImmediateArrival(graph, router.Id, packet, nextLink, cycle, current, next, routingDetail, record, metrics, issues, $"input_port={RouterPortName(winningPort.Value)}", packetFlits))
        {
            RemoveRouterFlits(next.RouterStates[router.Id], winningPort.Value, winningVc, packet.Id);
            next.RouterStates[router.Id].RoundRobinCursor = nextCursor;
        }
    }

    private static bool IssueTransferOrImmediateArrival(
        HardwareSimulationGraph graph,
        string componentId,
        Packet packet,
        SimLinkDef nextLink,
        long cycle,
        DeterministicKernelState current,
        DeterministicKernelState next,
        string routingDetail,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues,
        string inputPortDetail,
        IReadOnlyList<Flit>? flits = null)
    {
        _ = current;
        packet = ClonePacket(packet);
        if (!Phase8AExplicitRouteResolver.Advance(packet, nextLink.Id, out var exactRouteError))
        {
            issues.Add(new SimulationIssue(
                "Phase8AExplicitRouteProgressInvalid",
                "error",
                cycle,
                componentId,
                packet.Id,
                nextLink.Id,
                null,
                exactRouteError));
            record.Events.Add(new(TraceEventType.Error, PacketId: packet.Id, ComponentId: componentId, LinkId: nextLink.Id,
                Bits: packet.Bits, Detail: "phase=5;code=Phase8AExplicitRouteProgressInvalid"));
            return false;
        }
        if (nextLink.OpticalProfile is not null)
        {
            if (packet.OpticalState is null)
            {
                issues.Add(new SimulationIssue(
                    "P8OpticalPacketStateMissing",
                    "error",
                    cycle,
                    componentId,
                    packet.Id,
                    null,
                    null,
                    "A strict Phase 8 optical route cannot transfer a packet without typed OpticalPacketState."));
                return false;
            }
            packet.SignalDomain = PacketSignalDomain.Optical;
            packet.OpticalState = packet.OpticalState.ApplyLoss(nextLink.OpticalProfile.TotalLoss);
            routingDetail = AppendOpticalResourceDetail(routingDetail, nextLink, packet.OpticalState);
        }
        routingDetail = PacketTraceIdentity.AppendTraceDetail(routingDetail, packet);
        var transportFlits = flits?.Select(CloneFlit).ToList() ?? BuildTransportFlits(graph, packet);
        var linkLatency = Math.Max(0, nextLink.LatencyCycles);
        var serialized = FlitLinkSerializer.Serialize(transportFlits, Math.Max(1, nextLink.BandwidthBitsPerCycle), linkLatency, nextLink.EnergyPerBit);
        var arrivalCycle = cycle + serialized.Trace.Max(entry => entry.ArrivalCycle);
        var transferCycles = Math.Max(1, (int)serialized.BusyCycles);
        var transferEvent = new TraceEvent(TraceEventType.LinkTransfer, PacketId: packet.Id, ComponentId: componentId, LinkId: nextLink.Id,
            Source: nextLink.Source.ComponentId, Destination: nextLink.Destination.ComponentId, Bits: packet.Bits,
            Detail: FormatTransferDetail(arrivalCycle, routingDetail, inputPortDetail, linkLatency));

        if (arrivalCycle <= cycle)
        {
            var insertIndex = record.Events.Count;
            if (!TryStageImmediateArrivalAfterCommit(graph, packet, transportFlits, nextLink, cycle, next, record, metrics, issues))
            {
                RecordDownstreamFullHold(componentId, packet, transportFlits, nextLink, record, metrics);
                return false;
            }

            AccountIssuedTransfer(componentId, packet, nextLink, transportFlits.Count, serialized.TotalBitsTransferred, transferCycles, metrics);
            record.Events.Insert(insertIndex, transferEvent);
            return true;
        }

        AccountIssuedTransfer(componentId, packet, nextLink, transportFlits.Count, serialized.TotalBitsTransferred, transferCycles, metrics);
        record.Events.Add(transferEvent);
        var flitsById = transportFlits.ToDictionary(flit => flit.Id, CloneFlit, StringComparer.Ordinal);
        foreach (var entry in serialized.Trace.OrderBy(entry => entry.ArrivalCycle).ThenBy(entry => entry.FlitIndex))
        {
            next.InFlightFlits.Add(new FlitFlight(ClonePacket(packet), flitsById[entry.FlitId], nextLink, cycle + entry.ArrivalCycle));
        }
        return true;
    }

    private static void RecordExplicitRouteError(
        string componentId,
        Packet packet,
        Phase8AExplicitRouteResolution resolution,
        long cycle,
        CycleTraceRecord record,
        List<SimulationIssue> issues)
    {
        issues.Add(new SimulationIssue(
            resolution.ErrorCode,
            "error",
            cycle,
            componentId,
            packet.Id,
            null,
            null,
            resolution.ErrorMessage));
        record.Events.Add(new(TraceEventType.Error, PacketId: packet.Id, ComponentId: componentId, Bits: packet.Bits,
            Detail: $"phase=5;code={resolution.ErrorCode};packet_consumed=true"));
    }

    private static void AccountIssuedTransfer(
        string componentId,
        Packet packet,
        SimLinkDef nextLink,
        int flitCount,
        long serializedBits,
        int transferCycles,
        SimulationMetrics metrics)
    {
        var linkMetrics = metrics.Links[nextLink.Id];
        var linkEnergy = packet.Bits * nextLink.EnergyPerBit;
        linkMetrics.PacketsTransferred++;
        linkMetrics.TotalBitsTransferred += packet.Bits;
        linkMetrics.BusyCycles += transferCycles;
        linkMetrics.Energy += linkEnergy;
        linkMetrics.EnergyBreakdown.Dynamic += new Picojoules(linkEnergy);
        linkMetrics.FlitsTransferred += flitCount;
        linkMetrics.SerializationBitsSent += serializedBits;
        metrics.Components[componentId].OutputTrafficBits += packet.Bits;
        metrics.Components[componentId].ActiveCycles++;
        metrics.Global.TotalEnergy += linkEnergy;
        var category = nextLink.OpticalProfile is not null ||
            string.Equals(nextLink.RouteType, "optical", StringComparison.OrdinalIgnoreCase)
                ? EnergyCategory.Optical
                : EnergyCategory.NoC;
        metrics.Global.EnergyByCategory[category] = metrics.Global.EnergyByCategory[category] + new Picojoules(linkEnergy);
        if (category == EnergyCategory.Optical) metrics.Global.OpticalEnergy += linkEnergy;
        else metrics.Global.NoCEnergy += linkEnergy;
    }

    private static void RecordDownstreamFullHold(
        string componentId,
        Packet packet,
        IReadOnlyList<Flit> flits,
        SimLinkDef link,
        CycleTraceRecord record,
        SimulationMetrics metrics)
    {
        var stallCode = TransportSemanticsContract.DownstreamFullStallCode;
        RecordStall(metrics, componentId, StallReason.OutputBufferFull);
        metrics.Links[link.Id].BackpressureCycles++;
        metrics.Links[link.Id].CongestionCycles++;
        record.Events.Add(new TraceEvent(
            TraceEventType.Stall,
            PacketId: packet.Id,
            ComponentId: componentId,
            LinkId: link.Id,
            Source: link.Source.ComponentId,
            Destination: link.Destination.ComponentId,
            Bits: packet.Bits,
            Detail: $"phase=5;stall_code={stallCode};stall_reason={stallCode};legacy_reason={StallReason.OutputBufferFull};flow_control={FlowControlMode.BlockingReadyValid};ready=false;valid=true;held_packet={packet.Id};held_flits={string.Join(",", flits.OrderBy(flit => flit.FlitIndex).Select(flit => flit.Id))}",
            StallReason: stallCode));
    }

    private static bool TryStageImmediateArrivalAfterCommit(
        HardwareSimulationGraph graph,
        Packet packet,
        IReadOnlyList<Flit> flits,
        SimLinkDef link,
        long cycle,
        DeterministicKernelState next,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues)
    {
        var destination = graph.FindComponent(link.Destination.ComponentId)
            ?? throw new InvalidOperationException($"Missing destination component '{link.Destination.ComponentId}'.");
        var memoryAccessesThisCycle = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return TryAcceptArrivedPacket(graph, destination, ClonePacket(packet), flits, link, cycle, next, next, memoryAccessesThisCycle, record, metrics, issues);
    }
    private static void RunPhase6BufferMemoryUpdate(HardwareSimulationGraph graph, DeterministicKernelState next, SimulationMetrics metrics)
    {
        foreach (var component in graph.Components.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            if (next.OutputQueues.TryGetValue(component.Id, out var outputQueue)) metrics.Components[component.Id].MaxQueueLength = Math.Max(metrics.Components[component.Id].MaxQueueLength, outputQueue.Count);
            if (component.Type == ComponentKind.Memory)
            {
                metrics.Components[component.Id].MemoryUsedBits = next.MemoryUsedBits.GetValueOrDefault(component.Id);
                metrics.Components[component.Id].MemoryRejectedWrites = next.MemoryRejectedWrites.GetValueOrDefault(component.Id);
            }
        }
    }

    private static void RunPhase8MetricsAccumulation(HardwareSimulationGraph graph, DeterministicKernelState current, SimulationMetrics metrics)
    {
        AccountIdleCycles(graph, current.OutputQueues, current.RouterStates, current.ReductionStates, current.Processing, current.InFlightFlits.Concat(current.PendingArrivalFlits).ToList(), metrics);
        foreach (var component in graph.Components.Where(component => component.Type == ComponentKind.Memory))
        {
            metrics.Components[component.Id].MemoryUsedBits = current.MemoryUsedBits.GetValueOrDefault(component.Id);
            metrics.Components[component.Id].MemoryRejectedWrites = current.MemoryRejectedWrites.GetValueOrDefault(component.Id);
        }
    }

    private static bool TryEnqueueReductionInput(SimComponentDef reduction, Packet packet, SimLinkDef link, ReductionUnitState state, CycleTraceRecord record, SimulationMetrics metrics)
    {
        var numInputs = ReductionNumInputs(reduction);
        var capacity = Math.Max(numInputs, QueueCapacity(reduction));
        if (state.Inputs.Count >= capacity)
        {
            RecordStall(metrics, reduction.Id, StallReason.OutputBufferFull);
            record.Events.Add(new(TraceEventType.Stall, PacketId: packet.Id, ComponentId: reduction.Id, LinkId: link.Id,
                Bits: packet.Bits, Detail: $"phase=1;{StallReason.OutputBufferFull};stall_reason={StallReason.OutputBufferFull};buffer=reduction_input;queue_capacity={capacity}"));
            return false;
        }
        state.Inputs.Add(packet);
        metrics.Components[reduction.Id].MaxQueueLength = Math.Max(metrics.Components[reduction.Id].MaxQueueLength, state.Inputs.Count);
        record.Events.Add(new(TraceEventType.BufferOccupancy, PacketId: packet.Id, ComponentId: reduction.Id, LinkId: link.Id,
            Bits: packet.Bits, Detail: $"phase=1;state=WaitingInput;buffer=reduction_input;occupancy={state.Inputs.Count};num_inputs={numInputs}"));
        return true;
    }

    private static bool TryCompleteMemoryProcessingDeterministic(
        SimComponentDef memory,
        ProcessingItem item,
        DeterministicKernelState current,
        DeterministicKernelState next,
        long cycle,
        CycleTraceRecord record,
        SimulationMetrics metrics,
        List<SimulationIssue> issues)
    {
        EnsureMemoryRequestMetadata(item.Packet, item.MemoryOperation);
        var opText = item.MemoryOperation.ToString().ToLowerInvariant();
        if (item.MemoryOperation == MemoryOperationType.Write)
        {
            var capacityBits = MemoryCapacityBits(memory);
            var usedBits = next.MemoryUsedBits.GetValueOrDefault(memory.Id, current.MemoryUsedBits.GetValueOrDefault(memory.Id));
            metrics.Components[memory.Id].MemoryCapacityBits = capacityBits;
            if (usedBits + item.Packet.TotalBits > capacityBits)
            {
                next.MemoryRejectedWrites[memory.Id] = next.MemoryRejectedWrites.GetValueOrDefault(memory.Id) + 1;
                var message = $"Memory write request '{item.Packet.RequestId}' would exceed capacity_bits={capacityBits} with used_bits={usedBits} and write_bits={item.Packet.TotalBits}.";
                issues.Add(new SimulationIssue("MemoryCapacityOverflow", "error", cycle, memory.Id, item.Packet.Id, item.Packet.RequestId, opText, message));
                record.Events.Add(new(TraceEventType.Error, PacketId: item.Packet.Id, ComponentId: memory.Id, Bits: item.Packet.TotalBits,
                    Detail: $"phase=4;code=MemoryCapacityOverflow;severity=error;request_id={item.Packet.RequestId};op_type={opText};used_bits={usedBits};capacity_bits={capacityBits};write_bits={item.Packet.TotalBits}"));
                return false;
            }
            next.MemoryUsedBits[memory.Id] = usedBits + item.Packet.TotalBits;
            item.Packet.PacketType = PacketType.Status;
            item.MemoryWriteApplied = true;
            item.MemoryOperationCompleted = true;
            record.Events.Add(new(TraceEventType.Compute, PacketId: item.Packet.Id, ComponentId: memory.Id, Bits: item.Packet.TotalBits,
                Detail: $"memory_write_complete;phase=4;request_id={item.Packet.RequestId};op_type={opText};used_bits={next.MemoryUsedBits[memory.Id]};capacity_bits={capacityBits};memory_bank={item.MemoryBankId}"));
            return true;
        }
        if (item.MemoryOperation == MemoryOperationType.Read)
        {
            if (item.Packet.PacketType == PacketType.MemoryReadRequest) item.Packet.PacketType = PacketType.MemoryReadResponse;
            item.MemoryOperationCompleted = true;
            record.Events.Add(new(TraceEventType.Compute, PacketId: item.Packet.Id, ComponentId: memory.Id, Bits: item.Packet.TotalBits,
                Detail: $"memory_read_complete;phase=4;request_id={item.Packet.RequestId};op_type={opText};memory_bank={item.MemoryBankId}"));
            return true;
        }
        item.MemoryOperationCompleted = true;
        return true;
    }

    private static string ResolveKernelInputPort(CompiledComponentExecutionContract contract, string physicalPortName)
    {
        var inputs = contract.Ports.Where(port => port.Direction == PortDirection.Input).ToList();
        var exact = inputs.FirstOrDefault(port => string.Equals(port.Name, physicalPortName, StringComparison.Ordinal));
        return exact?.Name ?? (inputs.Count == 1 ? inputs[0].Name : physicalPortName);
    }

    private static string ResolveKernelOutputPort(
        HardwareSimulationGraph graph,
        SimComponentDef component,
        string contractPortName)
    {
        var physicalOutputs = graph.Ports
            .Where(port => string.Equals(port.ComponentId, component.Id, StringComparison.OrdinalIgnoreCase) &&
                           port.Direction == PortDirection.Output)
            .OrderBy(port => port.Name, StringComparer.Ordinal)
            .ToList();
        var exact = physicalOutputs.FirstOrDefault(port => string.Equals(port.Name, contractPortName, StringComparison.Ordinal));
        return exact?.Name ?? (physicalOutputs.Count == 1 ? physicalOutputs[0].Name : contractPortName);
    }

    private static void AddKernelIssueFacts(List<SimulationIssue> issues, string componentId, long cycle, IReadOnlyList<ComponentRuntimeKernelIssueFact> facts)
    {
        foreach (var fact in facts)
        {
            var severity = string.Equals(fact.Severity, "warning", StringComparison.OrdinalIgnoreCase) ? "warning" :
                string.Equals(fact.Severity, "info", StringComparison.OrdinalIgnoreCase) ? "info" : "error";
            issues.Add(new SimulationIssue(fact.Code, severity, cycle, componentId, fact.PacketId, null, null, fact.Message));
        }
    }
    private static void AddKernelEventFacts(CycleTraceRecord record, string componentId, IReadOnlyList<ComponentRuntimeKernelEventFact> facts)
    {
        foreach (var fact in facts)
        {
            record.Events.Add(new TraceEvent(fact.EventType, PacketId: fact.PacketId, ComponentId: componentId, Bits: fact.Bits, Detail: fact.Detail));
        }
    }

    private static void AccountKernelEnergy(SimComponentDef component, double energy, SimulationMetrics metrics)
    {
        var componentMetrics = metrics.Components[component.Id];
        componentMetrics.Energy += energy;
        componentMetrics.EnergyBreakdown.Dynamic += new Picojoules(energy);
        componentMetrics.InternalEnergyBreakdown ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        componentMetrics.InternalEnergyBreakdown["compute_dynamic_energy"] = componentMetrics.InternalEnergyBreakdown.GetValueOrDefault("compute_dynamic_energy") + energy;
        metrics.Global.TotalEnergy += energy;
        var category = component.ExecutionContract?.MetricDescriptors.FirstOrDefault()?.Category ?? EnergyCategory.NoC;
        metrics.Global.EnergyByCategory[category] = metrics.Global.EnergyByCategory[category] + new Picojoules(energy);
        switch (category)
        {
            case EnergyCategory.Compute: metrics.Global.ComputeEnergy += energy; break;
            case EnergyCategory.NoC: metrics.Global.NoCEnergy += energy; break;
            case EnergyCategory.Conversion: metrics.Global.ConversionEnergy += energy; break;
            case EnergyCategory.Optical: metrics.Global.OpticalEnergy += energy; break;
        }
    }

    private static void AccountKernelEnergy(SimComponentDef component, ComponentRuntimeEnergyContribution contribution, SimulationMetrics metrics)
    {
        var energy = contribution.Energy.Value;
        if (energy <= 0) return;
        var componentMetrics = metrics.Components[component.Id];
        componentMetrics.Energy += energy;
        componentMetrics.EnergyBreakdown[contribution.Kind] = componentMetrics.EnergyBreakdown[contribution.Kind] + contribution.Energy;
        componentMetrics.InternalEnergyBreakdown ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        componentMetrics.InternalEnergyBreakdown[contribution.Name] = componentMetrics.InternalEnergyBreakdown.GetValueOrDefault(contribution.Name) + energy;
        metrics.Global.TotalEnergy += energy;
        metrics.Global.EnergyByCategory[contribution.Category] = metrics.Global.EnergyByCategory[contribution.Category] + contribution.Energy;
        switch (contribution.Category)
        {
            case EnergyCategory.Compute: metrics.Global.ComputeEnergy += energy; break;
            case EnergyCategory.NoC: metrics.Global.NoCEnergy += energy; break;
            case EnergyCategory.Conversion:
                metrics.Global.ConversionEnergy += energy;
                if (component.Parameters.TryGetValue(AdapterRuntimeMetadata.OpticalEnergyContributionKey, out var rawOptical) &&
                    bool.TryParse(rawOptical, out var contributesOptical) &&
                    contributesOptical)
                {
                    // Compatibility projection only: TotalEnergy and EnergyByCategory remain counted once as Conversion.
                    metrics.Global.OpticalEnergy += energy;
                }
                break;
            case EnergyCategory.Optical: metrics.Global.OpticalEnergy += energy; break;
        }
    }

    private static void AccountKernelNamedMetrics(SimComponentDef component, IReadOnlyList<NamedMetricContribution> contributions, SimulationMetrics metrics)
    {
        foreach (var contribution in contributions)
        {
            if (string.IsNullOrWhiteSpace(contribution.Name) || !double.IsFinite(contribution.Value)) continue;
            UpdateNamedMetric(metrics.Components[component.Id].NamedMetrics, contribution.Name, contribution);
            UpdateNamedMetric(metrics.Global.NamedMetrics, $"{component.Id}.{contribution.Name}", contribution);
        }
    }

    private static void UpdateNamedMetric(Dictionary<string, NamedMetricAggregate> destination, string key, NamedMetricContribution contribution)
    {
        if (!destination.TryGetValue(key, out var aggregate))
        {
            destination[key] = new NamedMetricAggregate
            {
                Value = contribution.Value,
                Units = contribution.Units,
                Aggregation = contribution.Aggregation,
                Samples = 1
            };
            return;
        }

        aggregate.Value = contribution.Aggregation switch
        {
            NamedMetricAggregationKind.Sum => aggregate.Value + contribution.Value,
            NamedMetricAggregationKind.Last => contribution.Value,
            NamedMetricAggregationKind.Maximum => Math.Max(aggregate.Value, contribution.Value),
            NamedMetricAggregationKind.Minimum => Math.Min(aggregate.Value, contribution.Value),
            _ => aggregate.Value
        };
        aggregate.Samples++;
    }

    private static string DescribeUndrainedState(DeterministicKernelState state)
    {
        var pendingSources = string.Join(",", state.PendingPackets.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}"));
        var exactOperandDetail = state.UsesExactOperandInjection
            ? ";pending_exact_operands=[" + string.Join(",", state.PendingExactOperands.OrderBy(packet => packet.InjectionCycle).ThenBy(packet => packet.Id, StringComparer.Ordinal).Select(packet => $"{packet.Id}@{packet.InjectionCycle}")) + "]"
            : "";
        var outputQueues = string.Join(",", state.OutputQueues.Where(pair => pair.Value.Count != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value.Count}"));
        var processing = string.Join(",", state.Processing.Where(pair => pair.Value.Count != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value.Count}"));
        var nonIdleKernels = string.Join(",", state.ComponentKernelStates.Where(pair => !pair.Value.IsIdle).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}({pair.Value})"));
        return $"pending_sources=[{pendingSources}]{exactOperandDetail};in_flight_flits={state.InFlightFlits.Count};pending_arrival_flits={state.PendingArrivalFlits.Count};output_queues=[{outputQueues}];processing=[{processing}];non_idle_kernels=[{nonIdleKernels}]";
    }
    private static bool IsCompleteAfterCommit(DeterministicKernelState state, WorkloadSchedule? schedule, IReadOnlyList<ScheduledOperation> scheduledOperations)
    {
        var scheduleDrained = schedule is null ||
                              (state.NextScheduledOperationIndex >= scheduledOperations.Count &&
                               state.ActiveScheduledReleases.All(release => release.RemainingPackets == 0) &&
                               state.CompletedScheduledOperations.Count == scheduledOperations.Count);
        return state.PendingPackets.Values.All(count => count == 0) && state.PendingExactOperands.Count == 0 && scheduleDrained && state.InFlightFlits.Count == 0 && state.PendingArrivalFlits.Count == 0 &&
               state.OutputQueues.Values.All(queue => queue.Count == 0) && RouterStatesEmpty(state.RouterStates) &&
               ReductionStatesEmpty(state.ReductionStates) && state.Processing.Values.All(items => items.Count == 0) &&
               state.ComponentKernelStates.Values.All(kernelState => kernelState.IsIdle);
    }

    private static IReadOnlyDictionary<string, string> DeterministicTraceConfig(
        SimulationOptions options,
        HardwareSimulationGraph graph,
        WorkloadSchedule? schedule,
        IReadOnlyList<Packet>? exactInitialOperands = null)
    {
        var config = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["kernel"] = "deterministic-cycle-10phase",
            ["max_cycles"] = options.MaxCycles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["execution_mode"] = SimulationExecutionMode.CycleAccurate.ToString(),
            ["requested_execution_mode"] = options.ExecutionMode.ToString(),
            ["component_count"] = graph.Components.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["link_count"] = graph.Links.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["schedule"] = schedule is null ? "none" : schedule.WorkloadId
        };
        if (exactInitialOperands is not null)
        {
            var operandPlan = exactInitialOperands
                .OrderBy(packet => packet.InjectionCycle)
                .ThenBy(packet => packet.Id, StringComparer.Ordinal)
                .Select(ClonePacket)
                .ToList();
            var canonicalPlan = ComponentExecutionJson.CanonicalizeJson(
                System.Text.Json.JsonSerializer.Serialize(operandPlan, HardwareGraphJson.Options));
            config["operand_injection_source"] = "executable.initial_packets";
            config["pending_exact_operand_count"] = operandPlan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            config["pending_exact_operand_plan_sha256"] = ComponentExecutionJson.ComputeSha256(canonicalPlan);
        }
        return config;
    }

    private static List<FlitFlight> ActiveInFlight(DeterministicKernelState current, DeterministicKernelState next, long cycle) =>
        current.InFlightFlits.Where(flight => flight.ArrivalCycle > cycle)
            .Concat(next.InFlightFlits)
            .Concat(next.PendingArrivalFlits)
            .ToList();

    private static bool IsLinkBusy(SimLinkDef link, Packet packet, DeterministicKernelState current, DeterministicKernelState next, long cycle)
    {
        if (link.OpticalProfile is not null && packet.OpticalState is not null)
        {
            // Typed Phase 8 occupancy is wavelength-scoped and checked by TryFindBusyOpticalChannel.
            return false;
        }
        return current.InFlightFlits.Any(flight => flight.ArrivalCycle > cycle && flight.Link.Id == link.Id) ||
            next.InFlightFlits.Any(flight => flight.Link.Id == link.Id) ||
            next.PendingArrivalFlits.Any(flight => flight.Link.Id == link.Id);
    }

    private static string AppendOpticalResourceDetail(string routingDetail, SimLinkDef link, OpticalPacketState state)
    {
        var route = link.OpticalProfile?.Route;
        var resourceParts = new List<string>
        {
            "signal_domain=" + PacketSignalDomain.Optical,
            "optical_resource=route_hash:" + (route?.RouteHash ?? link.Id),
            "layer=" + (route?.LayerId ?? ""),
            "medium=" + (route?.Medium.ToString() ?? RoutingMedium.OpticalWaveguide.ToString()),
            "channel=" + state.ChannelId,
            "channel_id=" + state.ChannelId,
            "wavelength_nm=" + state.Wavelength.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "optical_power_dbm=" + state.OpticalPower.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "accumulated_loss_db=" + state.AccumulatedLoss.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "accumulated_crosstalk_db=" + state.AccumulatedCrosstalk.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (state.SignalToNoiseRatio is not null)
        {
            resourceParts.Add("snr_db=" + state.SignalToNoiseRatio.Value.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        var resource = string.Join(";", resourceParts);
        return string.IsNullOrWhiteSpace(routingDetail) ? resource : routingDetail + ";" + resource;
    }

    private static string FormatTransferDetail(long arrivalCycle, string routingDetail, string inputPortDetail, int linkLatency)
    {
        var parts = new List<string> { "phase=5", $"arrival_cycle={arrivalCycle}", $"link_latency={linkLatency}" };
        if (!string.IsNullOrWhiteSpace(routingDetail)) parts.Add(routingDetail);
        if (!string.IsNullOrWhiteSpace(inputPortDetail)) parts.Add(inputPortDetail);
        return string.Join(";", parts);
    }
    private static Packet ClonePacket(Packet packet) => new()
    {
        Id = packet.Id,
        PacketType = packet.PacketType,
        NumElements = packet.NumElements,
        BitWidth = packet.BitWidth,
        Bits = packet.Bits,
        Precision = packet.Precision,
        SourceComponentId = packet.SourceComponentId,
        DestinationComponentId = packet.DestinationComponentId,
        SourcePort = packet.SourcePort,
        DestinationPort = packet.DestinationPort,
        WorkloadOpId = packet.WorkloadOpId,
        TensorId = packet.TensorId,
        TileId = packet.TileId,
        RoutePath = packet.RoutePath.ToList(),
        DependencyIds = packet.DependencyIds.ToList(),
        Metadata = new Dictionary<string, string>(packet.Metadata, StringComparer.OrdinalIgnoreCase),
        SignalDomain = packet.SignalDomain,
        OpticalState = packet.OpticalState is null ? null : packet.OpticalState with { },
        RequestId = packet.RequestId,
        MemoryOperation = packet.MemoryOperation,
        MemoryAddress = packet.MemoryAddress,
        CurrentComponentId = packet.CurrentComponentId,
        CreatedCycle = packet.CreatedCycle,
        InjectionCycle = packet.InjectionCycle,
        DeliveredCycle = packet.DeliveredCycle,
        ArrivalCycle = packet.ArrivalCycle,
        VisitedComponents = packet.VisitedComponents.ToList(),
        Values = packet.Values.ToList()
    };

    private static ProcessingItem CloneProcessingItem(ProcessingItem item) => new(ClonePacket(item.Packet), item.ReadyCycle, item.MemoryOperation, item.MemoryBankId)
    {
        MemoryWriteApplied = item.MemoryWriteApplied,
        MemoryOperationCompleted = item.MemoryOperationCompleted,
        RuntimeApplied = item.RuntimeApplied
    };

    private static RouterState CloneRouterState(RouterState state)
    {
        var clone = new RouterState(state.VirtualChannelCount) { RoundRobinCursor = state.RoundRobinCursor };
        foreach (var port in state.InputVcs.Keys.ToList())
        {
            for (var vc = 0; vc < state.InputVcs[port].Count; vc++)
            {
                clone.InputVcs[port][vc].Clear();
                foreach (var envelope in state.InputVcs[port][vc])
                {
                    clone.InputVcs[port][vc].Enqueue(new FlitEnvelope(ClonePacket(envelope.Packet), CloneFlit(envelope.Flit)));
                }
            }
        }
        return clone;
    }

    private static FlitFlight CloneFlitFlight(FlitFlight flight) => new(ClonePacket(flight.Packet), CloneFlit(flight.Flit), flight.Link, flight.ArrivalCycle);

    private static Flit CloneFlit(Flit flit) => new()
    {
        Id = flit.Id,
        PacketId = flit.PacketId,
        FlitIndex = flit.FlitIndex,
        TotalFlits = flit.TotalFlits,
        PayloadBits = flit.PayloadBits,
        IsHead = flit.IsHead,
        IsTail = flit.IsTail,
        VirtualChannel = flit.VirtualChannel,
        RouteMetadata = flit.RouteMetadata,
        CreationCycle = flit.CreationCycle,
        Metadata = new Dictionary<string, string>(flit.Metadata, StringComparer.OrdinalIgnoreCase)
    };

    private static void RemoveMatchingFlitFlight(List<FlitFlight> flights, FlitFlight target)
    {
        var index = flights.FindIndex(flight => SameFlitFlight(flight, target));
        if (index >= 0) flights.RemoveAt(index);
    }

    private static void RemoveMatchingFlitFlights(List<FlitFlight> flights, IEnumerable<FlitFlight> targets)
    {
        foreach (var target in targets.ToList())
        {
            RemoveMatchingFlitFlight(flights, target);
        }
    }

    private static void AddPendingArrivalFlits(List<FlitFlight> pending, IEnumerable<FlitFlight> arrivals)
    {
        foreach (var arrival in arrivals)
        {
            if (pending.Any(existing => SameFlitFlight(existing, arrival))) continue;
            pending.Add(CloneFlitFlight(arrival));
        }
    }

    private static bool SamePacketLink(FlitFlight left, FlitFlight right) =>
        string.Equals(left.Link.Id, right.Link.Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Packet.Id, right.Packet.Id, StringComparison.OrdinalIgnoreCase);

    private static bool SameFlitFlight(FlitFlight left, FlitFlight right) =>
        SamePacketLink(left, right) &&
        string.Equals(left.Flit.Id, right.Flit.Id, StringComparison.Ordinal) &&
        left.ArrivalCycle == right.ArrivalCycle;

    private static List<Flit> BuildTransportFlits(HardwareSimulationGraph graph, Packet packet) =>
        FlitPacketizer.Packetize(packet, Math.Max(1, graph.SimulationConfig.FlitSizeBits)).Select(CloneFlit).ToList();

    private static bool RemoveOutputPacket(Queue<Packet> queue, string packetId)
    {
        var found = false;
        var remaining = queue.ToList();
        queue.Clear();
        foreach (var packet in remaining)
        {
            if (!found && string.Equals(packet.Id, packetId, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                continue;
            }
            queue.Enqueue(packet);
        }
        return found;
    }

    private static bool TryPeekRouterEnvelope(RouterState state, RouterInputPort port, out int virtualChannel, out FlitEnvelope? envelope)
    {
        for (var vc = 0; vc < state.InputVcs[port].Count; vc++)
        {
            if (state.InputVcs[port][vc].Count == 0) continue;
            virtualChannel = vc;
            envelope = state.InputVcs[port][vc].Peek();
            return true;
        }

        virtualChannel = -1;
        envelope = null;
        return false;
    }

    private static IReadOnlyList<Flit> RouterPacketFlits(RouterState state, RouterInputPort port, int virtualChannel, string packetId) =>
        state.InputVcs[port][virtualChannel]
            .TakeWhile(envelope => string.Equals(envelope.Packet.Id, packetId, StringComparison.OrdinalIgnoreCase))
            .Select(envelope => CloneFlit(envelope.Flit))
            .ToList();

    private static void RemoveRouterFlits(RouterState state, RouterInputPort port, int virtualChannel, string packetId)
    {
        var queue = state.InputVcs[port][virtualChannel];
        var remaining = queue.ToList();
        queue.Clear();
        foreach (var envelope in remaining)
        {
            if (string.Equals(envelope.Packet.Id, packetId, StringComparison.OrdinalIgnoreCase)) continue;
            queue.Enqueue(envelope);
        }
    }

    private static void RemoveReductionInputs(ReductionUnitState state, HashSet<string> packetIds) => state.Inputs.RemoveAll(packet => packetIds.Contains(packet.Id));

    private static void ReplaceProcessingItem(List<ProcessingItem> items, ProcessingItem target, ProcessingItem replacement)
    {
        var index = items.FindIndex(item => item.ReadyCycle == target.ReadyCycle && string.Equals(item.Packet.Id, target.Packet.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            items[index] = replacement;
        }
    }
    private static void RemoveProcessingItem(List<ProcessingItem> items, ProcessingItem target)
    {
        var index = items.FindIndex(item => item.ReadyCycle == target.ReadyCycle && string.Equals(item.Packet.Id, target.Packet.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) items.RemoveAt(index);
    }
}
