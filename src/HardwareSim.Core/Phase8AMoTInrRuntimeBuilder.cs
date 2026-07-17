using System.Globalization;
using System.Text.Json;
using static HardwareSim.Core.Phase8AMatMulRuntimeIds;

namespace HardwareSim.Core;

internal static class Phase8AMoTInrRuntimeBuilder
{
    public static (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan)
    {
        try
        {
            var registry = ComponentTypeRegistry.CreateDefault();
            var template = ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic();
            ConfigureFixedFp32Template(template);
            if (plan.Request.WeightsPreplaced)
                Phase8AStaticWeightDigitalVmmKernelFactory.Configure(registry, template);
            var library = new ComponentTemplateLibrary();
            library.AddOrReplace(template);

            var graph = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(plan.TopologyGraph));
            graph.ExtensionData.Remove(TopologyManifestJson.ExtensionPropertyName);
            ConfigureGraph(graph, plan);
            PrepareProcessingElements(graph, plan, template);

            var (program, programIssues) = Phase8AMoTInrGeneralRuntimeCompiler.Compile(plan, graph);
            if (program is null) return Failure(programIssues);
            var forest = program.MeshReductionForest
                ?? throw new InvalidOperationException("The generalized MoT-INR compiler did not resolve a Mesh reduction forest.");
            graph.Parameters["scenario_mesh_reduction_forest_hash"] = forest.CanonicalHash;
            graph.Parameters["scenario_mesh_reduction_egress_roots"] = string.Join(",", forest.EgressMeshRouterComponentIds);

            PrepareTopologyRuntimeComponents(graph, registry, plan);
            AddBoundaryComponentsAndLinks(graph, registry, plan, forest, program);
            ClassifyMetricScopes(graph, plan);

            var compiled = new SimulationGraphCompiler().CompileHardware(
                graph,
                simulationConfig: new SimulationConfig { FlitSizeBits = 128 },
                componentRegistry: registry,
                componentTemplateLibrary: library);
            if (!compiled.IsSuccess || compiled.Graph is null)
                return Failure(compiled.Errors.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            var mapping = Phase8AMoTInrGeneralRuntimeCompiler.BuildMapping(plan, graph, program);
            var imported = WorkloadMappingV2.FromJson(mapping.ToJson());
            if (!imported.CanCompileTopologyAware || imported.Mapping is null)
                return Failure(imported.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));
            mapping = imported.Mapping;

            var operands = Phase8AMoTInrGeneralRuntimeCompiler.BuildOperands(plan, program);
            var executable = Phase8AExecutableOperandCompiler.Compile(
                compiled.Graph,
                mapping,
                operands,
                new WorkloadSchedule { WorkloadId = "phase8a-mot-inr-matmul-v1" });
            if (!executable.IsSuccess || executable.Executable is null)
                return Failure(executable.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            var kernels = registry.FreezeRuntimeKernels();
            if (!kernels.IsSuccess || kernels.Snapshot is null)
                return Failure(kernels.Issues.Select(issue => new Phase8AMatMulScenarioIssue(issue.Code, issue.Location, issue.Message)));

            return (new Phase8AMatMulExecutableScenario(
                graph,
                compiled.Graph,
                kernels.Snapshot,
                mapping,
                executable.Executable,
                operands,
                executable.OperandPlanHash), []);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            return Failure([new Phase8AMatMulScenarioIssue("Phase8AMoTInrGraphBuildFailed", "$", exception.Message)]);
        }
    }

    private static void ConfigureFixedFp32Template(ComponentTemplate template)
    {
        template.TemplateId = "PE_Array_32x32_FP32_SRAM_Synthetic";
        template.DisplayName = "PE Array 32x32 FP32 SRAM Synthetic";
        template.CompiledProfile = null;
        template.Provenance.CompileHash = "";
        foreach (var port in template.ExternalPorts.Where(port => port.DataType == HardwareDataType.Tensor))
            port.Precision = PrecisionKind.FP32;
        foreach (var port in template.InternalBlocks.SelectMany(block => block.Ports)
                     .Where(port => port.DataType == HardwareDataType.Tensor))
        {
            port.Precision = PrecisionKind.FP32;
            port.WidthBits = Math.Max(port.WidthBits, Phase8AMoTInrRuntimeIds.FixedPacketBits);
        }
        foreach (var connection in template.InternalConnections
                     .Where(connection => string.Equals(connection.PayloadType, "tensor", StringComparison.OrdinalIgnoreCase)))
        {
            connection.Precision = PrecisionKind.FP32;
            connection.BandwidthBitsPerCycle = Math.Max(connection.BandwidthBitsPerCycle, Phase8AMoTInrRuntimeIds.FixedPacketBits);
        }
        foreach (var operand in template.OperationContract.InputOperands
                     .Concat(template.OperationContract.StoredOperands)
                     .Concat(template.OperationContract.OutputOperands))
            operand.DType = "fp32";
        template.OperationContract.MultiplyDType = "fp32";
        template.OperationContract.AccumulateDType = "fp32";
        template.OperationContract.OutputDType = "fp32";
        foreach (var storage in template.StorageLayouts.Where(storage => storage.LogicalName == "weight"))
        {
            var requiredBits = Phase8AMoTInrRuntimeIds.FixedPacketElements *
                Phase8AMoTInrRuntimeIds.FixedPacketElements *
                Phase8AMoTInrRuntimeIds.FixedPacketBitWidth;
            storage.Rows = checked((int)Math.Ceiling(requiredBits / (double)(storage.Banks * storage.Columns * storage.CellBits)));
            storage.Encoding = "fp32-sram-synthetic";
        }
    }
    private static void ConfigureGraph(HardwareGraph graph, Phase8AMatMulScenarioPlan plan)
    {
        graph.Parameters["scenario_overlay"] = "none;topology-native-mot-inr-v1";
        graph.Parameters["scenario_topology_execution_strategy_id"] = plan.Request.TopologyExecutionStrategyId;
        graph.Parameters["scenario_output_landing_mode"] = plan.Request.OutputLandingMode;
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
    }

    private static void PrepareProcessingElements(
        HardwareGraph graph,
        Phase8AMatMulScenarioPlan plan,
        ComponentTemplate template)
    {
        var peIds = plan.Assignments.Select(assignment => assignment.TargetComponentId).ToHashSet(StringComparer.Ordinal);
        foreach (var peId in peIds)
        {
            var component = graph.FindComponent(peId) ?? throw new InvalidOperationException("Missing topology PE " + peId);
            component.Ports = template.ExternalPorts.Select(port => new HardwarePort
            {
                Name = port.Name,
                Direction = port.Direction,
                SignalType = port.SignalType,
                DataType = port.DataType,
                Precision = port.DataType == HardwareDataType.Tensor ? PrecisionKind.FP32 : port.Precision,
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
                ["weight_write_bandwidth_bits_per_cycle"] = "8192",
                ["input_dtype"] = "fp32",
                ["weight_dtype"] = "fp32",
                ["accumulate_dtype"] = "fp32",
                ["output_dtype"] = "fp32"
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

    private static void PrepareTopologyRuntimeComponents(
        HardwareGraph graph,
        ComponentTypeRegistry registry,
        Phase8AMatMulScenarioPlan plan)
    {
        var manifestById = plan.TopologyManifest.Components.ToDictionary(item => item.ComponentId, StringComparer.Ordinal);
        var activeReductionIds = plan.HierarchicalReduction.LocalGroups.SelectMany(group => group.Stages)
            .Select(stage => stage.TargetReductionComponentId).ToHashSet(StringComparer.Ordinal);
        foreach (var manifest in plan.TopologyManifest.Components.OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            if (manifest.Role == TopologyPresetComponentRole.TreeRouter)
            {
                var component = ReplaceWithPlugin(graph, registry, manifest.ComponentId, Phase8ATensorSliceContract.TypeId);
                component.Parameters["input_queue_depth"] = "64";
                component.Parameters["output_queue_depth"] = "64";
                component.Parameters["max_targets"] = Math.Max(2, plan.Request.ClusterSize).ToString(CultureInfo.InvariantCulture);
            }
            else if (manifest.Role == TopologyPresetComponentRole.TreeReductionUnit && activeReductionIds.Contains(manifest.ComponentId))
            {
                var component = ReplaceWithPlugin(graph, registry, manifest.ComponentId, Phase8AGroupedVectorSumContract.TypeId);
                component.Parameters["input_queue_depth"] = "64";
                component.Parameters["output_queue_depth"] = "16";
                component.Parameters["missing_contributor_timeout_cycles"] = plan.Request.MaxCycles.ToString(CultureInfo.InvariantCulture);
                component.Parameters["max_vector_elements"] = Math.Max(plan.Request.N, 1024).ToString(CultureInfo.InvariantCulture);
            }
            else if (manifest.Role == TopologyPresetComponentRole.TreeReductionUnit)
            {
                var component = graph.FindComponent(manifest.ComponentId)
                    ?? throw new InvalidOperationException("Missing topology reduction node " + manifest.ComponentId);
                component.Type = ComponentKind.Router;
                component.TypeId = "";
                component.Parameters["num_ports"] = component.Ports.Count.ToString(CultureInfo.InvariantCulture);
                component.Parameters["runtime_role"] = "typed-partial-forwarder";
                component.Parameters["buffer_depth"] = "512";
                component.Parameters["queue_capacity"] = "512";
                component.Parameters["vc_depth_flits"] = "512";
            }
            else if (manifest.Role == TopologyPresetComponentRole.MeshRouter)
            {
                var component = graph.FindComponent(manifest.ComponentId)
                    ?? throw new InvalidOperationException("Missing topology mesh router " + manifest.ComponentId);
                component.Parameters[Phase8ATopologyExecutionStrategies.RouterBranchCapabilityParameter] =
                    Phase8ATopologyExecutionStrategies.RouterBranchCapabilityValue;
                component.Parameters["buffer_depth"] = "512";
                component.Parameters["queue_capacity"] = "512";
                component.Parameters["vc_depth_flits"] = "512";
            }
        }

        foreach (var link in graph.Links)
        {
            var sourceRole = manifestById[link.Source.ComponentId].Role;
            var destinationRole = manifestById[link.Destination.ComponentId].Role;
            if (sourceRole == TopologyPresetComponentRole.TreeRouter)
                link.Source = new PortRef(link.Source.ComponentId, Phase8ATensorSliceContract.OutputPort);
            else if (sourceRole == TopologyPresetComponentRole.TreeReductionUnit &&
                     graph.FindComponent(link.Source.ComponentId)?.TypeId == Phase8AGroupedVectorSumContract.TypeId)
                link.Source = new PortRef(link.Source.ComponentId, Phase8AGroupedVectorSumContract.OutputPort);
            else if (sourceRole == TopologyPresetComponentRole.ProcessingElement &&
                     link.Parameters.GetValueOrDefault("topology_role", "") == TopologyPresetLinkRole.PartialSumReturn.ToString())
                link.Source = new PortRef(link.Source.ComponentId, "out_result");

            if (destinationRole == TopologyPresetComponentRole.TreeRouter)
                link.Destination = new PortRef(link.Destination.ComponentId, Phase8ATensorSliceContract.InputPort);
            else if (destinationRole == TopologyPresetComponentRole.TreeReductionUnit &&
                     graph.FindComponent(link.Destination.ComponentId)?.TypeId == Phase8AGroupedVectorSumContract.TypeId)
                link.Destination = new PortRef(link.Destination.ComponentId, Phase8AGroupedVectorSumContract.InputPort);
            else if (destinationRole == TopologyPresetComponentRole.ProcessingElement &&
                     link.Parameters.GetValueOrDefault("topology_role", "") == TopologyPresetLinkRole.ActivationDistribution.ToString())
                link.Destination = new PortRef(link.Destination.ComponentId, "in_activation");
        }
    }

    private static HardwareComponent ReplaceWithPlugin(
        HardwareGraph graph,
        ComponentTypeRegistry registry,
        string componentId,
        string typeId)
    {
        var index = graph.Components.FindIndex(component => string.Equals(component.Id, componentId, StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException("Missing topology component " + componentId);
        var original = graph.Components[index];
        var replacement = registry.CreateComponent(typeId, original.Id, original.Position, original.Name);
        foreach (var pair in original.Parameters) replacement.Parameters[pair.Key] = pair.Value;
        foreach (var pair in original.VisualStyle.Where(pair => !replacement.VisualStyle.ContainsKey(pair.Key)))
            replacement.VisualStyle[pair.Key] = pair.Value;
        replacement.InternalState = new Dictionary<string, string>(original.InternalState, StringComparer.OrdinalIgnoreCase);
        replacement.ExtensionData = new Dictionary<string, JsonElement>(original.ExtensionData, StringComparer.Ordinal);
        graph.Components[index] = replacement;
        return replacement;
    }

    private static void AddBoundaryComponentsAndLinks(
        HardwareGraph graph,
        ComponentTypeRegistry registry,
        Phase8AMatMulScenarioPlan plan,
        Phase8AMeshReductionForest forest,
        Phase8AMoTInrCompiledProgram program)
    {
        graph.Components.Add(new HardwareComponent
        {
            Id = ActivationSource,
            Name = "Scenario Row Activation Source",
            Type = ComponentKind.WorkloadSource,
            Position = new GridPosition(-8, 0),
            Ports = [Port("out", PortDirection.Output, 8192, multi: true)],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["packet_count"] = program.Rows.Count.ToString(CultureInfo.InvariantCulture),
                ["queue_capacity"] = Math.Max(16, program.Rows.Count * 2).ToString(CultureInfo.InvariantCulture)
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
                ["queue_capacity"] = Math.Max(64, plan.Assignments.Count * 2).ToString(CultureInfo.InvariantCulture)
            }
        });

        foreach (var row in plan.TopologyManifest.Components
                     .Where(item => item.Role == TopologyPresetComponentRole.MeshRouter)
                     .GroupBy(item => item.MeshCoordinate!.Row)
                     .OrderBy(group => group.Key))
        {
            var first = row.OrderBy(item => item.MeshCoordinate!.Column).First();
            var cluster = first.ClusterIndex!.Value;
            var inputPort = $"scenario-row-source-in-c{cluster:D4}";
            AddPort(graph, first.ComponentId, inputPort, PortDirection.Input, 8192);
            graph.Links.Add(Link(
                ActivationSourceLink(cluster),
                ActivationSource,
                "out",
                first.ComponentId,
                inputPort,
                TopologyPresetLinkRole.ActivationDistribution.ToString(),
                Phase8AMoTInrRuntimeIds.PhysicalMetricScope));
        }

        foreach (var assignment in plan.Assignments)
        {
            graph.Links.Add(Link(
                WeightLink(assignment.AssignmentId),
                WeightSource,
                WeightPort(assignment.AssignmentId),
                assignment.TargetComponentId,
                "in_weight",
                "WeightPreload",
                Phase8AMoTInrRuntimeIds.PhysicalMetricScope));
        }

        foreach (var stagesAtMesh in forest.Groups
                     .SelectMany(group => group.Stages)
                     .GroupBy(stage => stage.TargetMeshRouterComponentId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var mesh = graph.FindComponent(stagesAtMesh.Key)
                ?? throw new InvalidOperationException("Missing Mesh reduction router " + stagesAtMesh.Key);
            var capabilityId = Phase8AMoTInrGeneralRuntimeIds.MeshSumCapability(mesh.Id);
            var capability = registry.CreateComponent(
                Phase8AGroupedVectorSumContract.TypeId,
                capabilityId,
                mesh.Position,
                $"{mesh.Name} Sum Capability");
            capability.Parameters["input_queue_depth"] = Math.Max(32, stagesAtMesh.Count() * 8).ToString(CultureInfo.InvariantCulture);
            capability.Parameters["output_queue_depth"] = "16";
            capability.Parameters["missing_contributor_timeout_cycles"] = plan.Request.MaxCycles.ToString(CultureInfo.InvariantCulture);
            capability.Parameters["max_vector_elements"] = Math.Max(plan.Request.N, 1024).ToString(CultureInfo.InvariantCulture);
            graph.Components.Add(capability);

            var inputPort = $"scenario-mesh-sum-out-{Phase8AMatMulRuntimeIds.Short(mesh.Id)}";
            var outputPort = $"scenario-mesh-sum-in-{Phase8AMatMulRuntimeIds.Short(mesh.Id)}";
            AddPort(graph, mesh.Id, inputPort, PortDirection.Output, 8192);
            AddPort(graph, mesh.Id, outputPort, PortDirection.Input, 8192);
            graph.Links.Add(Link(
                Phase8AMoTInrGeneralRuntimeIds.MeshSumInputLink(mesh.Id),
                mesh.Id,
                inputPort,
                capabilityId,
                Phase8AGroupedVectorSumContract.InputPort,
                "MeshRouterSumCapabilityInput",
                Phase8AMoTInrRuntimeIds.InternalMetricScope));
            var reinjection = Link(
                Phase8AMoTInrGeneralRuntimeIds.MeshSumOutputLink(mesh.Id),
                capabilityId,
                Phase8AGroupedVectorSumContract.OutputPort,
                mesh.Id,
                outputPort,
                "MeshRouterSumCapabilityReinjection",
                Phase8AMoTInrRuntimeIds.InternalMetricScope);
            reinjection.Parameters[Phase8AMoTInrRuntimeIds.DependencyScopeParameter] =
                Phase8AMoTInrRuntimeIds.StatefulReinjectionDependencyScope;
            graph.Links.Add(reinjection);
        }
        if (plan.Request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1)
        {
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

            var assemblyMesh = plan.HierarchicalReduction.FinalAssembly.AssemblyMeshRouterComponentId;
            AddPort(graph, assemblyMesh, "scenario-assembly-in", PortDirection.Output, 8192);
            graph.Links.Add(Link(
                AssemblyInputLink,
                assemblyMesh,
                "scenario-assembly-in",
                Assembly,
                Phase8ATensorAssemblyContract.InputPort,
                TopologyPresetLinkRole.PartialSumReturn.ToString(),
                Phase8AMoTInrRuntimeIds.InternalMetricScope));
            graph.Links.Add(Link(
                AssemblyOutputLink,
                Assembly,
                Phase8ATensorAssemblyContract.OutputPort,
                Sink,
                "in",
                "OutputLanding",
                Phase8AMoTInrRuntimeIds.PhysicalMetricScope));
        }
        else
        {
            foreach (var global in plan.HierarchicalReduction.GlobalGroups.OrderBy(item => item.NShardIndex))
            {
                var sinkId = Phase8AMoTInrGeneralRuntimeIds.FinalLocalSink(global.NShardIndex);
                graph.Components.Add(new HardwareComponent
                {
                    Id = sinkId,
                    Name = $"N Shard {global.NShardIndex} Local Output Sink",
                    Type = ComponentKind.WorkloadSink,
                    Position = new GridPosition(16, global.NShardIndex * 2),
                    Ports = [Port("in", PortDirection.Input, 8192)]
                });
                var forestGroup = forest.Groups.Single(item => item.GlobalGroupId == global.GroupId);
                var mesh = graph.FindComponent(forestGroup.RootMeshRouterComponentId)
                    ?? throw new InvalidOperationException("Missing Mesh reduction egress router " + forestGroup.RootMeshRouterComponentId);
                var outputPort = $"scenario-local-output-n{global.NShardIndex:D4}";
                AddPort(graph, mesh.Id, outputPort, PortDirection.Output, 8192);
                graph.Links.Add(Link(
                    Phase8AMoTInrGeneralRuntimeIds.FinalLocalSinkLink(global.NShardIndex),
                    mesh.Id,
                    outputPort,
                    sinkId,
                    "in",
                    "TopologyEgressOutput",
                    Phase8AMoTInrRuntimeIds.InternalMetricScope));
            }
        }
    }

    private static void ClassifyMetricScopes(HardwareGraph graph, Phase8AMatMulScenarioPlan plan)
    {
        var manifestLinks = plan.TopologyManifest.Links.ToDictionary(item => item.LinkId, StringComparer.Ordinal);
        foreach (var link in graph.Links)
        {
            if (!manifestLinks.TryGetValue(link.Id, out var manifest)) continue;
            link.Parameters[Phase8AMoTInrRuntimeIds.MetricScopeParameter] =
                manifest.Role == TopologyPresetLinkRole.ActivationDistribution &&
                manifest.Scope == TopologyPresetLinkScope.Attachment
                    ? Phase8AMoTInrRuntimeIds.InternalMetricScope
                    : Phase8AMoTInrRuntimeIds.PhysicalMetricScope;
        }
    }

    private static HardwarePort Port(string name, PortDirection direction, int bandwidth, bool multi = false) => new()
    {
        Name = name,
        Direction = direction,
        SignalType = SignalType.Digital,
        DataType = HardwareDataType.Tensor,
        Precision = PrecisionKind.FP32,
        Protocol = PortProtocol.Packet,
        BandwidthBitsPerCycle = bandwidth,
        Required = false,
        MultiConnect = multi
    };

    private static void AddPort(HardwareGraph graph, string componentId, string name, PortDirection direction, int bandwidth)
    {
        var component = graph.FindComponent(componentId) ?? throw new InvalidOperationException("Missing component " + componentId);
        if (component.Ports.Any(port => string.Equals(port.Name, name, StringComparison.Ordinal)))
            throw new InvalidOperationException("Duplicate runtime port " + componentId + "." + name);
        component.Ports.Add(Port(name, direction, bandwidth));
    }

    private static HardwareLink Link(
        string id,
        string source,
        string sourcePort,
        string destination,
        string destinationPort,
        string topologyRole,
        string metricScope)
    {
        var link = new HardwareLink
        {
            Id = id,
            Source = new PortRef(source, sourcePort),
            Destination = new PortRef(destination, destinationPort),
            BandwidthBitsPerCycle = 8192,
            LatencyCycles = 1,
            PhysicalLength = 1
        };
        link.Parameters["topology_role"] = topologyRole;
        link.Parameters["physical_route_id"] = "mot-inr-runtime:" + id;
        link.Parameters[Phase8AMoTInrRuntimeIds.MetricScopeParameter] = metricScope;
        return link;
    }

    private static (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Failure(
        IEnumerable<Phase8AMatMulScenarioIssue> issues) => (null, issues.ToArray());
}
