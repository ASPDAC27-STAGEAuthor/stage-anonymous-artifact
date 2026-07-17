using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Defines deterministic hard-failure codes for the Phase 8A mapping-problem boundary.</summary>
public static class Phase8AMappingProblemIssueCodes
{
    /// <summary>Identifies a malformed or incomplete mapping-problem request.</summary>
    public const string InvalidInput = "Phase8AMappingProblemInputInvalid";
    /// <summary>Identifies invalid normalized mapping input.</summary>
    public const string InvalidNormalizedInput = "Phase8AMappingProblemNormalizedInputInvalid";
    /// <summary>Identifies an invalid mapping-policy budget.</summary>
    public const string InvalidPolicyBudget = "Phase8AMappingProblemPolicyBudgetInvalid";
    /// <summary>Identifies an invalid Mapping 2.0 base mapping.</summary>
    public const string BaseMappingInvalid = "Phase8AMappingProblemBaseMappingInvalid";
    /// <summary>Identifies a mismatch between frozen authority hashes.</summary>
    public const string BaseHashMismatch = "Phase8AMappingProblemBaseHashMismatch";
    /// <summary>Identifies failed capability discovery.</summary>
    public const string CapabilityDiscoveryFailed = "Phase8AMappingProblemCapabilityDiscoveryFailed";
    /// <summary>Identifies a stale or mismatched capability snapshot.</summary>
    public const string CapabilitySnapshotMismatch = "Phase8AMappingProblemCapabilitySnapshotMismatch";
    /// <summary>Identifies an invalid capability-to-hardware port binding.</summary>
    public const string CapabilityPortBindingInvalid = "Phase8AMappingProblemCapabilityPortBindingInvalid";
    /// <summary>Identifies an invalid topology manifest.</summary>
    public const string TopologyManifestInvalid = "Phase8AMappingProblemTopologyManifestInvalid";
    /// <summary>Identifies an invalid logical-path catalog.</summary>
    public const string PathCatalogInvalid = "Phase8AMappingProblemPathCatalogInvalid";
    /// <summary>Identifies a logical-path catalog built from stale authorities.</summary>
    public const string PathCatalogStale = "Phase8AMappingProblemPathCatalogStale";
    /// <summary>Identifies a missing operation-lowering plan.</summary>
    public const string MissingLoweringPlan = "Phase8AMappingProblemLoweringMissing";
    /// <summary>Identifies duplicate lowering authority for an operation.</summary>
    public const string DuplicateLoweringPlan = "Phase8AMappingProblemLoweringDuplicate";
    /// <summary>Identifies a mismatched lowering semantic hash.</summary>
    public const string LoweringHashMismatch = "Phase8AMappingProblemLoweringHashMismatch";
    /// <summary>Identifies lowering content that disagrees with normalized workload semantics.</summary>
    public const string LoweringMismatch = "Phase8AMappingProblemLoweringMismatch";
    /// <summary>Identifies an invalid lowered collective requirement.</summary>
    public const string CollectiveInvalid = "Phase8AMappingProblemCollectiveInvalid";
    /// <summary>Identifies duplicate manual mapping constraints.</summary>
    public const string DuplicateManualConstraint = "Phase8AMappingProblemManualConstraintDuplicate";
    /// <summary>Identifies a malformed manual mapping constraint.</summary>
    public const string ManualConstraintInvalid = "Phase8AMappingProblemManualConstraintInvalid";
    /// <summary>Identifies a manual mapping constraint with no feasible option.</summary>
    public const string ManualConstraintInfeasible = "Phase8AMappingProblemManualConstraintInfeasible";
    /// <summary>Identifies an operation unsupported by a candidate target.</summary>
    public const string OperationUnsupported = "Phase8AMappingHardOperationUnsupported";
    /// <summary>Identifies a shape unsupported by a candidate target.</summary>
    public const string ShapeUnsupported = "Phase8AMappingHardShapeUnsupported";
    /// <summary>Identifies a precision unsupported by a candidate target.</summary>
    public const string PrecisionUnsupported = "Phase8AMappingHardPrecisionUnsupported";
    /// <summary>Identifies insufficient storage capacity for a candidate target.</summary>
    public const string CapacityExceeded = "Phase8AMappingHardCapacityExceeded";
    /// <summary>Identifies exhaustion of resident storage slots.</summary>
    public const string ResidentSlotsExceeded = "Phase8AMappingHardResidentSlotsExceeded";
    /// <summary>Identifies an unsupported semantic port binding.</summary>
    public const string PortUnsupported = "Phase8AMappingHardPortUnsupported";
    /// <summary>Identifies incompatible routing or capability domains.</summary>
    public const string DomainMismatch = "Phase8AMappingHardDomainMismatch";
    /// <summary>Identifies a missing route between required endpoints.</summary>
    public const string ReachabilityMissing = "Phase8AMappingHardReachabilityMissing";
    /// <summary>Identifies a route that violates exact endpoint requirements.</summary>
    public const string RouteInvalid = "Phase8AMappingHardRouteInvalid";
    /// <summary>Identifies an operation tile with no hard-feasible target.</summary>
    public const string NoFeasibleTarget = "Phase8AMappingProblemNoFeasibleTarget";
    /// <summary>Identifies a collective with no hard-feasible target.</summary>
    public const string NoFeasibleCollectiveTarget = "Phase8AMappingProblemNoFeasibleCollectiveTarget";
    /// <summary>Identifies inconsistent or overlapping frozen storage state.</summary>
    public const string StorageLedgerInvalid = "Phase8AMappingProblemStorageLedgerInvalid";
    /// <summary>Identifies arithmetic overflow while deriving the mapping problem.</summary>
    public const string ArithmeticOverflow = "Phase8AMappingProblemArithmeticOverflow";
}

/// <summary>Defines exact operation identities required by lowered collectives.</summary>
public static class Phase8ACollectiveCapabilityIds
{
    /// <summary>Gets the stable grouped-vector-sum collective capability identifier.</summary>
    public const string GroupedVectorSum = Phase8AGroupedVectorSumContract.TypeId;
    /// <summary>Gets the stable tensor-assembly collective capability identifier.</summary>
    public const string TensorAssembly = "com.hardware-sim.first-party.collective.tensor-assembly";
}

/// <summary>Freezes every authority required to construct one topology-aware mapping problem.</summary>
public sealed class Phase8AMappingProblemRequest
{
    private readonly string _actualGraphJson;

    /// <summary>Initializes a frozen request from every authority needed to build a mapping problem.</summary>
    /// <param name="problemId">The stable problem identifier.</param>
    /// <param name="normalizedInput">The normalized workload and semantic ordering.</param>
    /// <param name="actualTopologyGraph">The actual topology graph to freeze.</param>
    /// <param name="baseMapping">The Mapping 2.0 base mapping.</param>
    /// <param name="loweringAuthorities">The Core-owned operation lowerings.</param>
    /// <param name="capabilityDiscoveryRequest">The capability-discovery authority.</param>
    /// <param name="capabilityPortBindings">The capability-to-hardware port bindings.</param>
    /// <param name="topologyManifest">The typed topology manifest.</param>
    /// <param name="logicalPathCatalog">The exact logical-path catalog.</param>
    /// <param name="policyBudget">The bounded search policy.</param>
    /// <param name="manualTargetConstraints">Optional exact target locks.</param>
    /// <param name="manualOperandPathConstraints">Optional exact operand-path locks.</param>
    /// <param name="manualCollectiveConstraints">Optional exact collective locks.</param>
    public Phase8AMappingProblemRequest(
        string problemId,
        Phase8ATopologyAwareMappingInput? normalizedInput,
        HardwareGraph? actualTopologyGraph,
        WorkloadMappingV2? baseMapping,
        IEnumerable<Phase8ALoweringAuthority>? loweringAuthorities,
        Phase8ACapabilityDiscoveryRequest? capabilityDiscoveryRequest,
        IEnumerable<Phase8ACapabilityPortBinding>? capabilityPortBindings,
        TopologyManifest? topologyManifest,
        Phase8ALogicalPathCatalog? logicalPathCatalog,
        Phase8AMappingPolicyBudget? policyBudget,
        IEnumerable<Phase8AManualTargetConstraint>? manualTargetConstraints = null,
        IEnumerable<Phase8AManualOperandPathConstraint>? manualOperandPathConstraints = null,
        IEnumerable<Phase8AManualCollectiveConstraint>? manualCollectiveConstraints = null)
    {
        ProblemId = problemId?.Trim() ?? "";
        NormalizedInput = normalizedInput;
        _actualGraphJson = actualTopologyGraph is null ? "" : HardwareGraphJson.Serialize(actualTopologyGraph);
        BaseMapping = baseMapping;
        LoweringAuthorities = Freeze(loweringAuthorities, item => item.Plan.OperationId);
        CapabilityDiscoveryRequest = capabilityDiscoveryRequest;
        CapabilityPortBindings = Freeze(capabilityPortBindings, item => item.ComponentId + "\u001f" + item.CapabilityPortId);
        TopologyManifest = topologyManifest;
        LogicalPathCatalog = logicalPathCatalog;
        PolicyBudget = policyBudget;
        ManualTargetConstraints = Freeze(manualTargetConstraints, item => item.OperationTileId + "\u001f" + item.TargetComponentId + "\u001f" + item.StorageResourceId);
        ManualOperandPathConstraints = Freeze(manualOperandPathConstraints, item => item.OperationTileId + "\u001f" + item.OperandRoleId + "\u001f" + item.PathId);
        ManualCollectiveConstraints = Freeze(manualCollectiveConstraints, item => item.CollectiveIntentId + "\u001f" + item.TargetComponentId + "\u001f" + item.InputPortId);
    }

    /// <summary>Gets the stable mapping-problem identifier.</summary>
    public string ProblemId { get; }
    /// <summary>Gets the normalized topology-aware mapping input, when supplied.</summary>
    public Phase8ATopologyAwareMappingInput? NormalizedInput { get; }
    /// <summary>Gets the frozen Mapping 2.0 base mapping, when supplied.</summary>
    public WorkloadMappingV2? BaseMapping { get; }
    /// <summary>Gets the deterministically ordered lowering authorities.</summary>
    public IReadOnlyList<Phase8ALoweringAuthority> LoweringAuthorities { get; }
    /// <summary>Gets the capability-discovery request.</summary>
    public Phase8ACapabilityDiscoveryRequest? CapabilityDiscoveryRequest { get; }
    /// <summary>Gets the capability-to-hardware port bindings.</summary>
    public IReadOnlyList<Phase8ACapabilityPortBinding> CapabilityPortBindings { get; }
    /// <summary>Gets the typed topology manifest, when supplied.</summary>
    public TopologyManifest? TopologyManifest { get; }
    /// <summary>Gets the exact logical-path catalog, when supplied.</summary>
    public Phase8ALogicalPathCatalog? LogicalPathCatalog { get; }
    /// <summary>Gets the bounded mapping-policy budget, when supplied.</summary>
    public Phase8AMappingPolicyBudget? PolicyBudget { get; }
    /// <summary>Gets the exact manual target locks.</summary>
    public IReadOnlyList<Phase8AManualTargetConstraint> ManualTargetConstraints { get; }
    /// <summary>Gets the exact manual operand-path locks.</summary>
    public IReadOnlyList<Phase8AManualOperandPathConstraint> ManualOperandPathConstraints { get; }
    /// <summary>Gets the exact manual collective locks.</summary>
    public IReadOnlyList<Phase8AManualCollectiveConstraint> ManualCollectiveConstraints { get; }
    internal HardwareGraph? CloneActualGraph() => string.IsNullOrEmpty(_actualGraphJson) ? null : JsonSerializer.Deserialize<HardwareGraph>(_actualGraphJson, HardwareGraphJson.Options) ?? throw new InvalidOperationException("Frozen actual HardwareGraph JSON deserialized to null.");

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T>? values, Func<T, string> key) where T : class
    {
        var array = (values ?? []).ToArray();
        if (array.Any(item => item is null)) throw new ArgumentException("Mapping-problem collections cannot contain null entries.");
        return Array.AsReadOnly(array.OrderBy(key, StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Describes one deterministic fatal mapping-problem diagnostic.</summary>
/// <param name="Code">The stable diagnostic code.</param>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Location">The canonical authority location.</param>
/// <param name="Message">The human-readable diagnostic.</param>
/// <param name="RelatedId">An optional related stable identifier.</param>
public sealed record Phase8AMappingProblemIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null);

/// <summary>Records one option pruned by a hard constraint.</summary>
/// <param name="SubjectKindId">The kind of rejected mapping subject.</param>
/// <param name="SubjectId">The rejected subject identifier.</param>
/// <param name="TargetComponentId">The rejected target component.</param>
/// <param name="Code">The stable rejection code.</param>
/// <param name="Location">The canonical authority location.</param>
/// <param name="Message">The human-readable rejection reason.</param>
public sealed record Phase8AMappingHardConstraintRejection(
    string SubjectKindId,
    string SubjectId,
    string TargetComponentId,
    string Code,
    string Location,
    string Message);

/// <summary>Captures every immutable authority hash consumed by a mapping problem.</summary>
/// <param name="NormalizedInputHash">The normalized-input hash.</param>
/// <param name="BaseMappingHash">The Mapping 2.0 base hash.</param>
/// <param name="LoweringHash">The aggregate lowering hash.</param>
/// <param name="CapabilityContentHash">The capability-content hash.</param>
/// <param name="TopologyGraphHash">The topology-graph hash.</param>
/// <param name="PlacementHash">The placement hash.</param>
/// <param name="RouteHash">The route hash.</param>
/// <param name="TopologyManifestHash">The topology-manifest hash.</param>
/// <param name="LogicalPathCatalogHash">The logical-path-catalog hash.</param>
public sealed record Phase8AMappingBaseHashes(
    string NormalizedInputHash,
    string BaseMappingHash,
    string LoweringHash,
    string CapabilityContentHash,
    string TopologyGraphHash,
    string PlacementHash,
    string RouteHash,
    string TopologyManifestHash,
    string LogicalPathCatalogHash);

/// <summary>Represents one immutable topology-aware mapping search problem.</summary>
public sealed class Phase8AMappingProblem
{
    /// <summary>Gets the current mapping-problem schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets the current mapping-problem compiler contract identifier.</summary>
    public const string CurrentCompilerContractId = "phase8a-mapping-problem-compiler-v2";
    /// <summary>Gets the current canonical mapping-problem hash algorithm.</summary>
    public const string CurrentCanonicalHashAlgorithm = "sha256/phase8a-mapping-problem-1.0/v2";
    private readonly string _actualGraphJson;

    internal Phase8AMappingProblem(
        string problemId,
        Phase8ATopologyAwareMappingInput normalizedInput,
        HardwareGraph actualGraph,
        WorkloadMappingV2 baseMapping,
        IEnumerable<Phase8ALoweringAuthority> lowerings,
        Phase8ACapabilityAuthority capabilityAuthority,
        TopologyManifest topologyManifest,
        Phase8ALogicalPathCatalog logicalPathCatalog,
        Phase8AMappingPolicyBudget policyBudget,
        Phase8AMappingBaseHashes baseHashes,
        Phase8AExactRouteMatrix routeMatrix,
        IEnumerable<Phase8AStorageSelectorState> storageSelectors,
        IEnumerable<Phase8AMappingOperationProblem> operations,
        IEnumerable<Phase8AMappingCollectiveRequirement> collectives,
        IEnumerable<Phase8AManualTargetConstraint> manualTargets,
        IEnumerable<Phase8AManualOperandPathConstraint> manualPaths,
        IEnumerable<Phase8AManualCollectiveConstraint> manualCollectives,
        Phase8AMappingSearchStatus searchStatus,
        string semanticOrderingHash,
        string canonicalHash)
    {
        ProblemId = problemId;
        NormalizedInput = normalizedInput;
        _actualGraphJson = HardwareGraphJson.Serialize(actualGraph);
        BaseMapping = baseMapping;
        LoweringAuthorities = Array.AsReadOnly(lowerings.OrderBy(item => normalizedInput.OperationBindings.Single(binding => binding.OperationId == item.Plan.OperationId).OperationOrdinal).ToArray());
        CapabilityAuthority = capabilityAuthority;
        TopologyManifest = topologyManifest;
        LogicalPathCatalog = logicalPathCatalog;
        PolicyBudget = policyBudget;
        BaseHashes = baseHashes;
        RouteMatrix = routeMatrix;
        StorageSelectors = Array.AsReadOnly(storageSelectors.OrderBy(item => item.ComponentId, StringComparer.Ordinal).ThenBy(item => item.ResourceId, StringComparer.Ordinal).ToArray());
        Operations = Array.AsReadOnly(operations.OrderBy(item => item.OperationOrdinal).ThenBy(item => item.Tile.MRange.Offset).ThenBy(item => item.Tile.KRange.Offset).ThenBy(item => item.Tile.NRange.Offset).ToArray());
        Collectives = Array.AsReadOnly(collectives.OrderBy(item => item.OperationOrdinal).ThenBy(item => item.Intent.StageOrder).ThenBy(item => item.Intent.MRange.Offset).ThenBy(item => item.Intent.NRange.Offset).ToArray());
        ManualTargetConstraints = Array.AsReadOnly(manualTargets.ToArray());
        ManualOperandPathConstraints = Array.AsReadOnly(manualPaths.ToArray());
        ManualCollectiveConstraints = Array.AsReadOnly(manualCollectives.ToArray());
        SearchStatus = searchStatus;
        SemanticOrderingHash = semanticOrderingHash;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets the schema version of this problem.</summary>
    public string SchemaVersion => CurrentSchemaVersion;
    /// <summary>Gets the stable mapping-problem identifier.</summary>
    public string ProblemId { get; }
    /// <summary>Gets the validated normalized topology-aware input.</summary>
    public Phase8ATopologyAwareMappingInput NormalizedInput { get; }
    /// <summary>Gets the validated frozen Mapping 2.0 base.</summary>
    public WorkloadMappingV2 BaseMapping { get; }
    /// <summary>Gets the deterministically ordered lowering authorities.</summary>
    public IReadOnlyList<Phase8ALoweringAuthority> LoweringAuthorities { get; }
    /// <summary>Gets the discovered and verified capability authority.</summary>
    public Phase8ACapabilityAuthority CapabilityAuthority { get; }
    /// <summary>Gets the validated topology manifest.</summary>
    public TopologyManifest TopologyManifest { get; }
    /// <summary>Gets the validated exact logical-path catalog.</summary>
    public Phase8ALogicalPathCatalog LogicalPathCatalog { get; }
    /// <summary>Gets the validated bounded search policy.</summary>
    public Phase8AMappingPolicyBudget PolicyBudget { get; }
    /// <summary>Gets the immutable hashes of every consumed authority.</summary>
    public Phase8AMappingBaseHashes BaseHashes { get; }
    /// <summary>Gets the exact typed endpoint-pair route matrix.</summary>
    public Phase8AExactRouteMatrix RouteMatrix { get; }
    /// <summary>Gets the frozen global storage selector states.</summary>
    public IReadOnlyList<Phase8AStorageSelectorState> StorageSelectors { get; }
    /// <summary>Gets the hard-feasible operation-tile search domains.</summary>
    public IReadOnlyList<Phase8AMappingOperationProblem> Operations { get; }
    /// <summary>Gets the hard-feasible collective search domains.</summary>
    public IReadOnlyList<Phase8AMappingCollectiveRequirement> Collectives { get; }
    /// <summary>Gets the exact manual target locks.</summary>
    public IReadOnlyList<Phase8AManualTargetConstraint> ManualTargetConstraints { get; }
    /// <summary>Gets the exact manual operand-path locks.</summary>
    public IReadOnlyList<Phase8AManualOperandPathConstraint> ManualOperandPathConstraints { get; }
    /// <summary>Gets the exact manual collective locks.</summary>
    public IReadOnlyList<Phase8AManualCollectiveConstraint> ManualCollectiveConstraints { get; }
    /// <summary>Gets the completeness status of the retained search surface.</summary>
    public Phase8AMappingSearchStatus SearchStatus { get; }
    /// <summary>Gets the canonical semantic-ordering hash.</summary>
    public string SemanticOrderingHash { get; }
    /// <summary>Gets the canonical hash algorithm used by this problem.</summary>
    public string CanonicalHashAlgorithm => CurrentCanonicalHashAlgorithm;
    /// <summary>Gets the canonical hash of the complete mapping problem.</summary>
    public string CanonicalHash { get; }
    /// <summary>Creates a defensive clone of the frozen actual topology graph.</summary>
    /// <returns>A detached copy of the actual topology graph.</returns>
    public HardwareGraph CloneActualGraph() => JsonSerializer.Deserialize<HardwareGraph>(_actualGraphJson, HardwareGraphJson.Options) ?? throw new InvalidOperationException("Frozen actual HardwareGraph JSON deserialized to null.");
    /// <summary>Finds the operation problem for an exact operation-tile identifier.</summary>
    /// <param name="operationTileId">The operation-tile identifier to locate.</param>
    /// <returns>The matching operation problem, or <see langword="null"/> when absent.</returns>
    public Phase8AMappingOperationProblem? FindOperation(string operationTileId) => Operations.SingleOrDefault(item => string.Equals(item.Tile.OperationTileId, operationTileId, StringComparison.Ordinal));
}

/// <summary>Contains either one complete immutable problem or structured hard failures.</summary>
public sealed class Phase8AMappingProblemBuildResult
{
    internal Phase8AMappingProblemBuildResult(Phase8AMappingProblem? problem, IEnumerable<Phase8AMappingProblemIssue> issues, IEnumerable<Phase8AMappingHardConstraintRejection> rejections)
    {
        Problem = problem;
        Issues = Array.AsReadOnly(issues.OrderBy(item => item.Location, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray());
        Rejections = Array.AsReadOnly(rejections.OrderBy(item => item.SubjectKindId, StringComparer.Ordinal).ThenBy(item => item.SubjectId, StringComparer.Ordinal).ThenBy(item => item.TargetComponentId, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the complete problem when construction succeeded.</summary>
    public Phase8AMappingProblem? Problem { get; }
    /// <summary>Gets the deterministic fatal diagnostics.</summary>
    public IReadOnlyList<Phase8AMappingProblemIssue> Issues { get; }
    /// <summary>Gets the options pruned by hard constraints.</summary>
    public IReadOnlyList<Phase8AMappingHardConstraintRejection> Rejections { get; }
    /// <summary>Gets whether problem construction completed without errors.</summary>
    public bool IsSuccess => Problem is not null && Issues.All(item => item.Severity != ValidationSeverity.Error);
}

/// <summary>Computes complete Core-owned semantic hashes for discovered capabilities and lowering plans.</summary>
public static class Phase8AMappingAuthorityHasher
{
    /// <summary>Computes the canonical hash of normalized topology-aware mapping input.</summary>
    /// <param name="input">The normalized input to hash.</param>
    /// <returns>The lowercase SHA-256 content hash.</returns>
    public static string ComputeNormalizedInputHash(Phase8ATopologyAwareMappingInput input) => Hash(new
    {
        algorithm = "sha256/phase8a-topology-aware-normalized-input/v1",
        input.TopologyIdentity, input.PolicyId, input.Seed, input.Workload,
        input.OperationBindings, input.OperandIngressBindings, input.ComponentOrdinals, input.PathOrdinals, input.StorageSelectorOrdinals
    });

    /// <summary>Computes the canonical hash of a normalized reference workload.</summary>
    /// <param name="workload">The normalized workload to hash.</param>
    /// <returns>The lowercase SHA-256 content hash.</returns>
    public static string ComputeWorkloadHash(ReferenceMappingWorkload workload) => Hash(new
    {
        algorithm = "sha256/phase8a-normalized-workload/v1",
        workload
    });

    /// <summary>Computes the complete semantic content hash of a capability snapshot.</summary>
    /// <param name="snapshot">The capability snapshot to hash.</param>
    /// <returns>The lowercase SHA-256 content hash.</returns>
    public static string ComputeCapabilityContentHash(CapabilitySnapshot snapshot) => Hash(new
    {
        algorithm = Phase8ACapabilityAuthority.ContentHashAlgorithm,
        snapshot.SnapshotId,
        snapshot.HardwareGraphHash,
        snapshot.PlacementHash,
        snapshot.RouteHash,
        snapshot.RegistryHash,
        components = snapshot.Components.Select(component => new
        {
            component.ComponentId, component.StableTypeId, component.TemplateId, component.TemplateHash,
            component.ProfileId, component.ProfileHash, component.KernelId, component.KernelHash,
            operations = component.OperationKindIds,
            shapes = component.ShapeContracts,
            precisions = component.PrecisionIds,
            component.CapacityBits, component.LatencyCycles, component.BandwidthBitsPerCycle,
            ports = component.Ports.Select(port => new { port.PortId, port.DirectionId, port.ProtocolId, port.DomainId, port.BandwidthBitsPerCycle, port.SemanticRoleId, port.DataTypeId, port.PrecisionId }),
            component.DomainId,
            storage = component.StorageCapabilities.Select(storage => new
            {
                storage.ResourceId, storage.StorageLevelId, roles = storage.SupportedOperandRoleIds,
                precisions = storage.SupportedPrecisionIds, storage.CapacityBits, storage.AlignmentBits,
                storage.AllocationGranularityBits, storage.ResidentSlots, storage.PreloadPortId,
                storage.ReadBandwidthBitsPerCycle, storage.WriteBandwidthBitsPerCycle,
                storage.ReadLatencyCycles, storage.WriteLatencyCycles, storage.CommitModeId,
                storage.SupportsStreaming, storage.SupportsReuse, storage.SourceContractHash
            })
        })
    });

    /// <summary>Computes the complete semantic hash of a matrix-multiplication lowering plan.</summary>
    /// <param name="plan">The lowering plan to hash.</param>
    /// <returns>The lowercase SHA-256 semantic hash.</returns>
    public static string ComputeLoweringSemanticHash(Phase8AMatMulLoweringPlan plan) => Hash(new
    {
        algorithm = Phase8ALoweringAuthority.SemanticHashAlgorithm,
        plan.OperationId,
        operands = plan.OperandTiles.Select(TileProjection),
        outputs = plan.OutputTiles.Select(OutputProjection),
        operations = plan.OperationTiles.Select(item => new
        {
            item.OperationTileId, item.OperationId, item.ActivationTileId, item.WeightTileId, item.OutputTileId,
            m = Range(item.MRange), k = Range(item.KRange), n = Range(item.NRange),
            valid = item.ValidShape.Dimensions, padded = item.PaddedShape.Dimensions, partition = item.PartitionKind.ToString()
        }),
        collectives = plan.CollectiveIntents.Select(item => new
        {
            item.IntentId, item.KindId, item.GroupKey, item.StageOrder, contributors = item.ContributorTileIds,
            item.ResultTileId, m = Range(item.MRange), n = Range(item.NRange), item.PrecisionId
        }),
        finals = plan.FinalOutputTileIds
    });

    internal static string Hash(object value)
    {
        var json = JsonSerializer.Serialize(value, HardwareGraphJson.Options);
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(json));
    }

    private static object TileProjection(Phase8ALoweredOperandTile item) => new
    {
        item.TileId, item.TensorId, item.RoleId, item.PrecisionId,
        m = Range(item.MRange), k = Range(item.KRange), n = Range(item.NRange),
        valid = item.ValidShape.Dimensions, padded = item.PaddedShape.Dimensions,
        padding = Padding(item.Padding)
    };

    private static object OutputProjection(Phase8ALoweredOutputTile item) => new
    {
        item.TileId, item.TensorId, item.RoleId, item.PrecisionId,
        m = Range(item.MRange), n = Range(item.NRange),
        valid = item.ValidShape.Dimensions, padded = item.PaddedShape.Dimensions,
        padding = Padding(item.Padding)
    };

    private static object Range(MappingIndexRange range) => new { range.Offset, range.Extent };
    private static object Padding(Phase8APaddingCropProvenance padding) => new
    {
        padding.MHighPadding, padding.KHighPadding, padding.NHighPadding, padding.CropRequired, padding.CropStageId
    };
}
/// <summary>Builds immutable mapping problems from actual graph state and compiler-discovered authorities.</summary>
public static class Phase8AMappingProblemBuilder
{
    private const long MaximumSupportedBudget = 10_000_000;

    /// <summary>Builds and validates an immutable topology-aware mapping problem.</summary>
    /// <param name="request">The frozen authorities from which to build the problem.</param>
    /// <returns>A complete problem or structured deterministic failures.</returns>
    public static Phase8AMappingProblemBuildResult Build(Phase8AMappingProblemRequest? request)
    {
        try
        {
            return BuildCore(request);
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException or ArgumentException or KeyNotFoundException or JsonException or NullReferenceException)
        {
            return Failure(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$", "Malformed mapping-problem authority was rejected structurally: " + exception.Message));
        }
    }

    private static Phase8AMappingProblemBuildResult BuildCore(Phase8AMappingProblemRequest? request)
    {
        if (request is null) return Failure(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$", "A mapping-problem request is required."));
        var issues = new List<Phase8AMappingProblemIssue>();
        var rejections = new List<Phase8AMappingHardConstraintRejection>();
        if (string.IsNullOrWhiteSpace(request.ProblemId)) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.problemId", "A stable problem identity is required."));
        if (request.NormalizedInput is null) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.normalizedInput", "A generic normalized input is required."));
        if (request.CloneActualGraph() is null) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.actualTopologyGraph", "The actual HardwareGraph is required."));
        if (request.BaseMapping is null) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.baseMapping", "A frozen Mapping 2.0 base is required."));
        if (request.CapabilityDiscoveryRequest is null) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.capabilityDiscoveryRequest", "The completed compiled SimulationGraph and registry hashes are required."));
        if (request.TopologyManifest is null) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.topologyManifest", "A typed topology manifest is required."));
        if (request.LogicalPathCatalog is null) issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$.logicalPathCatalog", "An exact logical-path catalog is required."));
        ValidateBudget(request.PolicyBudget, issues);
        if (issues.Count != 0) return Failure(issues, rejections);

        var input = request.NormalizedInput!;
        var graph = request.CloneActualGraph()!;
        var mapping = request.BaseMapping!;
        var manifest = request.TopologyManifest!;
        var catalog = request.LogicalPathCatalog!;
        var budget = request.PolicyBudget!;
        ValidateNormalizedInput(input, graph, manifest, catalog, issues);
        ValidateBaseMapping(mapping, issues);
        var topology = ValidateTopologyAndCatalog(graph, manifest, catalog, issues);
        var normalizedHash = Phase8AMappingAuthorityHasher.ComputeNormalizedInputHash(input);
        ValidateBaseNormalizedAuthority(mapping, input, normalizedHash, topology, issues);
        if (issues.Any(item => item.Severity == ValidationSeverity.Error)) return Failure(issues, rejections);
        var capability = DiscoverCapability(request.CapabilityDiscoveryRequest!, request.CapabilityPortBindings, graph, mapping, topology, issues);
        if (capability is not null && !string.Equals(mapping.Candidate.ProfileHash, capability.ContentHash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseHashMismatch, "$.baseMapping.candidate.profileHash", "Base candidate profile authority must equal the Core-rediscovered complete capability content hash."));
        var validatedLowerings = ValidateLowerings(input, request.LoweringAuthorities, issues);
        if (capability is not null && validatedLowerings is not null) ValidateCapabilitySemanticCoverage(input, capability.Snapshot, validatedLowerings, issues);
        ValidateManualKeys(request, issues);
        if (issues.Any(item => item.Severity == ValidationSeverity.Error) || capability is null || validatedLowerings is null)
            return Failure(issues, rejections);

        try
        {
            long visited = 0;
            long retained = 0;
            var complete = true;
            var truncationReasons = new SortedSet<string>(StringComparer.Ordinal);
            var routeMatrix = BuildRouteMatrix(input, graph, catalog, capability, budget, request, issues, ref complete, truncationReasons);
            var storageSelectors = BuildStorageSelectors(input, mapping, capability.Snapshot, validatedLowerings, issues);
            if (issues.Any(item => item.Severity == ValidationSeverity.Error)) return Failure(issues, rejections);
            var operations = BuildOperationProblems(input, validatedLowerings, capability, routeMatrix, budget, request, rejections, issues, ref visited, ref retained, ref complete, truncationReasons);
            var collectives = BuildCollectiveProblems(input, validatedLowerings, capability, budget, request, rejections, issues, ref visited, ref retained, ref complete, truncationReasons);
            ValidateManualSubjects(request, validatedLowerings, issues);
            if (issues.Any(item => item.Severity == ValidationSeverity.Error)) return Failure(issues, rejections);

            var search = new Phase8AMappingSearchStatus(visited, retained, complete, string.Join("+", truncationReasons));


            var loweringHash = Phase8AMappingAuthorityHasher.Hash(validatedLowerings.Select(item => item.Authority.SemanticHash));
            var baseHashes = new Phase8AMappingBaseHashes(
                normalizedHash,
                mapping.CanonicalHash,
                loweringHash,
                capability.ContentHash,
                topology.Topology.Hash,
                topology.Placement.Hash,
                topology.Routing.Hash,
                manifest.CanonicalHash,
                catalog.CanonicalHash);
            var semanticOrderingHash = ComputeSemanticOrderingHash(input, operations, collectives, routeMatrix);
            var canonicalHash = Phase8AMappingAuthorityHasher.Hash(new
            {
                algorithm = Phase8AMappingProblem.CurrentCanonicalHashAlgorithm,
                request.ProblemId,
                baseHashes,
                capability.PluginRegistryHash,
                capability.RuntimeKernelRegistryHash,
                policyBudget = budget,
                manualTargets = request.ManualTargetConstraints,
                manualOperandPaths = request.ManualOperandPathConstraints,
                manualCollectives = request.ManualCollectiveConstraints,
                routeMatrix = routeMatrix.Pairs,
                operations,
                collectives,
                storageSelectors,
                search,
                semanticOrderingHash
            });
            var problem = new Phase8AMappingProblem(
                request.ProblemId, input, graph, mapping, validatedLowerings.Select(item => item.Authority), capability,
                manifest, catalog, budget, baseHashes, routeMatrix, storageSelectors, operations, collectives,
                request.ManualTargetConstraints, request.ManualOperandPathConstraints, request.ManualCollectiveConstraints,
                search, semanticOrderingHash, canonicalHash);
            return new Phase8AMappingProblemBuildResult(problem, issues, rejections);
        }
        catch (OverflowException)
        {
            issues.Add(Error(Phase8AMappingProblemIssueCodes.ArithmeticOverflow, "$", "Mapping-problem arithmetic exceeded Int64."));
            return Failure(issues, rejections);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidInput, "$", "Malformed frozen mapping authority: " + exception.Message));
            return Failure(issues, rejections);
        }
    }

    private sealed record ValidatedTopology(TopologyCanonicalHash Topology, TopologyCanonicalHash Placement, TopologyCanonicalHash Routing);
    private sealed record ValidatedLowering(Phase8ALoweringAuthority Authority, ReferenceMappingWorkloadOperation Operation, Phase8AOperationTensorBinding Binding);

    private static void ValidateBudget(Phase8AMappingPolicyBudget? budget, List<Phase8AMappingProblemIssue> issues)
    {
        if (budget is null || budget.MaxTargetOptionsPerTile <= 0 || budget.MaxPathsPerEndpointPair <= 0 ||
            budget.MaxCollectiveTargetsPerIntent <= 0 || budget.MaxSearchNodes <= 0 || budget.MaxSearchNodes > MaximumSupportedBudget)
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidPolicyBudget, "$.policyBudget", "Every enumeration limit must be positive and MaxSearchNodes must be within the deterministic bound."));
    }


    private static void ValidateBaseNormalizedAuthority(WorkloadMappingV2 mapping, Phase8ATopologyAwareMappingInput input, string normalizedHash, ValidatedTopology topology, List<Phase8AMappingProblemIssue> issues)
    {
        var workloadHash = Phase8AMappingAuthorityHasher.ComputeWorkloadHash(input.Workload);
        if (!string.Equals(mapping.Provenance.NormalizedInputHash, normalizedHash, StringComparison.Ordinal) ||
            !string.Equals(mapping.Provenance.WorkloadHash, workloadHash, StringComparison.Ordinal) || mapping.Provenance.DeterministicSeed != input.Seed)
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseHashMismatch, "$.baseMapping.provenance", "Base Mapping 2.0 workload, normalized input, and seed provenance must exactly match the generic Phase 8A input."));
        if (!string.Equals(mapping.Candidate.TopologyHash, topology.Topology.Hash, StringComparison.Ordinal) ||
            !string.Equals(mapping.Candidate.RouteHash, topology.Routing.Hash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseHashMismatch, "$.baseMapping.candidate", "Base candidate topology and route hashes must equal current authority; its originating policy remains independently provenance-bound."));
    }

    private static void ValidateBaseMapping(WorkloadMappingV2 mapping, List<Phase8AMappingProblemIssue> issues)
    {
        foreach (var source in WorkloadMappingV2Validator.Validate(mapping).Where(item => item.Severity == ValidationSeverity.Error))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseMappingInvalid, "$.baseMapping" + source.Location.TrimStart('$'), source.Message));
        var recomputed = WorkloadMappingV2CanonicalHasher.Compute(mapping);
        if (!string.Equals(recomputed.Algorithm, mapping.CanonicalHashAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(recomputed.Hash, mapping.CanonicalHash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseMappingInvalid, "$.baseMapping.canonicalHash", "Mapping 2.0 canonical content does not match its declared hash."));
    }

    private static ValidatedTopology ValidateTopologyAndCatalog(
        HardwareGraph graph,
        TopologyManifest manifest,
        Phase8ALogicalPathCatalog catalog,
        List<Phase8AMappingProblemIssue> issues)
    {
        var topology = TopologyPresetCanonicalizer.ComputeTopologyGraph(graph);
        var placement = TopologyPresetCanonicalizer.ComputePlacement(graph.Placement);
        var routing = TopologyPresetCanonicalizer.ComputeRouting(graph);
        RequireHash(manifest.TopologyGraphHashAlgorithm, manifest.TopologyGraphHash, topology, "$.topologyManifest.topologyGraphHash", issues);
        RequireHash(manifest.PlacementHashAlgorithm, manifest.PlacementHash, placement, "$.topologyManifest.placementHash", issues);
        RequireHash(manifest.RouteHashAlgorithm, manifest.RouteHash, routing, "$.topologyManifest.routeHash", issues);
        var requestHash = TopologyPresetCanonicalizer.ComputeRequest(manifest.Request);
        RequireHash(manifest.Provenance.RequestHashAlgorithm, manifest.Provenance.RequestHash, requestHash, "$.topologyManifest.provenance.requestHash", issues);
        var self = TopologyPresetCanonicalizer.ComputeManifest(manifest);
        RequireHash(manifest.CanonicalHashAlgorithm, manifest.CanonicalHash, self, "$.topologyManifest.canonicalHash", issues);

        var attached = TopologyManifestJson.ReadFromGraph(graph);
        foreach (var source in attached.Issues.Where(item => item.Severity == ValidationSeverity.Error))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.TopologyManifestInvalid, "$.actualTopologyGraph" + source.Location.TrimStart('$'), source.Message));
        if (attached.Manifest is null || !string.Equals(attached.Manifest.CanonicalHash, manifest.CanonicalHash, StringComparison.Ordinal) ||
            !string.Equals(TopologyManifestJson.Serialize(attached.Manifest), TopologyManifestJson.Serialize(manifest), StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.TopologyManifestInvalid, "$.topologyManifest", "The supplied manifest is not the exact typed manifest attached to the actual HardwareGraph."));

        foreach (var source in Phase8ALogicalPathCatalogBuilder.Validate(catalog, graph, manifest))
        {
            var stale = source.Code.Contains("Stale", StringComparison.Ordinal) || source.Code.Contains("Changed", StringComparison.Ordinal) || source.Code.Contains("Hash", StringComparison.Ordinal);
            issues.Add(Error(stale ? Phase8AMappingProblemIssueCodes.PathCatalogStale : Phase8AMappingProblemIssueCodes.PathCatalogInvalid,
                "$.logicalPathCatalog" + source.Location.TrimStart('$'), source.Message));
        }
        return new ValidatedTopology(topology, placement, routing);
    }

    private static void RequireHash(string algorithm, string hash, TopologyCanonicalHash expected, string location, List<Phase8AMappingProblemIssue> issues)
    {
        if (!string.Equals(algorithm, expected.Algorithm, StringComparison.Ordinal) || !string.Equals(hash, expected.Hash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.TopologyManifestInvalid, location, "The declared authority hash differs from recomputed actual graph state."));
    }

    private static void ValidateNormalizedInput(
        Phase8ATopologyAwareMappingInput input,
        HardwareGraph graph,
        TopologyManifest manifest,
        Phase8ALogicalPathCatalog catalog,
        List<Phase8AMappingProblemIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(input.Workload.WorkloadId) || input.Workload.Operations.Count == 0 ||
            string.IsNullOrWhiteSpace(input.TopologyIdentity) ||
            !string.Equals(input.PolicyId, ReferenceMappingPolicyIds.TopologyCostAwareV1, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput", "Workload, topology identity, and topology-cost-aware-v1 policy are required."));
        if (!string.Equals(input.TopologyIdentity, manifest.Request.TopologyId, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.topologyIdentity", "Normalized topology identity must exactly equal the attached typed manifest request; no preset fallback is permitted."));

        var operationIds = input.Workload.Operations.Select(item => item.OperationId).ToArray();
        if (operationIds.Any(string.IsNullOrWhiteSpace) || operationIds.Distinct(StringComparer.Ordinal).Count() != operationIds.Length)
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.workload.operations", "Operation ids must be non-empty and unique."));
        foreach (var operation in input.Workload.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.OperationTypeId) || operation.Geometry.M <= 0 || operation.Geometry.K <= 0 || operation.Geometry.N <= 0 ||
                !Enum.IsDefined(typeof(PrecisionKind), operation.Precision) || operation.Precision is PrecisionKind.Any or PrecisionKind.Analog)
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.workload.operations", "Every operation requires concrete positive M/K/N and a concrete digital precision.", operation.OperationId));
            if (operation.Tensors.Count != 3 || operation.Tensors.Any(tensor => string.IsNullOrWhiteSpace(tensor.TensorId)) || operation.Tensors.Select(tensor => tensor.TensorId).Distinct(StringComparer.Ordinal).Count() != operation.Tensors.Count ||
                operation.Tensors.Any(tensor => (tensor.Values.Count == 0 && string.IsNullOrWhiteSpace(tensor.ArtifactHash)) || tensor.Values.Any(value => !double.IsFinite(value))))
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.workload.operations", "MatMul requires exactly three unique tensor artifacts, each with finite inline values or a content artifact hash.", operation.OperationId));
        }

        var bindings = input.OperationBindings;
        if (bindings.Count != input.Workload.Operations.Count || bindings.Select(item => item.OperationId).Distinct(StringComparer.Ordinal).Count() != bindings.Count ||
            bindings.Select(item => item.OperationOrdinal).Distinct().Count() != bindings.Count)
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.operationBindings", "Exactly one unique semantic binding and ordinal are required per operation."));
        foreach (var binding in bindings)
        {
            var operation = input.Workload.Operations.SingleOrDefault(item => string.Equals(item.OperationId, binding.OperationId, StringComparison.Ordinal));
            if (operation is null || binding.OperationOrdinal < 0 || new[] { binding.ActivationTensorId, binding.WeightTensorId, binding.OutputTensorId }.Any(string.IsNullOrWhiteSpace) ||
                new[] { binding.ActivationTensorId, binding.WeightTensorId, binding.OutputTensorId }.Distinct(StringComparer.Ordinal).Count() != 3 ||
                operation is not null && !operation.Tensors.Select(tensor => tensor.TensorId).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { binding.ActivationTensorId, binding.WeightTensorId, binding.OutputTensorId }))
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.operationBindings", "Tensor roles must resolve three distinct tensors on the exact operation.", binding.OperationId));
        }

        if (input.OperandIngressBindings.Count != checked(input.Workload.Operations.Count * 2) ||
            input.OperandIngressBindings.GroupBy(item => (item.OperationId, item.OperandRoleId)).Any(group => group.Count() != 1) ||
            input.OperandIngressBindings.Any(item => input.Workload.Operations.All(operation => operation.OperationId != item.OperationId)))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.operandIngressBindings", "Operand ingress bindings must exactly cover activation and weight for every operation and contain no extras."));
        var ingressGroups = input.OperandIngressBindings.GroupBy(item => item.OperationId, StringComparer.Ordinal);
        foreach (var operation in input.Workload.Operations)
        {
            var ingress = ingressGroups.SingleOrDefault(group => string.Equals(group.Key, operation.OperationId, StringComparison.Ordinal))?.ToArray() ?? [];
            if (ingress.Length != 2 || ingress.Count(item => item.OperandRoleId == Phase8ATensorRoleIds.Activation) != 1 || ingress.Count(item => item.OperandRoleId == Phase8ATensorRoleIds.Weight) != 1)
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.operandIngressBindings", "Each operation requires exactly one activation and one weight producer endpoint.", operation.OperationId));
            foreach (var endpoint in ingress)
            {
                var component = graph.Components.SingleOrDefault(item => string.Equals(item.Id, endpoint.ProducerComponentId, StringComparison.Ordinal));
                var port = component?.Ports.SingleOrDefault(item => string.Equals(item.Name, endpoint.ProducerHardwarePortName, StringComparison.Ordinal));
                if (port is null || port.Direction != PortDirection.Output)
                    issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.operandIngressBindings", "Ingress endpoints must resolve exact actual-graph output ports.", endpoint.OperationId));
            }
        }

        if (input.ComponentOrdinals.Select(item => item.ComponentId).Distinct(StringComparer.Ordinal).Count() != input.ComponentOrdinals.Count ||
            input.ComponentOrdinals.GroupBy(item => (item.RoleId, item.Ordinal)).Any(group => group.Count() != 1) ||
            input.ComponentOrdinals.Any(item => item.Ordinal < 0 || graph.Components.All(component => !string.Equals(component.Id, item.ComponentId, StringComparison.Ordinal))))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "Semantic component identities and role ordinals must be unique and resolve the actual graph."));
        var requiredComponents = input.OperandIngressBindings.Select(item => item.ProducerComponentId).ToHashSet(StringComparer.Ordinal);
        if (requiredComponents.Any(id => input.ComponentOrdinals.All(item => !string.Equals(item.ComponentId, id, StringComparison.Ordinal) || item.RoleId != Phase8AMappingSemanticRoles.Ingress)))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "Every operand producer requires an explicit ingress semantic role."));

        if (input.PathOrdinals.Count != catalog.Entries.Count || input.PathOrdinals.Select(item => item.PathId).Distinct(StringComparer.Ordinal).Count() != input.PathOrdinals.Count ||
            input.PathOrdinals.Select(item => item.Ordinal).Distinct().Count() != input.PathOrdinals.Count ||
            input.PathOrdinals.Any(item => item.Ordinal < 0 || catalog.Entries.All(path => !string.Equals(path.PathId, item.PathId, StringComparison.Ordinal))))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.pathOrdinals", "Explicit semantic path ordinals must exactly cover the catalog."));
    }
    private static Phase8ACapabilityAuthority? DiscoverCapability(
        Phase8ACapabilityDiscoveryRequest discoveryRequest,
        IReadOnlyList<Phase8ACapabilityPortBinding> bindings,
        HardwareGraph actualGraph,
        WorkloadMappingV2 mapping,
        ValidatedTopology topology,
        List<Phase8AMappingProblemIssue> issues)
    {
        if (!string.Equals(discoveryRequest.TopologyGraphHash, topology.Topology.Hash, StringComparison.Ordinal) ||
            !string.Equals(discoveryRequest.PlacementHash, topology.Placement.Hash, StringComparison.Ordinal) ||
            !string.Equals(discoveryRequest.RouteHash, topology.Routing.Hash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseHashMismatch, "$.capabilityDiscoveryRequest", "Capability discovery hashes must equal recomputed actual topology, placement, and route state."));
        var actualSourceHash = ComponentExecutionJson.ComputeSha256(HardwareGraphJson.Serialize(actualGraph));
        if (!string.Equals(discoveryRequest.Graph.Provenance.SourceGraphHash, actualSourceHash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.BaseHashMismatch, "$.capabilityDiscoveryRequest.graph.provenance.sourceGraphHash", "The compiled SimulationGraph was not compiled from the exact actual HardwareGraph JSON."));
        if (!string.Equals(discoveryRequest.Graph.Provenance.ComponentRuntimeKernelRegistryHash, discoveryRequest.RuntimeKernelRegistryHash, StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.CapabilityDiscoveryFailed, "$.capabilityDiscoveryRequest.runtimeKernelRegistryHash", "Runtime registry authority differs from compiled SimulationGraph provenance."));

        var discovered = Phase8ACapabilityDiscovery.Discover(discoveryRequest);
        foreach (var source in discovered.Issues.Where(item => item.Severity == ValidationSeverity.Error))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.CapabilityDiscoveryFailed, "$.capabilityDiscovery" + source.Location.TrimStart('$'), source.Message));
        if (!discovered.IsSuccess || discovered.Snapshot is null) return null;
        var snapshot = discovered.Snapshot;
        var contentHash = Phase8AMappingAuthorityHasher.ComputeCapabilityContentHash(snapshot);
        if (!string.Equals(contentHash, Phase8AMappingAuthorityHasher.ComputeCapabilityContentHash(mapping.CapabilitySnapshot), StringComparison.Ordinal))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.CapabilitySnapshotMismatch, "$.baseMapping.capabilitySnapshot", "Mapping 2.0 capability content differs from Core rediscovery over the completed compiled graph."));

        var capabilityPorts = snapshot.Components.SelectMany(component => component.Ports.Select(port => (component, port))).ToArray();
        if (bindings.Count != capabilityPorts.Length ||
            bindings.GroupBy(item => (item.ComponentId, item.CapabilityPortId)).Any(group => group.Count() != 1) ||
            bindings.GroupBy(item => (item.ComponentId, item.HardwarePortName)).Any(group => group.Count() != 1))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.CapabilityPortBindingInvalid, "$.capabilityPortBindings", "Exactly one unique actual-graph binding is required per discovered capability port."));
        foreach (var (component, port) in capabilityPorts)
        {
            var bindingMatches = bindings.Where(item => string.Equals(item.ComponentId, component.ComponentId, StringComparison.Ordinal) && string.Equals(item.CapabilityPortId, port.PortId, StringComparison.Ordinal)).Take(2).ToArray();
            var binding = bindingMatches.Length == 1 ? bindingMatches[0] : null;
            var componentMatches = actualGraph.Components.Where(item => string.Equals(item.Id, component.ComponentId, StringComparison.Ordinal)).Take(2).ToArray();
            var actualComponent = componentMatches.Length == 1 ? componentMatches[0] : null;
            var actualPortMatches = binding is null || actualComponent is null ? Array.Empty<HardwarePort>() : actualComponent.Ports.Where(item => string.Equals(item.Name, binding.HardwarePortName, StringComparison.Ordinal)).Take(2).ToArray();
            var actualPort = actualPortMatches.Length == 1 ? actualPortMatches[0] : null;
            var compiledMatches = discoveryRequest.Graph.Ports.Where(item => string.Equals(item.Id, port.PortId, StringComparison.Ordinal) && string.Equals(item.ComponentId, component.ComponentId, StringComparison.Ordinal)).Take(2).ToArray();
            var compiledPort = compiledMatches.Length == 1 ? compiledMatches[0] : null;
            if (binding is null || actualPort is null || compiledPort is null ||
                !string.Equals(compiledPort.Name, binding.HardwarePortName, StringComparison.Ordinal) ||
                actualPort.Direction != compiledPort.Direction || actualPort.SignalType != compiledPort.SignalType ||
                actualPort.DataType != compiledPort.DataType || actualPort.Precision != compiledPort.Precision ||
                actualPort.Protocol != compiledPort.Protocol || actualPort.BandwidthBitsPerCycle != compiledPort.BandwidthBitsPerCycle ||
                !string.Equals(actualPort.ClockDomain, compiledPort.ClockDomain, StringComparison.Ordinal) ||
                !string.Equals(port.DirectionId, actualPort.Direction.ToString().ToLowerInvariant(), StringComparison.Ordinal) ||
                !string.Equals(port.ProtocolId, actualPort.Protocol.ToString().ToLowerInvariant(), StringComparison.Ordinal) ||
                !string.Equals(port.DomainId, $"{actualPort.SignalType.ToString().ToLowerInvariant()}/clock:{actualPort.ClockDomain}", StringComparison.Ordinal) ||
                !string.Equals(port.DataTypeId, actualPort.DataType.ToString(), StringComparison.Ordinal) ||
                !string.Equals(port.PrecisionId, actualPort.Precision.ToString(), StringComparison.Ordinal) ||
                port.BandwidthBitsPerCycle != actualPort.BandwidthBitsPerCycle)
                issues.Add(Error(Phase8AMappingProblemIssueCodes.CapabilityPortBindingInvalid, "$.capabilityPortBindings", "A discovered capability port must map one-to-one to the exact typed compiled and actual HardwareGraph port.", port.PortId));
        }
        return new Phase8ACapabilityAuthority(snapshot, contentHash, bindings, discoveryRequest.PluginRegistryHash,
            discoveryRequest.RuntimeKernelRegistryHash, discoveryRequest.Graph.Provenance.SourceGraphHash);
    }

    private static void ValidateCapabilitySemanticCoverage(
        Phase8ATopologyAwareMappingInput input,
        CapabilitySnapshot snapshot,
        IReadOnlyList<ValidatedLowering> lowerings,
        List<Phase8AMappingProblemIssue> issues)
    {
        var computeKinds = input.Workload.Operations.Select(item => item.OperationTypeId).ToHashSet(StringComparer.Ordinal);
        var requiredCollectiveKinds = lowerings.SelectMany(item => item.Authority.Plan.CollectiveIntents)
            .Select(item => item.KindId == Phase8ACollectiveIntentKinds.Sum ? Phase8ACollectiveCapabilityIds.GroupedVectorSum : Phase8ACollectiveCapabilityIds.TensorAssembly)
            .ToHashSet(StringComparer.Ordinal);
        var mappingCollectiveKinds = new[]
        {
            Phase8ACollectiveCapabilityIds.GroupedVectorSum,
            Phase8ACollectiveCapabilityIds.TensorAssembly
        };
        foreach (var component in snapshot.Components)
        {
            var computeCapable = component.OperationKindIds.Any(computeKinds.Contains);
            var requiredCollectiveCapable = component.OperationKindIds.Any(requiredCollectiveKinds.Contains);
            var mappingCollectiveCapable = component.OperationKindIds.Any(kind => mappingCollectiveKinds.Contains(kind, StringComparer.Ordinal));
            var declared = input.ComponentOrdinals.Where(item => string.Equals(item.ComponentId, component.ComponentId, StringComparison.Ordinal)).ToArray();
            if (computeCapable && (declared.Length != 1 || !string.Equals(declared[0].RoleId, Phase8AMappingSemanticRoles.Compute, StringComparison.Ordinal)))
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "Every discovered workload-capable component must be present as an explicit compute semantic ordinal; caller role lists cannot hide candidates.", component.ComponentId));
            if (requiredCollectiveCapable && (declared.Length != 1 || !string.Equals(declared[0].RoleId, Phase8AMappingSemanticRoles.Collective, StringComparison.Ordinal)))
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "Every discovered required collective-capable component must be present as an explicit collective semantic ordinal; caller role lists cannot hide candidates.", component.ComponentId));
            if (declared.Length == 1 && string.Equals(declared[0].RoleId, Phase8AMappingSemanticRoles.Compute, StringComparison.Ordinal) && !computeCapable)
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "A compute semantic ordinal must resolve a component with an exact workload operation capability.", component.ComponentId));
            if (declared.Length == 1 && string.Equals(declared[0].RoleId, Phase8AMappingSemanticRoles.Collective, StringComparison.Ordinal) && !mappingCollectiveCapable)
                issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "A collective semantic ordinal must resolve a component with a supported mapping collective capability.", component.ComponentId));
        }
        var discoveredIds = snapshot.Components.Select(item => item.ComponentId).ToHashSet(StringComparer.Ordinal);
        foreach (var declared in input.ComponentOrdinals.Where(item =>
                     (string.Equals(item.RoleId, Phase8AMappingSemanticRoles.Compute, StringComparison.Ordinal) ||
                      string.Equals(item.RoleId, Phase8AMappingSemanticRoles.Collective, StringComparison.Ordinal)) &&
                     !discoveredIds.Contains(item.ComponentId)))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.InvalidNormalizedInput, "$.normalizedInput.componentOrdinals", "Compute and collective semantic ordinals must resolve a Core-discovered compiled capability; undiscovered graph components cannot be promoted by caller metadata.", declared.ComponentId));
    }

    private static IReadOnlyList<ValidatedLowering>? ValidateLowerings(
        Phase8ATopologyAwareMappingInput input,
        IReadOnlyList<Phase8ALoweringAuthority> authorities,
        List<Phase8AMappingProblemIssue> issues)
    {
        var result = new List<ValidatedLowering>();
        var duplicate = authorities.GroupBy(item => item.Plan.OperationId, StringComparer.Ordinal).Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var id in duplicate) issues.Add(Error(Phase8AMappingProblemIssueCodes.DuplicateLoweringPlan, "$.loweringAuthorities", "More than one lowering authority claims the operation.", id));
        foreach (var operation in input.Workload.Operations)
        {
            var matches = authorities.Where(item => string.Equals(item.Plan.OperationId, operation.OperationId, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                issues.Add(Error(Phase8AMappingProblemIssueCodes.MissingLoweringPlan, "$.loweringAuthorities", "Every normalized operation requires one exact lowering authority.", operation.OperationId));
                continue;
            }
            if (matches.Length != 1) continue;
            var authority = matches[0];
            var computed = Phase8AMappingAuthorityHasher.ComputeLoweringSemanticHash(authority.Plan);
            if (!string.Equals(authority.SemanticHashAlgorithmId, Phase8ALoweringAuthority.SemanticHashAlgorithm, StringComparison.Ordinal) ||
                !string.Equals(authority.SemanticHash, computed, StringComparison.Ordinal))
            {
                issues.Add(Error(Phase8AMappingProblemIssueCodes.LoweringHashMismatch, "$.loweringAuthorities.semanticHash", "Lowering authority hash does not match its complete public semantic content.", operation.OperationId));
                continue;
            }
            var binding = input.OperationBindings.SingleOrDefault(item => string.Equals(item.OperationId, operation.OperationId, StringComparison.Ordinal));
            if (binding is null || !ValidateLoweringSemantics(operation, binding, authority.Plan, issues)) continue;
            result.Add(new ValidatedLowering(authority, operation, binding));
        }
        foreach (var extra in authorities.Where(item => input.Workload.Operations.All(operation => !string.Equals(operation.OperationId, item.Plan.OperationId, StringComparison.Ordinal))))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.LoweringMismatch, "$.loweringAuthorities", "A lowering authority refers to no normalized operation.", extra.Plan.OperationId));
        ValidateWorkloadWideLoweringIds(result.SelectMany(item => item.Authority.Plan.OperandTiles.Select(tile => tile.TileId)), "operand tile", issues);
        ValidateWorkloadWideLoweringIds(result.SelectMany(item => item.Authority.Plan.OperationTiles.Select(tile => tile.OperationTileId)), "operation tile", issues);
        ValidateWorkloadWideLoweringIds(result.SelectMany(item =>
            item.Authority.Plan.OperandTiles.Select(tile => tile.TileId)
                .Concat(item.Authority.Plan.OutputTiles.Select(tile => tile.TileId))
                .Concat(item.Authority.Plan.OperationTiles.Select(tile => tile.OperationTileId))
                .Concat(item.Authority.Plan.CollectiveIntents.Select(intent => intent.IntentId))), "cross-category lowering", issues);
        ValidateWorkloadWideLoweringIds(result.SelectMany(item => item.Authority.Plan.OutputTiles.Select(tile => tile.TileId)), "output tile", issues);
        ValidateWorkloadWideLoweringIds(result.SelectMany(item => item.Authority.Plan.CollectiveIntents.Select(intent => intent.IntentId)), "collective intent", issues);
        return issues.Any(item => item.Severity == ValidationSeverity.Error) ? null : result.OrderBy(item => item.Binding.OperationOrdinal).ToArray();
    }

    private static void ValidateWorkloadWideLoweringIds(IEnumerable<string> ids, string kind, List<Phase8AMappingProblemIssue> issues)
    {
        foreach (var duplicateId in ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.LoweringMismatch, "$.loweringAuthorities", $"Lowering {kind} identities must be unique across the workload.", duplicateId));
    }

    private static bool ValidateLoweringSemantics(
        ReferenceMappingWorkloadOperation operation,
        Phase8AOperationTensorBinding binding,
        Phase8AMatMulLoweringPlan plan,
        List<Phase8AMappingProblemIssue> issues)
    {
        var ok = true;
        void Bad(string message, string location = "$.loweringAuthorities.plan")
        {
            ok = false;
            issues.Add(Error(Phase8AMappingProblemIssueCodes.LoweringMismatch, location, message, operation.OperationId));
        }
        if (!string.Equals(plan.OperationId, operation.OperationId, StringComparison.Ordinal) || plan.OperationTiles.Count == 0) Bad("Lowering operation identity and non-empty tile surface must match the normalized operation.");
        if (!UniqueIds(plan.OperandTiles.Select(item => item.TileId)) || !UniqueIds(plan.OutputTiles.Select(item => item.TileId)) ||
            !UniqueIds(plan.OperationTiles.Select(item => item.OperationTileId)) || !UniqueIds(plan.CollectiveIntents.Select(item => item.IntentId)))
            Bad("Lowering tile and collective identities must be unique.");
        if (plan.CollectiveIntents.Any(item => string.IsNullOrWhiteSpace(item.GroupKey)) || !UniqueIds(plan.CollectiveIntents.Select(item => item.GroupKey)))
            Bad("Collective group keys must be non-empty and unique within the lowering plan.");

        var mIntervals = AxisIntervals(plan.OperationTiles.Select(item => item.MRange), operation.Geometry.M, "M", Bad);
        var kIntervals = AxisIntervals(plan.OperationTiles.Select(item => item.KRange), operation.Geometry.K, "K", Bad);
        var nIntervals = AxisIntervals(plan.OperationTiles.Select(item => item.NRange), operation.Geometry.N, "N", Bad);
        var partitions = plan.OperationTiles.Select(item => item.PartitionKind).Distinct().ToArray();
        Phase8AMatMulPartitionKind? expectedPartition = kIntervals.Count > 1 && nIntervals.Count > 1
            ? Phase8AMatMulPartitionKind.Hybrid
            : kIntervals.Count > 1
                ? Phase8AMatMulPartitionKind.K
                : nIntervals.Count > 1 ? Phase8AMatMulPartitionKind.N : null;
        if (partitions.Length != 1 || expectedPartition.HasValue && partitions[0] != expectedPartition.Value)
            Bad("Every compute tile must carry one partition label matching the actual K/N/hybrid shard surface.");
        var expectedTriples = from m in mIntervals from k in kIntervals from n in nIntervals select RangeKey(m, k, n);
        var actualTriples = plan.OperationTiles.GroupBy(item => RangeKey(item.MRange, item.KRange, item.NRange), StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var expectedSet = expectedTriples.ToHashSet(StringComparer.Ordinal);
        if (expectedSet.Count != plan.OperationTiles.Count || actualTriples.Any(pair => pair.Value != 1) || !expectedSet.SetEquals(actualTriples.Keys))
            Bad("Operation tiles must form the exact Cartesian product of contiguous M/K/N intervals with no gap, overlap, or duplicate.");

        var operands = plan.OperandTiles.GroupBy(item => item.TileId, StringComparer.Ordinal).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var outputs = plan.OutputTiles.GroupBy(item => item.TileId, StringComparer.Ordinal).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var partialByRange = new Dictionary<string, Phase8ALoweredOperationTile>(StringComparer.Ordinal);
        foreach (var tile in plan.OperationTiles)
        {
            if (!operands.TryGetValue(tile.ActivationTileId, out var activation) || !operands.TryGetValue(tile.WeightTileId, out var weight) || !outputs.TryGetValue(tile.OutputTileId, out var output))
            {
                Bad("Every compute tile must resolve exact activation, weight, and partial-output tiles.");
                continue;
            }
            var precision = operation.Precision.ToString();
            if (!string.Equals(tile.OperationId, operation.OperationId, StringComparison.Ordinal) ||
                !ShapeEquals(tile.ValidShape, tile.MRange.Extent, tile.NRange.Extent) || tile.PaddedShape.Dimensions.Count != 3 ||
                tile.PaddedShape.Dimensions[0] < tile.MRange.Extent || tile.PaddedShape.Dimensions[1] < tile.KRange.Extent || tile.PaddedShape.Dimensions[2] < tile.NRange.Extent)
                Bad("Compute-tile ranges, valid shape, and padded M/K/N shape are inconsistent.");
            if (!OperandMatches(activation, binding.ActivationTensorId, Phase8ATensorRoleIds.Activation, precision,
                    tile.MRange, tile.KRange, new MappingIndexRange(0, 0), tile.PaddedShape.Dimensions[0], tile.PaddedShape.Dimensions[1]))
                Bad("Activation operand tensor, role, ranges, precision, shapes, or padding differ from exact M/K semantics.");
            if (!OperandMatches(weight, binding.WeightTensorId, Phase8ATensorRoleIds.Weight, precision,
                    new MappingIndexRange(0, 0), tile.KRange, tile.NRange, tile.PaddedShape.Dimensions[1], tile.PaddedShape.Dimensions[2]))
                Bad("Weight operand tensor, role, ranges, precision, shapes, or padding differ from exact K/N semantics.");
            if (!OutputMatches(output, binding.OutputTensorId, Phase8ATensorRoleIds.PartialOutput, precision,
                    tile.MRange, tile.NRange, tile.PaddedShape.Dimensions[0], tile.PaddedShape.Dimensions[2]))
                Bad("Partial output tensor, role, ranges, precision, shapes, or padding differ from exact M/N semantics.");
            partialByRange[RangeKey(tile.MRange, tile.KRange, tile.NRange)] = tile;
        }

        var referencedOperandIds = plan.OperationTiles.SelectMany(item => new[] { item.ActivationTileId, item.WeightTileId }).ToHashSet(StringComparer.Ordinal);
        if (operands.Count != referencedOperandIds.Count || operands.Keys.Any(id => !referencedOperandIds.Contains(id)))
            Bad("Lowering operand set must contain exactly activation and weight tiles referenced by compute operations; extras are forbidden.");

        var expectedCollectiveIds = new HashSet<string>(StringComparer.Ordinal);
        var shardResults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in mIntervals)
        foreach (var n in nIntervals)
        {
            var contributors = kIntervals.Select(k => partialByRange.GetValueOrDefault(RangeKey(m, k, n))?.OutputTileId ?? "").ToArray();
            if (contributors.Any(string.IsNullOrEmpty)) continue;
            if (contributors.Length == 1) { shardResults[RangeKey(m, n)] = contributors[0]; continue; }
            var sums = plan.CollectiveIntents.Where(item => item.KindId == Phase8ACollectiveIntentKinds.Sum && Same(item.MRange, m) && Same(item.NRange, n)).ToArray();
            if (sums.Length != 1 || sums[0].StageOrder != 0 ||
                !string.Equals(sums[0].GroupKey, ExpectedCollectiveGroupKey(operation.OperationId, m, n), StringComparison.Ordinal) ||
                !string.Equals(sums[0].PrecisionId, operation.Precision.ToString(), StringComparison.Ordinal) ||
                !contributors.SequenceEqual(sums[0].ContributorTileIds) ||
                !outputs.TryGetValue(sums[0].ResultTileId, out var reduced) ||
                !OutputMatches(reduced, binding.OutputTensorId, Phase8ATensorRoleIds.ReducedOutput, operation.Precision.ToString(), m, n,
                    partialByRange[RangeKey(m, kIntervals[0], n)].PaddedShape.Dimensions[0], partialByRange[RangeKey(m, kIntervals[0], n)].PaddedShape.Dimensions[2]))
                Bad("Every multi-K M/N shard requires one exact stage-0 Sum over ordered partial outputs and one reduced-output tile.");
            else { expectedCollectiveIds.Add(sums[0].IntentId); shardResults[RangeKey(m, n)] = sums[0].ResultTileId; }
        }

        var finals = new List<string>();
        foreach (var m in mIntervals)
        {
            var contributors = nIntervals.Select(n => shardResults.GetValueOrDefault(RangeKey(m, n)) ?? "").ToArray();
            if (contributors.Any(string.IsNullOrEmpty)) continue;
            if (contributors.Length == 1) { finals.Add(contributors[0]); continue; }
            var concats = plan.CollectiveIntents.Where(item => item.KindId == Phase8ACollectiveIntentKinds.Concat && Same(item.MRange, m) && item.NRange.Offset == 0 && item.NRange.Extent == operation.Geometry.N).ToArray();
            var paddedMValues = plan.OperationTiles.Where(item => Same(item.MRange, m) && item.PaddedShape.Dimensions.Count == 3).Select(item => item.PaddedShape.Dimensions[0]).Distinct().ToArray();
            if (concats.Length != 1 || paddedMValues.Length != 1 || concats[0].StageOrder != 1 ||
                !string.Equals(concats[0].GroupKey, ExpectedCollectiveGroupKey(operation.OperationId, m, new MappingIndexRange(0, operation.Geometry.N)), StringComparison.Ordinal) ||
                !string.Equals(concats[0].PrecisionId, operation.Precision.ToString(), StringComparison.Ordinal) ||
                !contributors.SequenceEqual(concats[0].ContributorTileIds) ||
                !outputs.TryGetValue(concats[0].ResultTileId, out var final) ||
                !OutputMatches(final, binding.OutputTensorId, Phase8ATensorRoleIds.FinalOutput, operation.Precision.ToString(), m,
                    new MappingIndexRange(0, operation.Geometry.N), paddedMValues.Length == 1 ? paddedMValues[0] : 0, operation.Geometry.N))
                Bad("Every multi-N M shard requires one exact stage-1 Concat over ordered disjoint N shards and one final-output tile.");
            else { expectedCollectiveIds.Add(concats[0].IntentId); finals.Add(concats[0].ResultTileId); }
        }
        if (plan.CollectiveIntents.Count != expectedCollectiveIds.Count || plan.CollectiveIntents.Any(item => !expectedCollectiveIds.Contains(item.IntentId)))
            Bad("Lowering collectives must contain exactly the required Sum and Concat intents and no extras.");
        if (!finals.SequenceEqual(plan.FinalOutputTileIds)) Bad("Final output ids must exactly equal semantic increasing-M results; missing finals are forbidden.");
        var referencedOutputIds = plan.OperationTiles.Select(item => item.OutputTileId).Concat(plan.CollectiveIntents.Select(item => item.ResultTileId)).ToHashSet(StringComparer.Ordinal);
        if (outputs.Count != referencedOutputIds.Count || outputs.Keys.Any(id => !referencedOutputIds.Contains(id))) Bad("Lowering output set must contain exactly partial, reduced, and final results referenced by the plan.");
        return ok;
    }
    private static string ExpectedCollectiveGroupKey(string operationId, MappingIndexRange m, MappingIndexRange n) =>
        $"{operationId}|m={m.Offset}:{m.Extent}|n={n.Offset}:{n.Extent}";

    private static Phase8AExactRouteMatrix BuildRouteMatrix(
        Phase8ATopologyAwareMappingInput input,
        HardwareGraph graph,
        Phase8ALogicalPathCatalog catalog,
        Phase8ACapabilityAuthority capability,
        Phase8AMappingPolicyBudget budget,
        Phase8AMappingProblemRequest request,
        List<Phase8AMappingProblemIssue> issues,
        ref bool complete,
        SortedSet<string> truncationReasons)
    {
        var pairs = new List<Phase8AExactRoutePair>();
        foreach (var path in catalog.Entries)
        {
            if (path.Hops.Count == 0)
            {
                issues.Add(Error(Phase8AMappingProblemIssueCodes.RouteInvalid, "$.logicalPathCatalog.entries", "Mapping routes require at least one explicit directed hop.", path.PathId));
                continue;
            }
            var first = path.Hops[0];
            var last = path.Hops[^1];
            var sources = ResolveRouteEndpoints(input, capability, first.SourceComponentId, first.SourcePortName, true);
            var destinations = ResolveRouteEndpoints(input, capability, last.DestinationComponentId, last.DestinationPortName, false);
            if (sources.Count == 0 || destinations.Count == 0) continue;
            var pathOrdinal = input.PathOrdinals.Single(item => string.Equals(item.PathId, path.PathId, StringComparison.Ordinal)).Ordinal;
            foreach (var source in sources)
            foreach (var destination in destinations)
                pairs.Add(new Phase8AExactRoutePair(source, destination, path.PathId, pathOrdinal,
                    checked(path.Hops.Sum(item => (long)item.LatencyCycles)), path.Hops.Sum(item => item.RouteGeometryLengthMicrometers)));
        }
        var mandatoryPathIds = request.ManualOperandPathConstraints.Select(item => item.PathId).ToHashSet(StringComparer.Ordinal);
        var retained = new List<Phase8AExactRoutePair>();
        foreach (var group in pairs.Distinct().GroupBy(item => (
                     item.Source.ComponentId, item.Source.HardwarePortName, item.Source.CapabilityPortId, item.Source.SemanticRoleId, item.Source.ComponentOrdinal,
                     item.Destination.ComponentId, item.Destination.HardwarePortName, item.Destination.CapabilityPortId, item.Destination.SemanticRoleId, item.Destination.ComponentOrdinal)))
        {
            var ordered = group.OrderBy(item => item.PathOrdinal).ThenBy(item => item.PathId, StringComparer.Ordinal).ToArray();
            var selected = ordered.Take(budget.MaxPathsPerEndpointPair)
                .Concat(ordered.Where(item => mandatoryPathIds.Contains(item.PathId)))
                .Distinct()
                .OrderBy(item => item.PathOrdinal)
                .ThenBy(item => item.PathId, StringComparer.Ordinal)
                .ToArray();
            if (selected.Length < ordered.Length)
            {
                complete = false;
                truncationReasons.Add("endpoint-path-cap");
            }
            retained.AddRange(selected);
        }
        return new Phase8AExactRouteMatrix(retained);
    }

    private static IReadOnlyList<Phase8AExactRouteEndpoint> ResolveRouteEndpoints(
        Phase8ATopologyAwareMappingInput input,
        Phase8ACapabilityAuthority capability,
        string componentId,
        string hardwarePortName,
        bool source)
    {
        var ordinal = input.ComponentOrdinals.SingleOrDefault(item => string.Equals(item.ComponentId, componentId, StringComparison.Ordinal));
        if (ordinal is null) return [];
        var result = new List<Phase8AExactRouteEndpoint>();
        if (source)
        {
            foreach (var ingress in input.OperandIngressBindings.Where(item => string.Equals(item.ProducerComponentId, componentId, StringComparison.Ordinal) && string.Equals(item.ProducerHardwarePortName, hardwarePortName, StringComparison.Ordinal)))
                result.Add(new Phase8AExactRouteEndpoint(componentId, hardwarePortName, "", ingress.OperandRoleId, ordinal.Ordinal));
        }
        foreach (var binding in capability.PortBindings.Where(item => string.Equals(item.ComponentId, componentId, StringComparison.Ordinal) && string.Equals(item.HardwarePortName, hardwarePortName, StringComparison.Ordinal)))
        {
            var port = capability.Snapshot.Components.Single(item => string.Equals(item.ComponentId, componentId, StringComparison.Ordinal)).Ports.Single(item => string.Equals(item.PortId, binding.CapabilityPortId, StringComparison.Ordinal));
            if ((source && port.DirectionId == "output") || (!source && port.DirectionId == "input"))
                result.Add(new Phase8AExactRouteEndpoint(componentId, hardwarePortName, port.PortId, port.SemanticRoleId, ordinal.Ordinal));
        }
        return result.Distinct().ToArray();
    }

    private static IReadOnlyList<Phase8AStorageSelectorState> BuildStorageSelectors(
        Phase8ATopologyAwareMappingInput input,
        WorkloadMappingV2 mapping,
        CapabilitySnapshot snapshot,
        IReadOnlyList<ValidatedLowering> lowerings,
        List<Phase8AMappingProblemIssue> issues)
    {
        var resources = snapshot.Components.SelectMany(component => component.StorageCapabilities.Select(storage => (component, storage))).ToArray();
        if (input.StorageSelectorOrdinals.Count != resources.Length ||
            input.StorageSelectorOrdinals.GroupBy(item => (item.ComponentId, item.ResourceId)).Any(group => group.Count() != 1) ||
            input.StorageSelectorOrdinals.GroupBy(item => (item.ComponentId, item.Ordinal)).Any(group => group.Count() != 1) ||
            input.StorageSelectorOrdinals.Any(item => item.Ordinal < 0 || resources.All(resource => !string.Equals(resource.component.ComponentId, item.ComponentId, StringComparison.Ordinal) || !string.Equals(resource.storage.ResourceId, item.ResourceId, StringComparison.Ordinal))))
        {
            issues.Add(Error(Phase8AMappingProblemIssueCodes.StorageLedgerInvalid, "$.normalizedInput.storageSelectorOrdinals", "Explicit semantic storage ordinals must exactly cover every discovered selector and be unique per component."));
            return [];
        }
        var result = new List<Phase8AStorageSelectorState>();
        foreach (var (component, storage) in resources)
        {
            var sameLevelCount = component.StorageCapabilities.Count(item => string.Equals(item.StorageLevelId, storage.StorageLevelId, StringComparison.Ordinal));
            if (sameLevelCount != 1)
            {
                issues.Add(Error(Phase8AMappingProblemIssueCodes.StorageLedgerInvalid, "$.capability.storage", "Mapping 2.0 base placements identify levels; discovered resources must therefore have unique levels per component.", storage.ResourceId));
                continue;
            }
            var occupied = mapping.OperandPlacements.Where(item => string.Equals(item.StorageComponentId, component.ComponentId, StringComparison.Ordinal) && string.Equals(item.StorageLevelId, storage.StorageLevelId, StringComparison.Ordinal))
                .Select(item => new Phase8AStorageOccupiedInterval(item.PlacementId, item.TileId, item.AddressBits, item.SizeBits, VerifiedBaseReuseKey(item, storage, lowerings, issues)))
                .OrderBy(item => item.AddressBits).ToArray();
            if (storage.CapacityBits <= 0 || storage.AlignmentBits <= 0 || storage.AllocationGranularityBits <= 0 || storage.ResidentSlots <= 0 ||
                occupied.Any(item => item.AddressBits < 0 || item.SizeBits <= 0 || item.AddressBits % storage.AlignmentBits != 0 || item.AddressBits > storage.CapacityBits - item.SizeBits) ||
                occupied.Zip(occupied.Skip(1), (left, right) => left.AddressBits + left.SizeBits > right.AddressBits).Any(overlap => overlap) || occupied.Length > storage.ResidentSlots)
            {
                issues.Add(Error(Phase8AMappingProblemIssueCodes.StorageLedgerInvalid, "$.baseMapping.operandPlacements", "Base storage occupancy must be aligned, non-overlapping, in range, and within resident slots.", storage.ResourceId));
                continue;
            }
            var ordinal = input.StorageSelectorOrdinals.Single(item => string.Equals(item.ComponentId, component.ComponentId, StringComparison.Ordinal) && string.Equals(item.ResourceId, storage.ResourceId, StringComparison.Ordinal)).Ordinal;
            result.Add(new Phase8AStorageSelectorState(component.ComponentId, storage.ResourceId, storage.StorageLevelId, ordinal,
                storage.CapacityBits, storage.AlignmentBits, storage.AllocationGranularityBits, storage.ResidentSlots, storage.SupportsReuse, occupied));
        }
        return result;
    }

    private static string VerifiedBaseReuseKey(
        OperandPlacement placement,
        ComponentStorageCapabilitySnapshot storage,
        IReadOnlyList<ValidatedLowering> lowerings,
        List<Phase8AMappingProblemIssue> issues)
    {
        if (!string.Equals(placement.OperandRoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal)) return "";
        var authorityMatches = lowerings.Where(item => string.Equals(item.Operation.OperationId, placement.OperationId, StringComparison.Ordinal)).Take(2).ToArray();
        var tileMatches = authorityMatches.Length == 1
            ? authorityMatches[0].Authority.Plan.OperandTiles.Where(tile =>
                string.Equals(tile.TileId, placement.TileId, StringComparison.Ordinal) &&
                string.Equals(tile.TensorId, placement.TensorId, StringComparison.Ordinal) &&
                string.Equals(tile.RoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal)).Take(2).ToArray()
            : [];
        var artifactMatches = authorityMatches.Length == 1
            ? authorityMatches[0].Operation.Tensors.Where(artifact => string.Equals(artifact.TensorId, placement.TensorId, StringComparison.Ordinal)).Take(2).ToArray()
            : [];
        if (authorityMatches.Length != 1 || tileMatches.Length != 1 || artifactMatches.Length != 1)
        {
            issues.Add(Error(Phase8AMappingProblemIssueCodes.StorageLedgerInvalid, "$.baseMapping.operandPlacements", "Resident weight placement must resolve one exact current lowering tile and tensor artifact before reuse.", placement.PlacementId));
            return "";
        }
        var expected = Phase8AWeightResidencyKey.Compute(artifactMatches[0], tileMatches[0]);
        var requiredSizeBits = AlignUpSize(
            RequiredBits(tileMatches[0].PaddedShape, tileMatches[0].PrecisionId),
            storage.AllocationGranularityBits);
        var expectedPlacementId = Phase8AWeightResidencyKey.ComputePlacementId(
            placement.StorageComponentId, storage.ResourceId,
            placement.AddressBits, requiredSizeBits, expected);
        if (!string.Equals(placement.ReuseGroupId, expected, StringComparison.Ordinal) || !string.Equals(placement.PlacementId, expectedPlacementId, StringComparison.Ordinal) ||
            placement.SizeBits != requiredSizeBits)
        {
            issues.Add(Error(Phase8AMappingProblemIssueCodes.StorageLedgerInvalid, "$.baseMapping.operandPlacements", "Resident weight identity, placement id, and granularity-rounded size must be recomputable from the current artifact, exact ranges, padded shape, precision, storage contract, and target.", placement.PlacementId));
            return "";
        }
        return expected;
    }

    private static IReadOnlyList<Phase8AMappingOperationProblem> BuildOperationProblems(
        Phase8ATopologyAwareMappingInput input,
        IReadOnlyList<ValidatedLowering> lowerings,
        Phase8ACapabilityAuthority capability,
        Phase8AExactRouteMatrix routes,
        Phase8AMappingPolicyBudget budget,
        Phase8AMappingProblemRequest request,
        List<Phase8AMappingHardConstraintRejection> rejections,
        List<Phase8AMappingProblemIssue> issues,
        ref long visited,
        ref long retained,
        ref bool complete,
        SortedSet<string> truncationReasons)
    {
        var result = new List<Phase8AMappingOperationProblem>();
        var computeOrdinals = input.ComponentOrdinals.Where(item => item.RoleId == Phase8AMappingSemanticRoles.Compute).OrderBy(item => item.Ordinal).ToArray();
        foreach (var lowering in lowerings)
        foreach (var tile in lowering.Authority.Plan.OperationTiles.OrderBy(item => item.MRange.Offset).ThenBy(item => item.KRange.Offset).ThenBy(item => item.NRange.Offset))
        {
            var activation = lowering.Authority.Plan.OperandTiles.Single(item => string.Equals(item.TileId, tile.ActivationTileId, StringComparison.Ordinal));
            var weight = lowering.Authority.Plan.OperandTiles.Single(item => string.Equals(item.TileId, tile.WeightTileId, StringComparison.Ordinal));
            var output = lowering.Authority.Plan.OutputTiles.Single(item => string.Equals(item.TileId, tile.OutputTileId, StringComparison.Ordinal));
            var activationIngress = input.OperandIngressBindings.Single(item => item.OperationId == lowering.Operation.OperationId && item.OperandRoleId == Phase8ATensorRoleIds.Activation);
            var weightIngress = input.OperandIngressBindings.Single(item => item.OperationId == lowering.Operation.OperationId && item.OperandRoleId == Phase8ATensorRoleIds.Weight);
            var options = new List<Phase8AMappingTargetOption>();
            foreach (var semantic in computeOrdinals)
            {
                var component = capability.Snapshot.Components.SingleOrDefault(item => string.Equals(item.ComponentId, semantic.ComponentId, StringComparison.Ordinal));
                if (component is null) continue;
                if (!component.OperationKindIds.Contains(lowering.Operation.OperationTypeId, StringComparer.Ordinal)) { Reject(tile.OperationTileId, component.ComponentId, Phase8AMappingProblemIssueCodes.OperationUnsupported, rejections); continue; }
                if (!component.PrecisionIds.Contains(lowering.Operation.Precision.ToString(), StringComparer.Ordinal)) { Reject(tile.OperationTileId, component.ComponentId, Phase8AMappingProblemIssueCodes.PrecisionUnsupported, rejections); continue; }
                if (!ShapeFits(component, tile)) { Reject(tile.OperationTileId, component.ComponentId, Phase8AMappingProblemIssueCodes.ShapeUnsupported, rejections); continue; }
                var activationPorts = component.Ports.Where(item => item.DirectionId == "input" && item.SemanticRoleId == Phase8ATensorRoleIds.Activation).ToArray();
                var weightPorts = component.Ports.Where(item => item.DirectionId == "input" && item.SemanticRoleId == Phase8ATensorRoleIds.Weight).ToArray();
                var resultPorts = component.Ports.Where(item => item.DirectionId == "output" && item.SemanticRoleId == Phase8AMappingSemanticRoles.Result).ToArray();
                if (activationPorts.Length != 1 || weightPorts.Length != 1 || resultPorts.Length != 1 ||
                    !PortSupportsTensor(activationPorts[0], activation.PrecisionId) ||
                    !PortSupportsTensor(weightPorts[0], weight.PrecisionId) ||
                    !PortSupportsTensor(resultPorts[0], output.PrecisionId))
                { Reject(tile.OperationTileId, component.ComponentId, Phase8AMappingProblemIssueCodes.PortUnsupported, rejections); continue; }
                var activationHardware = HardwarePortName(capability, component.ComponentId, activationPorts[0].PortId);
                var weightHardware = HardwarePortName(capability, component.ComponentId, weightPorts[0].PortId);
                var activationPaths = ExactPaths(routes, activationIngress.ProducerComponentId, activationIngress.ProducerHardwarePortName, component.ComponentId, activationHardware, activationPorts[0].PortId);
                var weightPaths = ExactPaths(routes, weightIngress.ProducerComponentId, weightIngress.ProducerHardwarePortName, component.ComponentId, weightHardware, weightPorts[0].PortId);
                if (activationPaths.Count == 0 || weightPaths.Count == 0) { Reject(tile.OperationTileId, component.ComponentId, Phase8AMappingProblemIssueCodes.ReachabilityMissing, rejections); continue; }
                if (!ApplyManualPath(request, tile.OperationTileId, Phase8ATensorRoleIds.Activation, ref activationPaths) ||
                    !ApplyManualPath(request, tile.OperationTileId, Phase8ATensorRoleIds.Weight, ref weightPaths)) continue;
                BoundPaths(ref activationPaths, budget, ref complete, truncationReasons);
                BoundPaths(ref weightPaths, budget, ref complete, truncationReasons);
                var bits = RequiredBits(weight.PaddedShape, weight.PrecisionId);
                var weightArtifact = lowering.Operation.Tensors.Single(item => item.TensorId == lowering.Binding.WeightTensorId);
                foreach (var storage in component.StorageCapabilities)
                {
                    if (!storage.SupportedOperandRoleIds.Contains(Phase8ATensorRoleIds.Weight, StringComparer.Ordinal) || !storage.SupportedPrecisionIds.Contains(weight.PrecisionId, StringComparer.Ordinal) || storage.CapacityBits < bits || storage.ResidentSlots <= 0 ||
                        !string.Equals(storage.PreloadPortId, weightPorts[0].PortId, StringComparison.Ordinal)) continue;
                    var storageOrdinal = input.StorageSelectorOrdinals.Single(item => item.ComponentId == component.ComponentId && item.ResourceId == storage.ResourceId).Ordinal;
                    options.Add(new Phase8AMappingTargetOption(semantic.Ordinal, storageOrdinal, component.ComponentId, activationPorts[0].PortId, weightPorts[0].PortId, resultPorts[0].PortId,
                        storage.ResourceId, storage.StorageLevelId, bits, WeightReuseKey(weight, weightArtifact), activationPaths, weightPaths));
                }
            }
            var manual = request.ManualTargetConstraints.Where(item => item.OperationTileId == tile.OperationTileId).ToArray();
            if (manual.Length > 1) issues.Add(Error(Phase8AMappingProblemIssueCodes.DuplicateManualConstraint, "$.manualTargetConstraints", "Only one target lock is permitted per tile.", tile.OperationTileId));
            if (manual.Length == 1)
                options = options.Where(item => item.TargetComponentId == manual[0].TargetComponentId && item.WeightPortId == manual[0].TargetPortId && item.StorageResourceId == manual[0].StorageResourceId).ToList();
            visited = checked(visited + options.Count);


            if (options.Count > budget.MaxTargetOptionsPerTile) { complete = false; truncationReasons.Add("max-target-options-per-tile"); options = options.OrderBy(item => item.TargetOrdinal).ThenBy(item => item.StorageOrdinal).Take(budget.MaxTargetOptionsPerTile).ToList(); }
            retained = checked(retained + options.Count);
            var hasManualConstraint = manual.Length == 1 || request.ManualOperandPathConstraints.Any(item => item.OperationTileId == tile.OperationTileId);
            if (options.Count == 0) issues.Add(Error(hasManualConstraint ? Phase8AMappingProblemIssueCodes.ManualConstraintInfeasible : Phase8AMappingProblemIssueCodes.NoFeasibleTarget,
                "$.operations", "No exact hard-feasible target remains for the lowered compute tile.", tile.OperationTileId));
            else result.Add(new Phase8AMappingOperationProblem(lowering.Binding.OperationOrdinal, tile, activation, weight, output, options));
        }
        return result;
    }

    private static IReadOnlyList<Phase8AMappingCollectiveRequirement> BuildCollectiveProblems(
        Phase8ATopologyAwareMappingInput input,
        IReadOnlyList<ValidatedLowering> lowerings,
        Phase8ACapabilityAuthority capability,
        Phase8AMappingPolicyBudget budget,
        Phase8AMappingProblemRequest request,
        List<Phase8AMappingHardConstraintRejection> rejections,
        List<Phase8AMappingProblemIssue> issues,
        ref long visited,
        ref long retained,
        ref bool complete,
        SortedSet<string> truncationReasons)
    {
        var result = new List<Phase8AMappingCollectiveRequirement>();
        var ordinals = input.ComponentOrdinals.Where(item => item.RoleId == Phase8AMappingSemanticRoles.Collective).OrderBy(item => item.Ordinal).ToArray();
        foreach (var lowering in lowerings)
        foreach (var intent in lowering.Authority.Plan.CollectiveIntents.OrderBy(item => item.StageOrder).ThenBy(item => item.MRange.Offset).ThenBy(item => item.NRange.Offset))
        {
            var operationKind = intent.KindId == Phase8ACollectiveIntentKinds.Sum ? Phase8ACollectiveCapabilityIds.GroupedVectorSum : Phase8ACollectiveCapabilityIds.TensorAssembly;
            var inputRole = intent.KindId == Phase8ACollectiveIntentKinds.Sum ? Phase8AMappingSemanticRoles.PartialVector : Phase8AMappingSemanticRoles.AssemblyInput;
            var options = new List<Phase8AMappingCollectiveTargetOption>();
            foreach (var semantic in ordinals)
            {
                var component = capability.Snapshot.Components.SingleOrDefault(item => item.ComponentId == semantic.ComponentId);
                if (component is null || !component.OperationKindIds.Contains(operationKind, StringComparer.Ordinal)) { if (component is not null) Reject(intent.IntentId, component.ComponentId, Phase8AMappingProblemIssueCodes.OperationUnsupported, rejections, "collective-intent"); continue; }
                if (!component.PrecisionIds.Contains(intent.PrecisionId, StringComparer.Ordinal)) { Reject(intent.IntentId, component.ComponentId, Phase8AMappingProblemIssueCodes.PrecisionUnsupported, rejections, "collective-intent"); continue; }
                var inputs = component.Ports.Where(item => item.DirectionId == "input" && item.SemanticRoleId == inputRole).ToArray();
                var outputs = component.Ports.Where(item => item.DirectionId == "output" && item.SemanticRoleId == Phase8AMappingSemanticRoles.Result).ToArray();
                if (inputs.Length != 1 || outputs.Length != 1 || !PortSupportsTensor(inputs[0], intent.PrecisionId) || !PortSupportsTensor(outputs[0], intent.PrecisionId))
                { Reject(intent.IntentId, component.ComponentId, Phase8AMappingProblemIssueCodes.PortUnsupported, rejections, "collective-intent"); continue; }
                var contributorElements = intent.ContributorTileIds.Select(id => lowering.Authority.Plan.OutputTiles.Single(item => item.TileId == id).ValidShape.Dimensions.Aggregate(1L, (current, dimension) => checked(current * dimension))).DefaultIfEmpty(0).Max();
                var resultElements = checked(intent.MRange.Extent * intent.NRange.Extent);
                if (!PortShapeFits(component, inputRole, contributorElements) || !PortShapeFits(component, Phase8AMappingSemanticRoles.Result, resultElements))
                { Reject(intent.IntentId, component.ComponentId, Phase8AMappingProblemIssueCodes.ShapeUnsupported, rejections, "collective-intent"); continue; }
                options.Add(new Phase8AMappingCollectiveTargetOption(semantic.Ordinal, component.ComponentId, inputs[0].PortId, outputs[0].PortId));
            }
            var manual = request.ManualCollectiveConstraints.Where(item => item.CollectiveIntentId == intent.IntentId).ToArray();
            if (manual.Length > 1) issues.Add(Error(Phase8AMappingProblemIssueCodes.DuplicateManualConstraint, "$.manualCollectiveConstraints", "Only one collective lock is permitted per intent.", intent.IntentId));
            if (manual.Length == 1) options = options.Where(item => item.TargetComponentId == manual[0].TargetComponentId && item.InputPortId == manual[0].InputPortId).ToList();
            visited = checked(visited + options.Count);


            if (options.Count > budget.MaxCollectiveTargetsPerIntent) { complete = false; truncationReasons.Add("max-collective-targets-per-intent"); options = options.OrderBy(item => item.TargetOrdinal).Take(budget.MaxCollectiveTargetsPerIntent).ToList(); }
            retained = checked(retained + options.Count);
            if (options.Count == 0) issues.Add(Error(manual.Length == 1 ? Phase8AMappingProblemIssueCodes.ManualConstraintInfeasible : Phase8AMappingProblemIssueCodes.NoFeasibleCollectiveTarget,
                "$.collectives", "No exact semantic collective target remains.", intent.IntentId));
            else result.Add(new Phase8AMappingCollectiveRequirement(lowering.Binding.OperationOrdinal, intent, operationKind, inputRole, options));
        }
        return result;
    }
    private static void ValidateManualKeys(Phase8AMappingProblemRequest request, List<Phase8AMappingProblemIssue> issues)
    {
        AddDuplicate(request.ManualTargetConstraints.GroupBy(item => item.OperationTileId, StringComparer.Ordinal), "$.manualTargetConstraints", issues);
        AddDuplicate(request.ManualOperandPathConstraints.GroupBy(item => (item.OperationTileId, item.OperandRoleId)), "$.manualOperandPathConstraints", issues);
        AddDuplicate(request.ManualCollectiveConstraints.GroupBy(item => item.CollectiveIntentId, StringComparer.Ordinal), "$.manualCollectiveConstraints", issues);
        if (request.ManualOperandPathConstraints.Any(item => item.OperandRoleId != Phase8ATensorRoleIds.Activation && item.OperandRoleId != Phase8ATensorRoleIds.Weight))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.ManualConstraintInvalid, "$.manualOperandPathConstraints", "Manual operand paths accept only exact activation or weight roles."));
    }

    private static void AddDuplicate<TKey, T>(IEnumerable<IGrouping<TKey, T>> groups, string location, List<Phase8AMappingProblemIssue> issues) =>
        issues.AddRange(groups.Where(group => group.Count() > 1).Select(_ => Error(Phase8AMappingProblemIssueCodes.DuplicateManualConstraint, location, "Manual constraint subjects must be unique.")));

    private static void ValidateManualSubjects(Phase8AMappingProblemRequest request, IReadOnlyList<ValidatedLowering> lowerings, List<Phase8AMappingProblemIssue> issues)
    {
        var operationTileIds = lowerings.SelectMany(item => item.Authority.Plan.OperationTiles).Select(item => item.OperationTileId).ToHashSet(StringComparer.Ordinal);
        var collectiveIntentIds = lowerings.SelectMany(item => item.Authority.Plan.CollectiveIntents).Select(item => item.IntentId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in request.ManualTargetConstraints.Where(item => !operationTileIds.Contains(item.OperationTileId)))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.ManualConstraintInvalid, "$.manualTargetConstraints", "Manual target subject does not resolve an operation tile.", item.OperationTileId));
        foreach (var item in request.ManualOperandPathConstraints.Where(item => !operationTileIds.Contains(item.OperationTileId) || request.LogicalPathCatalog!.Entries.All(path => path.PathId != item.PathId)))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.ManualConstraintInvalid, "$.manualOperandPathConstraints", "Manual operand path subject or exact catalog path is missing.", item.OperationTileId));
        foreach (var item in request.ManualCollectiveConstraints.Where(item => !collectiveIntentIds.Contains(item.CollectiveIntentId)))
            issues.Add(Error(Phase8AMappingProblemIssueCodes.ManualConstraintInvalid, "$.manualCollectiveConstraints", "Manual collective subject does not resolve an intent.", item.CollectiveIntentId));
    }

    private static bool ApplyManualPath(Phase8AMappingProblemRequest request, string tileId, string role, ref IReadOnlyList<string> paths)
    {
        var manual = request.ManualOperandPathConstraints.Where(item => item.OperationTileId == tileId && item.OperandRoleId == role).ToArray();
        if (manual.Length == 1) paths = paths.Where(path => path == manual[0].PathId).ToArray();
        return paths.Count != 0;
    }

    private static void BoundPaths(ref IReadOnlyList<string> paths, Phase8AMappingPolicyBudget budget, ref bool complete, SortedSet<string> reasons)
    {
        if (paths.Count <= budget.MaxPathsPerEndpointPair) return;
        paths = paths.Take(budget.MaxPathsPerEndpointPair).ToArray();
        complete = false;
        reasons.Add("max-paths-per-endpoint-pair");
    }

    private static IReadOnlyList<string> ExactPaths(Phase8AExactRouteMatrix matrix, string sourceComponent, string sourcePort, string destinationComponent, string destinationPort, string destinationCapabilityPort) =>
        matrix.Find(sourceComponent, sourcePort, destinationComponent, destinationPort)
            .Where(item => item.Destination.CapabilityPortId == destinationCapabilityPort)
            .OrderBy(item => item.PathOrdinal).Select(item => item.PathId).Distinct(StringComparer.Ordinal).ToArray();

    private static string HardwarePortName(Phase8ACapabilityAuthority capability, string componentId, string capabilityPortId) =>
        capability.PortBindings.Single(item => item.ComponentId == componentId && item.CapabilityPortId == capabilityPortId).HardwarePortName;

    private static bool ShapeFits(ComponentCapabilitySnapshot capability, Phase8ALoweredOperationTile tile) =>
        ShapeLimit(capability, Phase8ACapabilityShapeKeys.MatMulMaximumM) >= tile.PaddedShape.Dimensions[0] &&
        ShapeLimit(capability, Phase8ACapabilityShapeKeys.MatMulMaximumK) >= tile.PaddedShape.Dimensions[1] &&
        ShapeLimit(capability, Phase8ACapabilityShapeKeys.MatMulMaximumN) >= tile.PaddedShape.Dimensions[2];

    private static bool PortShapeFits(ComponentCapabilitySnapshot capability, string semanticRoleId, long requiredElements)
    {
        if (!capability.ShapeContracts.TryGetValue("port." + semanticRoleId, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
        long capacity = 1;
        foreach (var part in raw.Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var dimension) || dimension <= 0) return false;
            capacity = checked(capacity * dimension);
        }
        return capacity >= requiredElements;
    }

    private static long ShapeLimit(ComponentCapabilitySnapshot capability, string key) =>
        capability.ShapeContracts.TryGetValue(key, out var raw) && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 0;

    private static long RequiredBits(MappingShape shape, string precisionId)
    {
        var elements = shape.Dimensions.Aggregate(1L, (current, dimension) => checked(current * dimension));
        return checked(elements * PrecisionBits(precisionId));
    }

    private static long AlignUpSize(long value, long alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static bool PortSupportsTensor(CapabilityPortSnapshot port, string precisionId) =>
        string.Equals(port.DataTypeId, HardwareDataType.Tensor.ToString(), StringComparison.Ordinal) &&
        string.Equals(port.PrecisionId, precisionId, StringComparison.Ordinal);

    private static int PrecisionBits(string precisionId) => precisionId switch
    {
        nameof(PrecisionKind.FP32) or nameof(PrecisionKind.TF32) or nameof(PrecisionKind.INT32) => 32,
        nameof(PrecisionKind.FP16) or nameof(PrecisionKind.BF16) or nameof(PrecisionKind.INT16) => 16,
        nameof(PrecisionKind.FP8_E4M3) or nameof(PrecisionKind.FP8_E5M2) or nameof(PrecisionKind.INT8) => 8,
        nameof(PrecisionKind.INT4) => 4,
        nameof(PrecisionKind.INT2) => 2,
        nameof(PrecisionKind.Binary) => 1,
        _ => throw new InvalidOperationException("Unsupported concrete precision in validated lowering.")
    };

    private static string WeightReuseKey(Phase8ALoweredOperandTile weight, ReferenceMappingTensorArtifact artifact) =>
        Phase8AWeightResidencyKey.Compute(artifact, weight);

    private static void Reject(string subjectId, string targetId, string code, List<Phase8AMappingHardConstraintRejection> rejections, string kind = "operation-tile") =>
        rejections.Add(new Phase8AMappingHardConstraintRejection(kind, subjectId, targetId, code, "$", "Target failed an exact hard feasibility constraint."));

    private static string ComputeSemanticOrderingHash(Phase8ATopologyAwareMappingInput input, IReadOnlyList<Phase8AMappingOperationProblem> operations, IReadOnlyList<Phase8AMappingCollectiveRequirement> collectives, Phase8AExactRouteMatrix routes) =>
        Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = "sha256/phase8a-semantic-ordering/v1",
            operations = operations.Select(item => new
            {
                item.OperationOrdinal,
                m = new { item.Tile.MRange.Offset, item.Tile.MRange.Extent },
                k = new { item.Tile.KRange.Offset, item.Tile.KRange.Extent },
                n = new { item.Tile.NRange.Offset, item.Tile.NRange.Extent },
                targets = item.TargetOptions.Select(target => new { target.TargetOrdinal, target.StorageOrdinal, activationPaths = PathOrdinals(input, target.ActivationPathIds), weightPaths = PathOrdinals(input, target.WeightPathIds) })
            }),
            collectives = collectives.Select(item => new { item.OperationOrdinal, item.Intent.StageOrder, m = new { item.Intent.MRange.Offset, item.Intent.MRange.Extent }, n = new { item.Intent.NRange.Offset, item.Intent.NRange.Extent }, targets = item.TargetOptions.Select(target => target.TargetOrdinal) }),
            routes = routes.Pairs.Select(item => new { source = new { item.Source.ComponentOrdinal, item.Source.SemanticRoleId }, destination = new { item.Destination.ComponentOrdinal, item.Destination.SemanticRoleId }, item.PathOrdinal })
        });

    private static IReadOnlyList<int> PathOrdinals(Phase8ATopologyAwareMappingInput input, IEnumerable<string> ids) => ids.Select(id => input.PathOrdinals.Single(item => item.PathId == id).Ordinal).OrderBy(value => value).ToArray();

    private static IReadOnlyList<MappingIndexRange> AxisIntervals(IEnumerable<MappingIndexRange> ranges, long extent, string axis, Action<string, string> bad)
    {
        var result = ranges.GroupBy(item => (item.Offset, item.Extent)).Select(group => group.First()).OrderBy(item => item.Offset).ThenBy(item => item.Extent).ToArray();
        long cursor = 0;
        foreach (var range in result)
        {
            if (range.Offset != cursor || range.Extent <= 0 || range.Offset > extent - range.Extent) { bad($"{axis} intervals must be positive, contiguous, and exactly cover the normalized axis.", "$.loweringAuthorities.plan.operationTiles"); return []; }
            cursor = checked(cursor + range.Extent);
        }
        if (cursor != extent) { bad($"{axis} intervals do not exactly cover the normalized axis.", "$.loweringAuthorities.plan.operationTiles"); return []; }
        return result;
    }

    private static bool OperandMatches(Phase8ALoweredOperandTile tile, string tensorId, string role, string precision, MappingIndexRange m, MappingIndexRange k, MappingIndexRange n, long padded0, long padded1) =>
        tile.TensorId == tensorId && tile.RoleId == role && tile.PrecisionId == precision && Same(tile.MRange, m) && Same(tile.KRange, k) && Same(tile.NRange, n) &&
        ShapeEquals(tile.ValidShape, role == Phase8ATensorRoleIds.Activation ? m.Extent : k.Extent, role == Phase8ATensorRoleIds.Activation ? k.Extent : n.Extent) &&
        ShapeEquals(tile.PaddedShape, padded0, padded1) &&
        tile.Padding.MHighPadding == (role == Phase8ATensorRoleIds.Activation ? padded0 - m.Extent : 0) &&
        tile.Padding.KHighPadding == (role == Phase8ATensorRoleIds.Activation ? padded1 - k.Extent : padded0 - k.Extent) &&
        tile.Padding.NHighPadding == (role == Phase8ATensorRoleIds.Activation ? 0 : padded1 - n.Extent) && !tile.Padding.CropRequired && tile.Padding.CropStageId == "none";

    private static bool OutputMatches(Phase8ALoweredOutputTile tile, string tensorId, string role, string precision, MappingIndexRange m, MappingIndexRange n, long paddedM, long paddedN) =>
        tile.TensorId == tensorId && tile.RoleId == role && tile.PrecisionId == precision && Same(tile.MRange, m) && Same(tile.NRange, n) &&
        ShapeEquals(tile.ValidShape, m.Extent, n.Extent) && ShapeEquals(tile.PaddedShape, paddedM, paddedN) &&
        tile.Padding.MHighPadding == paddedM - m.Extent && tile.Padding.KHighPadding == 0 && tile.Padding.NHighPadding == paddedN - n.Extent &&
        tile.Padding.CropRequired == (paddedM != m.Extent || paddedN != n.Extent) && tile.Padding.CropStageId == "final-output-crop";

    private static bool ShapeEquals(MappingShape shape, params long[] expected) => shape.Dimensions.SequenceEqual(expected);
    private static bool Same(MappingIndexRange left, MappingIndexRange right) => left.Offset == right.Offset && left.Extent == right.Extent;
    private static string RangeKey(params MappingIndexRange[] ranges) => string.Join("|", ranges.Select(item => $"{item.Offset}:{item.Extent}"));
    private static bool UniqueIds(IEnumerable<string> ids) { var array = ids.ToArray(); return array.All(id => !string.IsNullOrWhiteSpace(id)) && array.Distinct(StringComparer.Ordinal).Count() == array.Length; }

    private static Phase8AMappingProblemIssue Error(string code, string location, string message, string? relatedId = null) => new(code, ValidationSeverity.Error, location, message, relatedId);
    private static Phase8AMappingProblemBuildResult Failure(Phase8AMappingProblemIssue issue) => new(null, [issue], []);
    private static Phase8AMappingProblemBuildResult Failure(IEnumerable<Phase8AMappingProblemIssue> issues, IEnumerable<Phase8AMappingHardConstraintRejection> rejections) => new(null, issues, rejections);
}
/// <summary>Defines stable candidate-verification issue codes.</summary>
public static class Phase8ACandidateVerificationIssueCodes
{
    /// <summary>Identifies missing, duplicate, or extra candidate coverage.</summary>
    public const string CoverageInvalid = "Phase8ACandidateCoverageInvalid";
    /// <summary>Identifies a selection outside the retained hard-feasible domain.</summary>
    public const string SelectionInvalid = "Phase8ACandidateSelectionInvalid";
    /// <summary>Identifies a candidate that violates an exact manual lock.</summary>
    public const string ManualConstraintViolated = "Phase8ACandidateManualConstraintViolated";
    /// <summary>Identifies an invalid exact endpoint-pair route selection.</summary>
    public const string RouteInvalid = "Phase8ACandidateRouteInvalid";
    /// <summary>Identifies exhaustion of global storage capacity.</summary>
    public const string CapacityExceeded = "Phase8ACandidateStorageCapacityExceeded";
    /// <summary>Identifies exhaustion of global resident storage slots.</summary>
    public const string ResidentSlotsExceeded = "Phase8ACandidateResidentSlotsExceeded";
}

/// <summary>Verifies untrusted candidates and owns deterministic global storage allocation.</summary>
public static class Phase8AMappingCandidateVerifier
{
    private sealed record Interval(long Address, long Size, string ReuseKey, bool Base);
    private sealed class Ledger
    {
        /// <summary>Initializes a mutable verification ledger from frozen selector state.</summary>
        /// <param name="state">The frozen storage selector state.</param>
        public Ledger(Phase8AStorageSelectorState state)
        {
            State = state;
            Intervals = state.BaseOccupiedIntervals.Select(item => new Interval(item.AddressBits, item.SizeBits, item.ReuseKey, true)).OrderBy(item => item.Address).ToList();
        }
        /// <summary>Gets the frozen storage selector state.</summary>
        public Phase8AStorageSelectorState State { get; }
        /// <summary>Gets the intervals accumulated during verification.</summary>
        public List<Interval> Intervals { get; }
    }

    /// <summary>Verifies an untrusted candidate and assigns deterministic global storage allocations.</summary>
    /// <param name="problem">The immutable problem authority.</param>
    /// <param name="candidate">The untrusted candidate draft.</param>
    /// <returns>The allocation decisions and structured verification issues.</returns>
    public static Phase8ACandidateVerificationResult Verify(Phase8AMappingProblem problem, Phase8AMappingCandidateDraft? candidate)
    {
        if (problem is null) throw new ArgumentNullException(nameof(problem));
        try
        {
            return VerifyCore(problem, candidate);
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return new Phase8ACandidateVerificationResult([], [Issue(Phase8ACandidateVerificationIssueCodes.SelectionInvalid, "$", "Malformed candidate was rejected structurally: " + exception.Message)]);
        }
    }

    private static Phase8ACandidateVerificationResult VerifyCore(Phase8AMappingProblem problem, Phase8AMappingCandidateDraft? candidate)
    {
        var issues = new List<Phase8ACandidateVerificationIssue>();
        var allocations = new List<Phase8AStorageAllocationDecision>();
        if (candidate is null) return Result(issues, allocations, Phase8ACandidateVerificationIssueCodes.CoverageInvalid, "$", "A candidate draft is required.");
        if (candidate.HasMalformedElements || candidate.OperationSelections.Any(item => string.IsNullOrWhiteSpace(item.OperationTileId)) ||
            candidate.CollectiveSelections.Any(item => string.IsNullOrWhiteSpace(item.CollectiveIntentId) || item.ContributorRoutes is null || item.ContributorRoutes.Any(route => route is null || string.IsNullOrWhiteSpace(route.ContributorTileId) || string.IsNullOrWhiteSpace(route.PathId))))
            return Result(issues, allocations, Phase8ACandidateVerificationIssueCodes.SelectionInvalid, "$", "Candidate selections, identities, and contributor routes must be non-null and non-empty.");

        var operationGroups = candidate.OperationSelections.GroupBy(item => item.OperationTileId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var collectiveGroups = candidate.CollectiveSelections.GroupBy(item => item.CollectiveIntentId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var group in operationGroups.Where(group => group.Value.Length != 1 || problem.Operations.All(item => item.Tile.OperationTileId != group.Key)))
            issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.CoverageInvalid, "$.operationSelections", "Operation selections must cover each exact problem tile once and contain no extras.", group.Key));
        foreach (var operation in problem.Operations.Where(item => !operationGroups.TryGetValue(item.Tile.OperationTileId, out var group) || group.Length != 1))
            issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.CoverageInvalid, "$.operationSelections", "Missing or duplicate operation selection.", operation.Tile.OperationTileId));
        foreach (var group in collectiveGroups.Where(group => group.Value.Length != 1 || problem.Collectives.All(item => item.Intent.IntentId != group.Key)))
            issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.CoverageInvalid, "$.collectiveSelections", "Collective selections must cover each exact intent once and contain no extras.", group.Key));
        foreach (var collective in problem.Collectives.Where(item => !collectiveGroups.TryGetValue(item.Intent.IntentId, out var group) || group.Length != 1))
            issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.CoverageInvalid, "$.collectiveSelections", "Missing or duplicate collective selection.", collective.Intent.IntentId));
        if (issues.Count != 0) return new Phase8ACandidateVerificationResult(allocations, issues);

        var ledgers = problem.StorageSelectors.ToDictionary(item => (item.ComponentId, item.ResourceId), item => new Ledger(item));
        var selectedOperations = new Dictionary<string, (Phase8AOperationCandidateSelection Selection, Phase8AMappingTargetOption Option)>(StringComparer.Ordinal);
        foreach (var operation in problem.Operations)
        {
            var selection = operationGroups[operation.Tile.OperationTileId][0];
            var option = operation.TargetOptions.SingleOrDefault(item =>
                item.TargetComponentId == selection.TargetComponentId && item.ActivationPortId == selection.ActivationPortId &&
                item.WeightPortId == selection.WeightPortId && item.ResultPortId == selection.ResultPortId && item.StorageResourceId == selection.StorageResourceId);
            if (option is null)
            {
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.SelectionInvalid, "$.operationSelections", "Selected target/ports/storage tuple is not an exact retained hard-feasible option.", operation.Tile.OperationTileId));
                continue;
            }
            if (!option.ActivationPathIds.Contains(selection.ActivationPathId, StringComparer.Ordinal) || !option.WeightPathIds.Contains(selection.WeightPathId, StringComparer.Ordinal))
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.RouteInvalid, "$.operationSelections", "Both operand routes must be exact retained endpoint-pair paths.", operation.Tile.OperationTileId));
            var manualTarget = problem.ManualTargetConstraints.SingleOrDefault(item => item.OperationTileId == operation.Tile.OperationTileId);
            if (manualTarget is not null && (manualTarget.TargetComponentId != selection.TargetComponentId || manualTarget.TargetPortId != selection.WeightPortId || manualTarget.StorageResourceId != selection.StorageResourceId))
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.ManualConstraintViolated, "$.operationSelections", "Operation selection violates the exact manual target lock.", operation.Tile.OperationTileId));
            foreach (var role in new[] { Phase8ATensorRoleIds.Activation, Phase8ATensorRoleIds.Weight })
            {
                var manualPath = problem.ManualOperandPathConstraints.SingleOrDefault(item => item.OperationTileId == operation.Tile.OperationTileId && item.OperandRoleId == role);
                var selectedPath = role == Phase8ATensorRoleIds.Activation ? selection.ActivationPathId : selection.WeightPathId;
                if (manualPath is not null && manualPath.PathId != selectedPath)
                    issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.ManualConstraintViolated, "$.operationSelections", "Operation selection violates an exact operand path lock.", operation.Tile.OperationTileId));
            }
            if (!ledgers.TryGetValue((option.TargetComponentId, option.StorageResourceId), out var ledger))
            {
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.SelectionInvalid, "$.operationSelections", "Selected storage selector is absent from the frozen global ledger.", operation.Tile.OperationTileId));
                continue;
            }
            var size = AlignUp(option.RequiredWeightBits, ledger.State.AllocationGranularityBits);
            var reusable = ledger.State.SupportsReuse ? ledger.Intervals.Where(item => item.ReuseKey == option.WeightReuseKey && item.Size >= size).OrderBy(item => item.Address).FirstOrDefault() : null;
            if (reusable is not null)
                allocations.Add(new Phase8AStorageAllocationDecision(operation.Tile.OperationTileId, option.TargetComponentId, option.StorageResourceId, reusable.Address, size, option.WeightReuseKey, true));
            else if (ledger.Intervals.Count >= ledger.State.ResidentSlots)
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.ResidentSlotsExceeded, "$.operationSelections", "Global selector resident slots are exhausted.", operation.Tile.OperationTileId));
            else if (!TryLowestFit(ledger.Intervals, ledger.State.CapacityBits, ledger.State.AlignmentBits, size, out var address))
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.CapacityExceeded, "$.operationSelections", "No globally non-overlapping lowest aligned interval fits the selector.", operation.Tile.OperationTileId));
            else
            {
                ledger.Intervals.Add(new Interval(address, size, option.WeightReuseKey, false));
                ledger.Intervals.Sort((left, right) => left.Address.CompareTo(right.Address));
                allocations.Add(new Phase8AStorageAllocationDecision(operation.Tile.OperationTileId, option.TargetComponentId, option.StorageResourceId, address, size, option.WeightReuseKey, false));
            }
            selectedOperations[operation.Tile.OperationTileId] = (selection, option);
        }

        var produced = new Dictionary<string, (string ComponentId, string ResultPortId)>(StringComparer.Ordinal);
        foreach (var operation in problem.Operations)
            if (selectedOperations.TryGetValue(operation.Tile.OperationTileId, out var selected)) produced[operation.Output.TileId] = (selected.Selection.TargetComponentId, selected.Selection.ResultPortId);
        foreach (var collective in problem.Collectives)
        {
            var selection = collectiveGroups[collective.Intent.IntentId][0];
            var option = collective.TargetOptions.SingleOrDefault(item => item.TargetComponentId == selection.TargetComponentId && item.InputPortId == selection.InputPortId && item.ResultPortId == selection.ResultPortId);
            if (option is null)
            {
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.SelectionInvalid, "$.collectiveSelections", "Selected collective target and semantic ports are not an exact retained option.", collective.Intent.IntentId));
                continue;
            }
            var manual = problem.ManualCollectiveConstraints.SingleOrDefault(item => item.CollectiveIntentId == collective.Intent.IntentId);
            if (manual is not null && (manual.TargetComponentId != selection.TargetComponentId || manual.InputPortId != selection.InputPortId))
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.ManualConstraintViolated, "$.collectiveSelections", "Collective selection violates its exact manual lock.", collective.Intent.IntentId));
            var routeGroups = (selection.ContributorRoutes ?? []).GroupBy(item => item.ContributorTileId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (routeGroups.Count != collective.Intent.ContributorTileIds.Count || routeGroups.Any(group => group.Value.Length != 1) || collective.Intent.ContributorTileIds.Any(id => !routeGroups.ContainsKey(id)))
            {
                issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.CoverageInvalid, "$.collectiveSelections.contributorRoutes", "Contributor routes must cover every exact contributor once with no extras.", collective.Intent.IntentId));
                continue;
            }
            var destinationHardware = HardwarePortName(problem.CapabilityAuthority, selection.TargetComponentId, selection.InputPortId);
            foreach (var contributor in collective.Intent.ContributorTileIds)
            {
                if (!produced.TryGetValue(contributor, out var producer))
                {
                    issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.RouteInvalid, "$.collectiveSelections.contributorRoutes", "Contributor is not produced by an earlier selected compute or collective result.", contributor));
                    continue;
                }
                var sourceHardware = HardwarePortName(problem.CapabilityAuthority, producer.ComponentId, producer.ResultPortId);
                var route = routeGroups[contributor][0];
                var exact = problem.RouteMatrix.Find(producer.ComponentId, sourceHardware, selection.TargetComponentId, destinationHardware)
                    .Any(item => item.PathId == route.PathId && item.Source.CapabilityPortId == producer.ResultPortId && item.Destination.CapabilityPortId == selection.InputPortId);
                if (!exact) issues.Add(Issue(Phase8ACandidateVerificationIssueCodes.RouteInvalid, "$.collectiveSelections.contributorRoutes", "Contributor route does not match the exact typed result-port to collective-input endpoint pair.", contributor));
            }
            produced[collective.Intent.ResultTileId] = (selection.TargetComponentId, selection.ResultPortId);
        }
        return new Phase8ACandidateVerificationResult(allocations, issues);
    }

    private static bool TryLowestFit(IReadOnlyList<Interval> intervals, long capacity, long alignment, long size, out long address)
    {
        if (size <= 0 || size > capacity) { address = 0; return false; }
        long cursor = 0;
        foreach (var interval in intervals.OrderBy(item => item.Address))
        {
            var candidate = AlignUp(cursor, alignment);
            if (candidate <= interval.Address && size <= interval.Address - candidate) { address = candidate; return true; }
            cursor = Math.Max(cursor, checked(interval.Address + interval.Size));
        }
        address = AlignUp(cursor, alignment);
        return address <= capacity && size <= capacity - address;
    }

    private static long AlignUp(long value, long alignment)
    {
        if (value < 0 || alignment <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + (alignment - remainder));
    }
    private static string HardwarePortName(Phase8ACapabilityAuthority authority, string componentId, string portId) => authority.PortBindings.Single(item => item.ComponentId == componentId && item.CapabilityPortId == portId).HardwarePortName;
    private static Phase8ACandidateVerificationIssue Issue(string code, string location, string message, string? relatedId = null) => new(code, location, message, relatedId);
    private static Phase8ACandidateVerificationResult Result(List<Phase8ACandidateVerificationIssue> issues, List<Phase8AStorageAllocationDecision> allocations, string code, string location, string message)
    {
        issues.Add(Issue(code, location, message));
        return new Phase8ACandidateVerificationResult(allocations, issues);
    }
}