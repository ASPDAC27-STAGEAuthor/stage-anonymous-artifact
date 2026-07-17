using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace HardwareSim.Core;

/// <summary>Provides stable mode identifiers for WorkloadMapping 2.0 documents.</summary>
public static class WorkloadMappingV2Modes
{
    /// <summary>Identifies a native topology-aware mapping document.</summary>
    public const string TopologyAware = "topology-aware-v1";
    /// <summary>Identifies a lossless compatibility envelope migrated from WorkloadMapping 1.0.</summary>
    public const string LegacyCompatibility = "legacy-compat-v1";
}

/// <summary>Provides the exact supported communication-flow kind identifiers.</summary>
public static class Phase8ACommunicationFlowKinds
{
    /// <summary>Identifies one producer-to-one-consumer delivery without a branch.</summary>
    public const string Unicast = "unicast";
    /// <summary>Identifies one shared payload tree delivered to selected consumers.</summary>
    public const string Multicast = "multicast";
    /// <summary>Identifies one shared payload tree delivered to every declared consumer.</summary>
    public const string Broadcast = "broadcast";
}

/// <summary>Describes one structured WorkloadMapping 2.0 diagnostic.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Issue severity.</param>
/// <param name="Location">JSON-style issue location.</param>
/// <param name="Message">Human-readable issue explanation.</param>
/// <param name="RelatedId">Optional related mapping identifier.</param>
/// <param name="Suggestion">Optional corrective action.</param>
public sealed record WorkloadMappingV2Issue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null,
    string? Suggestion = null);

/// <summary>Defines one offset and extent along a tensor dimension.</summary>
/// <param name="Offset">Zero-based tensor offset.</param>
/// <param name="Extent">Number of valid elements covered by the range.</param>
public sealed record MappingIndexRange(long Offset, long Extent);

/// <summary>Defines an immutable tensor shape.</summary>
public sealed class MappingShape
{
    /// <summary>Creates an immutable tensor shape.</summary>
    /// <param name="dimensions">Tensor dimensions in semantic order.</param>
    [JsonConstructor]
    public MappingShape(IReadOnlyList<long>? dimensions) => Dimensions = MappingV2Freeze.List(dimensions);

    /// <summary>Gets tensor dimensions in semantic order.</summary>
    public IReadOnlyList<long> Dimensions { get; }
}

/// <summary>Describes one immutable capability port.</summary>
/// <param name="PortId">Stable port identifier.</param>
/// <param name="DirectionId">Stable direction identifier.</param>
/// <param name="ProtocolId">Stable protocol identifier.</param>
/// <param name="DomainId">Stable signal-domain identifier.</param>
/// <param name="BandwidthBitsPerCycle">Available transport bandwidth in bits per cycle.</param>
/// <param name="SemanticRoleId">Exact semantic role declared by the compiled execution contract, or an empty string for an unclassified custom non-MatMul port.</param>
/// <param name="DataTypeId">Exact typed data contract exposed by the compiled port.</param>
/// <param name="PrecisionId">Exact precision exposed by the compiled port.</param>
public sealed record CapabilityPortSnapshot(
    string PortId,
    string DirectionId,
    string ProtocolId,
    string DomainId,
    long BandwidthBitsPerCycle,
    string SemanticRoleId,
    string DataTypeId = "",
    string PrecisionId = "");

/// <summary>Describes one immutable storage resource exposed by a compiled component capability.</summary>
public sealed class ComponentStorageCapabilitySnapshot
{
    /// <summary>Creates an immutable component storage capability snapshot.</summary>
    [JsonConstructor]
    public ComponentStorageCapabilitySnapshot(
        string resourceId,
        string storageLevelId,
        IReadOnlyList<string>? supportedOperandRoleIds,
        IReadOnlyList<string>? supportedPrecisionIds,
        long capacityBits,
        long alignmentBits,
        long allocationGranularityBits,
        int residentSlots,
        string preloadPortId,
        long readBandwidthBitsPerCycle,
        long writeBandwidthBitsPerCycle,
        long readLatencyCycles,
        long writeLatencyCycles,
        string commitModeId,
        bool supportsStreaming,
        bool supportsReuse,
        string sourceContractHash)
    {
        ResourceId = resourceId ?? "";
        StorageLevelId = storageLevelId ?? "";
        SupportedOperandRoleIds = MappingV2Freeze.SortedStrings(supportedOperandRoleIds);
        SupportedPrecisionIds = MappingV2Freeze.SortedStrings(supportedPrecisionIds);
        CapacityBits = capacityBits;
        AlignmentBits = alignmentBits;
        AllocationGranularityBits = allocationGranularityBits;
        ResidentSlots = residentSlots;
        PreloadPortId = preloadPortId ?? "";
        ReadBandwidthBitsPerCycle = readBandwidthBitsPerCycle;
        WriteBandwidthBitsPerCycle = writeBandwidthBitsPerCycle;
        ReadLatencyCycles = readLatencyCycles;
        WriteLatencyCycles = writeLatencyCycles;
        CommitModeId = commitModeId ?? "";
        SupportsStreaming = supportsStreaming;
        SupportsReuse = supportsReuse;
        SourceContractHash = sourceContractHash ?? "";
    }

    /// <summary>Gets the stable storage resource identifier.</summary>
    public string ResourceId { get; }
    /// <summary>Gets the storage hierarchy level identifier.</summary>
    public string StorageLevelId { get; }
    /// <summary>Gets supported operand role identifiers in canonical order.</summary>
    public IReadOnlyList<string> SupportedOperandRoleIds { get; }
    /// <summary>Gets supported precision identifiers in canonical order.</summary>
    public IReadOnlyList<string> SupportedPrecisionIds { get; }
    /// <summary>Gets usable storage capacity in bits.</summary>
    public long CapacityBits { get; }
    /// <summary>Gets required allocation alignment in bits.</summary>
    public long AlignmentBits { get; }
    /// <summary>Gets allocation granularity in bits.</summary>
    public long AllocationGranularityBits { get; }
    /// <summary>Gets the number of independently resident operand slots.</summary>
    public int ResidentSlots { get; }
    /// <summary>Gets the explicit preload input port identifier.</summary>
    public string PreloadPortId { get; }
    /// <summary>Gets sustained read bandwidth in bits per cycle.</summary>
    public long ReadBandwidthBitsPerCycle { get; }
    /// <summary>Gets sustained write bandwidth in bits per cycle.</summary>
    public long WriteBandwidthBitsPerCycle { get; }
    /// <summary>Gets read latency in cycles.</summary>
    public long ReadLatencyCycles { get; }
    /// <summary>Gets write latency in cycles.</summary>
    public long WriteLatencyCycles { get; }
    /// <summary>Gets the stable weight or operand commit mode identifier.</summary>
    public string CommitModeId { get; }
    /// <summary>Gets whether this resource supports streaming operands.</summary>
    public bool SupportsStreaming { get; }
    /// <summary>Gets whether this resource supports resident operand reuse.</summary>
    public bool SupportsReuse { get; }
    /// <summary>Gets the exact compiled storage contract hash.</summary>
    public string SourceContractHash { get; }
}

/// <summary>Describes frozen capabilities for one compiled component.</summary>
public sealed class ComponentCapabilitySnapshot
{
    /// <summary>Creates an immutable component capability snapshot.</summary>
    [JsonConstructor]
    public ComponentCapabilitySnapshot(
        string componentId,
        string stableTypeId,
        string templateId,
        string templateHash,
        string profileId,
        string profileHash,
        string kernelId,
        string kernelHash,
        IReadOnlyList<string>? operationKindIds,
        IReadOnlyDictionary<string, string>? shapeContracts,
        IReadOnlyList<string>? precisionIds,
        long capacityBits,
        long latencyCycles,
        long bandwidthBitsPerCycle,
        IReadOnlyList<CapabilityPortSnapshot>? ports,
        string domainId,
        IReadOnlyList<ComponentStorageCapabilitySnapshot>? storageCapabilities = null,
        double? dynamicEnergyPicojoules = null,
        double? footprintAreaUm2 = null,
        double? footprintWidthUm = null,
        double? footprintHeightUm = null,
        string physicalFootprintHash = "",
        string physicalFootprintScope = "",
        string physicalFootprintSourceKind = "",
        string physicalFootprintUncertainty = "",
        IReadOnlyDictionary<string, string>? deviceProfileHashes = null)
    {
        ComponentId = componentId ?? "";
        StableTypeId = stableTypeId ?? "";
        TemplateId = templateId ?? "";
        TemplateHash = templateHash ?? "";
        ProfileId = profileId ?? "";
        ProfileHash = profileHash ?? "";
        KernelId = kernelId ?? "";
        KernelHash = kernelHash ?? "";
        OperationKindIds = MappingV2Freeze.SortedStrings(operationKindIds);
        ShapeContracts = MappingV2Freeze.StringMap(shapeContracts);
        PrecisionIds = MappingV2Freeze.SortedStrings(precisionIds);
        CapacityBits = capacityBits;
        LatencyCycles = latencyCycles;
        BandwidthBitsPerCycle = bandwidthBitsPerCycle;
        Ports = MappingV2Freeze.Sorted(ports, port => port.PortId);
        DomainId = domainId ?? "";
        StorageCapabilities = MappingV2Freeze.Sorted(storageCapabilities, storage => storage.ResourceId);
        DynamicEnergyPicojoules = dynamicEnergyPicojoules;
        FootprintAreaUm2 = footprintAreaUm2;
        FootprintWidthUm = footprintWidthUm;
        FootprintHeightUm = footprintHeightUm;
        PhysicalFootprintHash = physicalFootprintHash ?? "";
        PhysicalFootprintScope = physicalFootprintScope ?? "";
        PhysicalFootprintSourceKind = physicalFootprintSourceKind ?? "";
        PhysicalFootprintUncertainty = physicalFootprintUncertainty ?? "";
        DeviceProfileHashes = MappingV2Freeze.StringMap(deviceProfileHashes);
    }

    /// <summary>Gets the stable component identifier.</summary>
    public string ComponentId { get; }
    /// <summary>Gets the stable component type identifier.</summary>
    public string StableTypeId { get; }
    /// <summary>Gets the source template identifier.</summary>
    public string TemplateId { get; }
    /// <summary>Gets the exact source template hash.</summary>
    public string TemplateHash { get; }
    /// <summary>Gets the compiled profile identifier.</summary>
    public string ProfileId { get; }
    /// <summary>Gets the exact compiled profile hash.</summary>
    public string ProfileHash { get; }
    /// <summary>Gets the registered runtime kernel identifier.</summary>
    public string KernelId { get; }
    /// <summary>Gets the exact runtime kernel hash.</summary>
    public string KernelHash { get; }
    /// <summary>Gets supported stable operation-kind identifiers.</summary>
    public IReadOnlyList<string> OperationKindIds { get; }
    /// <summary>Gets named shape contracts in canonical key order.</summary>
    public IReadOnlyDictionary<string, string> ShapeContracts { get; }
    /// <summary>Gets supported stable precision identifiers.</summary>
    public IReadOnlyList<string> PrecisionIds { get; }
    /// <summary>Gets usable capacity in bits.</summary>
    public long CapacityBits { get; }
    /// <summary>Gets fixed capability latency in cycles.</summary>
    public long LatencyCycles { get; }
    /// <summary>Gets capability bandwidth in bits per cycle.</summary>
    public long BandwidthBitsPerCycle { get; }
    /// <summary>Gets immutable compiled port capabilities.</summary>
    public IReadOnlyList<CapabilityPortSnapshot> Ports { get; }
    /// <summary>Gets the component signal or execution domain identifier.</summary>
    public string DomainId { get; }
    /// <summary>Gets typed storage resources sorted by stable resource identifier.</summary>
    public IReadOnlyList<ComponentStorageCapabilitySnapshot> StorageCapabilities { get; }
    /// <summary>Gets the compiled operation dynamic energy when every included term is known.</summary>
    public double? DynamicEnergyPicojoules { get; }
    /// <summary>Gets the compiled continuous footprint area without placement-cell quantization.</summary>
    public double? FootprintAreaUm2 { get; }
    /// <summary>Gets the compiled continuous footprint width.</summary>
    public double? FootprintWidthUm { get; }
    /// <summary>Gets the compiled continuous footprint height.</summary>
    public double? FootprintHeightUm { get; }
    /// <summary>Gets the exact compiled physical-footprint hash.</summary>
    public string PhysicalFootprintHash { get; }
    /// <summary>Gets the physical area scope such as Macro or Array.</summary>
    public string PhysicalFootprintScope { get; }
    /// <summary>Gets the footprint evidence/derivation source kind.</summary>
    public string PhysicalFootprintSourceKind { get; }
    /// <summary>Gets the explicit footprint uncertainty statement.</summary>
    public string PhysicalFootprintUncertainty { get; }
    /// <summary>Gets every normalized device/profile snapshot hash bound to the runtime contract.</summary>
    public IReadOnlyDictionary<string, string> DeviceProfileHashes { get; }
}

/// <summary>Freezes all component capabilities used to construct a mapping candidate.</summary>
public sealed class CapabilitySnapshot
{
    /// <summary>Creates an immutable capability snapshot.</summary>
    [JsonConstructor]
    public CapabilitySnapshot(
        string snapshotId,
        string hardwareGraphHash,
        string placementHash,
        string routeHash,
        string registryHash,
        IReadOnlyList<ComponentCapabilitySnapshot>? components)
    {
        SnapshotId = snapshotId ?? "";
        HardwareGraphHash = hardwareGraphHash ?? "";
        PlacementHash = placementHash ?? "";
        RouteHash = routeHash ?? "";
        RegistryHash = registryHash ?? "";
        Components = MappingV2Freeze.Sorted(components, component => component.ComponentId);
    }

    /// <summary>Gets the stable capability snapshot identifier.</summary>
    public string SnapshotId { get; }
    /// <summary>Gets the frozen hardware graph hash.</summary>
    public string HardwareGraphHash { get; }
    /// <summary>Gets the frozen physical placement hash.</summary>
    public string PlacementHash { get; }
    /// <summary>Gets the frozen route-set hash.</summary>
    public string RouteHash { get; }
    /// <summary>Gets the frozen plugin and kernel registry hash.</summary>
    public string RegistryHash { get; }
    /// <summary>Gets component capabilities sorted by stable component identifier.</summary>
    public IReadOnlyList<ComponentCapabilitySnapshot> Components { get; }
}

/// <summary>Assigns one lowered operation tile to a concrete capable target.</summary>
public sealed class OperationTileAssignment
{
    /// <summary>Creates an immutable operation tile assignment.</summary>
    [JsonConstructor]
    public OperationTileAssignment(
        string assignmentId,
        string operationId,
        string tileId,
        string targetComponentId,
        string targetPortId,
        IReadOnlyList<string>? operandTileIds,
        MappingIndexRange mRange,
        MappingIndexRange kRange,
        MappingIndexRange nRange,
        MappingShape validShape,
        MappingShape paddedShape,
        string partitionPolicyId)
    {
        AssignmentId = assignmentId ?? "";
        OperationId = operationId ?? "";
        TileId = tileId ?? "";
        TargetComponentId = targetComponentId ?? "";
        TargetPortId = targetPortId ?? "";
        OperandTileIds = MappingV2Freeze.List(operandTileIds);
        MRange = mRange ?? new MappingIndexRange(0, 0);
        KRange = kRange ?? new MappingIndexRange(0, 0);
        NRange = nRange ?? new MappingIndexRange(0, 0);
        ValidShape = validShape ?? new MappingShape([]);
        PaddedShape = paddedShape ?? new MappingShape([]);
        PartitionPolicyId = partitionPolicyId ?? "";
    }

    /// <summary>Gets the stable assignment identifier.</summary>
    public string AssignmentId { get; }
    /// <summary>Gets the source workload operation identifier.</summary>
    public string OperationId { get; }
    /// <summary>Gets the lowered output tile identifier.</summary>
    public string TileId { get; }
    /// <summary>Gets the target component identifier.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the target port identifier.</summary>
    public string TargetPortId { get; }
    /// <summary>Gets operand tile identifiers in semantic operand order.</summary>
    public IReadOnlyList<string> OperandTileIds { get; }
    /// <summary>Gets the assigned M range.</summary>
    public MappingIndexRange MRange { get; }
    /// <summary>Gets the assigned K range.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the assigned N range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the unpadded valid tile shape.</summary>
    public MappingShape ValidShape { get; }
    /// <summary>Gets the physical padded tile shape.</summary>
    public MappingShape PaddedShape { get; }
    /// <summary>Gets the stable partition-policy identifier.</summary>
    public string PartitionPolicyId { get; }
}

/// <summary>Describes deterministic storage placement and lifecycle for one operand tile.</summary>
public sealed record OperandPlacement(
    string PlacementId,
    string OperationId,
    string TensorId,
    string TileId,
    string OperandRoleId,
    string StorageComponentId,
    string StorageLevelId,
    long AddressBits,
    long SizeBits,
    long AlignmentBits,
    string ResidencyModeId,
    string LoadModeId,
    string LifetimeStartId,
    string LifetimeEndId,
    string ReuseGroupId,
    bool CommitRequired);

/// <summary>Binds one flow consumer to an explicit logical route.</summary>
public sealed class CommunicationConsumerRoute
{
    /// <summary>Creates an immutable consumer route binding.</summary>
    [JsonConstructor]
    public CommunicationConsumerRoute(string consumerComponentId, string routePathId, IReadOnlyList<string>? linkIds)
    {
        ConsumerComponentId = consumerComponentId ?? "";
        RoutePathId = routePathId ?? "";
        LinkIds = MappingV2Freeze.List(linkIds);
    }

    /// <summary>Gets the consumer component identifier.</summary>
    public string ConsumerComponentId { get; }
    /// <summary>Gets the explicit RoutePath identifier.</summary>
    public string RoutePathId { get; }
    /// <summary>Gets directed logical link identifiers in execution order.</summary>
    public IReadOnlyList<string> LinkIds { get; }
}

/// <summary>Describes one typed unicast, multicast, or broadcast communication flow.</summary>
public sealed class CommunicationFlow
{
    /// <summary>Creates an immutable communication flow.</summary>
    [JsonConstructor]
    public CommunicationFlow(
        string flowId,
        string producerComponentId,
        IReadOnlyList<string>? consumerComponentIds,
        string tensorTileId,
        long bits,
        string flowKindId,
        IReadOnlyList<string>? branchComponentIds,
        IReadOnlyList<CommunicationConsumerRoute>? consumerRoutes)
    {
        FlowId = flowId ?? "";
        ProducerComponentId = producerComponentId ?? "";
        ConsumerComponentIds = MappingV2Freeze.SortedStrings(consumerComponentIds);
        TensorTileId = tensorTileId ?? "";
        Bits = bits;
        FlowKindId = flowKindId ?? "";
        BranchComponentIds = MappingV2Freeze.List(branchComponentIds);
        ConsumerRoutes = MappingV2Freeze.Sorted(consumerRoutes, route => route.ConsumerComponentId);
    }

    /// <summary>Gets the stable flow identifier.</summary>
    public string FlowId { get; }
    /// <summary>Gets the producer component identifier.</summary>
    public string ProducerComponentId { get; }
    /// <summary>Gets the canonical consumer component set.</summary>
    public IReadOnlyList<string> ConsumerComponentIds { get; }
    /// <summary>Gets the tensor tile transferred by the flow.</summary>
    public string TensorTileId { get; }
    /// <summary>Gets transferred payload size in bits.</summary>
    public long Bits { get; }
    /// <summary>Gets the stable flow-kind identifier.</summary>
    public string FlowKindId { get; }
    /// <summary>Gets branch components in declared traversal order.</summary>
    public IReadOnlyList<string> BranchComponentIds { get; }
    /// <summary>Gets explicit per-consumer route bindings.</summary>
    public IReadOnlyList<CommunicationConsumerRoute> ConsumerRoutes { get; }
}

/// <summary>Defines structured collective error behavior.</summary>
/// <param name="DuplicateContributor">Behavior for duplicate contributors.</param>
/// <param name="MissingContributor">Behavior for missing contributors.</param>
/// <param name="ShapeMismatch">Behavior for shape mismatch.</param>
/// <param name="RangeMismatch">Behavior for tensor-range mismatch.</param>
/// <param name="PrecisionMismatch">Behavior for precision mismatch.</param>
public sealed record CollectiveErrorBehavior(
    string DuplicateContributor,
    string MissingContributor,
    string ShapeMismatch,
    string RangeMismatch,
    string PrecisionMismatch);

/// <summary>Describes one ordered Sum, Concat, or Deduplicate collective.</summary>
public sealed class CollectivePlan
{
    /// <summary>Creates an immutable collective plan.</summary>
    [JsonConstructor]
    public CollectivePlan(
        string collectiveId,
        string collectiveKindId,
        IReadOnlyList<string>? contributorIds,
        string targetComponentId,
        string outputTileId,
        string orderPolicyId,
        string dataTypeId,
        string groupKey,
        CollectiveErrorBehavior errorBehavior)
    {
        CollectiveId = collectiveId ?? "";
        CollectiveKindId = collectiveKindId ?? "";
        ContributorIds = MappingV2Freeze.List(contributorIds);
        TargetComponentId = targetComponentId ?? "";
        OutputTileId = outputTileId ?? "";
        OrderPolicyId = orderPolicyId ?? "";
        DataTypeId = dataTypeId ?? "";
        GroupKey = groupKey ?? "";
        ErrorBehavior = errorBehavior ?? new CollectiveErrorBehavior("error", "error", "error", "error", "error");
    }

    /// <summary>Gets the stable collective identifier.</summary>
    public string CollectiveId { get; }
    /// <summary>Gets the stable collective-kind identifier.</summary>
    public string CollectiveKindId { get; }
    /// <summary>Gets contributor identifiers in deterministic accumulation or assembly order.</summary>
    public IReadOnlyList<string> ContributorIds { get; }
    /// <summary>Gets the target collective component identifier.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the collective output tile identifier.</summary>
    public string OutputTileId { get; }
    /// <summary>Gets the stable order-policy identifier.</summary>
    public string OrderPolicyId { get; }
    /// <summary>Gets the stable data-type identifier.</summary>
    public string DataTypeId { get; }
    /// <summary>Gets the exact collective isolation group key.</summary>
    public string GroupKey { get; }
    /// <summary>Gets structured collective error behavior.</summary>
    public CollectiveErrorBehavior ErrorBehavior { get; }
}

/// <summary>Defines one deterministic mapping score contribution.</summary>
/// <param name="MetricId">Stable metric identifier.</param>
/// <param name="Value">Raw metric value.</param>
/// <param name="Weight">Configured metric weight.</param>
/// <param name="WeightedValue">Deterministic weighted contribution.</param>
/// <param name="UnitId">Stable metric unit identifier.</param>
/// <param name="SourceId">Stable score-source identifier.</param>
public sealed record MappingScoreItem(
    string MetricId,
    decimal Value,
    decimal Weight,
    decimal WeightedValue,
    string UnitId,
    string SourceId);

/// <summary>Defines one proposed manual mapping difference.</summary>
/// <param name="Path">Canonical mapping field path.</param>
/// <param name="BeforeValue">Canonical value before applying the candidate.</param>
/// <param name="AfterValue">Canonical value after applying the candidate.</param>
/// <param name="ReasonCode">Stable reason code.</param>
public sealed record MappingManualDiffItem(string Path, string BeforeValue, string AfterValue, string ReasonCode);

/// <summary>Captures policy, diagnostics, scores, hashes, and manual differences for one candidate.</summary>
public sealed class MappingCandidate
{
    /// <summary>Creates an immutable mapping candidate descriptor.</summary>
    [JsonConstructor]
    public MappingCandidate(
        string candidateId,
        string policyId,
        string policyConfigHash,
        IReadOnlyList<WorkloadMappingV2Issue>? issues,
        IReadOnlyList<MappingScoreItem>? scoreBreakdown,
        string topologyHash,
        string routeHash,
        string profileHash,
        string tieBreakKey,
        IReadOnlyList<MappingManualDiffItem>? manualDiff)
    {
        CandidateId = candidateId ?? "";
        PolicyId = policyId ?? "";
        PolicyConfigHash = policyConfigHash ?? "";
        Issues = MappingV2Freeze.Sorted(issues, issue => $"{issue.Location}\u001f{issue.Code}\u001f{issue.RelatedId}");
        ScoreBreakdown = MappingV2Freeze.Sorted(scoreBreakdown, item => item.MetricId);
        TopologyHash = topologyHash ?? "";
        RouteHash = routeHash ?? "";
        ProfileHash = profileHash ?? "";
        TieBreakKey = tieBreakKey ?? "";
        ManualDiff = MappingV2Freeze.Sorted(manualDiff, item => $"{item.Path}\u001f{item.ReasonCode}");
    }

    /// <summary>Gets the stable candidate identifier.</summary>
    public string CandidateId { get; }
    /// <summary>Gets the stable mapping policy identifier and version.</summary>
    public string PolicyId { get; }
    /// <summary>Gets the exact normalized policy configuration hash.</summary>
    public string PolicyConfigHash { get; }
    /// <summary>Gets structured candidate issues in canonical order.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    /// <summary>Gets score contributions in canonical metric order.</summary>
    public IReadOnlyList<MappingScoreItem> ScoreBreakdown { get; }
    /// <summary>Gets the topology snapshot hash used by the candidate.</summary>
    public string TopologyHash { get; }
    /// <summary>Gets the route snapshot hash used by the candidate.</summary>
    public string RouteHash { get; }
    /// <summary>Gets the compiled profile snapshot hash used by the candidate.</summary>
    public string ProfileHash { get; }
    /// <summary>Gets the deterministic final tie-break key.</summary>
    public string TieBreakKey { get; }
    /// <summary>Gets manual proposal differences in canonical path order.</summary>
    public IReadOnlyList<MappingManualDiffItem> ManualDiff { get; }
}

/// <summary>Records immutable mapping-source provenance.</summary>
/// <param name="WorkloadHash">Normalized workload hash.</param>
/// <param name="NormalizedInputHash">Normalized mapping input hash.</param>
/// <param name="CompilerVersion">Mapping compiler version.</param>
/// <param name="DeterministicSeed">Explicit deterministic seed.</param>
public sealed record WorkloadMappingV2Provenance(
    string WorkloadHash,
    string NormalizedInputHash,
    string CompilerVersion,
    int DeterministicSeed);

/// <summary>Captures one immutable legacy operation-to-component entry.</summary>
public sealed class LegacyMappingEntrySnapshot
{
    /// <summary>Creates an immutable legacy mapping entry.</summary>
    [JsonConstructor]
    public LegacyMappingEntrySnapshot(
        string workloadOpId,
        string targetComponentId,
        string targetPort,
        IReadOnlyDictionary<string, string>? scheduleHints)
    {
        WorkloadOpId = workloadOpId ?? "";
        TargetComponentId = targetComponentId ?? "";
        TargetPort = targetPort ?? "";
        ScheduleHints = MappingV2Freeze.StringMap(scheduleHints);
    }

    /// <summary>Gets the legacy workload operation identifier.</summary>
    public string WorkloadOpId { get; }
    /// <summary>Gets the legacy target component identifier.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the legacy target port name.</summary>
    public string TargetPort { get; }
    /// <summary>Gets legacy schedule hints in canonical key order.</summary>
    public IReadOnlyDictionary<string, string> ScheduleHints { get; }
}

/// <summary>Captures one immutable legacy tensor placement.</summary>
public sealed record LegacyTensorPlacementSnapshot(
    string TensorId,
    string TileId,
    string StorageComponentId,
    string StorageLevel,
    string AddressHint);

/// <summary>Captures one immutable legacy route hint without promoting it to an executable flow.</summary>
public sealed class LegacyRouteHintSnapshot
{
    /// <summary>Creates an immutable legacy route hint.</summary>
    [JsonConstructor]
    public LegacyRouteHintSnapshot(string linkId, IReadOnlyList<string>? preferredPath, string priority)
    {
        LinkId = linkId ?? "";
        PreferredPath = MappingV2Freeze.List(preferredPath);
        Priority = priority ?? "";
    }

    /// <summary>Gets the legacy link identifier.</summary>
    public string LinkId { get; }
    /// <summary>Gets the ambiguous legacy preferred-path values in original order.</summary>
    public IReadOnlyList<string> PreferredPath { get; }
    /// <summary>Gets the legacy priority label.</summary>
    public string Priority { get; }
}

/// <summary>Preserves normalized WorkloadMapping 1.0 content without inventing Mapping 2.0 semantics.</summary>
public sealed class LegacyWorkloadMappingSnapshot
{
    /// <summary>Creates an immutable legacy mapping compatibility snapshot.</summary>
    [JsonConstructor]
    public LegacyWorkloadMappingSnapshot(
        string sourceSchemaVersion,
        IReadOnlyList<LegacyMappingEntrySnapshot>? entries,
        IReadOnlyList<LegacyTensorPlacementSnapshot>? placements,
        IReadOnlyList<LegacyRouteHintSnapshot>? routeHints)
    {
        SourceSchemaVersion = sourceSchemaVersion ?? "";
        Entries = MappingV2Freeze.List(entries);
        Placements = MappingV2Freeze.List(placements);
        RouteHints = MappingV2Freeze.List(routeHints);
    }

    /// <summary>Gets the migrated source schema version.</summary>
    public string SourceSchemaVersion { get; }
    /// <summary>Gets legacy mapping entries in source order.</summary>
    public IReadOnlyList<LegacyMappingEntrySnapshot> Entries { get; }
    /// <summary>Gets legacy tensor placements in source order.</summary>
    public IReadOnlyList<LegacyTensorPlacementSnapshot> Placements { get; }
    /// <summary>Gets legacy route hints in source order.</summary>
    public IReadOnlyList<LegacyRouteHintSnapshot> RouteHints { get; }
}

/// <summary>Represents an immutable WorkloadMapping 2.0 document.</summary>
public sealed class WorkloadMappingV2
{
    /// <summary>Defines the first topology-aware mapping schema version.</summary>
    public const string CurrentSchemaVersion = "2.0";
    /// <summary>Defines the mapping canonical-hash algorithm and projection version.</summary>
    public const string CurrentCanonicalHashAlgorithm = "sha256/workload-mapping-2.0/v1";

    /// <summary>Creates an immutable WorkloadMapping 2.0 document.</summary>
    [JsonConstructor]
    public WorkloadMappingV2(
        string schemaVersion,
        string mappingId,
        string modeId,
        CapabilitySnapshot capabilitySnapshot,
        IReadOnlyList<OperationTileAssignment>? operationTileAssignments,
        IReadOnlyList<OperandPlacement>? operandPlacements,
        IReadOnlyList<CommunicationFlow>? communicationFlows,
        IReadOnlyList<CollectivePlan>? collectivePlans,
        MappingCandidate candidate,
        WorkloadMappingV2Provenance provenance,
        LegacyWorkloadMappingSnapshot? legacyCompatibilitySnapshot,
        string canonicalHashAlgorithm,
        string canonicalHash)
    {
        SchemaVersion = schemaVersion ?? "";
        MappingId = mappingId ?? "";
        ModeId = modeId ?? "";
        CapabilitySnapshot = capabilitySnapshot ?? EmptyCapabilitySnapshot();
        OperationTileAssignments = MappingV2Freeze.Sorted(operationTileAssignments, item => item.AssignmentId);
        OperandPlacements = MappingV2Freeze.Sorted(operandPlacements, item => item.PlacementId);
        CommunicationFlows = MappingV2Freeze.Sorted(communicationFlows, item => item.FlowId);
        CollectivePlans = MappingV2Freeze.Sorted(collectivePlans, item => item.CollectiveId);
        Candidate = candidate ?? EmptyCandidate();
        Provenance = provenance ?? new WorkloadMappingV2Provenance("", "", "", 0);
        LegacyCompatibilitySnapshot = legacyCompatibilitySnapshot;
        CanonicalHashAlgorithm = canonicalHashAlgorithm ?? "";
        CanonicalHash = canonicalHash ?? "";
    }

    /// <summary>Gets the exact mapping schema version.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; }
    /// <summary>Gets the stable mapping document identifier.</summary>
    public string MappingId { get; }
    /// <summary>Gets the native or compatibility mapping mode identifier.</summary>
    public string ModeId { get; }
    /// <summary>Gets the frozen component capability snapshot.</summary>
    public CapabilitySnapshot CapabilitySnapshot { get; }
    /// <summary>Gets operation tile assignments in canonical identifier order.</summary>
    public IReadOnlyList<OperationTileAssignment> OperationTileAssignments { get; }
    /// <summary>Gets operand placements in canonical identifier order.</summary>
    public IReadOnlyList<OperandPlacement> OperandPlacements { get; }
    /// <summary>Gets communication flows in canonical identifier order.</summary>
    public IReadOnlyList<CommunicationFlow> CommunicationFlows { get; }
    /// <summary>Gets collective plans in canonical identifier order.</summary>
    public IReadOnlyList<CollectivePlan> CollectivePlans { get; }
    /// <summary>Gets mapping policy, score, diagnostics, and snapshot hashes.</summary>
    public MappingCandidate Candidate { get; }
    /// <summary>Gets immutable normalized-input provenance.</summary>
    public WorkloadMappingV2Provenance Provenance { get; }
    /// <summary>Gets lossless WorkloadMapping 1.0 content for explicit compatibility mode.</summary>
    public LegacyWorkloadMappingSnapshot? LegacyCompatibilitySnapshot { get; }
    /// <summary>Gets the declared canonical hash algorithm.</summary>
    public string CanonicalHashAlgorithm { get; }
    /// <summary>Gets the persisted semantic mapping hash.</summary>
    public string CanonicalHash { get; }

    /// <summary>Serializes this mapping with a verified canonical hash.</summary>
    public string ToJson() => WorkloadMappingV2Json.Serialize(this);

    /// <summary>Imports a current or migratable mapping document.</summary>
    public static WorkloadMappingV2ImportResult FromJson(string json) => WorkloadMappingV2Json.ImportToCurrent(json);

    internal WorkloadMappingV2 WithCanonicalHash(string canonicalHash) => new(
        SchemaVersion,
        MappingId,
        ModeId,
        CapabilitySnapshot,
        OperationTileAssignments,
        OperandPlacements,
        CommunicationFlows,
        CollectivePlans,
        Candidate,
        Provenance,
        LegacyCompatibilitySnapshot,
        CurrentCanonicalHashAlgorithm,
        canonicalHash);

    internal static CapabilitySnapshot EmptyCapabilitySnapshot() => new("unresolved", "", "", "", "", []);

    internal static MappingCandidate EmptyCandidate() => new("unresolved", "", "", [], [], "", "", "", "", []);
}

internal static class MappingV2Freeze
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T>? values)
    {
        var materialized = (values ?? Enumerable.Empty<T>()).ToList();
        if (materialized.Any(item => item is null))
            throw new ArgumentException("Immutable Mapping 2.0 collections cannot contain null entries.", nameof(values));
        return new ReadOnlyCollection<T>(materialized);
    }

    public static IReadOnlyList<T> Sorted<T>(IEnumerable<T>? values, Func<T, string> keySelector)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        var materialized = (values ?? Enumerable.Empty<T>()).ToList();
        if (materialized.Any(item => item is null))
            throw new ArgumentException("Immutable Mapping 2.0 collections cannot contain null entries.", nameof(values));
        return new ReadOnlyCollection<T>(materialized.OrderBy(keySelector, StringComparer.Ordinal).ToList());
    }

    public static IReadOnlyList<string> SortedStrings(IEnumerable<string>? values) =>
        new ReadOnlyCollection<string>((values ?? Enumerable.Empty<string>()).Select(value => value ?? "").OrderBy(value => value, StringComparer.Ordinal).ToList());

    public static IReadOnlyDictionary<string, string> StringMap(IReadOnlyDictionary<string, string>? values)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values ?? new Dictionary<string, string>())
        {
            copy[pair.Key ?? ""] = pair.Value ?? "";
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
