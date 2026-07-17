using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

/// <summary>Contains an imported WorkloadMapping 2.0 document or structured issues.</summary>
public sealed class WorkloadMappingV2ImportResult
{
    /// <summary>Gets whether import returned a mapping without error-severity issues.</summary>
    public bool IsSuccess => Mapping is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
    /// <summary>Gets whether the imported mapping is a native topology-aware document.</summary>
    public bool CanCompileTopologyAware => IsSuccess && string.Equals(Mapping?.ModeId, WorkloadMappingV2Modes.TopologyAware, StringComparison.Ordinal);
    /// <summary>Gets the imported immutable mapping when available.</summary>
    public WorkloadMappingV2? Mapping { get; init; }
    /// <summary>Gets the source schema version detected before migration.</summary>
    public string SourceVersion { get; init; } = "";
    /// <summary>Gets applied migration steps in execution order.</summary>
    public IReadOnlyList<WorkloadMappingV2MigrationStep> MigrationPath { get; init; } = [];
    /// <summary>Gets structured import, migration, and validation issues.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; init; } = [];
}

/// <summary>Provides strict WorkloadMapping 2.0 JSON import and serialization.</summary>
public static class WorkloadMappingV2Json
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes an immutable mapping and writes its verified canonical hash.</summary>
    /// <param name="mapping">Mapping document to serialize.</param>
    /// <returns>Strict WorkloadMapping 2.0 JSON.</returns>
    public static string Serialize(WorkloadMappingV2 mapping)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        var issues = WorkloadMappingV2Validator.Validate(mapping, requireCanonicalHash: false);
        var errors = issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new JsonException(string.Join("; ", errors.Select(issue => $"{issue.Code} at {issue.Location}: {issue.Message}")));
        }

        var hash = WorkloadMappingV2CanonicalHasher.Compute(mapping);
        return SerializeRaw(mapping.WithCanonicalHash(hash.Hash));
    }

    /// <summary>Imports a strict 2.0 document or explicitly migrates a 1.0 document.</summary>
    /// <param name="json">Mapping JSON to import.</param>
    /// <returns>The imported mapping, migration path, and structured issues.</returns>
    public static WorkloadMappingV2ImportResult ImportToCurrent(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failed("InvalidJson", "$", "WorkloadMapping JSON is empty.");
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("WorkloadMapping JSON must contain an object.");
        }
        catch (JsonException exception)
        {
            return Failed("InvalidJson", exception.Path ?? "$", exception.Message);
        }

        var version = ReadString(root, "schema_version");
        if (string.IsNullOrWhiteSpace(version))
        {
            return Failed("MissingSchemaVersion", "$.schema_version", "WorkloadMapping schema_version is required.");
        }

        if (string.Equals(version, "1.0", StringComparison.Ordinal))
        {
            return WorkloadMappingV2Migrator.MigrateLegacyJson(json);
        }

        if (!string.Equals(version, WorkloadMappingV2.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            var major = version.Split('.', 2)[0];
            var code = string.Equals(major, "2", StringComparison.Ordinal)
                ? "MigrationPathNotFound"
                : "UnsupportedSchemaVersion";
            return Failed(code, "$.schema_version", $"WorkloadMapping schema version '{version}' cannot be imported as {WorkloadMappingV2.CurrentSchemaVersion}.", version);
        }

        WorkloadMappingV2? mapping;
        try
        {
            mapping = JsonSerializer.Deserialize<WorkloadMappingV2>(json, Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return Failed("InvalidMappingDocument", exception is JsonException jsonException ? jsonException.Path ?? "$" : "$", exception.Message, version);
        }

        if (mapping is null)
        {
            return Failed("InvalidMappingDocument", "$", "WorkloadMapping JSON did not produce a mapping document.", version);
        }

        var issues = WorkloadMappingV2Validator.Validate(mapping, requireCanonicalHash: true).ToList();
        if (issues.All(issue => issue.Severity != ValidationSeverity.Error))
        {
            var expected = WorkloadMappingV2CanonicalHasher.Compute(mapping).Hash;
            if (!string.Equals(mapping.CanonicalHash, expected, StringComparison.Ordinal))
            {
                issues.Add(new WorkloadMappingV2Issue(
                    "MappingCanonicalHashMismatch",
                    ValidationSeverity.Error,
                    "$.canonicalHash",
                    "Persisted WorkloadMapping canonical hash does not match its semantic contents."));
            }
        }

        return new WorkloadMappingV2ImportResult
        {
            Mapping = mapping,
            SourceVersion = version,
            Issues = MappingV2Freeze.List(issues)
        };
    }

    internal static string SerializeRaw(WorkloadMappingV2 mapping) => JsonSerializer.Serialize(mapping, Options);

    private static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static string ReadString(JsonObject document, string key) =>
        document.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : "";

    private static WorkloadMappingV2ImportResult Failed(string code, string location, string message, string sourceVersion = "") => new()
    {
        SourceVersion = sourceVersion,
        Issues = [new WorkloadMappingV2Issue(code, ValidationSeverity.Error, location, message)]
    };
}

/// <summary>Validates immutable WorkloadMapping 2.0 structural and reference invariants.</summary>
public static class WorkloadMappingV2Validator
{
    /// <summary>Validates one mapping without mutating it.</summary>
    /// <param name="mapping">Mapping document to validate.</param>
    /// <param name="requireCanonicalHash">Whether a persisted canonical hash is required.</param>
    /// <returns>Structured issues in deterministic order.</returns>
    public static IReadOnlyList<WorkloadMappingV2Issue> Validate(WorkloadMappingV2 mapping, bool requireCanonicalHash = true)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        var issues = new List<WorkloadMappingV2Issue>();
        Require(string.Equals(mapping.SchemaVersion, WorkloadMappingV2.CurrentSchemaVersion, StringComparison.Ordinal), issues,
            "UnsupportedSchemaVersion", "$.schema_version", $"Expected WorkloadMapping schema {WorkloadMappingV2.CurrentSchemaVersion}.");
        Require(!string.IsNullOrWhiteSpace(mapping.MappingId), issues,
            "MissingMappingId", "$.mappingId", "Mapping identifier is required.");
        Require(string.Equals(mapping.CanonicalHashAlgorithm, WorkloadMappingV2.CurrentCanonicalHashAlgorithm, StringComparison.Ordinal), issues,
            "UnsupportedCanonicalHashAlgorithm", "$.canonicalHashAlgorithm", $"Expected canonical hash algorithm '{WorkloadMappingV2.CurrentCanonicalHashAlgorithm}'.");
        if (requireCanonicalHash)
        {
            Require(IsLowerSha256(mapping.CanonicalHash), issues,
                "MissingCanonicalHash", "$.canonicalHash", "A lowercase 64-character SHA-256 canonical hash is required.");
        }

        ValidateUnique(mapping.OperationTileAssignments.Select(item => item.AssignmentId), "$.operationTileAssignments", "DuplicateAssignmentId", issues);
        ValidateUnique(mapping.OperandPlacements.Select(item => item.PlacementId), "$.operandPlacements", "DuplicateOperandPlacementId", issues);
        ValidateUnique(mapping.CommunicationFlows.Select(item => item.FlowId), "$.communicationFlows", "DuplicateCommunicationFlowId", issues);
        ValidateUnique(mapping.CollectivePlans.Select(item => item.CollectiveId), "$.collectivePlans", "DuplicateCollectivePlanId", issues);
        ValidateUnique(mapping.CapabilitySnapshot.Components.Select(item => item.ComponentId), "$.capabilitySnapshot.components", "DuplicateCapabilityComponentId", issues);
        ValidateUnique(mapping.CapabilitySnapshot.Components.SelectMany(component => component.Ports).Select(port => port.PortId),
            "$.capabilitySnapshot.components.ports", "DuplicateCapabilityPortId", issues);
        ValidateCandidateAndProvenance(mapping, issues);

        if (string.Equals(mapping.ModeId, WorkloadMappingV2Modes.LegacyCompatibility, StringComparison.Ordinal))
        {
            Require(mapping.LegacyCompatibilitySnapshot is not null, issues,
                "MissingLegacyCompatibilitySnapshot", "$.legacyCompatibilitySnapshot", "Legacy compatibility mode requires the complete normalized 1.0 snapshot.");
            Require(mapping.OperationTileAssignments.Count == 0 && mapping.OperandPlacements.Count == 0 &&
                    mapping.CommunicationFlows.Count == 0 && mapping.CollectivePlans.Count == 0,
                issues, "LegacyCompatibilityInventedSemantics", "$", "Legacy compatibility mode cannot contain inferred Mapping 2.0 assignments, placements, flows, or collectives.");
        }
        else if (string.Equals(mapping.ModeId, WorkloadMappingV2Modes.TopologyAware, StringComparison.Ordinal))
        {
            Require(mapping.LegacyCompatibilitySnapshot is null, issues,
                "UnexpectedLegacyCompatibilitySnapshot", "$.legacyCompatibilitySnapshot", "Native topology-aware mappings cannot carry a legacy compatibility snapshot.");
            Require(!string.IsNullOrWhiteSpace(mapping.CapabilitySnapshot.SnapshotId) &&
                    !string.Equals(mapping.CapabilitySnapshot.SnapshotId, "unresolved", StringComparison.Ordinal),
                issues, "MissingCapabilitySnapshot", "$.capabilitySnapshot.snapshotId", "Native topology-aware mappings require a frozen capability snapshot.");
            Require(mapping.CapabilitySnapshot.Components.Count > 0, issues,
                "EmptyCapabilitySnapshot", "$.capabilitySnapshot.components", "Native topology-aware mappings require at least one component capability.");
            ValidateNativeSemantics(mapping, issues);
        }
        else
        {
            issues.Add(new WorkloadMappingV2Issue(
                "UnsupportedMappingMode",
                ValidationSeverity.Error,
                "$.modeId",
                $"Mapping mode '{mapping.ModeId}' is not supported."));
        }

        return MappingV2Freeze.Sorted(issues, issue => $"{issue.Location}\u001f{issue.Code}\u001f{issue.RelatedId}");
    }

    private static void ValidateNativeSemantics(WorkloadMappingV2 mapping, List<WorkloadMappingV2Issue> issues)
    {
        var componentIds = mapping.CapabilitySnapshot.Components.Select(item => item.ComponentId).ToHashSet(StringComparer.Ordinal);
        foreach (var component in mapping.CapabilitySnapshot.Components)
        {
            Require(!string.IsNullOrWhiteSpace(component.StableTypeId) &&
                    !string.IsNullOrWhiteSpace(component.DomainId) &&
                    component.CapacityBits >= 0 && component.LatencyCycles >= 0 &&
                    component.BandwidthBitsPerCycle >= 0,
                issues, "InvalidComponentCapabilityContract", "$.capabilitySnapshot.components",
                "Component stable type and domain identities are required; capacity, latency, and bandwidth must be non-negative.", component.ComponentId);
            ValidateUnique(component.OperationKindIds, "$.capabilitySnapshot.components.operationKindIds", "InvalidComponentOperationKindId", issues);
            ValidateUnique(component.PrecisionIds, "$.capabilitySnapshot.components.precisionIds", "InvalidComponentPrecisionId", issues);
            foreach (var port in component.Ports)
            {
                Require(!string.IsNullOrWhiteSpace(port.PortId) &&
                        !string.IsNullOrWhiteSpace(port.DirectionId) &&
                        !string.IsNullOrWhiteSpace(port.ProtocolId) &&
                        !string.IsNullOrWhiteSpace(port.DomainId) &&
                        port.SemanticRoleId is not null &&
                        port.BandwidthBitsPerCycle >= 0,
                    issues, "InvalidCapabilityPortContract", "$.capabilitySnapshot.components.ports",
                    "Capability port id, direction, protocol, domain, explicit semantic role string, and non-negative bandwidth are required.", port.PortId);
            }
            ValidateUnique(component.StorageCapabilities.Select(item => item.ResourceId), "$.capabilitySnapshot.components.storageCapabilities", "DuplicateStorageCapabilityResourceId", issues);
            ValidateUnique(component.StorageCapabilities.Select(item => item.StorageLevelId), "$.capabilitySnapshot.components.storageCapabilities", "DuplicateStorageCapabilityLevelId", issues);
            foreach (var storage in component.StorageCapabilities)
            {
                ValidateUnique(storage.SupportedOperandRoleIds, "$.capabilitySnapshot.components.storageCapabilities.supportedOperandRoleIds", "InvalidStorageOperandRoleId", issues);
                ValidateUnique(storage.SupportedPrecisionIds, "$.capabilitySnapshot.components.storageCapabilities.supportedPrecisionIds", "InvalidStoragePrecisionId", issues);
                Require(!string.IsNullOrWhiteSpace(storage.StorageLevelId), issues,
                    "MissingStorageLevelId", "$.capabilitySnapshot.components.storageCapabilities", "Storage capability level identifier is required.", storage.ResourceId);
                Require(storage.CapacityBits > 0 && storage.AlignmentBits > 0 && storage.AllocationGranularityBits > 0 &&
                        storage.ResidentSlots > 0 && storage.ReadBandwidthBitsPerCycle > 0 && storage.WriteBandwidthBitsPerCycle > 0 &&
                        storage.ReadLatencyCycles >= 0 && storage.WriteLatencyCycles >= 0,
                    issues, "InvalidStorageCapabilityRange", "$.capabilitySnapshot.components.storageCapabilities", "Storage capacity, alignment, granularity, slots, and bandwidth must be positive; latency values must be non-negative.", storage.ResourceId);
                var preloadPorts = component.Ports.Where(port =>
                    string.Equals(port.PortId, storage.PreloadPortId, StringComparison.Ordinal)).ToArray();
                Require(storage.SupportedOperandRoleIds.Count > 0 && storage.SupportedPrecisionIds.Count > 0 &&
                        !string.IsNullOrWhiteSpace(storage.PreloadPortId) &&
                        preloadPorts.Length == 1 &&
                        string.Equals(preloadPorts[0].DirectionId, "input", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(preloadPorts[0].SemanticRoleId), issues,
                    "IncompleteStorageAccessContract", "$.capabilitySnapshot.components.storageCapabilities", "Storage roles, precisions, and exactly one non-empty semantic input preload port are required.", storage.ResourceId);
                Require(!string.IsNullOrWhiteSpace(storage.CommitModeId) && !string.IsNullOrWhiteSpace(storage.SourceContractHash), issues,
                    "IncompleteStorageCapabilityContract", "$.capabilitySnapshot.components.storageCapabilities", "Storage commit mode and source contract hash are required.", storage.ResourceId);
            }
        }

        foreach (var assignment in mapping.OperationTileAssignments)
        {
            Require(!string.IsNullOrWhiteSpace(assignment.AssignmentId) &&
                    !string.IsNullOrWhiteSpace(assignment.OperationId) &&
                    !string.IsNullOrWhiteSpace(assignment.TileId) &&
                    !string.IsNullOrWhiteSpace(assignment.TargetPortId) &&
                    !string.IsNullOrWhiteSpace(assignment.PartitionPolicyId) &&
                    assignment.OperandTileIds.All(id => !string.IsNullOrWhiteSpace(id)),
                issues, "IncompleteAssignmentContract", "$.operationTileAssignments",
                "Assignment, operation, tile, target-port, operand-tile, and partition-policy identities must be explicit.", assignment.AssignmentId);
            var targetComponent = mapping.CapabilitySnapshot.Components.FirstOrDefault(component =>
                string.Equals(component.ComponentId, assignment.TargetComponentId, StringComparison.Ordinal));
            Require(targetComponent is not null, issues, "AssignmentTargetNotInCapabilitySnapshot", "$.operationTileAssignments", $"Assignment target '{assignment.TargetComponentId}' is not present in the frozen capability snapshot.", assignment.AssignmentId);
            Require(targetComponent?.Ports.Count(port =>
                        string.Equals(port.PortId, assignment.TargetPortId, StringComparison.Ordinal)) == 1,
                issues, "AssignmentTargetPortNotInTargetCapability", "$.operationTileAssignments",
                $"Assignment target port '{assignment.TargetPortId}' is not declared exactly once by component '{assignment.TargetComponentId}'.", assignment.AssignmentId);
            ValidateRange(assignment.MRange, "mRange", assignment.AssignmentId, issues);
            ValidateRange(assignment.KRange, "kRange", assignment.AssignmentId, issues);
            ValidateRange(assignment.NRange, "nRange", assignment.AssignmentId, issues);
            Require(assignment.ValidShape.Dimensions.All(value => value >= 0) && assignment.PaddedShape.Dimensions.All(value => value >= 0),
                issues, "InvalidAssignmentShape", "$.operationTileAssignments", "Valid and padded shapes cannot contain negative dimensions.", assignment.AssignmentId);
        }

        foreach (var placement in mapping.OperandPlacements)
        {
            Require(!string.IsNullOrWhiteSpace(placement.OperationId) &&
                    !string.IsNullOrWhiteSpace(placement.TensorId) &&
                    !string.IsNullOrWhiteSpace(placement.TileId) &&
                    !string.IsNullOrWhiteSpace(placement.OperandRoleId) &&
                    !string.IsNullOrWhiteSpace(placement.StorageComponentId) &&
                    !string.IsNullOrWhiteSpace(placement.StorageLevelId),
                issues, "IncompleteOperandPlacementContract", "$.operandPlacements",
                "Operand operation, tensor, tile, role, storage-component, and storage-level identities are required.", placement.PlacementId);
            Require(placement.AddressBits >= 0 && placement.SizeBits > 0 && placement.AlignmentBits > 0,
                issues, "InvalidOperandPlacementRange", "$.operandPlacements", "Operand address must be non-negative; size and alignment must be positive.", placement.PlacementId);
            var storageComponent = mapping.CapabilitySnapshot.Components.FirstOrDefault(component =>
                string.Equals(component.ComponentId, placement.StorageComponentId, StringComparison.Ordinal));
            Require(storageComponent is not null, issues,
                "PlacementTargetNotInCapabilitySnapshot", "$.operandPlacements", $"Storage target '{placement.StorageComponentId}' is not present in the frozen capability snapshot.", placement.PlacementId);
            Require(!string.IsNullOrWhiteSpace(placement.ResidencyModeId) && !string.IsNullOrWhiteSpace(placement.LoadModeId), issues,
                "MissingOperandPlacementMode", "$.operandPlacements", "Residency and load modes must be declared independently.", placement.PlacementId);
            var storage = storageComponent?.StorageCapabilities.FirstOrDefault(capability =>
                string.Equals(capability.StorageLevelId, placement.StorageLevelId, StringComparison.Ordinal));
            Require(storage is not null, issues,
                "OperandStorageCapabilityNotFound", "$.operandPlacements", $"Storage level '{placement.StorageLevelId}' is not declared by component '{placement.StorageComponentId}'.", placement.PlacementId);
            if (storage is not null)
            {
                Require(storage.SupportedOperandRoleIds.Contains(placement.OperandRoleId, StringComparer.Ordinal), issues,
                    "OperandRoleNotSupportedByStorage", "$.operandPlacements", $"Storage resource '{storage.ResourceId}' does not support operand role '{placement.OperandRoleId}'.", placement.PlacementId);
                Require(placement.SizeBits <= storage.CapacityBits && placement.AddressBits <= storage.CapacityBits - placement.SizeBits, issues,
                    "OperandPlacementExceedsStorageCapacity", "$.operandPlacements", $"Operand address range exceeds storage resource '{storage.ResourceId}' capacity.", placement.PlacementId);
                Require(placement.AlignmentBits >= storage.AlignmentBits && placement.AlignmentBits % storage.AlignmentBits == 0 &&
                        placement.AddressBits % placement.AlignmentBits == 0, issues,
                    "OperandPlacementAlignmentMismatch", "$.operandPlacements", $"Operand placement does not satisfy storage resource '{storage.ResourceId}' alignment.", placement.PlacementId);
                Require(storage.AllocationGranularityBits == 0 || placement.SizeBits % storage.AllocationGranularityBits == 0, issues,
                    "OperandPlacementGranularityMismatch", "$.operandPlacements", $"Operand size does not satisfy storage resource '{storage.ResourceId}' allocation granularity.", placement.PlacementId);
                Require(!string.Equals(placement.LoadModeId, "streaming", StringComparison.Ordinal) || storage.SupportsStreaming, issues,
                    "StreamingLoadNotSupported", "$.operandPlacements", $"Storage resource '{storage.ResourceId}' does not support streaming load mode.", placement.PlacementId);
                Require(string.IsNullOrWhiteSpace(placement.ReuseGroupId) || storage.SupportsReuse, issues,
                    "OperandReuseNotSupported", "$.operandPlacements", $"Storage resource '{storage.ResourceId}' does not support resident reuse.", placement.PlacementId);
            }
        }

        foreach (var selectorGroup in mapping.OperandPlacements
                     .GroupBy(placement => (placement.StorageComponentId, placement.StorageLevelId)))
        {
            var storage = mapping.CapabilitySnapshot.Components
                .FirstOrDefault(component => string.Equals(component.ComponentId, selectorGroup.Key.StorageComponentId, StringComparison.Ordinal))?
                .StorageCapabilities
                .FirstOrDefault(capability => string.Equals(capability.StorageLevelId, selectorGroup.Key.StorageLevelId, StringComparison.Ordinal));
            if (storage is null)
            {
                continue;
            }

            var ordered = selectorGroup
                .OrderBy(placement => placement.AddressBits)
                .ThenBy(placement => placement.PlacementId, StringComparer.Ordinal)
                .ToArray();
            Require(ordered.Length <= storage.ResidentSlots, issues,
                "OperandResidentSlotsExceeded", "$.operandPlacements",
                $"Storage selector '{selectorGroup.Key.StorageComponentId}/{selectorGroup.Key.StorageLevelId}' has {ordered.Length} resident placements but declares only {storage.ResidentSlots} slots.",
                selectorGroup.Key.StorageComponentId);
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                var previousEndIsValid = previous.SizeBits > 0 && previous.AddressBits >= 0 &&
                                         previous.AddressBits <= long.MaxValue - previous.SizeBits;
                Require(!previousEndIsValid || current.AddressBits >= previous.AddressBits + previous.SizeBits, issues,
                    "OperandPlacementOverlap", "$.operandPlacements",
                    $"Operand placements '{previous.PlacementId}' and '{current.PlacementId}' overlap within storage selector '{selectorGroup.Key.StorageComponentId}/{selectorGroup.Key.StorageLevelId}'.",
                    current.PlacementId);
            }
        }
        foreach (var flow in mapping.CommunicationFlows)
        {
            Require(flow.Bits > 0, issues, "InvalidCommunicationBits", "$.communicationFlows", "Communication flow bits must be positive.", flow.FlowId);
            Require(!string.IsNullOrWhiteSpace(flow.TensorTileId) && !string.IsNullOrWhiteSpace(flow.FlowKindId),
                issues, "IncompleteCommunicationContract", "$.communicationFlows", "Communication tensor-tile and flow-kind identities are required.", flow.FlowId);
            var supportedFlowKind = flow.FlowKindId is
                Phase8ACommunicationFlowKinds.Unicast or
                Phase8ACommunicationFlowKinds.Multicast or
                Phase8ACommunicationFlowKinds.Broadcast;
            Require(supportedFlowKind, issues, "UnsupportedCommunicationFlowKind",
                "$.communicationFlows", "Communication flow kind must be exactly unicast, multicast, or broadcast.", flow.FlowId);
            if (flow.FlowKindId == Phase8ACommunicationFlowKinds.Unicast)
                Require(flow.ConsumerComponentIds.Count == 1 && flow.BranchComponentIds.Count == 0,
                    issues, "CommunicationFlowKindShapeMismatch", "$.communicationFlows",
                    "Unicast requires exactly one consumer and no branch components.", flow.FlowId);
            else if (flow.FlowKindId is Phase8ACommunicationFlowKinds.Multicast or Phase8ACommunicationFlowKinds.Broadcast)
                Require(flow.ConsumerComponentIds.Count > 1 && flow.BranchComponentIds.Count > 0,
                    issues, "CommunicationFlowKindShapeMismatch", "$.communicationFlows",
                    "Multicast and broadcast require multiple consumers and at least one explicit branch component.", flow.FlowId);
            Require(componentIds.Contains(flow.ProducerComponentId), issues, "FlowProducerNotInCapabilitySnapshot", "$.communicationFlows", $"Flow producer '{flow.ProducerComponentId}' is not present in the frozen capability snapshot.", flow.FlowId);
            Require(flow.ConsumerComponentIds.Count > 0 &&
                    flow.ConsumerComponentIds.All(consumer => !string.IsNullOrWhiteSpace(consumer)) &&
                    flow.ConsumerComponentIds.Distinct(StringComparer.Ordinal).Count() == flow.ConsumerComponentIds.Count,
                issues, "InvalidFlowConsumers", "$.communicationFlows", "Communication consumers must be non-empty and unique.", flow.FlowId);
            foreach (var consumer in flow.ConsumerComponentIds)
            {
                Require(componentIds.Contains(consumer), issues, "FlowConsumerNotInCapabilitySnapshot", "$.communicationFlows", $"Flow consumer '{consumer}' is not present in the frozen capability snapshot.", flow.FlowId);
            }
            Require(flow.BranchComponentIds.All(componentIds.Contains) &&
                    flow.BranchComponentIds.Distinct(StringComparer.Ordinal).Count() == flow.BranchComponentIds.Count,
                issues, "InvalidFlowBranches", "$.communicationFlows", "Communication branch components must be unique members of the frozen capability snapshot.", flow.FlowId);
            Require(flow.ConsumerRoutes.Select(route => route.ConsumerComponentId).SequenceEqual(flow.ConsumerComponentIds), issues,
                "FlowConsumerRouteMismatch", "$.communicationFlows", "Every canonical flow consumer must have exactly one explicit route binding.", flow.FlowId);
            foreach (var route in flow.ConsumerRoutes)
            {
                Require(!string.IsNullOrWhiteSpace(route.RoutePathId) &&
                        route.LinkIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
                        route.LinkIds.Distinct(StringComparer.Ordinal).Count() == route.LinkIds.Count,
                    issues, "InvalidFlowRoute", "$.communicationFlows.consumerRoutes",
                    "Every consumer route requires a stable path id and unique non-empty ordered link ids.", flow.FlowId);
                Require(string.Equals(flow.ProducerComponentId, route.ConsumerComponentId, StringComparison.Ordinal) ||
                        route.LinkIds.Count > 0,
                    issues, "MissingRemoteFlowRouteHops", "$.communicationFlows.consumerRoutes",
                    "Only a same-component communication route may contain zero logical hops.", flow.FlowId);
            }
        }

        foreach (var collective in mapping.CollectivePlans)
        {
            Require(componentIds.Contains(collective.TargetComponentId), issues,
                "CollectiveTargetNotInCapabilitySnapshot", "$.collectivePlans", $"Collective target '{collective.TargetComponentId}' is not present in the frozen capability snapshot.", collective.CollectiveId);
            Require(!string.IsNullOrWhiteSpace(collective.CollectiveKindId) &&
                    !string.IsNullOrWhiteSpace(collective.OutputTileId) &&
                    !string.IsNullOrWhiteSpace(collective.OrderPolicyId) &&
                    !string.IsNullOrWhiteSpace(collective.DataTypeId) &&
                    !string.IsNullOrWhiteSpace(collective.GroupKey) &&
                    !string.IsNullOrWhiteSpace(collective.ErrorBehavior.DuplicateContributor) &&
                    !string.IsNullOrWhiteSpace(collective.ErrorBehavior.MissingContributor) &&
                    !string.IsNullOrWhiteSpace(collective.ErrorBehavior.ShapeMismatch) &&
                    !string.IsNullOrWhiteSpace(collective.ErrorBehavior.RangeMismatch) &&
                    !string.IsNullOrWhiteSpace(collective.ErrorBehavior.PrecisionMismatch),
                issues, "IncompleteCollectiveContract", "$.collectivePlans",
                "Collective kind, output, ordering, data type, group, and explicit error-behavior identities are required.", collective.CollectiveId);
            Require(collective.ContributorIds.Count > 0 && collective.ContributorIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
                    collective.ContributorIds.Distinct(StringComparer.Ordinal).Count() == collective.ContributorIds.Count,
                issues, "InvalidCollectiveContributors", "$.collectivePlans", "Collective contributors must be non-empty and unique while preserving declared order.", collective.CollectiveId);
        }
    }

    private static void ValidateCandidateAndProvenance(WorkloadMappingV2 mapping, List<WorkloadMappingV2Issue> issues)
    {
        var candidate = mapping.Candidate;
        Require(!string.IsNullOrWhiteSpace(candidate.CandidateId) &&
                !string.IsNullOrWhiteSpace(candidate.PolicyId) &&
                !string.IsNullOrWhiteSpace(candidate.PolicyConfigHash) &&
                !string.IsNullOrWhiteSpace(candidate.TieBreakKey),
            issues, "IncompleteMappingCandidate", "$.candidate",
            "Candidate, policy, policy-config, and tie-break identities are required.", candidate.CandidateId);
        if (string.Equals(mapping.ModeId, WorkloadMappingV2Modes.TopologyAware, StringComparison.Ordinal))
        {
            Require(!string.IsNullOrWhiteSpace(candidate.TopologyHash) &&
                    !string.IsNullOrWhiteSpace(candidate.RouteHash) &&
                    !string.IsNullOrWhiteSpace(candidate.ProfileHash),
                issues, "IncompleteMappingCandidateAuthority", "$.candidate",
                "Topology-aware candidates require topology, route, and profile authority hashes.", candidate.CandidateId);
        }
        else if (string.Equals(mapping.ModeId, WorkloadMappingV2Modes.LegacyCompatibility, StringComparison.Ordinal))
        {
            Require(string.IsNullOrEmpty(candidate.TopologyHash) &&
                    string.IsNullOrEmpty(candidate.RouteHash) &&
                    string.IsNullOrEmpty(candidate.ProfileHash),
                issues, "LegacyCompatibilityInventedCandidateAuthority", "$.candidate",
                "Legacy compatibility candidates cannot invent topology, route, or profile authority hashes.", candidate.CandidateId);
        }
        ValidateUnique(candidate.ScoreBreakdown.Select(item => item.MetricId), "$.candidate.scoreBreakdown", "DuplicateCandidateScoreMetricId", issues);
        decimal total = 0m;
        foreach (var item in candidate.ScoreBreakdown)
        {
            Require(!string.IsNullOrWhiteSpace(item.UnitId) && !string.IsNullOrWhiteSpace(item.SourceId) && item.Weight >= 0m,
                issues, "InvalidCandidateScoreContract", "$.candidate.scoreBreakdown",
                "Candidate score metrics require stable metric, unit, and source identities plus a non-negative weight.", item.MetricId);
            try
            {
                Require(item.WeightedValue == checked(item.Value * item.Weight), issues,
                    "CandidateWeightedScoreMismatch", "$.candidate.scoreBreakdown",
                    "Candidate weighted value must exactly equal value multiplied by weight.", item.MetricId);
                total = checked(total + item.WeightedValue);
            }
            catch (OverflowException)
            {
                issues.Add(new WorkloadMappingV2Issue(
                    "CandidateScoreArithmeticOverflow", ValidationSeverity.Error, "$.candidate.scoreBreakdown",
                    "Candidate score multiplication or accumulation exceeds supported decimal bounds.", item.MetricId));
            }
        }
        ValidateUnique(candidate.ManualDiff.Select(item => item.Path), "$.candidate.manualDiff", "DuplicateCandidateManualDiffPath", issues);
        foreach (var item in candidate.ManualDiff)
        {
            Require(!string.IsNullOrWhiteSpace(item.ReasonCode), issues,
                "InvalidCandidateManualDiff", "$.candidate.manualDiff",
                "Candidate manual differences require a stable path and reason code.", item.Path);
        }
        foreach (var issue in candidate.Issues)
        {
            Require(!string.IsNullOrWhiteSpace(issue.Code) && !string.IsNullOrWhiteSpace(issue.Location) && !string.IsNullOrWhiteSpace(issue.Message),
                issues, "InvalidCandidateIssue", "$.candidate.issues",
                "Candidate diagnostics require code, location, and message.", issue.RelatedId);
        }

        Require(!string.IsNullOrWhiteSpace(mapping.Provenance.WorkloadHash) &&
                !string.IsNullOrWhiteSpace(mapping.Provenance.NormalizedInputHash) &&
                !string.IsNullOrWhiteSpace(mapping.Provenance.CompilerVersion),
            issues, "IncompleteMappingProvenance", "$.provenance",
            "Mapping provenance requires workload hash, normalized-input hash, and compiler version.");
    }

    private static void ValidateRange(MappingIndexRange range, string name, string relatedId, List<WorkloadMappingV2Issue> issues) =>
        Require(range.Offset >= 0 && range.Extent > 0 && range.Offset <= long.MaxValue - range.Extent,
            issues, "InvalidAssignmentRange", "$.operationTileAssignments",
            $"Assignment {name} offset must be non-negative, extent must be positive, and the half-open range must not overflow.", relatedId);

    private static void ValidateUnique(IEnumerable<string> ids, string location, string code, List<WorkloadMappingV2Issue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                issues.Add(new WorkloadMappingV2Issue(code, ValidationSeverity.Error, location, $"Identifier '{id}' must be non-empty and unique.", id));
            }
        }
    }

    private static bool IsLowerSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Require(
        bool condition,
        List<WorkloadMappingV2Issue> issues,
        string code,
        string location,
        string message,
        string? relatedId = null)
    {
        if (!condition)
        {
            issues.Add(new WorkloadMappingV2Issue(code, ValidationSeverity.Error, location, message, relatedId));
        }
    }
}
