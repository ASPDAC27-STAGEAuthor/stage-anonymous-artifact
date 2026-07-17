using System.Globalization;

namespace HardwareSim.Core;

internal static class Phase8AMoTInrRuntimeIds
{
    public const string PhysicalMetricScope = "physical";
    public const string InternalMetricScope = "internal-semantic";
    public const string MetricScopeParameter = "scenario_metric_scope";
    public const string DependencyScopeParameter = HardwareDependencyScopes.Parameter;
    public const string StatefulReinjectionDependencyScope = HardwareDependencyScopes.StatefulCapabilityReinjectionV1;
    public const int FixedPacketElements = 32;
    public const int FixedPacketBitWidth = 32;
    public const int FixedPacketBits = FixedPacketElements * FixedPacketBitWidth;

    public static string RowFlow(int row) => $"scenario.flow.mot-inr.activation.row{row:D4}";
    public static string RowFlow(int row, int globalKTileIndex) => $"scenario.flow.mot-inr.activation.row{row:D4}.k{globalKTileIndex:D4}";
    public static string RowTile(int row) => $"X:row:{row:D4}";
    public static string RowTile(int row, int globalKTileIndex) => $"X:row:{row:D4}:k:{globalKTileIndex:D4}";
    public static string RowPath(int row) => $"scenario.path.mot-inr.activation.row{row:D4}";
    public static string RowPath(int row, int globalKTileIndex) => $"scenario.path.mot-inr.activation.row{row:D4}.k{globalKTileIndex:D4}";
    public static string BranchFlow(int cluster) => $"scenario.flow.mot-inr.branch.c{cluster:D4}";
    public static string BranchFlow(int cluster, int globalKTileIndex) => $"scenario.flow.mot-inr.branch.c{cluster:D4}.k{globalKTileIndex:D4}";
    public static string BranchPath(int cluster, string target) =>
        $"scenario.path.mot-inr.branch.c{cluster:D4}.{Phase8AMatMulRuntimeIds.Short(target)}";
    public static string BranchPath(int cluster, int globalKTileIndex, string target) =>
        $"scenario.path.mot-inr.branch.c{cluster:D4}.k{globalKTileIndex:D4}.{Phase8AMatMulRuntimeIds.Short(target)}";
    public static string SlicePath(string source, string target) =>
        $"scenario.path.mot-inr.slice.{Phase8AMatMulRuntimeIds.Short(source)}.{Phase8AMatMulRuntimeIds.Short(target)}";
    public static string SlicePath(int globalKTileIndex, string source, string target) =>
        $"scenario.path.mot-inr.slice.k{globalKTileIndex:D4}.{Phase8AMatMulRuntimeIds.Short(source)}.{Phase8AMatMulRuntimeIds.Short(target)}";
    public static string ReductionPath(string resultId, string target) =>
        $"scenario.path.mot-inr.reduction.{Phase8AMatMulRuntimeIds.Short(resultId)}.{Phase8AMatMulRuntimeIds.Short(target)}";
    public static string LandingPath(string resultId) =>
        $"scenario.path.mot-inr.landing.{Phase8AMatMulRuntimeIds.Short(resultId)}";
    public static string LocalSink(int cluster) => $"scenario-local-output-sink-c{cluster:D4}";
    public static string LocalSinkLink(int cluster) => $"scenario.link.local-output.c{cluster:D4}";
}

internal sealed record Phase8AMoTInrRowProgram(
    int MeshRow,
    int SourceClusterIndex,
    string InitialDestinationComponentId,
    IReadOnlyList<string> InitialRouteLinkIds,
    Phase8AMulticastBranchPlan? BranchPlan,
    IReadOnlyList<Phase8ABranchTargetPipeline> BranchPipelines,
    IReadOnlyDictionary<string, string> InitialMetadata,
    int GlobalKTileIndex = -1,
    MappingIndexRange? KRange = null);

internal sealed record Phase8AMoTInrCompiledProgram(
    IReadOnlyList<Phase8AMoTInrRowProgram> Rows,
    IReadOnlyDictionary<string, IReadOnlyList<Phase8AStageRoute>> AssignmentStageRoutes,
    IReadOnlyList<CommunicationFlow> InitialFlows,
    IReadOnlyList<CollectivePlan> CollectivePlans,
    Phase8AMeshReductionForest? MeshReductionForest = null);

internal static class Phase8AMoTInrRuntimeCompiler
{
    public static (Phase8AMoTInrCompiledProgram? Program, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Compile(
        Phase8AMatMulScenarioPlan plan,
        HardwareGraph graph)
    {
        var issues = ValidateApplicability(plan);
        if (issues.Count > 0) return (null, issues);

        try
        {
            var assignmentRoutes = BuildAssignmentStageRoutes(plan);
            var rows = BuildRows(plan, assignmentRoutes);
            var initialFlows = BuildInitialFlows(plan, rows);
            var collectives = BuildCollectives(plan);
            return (new Phase8AMoTInrCompiledProgram(rows, assignmentRoutes, initialFlows, collectives), []);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            return (null,
            [
                new Phase8AMatMulScenarioIssue(
                    "Phase8AMoTInrProgramCompileFailed",
                    "$.topologyExecutionStrategy",
                    exception.Message)
            ]);
        }
    }

    public static WorkloadMappingV2 BuildMapping(
        Phase8AMatMulScenarioPlan plan,
        HardwareGraph graph,
        Phase8AMoTInrCompiledProgram program)
    {
        var capabilities = new List<ComponentCapabilitySnapshot>(plan.ReferenceCapabilities.Components);
        var existing = capabilities.Select(capability => capability.ComponentId).ToHashSet(StringComparer.Ordinal);
        capabilities.AddRange(graph.Components.Where(component => !existing.Contains(component.Id)).Select(GenericCapability));
        var snapshot = new CapabilitySnapshot(
            ComponentExecutionJson.ComputeSha256("phase8a-mot-inr-runtime-capabilities-v1\n" + plan.ReferenceCapabilities.SnapshotId),
            plan.ReferenceCapabilities.HardwareGraphHash,
            plan.ReferenceCapabilities.PlacementHash,
            plan.ReferenceCapabilities.RouteHash,
            plan.ReferenceCapabilities.RegistryHash,
            capabilities);
        var flows = program.InitialFlows.ToList();
        if (!plan.Request.WeightsPreplaced)
        {
            foreach (var assignment in plan.Assignments)
            {
                var tile = plan.Lowering.OperationTiles.Single(item => item.OperationTileId == assignment.AssignmentId);
                flows.Add(UnicastFlow(
                    Phase8AMatMulRuntimeIds.WeightFlow(assignment.AssignmentId),
                    Phase8AMatMulRuntimeIds.WeightSource,
                    assignment.TargetComponentId,
                    tile.WeightTileId,
                    checked((long)plan.Request.PeRows * plan.Request.PeColumns * 8L),
                    [Phase8AMatMulRuntimeIds.WeightLink(assignment.AssignmentId)]));
            }
        }

        return new WorkloadMappingV2(
            WorkloadMappingV2.CurrentSchemaVersion,
            $"phase8a-mot-inr-k{plan.Request.K}-n{plan.Request.N}-d{plan.Request.WeightRowDivisionSize}-c{plan.Request.ClusterSize}",
            WorkloadMappingV2Modes.TopologyAware,
            snapshot,
            plan.Assignments,
            plan.WeightPlacement.Placements,
            flows,
            program.CollectivePlans,
            plan.Candidate,
            new WorkloadMappingV2Provenance(
                plan.Lowering.CanonicalHash,
                plan.MappingAuthorityHash,
                plan.Request.TopologyExecutionStrategyId + ";output=" + plan.Request.OutputLandingMode,
                plan.Request.Seed),
            null,
            WorkloadMappingV2.CurrentCanonicalHashAlgorithm,
            "");
    }

    public static IReadOnlyList<Packet> BuildOperands(
        Phase8AMatMulScenarioPlan plan,
        Phase8AMoTInrCompiledProgram program)
    {
        var packets = new List<Packet>();
        if (!plan.Request.WeightsPreplaced)
        {
            foreach (var assignment in plan.Assignments.OrderBy(item => item.AssignmentId, StringComparer.Ordinal))
            {
                var tile = plan.Lowering.OperationTiles.Single(item => item.OperationTileId == assignment.AssignmentId);
                packets.Add(Operand(
                    "weight:" + assignment.AssignmentId,
                    Phase8AMatMulRuntimeIds.WeightSource,
                    Phase8AMatMulRuntimeIds.WeightPort(assignment.AssignmentId),
                    assignment.TargetComponentId,
                    Phase8AMatMulRuntimeIds.WeightFlow(assignment.AssignmentId),
                    "weight",
                    tile.WeightTileId,
                    SliceWeights(plan.Weights, plan.Request.N, tile.KRange, tile.NRange),
                    PacketType.Weight,
                    0));
            }
        }

        foreach (var row in program.Rows.OrderBy(item => item.MeshRow))
        {
            var packet = Operand(
                $"activation:row{row.MeshRow:D4}",
                Phase8AMatMulRuntimeIds.ActivationSource,
                "out",
                row.InitialDestinationComponentId,
                Phase8AMoTInrRuntimeIds.RowFlow(row.MeshRow),
                "input",
                Phase8AMoTInrRuntimeIds.RowTile(row.MeshRow),
                plan.Input,
                PacketType.Activation,
                checked(64L + row.MeshRow * 512L));
            foreach (var pair in row.InitialMetadata) packet.Metadata[pair.Key] = pair.Value;
            if (row.BranchPlan is not null)
            {
                Phase8ACollectivePacketBinder.BindMulticast(packet, row.BranchPlan);
                Phase8ABranchPipelineBinder.Bind(packet, row.BranchPipelines);
            }
            packets.Add(packet);
        }

        return packets.AsReadOnly();
    }

    private static List<Phase8AMatMulScenarioIssue> ValidateApplicability(Phase8AMatMulScenarioPlan plan)
    {
        var issues = new List<Phase8AMatMulScenarioIssue>();
        if (!string.Equals(plan.TopologyManifest.Request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal))
            issues.Add(Issue("Phase8AMoTInrTopologyUnsupported", "$.topology", "The strategy requires the canonical Mesh-of-Trees v1 topology."));
        if (!string.Equals(plan.Request.ActivationIngressPolicy, Phase8AActivationIngressPolicies.LeftColumnStriped, StringComparison.Ordinal))
            issues.Add(Issue("Phase8AMoTInrIngressPolicyInvalid", "$.activationIngressPolicy", "The strategy requires left-column row replication."));

        var assignmentsByCluster = plan.DcLayout.Assignments.GroupBy(item => item.ClusterIndex).ToDictionary(group => group.Key, group => group.ToArray());
        var clusters = plan.TopologyManifest.Components.Where(item => item.Role == TopologyPresetComponentRole.MeshRouter)
            .Select(item => item.ClusterIndex ?? -1).OrderBy(index => index).ToArray();
        foreach (var cluster in clusters)
        {
            if (!assignmentsByCluster.TryGetValue(cluster, out var assignments))
            {
                issues.Add(Issue("Phase8AMoTInrClusterCoverageInvalid", $"$.clusters[{cluster}]", "Every cluster must hold one complete K partition for this strategy."));
                continue;
            }
            var nRanges = assignments.Select(item => (item.NRange.Offset, item.NRange.Extent)).Distinct().ToArray();
            var kRanges = assignments.OrderBy(item => item.KRange.Offset).Select(item => item.KRange).ToArray();
            if (nRanges.Length != 1 || assignments.Length != plan.Request.K / plan.Request.PeRows ||
                !IsGapFree(kRanges, plan.Request.K) || assignments.Select(item => item.PeOrdinal).Distinct().Count() != assignments.Length)
                issues.Add(Issue("Phase8AMoTInrClusterCoverageInvalid", $"$.clusters[{cluster}]", "Each cluster must map exactly one N shard and a gap-free, non-overlapping full-K PE partition."));
        }

        if (plan.HierarchicalReduction.GlobalGroups.Any(group =>
                group.Mode != Phase8AHierarchicalReductionModes.Bypass || group.Contributors.Count != 1))
            issues.Add(Issue("Phase8AMoTInrGlobalReductionUnsupported", "$.hierarchicalReduction.globalGroups", "This strategy version requires cluster-local full-K reduction followed by N-range assembly."));
        if (plan.HierarchicalReduction.LocalGroups.Any(group =>
                group.Mode != Phase8AHierarchicalReductionModes.GroupedVectorSum ||
                group.ForwardingReductionComponentIds.Count != 0 ||
                group.Stages.Count != plan.Request.ClusterSize - 1))
            issues.Add(Issue("Phase8AMoTInrLocalTreeInvalid", "$.hierarchicalReduction.localGroups", "Every active cluster must use one complete binary grouped-vector reduction tree without forwarding nodes."));
        return issues;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Phase8AStageRoute>> BuildAssignmentStageRoutes(
        Phase8AMatMulScenarioPlan plan)
    {
        var result = new Dictionary<string, IReadOnlyList<Phase8AStageRoute>>(StringComparer.Ordinal);
        foreach (var local in plan.HierarchicalReduction.LocalGroups.OrderBy(item => item.NRange.Offset))
        {
            var stageByInput = local.Stages.SelectMany(stage => stage.InputResultIds.Select(input => (input, stage)))
                .ToDictionary(item => item.input, item => item.stage, StringComparer.Ordinal);
            var global = plan.HierarchicalReduction.GlobalGroups.Single(item => item.Contributors.Any(contributor => contributor.LocalGroupId == local.GroupId));
            var shard = plan.HierarchicalReduction.FinalAssembly.Shards.Single(item => item.SourceResultId == global.OutputResultId);

            foreach (var contributor in local.Contributors)
            {
                var routes = new List<Phase8AStageRoute>();
                var currentResult = contributor.PartialResultId;
                var currentComponent = contributor.PeComponentId;
                while (stageByInput.TryGetValue(currentResult, out var stage))
                {
                    var link = local.TreeEdges.Single(edge =>
                        string.Equals(edge.SourceComponentId, currentComponent, StringComparison.Ordinal) &&
                        string.Equals(edge.DestinationComponentId, stage.TargetReductionComponentId, StringComparison.Ordinal));
                    routes.Add(new Phase8AStageRoute(
                        Phase8AMoTInrRuntimeIds.ReductionPath(currentResult, stage.TargetReductionComponentId),
                        stage.TargetReductionComponentId,
                        [link.LinkId],
                        SumMetadata(stage, currentResult)));
                    currentResult = stage.OutputResultId;
                    currentComponent = stage.TargetReductionComponentId;
                }

                if (!string.Equals(currentResult, local.OutputResultId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The local reduction chain did not terminate at its authoritative output result.");

                if (plan.Request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1)
                {
                    var landingLinks = shard.ReturnRouteLinkIds.Concat(shard.MeshRouteLinkIds)
                        .Append(Phase8AMatMulRuntimeIds.AssemblyInputLink).ToArray();
                    routes.Add(new Phase8AStageRoute(
                        Phase8AMoTInrRuntimeIds.LandingPath(global.OutputResultId),
                        Phase8AMatMulRuntimeIds.Assembly,
                        landingLinks,
                        AssemblyMetadata(plan, global, shard)));
                    routes.Add(new Phase8AStageRoute(
                        Phase8AMatMulRuntimeIds.Path(Phase8AMatMulRuntimeIds.AssemblyOutputFlow()),
                        Phase8AMatMulRuntimeIds.Sink,
                        [Phase8AMatMulRuntimeIds.AssemblyOutputLink]));
                }
                else
                {
                    var landingLinks = shard.ReturnRouteLinkIds.Append(Phase8AMoTInrRuntimeIds.LocalSinkLink(local.ClusterIndex)).ToArray();
                    routes.Add(new Phase8AStageRoute(
                        Phase8AMoTInrRuntimeIds.LandingPath(global.OutputResultId),
                        Phase8AMoTInrRuntimeIds.LocalSink(local.ClusterIndex),
                        landingLinks,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Phase8ACollectiveRuntimeMetadata.ContributorId] = global.OutputResultId,
                            [Phase8AOperandPipelineMetadata.StageId] = "cluster-local-output"
                        }));
                }

                result[contributor.AssignmentId] = routes.AsReadOnly();
            }
        }
        return result;
    }

    private static IReadOnlyList<Phase8AMoTInrRowProgram> BuildRows(
        Phase8AMatMulScenarioPlan plan,
        IReadOnlyDictionary<string, IReadOnlyList<Phase8AStageRoute>> assignmentRoutes)
    {
        var manifestComponents = plan.TopologyManifest.Components.ToDictionary(item => item.ComponentId, StringComparer.Ordinal);
        var manifestLinks = plan.TopologyManifest.Links.ToArray();
        var targetByAssignment = plan.ActivationTree.Trees.SelectMany(tree => tree.Targets)
            .ToDictionary(target => target.AssignmentId, StringComparer.Ordinal);
        var assignmentsByPe = plan.DcLayout.Assignments.ToDictionary(
            assignment => targetByAssignment[assignment.AssignmentId].PeComponentId,
            StringComparer.Ordinal);
        var meshByRow = plan.TopologyManifest.Components.Where(item => item.Role == TopologyPresetComponentRole.MeshRouter)
            .GroupBy(item => item.MeshCoordinate!.Row)
            .OrderBy(group => group.Key)
            .ToArray();
        var programs = new List<Phase8AMoTInrRowProgram>();

        foreach (var row in meshByRow)
        {
            var clusters = row.OrderBy(item => item.MeshCoordinate!.Column).ToArray();
            var rootMetadata = clusters.ToDictionary(
                mesh => mesh.ClusterIndex!.Value,
                mesh =>
                {
                    var treeRoot = manifestComponents[mesh.ChildComponentIds.Single()];
                    var targets = BuildSliceTargets(treeRoot, new MappingIndexRange(0, plan.Request.K));
                    return (TreeRoot: treeRoot.ComponentId, Metadata: (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Phase8ATensorSliceContract.TargetsMetadata] = Phase8ATensorSliceMetadata.Encode(targets)
                    });
                });

            IReadOnlyList<Phase8ATensorSliceTarget> BuildSliceTargets(
                TopologyManifestComponent node,
                MappingIndexRange parentRange)
            {
                var targets = new List<Phase8ATensorSliceTarget>();
                foreach (var childId in node.ChildComponentIds.OrderBy(id => SubtreeRange(manifestComponents[id]).Offset))
                {
                    var child = manifestComponents[childId];
                    var childRange = SubtreeRange(child);
                    var link = manifestLinks.Single(item =>
                        item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                        string.Equals(item.SourceComponentId, node.ComponentId, StringComparison.Ordinal) &&
                        string.Equals(item.DestinationComponentId, child.ComponentId, StringComparison.Ordinal));
                    IReadOnlyDictionary<string, string>? metadata = null;
                    IReadOnlyList<Phase8AStageRoute> downstream = [];
                    if (child.Role == TopologyPresetComponentRole.TreeRouter)
                    {
                        metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Phase8ATensorSliceContract.TargetsMetadata] = Phase8ATensorSliceMetadata.Encode(BuildSliceTargets(child, childRange))
                        };
                    }
                    else if (child.Role == TopologyPresetComponentRole.ProcessingElement)
                    {
                        var assignment = assignmentsByPe[child.ComponentId];
                        downstream = assignmentRoutes[assignment.AssignmentId];
                    }
                    else
                    {
                        throw new InvalidOperationException("Activation slice hierarchy contains a non-tree, non-PE child.");
                    }

                    targets.Add(new Phase8ATensorSliceTarget(
                        child.ComponentId,
                        checked((int)(childRange.Offset - parentRange.Offset)),
                        checked((int)childRange.Extent),
                        Phase8AMoTInrRuntimeIds.SlicePath(node.ComponentId, child.ComponentId),
                        [link.LinkId],
                        downstream,
                        metadata));
                }
                return targets.AsReadOnly();
            }

            MappingIndexRange SubtreeRange(TopologyManifestComponent component)
            {
                var leaves = DescendantPeIds(component).Select(id => assignmentsByPe[id].KRange).OrderBy(range => range.Offset).ToArray();
                if (leaves.Length == 0 || !IsGapFree(leaves, leaves.Sum(range => checked((int)range.Extent)), leaves[0].Offset))
                    throw new InvalidOperationException("A tensor-slice subtree does not contain one gap-free K range.");
                return new MappingIndexRange(leaves[0].Offset, leaves.Sum(range => range.Extent));
            }

            IEnumerable<string> DescendantPeIds(TopologyManifestComponent component)
            {
                if (component.Role == TopologyPresetComponentRole.ProcessingElement)
                {
                    yield return component.ComponentId;
                    yield break;
                }
                foreach (var child in component.ChildComponentIds.Select(id => manifestComponents[id]))
                foreach (var pe in DescendantPeIds(child))
                    yield return pe;
            }

            (Phase8AMulticastBranchPlan Plan, IReadOnlyList<Phase8ABranchTargetPipeline> Pipelines) BuildBranch(int index)
            {
                var current = clusters[index];
                var currentCluster = current.ClusterIndex!.Value;
                var local = rootMetadata[currentCluster];
                var targets = new List<Phase8AMulticastBranchTarget>();
                var pipelines = new List<Phase8ABranchTargetPipeline>();
                var attachment = manifestLinks.Single(item =>
                    item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                    string.Equals(item.SourceComponentId, current.ComponentId, StringComparison.Ordinal) &&
                    string.Equals(item.DestinationComponentId, local.TreeRoot, StringComparison.Ordinal));
                targets.Add(new Phase8AMulticastBranchTarget(
                    local.TreeRoot,
                    [attachment.LinkId],
                    Phase8AMoTInrRuntimeIds.BranchPath(currentCluster, local.TreeRoot)));
                pipelines.Add(new Phase8ABranchTargetPipeline(local.TreeRoot, [], local.Metadata));

                var next = clusters[index + 1];
                var meshLink = manifestLinks.Single(item =>
                    item.Role == TopologyPresetLinkRole.MeshTransport &&
                    string.Equals(item.SourceComponentId, current.ComponentId, StringComparison.Ordinal) &&
                    string.Equals(item.DestinationComponentId, next.ComponentId, StringComparison.Ordinal));
                if (index + 1 == clusters.Length - 1)
                {
                    var nextCluster = next.ClusterIndex!.Value;
                    var nextRoot = rootMetadata[nextCluster];
                    var nextAttachment = manifestLinks.Single(item =>
                        item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                        string.Equals(item.SourceComponentId, next.ComponentId, StringComparison.Ordinal) &&
                        string.Equals(item.DestinationComponentId, nextRoot.TreeRoot, StringComparison.Ordinal));
                    targets.Add(new Phase8AMulticastBranchTarget(
                        nextRoot.TreeRoot,
                        [meshLink.LinkId, nextAttachment.LinkId],
                        Phase8AMoTInrRuntimeIds.BranchPath(currentCluster, nextRoot.TreeRoot)));
                    pipelines.Add(new Phase8ABranchTargetPipeline(nextRoot.TreeRoot, [], nextRoot.Metadata));
                }
                else
                {
                    var nested = BuildBranch(index + 1);
                    targets.Add(new Phase8AMulticastBranchTarget(
                        next.ComponentId,
                        [meshLink.LinkId],
                        Phase8AMoTInrRuntimeIds.BranchPath(currentCluster, next.ComponentId)));
                    pipelines.Add(new Phase8ABranchTargetPipeline(next.ComponentId, [], null, nested.Plan, nested.Pipelines));
                }

                return (
                    new Phase8AMulticastBranchPlan(
                        Phase8AMoTInrRuntimeIds.BranchFlow(currentCluster),
                        $"mot-inr-row-branch:c{currentCluster:D4}",
                        targets),
                    pipelines.AsReadOnly());
            }

            var first = clusters[0];
            var sourceCluster = first.ClusterIndex!.Value;
            if (clusters.Length == 1)
            {
                var root = rootMetadata[sourceCluster];
                var attachment = manifestLinks.Single(item =>
                    item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                    string.Equals(item.SourceComponentId, first.ComponentId, StringComparison.Ordinal) &&
                    string.Equals(item.DestinationComponentId, root.TreeRoot, StringComparison.Ordinal));
                programs.Add(new Phase8AMoTInrRowProgram(
                    row.Key,
                    sourceCluster,
                    root.TreeRoot,
                    [Phase8AMatMulRuntimeIds.ActivationSourceLink(sourceCluster), attachment.LinkId],
                    null,
                    [],
                    root.Metadata));
            }
            else
            {
                var branch = BuildBranch(0);
                programs.Add(new Phase8AMoTInrRowProgram(
                    row.Key,
                    sourceCluster,
                    first.ComponentId,
                    [Phase8AMatMulRuntimeIds.ActivationSourceLink(sourceCluster)],
                    branch.Plan,
                    branch.Pipelines,
                    new Dictionary<string, string>()));
            }
        }
        return programs.AsReadOnly();
    }

    private static IReadOnlyList<CommunicationFlow> BuildInitialFlows(
        Phase8AMatMulScenarioPlan plan,
        IReadOnlyList<Phase8AMoTInrRowProgram> rows) => rows.Select(row => UnicastFlow(
            Phase8AMoTInrRuntimeIds.RowFlow(row.MeshRow),
            Phase8AMatMulRuntimeIds.ActivationSource,
            row.InitialDestinationComponentId,
            Phase8AMoTInrRuntimeIds.RowTile(row.MeshRow),
            checked((long)plan.Request.K * 8L),
            row.InitialRouteLinkIds)).ToArray();

    private static IReadOnlyList<CollectivePlan> BuildCollectives(Phase8AMatMulScenarioPlan plan)
    {
        var result = plan.HierarchicalReduction.LocalGroups
            .SelectMany(local => local.Stages.Select(stage => new CollectivePlan(
                "scenario.collective." + stage.StageId,
                Phase8ACollectiveIntentKinds.Sum,
                stage.InputResultIds,
                stage.TargetReductionComponentId,
                stage.OutputResultId,
                "stable-k-offset-v1",
                PrecisionKind.FP8_E4M3.ToString(),
                stage.StageId,
                StrictCollectiveErrors())))
            .ToList();
        if (plan.Request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1)
        {
            result.Add(new CollectivePlan(
                "scenario.collective.final-assembly",
                Phase8ACollectiveIntentKinds.Concat,
                plan.HierarchicalReduction.GlobalGroups.OrderBy(item => item.NRange.Offset).Select(item => item.OutputResultId).ToArray(),
                Phase8AMatMulRuntimeIds.Assembly,
                "Y",
                "stable-n-offset-v1",
                PrecisionKind.FP8_E4M3.ToString(),
                "scenario:assembly:Y",
                StrictCollectiveErrors()));
        }
        return result.AsReadOnly();
    }

    private static Dictionary<string, string> SumMetadata(Phase8ALocalReductionStage stage, string contributorId) =>
        new(StringComparer.Ordinal)
        {
            [Phase8ACollectiveRuntimeMetadata.OperationKind] = Phase8AGroupedVectorSumContract.SumOperation,
            [Phase8ACollectiveRuntimeMetadata.GroupKey] = stage.StageId,
            [Phase8ACollectiveRuntimeMetadata.ExpectedContributors] = Phase8ACollectiveMetadataCodec.EncodeStringList(stage.InputResultIds),
            [Phase8ACollectiveRuntimeMetadata.ContributorId] = contributorId,
            [Phase8ACollectiveRuntimeMetadata.OutputMOffset] = "0",
            [Phase8ACollectiveRuntimeMetadata.OutputMExtent] = "1",
            [Phase8ACollectiveRuntimeMetadata.OutputNOffset] = stage.NRange.Offset.ToString(CultureInfo.InvariantCulture),
            [Phase8ACollectiveRuntimeMetadata.OutputNExtent] = stage.NRange.Extent.ToString(CultureInfo.InvariantCulture),
            [Phase8ACollectiveRuntimeMetadata.DType] = PrecisionKind.FP8_E4M3.ToString(),
            [Phase8AOperandPipelineMetadata.LayerId] = "matmul",
            [Phase8AOperandPipelineMetadata.StageId] = "tree-node-sum"
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
        packet.Metadata["phase8a.topology_execution_strategy_id"] = Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1;
        return packet;
    }

    private static IReadOnlyList<double> SliceWeights(
        IReadOnlyList<double> weights,
        int totalColumns,
        MappingIndexRange k,
        MappingIndexRange n) => Enumerable.Range(0, checked((int)k.Extent))
        .SelectMany(row => weights.Skip(checked((int)((k.Offset + row) * totalColumns + n.Offset))).Take(checked((int)n.Extent)))
        .ToArray();

    private static ComponentCapabilitySnapshot GenericCapability(HardwareComponent component) => new(
        component.Id,
        string.IsNullOrWhiteSpace(component.TypeId) ? ComponentTypeIds.BuiltIn(component.Type) : component.TypeId,
        "runtime",
        "runtime-hash",
        "runtime",
        "runtime-profile-hash",
        component.TypeId,
        "runtime-kernel-hash",
        [],
        new Dictionary<string, string>(),
        [PrecisionKind.FP8_E4M3.ToString()],
        0,
        0,
        component.Ports.Select(port => (long)Math.Max(0, port.BandwidthBitsPerCycle)).DefaultIfEmpty(0).Max(),
        component.Ports.Select(port => new CapabilityPortSnapshot(
            component.Id + "." + port.Name,
            port.Direction == PortDirection.Input ? "input" : "output",
            "packet",
            "digital/default",
            port.BandwidthBitsPerCycle,
            "transport",
            port.DataType.ToString(),
            port.Precision.ToString())).ToArray(),
        "digital/default");

    private static CommunicationFlow UnicastFlow(
        string id,
        string source,
        string destination,
        string tile,
        long bits,
        IReadOnlyList<string> links) => new(
        id,
        source,
        [destination],
        tile,
        bits,
        Phase8ACommunicationFlowKinds.Unicast,
        [],
        [new CommunicationConsumerRoute(destination, Phase8AMatMulRuntimeIds.Path(id), links)]);

    private static CollectiveErrorBehavior StrictCollectiveErrors() => new("error", "error", "error", "error", "error");

    private static bool IsGapFree(IReadOnlyList<MappingIndexRange> ranges, int totalExtent, long start = 0)
    {
        var cursor = start;
        foreach (var range in ranges.OrderBy(item => item.Offset))
        {
            if (range.Offset != cursor || range.Extent <= 0) return false;
            cursor = checked(cursor + range.Extent);
        }
        return cursor == checked(start + totalExtent);
    }

    private static Phase8AMatMulScenarioIssue Issue(string code, string location, string message) =>
        new(code, location, message);
}
