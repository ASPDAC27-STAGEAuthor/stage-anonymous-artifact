namespace HardwareSim.Core;

/// <summary>Generates, maps, executes, and verifies one deterministic Phase 8A MatMul scenario.</summary>
public static class Phase8AMatMulScenarioGenerator
{
    /// <summary>Builds the complete scenario and requires exact full-cycle runtime equality with the tiled digital reference.</summary>
    public static Phase8AMatMulScenarioGenerationResult GenerateAndRun(Phase8AMatMulScenarioRequest request) =>
        GenerateAndRunCore(request, SimulationCycleTraceMode.Full, request.WeightsPreplaced
            ? Phase8AMatMulExecutionModes.FullCycleWeightsPreplaced
            : Phase8AMatMulExecutionModes.FullCycle);

    /// <summary>Builds the complete scenario through the exact cycle kernel while retaining metrics instead of per-cycle trace records.</summary>
    public static Phase8AMatMulScenarioGenerationResult GenerateAndRunMetricsOnly(Phase8AMatMulScenarioRequest request) =>
        GenerateAndRunCore(request, SimulationCycleTraceMode.MetricsOnly, Phase8AMatMulExecutionModes.MetricsOnlyCycle);

    private static Phase8AMatMulScenarioGenerationResult GenerateAndRunCore(
        Phase8AMatMulScenarioRequest request,
        SimulationCycleTraceMode traceMode,
        string executionMode)
    {
        var planned = Phase8AMatMulScenarioPlanner.Plan(request);
        if (planned.Plan is null) return new Phase8AMatMulScenarioGenerationResult(null, planned.Issues);
        var built = Phase8AMatMulScenarioGraphBuilder.Build(planned.Plan);
        if (built.Scenario is null) return new Phase8AMatMulScenarioGenerationResult(null, built.Issues);

        var simulation = new CycleSimulationEngine(built.Scenario.Kernels).Run(
            built.Scenario.Executable,
            new SimulationOptions
            {
                MaxCycles = request.MaxCycles,
                DeterministicSeed = request.Seed,
                CycleTraceMode = traceMode
            });
        if (!simulation.Completed || simulation.Issues.Count > 0)
        {
            var issues = simulation.Issues.Select(issue => new Phase8AMatMulScenarioIssue(
                issue.Code, "$.simulation", issue.Message)).ToList();
            if (!simulation.Completed)
                issues.Add(new("Phase8AScenarioSimulationIncomplete", "$.simulation", simulation.CompletionReason));
            return new Phase8AMatMulScenarioGenerationResult(null, issues);
        }

        var expected = EvaluateTiled(planned.Plan);
        var actualResult = AssembleDeliveredOutput(simulation, request);
        if (actualResult.Output is null)
            return new Phase8AMatMulScenarioGenerationResult(null, actualResult.Issues);
        if (!expected.SequenceEqual(actualResult.Output))
        {
            var mismatch = expected.Zip(actualResult.Output, (left, right) => (left, right))
                .Select((pair, index) => (pair.left, pair.right, index))
                .First(item => item.left != item.right);
            return new Phase8AMatMulScenarioGenerationResult(null,
            [
                new Phase8AMatMulScenarioIssue(
                    "Phase8AScenarioNumericMismatch",
                    "$.actualOutput[" + mismatch.index + "]",
                    $"Expected {mismatch.left:R} but runtime produced {mismatch.right:R}.")
            ]);
        }

        var evidence = BuildDataflowEvidence(planned.Plan, built.Scenario, simulation, executionMode);
        var bundle = new Phase8AMatMulScenarioBundle(
            request,
            planned.Plan.MeshRows,
            planned.Plan.MeshColumns,
            planned.Plan.TopologyGraph,
            built.Scenario.HardwareGraph,
            built.Scenario.Mapping,
            planned.Plan.DcLayout,
            planned.Plan.ActivationTree,
            planned.Plan.HierarchicalReduction,
            planned.Plan.WeightPlacement,
            planned.Plan.Input,
            planned.Plan.Weights,
            expected,
            actualResult.Output,
            simulation,
            evidence,
            planned.Plan.MappingAuthorityHash,
            planned.Plan.Lowering.CanonicalHash,
            built.Scenario.OperandPlanHash);
        return new Phase8AMatMulScenarioGenerationResult(bundle, []);
    }

    private static IReadOnlyList<double> EvaluateTiled(Phase8AMatMulScenarioPlan plan)
    {
        var output = new double[plan.Request.N];
        var kTiles = plan.Request.K / plan.Request.PeRows;
        var nTiles = plan.Request.N / plan.Request.PeColumns;
        for (var n = 0; n < nTiles; n++)
        {
            var partials = new List<IReadOnlyList<double>>();
            for (var k = 0; k < kTiles; k++)
            {
                var activation = plan.Input.Skip(k * plan.Request.PeRows).Take(plan.Request.PeRows).ToArray();
                var weights = Enumerable.Range(0, plan.Request.PeRows)
                    .SelectMany(row => plan.Weights
                        .Skip((k * plan.Request.PeRows + row) * plan.Request.N + n * plan.Request.PeColumns)
                        .Take(plan.Request.PeColumns))
                    .ToArray();
                partials.Add(DigitalVmmReferenceEvaluator.Evaluate(
                    activation, weights, plan.Request.PeRows, plan.Request.PeColumns,
                    "fp8", "fp8", "fp16", "fp8"));
            }
            for (var column = 0; column < plan.Request.PeColumns; column++)
                output[n * plan.Request.PeColumns + column] = partials.Sum(partial => partial[column]);
        }
        return Array.AsReadOnly(output);
    }

    private static (IReadOnlyList<double>? Output, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) AssembleDeliveredOutput(
        SimulationResult simulation,
        Phase8AMatMulScenarioRequest request)
    {
        if (request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1)
        {
            if (simulation.DeliveredPackets.Count != 1)
                return (null, [new("Phase8AScenarioOutputCoverageInvalid", "$.simulation.deliveredPackets", "Offset assembly must deliver exactly one complete Y packet.")]);
            var packet = simulation.DeliveredPackets[0];
            if (packet.Values.Count != request.N || packet.NumElements != request.N || packet.Values.Any(value => !double.IsFinite(value)))
                return (null, [new("Phase8AScenarioOutputRangeInvalid", "$.simulation.deliveredPackets[0]", "The delivered Y packet has invalid shape or numeric values.")]);
            return (Array.AsReadOnly(packet.Values.ToArray()), []);
        }

        var expectedShards = request.N / request.PeColumns;
        if (simulation.DeliveredPackets.Count != expectedShards)
            return (null, [new("Phase8AScenarioOutputCoverageInvalid", "$.simulation.deliveredPackets", $"Cluster-local landing must deliver exactly {expectedShards} disjoint Y shards.")]);
        var output = new double[request.N];
        var covered = new bool[request.N];
        for (var packetIndex = 0; packetIndex < simulation.DeliveredPackets.Count; packetIndex++)
        {
            var packet = simulation.DeliveredPackets[packetIndex];
            if (!packet.Metadata.TryGetValue(Phase8ACollectiveRuntimeMetadata.OutputNOffset, out var rawOffset) ||
                !packet.Metadata.TryGetValue(Phase8ACollectiveRuntimeMetadata.OutputNExtent, out var rawExtent) ||
                !int.TryParse(rawOffset, out var offset) || !int.TryParse(rawExtent, out var extent) ||
                offset < 0 || extent <= 0 || offset > request.N - extent ||
                packet.Values.Count != extent || packet.NumElements != extent ||
                packet.Values.Any(value => !double.IsFinite(value)))
                return (null, [new("Phase8AScenarioOutputRangeInvalid", $"$.simulation.deliveredPackets[{packetIndex}]", "A cluster-local Y shard has invalid offset, extent, shape, or numeric values.")]);
            for (var index = 0; index < extent; index++)
            {
                var destination = offset + index;
                if (covered[destination])
                    return (null, [new("Phase8AScenarioOutputCoverageInvalid", $"$.simulation.deliveredPackets[{packetIndex}]", "Cluster-local Y shards overlap.")]);
                covered[destination] = true;
                output[destination] = packet.Values[index];
            }
        }
        if (covered.Any(value => !value))
            return (null, [new("Phase8AScenarioOutputCoverageInvalid", "$.simulation.deliveredPackets", "Cluster-local Y shards do not cover the complete output range.")]);
        return (Array.AsReadOnly(output), []);
    }

    private static Phase8AMatMulDataflowEvidence BuildDataflowEvidence(
        Phase8AMatMulScenarioPlan plan,
        Phase8AMatMulExecutableScenario scenario,
        SimulationResult simulation,
        string executionMode)
    {
        var fixedPacketStrategy = string.Equals(
            plan.Request.TopologyExecutionStrategyId,
            Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1,
            StringComparison.Ordinal);
        var activation = plan.ActivationTree.Summary;
        var reduction = plan.HierarchicalReduction.Summary;
        var vectorBits = checked((long)plan.Request.PeColumns * plan.HierarchicalReduction.VectorBitWidth);
        var returnTransfers = checked(reduction.LocalTreeLinkTransferCount +
            reduction.GlobalReturnLinkTransferCount + reduction.AssemblyReturnLinkTransferCount);
        var meshTransfers = checked(reduction.GlobalMeshLinkTransferCount + reduction.AssemblyMeshLinkTransferCount);
        return new Phase8AMatMulDataflowEvidence(
            fixedPacketStrategy ? scenario.Operands.Count(packet => packet.PacketType == PacketType.Activation) : activation.SourceActivationPacketCount,
            plan.Request.WeightsPreplaced ? 0 : plan.Assignments.Count,
            plan.DcLayout.Summary.PePartialCount,
            reduction.PePartialCount,
            reduction.GlobalGroupCount,
            activation.UniqueLinkTransferCount,
            returnTransfers,
            meshTransfers,
            simulation.TraceHash?.Hash ?? "",
            executionMode,
            activation.ClusterActivationDeliveryCount,
            activation.AdditionalCloneCount,
            reduction.LocalResultCount,
            reduction.GlobalContributorPacketCount,
            1,
            reduction.LocalAddOperationCount,
            reduction.GlobalAddOperationCount,
            activation.TransferredBits,
            checked(returnTransfers * vectorBits),
            checked(meshTransfers * vectorBits),
            reduction.AssemblyTransferredBits,
            plan.DcLayout.CanonicalHash,
            plan.ActivationTree.CanonicalHash,
            plan.HierarchicalReduction.CanonicalHash,
            Physical("packets"),
            Physical("bits"),
            Physical("flits"),
            InternalCollectiveOperationCount(plan),
            simulation.Metrics.Links.Values.Sum(link => link.PacketsTransferred),
            PhysicalBitDistance());

        long Physical(string metric)
        {
            var physicalIds = scenario.HardwareGraph.Links
                .Where(link => string.Equals(
                    link.Parameters.GetValueOrDefault(Phase8AMoTInrRuntimeIds.MetricScopeParameter, ""),
                    Phase8AMoTInrRuntimeIds.PhysicalMetricScope,
                    StringComparison.Ordinal))
                .Select(link => link.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return simulation.Metrics.Links.Values
                .Where(link => physicalIds.Contains(link.LinkId))
                .Sum(link => metric switch
                {
                    "packets" => fixedPacketStrategy
                        ? FixedPacketTransfers(link.LinkId, link.TotalBitsTransferred)
                        : link.PacketsTransferred,
                    "bits" => link.TotalBitsTransferred,
                    "flits" => link.FlitsTransferred,
                    _ => 0
                });
        }

        long FixedPacketTransfers(string linkId, long bits)
        {
            if (bits % Phase8AMoTInrRuntimeIds.FixedPacketBits != 0)
                throw new InvalidOperationException($"Physical link '{linkId}' transferred {bits} bits, which is not divisible by the {Phase8AMoTInrRuntimeIds.FixedPacketBits}-bit MoT-INR packet unit.");
            return bits / Phase8AMoTInrRuntimeIds.FixedPacketBits;
        }

        double PhysicalBitDistance()
        {
            var physicalLinks = scenario.HardwareGraph.Links
                .Where(link => string.Equals(
                    link.Parameters.GetValueOrDefault(Phase8AMoTInrRuntimeIds.MetricScopeParameter, ""),
                    Phase8AMoTInrRuntimeIds.PhysicalMetricScope,
                    StringComparison.Ordinal))
                .ToDictionary(link => link.Id, StringComparer.OrdinalIgnoreCase);
            return simulation.Metrics.Links.Values
                .Where(metric => physicalLinks.ContainsKey(metric.LinkId))
                .Sum(metric => metric.TotalBitsTransferred * physicalLinks[metric.LinkId].PhysicalLength);
        }
    }

    private static long InternalCollectiveOperationCount(Phase8AMatMulScenarioPlan plan)
    {
        if (plan.Request.TopologyExecutionStrategyId != Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1)
            return checked(plan.HierarchicalReduction.Summary.LocalAddOperationCount + plan.HierarchicalReduction.Summary.GlobalAddOperationCount);
        var sliceOperations = plan.TopologyManifest.Components.Count(component => component.Role == TopologyPresetComponentRole.TreeRouter);
        var meshBranchOperations = checked(plan.MeshRows * Math.Max(0, plan.MeshColumns - 1));
        var concatOperations = plan.Request.OutputLandingMode == Phase8AOutputLandingModes.CentralOffsetAssemblyV1 ? 1 : 0;
        return checked((long)sliceOperations + meshBranchOperations +
            plan.HierarchicalReduction.Summary.LocalAddOperationCount +
            plan.HierarchicalReduction.Summary.GlobalAddOperationCount + concatOperations);
    }}
