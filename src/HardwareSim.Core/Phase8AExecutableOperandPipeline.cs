using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Stable metadata used by the Phase 8A executable operand pipeline.</summary>
public static class Phase8AOperandPipelineMetadata
{
    /// <summary>Stable Phase 8A contract value for ExternalOperand.</summary>
    public const string ExternalOperand = "phase8a.pipeline.external_operand";
    /// <summary>Stable Phase 8A contract value for OperandRole.</summary>
    public const string OperandRole = "phase8a.pipeline.operand_role";
    /// <summary>Stable Phase 8A contract value for FlowId.</summary>
    public const string FlowId = "phase8a.pipeline.flow_id";
    /// <summary>Stable Phase 8A contract value for LayerId.</summary>
    public const string LayerId = "phase8a.pipeline.layer_id";
    /// <summary>Stable Phase 8A contract value for StageId.</summary>
    public const string StageId = "phase8a.pipeline.stage_id";
    /// <summary>Stable Phase 8A contract value for InvocationId.</summary>
    public const string InvocationId = "phase8a.pipeline.invocation_id";
    /// <summary>Stable Phase 8A contract value for MappingHash.</summary>
    public const string MappingHash = "phase8a.pipeline.mapping_hash";
    /// <summary>Stable Phase 8A contract value for OperandPlanHash.</summary>
    public const string OperandPlanHash = "phase8a.pipeline.operand_plan_hash";
    /// <summary>Stable Phase 8A contract value for Operation.</summary>
    public const string Operation = "phase8a.pipeline.operation";
}

/// <summary>One structured executable-operand compilation issue.</summary>
public sealed record Phase8AExecutableOperandIssue(string Code, string Location, string Message, string RelatedId = "");

/// <summary>Result of compiling topology-aware mapping flows into real executable operands.</summary>
public sealed class Phase8AExecutableOperandCompilationResult
{
    /// <summary>Gets whether compilation produced an executable without issues.</summary>
    public bool IsSuccess => Executable is not null && Issues.Count == 0;
    /// <summary>Gets the compiled exact-operand executable.</summary>
    public ExecutableSimulationGraph? Executable { get; init; }
    /// <summary>Gets structured compilation issues.</summary>
    public IReadOnlyList<Phase8AExecutableOperandIssue> Issues { get; init; } = [];
    /// <summary>Gets the deterministic operand-plan hash.</summary>
    public string OperandPlanHash { get; init; } = "";
}

/// <summary>Compiles only externally sourced real operands; all intermediate tensors must be produced by runtime kernels.</summary>
public static class Phase8AExecutableOperandCompiler
{
    private static readonly HashSet<string> ExternalRoles = new(StringComparer.Ordinal)
    {
        "input", "weight", "bias", "control"
    };

    /// <summary>Builds an ExactOperands executable from a verified Mapping 2.0 flow catalog.</summary>
    public static Phase8AExecutableOperandCompilationResult Compile(
        HardwareSimulationGraph hardwareGraph,
        WorkloadMappingV2 mapping,
        IReadOnlyList<Packet> initialOperands,
        WorkloadSchedule? schedule = null)
    {
        if (hardwareGraph is null) throw new ArgumentNullException(nameof(hardwareGraph));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (initialOperands is null) throw new ArgumentNullException(nameof(initialOperands));
        var issues = new List<Phase8AExecutableOperandIssue>();
        WorkloadMappingV2? verifiedMapping = null;
        try
        {
            var imported = WorkloadMappingV2.FromJson(mapping.ToJson());
            if (!imported.CanCompileTopologyAware || imported.Mapping is null)
            {
                issues.AddRange(imported.Issues.Where(issue => issue.Severity == ValidationSeverity.Error)
                    .Select(issue => new Phase8AExecutableOperandIssue(issue.Code, issue.Location, issue.Message, issue.RelatedId ?? "")));
                if (issues.Count == 0)
                    issues.Add(Issue("ExecutableMappingModeInvalid", "$.mapping.mode_id", "Exact operand compilation requires a verified topology-aware WorkloadMappingV2."));
            }
            else verifiedMapping = imported.Mapping;
        }
        catch (JsonException exception)
        {
            issues.Add(Issue("ExecutableMappingInvalid", "$.mapping", exception.Message));
        }

        if (initialOperands.Count == 0)
            issues.Add(Issue("ExecutableOperandsMissing", "$.initial_operands", "At least one external operand transaction is required."));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packets = new List<Packet>();
        if (verifiedMapping is not null)
        {
            foreach (var operand in initialOperands.OrderBy(packet => packet.InjectionCycle).ThenBy(packet => packet.Id, StringComparer.Ordinal))
            {
                var location = "$.initial_operands[" + packets.Count + "]";
                var before = issues.Count;
                ValidatePacketShape(operand, location, ids, issues);
                var role = operand.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.OperandRole, "").Trim();
                if (!string.Equals(operand.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.ExternalOperand, ""), "true", StringComparison.Ordinal) ||
                    !ExternalRoles.Contains(role))
                    issues.Add(Issue("ExecutableDerivedOperandInjectionForbidden", location,
                        "InitialPackets may contain only explicit input, weight, bias, or control operands; intermediate results must be kernel-produced.", operand.Id));
                if (operand.DependencyIds.Count > 0)
                    issues.Add(Issue("ExecutableExternalOperandDependencyInvalid", location + ".dependency_ids",
                        "An externally injected operand cannot claim a runtime-produced dependency.", operand.Id));
                var flowId = operand.Metadata.GetValueOrDefault(Phase8AOperandPipelineMetadata.FlowId, "").Trim();
                var flow = verifiedMapping.CommunicationFlows.SingleOrDefault(candidate => string.Equals(candidate.FlowId, flowId, StringComparison.Ordinal));
                if (flow is null)
                {
                    issues.Add(Issue("ExecutableOperandFlowMissing", location + ".metadata.flow_id", $"Operand '{operand.Id}' references missing mapping flow '{flowId}'.", operand.Id));
                    continue;
                }
                if (!string.Equals(flow.ProducerComponentId, operand.SourceComponentId, StringComparison.Ordinal) ||
                    !string.Equals(flow.TensorTileId, operand.TileId, StringComparison.Ordinal) || flow.Bits != operand.Bits)
                    issues.Add(Issue("ExecutableOperandFlowMismatch", location,
                        $"Operand '{operand.Id}' must match mapping flow producer, tile, and exact bit count.", operand.Id));
                var route = flow.ConsumerRoutes.SingleOrDefault(candidate => string.Equals(candidate.ConsumerComponentId, operand.DestinationComponentId, StringComparison.Ordinal));
                if (route is null)
                {
                    issues.Add(Issue("ExecutableOperandRouteMissing", location,
                        $"Mapping flow '{flow.FlowId}' has no exact route to '{operand.DestinationComponentId}'.", operand.Id));
                    continue;
                }
                ValidateRoute(hardwareGraph, operand, route, location, issues);
                if (issues.Count != before) continue;
                var clone = PacketClone.Clone(operand);
                clone.RoutePath = [];
                Phase8AExplicitRouteMetadata.Bind(clone, route.RoutePathId, route.LinkIds);
                clone.Metadata[Phase8AOperandPipelineMetadata.MappingHash] = verifiedMapping.CanonicalHash;
                packets.Add(clone);
            }
        }

        if (issues.Count > 0)
            return Failed(issues);
        var planHash = ComputePlanHash(verifiedMapping!, packets);
        foreach (var packet in packets) packet.Metadata[Phase8AOperandPipelineMetadata.OperandPlanHash] = planHash;
        var frozenPackets = packets.Select(PacketClone.Clone).ToList().AsReadOnly();
        var flits = frozenPackets.SelectMany(packet => FlitPacketizer.Packetize(packet, hardwareGraph.SimulationConfig.FlitSizeBits)).ToList().AsReadOnly();
        return new Phase8AExecutableOperandCompilationResult
        {
            Executable = new ExecutableSimulationGraph
            {
                HardwareGraph = hardwareGraph,
                Schedule = schedule ?? new WorkloadSchedule { WorkloadId = "phase8a-exact-operands" },
                InitialPacketExecutionMode = ExecutableInitialPacketExecutionMode.ExactOperands,
                InitialPackets = frozenPackets,
                InitialFlits = flits,
                PacketizationMode = PacketizationMode.FlitLevelMode,
                TransportSemantics = hardwareGraph.SimulationConfig.TransportSemantics,
                WorkloadMappingProvenance = new WorkloadMappingProvenance
                {
                    WorkloadSchemaVersion = "phase8a-typed-operands-v1",
                    WorkloadHash = verifiedMapping!.Provenance.WorkloadHash,
                    MappingSchemaVersion = verifiedMapping.SchemaVersion,
                    MappingHash = verifiedMapping.CanonicalHash,
                    Note = "phase8a exact operand plan " + planHash
                }
            },
            Issues = [],
            OperandPlanHash = planHash
        };
    }

    private static void ValidatePacketShape(Packet packet, string location, HashSet<string> ids, List<Phase8AExecutableOperandIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(packet.Id) || !ids.Add(packet.Id))
            issues.Add(Issue("ExecutableOperandIdInvalid", location + ".id", "External operand ids must be non-empty and unique.", packet.Id));
        if (packet.Values.Count == 0 || packet.Values.Count != packet.NumElements || packet.Values.Any(value => !double.IsFinite(value)))
            issues.Add(Issue("ExecutableOperandPayloadInvalid", location + ".values", "External operands require finite values matching NumElements.", packet.Id));
        if (packet.NumElements <= 0 || packet.BitWidth <= 0 || packet.Bits != checked((long)packet.NumElements * packet.BitWidth))
            issues.Add(Issue("ExecutableOperandBitsInvalid", location + ".bits", "Operand Bits must exactly equal NumElements times BitWidth.", packet.Id));
        if (string.IsNullOrWhiteSpace(packet.WorkloadOpId) || string.IsNullOrWhiteSpace(packet.TensorId) || string.IsNullOrWhiteSpace(packet.TileId))
            issues.Add(Issue("ExecutableOperandProvenanceMissing", location, "WorkloadOpId, TensorId, and TileId are required.", packet.Id));
        if (packet.PacketType == PacketType.PartialSum)
            issues.Add(Issue("ExecutableDerivedOperandInjectionForbidden", location + ".packet_type", "Partial sums must be produced inside the runtime.", packet.Id));
    }

    private static void ValidateRoute(
        HardwareSimulationGraph graph,
        Packet packet,
        CommunicationConsumerRoute route,
        string location,
        List<Phase8AExecutableOperandIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(route.RoutePathId) || route.LinkIds.Count == 0)
        {
            issues.Add(Issue("ExecutableOperandRouteInvalid", location, "Exact route id and directed links are required.", packet.Id));
            return;
        }
        var expectedSource = packet.SourceComponentId;
        foreach (var linkId in route.LinkIds)
        {
            var link = graph.Links.SingleOrDefault(candidate => string.Equals(candidate.Id, linkId, StringComparison.Ordinal));
            if (link is null || !string.Equals(link.Source.ComponentId, expectedSource, StringComparison.Ordinal))
            {
                issues.Add(Issue("ExecutableOperandRouteInvalid", location, $"Route '{route.RoutePathId}' is missing link '{linkId}' or is not a contiguous directed path.", packet.Id));
                return;
            }
            expectedSource = link.Destination.ComponentId;
        }
        if (!string.Equals(expectedSource, packet.DestinationComponentId, StringComparison.Ordinal))
            issues.Add(Issue("ExecutableOperandRouteInvalid", location, $"Route '{route.RoutePathId}' does not terminate at '{packet.DestinationComponentId}'.", packet.Id));
    }

    private static string ComputePlanHash(WorkloadMappingV2 mapping, IReadOnlyList<Packet> packets) =>
        ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
        {
            mapping.CanonicalHash,
            Packets = packets.Select(packet => new
            {
                packet.Id,
                packet.PacketType,
                packet.NumElements,
                packet.BitWidth,
                packet.Bits,
                packet.Precision,
                packet.SourceComponentId,
                packet.DestinationComponentId,
                packet.WorkloadOpId,
                packet.TensorId,
                packet.TileId,
                packet.InjectionCycle,
                packet.Values,
                Metadata = packet.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            })
        }, HardwareGraphJson.Options)));

    private static Phase8AExecutableOperandCompilationResult Failed(IEnumerable<Phase8AExecutableOperandIssue> issues) => new()
    {
        Issues = new ReadOnlyCollection<Phase8AExecutableOperandIssue>(issues.ToList())
    };

    private static Phase8AExecutableOperandIssue Issue(string code, string location, string message, string relatedId = "") =>
        new(code, location, message, relatedId);
}
