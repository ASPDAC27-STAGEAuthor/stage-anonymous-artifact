using System.Globalization;
using System.Text.Json;
using static HardwareSim.Core.Phase8AMatMulRuntimeIds;

namespace HardwareSim.Core;

internal static class Phase8AMatMulResolvedRuntimeBuilder
{
    public static (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan) => Phase8AMatMulRuntimeStrategyRegistry.Build(plan);

    internal static (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) BuildLegacy(
        Phase8AMatMulScenarioPlan plan)
    {
        try
        {
            var registry = ComponentTypeRegistry.CreateDefault();
            var template = ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic();
            if (plan.Request.WeightsPreplaced)
                Phase8AStaticWeightDigitalVmmKernelFactory.Configure(registry, template);
            var library = new ComponentTemplateLibrary();
            library.AddOrReplace(template);
            var graph = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(plan.TopologyGraph));
            graph.ExtensionData.Remove(TopologyManifestJson.ExtensionPropertyName);
            graph.Parameters["scenario_overlay"] = "phase8a-executable-matmul-dc-v2";
            graph.Parameters["scenario_topology_authority_hash"] = plan.TopologyManifest.CanonicalHash;
            graph.Parameters["scenario_dc_layout_hash"] = plan.DcLayout.CanonicalHash;
            graph.Parameters["scenario_activation_tree_hash"] = plan.ActivationTree.CanonicalHash;
            graph.Parameters["scenario_reduction_plan_hash"] = plan.HierarchicalReduction.CanonicalHash;
            graph.Parameters["scenario_weight_row_division_size"] = plan.Request.WeightRowDivisionSize.ToString(CultureInfo.InvariantCulture);
            graph.Parameters["scenario_cluster_size"] = plan.Request.ClusterSize.ToString(CultureInfo.InvariantCulture);
            graph.Parameters["scenario_mesh_rows"] = plan.MeshRows.ToString(CultureInfo.InvariantCulture);
            graph.Parameters["scenario_mesh_columns"] = plan.MeshColumns.ToString(CultureInfo.InvariantCulture);
            graph.Parameters["scenario_activation_ingress_policy"] = plan.ActivationTree.IngressPolicyId;
            graph.Parameters["scenario_assembly_cluster_index"] = plan.Request.AssemblyClusterIndex.ToString(CultureInfo.InvariantCulture);
            graph.Placement = null;
            graph.Routing = null;

            PrepareProcessingElements(graph, plan, template);
            PrepareReductionTransport(graph, plan.TopologyManifest);
            AddBoundaryComponents(graph, registry, plan);
            AddOverlayComponents(graph, registry, plan);
            AddOverlayLinks(graph, plan);
            var programs = Phase8AMatMulRuntimeProgramCompiler.BuildActivationPrograms(plan);

            var compiled = new SimulationGraphCompiler().CompileHardware(
                graph,
                simulationConfig: new SimulationConfig { FlitSizeBits = 128 },
                componentRegistry: registry,
                componentTemplateLibrary: library);
            if (!compiled.IsSuccess || compiled.Graph is null)
                return Failure(compiled.Errors.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            var mapping = Phase8AMatMulRuntimeProgramCompiler.BuildMapping(plan, graph, programs);
            var imported = WorkloadMappingV2.FromJson(mapping.ToJson());
            if (!imported.CanCompileTopologyAware || imported.Mapping is null)
                return Failure(imported.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));
            mapping = imported.Mapping;
            var operands = Phase8AMatMulRuntimeProgramCompiler.BuildOperands(plan, programs);
            var executable = Phase8AExecutableOperandCompiler.Compile(
                compiled.Graph,
                mapping,
                operands,
                new WorkloadSchedule { WorkloadId = "phase8a-generated-matmul-dc-v2" });
            if (!executable.IsSuccess || executable.Executable is null)
                return Failure(executable.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));
            var kernels = registry.FreezeRuntimeKernels();
            if (!kernels.IsSuccess || kernels.Snapshot is null)
                return Failure(kernels.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            return (new Phase8AMatMulExecutableScenario(
                graph, compiled.Graph, kernels.Snapshot, mapping, executable.Executable,
                operands, executable.OperandPlanHash), []);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            return Failure([new Phase8AMatMulScenarioIssue("Phase8AScenarioGraphBuildFailed", "$", exception.Message)]);
        }
    }

    private static void PrepareProcessingElements(HardwareGraph graph, Phase8AMatMulScenarioPlan plan, ComponentTemplate template)
    {
        var peIds = plan.ActivationTree.Trees.SelectMany(tree => tree.Targets)
            .Select(target => target.PeComponentId).ToHashSet(StringComparer.Ordinal);
        foreach (var link in graph.Links)
        {
            if (peIds.Contains(link.Destination.ComponentId) && link.Destination.PortName == "activation-parent")
                link.Destination = new PortRef(link.Destination.ComponentId, "in_activation");
            if (peIds.Contains(link.Source.ComponentId) && link.Source.PortName == "partial-sum-parent")
                link.Source = new PortRef(link.Source.ComponentId, "out_result");
        }
        foreach (var peId in peIds)
        {
            var component = graph.FindComponent(peId) ?? throw new InvalidOperationException("Missing topology PE " + peId);
            component.Ports = template.ExternalPorts.Select(port => new HardwarePort
            {
                Name = port.Name,
                Direction = port.Direction,
                SignalType = port.SignalType,
                DataType = port.DataType,
                Precision = port.Precision,
                Protocol = port.Protocol,
                BandwidthBitsPerCycle = Math.Max(8192, port.BandwidthBitsPerCycle),
                Required = false,
                MultiConnect = string.Equals(port.Name, "in_activation", StringComparison.Ordinal)
            }).ToList();
            var parameterOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["array_rows"] = plan.Request.PeRows.ToString(CultureInfo.InvariantCulture),
                ["array_cols"] = plan.Request.PeColumns.ToString(CultureInfo.InvariantCulture),
                ["input_queue_depth"] = "8",
                ["output_queue_depth"] = "8",
                ["weight_write_bandwidth_bits_per_cycle"] = "8192"
            };
            if (plan.Request.WeightsPreplaced)
            {
                var assignment = plan.Assignments.Single(item => item.TargetComponentId == peId);
                var tile = plan.Lowering.OperationTiles.Single(item => item.OperationTileId == assignment.AssignmentId);
                var values = Enumerable.Range(0, checked((int)tile.KRange.Extent))
                    .SelectMany(row => plan.Weights
                        .Skip(checked((int)((tile.KRange.Offset + row) * plan.Request.N + tile.NRange.Offset)))
                        .Take(checked((int)tile.NRange.Extent)))
                    .ToArray();
                parameterOverrides[Phase8AStaticWeightDigitalVmmKernelFactory.InitialWeightValuesParameter] =
                    JsonSerializer.Serialize(values, HardwareGraphJson.Options);
                parameterOverrides[Phase8AStaticWeightDigitalVmmKernelFactory.InitialWeightIdParameter] =
                    "static-weight:" + assignment.AssignmentId;
            }
            component.TemplateRef = new ComponentTemplateInstanceRef
            {
                TemplateId = template.TemplateId,
                Version = template.Version,
                ParameterOverrides = parameterOverrides
            };
        }
    }

    private static void PrepareReductionTransport(HardwareGraph graph, TopologyManifest manifest)
    {
        foreach (var reductionId in manifest.Components
                     .Where(component => component.Role == TopologyPresetComponentRole.TreeReductionUnit)
                     .Select(component => component.ComponentId))
        {
            var component = graph.FindComponent(reductionId)
                ?? throw new InvalidOperationException("Missing topology reduction transport " + reductionId);
            component.Type = ComponentKind.Router;
            component.TypeId = "";
            component.Parameters["num_ports"] = component.Ports.Count.ToString(CultureInfo.InvariantCulture);
            component.Parameters["runtime_role"] = "partial-sum-transport";
            component.Parameters["collective_semantics"] = "registered-grouped-vector-collectors-only";
        }
    }

    private static void AddBoundaryComponents(HardwareGraph graph, ComponentTypeRegistry registry, Phase8AMatMulScenarioPlan plan)
    {
        graph.Components.Add(new HardwareComponent
        {
            Id = ActivationSource,
            Name = "Scenario Activation Source",
            Type = ComponentKind.WorkloadSource,
            Position = new GridPosition(-8, 0),
            Ports = [Port("out", PortDirection.Output, 8192, multi: true)],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["packet_count"] = plan.DcLayout.Summary.KTileCount.ToString(CultureInfo.InvariantCulture),
                ["queue_capacity"] = Math.Max(128, plan.DcLayout.Summary.KTileCount * 2).ToString(CultureInfo.InvariantCulture)
            }
        });
        graph.Components.Add(new HardwareComponent
        {
            Id = WeightSource,
            Name = "Scenario Weight Preload Source",
            Type = ComponentKind.WorkloadSource,
            Position = new GridPosition(-8, 4),
            Ports = plan.Assignments.Select(item => Port(WeightPort(item.AssignmentId), PortDirection.Output, 8192)).ToList(),
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["packet_count"] = plan.Assignments.Count.ToString(CultureInfo.InvariantCulture),
                ["queue_capacity"] = Math.Max(256, plan.Assignments.Count * 2).ToString(CultureInfo.InvariantCulture)
            }
        });
        graph.Components.Add(new HardwareComponent
        {
            Id = Sink,
            Name = "Scenario Output Sink",
            Type = ComponentKind.WorkloadSink,
            Position = new GridPosition(16, -5),
            Ports = [Port("in", PortDirection.Input, 8192)]
        });
        var assembly = registry.CreateComponent(Phase8ATensorAssemblyContract.TypeId, Assembly, new GridPosition(12, -5), "Final Offset Assembly");
        assembly.Parameters["input_queue_depth"] = Math.Max(16, plan.DcLayout.Summary.NShardCount * 2).ToString(CultureInfo.InvariantCulture);
        assembly.Parameters["output_queue_depth"] = "4";
        assembly.Parameters["missing_contributor_timeout_cycles"] = plan.Request.MaxCycles.ToString(CultureInfo.InvariantCulture);
        assembly.Parameters["max_tensor_elements"] = Math.Max(plan.Request.N, 1024).ToString(CultureInfo.InvariantCulture);
        graph.Components.Add(assembly);
    }

    private static void AddOverlayComponents(HardwareGraph graph, ComponentTypeRegistry registry, Phase8AMatMulScenarioPlan plan)
    {
        foreach (var componentId in plan.ActivationTree.Trees.SelectMany(tree => tree.BranchPoints)
                     .Select(branch => branch.ComponentId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            var anchor = graph.FindComponent(componentId) ?? throw new InvalidOperationException("Missing multicast branch anchor " + componentId);
            var multicast = registry.CreateComponent(Phase8ABranchMulticastContract.TypeId, BranchComponent(componentId), anchor.Position, "Activation Branch " + Short(componentId));
            multicast.Parameters["input_queue_depth"] = "64";
            multicast.Parameters["output_queue_depth"] = "64";
            var maximumFanout = plan.ActivationTree.Trees
                .SelectMany(tree => tree.BranchPoints)
                .Where(branch => branch.ComponentId == componentId)
                .Select(branch => branch.OutputPacketCount)
                .DefaultIfEmpty(2)
                .Max();
            multicast.Parameters["max_fanout"] = Math.Max(2, maximumFanout).ToString(CultureInfo.InvariantCulture);
            graph.Components.Add(multicast);
        }
        foreach (var local in plan.HierarchicalReduction.LocalGroups.Where(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum))
        {
            var anchor = graph.FindComponent(local.TargetComponentId) ?? throw new InvalidOperationException("Missing local reduction anchor " + local.TargetComponentId);
            graph.Components.Add(CreateSum(registry, LocalCollector(local.GroupId), anchor.Position, "Local Sum " + Short(local.GroupId), plan.Request.MaxCycles));
        }
        foreach (var global in plan.HierarchicalReduction.GlobalGroups.Where(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum))
        {
            var anchor = graph.FindComponent(global.CollectionMeshRouterComponentId) ?? throw new InvalidOperationException("Missing global reduction anchor " + global.CollectionMeshRouterComponentId);
            graph.Components.Add(CreateSum(registry, GlobalCollector(global.GroupId), anchor.Position, "Global Sum N" + global.NShardIndex, plan.Request.MaxCycles));
        }
    }

    private static HardwareComponent CreateSum(ComponentTypeRegistry registry, string id, GridPosition position, string name, int maxCycles)
    {
        var sum = registry.CreateComponent(Phase8AGroupedVectorSumContract.TypeId, id, position, name);
        sum.Parameters["input_queue_depth"] = "64";
        sum.Parameters["output_queue_depth"] = "16";
        sum.Parameters["missing_contributor_timeout_cycles"] = maxCycles.ToString(CultureInfo.InvariantCulture);
        return sum;
    }

    private static void AddOverlayLinks(HardwareGraph graph, Phase8AMatMulScenarioPlan plan)
    {
        foreach (var ingress in plan.ActivationTree.Trees
                     .GroupBy(tree => tree.SourceClusterIndex)
                     .OrderBy(group => group.Key))
        {
            var ingressMesh = ingress.Select(tree => tree.SourceComponentId).Distinct(StringComparer.Ordinal).Single();
            var ingressPort = "scenario-activation-source-in-c" + ingress.Key.ToString("D4", CultureInfo.InvariantCulture);
            AddPort(graph, ingressMesh, ingressPort, PortDirection.Input, 8192);
            graph.Links.Add(Link(ActivationSourceLink(ingress.Key), ActivationSource, "out", ingressMesh, ingressPort,
                TopologyPresetLinkRole.ActivationDistribution.ToString()));
        }
        foreach (var assignment in plan.Assignments)
            graph.Links.Add(Link(WeightLink(assignment.AssignmentId), WeightSource, WeightPort(assignment.AssignmentId), assignment.TargetComponentId, "in_weight"));

        var peIds = plan.Assignments.Select(item => item.TargetComponentId).ToHashSet(StringComparer.Ordinal);
        var branchEdges = plan.ActivationTree.Trees.SelectMany(tree => tree.BranchPoints.SelectMany(branch =>
                tree.Edges.Where(edge => edge.SourceComponentId == branch.ComponentId)
                    .Select(edge => (BranchComponentId: branch.ComponentId, Edge: edge))))
            .GroupBy(item => (item.BranchComponentId, item.Edge.LinkId))
            .Select(group => group.First())
            .OrderBy(item => item.BranchComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.Edge.LinkId, StringComparer.Ordinal)
            .ToArray();
        foreach (var componentId in branchEdges.Select(item => item.BranchComponentId).Distinct(StringComparer.Ordinal))
        {
            var suffix = Short(componentId);
            AddPort(graph, componentId, "scenario-mcast-in-" + suffix, PortDirection.Output, 8192);
            graph.Links.Add(Link(BranchInputLink(componentId), componentId, "scenario-mcast-in-" + suffix,
                BranchComponent(componentId), Phase8ABranchMulticastContract.InputPort,
                TopologyPresetLinkRole.ActivationDistribution.ToString()));
        }
        foreach (var item in branchEdges)
        {
            var destination = item.Edge.DestinationComponentId;
            var destinationPort = "scenario-mcast-edge-" + Short(item.Edge.LinkId);
            if (peIds.Contains(destination))
            {
                destinationPort = "in_activation";
            }
            else
            {
                AddPort(graph, destination, destinationPort, PortDirection.Input, 8192);
            }
            graph.Links.Add(Link(BranchOutputLink(item.BranchComponentId, item.Edge.LinkId),
                BranchComponent(item.BranchComponentId), Phase8ABranchMulticastContract.OutputPort,
                destination, destinationPort, TopologyPresetLinkRole.ActivationDistribution.ToString()));
        }        foreach (var local in plan.HierarchicalReduction.LocalGroups.Where(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum))
        {
            var suffix = Short(local.GroupId);
            AddPort(graph, local.TargetComponentId, "scenario-local-in-" + suffix, PortDirection.Output, 8192);
            graph.Links.Add(Link(LocalInputLink(local.GroupId), local.TargetComponentId, "scenario-local-in-" + suffix,
                LocalCollector(local.GroupId), Phase8AGroupedVectorSumContract.InputPort,
                TopologyPresetLinkRole.PartialSumReturn.ToString()));

            var global = plan.HierarchicalReduction.GlobalGroups.Single(group =>
                group.Contributors.Any(item => item.LocalGroupId == local.GroupId));
            IReadOnlyList<string> continuation;
            if (global.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum)
            {
                var contributor = global.Contributors.Single(item => item.LocalGroupId == local.GroupId);
                continuation = contributor.ReturnRouteLinkIds.Concat(contributor.MeshRouteLinkIds).ToArray();
            }
            else
            {
                var shard = plan.HierarchicalReduction.FinalAssembly.Shards.Single(item => item.SourceResultId == global.OutputResultId);
                continuation = shard.ReturnRouteLinkIds.Concat(shard.MeshRouteLinkIds).ToArray();
            }
            if (continuation.Count == 0) throw new InvalidOperationException("A local collector has no acyclic authority egress: " + local.GroupId);
            var first = graph.Links.Single(link => link.Id == continuation[0]);
            var destinationPort = "scenario-local-out-" + suffix;
            AddPort(graph, first.Destination.ComponentId, destinationPort, PortDirection.Input, 8192);
            graph.Links.Add(Link(LocalOutputLink(local.GroupId), LocalCollector(local.GroupId), Phase8AGroupedVectorSumContract.OutputPort,
                first.Destination.ComponentId, destinationPort, TopologyPresetLinkRole.PartialSumReturn.ToString()));
        }
        foreach (var global in plan.HierarchicalReduction.GlobalGroups.Where(group => group.Mode == Phase8AHierarchicalReductionModes.GroupedVectorSum))
        {
            var suffix = Short(global.GroupId);
            AddPort(graph, global.CollectionMeshRouterComponentId, "scenario-global-in-" + suffix, PortDirection.Output, 8192);
            graph.Links.Add(Link(GlobalInputLink(global.GroupId), global.CollectionMeshRouterComponentId, "scenario-global-in-" + suffix,
                GlobalCollector(global.GroupId), Phase8AGroupedVectorSumContract.InputPort,
                TopologyPresetLinkRole.PartialSumReturn.ToString()));

            var shard = plan.HierarchicalReduction.FinalAssembly.Shards.Single(item => item.SourceResultId == global.OutputResultId);
            if (shard.MeshRouteLinkIds.Count == 0)
            {
                graph.Links.Add(Link(GlobalOutputLink(global.GroupId), GlobalCollector(global.GroupId), Phase8AGroupedVectorSumContract.OutputPort,
                    Assembly, Phase8ATensorAssemblyContract.InputPort,
                    TopologyPresetLinkRole.PartialSumReturn.ToString()));
            }
            else
            {
                var first = graph.Links.Single(link => link.Id == shard.MeshRouteLinkIds[0]);
                var destinationPort = "scenario-global-out-" + suffix;
                AddPort(graph, first.Destination.ComponentId, destinationPort, PortDirection.Input, 8192);
                graph.Links.Add(Link(GlobalOutputLink(global.GroupId), GlobalCollector(global.GroupId), Phase8AGroupedVectorSumContract.OutputPort,
                    first.Destination.ComponentId, destinationPort,
                    TopologyPresetLinkRole.PartialSumReturn.ToString()));
            }
        }        var assemblyMesh = plan.HierarchicalReduction.FinalAssembly.AssemblyMeshRouterComponentId;
        AddPort(graph, assemblyMesh, "scenario-assembly-in", PortDirection.Output, 8192);
        graph.Links.Add(Link(AssemblyInputLink, assemblyMesh, "scenario-assembly-in", Assembly, Phase8ATensorAssemblyContract.InputPort,
            TopologyPresetLinkRole.PartialSumReturn.ToString()));
        graph.Links.Add(Link(AssemblyOutputLink, Assembly, Phase8ATensorAssemblyContract.OutputPort, Sink, "in"));
    }

    private static HardwarePort Port(string name, PortDirection direction, int bandwidth, bool multi = false) => new()
    {
        Name = name,
        Direction = direction,
        SignalType = SignalType.Digital,
        DataType = HardwareDataType.Tensor,
        Precision = PrecisionKind.FP8_E4M3,
        Protocol = PortProtocol.Packet,
        BandwidthBitsPerCycle = bandwidth,
        Required = false,
        MultiConnect = multi
    };

    private static void AddPort(HardwareGraph graph, string componentId, string name, PortDirection direction, int bandwidth)
    {
        var component = graph.FindComponent(componentId) ?? throw new InvalidOperationException("Missing component " + componentId);
        if (component.Ports.Any(port => string.Equals(port.Name, name, StringComparison.Ordinal)))
            throw new InvalidOperationException("Duplicate runtime overlay port " + componentId + "." + name);
        component.Ports.Add(Port(name, direction, bandwidth));
    }

    private static HardwareLink Link(
        string id,
        string source,
        string sourcePort,
        string destination,
        string destinationPort,
        string topologyRole = "")
    {
        var link = new HardwareLink
        {
            Id = id,
            Source = new PortRef(source, sourcePort),
            Destination = new PortRef(destination, destinationPort),
            BandwidthBitsPerCycle = 8192,
            LatencyCycles = 1
        };
        if (!string.IsNullOrWhiteSpace(topologyRole))
        {
            link.Parameters["topology_role"] = topologyRole;
            link.Parameters["physical_route_id"] = "runtime-overlay:" + id;
        }
        return link;
    }

    private static (Phase8AMatMulExecutableScenario? Plan, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Failure(
        IEnumerable<Phase8AMatMulScenarioIssue> issues) => (null, issues.ToArray());
}
