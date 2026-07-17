using System.Globalization;
using static HardwareSim.Core.Phase8AMatMulRuntimeIds;

namespace HardwareSim.Core;

internal sealed record Phase8AActivationRuntimeProgram(
    Phase8AActivationTileTree Tree,
    string SourceFlowId,
    string InitialDestinationId,
    IReadOnlyList<string> InitialRouteLinkIds,
    Phase8AMulticastBranchPlan? InitialMulticastPlan,
    IReadOnlyList<Phase8ABranchTargetPipeline> InitialTargetPipelines,
    IReadOnlyList<Phase8AStageRoute> UnicastExecutionChain,
    IReadOnlyList<CommunicationFlow> BranchFlows);

internal static class Phase8AMatMulRuntimeProgramCompiler
{
    public static IReadOnlyList<Phase8AActivationRuntimeProgram> BuildActivationPrograms(Phase8AMatMulScenarioPlan plan) =>
        plan.ActivationTree.Trees.OrderBy(tree => tree.GlobalKTileIndex)
            .Select(tree => BuildActivationProgram(plan, tree)).ToArray();

    private static Phase8AActivationRuntimeProgram BuildActivationProgram(
        Phase8AMatMulScenarioPlan plan,
        Phase8AActivationTileTree tree)
    {
        var outgoing = tree.Edges.GroupBy(edge => edge.SourceComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.LinkId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var branches = tree.BranchPoints.ToDictionary(branch => branch.ComponentId, StringComparer.Ordinal);
        var targetByPe = tree.Targets.ToDictionary(target => target.PeComponentId, StringComparer.Ordinal);
        var built = new Dictionary<string, BranchRuntimeNode>(StringComparer.Ordinal);

        BranchRuntimeNode BuildBranch(string componentId)
        {
            if (built.TryGetValue(componentId, out var existing)) return existing;
            if (!branches.ContainsKey(componentId) || !outgoing.TryGetValue(componentId, out var branchEdges) || branchEdges.Length < 2)
                throw new InvalidOperationException("Activation authority branch is not a real multi-output node: " + componentId);
            var targets = new List<Phase8AMulticastBranchTarget>();
            var pipelines = new List<Phase8ABranchTargetPipeline>();
            var flows = new List<CommunicationFlow>();
            foreach (var firstEdge in branchEdges)
            {
                var route = new List<string> { BranchOutputLink(componentId, firstEdge.LinkId) };
                var cursor = firstEdge.DestinationComponentId;
                while (!branches.ContainsKey(cursor) && !targetByPe.ContainsKey(cursor))
                {
                    if (!outgoing.TryGetValue(cursor, out var nextEdges) || nextEdges.Length != 1)
                        throw new InvalidOperationException("Activation tree contains an untyped terminal or implicit branch at " + cursor);
                    route.Add(nextEdges[0].LinkId);
                    cursor = nextEdges[0].DestinationComponentId;
                }
                if (branches.ContainsKey(cursor))
                {
                    route.Add(BranchInputLink(cursor));
                    var child = BuildBranch(cursor);
                    targets.Add(new Phase8AMulticastBranchTarget(
                        BranchComponent(cursor), route, ActivationPath(tree.GlobalKTileIndex, componentId, cursor)));
                    pipelines.Add(new Phase8ABranchTargetPipeline(
                        BranchComponent(cursor), [], null, child.Plan, child.Pipelines));
                    flows.AddRange(child.Flows);
                }
                else
                {
                    var target = targetByPe[cursor];
                    targets.Add(new Phase8AMulticastBranchTarget(
                        cursor, route, ActivationPath(tree.GlobalKTileIndex, componentId, cursor)));
                    pipelines.Add(new Phase8ABranchTargetPipeline(cursor, BuildExecutionChain(plan, target.AssignmentId)));
                }
            }
            var flowId = BranchFlow(tree.GlobalKTileIndex, componentId);
            var branchPlan = new Phase8AMulticastBranchPlan(flowId, branches[componentId].BranchId, targets);
            flows.Insert(0, new CommunicationFlow(
                flowId,
                BranchComponent(componentId),
                targets.Select(target => target.ConsumerComponentId).ToArray(),
                tree.ActivationTileId,
                tree.Bits,
                Phase8ACommunicationFlowKinds.Multicast,
                [BranchComponent(componentId)],
                targets.Select(target => new CommunicationConsumerRoute(
                    target.ConsumerComponentId, target.RoutePathId, target.RouteLinkIds)).ToArray()));
            var node = new BranchRuntimeNode(branchPlan, pipelines.ToArray(), flows.ToArray());
            built[componentId] = node;
            return node;
        }

        var initialRoute = new List<string> { ActivationSourceLink(tree.SourceClusterIndex) };
        var cursor = tree.SourceComponentId;
        if (tree.Targets.Count == 1)
        {
            var target = tree.Targets[0];
            initialRoute.AddRange(target.RouteLinkIds);
            return new Phase8AActivationRuntimeProgram(
                tree, ActivationSourceFlow(tree.GlobalKTileIndex), target.PeComponentId, initialRoute,
                null, [], BuildExecutionChain(plan, target.AssignmentId), []);
        }
        while (!branches.ContainsKey(cursor))
        {
            if (!outgoing.TryGetValue(cursor, out var nextEdges) || nextEdges.Length != 1)
                throw new InvalidOperationException("Activation tree has no deterministic first branch for K tile " + tree.GlobalKTileIndex);
            initialRoute.Add(nextEdges[0].LinkId);
            cursor = nextEdges[0].DestinationComponentId;
        }
        initialRoute.Add(BranchInputLink(cursor));
        var root = BuildBranch(cursor);
        return new Phase8AActivationRuntimeProgram(
            tree, ActivationSourceFlow(tree.GlobalKTileIndex), BranchComponent(cursor), initialRoute,
            root.Plan, root.Pipelines, [], root.Flows);
    }

    private static IReadOnlyList<Phase8AStageRoute> BuildExecutionChain(
        Phase8AMatMulScenarioPlan plan,
        string dcAssignmentId)
    {
        var local = plan.HierarchicalReduction.LocalGroups.Single(group =>
            group.Contributors.Any(contributor => contributor.AssignmentId == dcAssignmentId));
        var contributor = local.Contributors.Single(item => item.AssignmentId == dcAssignmentId);
        var global = plan.HierarchicalReduction.GlobalGroups.Single(group =>
            group.Contributors.Any(item => item.LocalGroupId == local.GroupId));
        var globalContributor = global.Contributors.Single(item => item.LocalGroupId == local.GroupId);
        var shard = plan.HierarchicalReduction.FinalAssembly.Shards.Single(item => item.SourceResultId == global.OutputResultId);
        var stages = new List<Phase8AStageRoute>();

        if (local.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
        {
            stages.Add(new Phase8AStageRoute(
                Path(PartialFlow(dcAssignmentId)),
                LocalCollector(local.GroupId),
                contributor.RouteLinkIds.Append(LocalInputLink(local.GroupId)),
                SumMetadata(
                    local.GroupKey,
                    local.Contributors.Select(item => item.PeComponentId),
                    contributor.PeComponentId,
                    local.NRange,
                    "local-sum")));
        }

        if (global.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
        {
            var route = new List<string>();
            var authorityRoute = globalContributor.ReturnRouteLinkIds.Concat(globalContributor.MeshRouteLinkIds).ToArray();
            if (local.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
            {
                route.Add(LocalOutputLink(local.GroupId));
                route.AddRange(authorityRoute.Skip(1));
            }
            else
            {
                route.AddRange(authorityRoute);
            }
            route.Add(GlobalInputLink(global.GroupId));
            stages.Add(new Phase8AStageRoute(
                Path(local.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum
                    ? LocalOutputFlow(local.GroupId)
                    : PartialFlow(dcAssignmentId)),
                GlobalCollector(global.GroupId),
                route,
                SumMetadata(
                    global.GroupKey,
                    global.Contributors.Select(item => item.LocalResultId),
                    local.OutputResultId,
                    global.NRange,
                    "global-sum")));
        }

        var assemblyRoute = new List<string>();
        if (global.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
        {
            assemblyRoute.Add(GlobalOutputLink(global.GroupId));
            if (shard.MeshRouteLinkIds.Count > 0)
            {
                assemblyRoute.AddRange(shard.MeshRouteLinkIds.Skip(1));
                assemblyRoute.Add(AssemblyInputLink);
            }
        }
        else
        {
            var authorityRoute = shard.ReturnRouteLinkIds.Concat(shard.MeshRouteLinkIds).ToArray();
            if (local.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
            {
                assemblyRoute.Add(LocalOutputLink(local.GroupId));
                assemblyRoute.AddRange(authorityRoute.Skip(1));
            }
            else
            {
                assemblyRoute.AddRange(authorityRoute);
            }
            assemblyRoute.Add(AssemblyInputLink);
        }
        var assemblyFlow = global.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum
            ? GlobalOutputFlow(global.GroupId)
            : local.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum
                ? LocalOutputFlow(local.GroupId)
                : PartialFlow(dcAssignmentId);
        stages.Add(new Phase8AStageRoute(
            Path(assemblyFlow),
            Assembly,
            assemblyRoute,
            AssemblyMetadata(plan, global, shard)));
        stages.Add(new Phase8AStageRoute(Path(AssemblyOutputFlow()), Sink, [AssemblyOutputLink]));
        return stages;
    }

    private static Dictionary<string, string> SumMetadata(
        string groupKey,
        IEnumerable<string> expected,
        string contributor,
        MappingIndexRange nRange,
        string stageId) => new(StringComparer.Ordinal)
    {
        [Phase8ACollectiveRuntimeMetadata.OperationKind] = Phase8AGroupedVectorSumContract.SumOperation,
        [Phase8ACollectiveRuntimeMetadata.GroupKey] = groupKey,
        [Phase8ACollectiveRuntimeMetadata.ExpectedContributors] = Phase8ACollectiveMetadataCodec.EncodeStringList(expected),
        [Phase8ACollectiveRuntimeMetadata.ContributorId] = contributor,
        [Phase8ACollectiveRuntimeMetadata.OutputMOffset] = "0",
        [Phase8ACollectiveRuntimeMetadata.OutputMExtent] = "1",
        [Phase8ACollectiveRuntimeMetadata.OutputNOffset] = nRange.Offset.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.OutputNExtent] = nRange.Extent.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.DType] = PrecisionKind.FP8_E4M3.ToString(),
        [Phase8AOperandPipelineMetadata.LayerId] = "matmul",
        [Phase8AOperandPipelineMetadata.StageId] = stageId
    };

    private static Dictionary<string, string> AssemblyMetadata(
        Phase8AMatMulScenarioPlan plan,
        Phase8AGlobalReductionPlan global,
        Phase8AFinalAssemblyShardPlan shard) => new(StringComparer.Ordinal)
    {
        [Phase8ACollectiveRuntimeMetadata.OperationKind] = Phase8ATensorAssemblyContract.ConcatOperation,
        [Phase8ACollectiveRuntimeMetadata.GroupKey] = "scenario:assembly:Y",
        [Phase8ACollectiveRuntimeMetadata.ExpectedContributors] = Phase8ACollectiveMetadataCodec.EncodeStringList(
            plan.HierarchicalReduction.GlobalGroups.OrderBy(item => item.NRange.Offset).Select(item => item.OutputResultId)),
        [Phase8ACollectiveRuntimeMetadata.ContributorId] = global.OutputResultId,
        [Phase8ACollectiveRuntimeMetadata.OutputMOffset] = "0",
        [Phase8ACollectiveRuntimeMetadata.OutputMExtent] = "1",
        [Phase8ACollectiveRuntimeMetadata.OutputNOffset] = shard.NRange.Offset.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.OutputNExtent] = shard.NRange.Extent.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.TensorMExtent] = "1",
        [Phase8ACollectiveRuntimeMetadata.TensorNExtent] = plan.Request.N.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.DType] = PrecisionKind.FP8_E4M3.ToString(),
        [Phase8AOperandPipelineMetadata.LayerId] = "matmul",
        [Phase8AOperandPipelineMetadata.StageId] = "offset-assembly"
    };

    public static WorkloadMappingV2 BuildMapping(
        Phase8AMatMulScenarioPlan plan,
        HardwareGraph graph,
        IReadOnlyList<Phase8AActivationRuntimeProgram> programs)
    {
        var capabilities = new List<ComponentCapabilitySnapshot>(plan.ReferenceCapabilities.Components);
        var existing = capabilities.Select(capability => capability.ComponentId).ToHashSet(StringComparer.Ordinal);
        capabilities.AddRange(graph.Components.Where(component => !existing.Contains(component.Id)).Select(GenericCapability));
        var snapshot = new CapabilitySnapshot(
            ComponentExecutionJson.ComputeSha256("phase8a-generated-runtime-capabilities-v2\n" + plan.ReferenceCapabilities.SnapshotId),
            plan.ReferenceCapabilities.HardwareGraphHash,
            plan.ReferenceCapabilities.PlacementHash,
            plan.ReferenceCapabilities.RouteHash,
            plan.ReferenceCapabilities.RegistryHash,
            capabilities);
        var flows = BuildFlows(plan, programs);
        var collectives = BuildCollectives(plan);
        return new WorkloadMappingV2(
            WorkloadMappingV2.CurrentSchemaVersion,
            $"phase8a-matmul-k{plan.Request.K}-n{plan.Request.N}-d{plan.Request.WeightRowDivisionSize}-c{plan.Request.ClusterSize}",
            WorkloadMappingV2Modes.TopologyAware,
            snapshot,
            plan.Assignments,
            plan.WeightPlacement.Placements,
            flows,
            collectives,
            plan.Candidate,
            new WorkloadMappingV2Provenance(
                plan.Lowering.CanonicalHash,
                plan.MappingAuthorityHash,
                "phase8a-matmul-scenario-generator-v2",
                plan.Request.Seed),
            null,
            WorkloadMappingV2.CurrentCanonicalHashAlgorithm,
            "");
    }

    private static IReadOnlyList<CommunicationFlow> BuildFlows(
        Phase8AMatMulScenarioPlan plan,
        IReadOnlyList<Phase8AActivationRuntimeProgram> programs)
    {
        var flows = new List<CommunicationFlow>();
        foreach (var program in programs)
        {
            flows.Add(UnicastFlow(program.SourceFlowId, ActivationSource, program.InitialDestinationId,
                program.Tree.ActivationTileId, program.Tree.Bits, program.InitialRouteLinkIds));
            flows.AddRange(program.BranchFlows);
        }
        foreach (var assignment in plan.Assignments)
        {
            var lowered = plan.Lowering.OperationTiles.Single(item => item.OperationTileId == assignment.AssignmentId);
            flows.Add(UnicastFlow(WeightFlow(assignment.AssignmentId), WeightSource, assignment.TargetComponentId,
                lowered.WeightTileId, plan.Request.PeRows * plan.Request.PeColumns * 8L, [WeightLink(assignment.AssignmentId)]));
        }
        foreach (var dc in plan.DcLayout.Assignments)
        {
            var target = plan.ActivationTree.Trees.SelectMany(tree => tree.Targets).Single(item => item.AssignmentId == dc.AssignmentId);
            var first = BuildExecutionChain(plan, dc.AssignmentId)[0];
            flows.Add(UnicastFlow(PartialFlow(dc.AssignmentId), target.PeComponentId, first.DestinationComponentId,
                dc.PartialResultId, plan.Request.PeColumns * 8L, first.LinkIds));
        }
        foreach (var local in plan.HierarchicalReduction.LocalGroups.Where(item => item.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum))
        {
            var chain = BuildExecutionChain(plan, local.Contributors[0].AssignmentId);
            var next = chain[1];
            flows.Add(UnicastFlow(LocalOutputFlow(local.GroupId), LocalCollector(local.GroupId), next.DestinationComponentId,
                local.OutputResultId, local.VectorBits, next.LinkIds));
        }
        foreach (var global in plan.HierarchicalReduction.GlobalGroups.Where(item => item.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum))
        {
            var local = plan.HierarchicalReduction.LocalGroups.Single(item => item.GroupId == global.Contributors[0].LocalGroupId);
            var chain = BuildExecutionChain(plan, local.Contributors[0].AssignmentId);
            var next = chain[local.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum ? 2 : 1];
            flows.Add(UnicastFlow(GlobalOutputFlow(global.GroupId), GlobalCollector(global.GroupId), next.DestinationComponentId,
                global.OutputResultId, global.VectorBits, next.LinkIds));
        }
        flows.Add(UnicastFlow(AssemblyOutputFlow(), Assembly, Sink, "Y", plan.Request.N * 8L, [AssemblyOutputLink]));
        return flows;
    }

    private static IReadOnlyList<CollectivePlan> BuildCollectives(Phase8AMatMulScenarioPlan plan)
    {
        var collectives = new List<CollectivePlan>();
        collectives.AddRange(plan.HierarchicalReduction.LocalGroups.Where(item => item.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
            .Select(local => new CollectivePlan(
                "scenario.collective." + local.GroupId,
                Phase8ACollectiveIntentKinds.Sum,
                local.Contributors.Select(item => item.PeComponentId).ToArray(),
                LocalCollector(local.GroupId), local.OutputResultId,
                "stable-k-offset-v1", PrecisionKind.FP8_E4M3.ToString(), local.GroupKey,
                StrictCollectiveErrors())));
        collectives.AddRange(plan.HierarchicalReduction.GlobalGroups.Where(item => item.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
            .Select(global => new CollectivePlan(
                "scenario.collective." + global.GroupId,
                Phase8ACollectiveIntentKinds.Sum,
                global.Contributors.Select(item => item.LocalResultId).ToArray(),
                GlobalCollector(global.GroupId), global.OutputResultId,
                "stable-k-offset-v1", PrecisionKind.FP8_E4M3.ToString(), global.GroupKey,
                StrictCollectiveErrors())));
        collectives.Add(new CollectivePlan(
            "scenario.collective.final-assembly",
            Phase8ACollectiveIntentKinds.Concat,
            plan.HierarchicalReduction.GlobalGroups.OrderBy(item => item.NRange.Offset).Select(item => item.OutputResultId).ToArray(),
            Assembly, "Y", "stable-n-offset-v1", PrecisionKind.FP8_E4M3.ToString(), "scenario:assembly:Y",
            StrictCollectiveErrors()));
        return collectives;
    }

    public static IReadOnlyList<Packet> BuildOperands(
        Phase8AMatMulScenarioPlan plan,
        IReadOnlyList<Phase8AActivationRuntimeProgram> programs)
    {
        var packets = new List<Packet>();
        if (!plan.Request.WeightsPreplaced)
        {
            foreach (var assignment in plan.Assignments.OrderBy(item => item.AssignmentId, StringComparer.Ordinal))
            {
                var tile = plan.Lowering.OperationTiles.Single(item => item.OperationTileId == assignment.AssignmentId);
                packets.Add(Operand(
                    "weight:" + assignment.AssignmentId,
                    WeightSource,
                    WeightPort(assignment.AssignmentId),
                    assignment.TargetComponentId,
                    WeightFlow(assignment.AssignmentId),
                    "weight",
                    tile.WeightTileId,
                    SliceWeights(plan.Weights, plan.Request.N, tile.KRange, tile.NRange),
                    PacketType.Weight,
                    0));
            }
        }
        foreach (var program in programs)
        {
            var tree = program.Tree;
            var values = plan.Input.Skip(checked((int)tree.KRange.Offset)).Take(checked((int)tree.KRange.Extent)).ToArray();
            var packet = Operand(
                "activation:k" + tree.GlobalKTileIndex,
                ActivationSource,
                "out",
                program.InitialDestinationId,
                program.SourceFlowId,
                "input",
                tree.ActivationTileId,
                values,
                PacketType.Activation,
                64L + tree.GlobalKTileIndex * 512L);
            if (program.InitialMulticastPlan is not null)
            {
                Phase8ACollectivePacketBinder.BindMulticast(packet, program.InitialMulticastPlan);
                Phase8ABranchPipelineBinder.Bind(packet, program.InitialTargetPipelines);
            }
            else
            {
                Phase8AStageRouteBinder.BindRemainingRoutes(packet, program.UnicastExecutionChain);
            }
            packets.Add(packet);
        }
        return packets.AsReadOnly();
    }

    private static Packet Operand(
        string id,
        string source,
        string sourcePort,
        string destination,
        string flow,
        string role,
        string tile,
        IReadOnlyList<double> values,
        PacketType packetType,
        long injectionCycle)
    {
        var packet = new Packet
        {
            Id = id,
            PacketType = packetType,
            NumElements = values.Count,
            BitWidth = 8,
            Bits = checked(values.Count * 8),
            Precision = PrecisionKind.FP8_E4M3,
            SourceComponentId = source,
            SourcePort = sourcePort,
            DestinationComponentId = destination,
            CurrentComponentId = source,
            WorkloadOpId = "matmul",
            TensorId = role == "weight" ? "W" : "X",
            TileId = tile,
            RequestId = id + ":request",
            CreatedCycle = injectionCycle,
            InjectionCycle = injectionCycle,
            Values = values.ToList(),
            VisitedComponents = [source]
        };
        packet.Metadata[Phase8AOperandPipelineMetadata.ExternalOperand] = "true";
        packet.Metadata[Phase8AOperandPipelineMetadata.OperandRole] = role;
        packet.Metadata[Phase8AOperandPipelineMetadata.FlowId] = flow;
        packet.Metadata[Phase8AOperandPipelineMetadata.InvocationId] = "scenario";
        packet.Metadata[Phase8AOperandPipelineMetadata.LayerId] = "matmul";
        packet.Metadata[Phase8AOperandPipelineMetadata.StageId] = role + "-preload";
        return packet;
    }

    private static IReadOnlyList<double> SliceWeights(
        IReadOnlyList<double> weights, int totalColumns, MappingIndexRange k, MappingIndexRange n) =>
        Enumerable.Range(0, checked((int)k.Extent))
            .SelectMany(row => weights.Skip(checked((int)((k.Offset + row) * totalColumns + n.Offset))).Take(checked((int)n.Extent)))
            .ToArray();

    private static ComponentCapabilitySnapshot GenericCapability(HardwareComponent component) => new(
        component.Id,
        string.IsNullOrWhiteSpace(component.TypeId) ? ComponentTypeIds.BuiltIn(component.Type) : component.TypeId,
        "runtime", "runtime-hash", "runtime", "runtime-profile-hash",
        component.TypeId, "runtime-kernel-hash", [], new Dictionary<string, string>(),
        [PrecisionKind.FP8_E4M3.ToString()], 0, 0,
        component.Ports.Select(port => (long)Math.Max(0, port.BandwidthBitsPerCycle)).DefaultIfEmpty(0).Max(),
        component.Ports.Select(port => new CapabilityPortSnapshot(
            component.Id + "." + port.Name,
            port.Direction == PortDirection.Input ? "input" : "output",
            "packet", "digital/default", port.BandwidthBitsPerCycle,
            "transport", port.DataType.ToString(), port.Precision.ToString())).ToArray(),
        "digital/default");

    private static CollectiveErrorBehavior StrictCollectiveErrors() => new("error", "error", "error", "error", "error");

    private static CommunicationFlow UnicastFlow(
        string id, string source, string destination, string tile, long bits, IReadOnlyList<string> links) => new(
        id, source, [destination], tile, bits, Phase8ACommunicationFlowKinds.Unicast, [],
        [new CommunicationConsumerRoute(destination, Path(id), links)]);

    private sealed record BranchRuntimeNode(
        Phase8AMulticastBranchPlan Plan,
        IReadOnlyList<Phase8ABranchTargetPipeline> Pipelines,
        IReadOnlyList<CommunicationFlow> Flows);
}
