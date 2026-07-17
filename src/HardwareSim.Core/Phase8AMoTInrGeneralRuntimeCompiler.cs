using System.Globalization;

namespace HardwareSim.Core;

internal static class Phase8AMoTInrGeneralRuntimeIds
{
    public static string MeshSumCapability(string meshRouterComponentId) =>
        $"mot-inr-mesh-sum-{Phase8AMatMulRuntimeIds.Short(meshRouterComponentId)}";
    public static string MeshSumInputLink(string meshRouterComponentId) =>
        $"scenario.link.mot-inr.mesh-sum-input.{Phase8AMatMulRuntimeIds.Short(meshRouterComponentId)}";
    public static string MeshSumOutputLink(string meshRouterComponentId) =>
        $"scenario.link.mot-inr.mesh-sum-output.{Phase8AMatMulRuntimeIds.Short(meshRouterComponentId)}";
    public static string FinalLocalSink(int nShard) => $"scenario-local-output-sink-n{nShard:D4}";
    public static string FinalLocalSinkLink(int nShard) => $"scenario.link.local-output.n{nShard:D4}";
}

internal static class Phase8AMoTInrGeneralRuntimeCompiler
{
    public static (Phase8AMoTInrCompiledProgram? Program, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Compile(
        Phase8AMatMulScenarioPlan plan,
        HardwareGraph graph)
    {
        var issues = ValidateApplicability(plan);
        if (issues.Count > 0) return (null, issues);
        try
        {
            var forest = Phase8AMeshReductionForestPlanner.Build(plan);
            var assignmentRoutes = BuildAssignmentStageRoutes(plan, forest);
            var rows = BuildRows(plan, assignmentRoutes);
            return (new Phase8AMoTInrCompiledProgram(
                rows,
                assignmentRoutes,
                BuildInitialFlows(plan, rows),
                BuildCollectives(plan, forest),
                forest), []);
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
        var referenceById = plan.ReferenceCapabilities.Components
            .ToDictionary(component => component.ComponentId, StringComparer.Ordinal);
        var capabilities = graph.Components.Select(component =>
                referenceById.TryGetValue(component.Id, out var reference) &&
                component.TemplateRef is not null
                    ? reference
                    : GenericCapability(component))
            .ToArray();
        var snapshot = new CapabilitySnapshot(
            ComponentExecutionJson.ComputeSha256("phase8a-mot-inr-general-runtime-capabilities-v1\n" + plan.ReferenceCapabilities.SnapshotId),
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
                    checked((long)plan.Request.PeRows * plan.Request.PeColumns * (long)Phase8AMoTInrRuntimeIds.FixedPacketBitWidth),
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
                plan.Request.TopologyExecutionStrategyId + ";output=" + plan.Request.OutputLandingMode + ";mesh-reduction=" + program.MeshReductionForest!.CanonicalHash,
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

        foreach (var row in program.Rows
                     .OrderBy(item => item.MeshRow)
                     .ThenBy(item => item.GlobalKTileIndex))
        {
            var kRange = row.KRange
                ?? throw new InvalidOperationException("A mapping-aware row packet is missing its K range.");
            var values = plan.Input
                .Skip(checked((int)kRange.Offset))
                .Take(checked((int)kRange.Extent))
                .ToArray();
            var packet = Operand(
                $"activation:row{row.MeshRow:D4}:k{row.GlobalKTileIndex:D4}",
                Phase8AMatMulRuntimeIds.ActivationSource,
                "out",
                row.InitialDestinationComponentId,
                Phase8AMoTInrRuntimeIds.RowFlow(row.MeshRow, row.GlobalKTileIndex),
                "input",
                Phase8AMoTInrRuntimeIds.RowTile(row.MeshRow, row.GlobalKTileIndex),
                values,
                PacketType.Activation,
                checked(64L + row.MeshRow * 512L + row.GlobalKTileIndex * 8L));
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

        var assignmentsByCluster = plan.DcLayout.Assignments.GroupBy(item => item.ClusterIndex)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var clusters = plan.TopologyManifest.Components
            .Where(item => item.Role == TopologyPresetComponentRole.MeshRouter)
            .Select(item => item.ClusterIndex ?? -1)
            .OrderBy(index => index)
            .ToArray();
        foreach (var cluster in clusters)
        {
            if (!assignmentsByCluster.TryGetValue(cluster, out var assignments) || assignments.Length == 0 ||
                assignments.Length > plan.Request.ClusterSize ||
                assignments.Select(item => item.PeOrdinal).Distinct().Count() != assignments.Length ||
                assignments.Any(item => item.KRange.Offset < 0 || item.KRange.Extent <= 0 ||
                                        item.KRange.Offset > plan.Request.K - item.KRange.Extent ||
                                        item.NRange.Offset < 0 || item.NRange.Extent <= 0 ||
                                        item.NRange.Offset > plan.Request.N - item.NRange.Extent))
                issues.Add(Issue("Phase8AMoTInrClusterCoverageInvalid", $"$.clusters[{cluster}]", "Every topology cluster must contain one or more unique, in-range D/C PE assignments."));
        }

        foreach (var local in plan.HierarchicalReduction.LocalGroups)
        {
            var expectedStages = Math.Max(0, local.Contributors.Count - 1);
            if (local.Stages.Count != expectedStages ||
                (expectedStages == 0 && local.Mode != Phase8AHierarchicalReductionModes.Bypass) ||
                (expectedStages > 0 && local.Mode != Phase8AHierarchicalReductionModes.GroupedVectorSum))
                issues.Add(Issue("Phase8AMoTInrLocalTreeInvalid", $"$.hierarchicalReduction.localGroups[{local.GroupId}]", "Local groups must expose exactly contributor-count minus one real binary tree stages."));
        }

        var activeNodes = plan.HierarchicalReduction.LocalGroups.SelectMany(group => group.Stages)
            .Select(stage => stage.TargetReductionComponentId).ToHashSet(StringComparer.Ordinal);
        var activeForwardingOverlap = plan.HierarchicalReduction.LocalGroups
            .SelectMany(group => group.ForwardingReductionComponentIds)
            .FirstOrDefault(activeNodes.Contains);
        if (activeForwardingOverlap is not null)
            issues.Add(Issue("Phase8AMoTInrReductionRoleConflict", "$.hierarchicalReduction.localGroups", $"Reduction node '{activeForwardingOverlap}' cannot be both a numerical stage and a forwarding-only node."));

        if (plan.Request.OutputLandingMode == Phase8AOutputLandingModes.ClusterLocalShardsV1 &&
            plan.HierarchicalReduction.GlobalGroups.Any(group => group.Contributors.Count > 1))
            issues.Add(Issue("Phase8AMoTInrClusterLocalReductionIncomplete", "$.outputLandingMode", "Cluster-local landing is valid only when every global output group bypasses Mesh reduction; use topology-egress or central assembly for distributed reduction."));

        foreach (var global in plan.HierarchicalReduction.GlobalGroups)
        {
            var expectedMode = global.Contributors.Count == 1
                ? Phase8AHierarchicalReductionModes.Bypass
                : Phase8AHierarchicalReductionModes.GroupedVectorSum;
            if (global.Mode != expectedMode)
                issues.Add(Issue("Phase8AMoTInrGlobalReductionInvalid", $"$.hierarchicalReduction.globalGroups[{global.GroupId}]", "Global groups must bypass one contributor or sum two or more same-N local results."));
        }
        return issues;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Phase8AStageRoute>> BuildAssignmentStageRoutes(
        Phase8AMatMulScenarioPlan plan,
        Phase8AMeshReductionForest forest)
    {
        var result = new Dictionary<string, IReadOnlyList<Phase8AStageRoute>>(StringComparer.Ordinal);
        foreach (var local in plan.HierarchicalReduction.LocalGroups
                     .OrderBy(item => item.NRange.Offset)
                     .ThenBy(item => item.KRange.Offset))
        {
            var stageByInput = local.Stages.SelectMany(stage => stage.InputResultIds.Select(input => (input, stage)))
                .ToDictionary(item => item.input, item => item.stage, StringComparer.Ordinal);
            var edgeBySource = local.TreeEdges.GroupBy(edge => edge.SourceComponentId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var global = plan.HierarchicalReduction.GlobalGroups.Single(item =>
                item.Contributors.Any(contributor => contributor.LocalGroupId == local.GroupId));
            var globalContributor = global.Contributors.Single(item => item.LocalGroupId == local.GroupId);
            var shard = plan.HierarchicalReduction.FinalAssembly.Shards.Single(item => item.SourceResultId == global.OutputResultId);
            var forestGroup = forest.Groups.Single(item => item.GlobalGroupId == global.GroupId);
            var forestLeaf = forestGroup.Leaves.Single(item => item.LocalGroupId == local.GroupId);
            var meshStageByInput = forestGroup.Stages
                .SelectMany(stage => stage.Inputs.Select(input => (input.ResultId, Stage: stage)))
                .ToDictionary(item => item.ResultId, item => item.Stage, StringComparer.Ordinal);

            IReadOnlyList<string> PathTo(string source, string target)
            {
                var links = new List<string>();
                var current = source;
                var seen = new HashSet<string>(StringComparer.Ordinal) { current };
                while (!string.Equals(current, target, StringComparison.Ordinal))
                {
                    if (!edgeBySource.TryGetValue(current, out var outgoing) || outgoing.Length != 1)
                        throw new InvalidOperationException($"Local group '{local.GroupId}' has no unique return-tree path from '{current}' to '{target}'.");
                    var edge = outgoing[0];
                    links.Add(edge.LinkId);
                    current = edge.DestinationComponentId;
                    if (!seen.Add(current)) throw new InvalidOperationException("Local reduction return path contains a cycle.");
                }
                return links.AsReadOnly();
            }

            foreach (var contributor in local.Contributors)
            {
                var routes = new List<Phase8AStageRoute>();
                var currentResult = contributor.PartialResultId;
                var currentComponent = contributor.PeComponentId;
                while (stageByInput.TryGetValue(currentResult, out var stage))
                {
                    var links = PathTo(currentComponent, stage.TargetReductionComponentId);
                    routes.Add(new Phase8AStageRoute(
                        Phase8AMoTInrRuntimeIds.ReductionPath(currentResult, stage.TargetReductionComponentId),
                        stage.TargetReductionComponentId,
                        links,
                        LocalSumMetadata(stage, currentResult)));
                    currentResult = stage.OutputResultId;
                    currentComponent = stage.TargetReductionComponentId;
                }
                if (!string.Equals(currentResult, local.OutputResultId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The local reduction chain did not terminate at its authoritative output result.");

                if (forestGroup.Stages.Count > 0)
                {
                    var prefix = forestLeaf.ReturnRouteLinkIds;
                    while (meshStageByInput.TryGetValue(currentResult, out var meshStage))
                    {
                        var stageInput = meshStage.Inputs.Single(input => input.ResultId == currentResult);
                        var stageLinks = prefix
                            .Concat(stageInput.MeshRouteLinkIds)
                            .Append(Phase8AMoTInrGeneralRuntimeIds.MeshSumInputLink(meshStage.TargetMeshRouterComponentId))
                            .ToArray();
                        var capabilityId = Phase8AMoTInrGeneralRuntimeIds.MeshSumCapability(meshStage.TargetMeshRouterComponentId);
                        routes.Add(new Phase8AStageRoute(
                            Phase8AMoTInrRuntimeIds.ReductionPath(currentResult, capabilityId),
                            capabilityId,
                            stageLinks,
                            MeshSumMetadata(meshStage, currentResult)));
                        currentResult = meshStage.OutputResultId;
                        prefix = [Phase8AMoTInrGeneralRuntimeIds.MeshSumOutputLink(meshStage.TargetMeshRouterComponentId)];
                    }
                    if (!string.Equals(currentResult, forestGroup.RootResultId, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Mesh reduction group '{forestGroup.GlobalGroupId}' did not terminate at its resolved root.");
                    AddLanding(routes, plan, global, shard, forestGroup, prefix);
                }
                else
                {
                    AddLanding(routes, plan, global, shard, forestGroup, shard.ReturnRouteLinkIds);
                }
                result[contributor.AssignmentId] = routes.AsReadOnly();
            }
        }
        return result;
    }

    private static void AddLanding(
        List<Phase8AStageRoute> routes,
        Phase8AMatMulScenarioPlan plan,
        Phase8AGlobalReductionPlan global,
        Phase8AFinalAssemblyShardPlan shard,
        Phase8AMeshReductionGroup forestGroup,
        IReadOnlyList<string> prefix)
    {
        if (plan.Request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1)
        {
            var landingLinks = prefix
                .Concat(forestGroup.RootToAssemblyMeshRouteLinkIds)
                .Append(Phase8AMatMulRuntimeIds.AssemblyInputLink)
                .ToArray();
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
            var landingLinks = prefix
                .Append(Phase8AMoTInrGeneralRuntimeIds.FinalLocalSinkLink(global.NShardIndex))
                .ToArray();
            routes.Add(new Phase8AStageRoute(
                Phase8AMoTInrRuntimeIds.LandingPath(global.OutputResultId),
                Phase8AMoTInrGeneralRuntimeIds.FinalLocalSink(global.NShardIndex),
                landingLinks,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Phase8ACollectiveRuntimeMetadata.ContributorId] = global.OutputResultId,
                    [Phase8AOperandPipelineMetadata.StageId] = "cluster-local-output"
                }));
        }
    }
    private static IReadOnlyList<Phase8AMoTInrRowProgram> BuildRows(
        Phase8AMatMulScenarioPlan plan,
        IReadOnlyDictionary<string, IReadOnlyList<Phase8AStageRoute>> assignmentRoutes)
    {
        var components = plan.TopologyManifest.Components.ToDictionary(item => item.ComponentId, StringComparer.Ordinal);
        var links = plan.TopologyManifest.Links.ToArray();
        var targetByAssignment = plan.ActivationTree.Trees.SelectMany(tree => tree.Targets)
            .ToDictionary(target => target.AssignmentId, StringComparer.Ordinal);
        var assignmentByPe = plan.DcLayout.Assignments.ToDictionary(
            assignment => targetByAssignment[assignment.AssignmentId].PeComponentId,
            StringComparer.Ordinal);
        var meshByRow = plan.TopologyManifest.Components
            .Where(item => item.Role == TopologyPresetComponentRole.MeshRouter)
            .GroupBy(item => item.MeshCoordinate!.Row)
            .OrderBy(group => group.Key)
            .ToArray();
        var programs = new List<Phase8AMoTInrRowProgram>();

        IEnumerable<Phase8ADcPeAssignment> ActiveDescendantAssignments(
            TopologyManifestComponent component,
            int globalKTileIndex)
        {
            if (component.Role == TopologyPresetComponentRole.ProcessingElement)
            {
                if (assignmentByPe.TryGetValue(component.ComponentId, out var assignment) &&
                    assignment.GlobalKTileIndex == globalKTileIndex)
                    yield return assignment;
                yield break;
            }
            foreach (var child in component.ChildComponentIds.Select(id => components[id]))
            foreach (var assignment in ActiveDescendantAssignments(child, globalKTileIndex))
                yield return assignment;
        }

        IReadOnlyList<Phase8ATensorSliceTarget> BuildTargets(
            TopologyManifestComponent node,
            int globalKTileIndex,
            MappingIndexRange kRange)
        {
            var activeChildren = node.ChildComponentIds.Select(id => components[id])
                .Select(child => (Child: child, Assignments: ActiveDescendantAssignments(child, globalKTileIndex).ToArray()))
                .Where(item => item.Assignments.Length > 0)
                .OrderBy(item => item.Child.ComponentId, StringComparer.Ordinal)
                .ToArray();
            if (activeChildren.Length == 0)
                throw new InvalidOperationException($"Activation tree node '{node.ComponentId}' has no target for K tile {globalKTileIndex}.");

            var targets = new List<Phase8ATensorSliceTarget>();
            foreach (var item in activeChildren)
            {
                if (item.Assignments.Any(assignment => assignment.KRange != kRange))
                    throw new InvalidOperationException($"Activation K tile {globalKTileIndex} has inconsistent ranges inside cluster tree '{node.ComponentId}'.");
                var link = links.Single(candidate =>
                    candidate.Role == TopologyPresetLinkRole.ActivationDistribution &&
                    string.Equals(candidate.SourceComponentId, node.ComponentId, StringComparison.Ordinal) &&
                    string.Equals(candidate.DestinationComponentId, item.Child.ComponentId, StringComparison.Ordinal));
                IReadOnlyDictionary<string, string>? metadata = null;
                IReadOnlyList<Phase8AStageRoute> downstream = [];
                if (item.Child.Role == TopologyPresetComponentRole.TreeRouter)
                {
                    metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Phase8ATensorSliceContract.TargetsMetadata] = Phase8ATensorSliceMetadata.Encode(
                            BuildTargets(item.Child, globalKTileIndex, kRange)),
                        [Phase8ATensorSliceContract.CoverageModeMetadata] = Phase8ATensorSliceContract.SelectReplicateCoverageMode
                    };
                }
                else if (item.Child.Role == TopologyPresetComponentRole.ProcessingElement)
                {
                    var assignment = item.Assignments.Single();
                    downstream = assignmentRoutes[assignment.AssignmentId];
                }
                else
                {
                    throw new InvalidOperationException("Activation hierarchy contains a non-tree, non-PE child.");
                }
                targets.Add(new Phase8ATensorSliceTarget(
                    item.Child.ComponentId,
                    0,
                    checked((int)kRange.Extent),
                    Phase8AMoTInrRuntimeIds.SlicePath(globalKTileIndex, node.ComponentId, item.Child.ComponentId),
                    [link.LinkId],
                    downstream,
                    metadata));
            }
            return targets.AsReadOnly();
        }

        foreach (var row in meshByRow)
        {
            var clusters = row.OrderBy(item => item.MeshCoordinate!.Column).ToArray();
            var clusterPosition = clusters.Select((cluster, index) => (cluster.ComponentId, index))
                .ToDictionary(item => item.ComponentId, item => item.index, StringComparer.Ordinal);
            IReadOnlyList<string> MeshRoute(TopologyManifestComponent source, TopologyManifestComponent destination)
            {
                var start = clusterPosition[source.ComponentId];
                var end = clusterPosition[destination.ComponentId];
                if (end < start) throw new InvalidOperationException("Activation row demand must route from left to right.");
                return Enumerable.Range(start, end - start).Select(index => links.Single(item =>
                        item.Role == TopologyPresetLinkRole.MeshTransport &&
                        string.Equals(item.SourceComponentId, clusters[index].ComponentId, StringComparison.Ordinal) &&
                        string.Equals(item.DestinationComponentId, clusters[index + 1].ComponentId, StringComparison.Ordinal)))
                    .Select(item => item.LinkId)
                    .ToArray();
            }

            var rowClusterIndices = clusters.Select(cluster => cluster.ClusterIndex!.Value).ToHashSet();
            var rowTileIndices = plan.DcLayout.Assignments
                .Where(assignment => rowClusterIndices.Contains(assignment.ClusterIndex))
                .Select(assignment => assignment.GlobalKTileIndex)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            foreach (var globalKTileIndex in rowTileIndices)
            {
                var tileAssignments = plan.DcLayout.Assignments
                    .Where(assignment => assignment.GlobalKTileIndex == globalKTileIndex && rowClusterIndices.Contains(assignment.ClusterIndex))
                    .ToArray();
                var kRange = tileAssignments.Select(assignment => assignment.KRange).Distinct().Single();
                if (kRange.Extent != Phase8AMoTInrRuntimeIds.FixedPacketElements)
                    throw new InvalidOperationException($"MoT-INR fixed packet K tile {globalKTileIndex} must contain exactly {Phase8AMoTInrRuntimeIds.FixedPacketElements} elements.");
                var demandClusters = clusters
                    .Where(cluster => tileAssignments.Any(assignment => assignment.ClusterIndex == cluster.ClusterIndex!.Value))
                    .ToArray();
                var rootMetadata = demandClusters.ToDictionary(
                    mesh => mesh.ClusterIndex!.Value,
                    mesh =>
                    {
                        var treeRoot = components[mesh.ChildComponentIds.Single()];
                        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Phase8ATensorSliceContract.TargetsMetadata] = Phase8ATensorSliceMetadata.Encode(
                                BuildTargets(treeRoot, globalKTileIndex, kRange)),
                            [Phase8ATensorSliceContract.CoverageModeMetadata] = Phase8ATensorSliceContract.SelectReplicateCoverageMode
                        };
                        return (TreeRoot: treeRoot.ComponentId, Metadata: (IReadOnlyDictionary<string, string>)metadata);
                    });

                (Phase8AMulticastBranchPlan Plan, IReadOnlyList<Phase8ABranchTargetPipeline> Pipelines) BuildBranch(int index)
                {
                    var current = demandClusters[index];
                    var currentCluster = current.ClusterIndex!.Value;
                    var local = rootMetadata[currentCluster];
                    var attachment = links.Single(item =>
                        item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                        string.Equals(item.SourceComponentId, current.ComponentId, StringComparison.Ordinal) &&
                        string.Equals(item.DestinationComponentId, local.TreeRoot, StringComparison.Ordinal));
                    var targets = new List<Phase8AMulticastBranchTarget>
                    {
                        new(local.TreeRoot, [attachment.LinkId],
                            Phase8AMoTInrRuntimeIds.BranchPath(currentCluster, globalKTileIndex, local.TreeRoot))
                    };
                    var pipelines = new List<Phase8ABranchTargetPipeline>
                    {
                        new(local.TreeRoot, [], local.Metadata)
                    };
                    var next = demandClusters[index + 1];
                    var meshRoute = MeshRoute(current, next);
                    if (index + 1 == demandClusters.Length - 1)
                    {
                        var nextRoot = rootMetadata[next.ClusterIndex!.Value];
                        var nextAttachment = links.Single(item =>
                            item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                            string.Equals(item.SourceComponentId, next.ComponentId, StringComparison.Ordinal) &&
                            string.Equals(item.DestinationComponentId, nextRoot.TreeRoot, StringComparison.Ordinal));
                        targets.Add(new(nextRoot.TreeRoot, meshRoute.Append(nextAttachment.LinkId),
                            Phase8AMoTInrRuntimeIds.BranchPath(currentCluster, globalKTileIndex, nextRoot.TreeRoot)));
                        pipelines.Add(new(nextRoot.TreeRoot, [], nextRoot.Metadata));
                    }
                    else
                    {
                        var nested = BuildBranch(index + 1);
                        targets.Add(new(next.ComponentId, meshRoute,
                            Phase8AMoTInrRuntimeIds.BranchPath(currentCluster, globalKTileIndex, next.ComponentId)));
                        pipelines.Add(new(next.ComponentId, [], null, nested.Plan, nested.Pipelines));
                    }
                    return (new Phase8AMulticastBranchPlan(
                        Phase8AMoTInrRuntimeIds.BranchFlow(currentCluster, globalKTileIndex),
                        $"mot-inr-row-branch:r{row.Key:D4}:k{globalKTileIndex:D4}:c{currentCluster:D4}",
                        targets), pipelines.AsReadOnly());
                }

                var boundaryCluster = clusters[0];
                var boundaryClusterIndex = boundaryCluster.ClusterIndex!.Value;
                var firstDemand = demandClusters[0];
                var initialLinks = new[] { Phase8AMatMulRuntimeIds.ActivationSourceLink(boundaryClusterIndex) }
                    .Concat(MeshRoute(boundaryCluster, firstDemand))
                    .ToList();
                if (demandClusters.Length == 1)
                {
                    var root = rootMetadata[firstDemand.ClusterIndex!.Value];
                    var attachment = links.Single(item =>
                        item.Role == TopologyPresetLinkRole.ActivationDistribution &&
                        string.Equals(item.SourceComponentId, firstDemand.ComponentId, StringComparison.Ordinal) &&
                        string.Equals(item.DestinationComponentId, root.TreeRoot, StringComparison.Ordinal));
                    initialLinks.Add(attachment.LinkId);
                    programs.Add(new Phase8AMoTInrRowProgram(
                        row.Key,
                        boundaryClusterIndex,
                        root.TreeRoot,
                        initialLinks.AsReadOnly(),
                        null,
                        [],
                        root.Metadata,
                        globalKTileIndex,
                        kRange));
                }
                else
                {
                    var branch = BuildBranch(0);
                    programs.Add(new Phase8AMoTInrRowProgram(
                        row.Key,
                        boundaryClusterIndex,
                        firstDemand.ComponentId,
                        initialLinks.AsReadOnly(),
                        branch.Plan,
                        branch.Pipelines,
                        new Dictionary<string, string>(),
                        globalKTileIndex,
                        kRange));
                }
            }
        }
        return programs
            .OrderBy(program => program.MeshRow)
            .ThenBy(program => program.GlobalKTileIndex)
            .ToArray();
    }
    private static IReadOnlyList<CommunicationFlow> BuildInitialFlows(
        Phase8AMatMulScenarioPlan plan,
        IReadOnlyList<Phase8AMoTInrRowProgram> rows) => rows.Select(row => UnicastFlow(
            Phase8AMoTInrRuntimeIds.RowFlow(row.MeshRow, row.GlobalKTileIndex),
            Phase8AMatMulRuntimeIds.ActivationSource,
            row.InitialDestinationComponentId,
            Phase8AMoTInrRuntimeIds.RowTile(row.MeshRow, row.GlobalKTileIndex),
            Phase8AMoTInrRuntimeIds.FixedPacketBits,
            row.InitialRouteLinkIds)).ToArray();

    private static IReadOnlyList<CollectivePlan> BuildCollectives(
        Phase8AMatMulScenarioPlan plan,
        Phase8AMeshReductionForest forest)
    {
        var result = plan.HierarchicalReduction.LocalGroups
            .SelectMany(local => local.Stages.Select(stage => new CollectivePlan(
                "scenario.collective." + stage.StageId,
                Phase8ACollectiveIntentKinds.Sum,
                stage.InputResultIds,
                stage.TargetReductionComponentId,
                stage.OutputResultId,
                "stable-k-offset-v1",
                PrecisionKind.FP32.ToString(),
                stage.StageId,
                StrictCollectiveErrors())))
            .ToList();
        result.AddRange(forest.Groups.SelectMany(group => group.Stages.Select(stage => new CollectivePlan(
            "scenario.collective." + stage.StageId,
            Phase8ACollectiveIntentKinds.Sum,
            stage.Inputs.Select(input => input.ResultId).ToArray(),
            Phase8AMoTInrGeneralRuntimeIds.MeshSumCapability(stage.TargetMeshRouterComponentId),
            stage.OutputResultId,
            "stable-k-offset-v1",
            PrecisionKind.FP32.ToString(),
            stage.GroupKey,
            StrictCollectiveErrors()))));
        if (plan.Request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1)
        {
            result.Add(new CollectivePlan(
                "scenario.collective.final-assembly",
                Phase8ACollectiveIntentKinds.Concat,
                plan.HierarchicalReduction.GlobalGroups.OrderBy(item => item.NRange.Offset).Select(item => item.OutputResultId).ToArray(),
                Phase8AMatMulRuntimeIds.Assembly,
                "Y",
                "stable-n-offset-v1",
                PrecisionKind.FP32.ToString(),
                "scenario:assembly:Y",
                StrictCollectiveErrors()));
        }
        return result.AsReadOnly();
    }

    private static Dictionary<string, string> LocalSumMetadata(Phase8ALocalReductionStage stage, string contributorId) =>
        SumMetadata(stage.StageId, stage.InputResultIds, contributorId, stage.NRange, "tree-node-sum");

    private static Dictionary<string, string> MeshSumMetadata(Phase8AMeshReductionStage stage, string contributorId) =>
        SumMetadata(
            stage.GroupKey,
            stage.Inputs.Select(input => input.ResultId),
            contributorId,
            stage.NRange,
            stage.StageId);

    private static Dictionary<string, string> SumMetadata(
        string groupKey,
        IEnumerable<string> expected,
        string contributorId,
        MappingIndexRange nRange,
        string stageId) => new(StringComparer.Ordinal)
    {
        [Phase8ACollectiveRuntimeMetadata.OperationKind] = Phase8AGroupedVectorSumContract.SumOperation,
        [Phase8ACollectiveRuntimeMetadata.GroupKey] = groupKey,
        [Phase8ACollectiveRuntimeMetadata.ExpectedContributors] = Phase8ACollectiveMetadataCodec.EncodeStringList(expected),
        [Phase8ACollectiveRuntimeMetadata.ContributorId] = contributorId,
        [Phase8ACollectiveRuntimeMetadata.OutputMOffset] = "0",
        [Phase8ACollectiveRuntimeMetadata.OutputMExtent] = "1",
        [Phase8ACollectiveRuntimeMetadata.OutputNOffset] = nRange.Offset.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.OutputNExtent] = nRange.Extent.ToString(CultureInfo.InvariantCulture),
        [Phase8ACollectiveRuntimeMetadata.DType] = PrecisionKind.FP32.ToString(),
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
        [Phase8ACollectiveRuntimeMetadata.DType] = PrecisionKind.FP32.ToString(),
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
            BitWidth = Phase8AMoTInrRuntimeIds.FixedPacketBitWidth,
            Bits = checked(values.Count * Phase8AMoTInrRuntimeIds.FixedPacketBitWidth),
            Precision = PrecisionKind.FP32,
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
        [PrecisionKind.FP32.ToString()],
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

    private static Phase8AMatMulScenarioIssue Issue(string code, string location, string message) =>
        new(code, location, message);
}
